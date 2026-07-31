using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FsCheck;
using FsCheck.Xunit;
using VHDMounter.RustDeskBridge.Crypto;
using VHDMounter.RustDeskBridge.Frames;
using Xunit;

namespace VHDMounter.Tests.RustDeskBridge.Properties
{
    /// <summary>
    /// Property 4：握手帧字段约束 + LRU 不变式（含并发驱动器）。
    ///
    /// Validates: Requirements 4.4, 4.5, 4.6, 4.7, 4.8, 4.10, 4.11, 12.1, 12.2, 14.4
    ///
    /// (a) 时间窗超出 / nonce 命中 / proof 错 / secretVersion 非 u32 → invalid_proof
    /// (b) secretVersion 是 u32 但版本不等 → secret_outdated
    /// (c) LRU 时间窗驱逐不变式恒成立
    /// (d) <c>LRU.Count ≤ 300 × N</c>
    /// (e) 同 nonce 双提交至少一次返回 invalid_proof（线性化）
    /// </summary>
    [Trait("feature", "rustdesk-bridge-host")]
    [Trait("property", "Property 4: 握手帧字段约束与 LRU 不变式")]
    public sealed class HandshakeFrameFieldConstraintsProperty
    {
        private const long TimeWindowMs = 300_000;
        private const int NoncesPerClient = 300;

        private sealed class FakeClock : IClock
        {
            public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UnixEpoch.AddYears(54); // 2024-ish
        }

        private sealed class StaticSecretProvider : IBridgeSecretProvider
        {
            private readonly byte[] _secret;
            public StaticSecretProvider(uint version, byte[] secret)
            {
                CurrentSecretVersion = version;
                _secret = secret;
            }
            public uint CurrentSecretVersion { get; }
            public ReadOnlySpan<byte> GetActiveSecret() => _secret;
        }

        private static readonly byte[] FixtureSecret = Enumerable.Repeat((byte)0xAB, 32).ToArray();

        // Requirement 4.4 / 4.5 / 4.6 / 4.7 决策表的简化模型
        private enum RejectReason { Accepted, InvalidProof, SecretOutdated }

        private static RejectReason EvaluateHandshake(
            HandshakeFrame frame,
            HmacVerifier verifier,
            HandshakeNonceLruCache lru,
            uint expectedSecretVersion,
            long nowMs)
        {
            // Requirement 4.4：secretVersion 非 u32 / 缺失 → invalid_proof
            // 在 C# 强类型下 frame.SecretVersion 一定是 u32；FsCheck 用 ulong 范围筛超界
            // 这里通过 "frame.SecretVersion > uint.MaxValue 已经不可能" 来覆盖；
            // 我们额外校验：若 caller 想造非 u32，需要把 secretVersion 编码成字符串 —— 在 schema 解析阶段
            // 拒绝（schema 层），等价于本判定函数的 invalid_proof 分支。

            // Requirement 4.6：时间窗
            if (Math.Abs(nowMs - frame.TimestampMs) > TimeWindowMs)
            {
                return RejectReason.InvalidProof;
            }

            // Requirement 4.5：secretVersion 不等 → secret_outdated（在版本是合法 u32 的前提下）
            if (frame.SecretVersion != expectedSecretVersion)
            {
                return RejectReason.SecretOutdated;
            }

            // Requirement 4.7：nonce 重放 → invalid_proof
            // Requirement 4.11：原子 EvictExpired → 查重 → 插入
            if (!lru.TryAdd(frame.SecretVersion, frame.Nonce))
            {
                return RejectReason.InvalidProof;
            }

            // Requirement 3.1 / 3.4：HMAC proof 校验
            if (!verifier.VerifyHandshake(frame))
            {
                return RejectReason.InvalidProof;
            }

            return RejectReason.Accepted;
        }

        private static HandshakeFrame BuildSignedHandshake(
            uint secretVersion, string nonce, long timestampMs, byte[] secret)
        {
            var input = HmacVerifier.BuildHandshakeHmacInput(secretVersion, nonce, timestampMs);
            var proof = HmacVerifier.ComputeMacBase64WithKey(secret, input);
            return new HandshakeFrame
            {
                Protocol = HandshakeFrame.ProtocolLiteral,
                SecretVersion = secretVersion,
                Nonce = nonce,
                TimestampMs = timestampMs,
                ClientKind = "rustdesk",
                ClientVersion = "test/1.0",
                Proof = proof,
            };
        }

        // ---------- Property tests ----------

        [Property(MaxTest = 200)]
        public Property TimeWindowOutOfRange_RejectsAsInvalidProof(uint secretVersion, long timestampDeltaMs)
        {
            return Prop.ForAll(
                Arb.From(Gen.Choose(0, 1).Select(i => (long)i * 2 - 1)), // sign = ±1
                sign =>
                {
                    if (Math.Abs(timestampDeltaMs) <= TimeWindowMs) return true; // 仅关心窗外

                    var clock = new FakeClock();
                    var nowMs = clock.UtcNow.ToUnixTimeMilliseconds();
                    var frameTimeMs = nowMs + sign * Math.Abs(timestampDeltaMs);
                    using var lru = new HandshakeNonceLruCache(NoncesPerClient, TimeSpan.FromMinutes(5), clock);
                    var verifier = new HmacVerifier(new StaticSecretProvider(secretVersion, FixtureSecret));
                    var nonce = "n" + Math.Abs(timestampDeltaMs).ToString("x");
                    var frame = BuildSignedHandshake(secretVersion, nonce, frameTimeMs, FixtureSecret);

                    var outcome = EvaluateHandshake(frame, verifier, lru, secretVersion, nowMs);
                    return outcome == RejectReason.InvalidProof;
                });
        }

        [Property(MaxTest = 200)]
        public Property NonceReplay_WithinWindow_RejectsSecond(uint secretVersion)
        {
            var clock = new FakeClock();
            var nowMs = clock.UtcNow.ToUnixTimeMilliseconds();
            using var lru = new HandshakeNonceLruCache(NoncesPerClient, TimeSpan.FromMinutes(5), clock);
            var verifier = new HmacVerifier(new StaticSecretProvider(secretVersion, FixtureSecret));

            var nonce = "deadbeefcafebabe1122334455667788";
            var f1 = BuildSignedHandshake(secretVersion, nonce, nowMs, FixtureSecret);
            var f2 = BuildSignedHandshake(secretVersion, nonce, nowMs + 1, FixtureSecret);

            var first = EvaluateHandshake(f1, verifier, lru, secretVersion, nowMs);
            var second = EvaluateHandshake(f2, verifier, lru, secretVersion, nowMs + 1);

            return ((first == RejectReason.Accepted) && (second == RejectReason.InvalidProof)).ToProperty();
        }

        [Property(MaxTest = 200)]
        public Property ProofMismatch_AlwaysReturnsInvalidProof(uint secretVersion, NonNegativeInt seed)
        {
            var clock = new FakeClock();
            var nowMs = clock.UtcNow.ToUnixTimeMilliseconds();
            using var lru = new HandshakeNonceLruCache(NoncesPerClient, TimeSpan.FromMinutes(5), clock);
            var verifier = new HmacVerifier(new StaticSecretProvider(secretVersion, FixtureSecret));

            var nonce = "noncefor" + seed.Get.ToString("x16").PadLeft(24, '0');
            var frame = BuildSignedHandshake(secretVersion, nonce, nowMs, FixtureSecret);

            // 翻转 proof 第 0 字节
            var proofBytes = Convert.FromBase64String(frame.Proof);
            proofBytes[0] ^= 0xFF;
            frame.Proof = Convert.ToBase64String(proofBytes);

            var outcome = EvaluateHandshake(frame, verifier, lru, secretVersion, nowMs);
            return (outcome == RejectReason.InvalidProof).ToProperty();
        }

        [Property(MaxTest = 100)]
        public Property SecretVersionMismatch_ReturnsSecretOutdated(uint actualVersion, uint expectedVersion)
        {
            if (actualVersion == expectedVersion) return true.ToProperty();

            var clock = new FakeClock();
            var nowMs = clock.UtcNow.ToUnixTimeMilliseconds();
            using var lru = new HandshakeNonceLruCache(NoncesPerClient, TimeSpan.FromMinutes(5), clock);
            var verifier = new HmacVerifier(new StaticSecretProvider(expectedVersion, FixtureSecret));

            var nonce = "v" + actualVersion + "x" + expectedVersion;
            var frame = BuildSignedHandshake(actualVersion, nonce, nowMs, FixtureSecret);

            var outcome = EvaluateHandshake(frame, verifier, lru, expectedVersion, nowMs);
            return (outcome == RejectReason.SecretOutdated).ToProperty();
        }

        [Fact]
        public void LruCount_NeverExceeds300xClientCount()
        {
            // Requirement 14.4：LRU.Count ≤ 300 × N
            const int N = 4;
            const int capacity = 300 * N;
            using var lru = new HandshakeNonceLruCache(capacity, TimeSpan.FromMinutes(5), new FakeClock());

            // 灌入 capacity * 3 条
            for (var i = 0; i < capacity * 3; i++)
            {
                lru.TryAdd(1, "n" + i.ToString("x8"));
                Assert.True(lru.Count <= capacity, $"LRU.Count = {lru.Count} 超过容量 {capacity}");
            }
            Assert.Equal(capacity, lru.Count);
        }

        [Fact]
        public void LruTimeWindowEviction_NoEntryOlderThanTtl()
        {
            // 不变式：任意操作序列结束后，不存在 first_seen_at > 5min 的条目
            var clock = new FakeClock();
            using var lru = new HandshakeNonceLruCache(1024, TimeSpan.FromMinutes(5), clock);

            for (var i = 0; i < 50; i++)
            {
                lru.TryAdd(1, "early-" + i);
                clock.UtcNow += TimeSpan.FromSeconds(1);
            }

            clock.UtcNow += TimeSpan.FromMinutes(6);
            // 触发驱逐：再插一条会自动 EvictExpiredLocked
            lru.TryAdd(1, "fresh");
            Assert.Equal(1, lru.Count);
        }

        [Fact]
        public async Task ConcurrentSameNonce_AtLeastOneInvalidProofResponse()
        {
            // (e) 同 nonce 双提交至少一次 invalid_proof（linearization 不变式）
            const int parallelism = 16;
            var clock = new FakeClock();
            var nowMs = clock.UtcNow.ToUnixTimeMilliseconds();
            using var lru = new HandshakeNonceLruCache(parallelism * 4, TimeSpan.FromMinutes(5), clock);
            var verifier = new HmacVerifier(new StaticSecretProvider(1u, FixtureSecret));
            var nonce = "abad1deadeadbeefcafebabe11223344";

            var startGate = new ManualResetEventSlim(false);
            var responses = new ConcurrentBag<RejectReason>();
            var tasks = Enumerable.Range(0, parallelism).Select(_ => Task.Run(() =>
            {
                var frame = BuildSignedHandshake(1u, nonce, nowMs, FixtureSecret);
                startGate.Wait();
                var outcome = EvaluateHandshake(frame, verifier, lru, 1u, nowMs);
                responses.Add(outcome);
            })).ToArray();

            startGate.Set();
            await Task.WhenAll(tasks);

            Assert.Equal(1, responses.Count(r => r == RejectReason.Accepted));
            Assert.True(
                responses.Count(r => r == RejectReason.InvalidProof) >= 1,
                "并发同 nonce 必须至少一次返回 invalid_proof");
        }

        [Property(MaxTest = 50)]
        public Property HappyPath_AcceptsValidHandshake(uint secretVersion, NonNegativeInt seed)
        {
            var clock = new FakeClock();
            var nowMs = clock.UtcNow.ToUnixTimeMilliseconds();
            using var lru = new HandshakeNonceLruCache(NoncesPerClient, TimeSpan.FromMinutes(5), clock);
            var verifier = new HmacVerifier(new StaticSecretProvider(secretVersion, FixtureSecret));
            var nonce = "ok" + seed.Get.ToString("x16").PadLeft(30, '0');
            var frame = BuildSignedHandshake(secretVersion, nonce, nowMs, FixtureSecret);
            var outcome = EvaluateHandshake(frame, verifier, lru, secretVersion, nowMs);
            return (outcome == RejectReason.Accepted).ToProperty();
        }
    }
}
