using System.Text.Json.Serialization;

namespace VHDMounter.RustDeskBridge.Frames
{
    /// <summary>
    /// 协议文档 §8.1 主控端审批请求帧。
    /// </summary>
    internal sealed class PeerApprovalFrame
    {
        public const string ProtocolLiteral = "VHDRustDeskBridgePeerApprovalV1";

        public const string ConnectionTypeControlled = "controlled";
        public const string ConnectionTypeViewOnly = "view-only";
        public const string ConnectionTypeFileTransfer = "file-transfer";
        public const string ConnectionTypePortForward = "port-forward";
        public const string ConnectionTypeTerminal = "terminal";

        public static readonly string[] AllowedConnectionTypes = new[]
        {
            ConnectionTypeControlled,
            ConnectionTypeViewOnly,
            ConnectionTypeFileTransfer,
            ConnectionTypePortForward,
            ConnectionTypeTerminal,
        };

        [JsonPropertyName("protocol")]
        public string Protocol { get; set; } = string.Empty;

        [JsonPropertyName("secretVersion")]
        public uint SecretVersion { get; set; }

        [JsonPropertyName("controlledMachineId")]
        public string ControlledMachineId { get; set; } = string.Empty;

        [JsonPropertyName("controllerId")]
        public string ControllerId { get; set; } = string.Empty;

        [JsonPropertyName("controllerName")]
        public string ControllerName { get; set; } = string.Empty;

        [JsonPropertyName("controllerPlatform")]
        public string ControllerPlatform { get; set; } = string.Empty;

        [JsonPropertyName("controllerHwid")]
        public string ControllerHwid { get; set; } = string.Empty;

        [JsonPropertyName("peerSocketAddr")]
        public string PeerSocketAddr { get; set; } = string.Empty;

        [JsonPropertyName("connectionType")]
        public string ConnectionType { get; set; } = string.Empty;

        [JsonPropertyName("requestNonce")]
        public string RequestNonce { get; set; } = string.Empty;

        [JsonPropertyName("timestampMs")]
        public long TimestampMs { get; set; }

        [JsonPropertyName("mac")]
        public string Mac { get; set; } = string.Empty;
    }
}
