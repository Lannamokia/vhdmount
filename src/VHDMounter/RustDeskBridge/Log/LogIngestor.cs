using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using VHDMounter.RustDeskBridge.Frames;

namespace VHDMounter.RustDeskBridge.Log
{
    /// <summary>
    /// Requirement 9.4 / 9.5 / 11.3：把 LogFrame 字段映射成 MachineLogEntry 入队 MachineLogBuffer。
    ///
    /// 关键约束：
    /// - message 先按 UTF-8 字节边界截断到 ≤ 4096，<b>不</b>出现半个码点
    /// - 截断后过 <see cref="BridgeMessageSanitizer.Sanitize"/> 二次脱敏
    /// - level 不在 {error, warn, info, debug, trace} 集合时按 §9.3 当 schema 失败处理
    ///   （由调用方判断；本类的 <see cref="Ingest"/> 在 level 非法时返回 false，不入队）
    /// - Metadata 留空字典（Requirement 9.4：<b>不</b>把 RustDesk 端字段写入 Metadata）
    /// </summary>
    internal sealed class LogIngestor
    {
        public const int MaxMessageBytes = 4096;

        private readonly MachineLogBuffer _buffer;

        public LogIngestor(MachineLogBuffer buffer)
        {
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        }

        /// <summary>
        /// 返回 true 表示已成功入队；false 表示 level schema 非法，调用方按 §9.3 静默丢弃 + drop 计数。
        /// </summary>
        public bool Ingest(LogFrame frame)
        {
            if (frame == null) return false;
            if (Array.IndexOf(LogFrame.AllowedLevels, frame.Level ?? string.Empty) < 0)
            {
                return false;
            }

            var truncated = TruncateUtf8(frame.Message ?? string.Empty, MaxMessageBytes);
            var sanitized = BridgeMessageSanitizer.Sanitize(truncated);

            var entry = new MachineLogEntry
            {
                SessionId = _buffer.CurrentSessionId,
                OccurredAt = FormatTimestampUtc(frame.TimestampMs),
                Level = frame.Level,
                Component = "rustdesk-bridge",
                EventKey = MachineLogSanitizer.NormalizeEventKey(frame.Target ?? "RUSTDESK_BRIDGE_LOG"),
                Message = sanitized,
                RawText = sanitized,
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            };

            return _buffer.EnqueueRustDeskBridgeEntry(entry);
        }

        /// <summary>
        /// 把毫秒级 unix 时间戳格式化为 <c>yyyy-MM-ddTHH:mm:ss.fffZ</c>（Requirement 9.4）。
        /// </summary>
        public static string FormatTimestampUtc(long timestampMs)
        {
            try
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(timestampMs).UtcDateTime
                    .ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
            }
            catch (ArgumentOutOfRangeException)
            {
                // 异常时间戳（远超合法范围）：退化到当前时刻，避免 Bridge_Server 整条挂掉
                return DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// 按 UTF-8 字节边界截断字符串到不超过 <paramref name="maxBytes"/>，确保不出现半个码点。
        /// </summary>
        public static string TruncateUtf8(string text, int maxBytes)
        {
            if (string.IsNullOrEmpty(text) || maxBytes <= 0) return string.Empty;
            var encoded = Encoding.UTF8.GetBytes(text);
            if (encoded.Length <= maxBytes) return text;

            // 从 maxBytes 开始向后退到最近的 UTF-8 起始字节边界
            // UTF-8 后续字节模式 10xxxxxx 不能作为切点
            var cut = maxBytes;
            while (cut > 0 && (encoded[cut] & 0xC0) == 0x80)
            {
                cut--;
            }
            return Encoding.UTF8.GetString(encoded, 0, cut);
        }
    }
}
