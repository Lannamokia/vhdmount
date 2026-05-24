using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace VHDMounter.RustDeskBridge.Upload
{
    /// <summary>
    /// Requirement 5.7 / 5.9 / 6.6：有界 FIFO 重试队列。
    ///
    /// <list type="bullet">
    /// <item>容量上限 64（与 Wave 0 的 <c>BridgeConfig.ReportRetryQueueCapacityDefault</c> 一致）；
    ///       超容时丢弃**最旧**条目并 <see cref="ReportDropCount"/> 自增（进程级原子计数）</item>
    /// <item>30s 指数退避：第 attempt 次（1-based）失败后等 [1s,2s,4s,8s,16s,30s] 中第
    ///       <c>min(attempt-1, 5)</c> 个；attempt &gt;= 6 即第 5 次重试仍 RetryableFailure → 丢弃 + drop +1</item>
    /// <item>Enqueue 仅入队（成败 / 满 / 入队抛异常都**不**回写 ReportAck —— ack 始终 accepted，
    ///       由 <see cref="VHDMounter.RustDeskBridge.Session.SessionStateMachine"/> 直接发出）</item>
    /// </list>
    ///
    /// 消费循环 <see cref="RunAsync"/>：取队首 → <see cref="ReportUploader.UploadAsync"/>：
    /// <list type="bullet">
    /// <item><see cref="ReportUploadOutcome.Success"/> → 移除（抹零 passwordPlain）</item>
    /// <item><see cref="ReportUploadOutcome.RetryableFailure"/> → 等退避间隔后入队尾继续重试；
    ///       attempt &gt;= 6 → 丢弃 + drop +1（语义上"重试已尽"）</item>
    /// <item><see cref="ReportUploadOutcome.NonRecoverableFailure"/> → 直接丢弃；<b>不</b>计入 drop（不是缓冲不足，是不可恢复错）</item>
    /// </list>
    ///
    /// passwordPlain byte[] 在入队时复制一份独立副本（避免调用方在异步消费过程中改写 / 抹零），
    /// 在每次"丢弃 / 完成"路径上调用
    /// <see cref="CryptographicOperations.ZeroMemory"/> 抹零（Requirement 6.7 / 13.5）。
    /// </summary>
    internal sealed class ReportUploadQueue : IAsyncDisposable
    {
        public const int DefaultCapacity = 64;
        public const int MaxAttempts = 6;

        // 1s / 2s / 4s / 8s / 16s / 30s（attempt 1 入队后第一次失败 → 等 [0]，依此类推）
        public static readonly TimeSpan[] BackoffSchedule = new[]
        {
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(4),
            TimeSpan.FromSeconds(8),
            TimeSpan.FromSeconds(16),
            TimeSpan.FromSeconds(30),
        };

        private readonly ReportUploader _uploader;
        private readonly Action<string> _diagnostics;
        private readonly int _capacity;
        private readonly object _gate = new object();
        private readonly LinkedList<QueuedItem> _items = new LinkedList<QueuedItem>();
        private readonly SemaphoreSlim _signal = new SemaphoreSlim(0, int.MaxValue);

        private long _reportDropCount;

        public ReportUploadQueue(
            ReportUploader uploader,
            Action<string> diagnostics = null,
            int capacity = DefaultCapacity)
        {
            _uploader = uploader ?? throw new ArgumentNullException(nameof(uploader));
            _diagnostics = diagnostics ?? (_ => { });
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
        }

        /// <summary>
        /// 进程级原子计数，覆盖两类丢弃来源：
        /// <list type="bullet">
        /// <item>入队时容量已满，丢弃最旧条目</item>
        /// <item>同条目重试 5 次后仍是 RetryableFailure</item>
        /// </list>
        /// 不计入 NonRecoverableFailure（schema 错 / 401 / 403 / AAD 错），
        /// 因为那是不可恢复错误而非缓冲不足。
        /// </summary>
        public long ReportDropCount => Interlocked.Read(ref _reportDropCount);

        public int CurrentCount
        {
            get { lock (_gate) return _items.Count; }
        }

        /// <summary>
        /// 入队一条上行任务。<paramref name="payload"/>.PasswordPlain 会被复制一份独立副本，
        /// 调用方仍需自行抹零原 byte[]。
        ///
        /// 队列满时丢弃最旧条目（FIFO 驱逐），<see cref="ReportDropCount"/> 自增。
        /// 不抛异常 —— ack 永远 accepted。
        /// </summary>
        public void Enqueue(ReportPayload payload)
        {
            QueuedItem dropped = null;
            try
            {
                var copy = CopyPayload(payload);
                var item = new QueuedItem(copy, attempt: 1, nextSendUtcMs: 0);

                lock (_gate)
                {
                    if (_items.Count >= _capacity)
                    {
                        var head = _items.First;
                        if (head != null)
                        {
                            dropped = head.Value;
                            _items.RemoveFirst();
                        }
                    }
                    _items.AddLast(item);
                }

                if (dropped != null)
                {
                    Interlocked.Increment(ref _reportDropCount);
                    _diagnostics(
                        $"Report 重试队列容量已满 (capacity={_capacity})，丢弃最旧条目；reportDropCount={ReportDropCount}");
                    ZeroOut(dropped.Payload);
                }

                ReleaseSignal();
            }
            catch (Exception ex)
            {
                // 入队抛异常 —— 不传播给调用方（ack 始终 accepted）。这里只能尽力清理。
                _diagnostics($"Report 入队抛异常（已忽略）：{ex.Message}");
            }
        }

        /// <summary>
        /// 消费循环。每次取队首条目 → 检查 nextSendUtcMs → 若已到时间则
        /// <see cref="ReportUploader.UploadAsync"/>，按 outcome 分别处理。
        /// </summary>
        public async Task RunAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                QueuedItem head;
                long nowMs;
                long waitMs;

                lock (_gate)
                {
                    head = _items.Count > 0 ? _items.First.Value : null;
                    nowMs = NowMs();
                    waitMs = head == null ? -1L : Math.Max(0, head.NextSendUtcMs - nowMs);
                }

                if (head == null)
                {
                    try
                    {
                        await _signal.WaitAsync(ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        return;
                    }
                    continue;
                }

                if (waitMs > 0)
                {
                    try
                    {
                        // 既等退避，也等新条目入队（新条目可能要先发，但 FIFO 下不会插队 —— 只是为了能更快响应取消）
                        await _signal.WaitAsync(TimeSpan.FromMilliseconds(Math.Min(waitMs, 30_000)), ct)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        return;
                    }
                    continue;
                }

                // 取出队首并尝试上行
                lock (_gate)
                {
                    if (_items.Count == 0 || _items.First.Value != head)
                    {
                        // 期间被并发 Enqueue 改动 —— 重新循环
                        continue;
                    }
                    _items.RemoveFirst();
                }

                ReportUploadOutcome outcome;
                try
                {
                    outcome = await _uploader.UploadAsync(head.Payload, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // 取消：把条目重新插入队首并退出（不抹零）
                    lock (_gate) { _items.AddFirst(head); }
                    return;
                }
                catch (Exception ex)
                {
                    _diagnostics($"Report 上行异常（attempt={head.Attempt}）：{ex.Message}");
                    outcome = ReportUploadOutcome.RetryableFailure;
                }

                if (outcome == ReportUploadOutcome.Success)
                {
                    ZeroOut(head.Payload);
                    continue;
                }

                if (outcome == ReportUploadOutcome.NonRecoverableFailure)
                {
                    // 直接丢弃，不计入 drop —— 业务定义"不可恢复"
                    _diagnostics(
                        $"Report 上行不可恢复（attempt={head.Attempt}），直接丢弃，不计入 reportDropCount");
                    ZeroOut(head.Payload);
                    continue;
                }

                // RetryableFailure：考虑重试 / 投降
                var nextAttempt = head.Attempt + 1;
                if (nextAttempt > MaxAttempts)
                {
                    Interlocked.Increment(ref _reportDropCount);
                    _diagnostics(
                        $"Report 重试已尽（attempt={head.Attempt}），丢弃；reportDropCount={ReportDropCount}");
                    ZeroOut(head.Payload);
                    continue;
                }

                var backoff = BackoffSchedule[Math.Min(head.Attempt - 1, BackoffSchedule.Length - 1)];
                var nextSendUtcMs = NowMs() + (long)backoff.TotalMilliseconds;
                var requeued = new QueuedItem(head.Payload, nextAttempt, nextSendUtcMs);

                lock (_gate)
                {
                    if (_items.Count >= _capacity)
                    {
                        var oldHead = _items.First;
                        if (oldHead != null)
                        {
                            Interlocked.Increment(ref _reportDropCount);
                            _diagnostics(
                                $"重试入队时队列已满，丢弃最旧条目；reportDropCount={ReportDropCount}");
                            ZeroOut(oldHead.Value.Payload);
                            _items.RemoveFirst();
                        }
                    }
                    _items.AddLast(requeued);
                }

                _diagnostics(
                    $"Report 上行失败 attempt={head.Attempt}，{(int)backoff.TotalSeconds}s 后重试");
                ReleaseSignal();
            }
        }

        public async ValueTask DisposeAsync()
        {
            // 释放队列内未发送的 byte[]。注意：本方法不 stop RunAsync —— 调用方
            // 应当通过取消传入的 CancellationToken 来停止消费循环，再 Dispose。
            await Task.Yield();
            lock (_gate)
            {
                foreach (var item in _items)
                {
                    ZeroOut(item.Payload);
                }
                _items.Clear();
            }
            _signal.Dispose();
        }

        // ---------- 内部 ----------

        private static ReportPayload CopyPayload(ReportPayload original)
        {
            var pwd = original.PasswordPlain ?? Array.Empty<byte>();
            var copy = new byte[pwd.Length];
            if (pwd.Length > 0)
            {
                Buffer.BlockCopy(pwd, 0, copy, 0, pwd.Length);
            }
            return new ReportPayload(
                original.RustDeskId,
                original.PasswordKind,
                copy,
                original.ReportedAtMs,
                original.SecretVersion);
        }

        private static void ZeroOut(ReportPayload payload)
        {
            if (payload.PasswordPlain != null && payload.PasswordPlain.Length > 0)
            {
                CryptographicOperations.ZeroMemory(payload.PasswordPlain);
            }
        }

        private void ReleaseSignal()
        {
            try { _signal.Release(); }
            catch (ObjectDisposedException) { /* ignore */ }
            catch (SemaphoreFullException) { /* signal 已饱和 —— 反正消费循环会重新检查 */ }
        }

        private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private sealed class QueuedItem
        {
            public QueuedItem(ReportPayload payload, int attempt, long nextSendUtcMs)
            {
                Payload = payload;
                Attempt = attempt;
                NextSendUtcMs = nextSendUtcMs;
            }
            public ReportPayload Payload { get; }
            public int Attempt { get; }
            public long NextSendUtcMs { get; }
        }
    }
}
