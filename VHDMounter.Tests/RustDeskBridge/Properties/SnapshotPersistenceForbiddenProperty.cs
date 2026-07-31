using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FsCheck;
using FsCheck.Xunit;
using VHDMounter.RustDeskBridge.Crypto;
using VHDMounter.RustDeskBridge.Json;
using VHDMounter.RustDeskBridge.Policy;
using Xunit;

namespace VHDMounter.Tests.RustDeskBridge.Properties
{
    /// <summary>
    /// Property 10: 快照不持久化 + In_Memory_Obfuscation round-trip。
    ///
    /// Validates: Requirements 8.7, 8.8
    ///
    /// 用 FsCheck 生成含哨兵 controllerId 的快照 → SnapshotStore.TryReplace + 多次
    /// Evaluate → 扫描磁盘文件系统 / Trace listener 两个最重要的入口（Windows 注册表 /
    /// Event Log 跨平台测试不便，单元测试覆盖 disk + Trace 即可，跨进程集成测试覆盖完整版）。
    ///
    /// 同时校验 In_Memory_Obfuscation Wrap → Unwrap 字节相等。
    /// </summary>
    [Trait("feature", "rustdesk-bridge-host")]
    [Trait("property", "Property 10: 快照不持久化与 In_Memory_Obfuscation round-trip")]
    public sealed class SnapshotPersistenceForbiddenProperty
    {
        private const string SentinelControllerId = "TRUSTED-SENTINEL-987654321";
        private const string SentinelControllerHwidHash =
            "deadbeefcafebabe0123456789abcdef0011223344556677889900aabbccddee";
        private const string SentinelAuditNote = "AUDIT-SENTINEL-SHOULD-NEVER-PERSIST";

        // 测试夹具：用一对 RSA 给"服务端 Bridge_Policy_Signing 私钥"角色，一份对应公钥给 SnapshotStore 验签
        private static readonly Lazy<RSA> PolicySigner = new(() => RSA.Create(2048));

        private sealed class FixedKeyValidator : IPolicyPubkeyValidator
        {
            public string CurrentPubkeyDigestHex { get; set; } = "fixture";
            public bool VerifyResponseSignature(ReadOnlySpan<byte> payload, string signatureBase64)
            {
                if (string.IsNullOrEmpty(signatureBase64)) return false;
                byte[] sig;
                try { sig = Convert.FromBase64String(signatureBase64); }
                catch (FormatException) { return false; }
                return PolicySigner.Value.VerifyData(payload.ToArray(), sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
        }

        private sealed class FakeClock : IClock
        {
            public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1730000000000L);
        }

        private static string BuildSignedSnapshotJson(
            string machineId, long snapshotSeq, long issuedAtMs, IEnumerable<string> controllerIds)
        {
            // entries 形如 [ {"controllerId":"...","controllerHwidHash":"...","scope":"global","enabled":true,"expiresAt":null,"label":"..."} ]
            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
            {
                writer.WriteStartArray();
                foreach (var cid in controllerIds)
                {
                    writer.WriteStartObject();
                    writer.WriteString("controllerId", cid);
                    writer.WriteNull("controllerHwidHash"); // 测试场景：不限定 hwid，命中所有 controllerHwid
                    writer.WriteString("scope", "global");
                    writer.WriteBoolean("enabled", true);
                    writer.WriteNull("expiresAt");
                    writer.WriteString("label", "lbl-" + cid);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }
            var entriesBytes = ms.ToArray();

            // 模拟服务端：先用 JCS 规范化 entries 数组，再算 sha256，再签 TrustedControllersSnapshotV1 payload
            var canonical = JcsCanonicalizer.Canonicalize(JsonDocument.Parse(entriesBytes).RootElement);
            var entriesDigestHex = Convert.ToHexString(SHA256.HashData(canonical)).ToLowerInvariant();
            var payload = string.Concat(
                "TrustedControllersSnapshotV1\n",
                machineId, "\n",
                snapshotSeq.ToString(CultureInfo.InvariantCulture), "\n",
                issuedAtMs.ToString(CultureInfo.InvariantCulture), "\n",
                entriesDigestHex);
            var sig = PolicySigner.Value.SignData(
                Encoding.ASCII.GetBytes(payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var sigBase64 = Convert.ToBase64String(sig);

            using var outer = new MemoryStream();
            using (var w = new Utf8JsonWriter(outer, new JsonWriterOptions
            {
                Indented = false,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }))
            {
                w.WriteStartObject();
                w.WriteString("machineId", machineId);
                w.WriteNumber("snapshotSeq", snapshotSeq);
                w.WriteNumber("issuedAt", issuedAtMs);
                // 写入 entries 时直接展开预生成的字节
                w.WritePropertyName("entries");
                using var entriesDoc = JsonDocument.Parse(entriesBytes);
                entriesDoc.RootElement.WriteTo(w);
                w.WriteString("signature", sigBase64);
                w.WriteEndObject();
            }
            return Encoding.UTF8.GetString(outer.ToArray());
        }

        // ---------- Property tests ----------

        [Fact]
        public void Wrap_Then_Unwrap_PreservesEntriesBytes()
        {
            using var io = new InMemoryObfuscation();
            var entriesPlain = Encoding.UTF8.GetBytes("[{\"controllerId\":\"" + SentinelControllerId + "\"}]");
            var wrapped = io.Wrap(entriesPlain);
            var roundtrip = io.Unwrap(wrapped);
            Assert.Equal(entriesPlain, roundtrip);
        }

        [Fact]
        public void TryReplace_ThenEvaluate_DoesNotLeakSentinelToTrace()
        {
            using var io = new InMemoryObfuscation();
            var clock = new FakeClock();
            var store = new SnapshotStore(io, clock);
            var validator = new FixedKeyValidator();

            var captured = new StringWriter();
            var listener = new TextWriterTraceListener(captured);
            Trace.Listeners.Add(listener);
            try
            {
                var snapshotJson = BuildSignedSnapshotJson(
                    machineId: "MACHINE-FIX",
                    snapshotSeq: 1,
                    issuedAtMs: clock.UtcNow.ToUnixTimeMilliseconds(),
                    controllerIds: new[] { SentinelControllerId });

                Assert.True(store.TryReplace(snapshotJson, validator, out var rejectReason),
                    $"TryReplace 应当成功，rejectReason={rejectReason} snapshot.length={snapshotJson.Length}");

                // 多次 Evaluate（命中 + 未命中混合）
                for (var i = 0; i < 50; i++)
                {
                    var hit = store.Evaluate(SentinelControllerId, "hwid-x", "MACHINE-FIX");
                    Assert.True(hit.IsApproved);
                    var miss = store.Evaluate("OTHER-" + i, "hwid-y", "MACHINE-FIX");
                    Assert.False(miss.IsApproved);
                }

                listener.Flush();
                var traceText = captured.ToString();
                Assert.DoesNotContain(SentinelControllerId, traceText);
                Assert.DoesNotContain(SentinelControllerHwidHash, traceText);
            }
            finally
            {
                Trace.Listeners.Remove(listener);
                listener.Dispose();
                captured.Dispose();
            }
        }

        [Fact]
        public void TryReplace_DoesNotPersistSentinel_OnDisk()
        {
            using var io = new InMemoryObfuscation();
            var clock = new FakeClock();
            var store = new SnapshotStore(io, clock);
            var validator = new FixedKeyValidator();

            var sandboxRoot = Path.Combine(Path.GetTempPath(),
                "vhdm-bridge-snapshot-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sandboxRoot);
            try
            {
                // 构造哨兵快照：controllerId / hwidHash / 服务端签名 base64 三个标记字符串
                var snapshotJson = BuildSignedSnapshotJson(
                    machineId: "MACHINE-SCAN",
                    snapshotSeq: 1,
                    issuedAtMs: clock.UtcNow.ToUnixTimeMilliseconds(),
                    controllerIds: new[] { SentinelControllerId });

                // 拿出签名 base64 用于扫描
                using var doc = JsonDocument.Parse(snapshotJson);
                var signatureBase64 = doc.RootElement.GetProperty("signature").GetString() ?? string.Empty;

                Assert.True(store.TryReplace(snapshotJson, validator, out _));

                for (var i = 0; i < 20; i++)
                {
                    store.Evaluate(SentinelControllerId, "hwid-x", "MACHINE-SCAN");
                }

                // 扫描"潜在持久化区域"：测试沙盒本身（生产代码不应当往任何地方写）
                // 与 AppContext.BaseDirectory（测试运行目录）。命中任一 → 失败。
                foreach (var rootDir in new[] { sandboxRoot, AppContext.BaseDirectory })
                {
                    foreach (var file in Directory.EnumerateFiles(rootDir, "*", SearchOption.AllDirectories))
                    {
                        FileInfo fi;
                        try { fi = new FileInfo(file); }
                        catch (IOException) { continue; }
                        catch (UnauthorizedAccessException) { continue; }

                        if (fi.Length > 4 * 1024 * 1024) continue; // 跳过大文件（pdb / dll 等不会包含 ASCII 哨兵字符串）
                        string content;
                        try { content = File.ReadAllText(file, Encoding.UTF8); }
                        catch (IOException) { continue; }
                        catch (UnauthorizedAccessException) { continue; }

                        Assert.False(content.Contains(SentinelControllerId),
                            $"哨兵 controllerId 出现在文件 {file}");
                        Assert.False(content.Contains(SentinelControllerHwidHash),
                            $"哨兵 hwidHash 出现在文件 {file}");
                        Assert.False(content.Contains(signatureBase64),
                            $"服务端签名 base64 出现在文件 {file}");
                    }
                }
            }
            finally
            {
                try { Directory.Delete(sandboxRoot, true); } catch { /* ignore */ }
            }
        }

        [Property(MaxTest = 5)]
        public Property TamperedSignature_RejectsReplacement(NonNegativeInt seqOffset)
        {
            using var io = new InMemoryObfuscation();
            var clock = new FakeClock();
            var store = new SnapshotStore(io, clock);
            var validator = new FixedKeyValidator();

            var snapshotJson = BuildSignedSnapshotJson(
                machineId: "MACHINE-TAMPER",
                snapshotSeq: 1 + seqOffset.Get,
                issuedAtMs: clock.UtcNow.ToUnixTimeMilliseconds(),
                controllerIds: new[] { SentinelControllerId });

            // 在 signature 字段位置翻转一字节（改在中间位置避免 base64 padding 边界异常）
            using var doc = JsonDocument.Parse(snapshotJson);
            var origSig = doc.RootElement.GetProperty("signature").GetString() ?? string.Empty;
            var sigBytes = Convert.FromBase64String(origSig);
            // 翻转中间字节，避免触碰 RSA 签名 PKCS1 padding 起始 / 末尾的 0x00 边界
            // 中间字节是 RSA 签名内部数据的 hash 区，翻转必然让 PKCS1 解密结果不通过 ASN.1 校验
            sigBytes[sigBytes.Length / 2] ^= 0x80;
            var tamperedSig = Convert.ToBase64String(sigBytes);
            var tamperedJson = snapshotJson.Replace(origSig, tamperedSig);
            Assert.NotEqual(snapshotJson, tamperedJson); // 确保 Replace 实际生效

            // 先确认未篡改版本能通过（确保夹具签名正确）
            using (var io2 = new InMemoryObfuscation())
            using (var verifyStore = new SnapshotStoreShim(io2, clock))
            {
                var verifyOk = verifyStore.Inner.TryReplace(snapshotJson, validator, out var verifyReason);
                if (!verifyOk)
                {
                    return false.Label($"原始签名快照都没通过：{verifyReason}");
                }
            }

            var ok = store.TryReplace(tamperedJson, validator, out var rejectReason);
            return ((!ok) && string.Equals(rejectReason, "signature_invalid", StringComparison.Ordinal))
                .Label($"ok={ok}, rejectReason={rejectReason}, sigLen={sigBytes.Length}");
        }

        private sealed class SnapshotStoreShim : IDisposable
        {
            public SnapshotStore Inner { get; }
            public SnapshotStoreShim(InMemoryObfuscation io, IClock clock) { Inner = new SnapshotStore(io, clock); }
            public void Dispose() { /* 测试夹具 - 仅持有引用 */ }
        }
    }
}
