using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VHDMounter.RustDeskBridge.Crypto;
using Xunit;

namespace VHDMounter.Tests.RustDeskBridge.Properties
{
    /// <summary>
    /// 任务 5.1 落地的 LRU 单元测试。覆盖：
    ///  - 容量驱逐：超容时淘汰最早条目
    ///  - 时间维度驱逐：TTL 内重复 nonce 拒绝、TTL 后重复 nonce 接受
    ///  - 同 nonce 不同 secretVersion 视为独立 key
    ///  - 多线程并发同 nonce 提交：linearizability 不变式（恰一次接受）
    /// </summary>
    [Trait("feature", "rustdesk-bridge-host")]
    [Trait("property", "Property 4: 握手帧字段约束与 LRU 不变式")]
    public sealed class HandshakeNonceLruCacheTests
    {
        private sealed class FakeClock : IClock
        {
            public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UnixEpoch;
        }

        [Fact]
        public void TryAdd_FirstTime_ReturnsTrue()
        {
            using var lru = new HandshakeNonceLruCache(10, TimeSpan.FromMinutes(5), new FakeClock());
            Assert.True(lru.TryAdd(1, "abcd"));
        }

        [Fact]
        public void TryAdd_DuplicateWithinTtl_ReturnsFalse()
        {
            var clock = new FakeClock();
            using var lru = new HandshakeNonceLruCache(10, TimeSpan.FromMinutes(5), clock);
            Assert.True(lru.TryAdd(1, "abcd"));
            clock.UtcNow += TimeSpan.FromSeconds(30);
            Assert.False(lru.TryAdd(1, "abcd"));
        }

        [Fact]
        public void TryAdd_DuplicateAfterTtl_ReturnsTrue()
        {
            var clock = new FakeClock();
            using var lru = new HandshakeNonceLruCache(10, TimeSpan.FromMinutes(5), clock);
            Assert.True(lru.TryAdd(1, "abcd"));
            clock.UtcNow += TimeSpan.FromMinutes(6);
            Assert.True(lru.TryAdd(1, "abcd"));
        }

        [Fact]
        public void Capacity_EvictsOldest()
        {
            var clock = new FakeClock();
            using var lru = new HandshakeNonceLruCache(3, TimeSpan.FromHours(1), clock);
            for (var i = 0; i < 3; i++)
            {
                Assert.True(lru.TryAdd(1, "n" + i));
                clock.UtcNow += TimeSpan.FromSeconds(1);
            }
            Assert.Equal(3, lru.Count);
            // 加第 4 个 → 最旧的 n0 被驱逐
            Assert.True(lru.TryAdd(1, "n3"));
            Assert.Equal(3, lru.Count);
            // n0 已被驱逐，可以再次添加
            Assert.True(lru.TryAdd(1, "n0"));
        }

        [Fact]
        public void DifferentSecretVersion_TreatedAsIndependentKey()
        {
            using var lru = new HandshakeNonceLruCache(10, TimeSpan.FromMinutes(5), new FakeClock());
            Assert.True(lru.TryAdd(1, "abcd"));
            Assert.True(lru.TryAdd(2, "abcd"));
            Assert.False(lru.TryAdd(1, "abcd"));
            Assert.False(lru.TryAdd(2, "abcd"));
        }

        [Fact]
        public async Task TryAdd_Concurrent_SameNonce_OnlyOneWins()
        {
            using var lru = new HandshakeNonceLruCache(1024, TimeSpan.FromMinutes(5), new FakeClock());
            const int parallelism = 32;
            var nonce = "deadbeefcafebabe1234567890abcdef";

            var startGate = new ManualResetEventSlim(false);
            var tasks = Enumerable.Range(0, parallelism)
                .Select(_ => Task.Run(() =>
                {
                    startGate.Wait();
                    return lru.TryAdd(1, nonce);
                }))
                .ToArray();

            startGate.Set();
            var results = await Task.WhenAll(tasks);

            Assert.Equal(1, results.Count(r => r));
            Assert.Equal(parallelism - 1, results.Count(r => !r));
        }

        [Fact]
        public void EvictExpired_RemovesAllExpired()
        {
            var clock = new FakeClock();
            using var lru = new HandshakeNonceLruCache(100, TimeSpan.FromMinutes(5), clock);
            for (var i = 0; i < 5; i++)
            {
                Assert.True(lru.TryAdd(1, "n" + i));
            }
            clock.UtcNow += TimeSpan.FromMinutes(6);
            lru.EvictExpired();
            Assert.Equal(0, lru.Count);
        }
    }
}
