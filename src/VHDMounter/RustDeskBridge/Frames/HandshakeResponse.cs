using System.Text.Json.Serialization;

namespace VHDMounter.RustDeskBridge.Frames
{
    /// <summary>
    /// 协议文档 §5.3 / Requirement 4.9 规定的裸 JSON 响应：
    /// 成功路径仅 <c>{ "ok": true }</c>，失败路径仅 <c>{ "ok": false, "reason": &lt;enum&gt; }</c>。
    /// 不携带 mac/proof/secretVersion/nonce/timestampMs。
    /// </summary>
    internal sealed class HandshakeResponse
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }

        [JsonPropertyName("reason")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Reason { get; set; }

        public static HandshakeResponse Success() => new() { Ok = true, Reason = null };

        public static HandshakeResponse Failure(string reason) => new() { Ok = false, Reason = reason };
    }
}
