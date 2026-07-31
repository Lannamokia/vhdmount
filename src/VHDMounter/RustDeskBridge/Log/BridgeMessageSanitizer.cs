using System;
using System.Collections.Generic;

namespace VHDMounter.RustDeskBridge.Log
{
    /// <summary>
    /// Requirement 11.3：在帧 message 落 MachineLogBuffer 之前加一道防御性脱敏。
    /// 直接转调既有 <see cref="MachineLogSanitizer.SanitizeSensitiveText"/> —— 既有实现已经覆盖
    /// 明文密码 / RustDesk 桥字段（controllerName / controllerHwid / passwordCipher / wrapKeyCipher /
    /// authTag / mac / proof / signature / iv）/ TPM 句柄字面量 / 私钥 PEM 等 6 类规则。
    /// </summary>
    internal static class BridgeMessageSanitizer
    {
        public static string Sanitize(string text)
        {
            return MachineLogSanitizer.SanitizeSensitiveText(text ?? string.Empty);
        }

        /// <summary>
        /// 把异常 Message + StackTrace 走脱敏后写入 Component="rustdesk-bridge" 条目。
        /// </summary>
        public static void LogException(
            Exception ex,
            MachineLogBuffer buffer,
            string eventKey,
            string level = "error",
            string sessionId = null)
        {
            if (buffer == null || ex == null) return;

            var raw = string.Concat(
                ex.GetType().FullName ?? "Exception",
                ": ",
                ex.Message ?? string.Empty,
                Environment.NewLine,
                ex.StackTrace ?? string.Empty);
            var sanitized = Sanitize(raw);

            var entry = new MachineLogEntry
            {
                SessionId = sessionId ?? buffer.CurrentSessionId,
                OccurredAt = DateTimeOffset.UtcNow.ToString(
                    "yyyy-MM-ddTHH:mm:ss.fffZ",
                    System.Globalization.CultureInfo.InvariantCulture),
                Level = string.IsNullOrEmpty(level) ? "error" : level,
                Component = "rustdesk-bridge",
                EventKey = MachineLogSanitizer.NormalizeEventKey(eventKey ?? "BRIDGE_EXCEPTION"),
                Message = sanitized,
                RawText = sanitized,
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            };

            buffer.EnqueueRustDeskBridgeEntry(entry);
        }
    }
}
