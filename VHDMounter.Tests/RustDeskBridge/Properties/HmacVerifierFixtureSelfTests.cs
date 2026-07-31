using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using VHDMounter.RustDeskBridge.Crypto;
using Xunit;

namespace VHDMounter.Tests.RustDeskBridge.Properties
{
    /// <summary>
    /// 用 test-fixtures/protocol-vectors.json 锁字节做 5 帧 HMAC 输入字节构造 + base64 mac 双向自检。
    /// 任务 4.1 验收标准的核心覆盖：协议文档 §13 测试向量与 C# 端 HmacVerifier 实现逐字节相等。
    /// </summary>
    [Trait("feature", "rustdesk-bridge-host")]
    [Trait("property", "Property 3: HMAC 输入构造逐字节相等")]
    public sealed class HmacVerifierFixtureSelfTests
    {
        private static byte[] FixtureSecret =>
            Convert.FromHexString("abababababababababababababababababababababababababababababababab");

        private static JsonElement LoadFixture()
        {
            var fixturePath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..",
                "test-fixtures", "protocol-vectors.json"));
            var json = File.ReadAllText(fixturePath, Encoding.UTF8);
            return JsonDocument.Parse(json).RootElement.Clone();
        }

        [Fact]
        public void Handshake_HmacInput_BytesMatchFixture()
        {
            var v = LoadFixture().GetProperty("vectors").GetProperty("handshake");
            var frame = v.GetProperty("frame");

            var actual = HmacVerifier.BuildHandshakeHmacInput(
                frame.GetProperty("secretVersion").GetUInt32(),
                frame.GetProperty("nonce").GetString(),
                frame.GetProperty("timestampMs").GetInt64());

            var expectedAscii = v.GetProperty("hmacInputAscii").GetString();
            Assert.Equal(Encoding.ASCII.GetBytes(expectedAscii), actual);

            var mac = HmacVerifier.ComputeMacBase64WithKey(FixtureSecret, actual);
            Assert.Equal(v.GetProperty("proofBase64").GetString(), mac);
        }

        [Fact]
        public void Report_HmacInput_BytesMatchFixture()
        {
            var v = LoadFixture().GetProperty("vectors").GetProperty("report");
            var frame = v.GetProperty("frame");

            var actual = HmacVerifier.BuildReportHmacInput(
                frame.GetProperty("secretVersion").GetUInt32(),
                frame.GetProperty("rustDeskId").GetString(),
                frame.GetProperty("passwordKind").GetString(),
                frame.GetProperty("password").GetString(),
                frame.GetProperty("reason").GetString(),
                frame.GetProperty("reportedAt").GetInt64(),
                frame.GetProperty("nonce").GetString());

            Assert.Equal(Encoding.ASCII.GetBytes(v.GetProperty("hmacInputAscii").GetString()), actual);

            var mac = HmacVerifier.ComputeMacBase64WithKey(FixtureSecret, actual);
            Assert.Equal(v.GetProperty("macBase64").GetString(), mac);
        }

        [Fact]
        public void Log_HmacInput_BytesMatchFixture()
        {
            var v = LoadFixture().GetProperty("vectors").GetProperty("log");
            var frame = v.GetProperty("frame");

            var actual = HmacVerifier.BuildLogHmacInput(
                frame.GetProperty("secretVersion").GetUInt32(),
                frame.GetProperty("level").GetString(),
                frame.GetProperty("target").GetString(),
                frame.GetProperty("message").GetString(),
                frame.GetProperty("timestampMs").GetInt64());

            Assert.Equal(Encoding.ASCII.GetBytes(v.GetProperty("hmacInputAscii").GetString()), actual);
            Assert.Equal(v.GetProperty("macBase64").GetString(),
                HmacVerifier.ComputeMacBase64WithKey(FixtureSecret, actual));
        }

        [Fact]
        public void PeerApproval_HmacInput_BytesMatchFixture()
        {
            var v = LoadFixture().GetProperty("vectors").GetProperty("peerApproval");
            var frame = v.GetProperty("frame");

            var actual = HmacVerifier.BuildPeerApprovalHmacInput(
                frame.GetProperty("secretVersion").GetUInt32(),
                frame.GetProperty("controlledMachineId").GetString(),
                frame.GetProperty("controllerId").GetString(),
                frame.GetProperty("controllerName").GetString(),
                frame.GetProperty("controllerPlatform").GetString(),
                frame.GetProperty("controllerHwid").GetString(),
                frame.GetProperty("peerSocketAddr").GetString(),
                frame.GetProperty("connectionType").GetString(),
                frame.GetProperty("requestNonce").GetString(),
                frame.GetProperty("timestampMs").GetInt64());

            Assert.Equal(Encoding.ASCII.GetBytes(v.GetProperty("hmacInputAscii").GetString()), actual);
            Assert.Equal(v.GetProperty("macBase64").GetString(),
                HmacVerifier.ComputeMacBase64WithKey(FixtureSecret, actual));
        }

        [Fact]
        public void Revocation_HmacInput_BytesMatchFixture()
        {
            var v = LoadFixture().GetProperty("vectors").GetProperty("revocation");
            var frame = v.GetProperty("frame");

            var actual = HmacVerifier.BuildRevocationHmacInput(
                frame.GetProperty("secretVersion").GetUInt32(),
                frame.GetProperty("reason").GetString(),
                frame.GetProperty("issuedAt").GetInt64());

            Assert.Equal(Encoding.ASCII.GetBytes(v.GetProperty("hmacInputAscii").GetString()), actual);
            Assert.Equal(v.GetProperty("macBase64").GetString(),
                HmacVerifier.ComputeMacBase64WithKey(FixtureSecret, actual));
        }

        [Fact]
        public void VerifyMacBase64_FlippedByte_RejectsInConstantTime()
        {
            var v = LoadFixture().GetProperty("vectors").GetProperty("handshake");
            var frame = v.GetProperty("frame");
            var input = HmacVerifier.BuildHandshakeHmacInput(
                frame.GetProperty("secretVersion").GetUInt32(),
                frame.GetProperty("nonce").GetString(),
                frame.GetProperty("timestampMs").GetInt64());

            var validBase64 = v.GetProperty("proofBase64").GetString();
            var validBytes = Convert.FromBase64String(validBase64);
            var verifier = new HmacVerifier(new FixtureSecretProvider(1, FixtureSecret));

            // 翻转 mac 第 0 字节
            validBytes[0] ^= 0x01;
            var tampered = Convert.ToBase64String(validBytes);

            Assert.False(verifier.VerifyMacBase64(input, tampered));
            // 还原后应通过
            validBytes[0] ^= 0x01;
            Assert.True(verifier.VerifyMacBase64(input, Convert.ToBase64String(validBytes)));
        }

        private sealed class FixtureSecretProvider : IBridgeSecretProvider
        {
            private readonly byte[] _secret;
            public FixtureSecretProvider(uint version, byte[] secret)
            {
                CurrentSecretVersion = version;
                _secret = secret;
            }
            public uint CurrentSecretVersion { get; }
            public ReadOnlySpan<byte> GetActiveSecret() => _secret;
        }
    }
}
