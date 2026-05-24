using System;
using System.Collections.Generic;
using VHDMounter.RustDeskBridge.Crypto;

namespace VHDMounter.RustDeskBridge.RateLimit
{
    /// <summary>
    /// 跨 Bridge_Session 的握手失败 5 秒滑动窗口 + 60 秒冷却（Requirement 14.1 / 14.2）。
    /// 触达阈值（默认 ≥ 3）后进入冷却期：冷却期内的握手帧由 SessionStateMachine 直接回
    /// <c>HandshakeResponse { ok: false, reason: "rate_limited" }</c>，<b>不</b>解析任何业务字段、
    /// <b>不</b>计算 HMAC、<b>不</b>触碰 LRU。
    ///
    /// 冷却到期重置：清空失败记录、退出冷却。
    /// </summary>
    internal sealed class HandshakeRateLimiter
    {
        private readonly IClock _clock;
        private readonly int _failureThreshold;
        private readonly TimeSpan _window;
        private readonly TimeSpan _cooldown;
        private readonly object _gate = new object();
        private readonly Queue<DateTimeOffset> _failures = new Queue<DateTimeOffset>();
        private DateTimeOffset? _coolingDownUntil;

        public HandshakeRateLimiter(
            IClock clock,
            int failureThreshold = 3,
            TimeSpan window = default,
            TimeSpan cooldown = default)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            if (failureThreshold <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(failureThreshold));
            }
            _failureThreshold = failureThreshold;
            _window = window == default ? TimeSpan.FromSeconds(5) : window;
            _cooldown = cooldown == default ? TimeSpan.FromSeconds(60) : cooldown;
        }

        public bool IsCoolingDown
        {
            get
            {
                lock (_gate)
                {
                    return IsCoolingDownLocked(_clock.UtcNow);
                }
            }
        }

        /// <summary>
        /// 记录一次握手失败：可能让计数器跨过阈值进入冷却。
        /// </summary>
        public void RecordFailure()
        {
            lock (_gate)
            {
                var now = _clock.UtcNow;

                // 已经在冷却中，不必再累加（避免无限延长）
                if (IsCoolingDownLocked(now))
                {
                    return;
                }

                EvictOutsideWindowLocked(now);
                _failures.Enqueue(now);

                if (_failures.Count >= _failureThreshold)
                {
                    _coolingDownUntil = now + _cooldown;
                    _failures.Clear();
                }
            }
        }

        public void Reset()
        {
            lock (_gate)
            {
                _failures.Clear();
                _coolingDownUntil = null;
            }
        }

        private bool IsCoolingDownLocked(DateTimeOffset now)
        {
            if (!_coolingDownUntil.HasValue) return false;
            if (now >= _coolingDownUntil.Value)
            {
                _coolingDownUntil = null;
                _failures.Clear();
                return false;
            }
            return true;
        }

        private void EvictOutsideWindowLocked(DateTimeOffset now)
        {
            while (_failures.Count > 0 && now - _failures.Peek() > _window)
            {
                _failures.Dequeue();
            }
        }
    }
}
