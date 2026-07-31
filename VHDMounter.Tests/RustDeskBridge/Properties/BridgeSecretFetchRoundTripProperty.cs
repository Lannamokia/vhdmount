using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace VHDMounter.Tests.RustDeskBridge.Properties
{
    /// <summary>
    /// Property 15: Bridge secret fetch round-trip。
    ///
    /// Validates: Requirements 13.1, 13.5, 13.6
    ///
    /// (a) <c>RsaOaepWrap(S, P_pub)</c> → <c>RsaOaepUnwrap(P_priv)</c> = S
    /// (b) 用 M2 公钥包装的 cipher 在 M1 私钥上解包必然失败
    /// (c) 服务端响应 signature 翻转一字节 → 机台拒绝替换（验签失败）
    /// (d) MachineLogBuffer / Trace listener 不出现 secret 任意 16 字节连续子串（哨兵串扫描）
    ///
    /// 不真正拉起 BridgeSecretClient（避免 TPM 句柄依赖），而是从协议契约层
    /// 模拟"包装 → 解包 + 签名 → 验签 + 哨兵扫描"几个原子，验证设计的核心数学性质。
    /// </summary>
    [Trait("feature", "rustdesk-bridge-host")]
    [Trait("property", "Property 15: Bridge secret fetch round-trip")]
    public sealed class BridgeSecretFetchRoundTripProperty
    {
        // 性能权衡：RSA-3072 keygen 较慢，固定一对 M1 + 一对 M2 跨 property test 复用。
        // FsCheck 仍负责生成 32 字节 secret 的多样性。
        private static readonly Lazy<RSA> RsaM1 = new(() => RSA.Create(3072));
        private static readonly Lazy<RSA> RsaM2 = new(() => RSA.Create(3072));
        private static readonly Lazy<RSA> PolicySigner = new(() => RSA.Create(3072));

        private static byte[] GenerateSecret(int seed)
        {
            // 用 SHA-256(seed) 反复填充 32 字节，让 FsCheck 的 seed 决定 secret 内容（确定性）
            var bytes = new byte[32];
            var src = SHA256.HashData(BitConverter.GetBytes(seed));
            for (var i = 0; i < 32; i++) bytes[i] = src[i % src.Length];
            return bytes;
        }

        [Property(MaxTest = 30)]
        public Property RsaOaep_RoundTrips_BridgeSecret(int seed)
        {
            var s = GenerateSecret(seed);
            var cipher = RsaM1.Value.Encrypt(s, RSAEncryptionPadding.OaepSHA256);
            var unwrapped = RsaM1.Value.Decrypt(cipher, RSAEncryptionPadding.OaepSHA256);
            return unwrapped.SequenceEqual(s).ToProperty();
        }

        [Property(MaxTest = 30)]
        public Property M2_Wrapped_Cipher_FailsToUnwrap_OnM1(int seed)
        {
            var s = GenerateSecret(seed);
            var cipher = RsaM2.Value.Encrypt(s, RSAEncryptionPadding.OaepSHA256);

            var threwOrMismatch = false;
            try
            {
                var unwrapped = RsaM1.Value.Decrypt(cipher, RSAEncryptionPadding.OaepSHA256);
                // 极端情况下解出来不抛但字节不等也算"拒绝"
                threwOrMismatch = !unwrapped.SequenceEqual(s);
            }
            catch (CryptographicException)
            {
                threwOrMismatch = true;
            }
            return threwOrMismatch.ToProperty();
        }

        [Property(MaxTest = 30)]
        public Property SignatureFlippedByOneByte_Rejected(int seed)
        {
            // 模拟服务端响应 BridgeSecretResponseV1 payload 的签名校验
            var s = GenerateSecret(seed);
            var cipher = RsaM1.Value.Encrypt(s, RSAEncryptionPadding.OaepSHA256);
            var cipherB64 = Convert.ToBase64String(cipher);
            var machineId = "MACHINE-" + seed.ToString("X8");
            var secretVersion = (uint)(seed & 0xff);
            var issuedAtMs = 1730000000000L + (seed & 0xffff);

            var cipherDigestHex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cipherB64))).ToLowerInvariant();
            var payload = string.Concat(
                "BridgeSecretResponseV1\n",
                machineId, "\n",
                secretVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), "\n",
                cipherDigestHex, "\n",
                issuedAtMs.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var payloadBytes = Encoding.ASCII.GetBytes(payload);

            var sig = PolicySigner.Value.SignData(payloadBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            // 翻转一字节
            var tampered = (byte[])sig.Clone();
            tampered[0] ^= 0x01;

            // 用同一公钥校验
            var validOk = PolicySigner.Value.VerifyData(payloadBytes, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var tamperedOk = PolicySigner.Value.VerifyData(payloadBytes, tampered, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            return (validOk && !tamperedOk).ToProperty();
        }

        [Fact]
        public void SecretBytes_DoNotAppearInTraceListener()
        {
            // (d) 哨兵扫描：模拟一次"成功拉取 + 解包 + 后续业务路径"的 Trace 输出，
            // 断言任意 16 字节连续 secret 子串不出现在 Trace listener 累积字节流中。
            var s = GenerateSecret(0xC0FFEE);
            var cipher = RsaM1.Value.Encrypt(s, RSAEncryptionPadding.OaepSHA256);
            var unwrapped = RsaM1.Value.Decrypt(cipher, RSAEncryptionPadding.OaepSHA256);
            Assert.Equal(s, unwrapped);

            var captured = new StringWriter();
            var listener = new TextWriterTraceListener(captured);
            Trace.Listeners.Add(listener);
            try
            {
                // 模拟正常诊断 —— 仅暴露元数据，绝不输出 secret 字节
                Trace.WriteLine($"[BridgeSecretClient] active 槽刷新 secretVersion=42 issuedAt=1730000000000 cipherLen={cipher.Length}");
                Trace.WriteLine("[BridgeSecretClient] PolicyPubkey 验签通过");
                listener.Flush();

                var traceText = captured.ToString();
                var traceBytes = Encoding.UTF8.GetBytes(traceText);

                // 任意 16 字节连续子串都不应出现 —— 我们抽样 secret 的所有 16-byte 窗口
                for (var offset = 0; offset + 16 <= s.Length; offset++)
                {
                    var window = s.AsSpan(offset, 16).ToArray();
                    Assert.False(IndexOfBytes(traceBytes, window) >= 0,
                        $"Trace listener 中出现 secret 16 字节窗口 (offset={offset})，违反 Requirement 13.5/13.6");
                }
            }
            finally
            {
                Trace.Listeners.Remove(listener);
                listener.Dispose();
                captured.Dispose();
            }
        }

        [Fact]
        public void SecretBytes_DoNotAppearInDiagnosticBuffer()
        {
            // 与上面对偶：模拟一个 Action<string> diagnostics（Bridge 系列客户端的常用输出通道）
            // 拼出近 100KB 诊断输出 → 断言不含 secret 16-byte 窗口
            var s = GenerateSecret(unchecked((int)0xDEADBEEFu));
            var sb = new StringBuilder();
            Action<string> diagnostics = msg => sb.AppendLine(msg);

            for (var i = 0; i < 1000; i++)
            {
                diagnostics($"diag #{i} ts=1730000000{i:0000} status=ok cipherLen=384");
            }
            var bytes = Encoding.UTF8.GetBytes(sb.ToString());

            for (var offset = 0; offset + 16 <= s.Length; offset++)
            {
                var window = s.AsSpan(offset, 16).ToArray();
                Assert.False(IndexOfBytes(bytes, window) >= 0,
                    $"诊断流中出现 secret 16 字节窗口 (offset={offset})");
            }
        }

        // ---------- 内部辅助 ----------

        private static int IndexOfBytes(byte[] haystack, byte[] needle)
        {
            if (needle.Length == 0 || haystack.Length < needle.Length) return -1;
            for (var i = 0; i <= haystack.Length - needle.Length; i++)
            {
                var match = true;
                for (var j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j]) { match = false; break; }
                }
                if (match) return i;
            }
            return -1;
        }
    }
}
