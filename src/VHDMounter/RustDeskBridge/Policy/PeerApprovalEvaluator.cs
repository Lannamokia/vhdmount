using System;
using VHDMounter.RustDeskBridge.Frames;

namespace VHDMounter.RustDeskBridge.Policy
{
    /// <summary>
    /// Requirement 7.5 / 7.6 / 7.7 / 8.5 / 8.9 / 8.10：PeerApproval 帧 → 本地查表决定。
    ///
    /// 逻辑：
    /// 1. <see cref="IRegistrationGate.IsRegisteredAndApproved"/> == false → 直接 Rejected（§8.9）
    /// 2. <c>frame.controlledMachineId != thisMachineId</c> → Rejected（§7.5）
    /// 3. 调 <see cref="SnapshotStore.Evaluate"/> → 命中 Approved(ttlMs:1)；未命中 Rejected
    ///
    /// 本类**不**缓存"上一次决定"（§8.10）—— 同 (controllerId, peerSocketAddr) 在快照不变期内
    /// 被多次询问时，每次都重新走一遍 SnapshotStore.Evaluate。
    ///
    /// reason 字段一律省略（决策点 3 / Requirement 7.7）。
    /// </summary>
    internal sealed class PeerApprovalEvaluator
    {
        private readonly SnapshotStore _snapshots;
        private readonly IRegistrationGate _registrationGate;

        public PeerApprovalEvaluator(SnapshotStore snapshots, IRegistrationGate registrationGate)
        {
            _snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
            _registrationGate = registrationGate ?? throw new ArgumentNullException(nameof(registrationGate));
        }

        public PeerApprovalResponse Evaluate(PeerApprovalFrame frame, string thisMachineId)
        {
            if (frame == null)
            {
                return PeerApprovalResponse.Rejected();
            }

            // §8.9：未注册 / 未审批期间一律 rejected
            if (!_registrationGate.IsRegisteredAndApproved)
            {
                return PeerApprovalResponse.Rejected();
            }

            // §7.5：machineId 不匹配 → rejected（不携带 reason）
            if (!string.Equals(frame.ControlledMachineId ?? string.Empty,
                               thisMachineId ?? string.Empty,
                               StringComparison.Ordinal))
            {
                return PeerApprovalResponse.Rejected();
            }

            // §8.5 / §8.10：直接走 SnapshotStore.Evaluate，不缓存决定
            var decision = _snapshots.Evaluate(
                frame.ControllerId ?? string.Empty,
                frame.ControllerHwid ?? string.Empty,
                thisMachineId ?? string.Empty);

            return decision.IsApproved
                ? PeerApprovalResponse.Approved(SnapshotStore.ApprovalTtlMs)
                : PeerApprovalResponse.Rejected();
        }
    }
}
