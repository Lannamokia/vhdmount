using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using VHDMounter.RustDeskBridge.Crypto;

namespace VHDMounter.RustDeskBridge.RateLimit
{
    /// <summary>
    /// 同 (rustDeskId, passwordKind, password) 三元组在 1 秒内最多 1 次上报判定（Requirement 14.3）。
    /// 超频时 Report ack 仍 <c>accepted</c>（Requirement 5.7 / 5.9），但 ReportUploader **不**入上行队列；
    /// 实现层只暴露布尔判定，由调用方决定 ack 后续路径。
    ///
    /// 三元组以 SHA-256 摘要做 key，避免在内存中长期持有明文密码（与 Requirement 6.7 / 6.8 一致）。
    /// </summary>
    internal sealed class ReportRateLimiter
    {
        private readonly IClock _clock;
        private readonly TimeSpan _window;
        private readonly object _gate = new object();
        private readonly Dictionary<string, DateTimeOffset> _lastSeen = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        private readonly int _maxKeys;

        public ReportRateLimiter(
            IClock clock,
            TimeSpan window = default,
            int maxKeys = 1024)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _window = window == default ? TimeSpan.FromSeconds(1) : window;
            if (maxKeys <= 0) throw new ArgumentOutOfRangeException(nameof(maxKeys));
            _maxKeys = maxKeys;
        }

        /// <summary>
        /// 返回 true 表示当前三元组允许上行；false 表示在 1 秒窗口内已上行过（Report ack 仍走 accepted）。
        /// </summary>
        public bool TryAcquire(string rustDeskId, string passwordKind, string password)
        {
            var key = ComputeKey(rustDeskId ?? string.Empty, passwordKind ?? string.Empty, password ?? string.Empty);
            lock (_gate)
            {
                var now = _clock.UtcNow;
                EvictExpiredLocked(now);

                if (_lastSeen.TryGetValue(key, out var seen) && now - seen <= _window)
                {
                    return false;
                }

                if (_lastSeen.Count >= _maxKeys)
                {
                    // 容量爆掉的兜底：丢一半最早条目，避免无限增长
                    TrimOldestLocked(_lastSeen.Count / 2);
                }

                _lastSeen[key] = now;
                return true;
            }
        }

        public void Reset()
        {
            lock (_gate)
            {
                _lastSeen.Clear();
            }
        }

        public int TrackedKeyCount
        {
            get
            {
                lock (_gate)
                {
                    return _lastSeen.Count;
                }
            }
        }

        private void EvictExpiredLocked(DateTimeOffset now)
        {
            // O(n) 扫描，反正 _maxKeys 默认 1024，1 秒窗口下淘汰频繁、列表小
            List<string> expired = null;
            foreach (var kv in _lastSeen)
            {
                if (now - kv.Value > _window)
                {
                    expired ??= new List<string>();
                    expired.Add(kv.Key);
                }
            }
            if (expired != null)
            {
                foreach (var k in expired) _lastSeen.Remove(k);
            }
        }

        private void TrimOldestLocked(int dropCount)
        {
            if (dropCount <= 0) return;
            // 简单按 value 排序后丢前 dropCount 个；性能不敏感（仅在容量爆时触发）
            var ordered = new List<KeyValuePair<string, DateTimeOffset>>(_lastSeen);
            ordered.Sort((a, b) => a.Value.CompareTo(b.Value));
            for (var i = 0; i < dropCount && i < ordered.Count; i++)
            {
                _lastSeen.Remove(ordered[i].Key);
            }
        }

        private static string ComputeKey(string rustDeskId, string passwordKind, string password)
        {
            // SHA-256(rustDeskId || \0 || passwordKind || \0 || password)；用 \0 防边界混淆
            var sb = new StringBuilder(rustDeskId.Length + passwordKind.Length + password.Length + 2);
            sb.Append(rustDeskId).Append('\0').Append(passwordKind).Append('\0').Append(password);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
        }
    }
}
