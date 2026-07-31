using System;
using VHDMounter.RustDeskBridge.Crypto;
using VHDMounter.RustDeskBridge.RateLimit;
using Xunit;

namespace VHDMounter.Tests.RustDeskBridge.Properties
{
    [Trait("feature", "rustdesk-bridge-host")]
    [Trait("property", "Property 16: 速率与资源边界")]
    public sealed class RateLimiterTests
    {
        private sealed class FakeClock : IClock
        {
            public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UnixEpoch;
        }

        [Fact]
        public void HandshakeRateLimiter_ThreeFailuresInWindow_EntersCooldown()
        {
            var clock = new FakeClock();
            var rl = new HandshakeRateLimiter(clock,
                failureThreshold: 3,
                window: TimeSpan.FromSeconds(5),
                cooldown: TimeSpan.FromSeconds(60));

            Assert.False(rl.IsCoolingDown);
            rl.RecordFailure();
            rl.RecordFailure();
            Assert.False(rl.IsCoolingDown);
            rl.RecordFailure();
            Assert.True(rl.IsCoolingDown);
        }

        [Fact]
        public void HandshakeRateLimiter_FailuresOutsideWindow_DoNotAccumulate()
        {
            var clock = new FakeClock();
            var rl = new HandshakeRateLimiter(clock, 3, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60));

            rl.RecordFailure();
            clock.UtcNow += TimeSpan.FromSeconds(6);
            rl.RecordFailure();
            clock.UtcNow += TimeSpan.FromSeconds(6);
            rl.RecordFailure();
            Assert.False(rl.IsCoolingDown);
        }

        [Fact]
        public void HandshakeRateLimiter_CooldownExpires_ResetsState()
        {
            var clock = new FakeClock();
            var rl = new HandshakeRateLimiter(clock, 3, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60));
            rl.RecordFailure();
            rl.RecordFailure();
            rl.RecordFailure();
            Assert.True(rl.IsCoolingDown);

            clock.UtcNow += TimeSpan.FromSeconds(61);
            Assert.False(rl.IsCoolingDown);
        }

        [Fact]
        public void HandshakeRateLimiter_DoesNotExtendCooldownOnAdditionalFailures()
        {
            var clock = new FakeClock();
            var rl = new HandshakeRateLimiter(clock, 3, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60));
            rl.RecordFailure();
            rl.RecordFailure();
            rl.RecordFailure();
            // 已进入冷却
            clock.UtcNow += TimeSpan.FromSeconds(30);
            rl.RecordFailure();
            // 仍按原冷却到期点计算
            clock.UtcNow += TimeSpan.FromSeconds(31);
            Assert.False(rl.IsCoolingDown);
        }

        [Fact]
        public void ReportRateLimiter_FirstAcquireWins_SecondInWindowFails()
        {
            var clock = new FakeClock();
            var rl = new ReportRateLimiter(clock, TimeSpan.FromSeconds(1));

            Assert.True(rl.TryAcquire("rid", "temporary", "Hunter2!"));
            Assert.False(rl.TryAcquire("rid", "temporary", "Hunter2!"));
        }

        [Fact]
        public void ReportRateLimiter_AfterWindow_AcquireSucceedsAgain()
        {
            var clock = new FakeClock();
            var rl = new ReportRateLimiter(clock, TimeSpan.FromSeconds(1));

            Assert.True(rl.TryAcquire("rid", "temporary", "Hunter2!"));
            clock.UtcNow += TimeSpan.FromSeconds(2);
            Assert.True(rl.TryAcquire("rid", "temporary", "Hunter2!"));
        }

        [Fact]
        public void ReportRateLimiter_DifferentTriplet_DoNotShareWindow()
        {
            var rl = new ReportRateLimiter(new FakeClock(), TimeSpan.FromSeconds(1));
            Assert.True(rl.TryAcquire("rid", "temporary", "P1"));
            Assert.True(rl.TryAcquire("rid", "temporary", "P2"));
            Assert.True(rl.TryAcquire("rid", "permanent", "P1"));
            Assert.True(rl.TryAcquire("rid2", "temporary", "P1"));
        }
    }
}
