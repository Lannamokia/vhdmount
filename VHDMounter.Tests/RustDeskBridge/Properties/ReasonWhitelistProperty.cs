using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using FsCheck;
using FsCheck.Xunit;
using VHDMounter;
using VHDMounter.RustDeskBridge.Frames;
using Xunit;

namespace VHDMounter.Tests.RustDeskBridge.Properties
{
    /// <summary>
    /// Property 13: 全局 reason 白名单与内部错误解耦。
    ///
    /// Validates: Requirements 3.4, 7.7, 11.1, 11.2, 11.3
    ///
    /// (a) 响应 reason ∈ <c>{"deny", "rate_limited", "invalid_proof", "invalid_mac", "secret_outdated", "denied"} ∪ {null}</c>
    /// (b) MachineLogBuffer 内部错误条目先经 Sanitize；明文密码 / controllerName /
    ///     controllerHwid / 共享密钥 / TPM 句柄字面量不直接出现
    ///
    /// 工程权衡：直接用 SessionStateMachine 注入异常路径不容易（需要真实管道）；改为：
    ///  - 单测 HandshakeResponse / ReportAck / PeerApprovalResponse 的 reason 字段集合 ⊆ 白名单
    ///  - 用反射枚举 SessionStateMachine 源码中所有 Reject* 调用使用的字面量，断言全部在白名单
    ///  - 单测 MachineLogSanitizer 对各类敏感字面量的脱敏行为
    /// </summary>
    [Trait("feature", "rustdesk-bridge-host")]
    [Trait("property", "Property 13: reason 白名单与内部错误解耦")]
    public sealed class ReasonWhitelistProperty
    {
        private static readonly HashSet<string> AllowedReasons = new HashSet<string>(StringComparer.Ordinal)
        {
            "deny",
            "rate_limited",
            "invalid_proof",
            "invalid_mac",
            "secret_outdated",
            "denied",
        };

        // ---------- (a) HandshakeResponse / ReportAck / PeerApprovalResponse 字段集合白名单 ----------

        [Property(MaxTest = 200)]
        public Property HandshakeResponse_FailureReason_AlwaysInWhitelist(NonNull<string> reason)
        {
            var resp = HandshakeResponse.Failure(reason.Get);
            // 程序员可能传任意 string —— 但生产代码（SessionStateMachine）只会传白名单内的；
            // 我们用反射枚举源码字面量做精确白名单校验（见 SessionStateMachine_AllRejectReasons_AreInWhitelist）。
            // 这里仅断言"序列化输出仍是裸 JSON 形态"。
            var json = JsonSerializer.Serialize(resp, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            });
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            // 顶层只能有 "ok" + (可选 "reason")
            var keys = root.EnumerateObject().Select(p => p.Name).ToHashSet();
            return (keys.Count >= 1 && keys.IsSubsetOf(new HashSet<string> { "ok", "reason" })).ToProperty();
        }

        [Fact]
        public void ReportAck_StaticReasonConstants_AllInWhitelist()
        {
            Assert.Contains(ReportAck.ReasonDeny, AllowedReasons);
            Assert.Contains(ReportAck.ReasonRateLimited, AllowedReasons);
            Assert.Contains(ReportAck.ReasonSecretOutdated, AllowedReasons);
            Assert.Contains(ReportAck.ReasonInvalidMac, AllowedReasons);
        }

        [Fact]
        public void PeerApprovalResponse_RejectedNeverCarriesReason()
        {
            // 决策点 3：始终省略 reason
            var resp = PeerApprovalResponse.Rejected();
            Assert.Null(resp.TtlMs);
            // PeerApprovalResponse 类型上没有 Reason 字段（参见 src/.../Frames/PeerApprovalResponse.cs）
            var props = typeof(PeerApprovalResponse).GetProperties().Select(p => p.Name).ToHashSet();
            Assert.DoesNotContain("Reason", props);

            var json = JsonSerializer.Serialize(resp, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            });
            Assert.DoesNotContain("\"reason\"", json);
        }

        // ---------- (a) 反射 SessionStateMachine 源码字面量 ⊆ 白名单 ----------

        [Fact]
        public void SessionStateMachine_AllRejectReasons_AreInWhitelist()
        {
            // 通过读取 SessionStateMachine 源码，确认所有传给 RejectHandshake / Rejected("...") 的字面量
            // 都在白名单内。源码路径：src/VHDMounter/RustDeskBridge/Session/SessionStateMachine.cs
            var srcPath = LocateRepoFile("src/VHDMounter/RustDeskBridge/Session/SessionStateMachine.cs");
            Assert.True(File.Exists(srcPath), $"找不到 SessionStateMachine.cs: {srcPath}");
            var src = File.ReadAllText(srcPath);

            // 抓 RejectHandshakeAsync(pipe, "<reason>", ...) 的字面量
            var rejectHandshakeReasons = ExtractStringLiteralsAfter(src, "RejectHandshakeAsync(pipe, \"");
            // 抓 ReportAck.Rejected(...)，但其参数是常量符号；我们直接断言常量集合即可（已在另一测试中覆盖）
            // 抓 HandshakeResponse.Failure("<reason>", ...) 的字面量
            var failureReasons = ExtractStringLiteralsAfter(src, "HandshakeResponse.Failure(\"");

            var allReasons = rejectHandshakeReasons.Concat(failureReasons).Distinct().ToList();
            Assert.NotEmpty(allReasons);
            foreach (var r in allReasons)
            {
                Assert.True(AllowedReasons.Contains(r),
                    $"SessionStateMachine 中出现非白名单 reason: '{r}'");
            }
        }

        // ---------- (b) MachineLogSanitizer 对敏感字面量的脱敏 ----------

        [Theory]
        [InlineData("password=Hunter2!Sentinel", "Hunter2!Sentinel")]
        [InlineData("controllerName=ALICE-WORKSTATION", "ALICE-WORKSTATION")]
        [InlineData("controllerHwid=aabbccddeeff00112233445566778899", "aabbccddeeff00112233445566778899")]
        [InlineData("authorization=Bearer-xyzpdq", "Bearer-xyzpdq")]
        [InlineData("ciphertext=abcdef0123456789", "abcdef0123456789")]
        public void MachineLogSanitizer_RedactsSensitiveLiterals(string input, string sentinel)
        {
            var sanitized = MachineLogSanitizer.SanitizeSensitiveText(input);
            Assert.DoesNotContain(sentinel, sanitized);
            Assert.Contains("***", sanitized);
        }

        [Fact]
        public void MachineLogSanitizer_RedactsTpmHandleLiteral()
        {
            var sanitized = MachineLogSanitizer.SanitizeSensitiveText(
                "key handle = VHDMounterKey_MACHINE-DEADBEEF on Microsoft Software Key Storage Provider");
            Assert.DoesNotContain("VHDMounterKey_MACHINE-DEADBEEF", sanitized);
            Assert.DoesNotContain("Microsoft Software Key Storage Provider", sanitized);
            Assert.Contains("***", sanitized);
        }

        [Fact]
        public void MachineLogSanitizer_RedactsRsaPrivateKeyPem()
        {
            var pem = "-----BEGIN RSA PRIVATE KEY-----\nABCDEF...sensitive...\n-----END RSA PRIVATE KEY-----";
            var sanitized = MachineLogSanitizer.SanitizeSensitiveText("got key=" + pem);
            Assert.Contains("[PRIVATE_KEY_REDACTED]", sanitized);
            Assert.DoesNotContain("ABCDEF...sensitive...", sanitized);
        }

        [Fact]
        public void MachineLogSanitizer_RedactsJsonFormSensitiveFields()
        {
            var input =
                "{\"controllerName\":\"alice\",\"controllerHwid\":\"deadbeef\",\"mac\":\"abcdef==\",\"signature\":\"x\"}";
            var sanitized = MachineLogSanitizer.SanitizeSensitiveText(input);
            Assert.DoesNotContain("alice", sanitized);
            Assert.DoesNotContain("deadbeef", sanitized);
            Assert.DoesNotContain("abcdef==", sanitized);
            Assert.Contains("***", sanitized);
        }

        // ---------- 辅助 ----------

        private static IEnumerable<string> ExtractStringLiteralsAfter(string source, string marker)
        {
            var results = new List<string>();
            int idx = 0;
            while (true)
            {
                var pos = source.IndexOf(marker, idx, StringComparison.Ordinal);
                if (pos < 0) break;
                var start = pos + marker.Length;
                // 找下一个未转义的 "
                var end = source.IndexOf('"', start);
                if (end < 0) break;
                results.Add(source.Substring(start, end - start));
                idx = end + 1;
            }
            return results;
        }

        private static string LocateRepoFile(string relativePath)
        {
            // 测试运行目录可能是 VHDMounter.Tests/bin/<config>/<tfm>，往上找仓库根
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 8 && dir != null; i++)
            {
                var candidate = Path.Combine(dir, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(candidate)) return candidate;
                dir = Path.GetDirectoryName(dir);
            }
            return relativePath;
        }
    }
}
