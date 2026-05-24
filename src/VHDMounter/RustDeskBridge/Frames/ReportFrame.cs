using System.Text.Json.Serialization;

namespace VHDMounter.RustDeskBridge.Frames
{
    /// <summary>
    /// 协议文档 §6.1 报告帧。RustDesk_Controlled 把 (rustDeskId, password) 推给 VHDMount。
    /// </summary>
    internal sealed class ReportFrame
    {
        public const string ProtocolLiteral = "VHDRustDeskBridgeReportV1";

        public const string PasswordKindTemporary = "temporary";
        public const string PasswordKindPermanent = "permanent";
        public const string PasswordKindPreset = "preset";
        public const string PasswordKindAbsent = "absent";

        public const string ReasonStartup = "startup";
        public const string ReasonIdChange = "id_change";
        public const string ReasonPasswordChange = "password_change";
        public const string ReasonRotation = "rotation";
        public const string ReasonHeartbeat = "heartbeat";

        public static readonly string[] AllowedPasswordKinds = new[]
        {
            PasswordKindTemporary,
            PasswordKindPermanent,
            PasswordKindPreset,
            PasswordKindAbsent,
        };

        public static readonly string[] AllowedReasons = new[]
        {
            ReasonStartup,
            ReasonIdChange,
            ReasonPasswordChange,
            ReasonRotation,
            ReasonHeartbeat,
        };

        [JsonPropertyName("protocol")]
        public string Protocol { get; set; } = string.Empty;

        [JsonPropertyName("secretVersion")]
        public uint SecretVersion { get; set; }

        [JsonPropertyName("rustDeskId")]
        public string RustDeskId { get; set; } = string.Empty;

        [JsonPropertyName("passwordKind")]
        public string PasswordKind { get; set; } = string.Empty;

        [JsonPropertyName("password")]
        public string Password { get; set; } = string.Empty;

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;

        [JsonPropertyName("reportedAt")]
        public long ReportedAt { get; set; }

        [JsonPropertyName("nonce")]
        public string Nonce { get; set; } = string.Empty;

        [JsonPropertyName("mac")]
        public string Mac { get; set; } = string.Empty;
    }
}
