using VHDMounter;

namespace VHDMounter.RustDeskBridge.Policy
{
    /// <summary>
    /// Requirement 8.9 / 16.4 / 16.5：本 feature 所有"是否对 VHDSelectServer 发起业务请求 /
    /// 是否处理本机 PeerApproval"判断都收敛到这个单方法接口，便于测试夹具替换
    /// （生产实现 <see cref="MachineKeyRegistrationGate"/> 直接代理
    /// <see cref="MachineKeyRegistration.IsRegisteredAndApproved"/>）。
    /// </summary>
    internal interface IRegistrationGate
    {
        /// <summary>
        /// 当前进程是否已完成机台 X.509 注册并被管理员审批。
        /// false 期间：SnapshotRefreshLoop 跳过拉取、PeerApprovalEvaluator 一律 rejected
        /// （不假设任何"默认放行"行为）。
        /// </summary>
        bool IsRegisteredAndApproved { get; }
    }

    /// <summary>
    /// 生产实现：直接代理静态属性
    /// <see cref="MachineKeyRegistration.IsRegisteredAndApproved"/>。
    /// </summary>
    internal sealed class MachineKeyRegistrationGate : IRegistrationGate
    {
        public static readonly MachineKeyRegistrationGate Instance = new MachineKeyRegistrationGate();

        public bool IsRegisteredAndApproved => MachineKeyRegistration.IsRegisteredAndApproved;
    }
}
