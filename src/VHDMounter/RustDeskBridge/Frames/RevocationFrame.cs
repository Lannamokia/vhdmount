using System.Text.Json.Serialization;

namespace VHDMounter.RustDeskBridge.Frames
{
    /// <summary>
    /// 协议文档 §9.1 服务端推送的吊销帧。VHDMount → RustDesk_Controlled。
    /// </summary>
    internal sealed class RevocationFrame
    {
        public const string ProtocolLiteral = "VHDRustDeskBridgeRevocationV1";

        public const string ReasonDenied = "denied";
        public const string ReasonSecretOutdated = "secret_outdated";

        [JsonPropertyName("protocol")]
        public string Protocol { get; set; } = ProtocolLiteral;

        [JsonPropertyName("secretVersion")]
        public uint SecretVersion { get; set; }

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;

        [JsonPropertyName("issuedAt")]
        public long IssuedAt { get; set; }

        [JsonPropertyName("mac")]
        public string Mac { get; set; } = string.Empty;
    }
}
