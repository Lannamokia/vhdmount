using System;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace VHDMounter.RustDeskBridge.Upload
{
    /// <summary>
    /// RustDesk 桥相关上行 HTTP 请求的 RSA-PKCS1-SHA256 签名器，沿用
    /// <see cref="VHDMounter.SoftwareDeploy.DeployRequestSigner"/> 完全同构的 9 行 \n
    /// 拼接 ASCII 串 + 四个 X-VHDM-* 请求头格式。
    ///
    /// 唯一不同：首行字面量按调用入口不同分为 5 种，绝不串用：
    /// - <see cref="ReportPayloadVersion"/> 用于 <c>POST /rustdesk/report</c>（Requirement 6.8）
    /// - <see cref="SnapshotFetchPayloadVersion"/> 用于 <c>GET /rustdesk/trusted-controllers</c>（Requirement 15.5）
    /// - <see cref="WrapKeyPayloadVersion"/> 用于 <c>POST /rustdesk/wrap-key</c>（Requirement 15.6）
    /// - <see cref="PolicyPubkeyFetchPayloadVersion"/> 用于 <c>GET /rustdesk/policy-pubkey</c>（决策点 7）
    /// - <see cref="BridgeSecretFetchPayloadVersion"/> 用于 <c>GET /rustdesk/bridge-secret</c>（决策点 1）
    ///
    /// 每次签名前调用 <see cref="VHDManager.EnsureOrCreateTpmRsa"/> 取新 RSACng → 立即 Dispose
    /// （Requirement 16.6 / 16.7）；不缓存共享 TPM 句柄。
    /// </summary>
    internal static class RustDeskReportSigner
    {
        public const string ReportPayloadVersion = "VHDMounterRustDeskReportV1";
        public const string SnapshotFetchPayloadVersion = "VHDMounterTrustedControllersFetchV1";
        public const string WrapKeyPayloadVersion = "VHDMounterWrapKeyV1";
        public const string PolicyPubkeyFetchPayloadVersion = "VHDMounterPolicyPubkeyFetchV1";
        public const string BridgeSecretFetchPayloadVersion = "VHDMounterBridgeSecretFetchV1";

        /// <summary>
        /// 与 <see cref="VHDMounter.SoftwareDeploy.DeployRequestSigner.BuildDefaultKeyId"/> 一致：
        /// <c>VHDMounterKey_&lt;machineId&gt;</c>。
        /// </summary>
        public static string BuildDefaultKeyId(string machineId)
        {
            return $"VHDMounterKey_{machineId}";
        }

        public static void SignReport(HttpRequestMessage request, string machineId, string keyId, string bodyJson)
            => Sign(request, ReportPayloadVersion, machineId, keyId, bodyJson ?? string.Empty);

        public static void SignSnapshotFetch(HttpRequestMessage request, string machineId, string keyId)
            => Sign(request, SnapshotFetchPayloadVersion, machineId, keyId, string.Empty);

        public static void SignWrapKeyFetch(HttpRequestMessage request, string machineId, string keyId)
            => Sign(request, WrapKeyPayloadVersion, machineId, keyId, string.Empty);

        public static void SignPolicyPubkeyFetch(HttpRequestMessage request, string machineId, string keyId)
            => Sign(request, PolicyPubkeyFetchPayloadVersion, machineId, keyId, string.Empty);

        public static void SignBridgeSecretFetch(HttpRequestMessage request, string machineId, string keyId)
            => Sign(request, BridgeSecretFetchPayloadVersion, machineId, keyId, string.Empty);

        /// <summary>
        /// 暴露的低级签名 API，仅给测试与跨语言对照夹具使用。生产代码请走 SignXxx 入口。
        /// </summary>
        public static string BuildSigningPayload(
            string payloadVersion,
            string machineId,
            string keyId,
            string method,
            string absolutePath,
            string host,
            string timestampMs,
            string nonce,
            string bodyHashHex)
        {
            return string.Join("\n", new[]
            {
                payloadVersion,
                (machineId ?? string.Empty).Trim(),
                (keyId ?? string.Empty).Trim(),
                (method ?? string.Empty).ToUpperInvariant(),
                absolutePath ?? string.Empty,
                (host ?? string.Empty).Split(':')[0],
                timestampMs ?? string.Empty,
                nonce ?? string.Empty,
                bodyHashHex ?? string.Empty,
            });
        }

        private static void Sign(
            HttpRequestMessage request,
            string payloadVersion,
            string machineId,
            string keyId,
            string bodyJson)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.RequestUri == null)
            {
                throw new InvalidOperationException("RustDesk 桥请求缺少目标地址");
            }

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                .ToString(CultureInfo.InvariantCulture);
            var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            var bodyHash = ComputeSha256Hex(bodyJson);
            var payload = BuildSigningPayload(
                payloadVersion,
                machineId,
                keyId,
                request.Method.Method,
                request.RequestUri.AbsolutePath,
                request.RequestUri.Host,
                timestamp,
                nonce,
                bodyHash);

            using var rsa = VHDManager.EnsureOrCreateTpmRsa(machineId);
            var signatureBytes = rsa.SignData(
                Encoding.UTF8.GetBytes(payload),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            request.Headers.Remove("X-VHDM-KeyId");
            request.Headers.Remove("X-VHDM-Timestamp");
            request.Headers.Remove("X-VHDM-Nonce");
            request.Headers.Remove("X-VHDM-Signature");
            request.Headers.Add("X-VHDM-KeyId", keyId);
            request.Headers.Add("X-VHDM-Timestamp", timestamp);
            request.Headers.Add("X-VHDM-Nonce", nonce);
            request.Headers.Add("X-VHDM-Signature", Convert.ToBase64String(signatureBytes));
        }

        public static string ComputeSha256Hex(string text)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
