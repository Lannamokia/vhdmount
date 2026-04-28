using System;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace VHDMounter.SoftwareDeploy
{
    internal static class DeployRequestSigner
    {
        private const string SigningPayloadVersion = "VHDMountDeploymentRequestV1";

        public static string BuildDefaultKeyId(string machineId)
        {
            return $"VHDMounterKey_{machineId}";
        }

        public static void Sign(HttpRequestMessage request, string machineId, string keyId, string bodyJson = "")
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (request.RequestUri == null)
            {
                throw new InvalidOperationException("部署请求缺少目标地址");
            }

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
            var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            var bodyHash = ComputeSha256Hex(bodyJson ?? string.Empty);
            var payload = string.Join("\n", new[]
            {
                SigningPayloadVersion,
                machineId.Trim(),
                keyId.Trim(),
                request.Method.Method.ToUpperInvariant(),
                request.RequestUri.AbsolutePath,
                timestamp,
                nonce,
                bodyHash,
            });

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

        private static string ComputeSha256Hex(string text)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
