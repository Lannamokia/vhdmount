using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace VHDMounter.RustDeskBridge.Log
{
    /// <summary>
    /// Requirement 9.3 / 9.7 / 9.8：进程级原子计数 + 60s 滚动窗口汇总条目。
    ///
    /// 共享两类来源：
    /// <list type="bullet">
    /// <item>§9.3 Log 帧 mac / secretVersion / level schema 校验失败</item>
    /// <item>§9.7 MachineLogBuffer 写失败</item>
    /// </list>
    ///
    /// 进程生命周期内只增不减；Bridge_Session 重建不归零。
    ///
    /// <see cref="RunSummaryLoopAsync"/>：60 秒滚动窗口若有非零增量 OR 收到
    /// <see cref="NotifySessionEnded"/>，则写一条
    /// <c>Level="warn"</c>、<c>EventKey="bridge_log_drop_count"</c>、
    /// <c>Message="window=&lt;dec&gt;, total=&lt;dec&gt;"</c> 到 MachineLogBuffer。
    /// </summary>
    internal sealed class BridgeLogDropCounter
    {
        public static readonly TimeSpan DefaultSummaryWindow = TimeSpan.FromSeconds(60);

        private readonly MachineLogBuffer _buffer;
        private readonly TimeSpan _summaryWindow;
        private readonly Func<DateTimeOffset> _utcNow;

        private long _totalCount;
        private long _lastReportedTotal;
        private readonly SemaphoreSlim _sessionEndSignal = new SemaphoreSlim(0, int.MaxValue);

        public BridgeLogDropCounter(MachineLogBuffer buffer, TimeSpan? summaryWindow = null, Func<DateTimeOffset> utcNow = null)
        {
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            _summaryWindow = summaryWindow ?? DefaultSummaryWindow;
            _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        }

        public long TotalCount => Interlocked.Read(ref _totalCount);

        /// <summary>
        /// 累加一次失败计数（来源：§9.3 / §9.7）。线程安全、O(1)。
        /// </summary>
        public void Increment()
        {
            Interlocked.Increment(ref _totalCount);
        }

        /// <summary>
        /// Bridge_Session 结束时调用 —— 立即触发一次汇总（即使窗口未到期），
        /// 之后 RunSummaryLoopAsync 会在下一次循环检查时写入条目。
        /// </summary>
        public void NotifySessionEnded()
        {
            try
            {
                _sessionEndSignal.Release();
            }
            catch (SemaphoreFullException)
            {
                // 队列满 —— 忽略（已经通知过了）
            }
            catch (ObjectDisposedException)
            {
                // 进程退出
            }
        }

        /// <summary>
        /// 进程级 60s 滚动窗口循环。每个窗口结束或收到 NotifySessionEnded 时
        /// 检查窗口内 delta，非零则写一条汇总日志。
        /// </summary>
        public async Task RunSummaryLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                bool sessionEndedEarly;
                try
                {
                    sessionEndedEarly = await _sessionEndSignal.WaitAsync(_summaryWindow, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }

                FlushIfNonzero();
                _ = sessionEndedEarly;
            }
        }

        /// <summary>
        /// 显式触发一次窗口汇总（测试与进程退出路径用）。
        /// </summary>
        public void FlushIfNonzero()
        {
            var total = Interlocked.Read(ref _totalCount);
            var lastReported = Interlocked.Read(ref _lastReportedTotal);
            var delta = total - lastReported;
            if (delta <= 0)
            {
                return;
            }

            // 试图把 lastReported 推进到 total；如果其它线程更早完成,自然不会重复打印
            if (Interlocked.CompareExchange(ref _lastReportedTotal, total, lastReported) != lastReported)
            {
                return;
            }

            var entry = new MachineLogEntry
            {
                SessionId = _buffer.CurrentSessionId,
                OccurredAt = _utcNow().UtcDateTime.ToString(
                    "yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
                Level = "warn",
                Component = "rustdesk-bridge",
                EventKey = "bridge_log_drop_count",
                Message = $"window={delta.ToString(CultureInfo.InvariantCulture)}, total={total.ToString(CultureInfo.InvariantCulture)}",
                RawText = $"window={delta.ToString(CultureInfo.InvariantCulture)}, total={total.ToString(CultureInfo.InvariantCulture)}",
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            };

            _buffer.EnqueueRustDeskBridgeEntry(entry);
        }
    }
}
