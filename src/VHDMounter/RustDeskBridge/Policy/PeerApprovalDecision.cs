using System;

namespace VHDMounter.RustDeskBridge.Policy
{
    /// <summary>
    /// SnapshotStore 内存查表得出的本地审批决定。
    ///
    /// SessionStateMachine 在 PeerApproval 帧通过 HMAC 校验后调用
    /// <see cref="SnapshotStore.Evaluate"/> 取 PeerApprovalDecision，并按
    /// design §"路径 3：PeerApproval 本地查表" 把它映射成线上的
    /// <see cref="VHDMounter.RustDeskBridge.Frames.PeerApprovalResponse"/>。
    ///
    /// 决策点 3：始终省略 reason —— 本类型不携带任何用户可见 reason 字段。
    /// </summary>
    internal readonly struct PeerApprovalDecision
    {
        private PeerApprovalDecision(bool approved)
        {
            IsApproved = approved;
        }

        public bool IsApproved { get; }

        public static PeerApprovalDecision Approve() => new PeerApprovalDecision(true);
        public static PeerApprovalDecision Reject() => new PeerApprovalDecision(false);
    }
}
