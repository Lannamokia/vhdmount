using System;
using System.Collections.Generic;
using System.IO;

namespace VHDMounter.RustDeskBridge.Config
{
    /// <summary>
    /// RustDesk 桥相关的运行期配置，全部从既有 <see cref="MachineLogClientConfiguration"/> 同一份
    /// <c>vhdmonter_config.ini</c> 派生。沿用现有 INI 解析风格（按行 split '=' 取键值），
    /// 缺省值与越界回退按 design §"Bridge_Config 配置 schema" 的注释执行。
    /// </summary>
    internal sealed class BridgeConfig
    {
        public const int SnapshotPullIntervalMinSeconds = 60;
        public const int SnapshotPullIntervalMaxSeconds = 600;
        public const int SnapshotPullIntervalDefaultSeconds = 300;

        public const int ReportUpstreamTimeoutMinMs = 1_000;
        public const int ReportUpstreamTimeoutMaxMs = 30_000;
        public const int ReportUpstreamTimeoutDefaultMs = 5_000;

        public const int ReportRetryQueueCapacityMin = 8;
        public const int ReportRetryQueueCapacityMax = 1_024;
        public const int ReportRetryQueueCapacityDefault = 64;

        public const int HandshakeNonceLruClientCountMin = 1;
        public const int HandshakeNonceLruClientCountMax = 16;
        public const int HandshakeNonceLruClientCountDefault = 1;

        public const int RevocationListenPortMin = 1024;
        public const int RevocationListenPortMax = 65535;
        public const int RevocationListenPortDefault = 7891;

        public string ConfigPath { get; private set; } = string.Empty;

        public bool EnableRustDeskBridge { get; private set; }

        public string BridgePolicyPubkeyPath { get; private set; } = string.Empty;

        public int SnapshotPullIntervalSeconds { get; private set; } = SnapshotPullIntervalDefaultSeconds;

        public int ReportUpstreamTimeoutMs { get; private set; } = ReportUpstreamTimeoutDefaultMs;

        public int ReportRetryQueueCapacity { get; private set; } = ReportRetryQueueCapacityDefault;

        public int HandshakeNonceLruClientCount { get; private set; } = HandshakeNonceLruClientCountDefault;

        public int BridgeRevocationListenPort { get; private set; } = RevocationListenPortDefault;

        /// <summary>
        /// Handshake_Nonce_LRU 容量 = 300 × N（Requirement 14.4）。
        /// </summary>
        public int HandshakeNonceLruCapacity => 300 * HandshakeNonceLruClientCount;

        public static BridgeConfig Load(string configPath, Action<string> diagnostics = null)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(configPath))
            {
                foreach (var rawLine in File.ReadAllLines(configPath))
                {
                    var line = rawLine?.Trim();
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";") || line.StartsWith("["))
                    {
                        continue;
                    }

                    var parts = line.Split('=', 2);
                    if (parts.Length == 2)
                    {
                        values[parts[0].Trim()] = parts[1].Trim();
                    }
                }
            }
            else
            {
                diagnostics?.Invoke($"RustDesk 桥配置文件不存在，使用默认值: {configPath}");
            }

            return new BridgeConfig
            {
                ConfigPath = configPath,
                EnableRustDeskBridge = ParseBool(values, "EnableRustDeskBridge", false),
                BridgePolicyPubkeyPath = ParseString(values, "BridgePolicyPubkeyPath", string.Empty),
                SnapshotPullIntervalSeconds = ParseInt(
                    values,
                    "SnapshotPullIntervalSeconds",
                    SnapshotPullIntervalDefaultSeconds,
                    SnapshotPullIntervalMinSeconds,
                    SnapshotPullIntervalMaxSeconds,
                    diagnostics),
                ReportUpstreamTimeoutMs = ParseInt(
                    values,
                    "ReportUpstreamTimeoutMs",
                    ReportUpstreamTimeoutDefaultMs,
                    ReportUpstreamTimeoutMinMs,
                    ReportUpstreamTimeoutMaxMs,
                    diagnostics),
                ReportRetryQueueCapacity = ParseInt(
                    values,
                    "ReportRetryQueueCapacity",
                    ReportRetryQueueCapacityDefault,
                    ReportRetryQueueCapacityMin,
                    ReportRetryQueueCapacityMax,
                    diagnostics),
                HandshakeNonceLruClientCount = ParseInt(
                    values,
                    "HandshakeNonceLruClientCount",
                    HandshakeNonceLruClientCountDefault,
                    HandshakeNonceLruClientCountMin,
                    HandshakeNonceLruClientCountMax,
                    diagnostics),
                BridgeRevocationListenPort = ParseInt(
                    values,
                    "BridgeRevocationListenPort",
                    RevocationListenPortDefault,
                    RevocationListenPortMin,
                    RevocationListenPortMax,
                    diagnostics),
            };
        }

        private static bool ParseBool(IDictionary<string, string> values, string key, bool fallback)
        {
            return values.TryGetValue(key, out var rawValue) &&
                   bool.TryParse(rawValue, out var parsed)
                ? parsed
                : fallback;
        }

        private static int ParseInt(
            IDictionary<string, string> values,
            string key,
            int fallback,
            int minValue,
            int maxValue,
            Action<string> diagnostics)
        {
            if (!values.TryGetValue(key, out var rawValue) || !int.TryParse(rawValue, out var parsed))
            {
                return fallback;
            }

            if (parsed < minValue)
            {
                diagnostics?.Invoke($"配置项 {key}={parsed} 低于最小值 {minValue}，已夹紧");
                return minValue;
            }

            if (parsed > maxValue)
            {
                diagnostics?.Invoke($"配置项 {key}={parsed} 高于最大值 {maxValue}，已夹紧");
                return maxValue;
            }

            return parsed;
        }

        private static string ParseString(IDictionary<string, string> values, string key, string fallback)
        {
            return values.TryGetValue(key, out var rawValue) && !string.IsNullOrWhiteSpace(rawValue)
                ? rawValue.Trim()
                : fallback;
        }
    }
}
