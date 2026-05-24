using System;

namespace VHDMounter.RustDeskBridge.Policy
{
    /// <summary>
    /// 用 Bridge_Policy_Signing_Pubkey 校验服务端响应签名（RSA-PKCS1-SHA256）。
    /// 由 <see cref="PolicyPubkeyClient"/> 实现，给 SnapshotStore /
    /// BridgeSecretClient / WrapKeyClient 共用（任务 6.1 / Requirement 8.3.1 / 15.10）。
    /// </summary>
    internal interface IPolicyPubkeyValidator
    {
        /// <summary>
        /// 当前已加载的 PEM 字节摘要（小写十六进制），用于 §8.6.3
        /// "运行期检测到 PEM 与配置值不同就立即作废 Snapshot" 的诊断比对。
        /// </summary>
        string CurrentPubkeyDigestHex { get; }

        /// <summary>
        /// 用当前 active 的 Bridge_Policy_Signing_Pubkey 校验对 <paramref name="payload"/> 字节的
        /// RSA-PKCS1-v1_5 + SHA-256 签名是否合法。
        /// 任何解码 / 格式 / 校验异常一律返回 false（与 mac 不匹配同码）。
        /// </summary>
        bool VerifyResponseSignature(ReadOnlySpan<byte> payload, string signatureBase64);
    }
}
