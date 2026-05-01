using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace VHDMounter
{
    internal sealed class MachineLogEntry
    {
        public string SessionId { get; set; } = string.Empty;

        public long Seq { get; set; }

        public string OccurredAt { get; set; } = string.Empty;

        public string Level { get; set; } = "info";

        public string Component { get; set; } = "Program";

        public string EventKey { get; set; } = "TRACE_LINE";

        public string Message { get; set; } = string.Empty;

        public string RawText { get; set; } = string.Empty;

        public Dictionary<string, string> Metadata { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        [JsonIgnore]
        public int SerializedByteCount { get; set; }
    }

    internal sealed class MachineLogClientConfiguration
    {
        public string ConfigPath { get; private set; } = string.Empty;

        public bool EnableLogUpload { get; private set; }

        public string MachineId { get; private set; } = "MACHINE_001";

        public string ServerBaseUrl { get; private set; } = ServiceEndpointResolver.DefaultServerBaseUrl;

        public string RegistrationCertificatePath { get; private set; } = string.Empty;

        public string RegistrationCertificatePassword { get; private set; } = string.Empty;

        public int MachineLogUploadIntervalMs { get; private set; } = 3000;

        public int MachineLogUploadBatchSize { get; private set; } = 200;

        public long MachineLogUploadMaxSpoolBytes { get; private set; } = 50L * 1024L * 1024L;

        public string SpoolPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "machine-log-spool.jsonl");

        public string ResolveMachineLogBootstrapUrl()
        {
            return ServiceEndpointResolver.CombineHttpEndpoint(ServerBaseUrl, "api/machine-log-bootstrap");
        }

        public Uri ResolveWebSocketUri()
        {
            return ServiceEndpointResolver.BuildWebSocketEndpoint(ServerBaseUrl, "ws/machine-log");
        }

        public static string GenerateSessionId()
        {
            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
            var suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant();
            return $"{timestamp}-{suffix}";
        }

        public static MachineLogClientConfiguration Load(string configPath, Action<string> diagnostics = null)
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
                diagnostics?.Invoke($"机台日志配置文件不存在，使用默认值: {configPath}");
            }

            return new MachineLogClientConfiguration
            {
                ConfigPath = configPath,
                EnableLogUpload = ParseBool(values, "EnableLogUpload", false),
                MachineId = ParseString(values, "MachineId", "MACHINE_001"),
                ServerBaseUrl = ServiceEndpointResolver.ResolveServerBaseUrl(values),
                RegistrationCertificatePath = ParseString(values, "RegistrationCertificatePath", string.Empty),
                RegistrationCertificatePassword = ParseString(values, "RegistrationCertificatePassword", string.Empty),
                MachineLogUploadIntervalMs = ParseInt(values, "MachineLogUploadIntervalMs", 3000, 250, 60000),
                MachineLogUploadBatchSize = ParseInt(values, "MachineLogUploadBatchSize", 200, 1, 200),
                MachineLogUploadMaxSpoolBytes = ParseLong(values, "MachineLogUploadMaxSpoolBytes", 50L * 1024L * 1024L, 1024L * 1024L, 512L * 1024L * 1024L),
            };
        }

        private static bool ParseBool(IDictionary<string, string> values, string key, bool fallback)
        {
            return values.TryGetValue(key, out var rawValue) &&
                   bool.TryParse(rawValue, out var parsed)
                ? parsed
                : fallback;
        }

        private static int ParseInt(IDictionary<string, string> values, string key, int fallback, int minValue, int maxValue)
        {
            if (!values.TryGetValue(key, out var rawValue) || !int.TryParse(rawValue, out var parsed))
            {
                return fallback;
            }

            return Math.Clamp(parsed, minValue, maxValue);
        }

        private static long ParseLong(IDictionary<string, string> values, string key, long fallback, long minValue, long maxValue)
        {
            if (!values.TryGetValue(key, out var rawValue) || !long.TryParse(rawValue, out var parsed))
            {
                return fallback;
            }

            return Math.Clamp(parsed, minValue, maxValue);
        }

        private static string ParseString(IDictionary<string, string> values, string key, string fallback)
        {
            return values.TryGetValue(key, out var rawValue) && !string.IsNullOrWhiteSpace(rawValue)
                ? rawValue.Trim()
                : fallback;
        }
    }
}
