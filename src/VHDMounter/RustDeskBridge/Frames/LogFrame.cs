using System.Text.Json.Serialization;

namespace VHDMounter.RustDeskBridge.Frames
{
    /// <summary>
    /// 协议文档 §7.1 日志帧。fire-and-forget，无响应。
    /// </summary>
    internal sealed class LogFrame
    {
        public const string ProtocolLiteral = "VHDRustDeskBridgeLogV1";

        public const string LevelError = "error";
        public const string LevelWarn = "warn";
        public const string LevelInfo = "info";
        public const string LevelDebug = "debug";
        public const string LevelTrace = "trace";

        public static readonly string[] AllowedLevels = new[]
        {
            LevelError,
            LevelWarn,
            LevelInfo,
            LevelDebug,
            LevelTrace,
        };

        [JsonPropertyName("protocol")]
        public string Protocol { get; set; } = string.Empty;

        [JsonPropertyName("secretVersion")]
        public uint SecretVersion { get; set; }

        [JsonPropertyName("level")]
        public string Level { get; set; } = string.Empty;

        [JsonPropertyName("target")]
        public string Target { get; set; } = string.Empty;

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("timestampMs")]
        public long TimestampMs { get; set; }

        [JsonPropertyName("mac")]
        public string Mac { get; set; } = string.Empty;
    }
}
