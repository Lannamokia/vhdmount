using System;

namespace VHDMounter.RustDeskBridge.Crypto
{
    /// <summary>
    /// 把"取当前 active 的 RustDeskClientSharedSecret"这件事抽到 HmacVerifier 之外。
    /// HmacVerifier 不直接持有密钥字段（任务 4.1 / Requirement 13.5），由
    /// <see cref="BridgeSecretClient"/> 等运行期组件实现该接口注入活字节。
    /// </summary>
    internal interface IBridgeSecretProvider
    {
        /// <summary>
        /// 当前 VHDMount 接受的 secretVersion（与协议文档 §1.2 / §3 一致）。
        /// </summary>
        uint CurrentSecretVersion { get; }

        /// <summary>
        /// 拿到当前 active 的 32 字节 RustDeskClientSharedSecret 的只读副本。
        /// 实现 SHALL 在每次返回前确认密钥已加载，否则抛 <see cref="InvalidOperationException"/>。
        /// 调用方使用完毕**不**保留长期副本（Requirement 13.5）。
        /// </summary>
        ReadOnlySpan<byte> GetActiveSecret();
    }
}
