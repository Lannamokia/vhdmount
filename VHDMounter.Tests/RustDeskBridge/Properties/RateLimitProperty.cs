using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FsCheck;
using FsCheck.Xunit;
using VHDMounter.RustDeskBridge.Crypto;
using VHDMounter.RustDeskBridge.Frames;
using VHDMounter.RustDeskBridge.Json;
using VHDMounter.RustDeskBridge.Policy;
using VHDMounter.RustDeskBridge.RateLimit;
using VHDMounter.RustDeskBridge.Session;
using Xunit;

namespace VHDMounter.Tests.RustDeskBridge.Properties
{
    /// <summary>
    /// Property 16: 速率与资源边界。
    ///
    /// Validates: Requirements 14.1, 14.2, 14.3, 14.4, 14.5
    ///
    /// (a) HandshakeRateLimiter：5s 窗口 ≥ 3 次失败 → 60s 冷却 → 冷却期内 IsCoolingDown == true
    /// (b) ReportRateLimiter：1 秒同三元组 ≤ 1 次（TryAcquire 第二次返回 false）
    /// (c) BridgeSession.RecordReportNonce / RecordPeerApprovalNonce 触达 4096 → overflow == true
    /// (d) SnapshotStore：> 256 KiB 拒绝替换 + snapshotOversizeCount += 1
    /// </summary>
    [Trait("feature", "rustdesk-bridge-host")]
    [Trait("property", "Property 16: 速率与资源边界")]
    public sealed class RateLimitProperty
    {
        private sealed class FakeClock : IClock
        {
            public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1730000000000L);
        }

        private sealed class FakeValidator : IPolicyPubkeyValidator
        {
            public string CurrentPubkeyDigestHex => "fixture";
            public bool VerifyResponseSignature(ReadOnlySpan<byte> payload, string signatureBase64) => true;
        }

        // ---------- (a) HandshakeRateLimiter 5s 窗口 + 60s 冷却 ----------

        [Property(MaxTest = 50)]
        public Property Handshake_ThreeFailuresWithinWindow_EntersCooldown(int seed)
        {
            var clock = new FakeClock();
            var rl = new HandshakeRateLimiter(clock,
                failureThreshold: 3,
                window: TimeSpan.FromSeconds(5),
                cooldown: TimeSpan.FromSeconds(60));

            // 在 5 秒窗口内连续累积 3 次失败
            rl.RecordFailure();
            clock.UtcNow += TimeSpan.FromMilliseconds(Math.Abs(seed) % 2000); // 0–2s
            rl.RecordFailure();
            if (rl.IsCoolingDown) return false.ToProperty(); // 第二次后还不应冷却

            clock.UtcNow += TimeSpan.FromMilliseconds(Math.Abs(seed >> 8) % 2000);
            rl.RecordFailure();
            // 三次后必入冷却
            return rl.IsCoolingDown.ToProperty();
        }

        [Property(MaxTest = 30)]
        public Property Handshake_FailuresOutsideWindow_DoNotAccumulate(byte gapSeconds)
        {
            // 让两次失败的间隔大于窗口（5s）
            var gap = (gapSeconds % 5) + 6;
            var clock = new FakeClock();
            var rl = new HandshakeRateLimiter(clock, 3, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60));

            rl.RecordFailure();
            clock.UtcNow += TimeSpan.FromSeconds(gap);
            rl.RecordFailure();
            clock.UtcNow += TimeSpan.FromSeconds(gap);
            rl.RecordFailure();
            return (!rl.IsCoolingDown).ToProperty();
        }

        [Fact]
        public void Handshake_CooldownExpiresAfter60s_ResetsState()
        {
            var clock = new FakeClock();
            var rl = new HandshakeRateLimiter(clock, 3, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60));

            rl.RecordFailure();
            rl.RecordFailure();
            rl.RecordFailure();
            Assert.True(rl.IsCoolingDown);

            // 推时钟到刚到 60s 还在冷却
            clock.UtcNow += TimeSpan.FromSeconds(59);
            Assert.True(rl.IsCoolingDown);

            // 60.5s 后跳出冷却
            clock.UtcNow += TimeSpan.FromMilliseconds(1500);
            Assert.False(rl.IsCoolingDown);
        }

        // ---------- (b) ReportRateLimiter 1 秒同三元组 ----------

        [Property(MaxTest = 50)]
        public Property Report_SameTripletWithinOneSecond_OnlyFirstAcquireSucceeds(
            NonNull<string> rid, NonNull<string> kind, NonNull<string> pwd)
        {
            var rl = new ReportRateLimiter(new FakeClock(), TimeSpan.FromSeconds(1));
            var first = rl.TryAcquire(rid.Get, kind.Get, pwd.Get);
            var second = rl.TryAcquire(rid.Get, kind.Get, pwd.Get);
            return (first && !second).ToProperty();
        }

        [Property(MaxTest = 50)]
        public Property Report_AfterOneSecond_AcquireSucceedsAgain(
            NonNull<string> rid, NonNull<string> kind, NonNull<string> pwd)
        {
            var clock = new FakeClock();
            var rl = new ReportRateLimiter(clock, TimeSpan.FromSeconds(1));
            var first = rl.TryAcquire(rid.Get, kind.Get, pwd.Get);
            clock.UtcNow += TimeSpan.FromMilliseconds(1500);
            var second = rl.TryAcquire(rid.Get, kind.Get, pwd.Get);
            return (first && second).ToProperty();
        }

        [Fact]
        public void Report_DifferentTriplets_DoNotShareWindow()
        {
            var rl = new ReportRateLimiter(new FakeClock(), TimeSpan.FromSeconds(1));
            Assert.True(rl.TryAcquire("rid1", "temporary", "P1"));
            Assert.True(rl.TryAcquire("rid1", "temporary", "P2"));
            Assert.True(rl.TryAcquire("rid1", "permanent", "P1"));
            Assert.True(rl.TryAcquire("rid2", "temporary", "P1"));
        }

        // ---------- (c) BridgeSession nonce HashSet 触达 4096 → overflow ----------

        [Fact]
        public void Session_RecordReportNonce_OverflowAt4096()
        {
            using var pipe = new System.IO.Pipes.NamedPipeServerStream(
                "VHDMount.Test." + Guid.NewGuid().ToString("N"),
                System.IO.Pipes.PipeDirection.InOut, 1);
            using var session = new BridgeSession(pipe);

            for (var i = 0; i < BridgeSession.MaxNoncePerSession - 1; i++)
            {
                Assert.True(session.RecordReportNonce("n-" + i, out var overflow));
                Assert.False(overflow, $"i={i} 不应 overflow");
            }
            // 第 4096 次：成功添加，但 overflow 标记 true
            Assert.True(session.RecordReportNonce("n-final", out var overflowOnLast));
            Assert.True(overflowOnLast, "第 4096 个 nonce 写入后应当 overflow=true 让上层关闭会话");

            // 第 4097 次再写：失败 + overflow=true
            Assert.False(session.RecordReportNonce("n-extra", out var overflowExtra));
            Assert.True(overflowExtra);
        }

        [Fact]
        public void Session_RecordPeerApprovalNonce_OverflowAt4096()
        {
            using var pipe = new System.IO.Pipes.NamedPipeServerStream(
                "VHDMount.Test." + Guid.NewGuid().ToString("N"),
                System.IO.Pipes.PipeDirection.InOut, 1);
            using var session = new BridgeSession(pipe);

            for (var i = 0; i < BridgeSession.MaxNoncePerSession - 1; i++)
            {
                Assert.True(session.RecordPeerApprovalNonce("nonce-" + i, out var overflow));
                Assert.False(overflow);
            }
            Assert.True(session.RecordPeerApprovalNonce("nonce-final", out var overflowOnLast));
            Assert.True(overflowOnLast);
            Assert.False(session.RecordPeerApprovalNonce("nonce-extra", out var overflowExtra));
            Assert.True(overflowExtra);
        }

        // ---------- (d) SnapshotStore > 256 KiB 拒绝替换 + count +1 ----------

        [Fact]
        public void Snapshot_OversizePayload_RejectedAndCounterIncrements()
        {
            using var io = new InMemoryObfuscation();
            var clock = new FakeClock();
            var store = new SnapshotStore(io, clock);
            var validator = new FakeValidator();

            Assert.Equal(0L, store.SnapshotOversizeCount);

            // 构造一个 > 256 KiB 的 raw JSON（不必合法签名 —— 大小检查在 (a) 步骤就会拒绝）
            var pad = new string('x', SnapshotStore.MaxPlainSnapshotBytes + 1024);
            var oversizeJson = "{\"machineId\":\"M\",\"snapshotSeq\":1,\"issuedAt\":1," +
                "\"entries\":[],\"signature\":\"\",\"pad\":\"" + pad + "\"}";

            Assert.False(store.TryReplace(oversizeJson, validator, out var reason));
            Assert.Equal("snapshot_oversize", reason);
            Assert.Equal(1L, store.SnapshotOversizeCount);

            // 再来一次 → +2
            Assert.False(store.TryReplace(oversizeJson, validator, out _));
            Assert.Equal(2L, store.SnapshotOversizeCount);
        }
    }
}
