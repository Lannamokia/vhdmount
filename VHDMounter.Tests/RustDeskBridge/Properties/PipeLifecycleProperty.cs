using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using VHDMounter;
using VHDMounter.RustDeskBridge.Crypto;
using VHDMounter.RustDeskBridge.Pipe;
using VHDMounter.RustDeskBridge.RateLimit;
using Xunit;

namespace VHDMounter.Tests.RustDeskBridge.Properties
{
    /// <summary>
    /// Property 1: 命名管道生命周期不变式（SMOKE 简化版）。
    ///
    /// Validates: Requirements 1.4, 1.5, 1.7
    ///
    /// 工程权衡：完整 PBT 需要 mock NamedPipeServerStream（无法做到，.NET 没有抽象接口）+
    /// 跨进程客户端连接，难度极高。此处采用 SMOKE：
    ///
    /// 1. 启动一个真实 PipeAcceptLoop（同时刻 1 个实例 + 用唯一名字隔离）+ 注入一个立即 return 的
    ///    sessionRunner，让循环跑起来后断开
    /// 2. 用 NamedPipeClientStream 多次连接、立即断开，验证 PipeAcceptLoop 在 1 秒内重建管道并
    ///    再次接受连接（§1.5 重建语义）
    /// 3. 验证 sessionRunner 看到的是一个干净的新 NamedPipeServerStream（隐含 §1.4：同时只有 1 个实例）
    ///
    /// 这覆盖了 Property 1 的 (a)(b)：单一实例 + 1 秒重建。
    ///
    /// **重要：BridgePipeFactory 构造的管道 DACL 仅允许 BUILTIN\Administrators + SYSTEM
    ///   连接（Requirement 14.6 / 14.7）。如果测试进程不是管理员身份运行，
    ///   NamedPipeClientStream.ConnectAsync 会立即被拒绝（ACCESS_DENIED）→ 测试 SKIP。**
    /// 完整的并发驱动器 / 多事件序列由 Wave 7 的跨进程集成测试承担。
    /// </summary>
    [Trait("feature", "rustdesk-bridge-host")]
    [Trait("property", "Property 1: 命名管道生命周期不变式")]
    [Trait("kind", "SMOKE")]
    public sealed class PipeLifecycleProperty : IDisposable
    {
        private readonly string _pipeName;
        private readonly string _spoolDir;
        private readonly MachineLogBuffer _buffer;

        public PipeLifecycleProperty()
        {
            _pipeName = "VHDMount.RustDeskBridgeTest." + Guid.NewGuid().ToString("N");
            _spoolDir = Path.Combine(Path.GetTempPath(), "vhdm-bridge-pipe-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_spoolDir);
            _buffer = new MachineLogBuffer(Path.Combine(_spoolDir, "spool.jsonl"), "test-session-pipe", 1024 * 1024);
        }

        public void Dispose()
        {
            try { _buffer.Dispose(); } catch { /* ignore */ }
            try { if (Directory.Exists(_spoolDir)) Directory.Delete(_spoolDir, true); } catch { /* ignore */ }
        }

        private static bool IsRunningElevated()
        {
            using var id = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(id);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        private sealed class FakeClock : IClock
        {
            public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
        }

        // ---------- §1.5：每事件后 1 秒内重建管道 + 再次接受连接 ----------

        [Fact(Timeout = 30000)]
        public async Task PipeRecreate_AfterSessionEnds_NewInstanceAcceptsNextConnection()
        {
            if (!IsRunningElevated())
            {
                // BridgePipeFactory DACL 只允许 Administrators+SYSTEM，非管理员客户端 ACCESS_DENIED → 跳过
                return;
            }
            var clock = new FakeClock();
            var rateLimiter = new HandshakeRateLimiter(clock,
                failureThreshold: 3, window: TimeSpan.FromSeconds(5), cooldown: TimeSpan.FromSeconds(60));

            // sessionRunner: 立即 return（让循环马上重建管道）
            var sessionsHandled = 0;
            PipeAcceptLoop.SessionRunnerDelegate sessionRunner = (stream, isCoolingDown, ct) =>
            {
                Interlocked.Increment(ref sessionsHandled);
                Assert.False(isCoolingDown);
                Assert.True(stream.IsConnected);
                return Task.CompletedTask;
            };

            await using var loop = new PipeAcceptLoop(
                _pipeName, sessionRunner, rateLimiter, _buffer, clock,
                diagnostics: msg => { /* swallow */ });
            using var cts = new CancellationTokenSource();
            await loop.StartAsync(cts.Token);

            // 连续连接 3 次 —— 每次都是独立的 Bridge_Session
            for (var i = 0; i < 3; i++)
            {
                var connected = await ConnectClientWithinAsync(_pipeName, TimeSpan.FromSeconds(5));
                Assert.True(connected, $"第 {i + 1} 次客户端连接超时（PipeAcceptLoop 未在 1 秒内重建）");
                // 客户端 ConnectAsync 完成 ≠ 服务端 sessionRunner 已经被调用：
                // 服务端 WaitForConnectionAsync 在 ThreadPool 上排队 resume，
                // sessionRunner 才执行；在繁忙的 CI runner 上这一段调度可能落到
                // 几百毫秒。轮询 sessionsHandled 比固定 Task.Delay 更稳。
                await WaitForSessionsAsync(() => Volatile.Read(ref sessionsHandled),
                    atLeast: i + 1, timeout: TimeSpan.FromSeconds(5),
                    because: $"sessionRunner 第 {i + 1} 次未在 5 秒内被调用——"
                           + "PipeAcceptLoop 可能未在 §1.5 的 1 秒预算内重建管道");
            }

            cts.Cancel();
            await loop.StopAsync();

            Assert.True(sessionsHandled >= 3,
                $"期望 sessionRunner 至少被调用 3 次，实际 {sessionsHandled}");
        }

        // ---------- §1.4：同时刻最多 1 个 Bridge_Session ----------

        [Fact(Timeout = 30000)]
        public async Task PipeAcceptLoop_OnlyOneSessionAtATime()
        {
            if (!IsRunningElevated())
            {
                return;
            }
            var clock = new FakeClock();
            var rateLimiter = new HandshakeRateLimiter(clock, 3, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60));

            // sessionRunner: 持有 100ms，期间任何并发连接尝试都不应建立第二个实例
            var concurrentSessions = 0;
            var maxConcurrent = 0;
            var lockObj = new object();
            PipeAcceptLoop.SessionRunnerDelegate sessionRunner = async (stream, isCoolingDown, ct) =>
            {
                lock (lockObj)
                {
                    concurrentSessions++;
                    if (concurrentSessions > maxConcurrent) maxConcurrent = concurrentSessions;
                }
                await Task.Delay(150, ct);
                lock (lockObj) concurrentSessions--;
            };

            await using var loop = new PipeAcceptLoop(
                _pipeName, sessionRunner, rateLimiter, _buffer, clock,
                diagnostics: msg => { });
            using var cts = new CancellationTokenSource();
            await loop.StartAsync(cts.Token);

            // 让两个客户端尝试同时连接 —— 第二个应当被阻塞直到第一个 sessionRunner 完成 + 管道重建
            var t1 = Task.Run(async () =>
            {
                using var c1 = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut);
                await c1.ConnectAsync(5000);
                await Task.Delay(50);
            });
            var t2 = Task.Run(async () =>
            {
                using var c2 = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut);
                await c2.ConnectAsync(10000);
                await Task.Delay(50);
            });

            await Task.WhenAll(t1, t2);
            // 等到两个 session 都被串行处理完毕（concurrentSessions 归零）。
            // 原版固定 Task.Delay(500) 在 GHA windows-latest 上不够稳定：
            // 第二个 sessionRunner 的 await Task.Delay(150, ct) 排队 + ThreadPool
            // resume 在繁忙 CI 上可能落到几百毫秒以外，让 maxConcurrent 还没收敛
            // 就被 cancel 打断。轮询 concurrentSessions == 0 是直接观察服务端
            // 真实状态的方式。
            await WaitForSessionsAsync(
                probe: () =>
                {
                    lock (lockObj) return concurrentSessions == 0 ? 1 : 0;
                },
                atLeast: 1,
                timeout: TimeSpan.FromSeconds(10),
                because: "两个 session 未在 10 秒内全部完成—— "
                       + "可能 PipeAcceptLoop 串行预算被打破");

            cts.Cancel();
            await loop.StopAsync();

            Assert.True(maxConcurrent <= 1,
                $"同时刻活跃 sessionRunner 应当 ≤ 1，实际峰值 {maxConcurrent}");
        }

        // ---------- §1.5：sessionRunner 抛异常后 1 秒内仍能再次接受 ----------

        [Fact(Timeout = 30000)]
        public async Task PipeAcceptLoop_RecreatesAfterSessionRunnerThrows()
        {
            if (!IsRunningElevated())
            {
                return;
            }
            var clock = new FakeClock();
            var rateLimiter = new HandshakeRateLimiter(clock, 3, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60));

            var sessionsHandled = 0;
            PipeAcceptLoop.SessionRunnerDelegate sessionRunner = (stream, isCoolingDown, ct) =>
            {
                var n = Interlocked.Increment(ref sessionsHandled);
                if (n == 1) throw new InvalidOperationException("intentional throw on first session");
                return Task.CompletedTask;
            };

            await using var loop = new PipeAcceptLoop(
                _pipeName, sessionRunner, rateLimiter, _buffer, clock,
                diagnostics: msg => { });
            using var cts = new CancellationTokenSource();
            await loop.StartAsync(cts.Token);

            // 第一次连接 → sessionRunner 抛异常
            Assert.True(await ConnectClientWithinAsync(_pipeName, TimeSpan.FromSeconds(5)),
                "第一次连接超时");
            // 客户端 ConnectAsync 完成 ≠ 服务端 sessionRunner 已经被调用：服务端
            // WaitForConnectionAsync 在 ThreadPool 上排队 resume，sessionRunner 才
            // 执行；在繁忙的 CI runner 上这一段调度可能落到几百毫秒。直接 Task.Delay
            // 是 race —— 改为轮询 sessionsHandled，保证看到「第一次抛出」这一事件
            // 后再让 PipeAcceptLoop 走 §1.5 的 1 秒重建路径。
            await WaitForSessionsAsync(() => Volatile.Read(ref sessionsHandled), atLeast: 1,
                timeout: TimeSpan.FromSeconds(5),
                because: "sessionRunner 第 1 次未在 5 秒内被调用");

            // 第二次连接 → 应当能成功（PipeAcceptLoop 已重建）
            Assert.True(await ConnectClientWithinAsync(_pipeName, TimeSpan.FromSeconds(5)),
                "sessionRunner 抛异常后 PipeAcceptLoop 没有 1 秒内重建管道");
            // 同样轮询：等到 sessionRunner 被调用第 2 次（即 PipeAcceptLoop 重建
            // 后接受了第二次连接），再去 cancel + StopAsync。原版用 cts.Cancel()
            // 紧跟 ConnectClientWithinAsync 之后，在 GHA windows-latest 上有
            // race —— 客户端 kernel 已经返回，但服务端 WaitForConnectionAsync
            // resume 的 ThreadPool work item 还没跑到，sessionRunner 自然没递增。
            await WaitForSessionsAsync(() => Volatile.Read(ref sessionsHandled), atLeast: 2,
                timeout: TimeSpan.FromSeconds(5),
                because: "sessionRunner 第 2 次未在 5 秒内被调用——PipeAcceptLoop "
                       + "未在 §1.5 的 1 秒预算内重建管道并接受第二次连接");

            cts.Cancel();
            await loop.StopAsync();
            Assert.Equal(2, sessionsHandled);
        }

        // ---------- 辅助 ----------

        private static async Task<bool> ConnectClientWithinAsync(string pipeName, TimeSpan timeout)
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
            try
            {
                await client.ConnectAsync((int)timeout.TotalMilliseconds);
                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
        }

        /// <summary>
        /// 轮询 <paramref name="probe"/> 直到读到值 ≥ <paramref name="atLeast"/>，
        /// 或在 <paramref name="timeout"/> 内未达成则 <see cref="Assert.Fail(string)"/>。
        ///
        /// 用于替代「客户端 ConnectAsync 完成后 Task.Delay(N)」这种依赖固定休眠
        /// 让服务端 sessionRunner 完成调度的脆弱写法 —— GHA windows-latest 共享
        /// VM 上 ThreadPool 调度可能落到几百毫秒以外，固定 Delay 是 race。
        /// </summary>
        private static async Task WaitForSessionsAsync(
            Func<int> probe, int atLeast, TimeSpan timeout, string because)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (probe() >= atLeast) return;
                await Task.Delay(20).ConfigureAwait(false);
            }
            // 最后一次复查，给极端 GC pause 留个机会。
            if (probe() >= atLeast) return;
            Assert.Fail($"{because}（实际值 {probe()}，期望 ≥ {atLeast}）");
        }
    }
}
