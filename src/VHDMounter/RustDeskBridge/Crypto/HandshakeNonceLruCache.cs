using System;
using System.Collections.Generic;
using System.Threading;

namespace VHDMounter.RustDeskBridge.Crypto
{
    /// <summary>
    /// (secretVersion, nonce) → first_seen_at LRU，跨 Bridge_Session 拒绝握手帧重放
    /// （协议文档 §10 / Requirement 4.7 / 4.10 / 4.11 / 12.1 / 12.2 / 14.4）。
    ///
    /// 数据结构：<see cref="LinkedList{T}"/> + <see cref="Dictionary{TKey, TValue}"/>
    /// 索引组合，让查重 O(1)、容量驱逐取首节点 O(1)、时间维度驱逐自首端遍历到第一个未过期为止。
    ///
    /// 同步模型：单一 <see cref="SemaphoreSlim"/>(1) 保护全部 TryAdd / EvictExpired / Count
    /// 操作（Requirement 4.11）。任何持锁路径**不**做任何 ≥ 50 ms 的 I/O；时间获取走
    /// 注入的 <see cref="IClock"/>。
    ///
    /// **不**持久化（决策点 2 / Requirement 12.1：仅进程内存）。
    /// </summary>
    internal sealed class HandshakeNonceLruCache : IDisposable
    {
        private readonly int _capacity;
        private readonly TimeSpan _ttl;
        private readonly IClock _clock;
        private readonly LinkedList<HandshakeNonceEntry> _order = new LinkedList<HandshakeNonceEntry>();
        private readonly Dictionary<(uint Version, string Nonce), LinkedListNode<HandshakeNonceEntry>> _index =
            new Dictionary<(uint, string), LinkedListNode<HandshakeNonceEntry>>();
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private bool _disposed;

        public HandshakeNonceLruCache(int capacity, TimeSpan ttl, IClock clock)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (ttl <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ttl));
            _capacity = capacity;
            _ttl = ttl;
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>
        /// 当前缓存条目数（仅供测试与诊断；持锁读取）。
        /// </summary>
        public int Count
        {
            get
            {
                _gate.Wait();
                try
                {
                    return _index.Count;
                }
                finally
                {
                    _gate.Release();
                }
            }
        }

        /// <summary>
        /// 原子地 EvictExpired → 查重 → 插入。
        /// 返回 true 表示当前 (secretVersion, nonce) 是首次见、已被记录；
        /// 返回 false 表示在窗口期内已经见过（应当返回 invalid_proof，Requirement 4.7）。
        /// 容量上限触达时驱逐最早条目（先时间维度后容量维度，Requirement 4.10）。
        /// </summary>
        public bool TryAdd(uint secretVersion, string nonce)
        {
            if (string.IsNullOrEmpty(nonce)) throw new ArgumentException("nonce 不能为空", nameof(nonce));

            _gate.Wait();
            try
            {
                ThrowIfDisposed();

                var now = _clock.UtcNow;
                EvictExpiredLocked(now);

                var key = (secretVersion, nonce);
                if (_index.ContainsKey(key))
                {
                    return false;
                }

                while (_index.Count >= _capacity)
                {
                    var oldest = _order.First;
                    if (oldest == null) break;
                    _index.Remove((oldest.Value.SecretVersion, oldest.Value.Nonce));
                    _order.RemoveFirst();
                }

                var entry = new HandshakeNonceEntry(secretVersion, nonce, now);
                var node = _order.AddLast(entry);
                _index[key] = node;
                return true;
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// 显式触发时间维度驱逐（供周期性维护任务调用，但 TryAdd 也会在内部触发，因此一般可省）。
        /// </summary>
        public void EvictExpired()
        {
            _gate.Wait();
            try
            {
                ThrowIfDisposed();
                EvictExpiredLocked(_clock.UtcNow);
            }
            finally
            {
                _gate.Release();
            }
        }

        private void EvictExpiredLocked(DateTimeOffset now)
        {
            // first_seen_at 是按插入时刻递增的（除非 IClock 倒退；倒退场景仅出现在测试夹具，
            // 此时把整个 LRU 视为过期更安全）
            while (_order.First is { } head)
            {
                var age = now - head.Value.FirstSeenAt;
                if (age <= _ttl) break;
                _index.Remove((head.Value.SecretVersion, head.Value.Nonce));
                _order.RemoveFirst();
            }
        }

        public void Dispose()
        {
            _gate.Wait();
            try
            {
                _disposed = true;
                _order.Clear();
                _index.Clear();
            }
            finally
            {
                _gate.Release();
            }
            _gate.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(HandshakeNonceLruCache));
        }
    }

    internal readonly struct HandshakeNonceEntry
    {
        public HandshakeNonceEntry(uint secretVersion, string nonce, DateTimeOffset firstSeenAt)
        {
            SecretVersion = secretVersion;
            Nonce = nonce;
            FirstSeenAt = firstSeenAt;
        }

        public uint SecretVersion { get; }
        public string Nonce { get; }
        public DateTimeOffset FirstSeenAt { get; }
    }

    /// <summary>
    /// 注入式时钟，便于测试夹具确定性推进时间。
    /// </summary>
    internal interface IClock
    {
        DateTimeOffset UtcNow { get; }
    }

    internal sealed class SystemClock : IClock
    {
        public static readonly SystemClock Instance = new SystemClock();
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
