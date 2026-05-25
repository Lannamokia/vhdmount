using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using VHDMounter.RustDeskBridge.Crypto;
using VHDMounter.RustDeskBridge.RateLimit;

namespace VHDMounter.RustDeskBridge.Pipe
{
    /// <summary>
    /// Requirement 1.3 / 1.4 / 1.5 / 1.6 / 1.7 / 14.2：命名管道接受循环。
    ///
    /// <list type="bullet">
    /// <item>串行接受：<see cref="BridgePipeFactory.CreatePipeInstance"/> →
    ///       <see cref="NamedPipeServerStream.WaitForConnectionAsync"/> → 调
    ///       <see cref="_sessionRunner"/></item>
    /// <item>同时刻最多 1 个 Bridge_Session（串行循环 + Factory 设的 nMaxInstances=1）</item>
    /// <item>sessionRunner 完成后 1 秒内重建（§1.5）</item>
    /// <item>管道占用（<c>ALL_PIPE_INSTANCES_BUSY = 0xE7 = 231</c>）→ 5 秒退避（§1.3）</item>
    /// <item>其它 Win32 错误 → 退避表 5/10/20s 倍增 + MachineLogBuffer 写
    ///       <c>EventKey="bridge_pipe_create_failed"</c>（§1.7）</item>
    /// <item>冷却期内（<see cref="HandshakeRateLimiter.IsCoolingDown"/>）继续接受连接，
    ///       但通过参数把"冷却中"标志透传给 sessionRunner —— sessionRunner 自身在
    ///       Wave 5 实现冷却期内回 <c>HandshakeResponse { ok: false, reason: "rate_limited" }</c>
    ///       后立即关闭管道</item>
    /// </list>
    ///
    /// 调用 sessionRunner 的协程同步等待其结束（不阻塞循环 —— sessionRunner 自己
    /// 协调 BridgeSession 生命周期与协议帧路由）。
    ///
    /// 本类实现 <see cref="IAsyncDisposable"/>，<see cref="DisposeAsync"/> 会取消主循环
    /// 并等待 sessionRunner 当前轮次结束（最多 5 秒），然后 Dispose 当前管道句柄。
    /// </summary>
    internal sealed class PipeAcceptLoop : IAsyncDisposable
    {
        public delegate Task SessionRunnerDelegate(NamedPipeServerStream stream, bool isCoolingDown, CancellationToken ct);

        public const int Win32ErrorAllPipeInstancesBusy = 231; // ERROR_PIPE_BUSY (0xE7)

        public static readonly TimeSpan PipeBusyBackoff = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan SessionRecreateGap = TimeSpan.FromSeconds(1);
        public static readonly TimeSpan FirstFailureBackoff = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan MaxFailureBackoff = TimeSpan.FromSeconds(20);

        /// <summary>
        /// <see cref="StartAsync"/> 等待首个 pipe instance 进入 namespace 的最长时间。
        ///
        /// 默认 30s 给 GHA 共享 runner 留足缓冲（cold disk + Defender 扫描 + ThreadPool
        /// 排队），但远小于 host 整体启动超时；超出则 StartAsync 抛
        /// <see cref="TimeoutException"/> 让调用方按 host 启动失败处理（不会让 host
        /// 永久卡在 await StartAsync）。生产环境通常只需几十毫秒。
        /// </summary>
        public static readonly TimeSpan StartReadyTimeout = TimeSpan.FromSeconds(30);

        private readonly string _pipeName;
        private readonly SessionRunnerDelegate _sessionRunner;
        private readonly HandshakeRateLimiter _rateLimiter;
        private readonly MachineLogBuffer _logBuffer;
        private readonly IClock _clock;
        private readonly Action<string> _diagnostics;

        private CancellationTokenSource _cts;
        private Task _runner;

        // 首次成功 CreateNamedPipeW 后置位的就绪信号。
        //
        // 没有它时 StartAsync 是 fire-and-forget：把 RunAsync 丢到 ThreadPool 就立刻
        // 返回，客户端在 `await StartAsync` 之后立即 ConnectAsync，但此时 P/Invoke
        // CreateNamedPipeW 还没把 pipe 挂到 named-pipe namespace 里，
        // NamedPipeClientStream.ConnectInternal 会一直 retry 直到 timeout（在本地
        // SSD + 空机上几十毫秒就能成功；在 GHA windows-latest 共享 VM 上可能撞到
        // ThreadPool 调度延迟 + Defender 实时扫描，五秒级 timeout 也偶尔输掉）。
        //
        // 这是 production race condition：BridgeServerHost 等任何调用方都受影响，CI
        // 只是把它暴露出来的最低成本探针。修法是让 StartAsync 等到第一个 pipe instance
        // 真的进入 namespace 后才返回——后续的重建（§1.5）由 RunAsync 自己负责，
        // 与 StartAsync 无关。
        private TaskCompletionSource<bool> _firstPipeReady;

        public PipeAcceptLoop(
            string pipeName,
            SessionRunnerDelegate sessionRunner,
            HandshakeRateLimiter rateLimiter,
            MachineLogBuffer logBuffer,
            IClock clock,
            Action<string> diagnostics = null)
        {
            if (string.IsNullOrWhiteSpace(pipeName))
            {
                throw new ArgumentException("pipeName 不能为空", nameof(pipeName));
            }
            _pipeName = pipeName;
            _sessionRunner = sessionRunner ?? throw new ArgumentNullException(nameof(sessionRunner));
            _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
            _logBuffer = logBuffer ?? throw new ArgumentNullException(nameof(logBuffer));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _diagnostics = diagnostics ?? (_ => { });
        }

        public async Task StartAsync(CancellationToken ct)
        {
            if (_runner != null)
            {
                throw new InvalidOperationException("PipeAcceptLoop 已经启动");
            }
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _firstPipeReady = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _runner = Task.Run(() => RunAsync(_cts.Token), CancellationToken.None);

            // 等到 RunAsync 第一次成功 CreatePipeInstance、且 pipe 已挂到 named-pipe
            // namespace 后再返回。这样调用方在 `await StartAsync` 后可以零 race
            // 直接 ConnectAsync。
            //
            // 错误传播策略：
            //   - 调用方 ct 触发：把异常透传出去（OperationCanceledException）
            //   - StartReadyTimeout 触发：抛 TimeoutException，由调用方 + 退到
            //     host 启动失败路径
            //   - RunAsync 在第一次 CreatePipeInstance 上反复抛异常：RunAsync 会按
            //     §1.7 退避表自我重试；如果 30s 内一直拿不到 pipe，本 StartAsync
            //     按 timeout 处理（说明环境根本起不来，不应让 host 永久卡死）
            using var startCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            startCts.CancelAfter(StartReadyTimeout);
            try
            {
                await _firstPipeReady.Task.WaitAsync(startCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // 外部取消：让 RunAsync 自己收尾，把异常向上抛
                throw;
            }
            catch (OperationCanceledException)
            {
                // StartReadyTimeout 触发，把循环也停掉
                try { _cts.Cancel(); } catch { /* ignore */ }
                throw new TimeoutException(
                    $"PipeAcceptLoop 在 {(int)StartReadyTimeout.TotalSeconds}s 内未能完成首个管道实例创建");
            }
        }

        public async Task StopAsync()
        {
            if (_cts == null) return;
            try { _cts.Cancel(); } catch (ObjectDisposedException) { /* already disposed */ }
            // 如果 StartAsync 还在 await _firstPipeReady（极端情况：调用方先 StartAsync
            // 然后立刻 StopAsync），把 TCS 标记为取消，让 StartAsync 立即解开。
            try { _firstPipeReady?.TrySetCanceled(); } catch { /* ignore */ }
            try
            {
                if (_runner != null)
                {
                    await _runner.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { /* expected */ }
            catch (Exception ex)
            {
                _diagnostics($"PipeAcceptLoop 停止异常：{ex.Message}");
            }
            finally
            {
                try { _cts.Dispose(); } catch { /* ignore */ }
                _cts = null;
                _runner = null;
                _firstPipeReady = null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);
        }

        // ---------- 内部 ----------

        private async Task RunAsync(CancellationToken ct)
        {
            try
            {
                await RunLoopAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                // 极端情况：循环退出时（例如外部 ct 在第一次 CreatePipeInstance 之前
                // 就触发取消、或者首个 CreatePipeInstance 反复失败到 ct 取消），
                // _firstPipeReady 仍未被解锁。这里兜底取消，避免 StartAsync 一直
                // 阻塞到 StartReadyTimeout 才放手。
                try { _firstPipeReady?.TrySetCanceled(); } catch { /* ignore */ }
            }
        }

        private async Task RunLoopAsync(CancellationToken ct)
        {
            int consecutiveOtherFailures = 0;

            while (!ct.IsCancellationRequested)
            {
                NamedPipeServerStream pipe;
                try
                {
                    pipe = BridgePipeFactory.CreatePipeInstance(_pipeName);
                    consecutiveOtherFailures = 0;
                    // 首个 pipe 已挂入 named-pipe namespace，解锁 StartAsync。
                    // TrySetResult 的设计是「至多一次」：后续重建（§1.5）不会再触发，
                    // 只有第一次创建成功的事件能解锁外部 await。
                    _firstPipeReady?.TrySetResult(true);
                }
                catch (BridgePipeCreateException ex)
                    when (ex.InnerException is Win32Exception w32 && w32.NativeErrorCode == Win32ErrorAllPipeInstancesBusy)
                {
                    _diagnostics($"RustDesk 桥管道被占用，{(int)PipeBusyBackoff.TotalSeconds}s 后重试");
                    LogPipeCreateFailure(w32.NativeErrorCode, ex.Message);
                    await DelayOrCancel(PipeBusyBackoff, ct).ConfigureAwait(false);
                    continue;
                }
                catch (BridgePipeCreateException ex)
                {
                    consecutiveOtherFailures++;
                    var backoff = ComputeBackoff(consecutiveOtherFailures);
                    _diagnostics(
                        $"RustDesk 桥管道创建失败（第 {consecutiveOtherFailures} 次），" +
                        $"{(int)backoff.TotalSeconds}s 后重试：{ex.Message}");
                    var win32Code = ex.InnerException is Win32Exception w ? w.NativeErrorCode : 0;
                    LogPipeCreateFailure(win32Code, ex.Message);
                    await DelayOrCancel(backoff, ct).ConfigureAwait(false);
                    continue;
                }

                try
                {
                    try
                    {
                        await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (IOException ex)
                    {
                        _diagnostics($"RustDesk 桥 ConnectNamedPipe 异常：{ex.Message}");
                        await DelayOrCancel(SessionRecreateGap, ct).ConfigureAwait(false);
                        continue;
                    }

                    if (ct.IsCancellationRequested) return;

                    var coolingDown = _rateLimiter.IsCoolingDown;
                    try
                    {
                        await _sessionRunner(pipe, coolingDown, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        _diagnostics($"RustDesk 桥 sessionRunner 抛出异常：{ex.Message}");
                    }
                }
                finally
                {
                    try { pipe.Dispose(); } catch { /* ignore */ }
                }

                // §1.5：1 秒内重建管道
                await DelayOrCancel(SessionRecreateGap, ct).ConfigureAwait(false);
            }
        }

        private static TimeSpan ComputeBackoff(int consecutiveFailures)
        {
            if (consecutiveFailures <= 1) return FirstFailureBackoff;
            if (consecutiveFailures == 2) return TimeSpan.FromSeconds(10);
            return MaxFailureBackoff;
        }

        private void LogPipeCreateFailure(int win32Code, string detail)
        {
            try
            {
                var entry = new MachineLogEntry
                {
                    SessionId = _logBuffer.CurrentSessionId,
                    OccurredAt = _clock.UtcNow.UtcDateTime.ToString(
                        "yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
                    Level = "warn",
                    Component = "rustdesk-bridge",
                    EventKey = "bridge_pipe_create_failed",
                    Message = $"win32={win32Code.ToString(CultureInfo.InvariantCulture)}",
                    RawText = $"win32={win32Code.ToString(CultureInfo.InvariantCulture)} detail={Truncate(detail, 200)}",
                    Metadata = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                };
                _logBuffer.EnqueueRustDeskBridgeEntry(entry);
            }
            catch
            {
                // 日志写失败不阻塞重试循环
            }
        }

        private static async Task DelayOrCancel(TimeSpan delay, CancellationToken ct)
        {
            try
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // 退出循环由调用方判断
            }
        }

        private static string Truncate(string s, int maxLength)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= maxLength) return s ?? string.Empty;
            return s.Substring(0, maxLength);
        }
    }
}
