using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace VHDMounter
{
    internal static class MachineLogSanitizer
    {
        private static readonly Regex InlineSecretPattern = new Regex(
            "(?i)(\\b(?:password|secret|token|authorization|ciphertext|totpsecret|totpcode|sessionsecret|evhdpassword|registrationcertificatepassword)\\b\\s*[:=]\\s*)([^\\s,;]+)",
            RegexOptions.Compiled);

        private static readonly Regex QuerySecretPattern = new Regex(
            "(?i)([?&](?:password|secret|token|ciphertext|totpSecret|totpCode|sessionSecret|evhdPassword|registrationCertificatePassword)=)([^&\\s]+)",
            RegexOptions.Compiled);

        private static readonly Regex JsonSecretPattern = new Regex(
            "(?i)(\"(?:password|secret|token|authorization|ciphertext|totpSecret|totpCode|sessionSecret|evhdPassword|registrationCertificatePassword)\"\\s*:\\s*\")([^\"]*)(\")",
            RegexOptions.Compiled);

        // --- RustDesk 桥相关脱敏规则（rustdesk-bridge-host feature, Requirement 11.3）---

        // 1) RustDesk Bridge 字段（controllerName / controllerHwid / passwordCipher /
        //    wrapKeyCipher / iv / authTag / mac / proof / signature）：JSON 形式 → 哈希指代
        private static readonly Regex RustDeskBridgeJsonFieldsPattern = new Regex(
            "(?i)(\"(?:controllerName|controllerHwid|passwordCipher|wrapKeyCipher|authTag|mac|proof|signature|iv)\"\\s*:\\s*\")([^\"]*)(\")",
            RegexOptions.Compiled);

        // 2) RustDesk Bridge 字段（同上）：inline 形式（key=value 或 key: value）
        private static readonly Regex RustDeskBridgeInlineFieldsPattern = new Regex(
            "(?i)(\\b(?:controllerName|controllerHwid|passwordCipher|wrapKeyCipher|authTag|mac|proof|signature)\\b\\s*[:=]\\s*)([^\\s,;]+)",
            RegexOptions.Compiled);

        // 3) TPM 句柄字面量
        private static readonly Regex TpmHandlePattern = new Regex(
            "(?i)(VHDMounterKey_[A-Za-z0-9_-]+|Microsoft Software Key Storage Provider)",
            RegexOptions.Compiled);

        // 4) RSA / EC / 私钥 PEM 头尾及内部 base64
        private static readonly Regex PrivateKeyPemPattern = new Regex(
            "-----BEGIN (?:RSA |EC |DSA |ENCRYPTED )?PRIVATE KEY-----[\\s\\S]*?-----END (?:RSA |EC |DSA |ENCRYPTED )?PRIVATE KEY-----",
            RegexOptions.Compiled);

        private static readonly Regex PrefixPattern = new Regex(
            "^(?<prefix>[A-Za-z0-9_]+):\\s*(?<message>.*)$",
            RegexOptions.Compiled);

        private static readonly Regex MetadataPattern = new Regex(
            "(?<key>[A-Za-z][A-Za-z0-9_]{1,63})=(?<value>\"[^\"]*\"|[^\\s]+)",
            RegexOptions.Compiled);

        public static string SanitizeSensitiveText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var sanitized = text;
            sanitized = InlineSecretPattern.Replace(sanitized, "$1***");
            sanitized = QuerySecretPattern.Replace(sanitized, "$1***");
            sanitized = JsonSecretPattern.Replace(sanitized, "$1***$3");

            // RustDesk 桥（rustdesk-bridge-host feature, Requirement 11.3）
            sanitized = RustDeskBridgeJsonFieldsPattern.Replace(sanitized, "$1***$3");
            sanitized = RustDeskBridgeInlineFieldsPattern.Replace(sanitized, "$1***");
            sanitized = TpmHandlePattern.Replace(sanitized, "***");
            sanitized = PrivateKeyPemPattern.Replace(sanitized, "[PRIVATE_KEY_REDACTED]");

            return sanitized.Replace("\0", string.Empty).TrimEnd();
        }

        public static MachineLogEntry BuildEntry(string sessionId, long seq, string rawText)
        {
            var sanitized = SanitizeSensitiveText(rawText);
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                return null;
            }

            var component = "Program";
            var eventKey = "TRACE_LINE";
            var message = sanitized;
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var match = PrefixPattern.Match(sanitized);
            if (match.Success)
            {
                var prefix = match.Groups["prefix"].Value;
                var body = match.Groups["message"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(prefix))
                {
                    eventKey = NormalizeEventKey(prefix);
                    component = InferComponent(prefix, eventKey);
                    metadata["rawPrefix"] = prefix;
                }

                if (!string.IsNullOrWhiteSpace(body))
                {
                    message = body;
                }
            }
            else if (sanitized.StartsWith("====", StringComparison.Ordinal))
            {
                eventKey = "LIFECYCLE_MARKER";
            }

            foreach (Match metadataMatch in MetadataPattern.Matches(message))
            {
                var key = metadataMatch.Groups["key"].Value;
                var value = metadataMatch.Groups["value"].Value.Trim('"');
                if (!metadata.ContainsKey(key))
                {
                    metadata[key] = SanitizeSensitiveText(value);
                }
            }

            return new MachineLogEntry
            {
                SessionId = sessionId,
                Seq = seq,
                OccurredAt = DateTimeOffset.UtcNow.ToString("O"),
                Level = InferLevel(sanitized, eventKey),
                Component = component,
                EventKey = eventKey,
                Message = message,
                RawText = sanitized,
                Metadata = metadata,
            };
        }

        public static string NormalizeEventKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "TRACE_LINE";
            }

            var normalized = Regex.Replace(value.Trim(), "[^A-Za-z0-9]+", "_")
                .Trim('_')
                .ToUpperInvariant();
            return string.IsNullOrWhiteSpace(normalized) ? "TRACE_LINE" : normalized;
        }

        private static string InferComponent(string prefix, string eventKey)
        {
            var normalizedPrefix = prefix?.Trim() ?? string.Empty;
            if (normalizedPrefix.Equals("STATUS", StringComparison.OrdinalIgnoreCase) ||
                normalizedPrefix.StartsWith("EVHD_MOUNT_", StringComparison.OrdinalIgnoreCase))
            {
                return "VHDManager";
            }

            if (normalizedPrefix.StartsWith("MAINWINDOW", StringComparison.OrdinalIgnoreCase) ||
                eventKey.StartsWith("UI_", StringComparison.OrdinalIgnoreCase))
            {
                return "MainWindow";
            }

            if (normalizedPrefix.StartsWith("SELF_UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                return "Program";
            }

            return "Program";
        }

        private static string InferLevel(string text, string eventKey)
        {
            var sample = (text ?? string.Empty).ToLowerInvariant();
            if (sample.Contains("exception") ||
                sample.Contains(" error") ||
                sample.Contains("失败") ||
                sample.Contains("异常") ||
                sample.Contains("fatal"))
            {
                return "error";
            }

            if (sample.Contains("warn") ||
                sample.Contains("warning") ||
                sample.Contains("重试") ||
                sample.Contains("超时") ||
                sample.Contains("等待") ||
                sample.Contains("跳过") ||
                sample.Contains("未找到"))
            {
                return "warn";
            }

            if (eventKey.Equals("TRACE_LINE", StringComparison.OrdinalIgnoreCase) ||
                eventKey.Equals("CURRENT_DIRECTORY", StringComparison.OrdinalIgnoreCase) ||
                eventKey.Equals("BASE_DIRECTORY", StringComparison.OrdinalIgnoreCase) ||
                eventKey.Equals("RID_LIB_PATH", StringComparison.OrdinalIgnoreCase))
            {
                return "debug";
            }

            return "info";
        }
    }
}