using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FsCheck;
using FsCheck.Xunit;
using VHDMounter.RustDeskBridge.Upload;
using Xunit;

namespace VHDMounter.Tests.RustDeskBridge.Properties
{
    /// <summary>
    /// Property 8: 上行请求签名构造（任务 3.3）
    /// Validates: Requirements 6.8, 16.1, 16.2
    ///
    /// 用 FsCheck 生成 (machineId, keyId, host, timestampMs, nonce, bodyJson) 任意组合，
    /// 断言四类 SignXxx 构造的 payload 字节序列等于显式 \n 拼接结果；与既有
    /// DeployRequestSigner 测试套同结构。
    /// </summary>
    [Trait("feature", "rustdesk-bridge-host")]
    [Trait("property", "Property 8: 上行请求签名构造")]
    public sealed class RequestSignaturePayloadProperty
    {
        private static readonly string[] AllowedPayloadVersions =
        {
            RustDeskReportSigner.ReportPayloadVersion,
            RustDeskReportSigner.SnapshotFetchPayloadVersion,
            RustDeskReportSigner.WrapKeyPayloadVersion,
            RustDeskReportSigner.PolicyPubkeyFetchPayloadVersion,
            RustDeskReportSigner.BridgeSecretFetchPayloadVersion,
        };

        [Property(MaxTest = 200)]
        public Property PayloadIsExactlyNineLineLfJoinedAscii(
            NonEmptyString machineId,
            NonEmptyString host,
            long timestampMs,
            NonEmptyString nonce,
            string bodyJson)
        {
            return Prop.ForAll(
                Arb.From(Gen.Elements(AllowedPayloadVersions)),
                Arb.From(Gen.Elements("GET", "POST", "PUT", "DELETE", "PATCH")),
                Arb.From(Gen.OneOf(
                    Gen.Constant("/api/test"),
                    Gen.Constant("/api/machines/abc/rustdesk/report"),
                    Gen.Constant("/"))),
                (payloadVersion, method, path) =>
                {
                    var keyId = RustDeskReportSigner.BuildDefaultKeyId(machineId.Get);

                    var actualPayload = RustDeskReportSigner.BuildSigningPayload(
                        payloadVersion,
                        machineId.Get,
                        keyId,
                        method,
                        path,
                        host.Get,
                        timestampMs.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        nonce.Get,
                        RustDeskReportSigner.ComputeSha256Hex(bodyJson ?? string.Empty));

                    // 重算"标准答案"
                    var bodyHash = RustDeskReportSigner.ComputeSha256Hex(bodyJson ?? string.Empty);
                    var hostNoPort = host.Get.Split(':')[0];
                    var expected = string.Join("\n", new[]
                    {
                        payloadVersion,
                        machineId.Get.Trim(),
                        keyId.Trim(),
                        method.ToUpperInvariant(),
                        path,
                        hostNoPort,
                        timestampMs.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        nonce.Get,
                        bodyHash,
                    });

                    return string.Equals(actualPayload, expected, StringComparison.Ordinal);
                });
        }

        [Property(MaxTest = 50)]
        public Property PayloadIsAsciiOnly_NoTrailingNewline(
            NonEmptyString machineId,
            NonEmptyString host,
            long timestampMs,
            NonEmptyString nonce)
        {
            return Prop.ForAll(
                Arb.From(Gen.Elements(AllowedPayloadVersions)),
                payloadVersion =>
                {
                    var keyId = RustDeskReportSigner.BuildDefaultKeyId(machineId.Get);
                    var p = RustDeskReportSigner.BuildSigningPayload(
                        payloadVersion, machineId.Get, keyId, "GET", "/api/x", host.Get,
                        timestampMs.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        nonce.Get,
                        RustDeskReportSigner.ComputeSha256Hex(string.Empty));

                    if (p.EndsWith("\n", StringComparison.Ordinal)) return false;
                    var bytes = Encoding.UTF8.GetBytes(p);
                    return bytes.Length == p.Length; // ASCII-only 等价于 UTF-8 字节数 == 字符数
                });
        }

        [Fact]
        public void EmptyBodyHash_IsRfcConstantE3B0()
        {
            // sha256("") 的固定常量
            var expected = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
            var actual = RustDeskReportSigner.ComputeSha256Hex(string.Empty);
            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData(RustDeskReportSigner.ReportPayloadVersion)]
        [InlineData(RustDeskReportSigner.SnapshotFetchPayloadVersion)]
        [InlineData(RustDeskReportSigner.WrapKeyPayloadVersion)]
        [InlineData(RustDeskReportSigner.PolicyPubkeyFetchPayloadVersion)]
        [InlineData(RustDeskReportSigner.BridgeSecretFetchPayloadVersion)]
        public void Payload_FirstLine_IsExactlyVersionLiteral(string payloadVersion)
        {
            var p = RustDeskReportSigner.BuildSigningPayload(
                payloadVersion, "M1", "KID", "GET", "/api/x", "host.example",
                "1730000000000", "abcd",
                RustDeskReportSigner.ComputeSha256Hex(string.Empty));
            var firstLine = p.Split('\n')[0];
            Assert.Equal(payloadVersion, firstLine);
        }

        [Fact]
        public void HostWithPort_StrippedToBareHost()
        {
            var p = RustDeskReportSigner.BuildSigningPayload(
                RustDeskReportSigner.ReportPayloadVersion,
                "M1", "KID", "POST", "/api/x", "host.example:8443",
                "1730000000000", "deadbeef",
                RustDeskReportSigner.ComputeSha256Hex(""));
            var lines = p.Split('\n');
            Assert.Equal("host.example", lines[5]);
        }
    }
}
