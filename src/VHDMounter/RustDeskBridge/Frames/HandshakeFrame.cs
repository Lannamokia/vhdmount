using System.Text.Json.Serialization;

namespace VHDMounter.RustDeskBridge.Frames
{
    /// <summary>
    /// Bridge_Pipe 接入后必收的第一帧（协议文档 §5.1）。
    /// </summary>
    internal sealed class HandshakeFrame
    {
        public const string ProtocolLiteral = "VHDRustDeskBridgeHandshakeV1";

        [JsonPropertyName("protocol")]
        public string Protocol { get; set; } = string.Empty;

        [JsonPropertyName("secretVersion")]
        public uint SecretVersion { get; set; }

        [JsonPropertyName("nonce")]
        public string Nonce { get; set; } = string.Empty;

        [JsonPropertyName("timestampMs")]
        public long TimestampMs { get; set; }

        [JsonPropertyName("clientKind")]
        public string ClientKind { get; set; } = string.Empty;

        [JsonPropertyName("clientVersion")]
        public string ClientVersion { get; set; } = string.Empty;

        [JsonPropertyName("proof")]
        public string Proof { get; set; } = string.Empty;
    }
}
