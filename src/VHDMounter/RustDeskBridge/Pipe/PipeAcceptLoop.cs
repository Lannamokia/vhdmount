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

        private readonly string _pipeName;
        private readonly SessionRunnerDelegate _sessionRunner;
        private readonly HandshakeRateLimiter _rateLimiter;
        private readonly MachineLogBuffer _logBuffer;
        private readonly IClock _clock;
        private readonly Action<string> _diagnostics;

        private CancellationTokenSource _cts;
        private Task _runner;

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

        public Task StartAsync(CancellationToken ct)
        {
            if (_runner != null)
            {
                throw new InvalidOperationException("PipeAcceptLoop 已经启动");
            }
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _runner = Task.Run(() => RunAsync(_cts.Token), CancellationToken.None);
            return Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            if (_cts == null) return;
            try { _cts.Cancel(); } catch (ObjectDisposedException) { /* already disposed */ }
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
            }
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);
        }

        // ---------- 内部 ----------

        private async Task RunAsync(CancellationToken ct)
        {
            int consecutiveOtherFailures = 0;

            while (!ct.IsCancellationRequested)
            {
                NamedPipeServerStream pipe;
                try
                {
                    pipe = BridgePipeFactory.CreatePipeInstance(_pipeName);
                    consecutiveOtherFailures = 0;
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
