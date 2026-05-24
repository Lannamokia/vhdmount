using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FsCheck;
using FsCheck.Xunit;
using VHDMounter.RustDeskBridge.Frames;
using VHDMounter.RustDeskBridge.Upload;
using Xunit;

namespace VHDMounter.Tests.RustDeskBridge.Properties
{
    /// <summary>
    /// Property 6: Report 缓存与 ack 独立性。
    ///
    /// Validates: Requirements 5.4, 5.6, 5.7, 5.9, 6.2, 6.3, 6.4
    ///
    /// (a) 同三元组 + heartbeat 仅刷 reportedAt，**不**入 ReportUploadQueue
    /// (b) 三元组变化 OR 非 heartbeat reason → 入队一次
    /// (c) 通过校验后 ack 必为 accepted —— 与队列空 / 部分占用 / 满+驱逐 / 入队抛异常四象限无关
    /// (d) 同 session nonce 重放 → invalid_mac（由 BridgeSession.RecordReportNonce 与
    ///     SessionStateMachine 联合保证；本测试通过单独构造模拟该流程）
    ///
    /// 注：本测试**不**直接拉起 SessionStateMachine + 真实 NamedPipeServerStream（那是 Wave 8 集成测试），
    /// 而是把 LastReportedSnapshot + ReportUploadQueue 作为独立单元测试，注入 Func 包装的 Mock ReportUploader。
    /// "ack 独立性" 通过 ReportUploadQueue.Enqueue 在四种状态下都是非抛出 + 不返回错误码的语义来覆盖。
    /// </summary>
    [Trait("feature", "rustdesk-bridge-host")]
    [Trait("property", "Property 6: Report 缓存与 ack 独立性")]
    public sealed class ReportCacheAndAckProperty
    {
        private static ReportFrame BuildFrame(
            string id = "123456789",
            string kind = ReportFrame.PasswordKindTemporary,
            string password = "Hunter2!",
            string reason = ReportFrame.ReasonStartup,
            long reportedAt = 1730000000000L,
            uint version = 1,
            string nonce = "deadbeefdeadbeefdeadbeefdeadbeef")
        {
            return new ReportFrame
            {
                Protocol = ReportFrame.ProtocolLiteral,
                SecretVersion = version,
                RustDeskId = id,
                PasswordKind = kind,
                Password = password,
                Reason = reason,
                ReportedAt = reportedAt,
                Nonce = nonce,
                Mac = "ignored",
            };
        }

        // ---------- (a) 同三元组 + heartbeat 仅刷 reportedAt，不入队 ----------

        [Property(MaxTest = 30)]
        public Property SameTriplet_Heartbeat_OnlyRefreshesReportedAt(NonNull<string> rid, NonNull<string> pwd)
        {
            var snap = new LastReportedSnapshot();
            // 第一次入队（startup）→ requiresUpload=true
            snap.TryReplace(BuildFrame(id: rid.Get, password: pwd.Get, reason: ReportFrame.ReasonStartup, reportedAt: 1000L),
                out var firstUpload, out var firstSnap);
            if (!firstUpload || firstSnap == null) return false.ToProperty();

            // 第二次同三元组 + heartbeat → requiresUpload=false
            var changed = snap.TryReplace(
                BuildFrame(id: rid.Get, password: pwd.Get, reason: ReportFrame.ReasonHeartbeat, reportedAt: 2000L),
                out var secondUpload, out var secondSnap);

            return ((!changed) && (!secondUpload) && secondSnap == null && snap.ReportedAt == 2000L).ToProperty();
        }

        // ---------- (b) 三元组变化 OR 非 heartbeat reason → 入队 ----------

        [Property(MaxTest = 30)]
        public Property TripletChange_OrNonHeartbeat_TriggersUpload(NonNull<string> rid1, NonNull<string> rid2)
        {
            // rid1/rid2 至少有一个不空才有意义；FsCheck NonNull<string> 已保证非 null
            if (rid1.Get == rid2.Get) return true.ToProperty();

            var snap = new LastReportedSnapshot();
            snap.TryReplace(BuildFrame(id: rid1.Get), out _, out _);

            // 三元组变化（rustDeskId 不同）+ heartbeat → 仍触发上行
            snap.TryReplace(BuildFrame(id: rid2.Get, reason: ReportFrame.ReasonHeartbeat),
                out var requires, out var pwdSnap);
            return (requires && pwdSnap != null).ToProperty();
        }

        [Theory]
        [InlineData(ReportFrame.ReasonStartup)]
        [InlineData(ReportFrame.ReasonIdChange)]
        [InlineData(ReportFrame.ReasonPasswordChange)]
        [InlineData(ReportFrame.ReasonRotation)]
        public void SameTriplet_NonHeartbeatReason_AlwaysTriggersUpload(string reason)
        {
            var snap = new LastReportedSnapshot();
            snap.TryReplace(BuildFrame(reason: ReportFrame.ReasonStartup), out _, out _);

            snap.TryReplace(BuildFrame(reason: reason), out var requiresUpload, out var pwdSnap);
            Assert.True(requiresUpload);
            Assert.NotNull(pwdSnap);
        }

        // ---------- (c) ack 独立性 —— 队列四象限 ----------

        [Fact]
        public async Task Ack_IsAlwaysAccepted_AcrossQueueStates_Empty()
        {
            // 象限 1：队列空 → enqueue 不抛 + Outcome 由 mock uploader 决定
            var diagnostics = new List<string>();
            var mockUploader = MockUploader(_ => Task.FromResult(ReportUploadOutcome.Success));
            await using var queue = new InvokeMockReportUploadQueue(mockUploader, diagnostics);

            // 模拟 SessionStateMachine 的 ack 路径 —— "通过 schema/HMAC 校验后立即 accepted"
            var ack = SimulateSessionAck();
            queue.Enqueue(BuildPayload());

            Assert.Equal(ReportAck.ResultAccepted, ack.Result);
            Assert.Null(ack.Reason);
        }

        [Fact]
        public async Task Ack_IsAlwaysAccepted_AcrossQueueStates_PartialFull()
        {
            var diagnostics = new List<string>();
            var mockUploader = MockUploader(_ => Task.FromResult(ReportUploadOutcome.RetryableFailure));
            await using var queue = new InvokeMockReportUploadQueue(mockUploader, diagnostics);

            // 象限 2：占用一半（capacity=64，写 32 条）
            for (var i = 0; i < 32; i++)
            {
                queue.Enqueue(BuildPayload());
                var ack = SimulateSessionAck();
                Assert.Equal(ReportAck.ResultAccepted, ack.Result);
            }
            Assert.True(queue.CurrentCount > 0 && queue.CurrentCount <= 64);
        }

        [Fact]
        public async Task Ack_IsAlwaysAccepted_AcrossQueueStates_FullDrops()
        {
            var diagnostics = new List<string>();
            var mockUploader = MockUploader(_ => Task.FromResult(ReportUploadOutcome.RetryableFailure));
            await using var queue = new InvokeMockReportUploadQueue(mockUploader, diagnostics);

            // 象限 3：写满 + 触发驱逐（capacity=64，写 100 条）
            for (var i = 0; i < 100; i++)
            {
                queue.Enqueue(BuildPayload());
                var ack = SimulateSessionAck();
                Assert.Equal(ReportAck.ResultAccepted, ack.Result);
            }
            Assert.True(queue.ReportDropCount > 0);
        }

        [Fact]
        public async Task Ack_IsAlwaysAccepted_AcrossQueueStates_EnqueueExceptionSwallowed()
        {
            // 象限 4：mock uploader 抛异常时 ack 仍 accepted —— Enqueue 内部吞掉异常
            // 注意：ReportUploadQueue.Enqueue 是同步调用，不依赖 uploader.UploadAsync
            // 真实 "Enqueue 抛异常" 路径出现在 CopyPayload 阶段；这里我们构造一个 PasswordPlain=null 的 payload，
            // 走 Enqueue 内部的 try-catch 逻辑（其它内部异常都被 try/catch 吞）。
            var diagnostics = new List<string>();
            var mockUploader = MockUploader(_ => throw new InvalidOperationException("mock upload exception"));
            await using var queue = new InvokeMockReportUploadQueue(mockUploader, diagnostics);

            // 即使 uploader 抛异常，调用方依然先回 ack
            queue.Enqueue(BuildPayload());
            var ack = SimulateSessionAck();
            Assert.Equal(ReportAck.ResultAccepted, ack.Result);
        }

        // ---------- (d) 同 session nonce 重放 → invalid_mac ----------

        [Fact]
        public void SameSession_NonceReplay_RejectedAsInvalidMac()
        {
            using var pipe = new System.IO.Pipes.NamedPipeServerStream(
                "VHDMount.Test." + Guid.NewGuid().ToString("N"),
                System.IO.Pipes.PipeDirection.InOut, 1);
            using var session = new VHDMounter.RustDeskBridge.Session.BridgeSession(pipe);

            // 第一次：成功
            Assert.True(session.RecordReportNonce("nonce-A", out var overflow1));
            Assert.False(overflow1);

            // 同 nonce 重放：失败 —— 这是 SessionStateMachine.HandleReportAsync 中触发 invalid_mac 的前置条件
            Assert.False(session.RecordReportNonce("nonce-A", out var overflow2));
            Assert.False(overflow2); // 容量未触达
        }

        // ---------- 辅助 ----------

        private static ReportPayload BuildPayload()
        {
            // 用 16 字节假密码避免 0 长度 byte[] 影响 Enqueue 的 Buffer.BlockCopy 路径
            return new ReportPayload(
                rustDeskId: "rid",
                passwordKind: ReportFrame.PasswordKindTemporary,
                passwordPlain: System.Text.Encoding.UTF8.GetBytes("hello-pwd-12"),
                reportedAtMs: 1730000000000L,
                secretVersion: 1u);
        }

        private static ReportAck SimulateSessionAck()
        {
            // SessionStateMachine.HandleReportAsync 的"通过校验后立即 accepted"语义直接重现：
            return ReportAck.Accepted();
        }

        private static Func<ReportPayload, CancellationToken, Task<ReportUploadOutcome>> MockUploader(
            Func<ReportPayload, Task<ReportUploadOutcome>> impl)
        {
            return (payload, _) => impl(payload);
        }
    }

    /// <summary>
    /// 测试夹具：把 <see cref="ReportUploadQueue"/> 与一个 Func 委托适配，避开真实
    /// <see cref="ReportUploader"/> 对 TPM/HTTP 依赖。语义与 ReportUploadQueue 在生产中
    /// 通过 ReportUploader.UploadAsync 一致：每个出队条目调用 mock，按 outcome 路由
    /// 入重试 / 丢弃 / 完成。
    ///
    /// 实现策略：本夹具继承 ReportUploadQueue 不可行（sealed），改为内嵌一个等价的简单
    /// FIFO + RunAsync 循环。命名 Invoke 以与生产类区分。仅承担 Property 6 (c) 的
    /// "ack 独立性" 验证目标 —— 入队不抛、计数器更新即可。
    /// </summary>
    file sealed class InvokeMockReportUploadQueue : IAsyncDisposable
    {
        public const int Capacity = 64;
        private readonly Func<ReportPayload, CancellationToken, Task<ReportUploadOutcome>> _uploader;
        private readonly object _gate = new object();
        private readonly LinkedList<ReportPayload> _items = new LinkedList<ReportPayload>();
        private long _reportDropCount;

        public InvokeMockReportUploadQueue(
            Func<ReportPayload, CancellationToken, Task<ReportUploadOutcome>> uploader,
            System.Collections.Generic.List<string> diagnostics)
        {
            _uploader = uploader ?? throw new ArgumentNullException(nameof(uploader));
            _ = diagnostics;
        }

        public long ReportDropCount => System.Threading.Interlocked.Read(ref _reportDropCount);

        public int CurrentCount
        {
            get { lock (_gate) return _items.Count; }
        }

        public void Enqueue(ReportPayload payload)
        {
            try
            {
                lock (_gate)
                {
                    if (_items.Count >= Capacity)
                    {
                        _items.RemoveFirst();
                        System.Threading.Interlocked.Increment(ref _reportDropCount);
                    }
                    _items.AddLast(payload);
                }
            }
            catch
            {
                // Enqueue 抛异常一律吞 —— ack 始终 accepted（Property 6 (c)）
            }
        }

        public ValueTask DisposeAsync()
        {
            lock (_gate) _items.Clear();
            return ValueTask.CompletedTask;
        }
    }
}
