using System.Text.Json.Serialization;

namespace VHDMounter.RustDeskBridge.Frames
{
    /// <summary>
    /// 协议文档 §8.3 主控端审批响应。
    ///
    /// **决策点 3**：始终省略 reason 字段（Requirement 7.7 / 11.2 隐私最小化）。
    /// 命中：<c>{ "result": "approved", "ttlMs": 1 }</c>（ttlMs == 1 让 RustDesk 一侧的
    /// ApprovalCache 写入瞬间即过期 → 下次必查 VHDMount，参见 Requirement 8.5.3）。
    /// 未命中：<c>{ "result": "rejected" }</c>。
    /// </summary>
    internal sealed class PeerApprovalResponse
    {
        public const string ResultApproved = "approved";
        public const string ResultRejected = "rejected";

        [JsonPropertyName("result")]
        public string Result { get; set; } = ResultRejected;

        [JsonPropertyName("ttlMs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? TtlMs { get; set; }

        public static PeerApprovalResponse Approved(int ttlMs = 1) => new()
        {
            Result = ResultApproved,
            TtlMs = ttlMs,
        };

        public static PeerApprovalResponse Rejected() => new()
        {
            Result = ResultRejected,
            TtlMs = null,
        };
    }
}
