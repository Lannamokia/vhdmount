using System.Text.Json.Serialization;

namespace VHDMounter.RustDeskBridge.Frames
{
    /// <summary>
    /// 协议文档 §6.3 ack。Requirement 5.7 / 5.9 强制：通过 schema + HMAC 校验后必为 accepted。
    /// </summary>
    internal sealed class ReportAck
    {
        public const string ResultAccepted = "accepted";
        public const string ResultRejected = "rejected";

        public const string ReasonDeny = "deny";
        public const string ReasonRateLimited = "rate_limited";
        public const string ReasonSecretOutdated = "secret_outdated";
        public const string ReasonInvalidMac = "invalid_mac";

        [JsonPropertyName("result")]
        public string Result { get; set; } = ResultAccepted;

        [JsonPropertyName("reason")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Reason { get; set; }

        public static ReportAck Accepted() => new() { Result = ResultAccepted, Reason = null };

        public static ReportAck Rejected(string reason) => new() { Result = ResultRejected, Reason = reason };
    }
}
