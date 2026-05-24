using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VHDMounter;
using VHDMounter.RustDeskBridge.Crypto;
using VHDMounter.RustDeskBridge.Frames;
using VHDMounter.RustDeskBridge.Json;
using VHDMounter.RustDeskBridge.Policy;
using VHDMounter.RustDeskBridge.Revocation;
using Xunit;

namespace VHDMounter.Tests.RustDeskBridge.Properties
{
    /// <summary>
    /// Property 12: Revocation 推送与级联失效。
    ///
    /// Validates: Requirements 10.3, 10.5
    ///
    /// (a) 推送后 SnapshotStore.Invalidate() 被调用 → PeerApprovalEvaluator 全 rejected
    ///     （等价于"已收但未回写的 PeerApproval 不再写出响应"——本测试验证决策路径）
    /// (b) 下一次 §8.5 查表走 §8.6.1 失效路径直到下一次成功拉取
    /// (c) 连续两次同 (reason, secretVersion) 第二次 no-op
    /// (d) EventKey="revocation_pushed" 条目不含 controllerId / controllerName 子串
    ///
    /// 工程权衡：(a) 涉及"已收但未回写的 PeerApproval 不再写出响应"需要真实管道，难度高。
    /// 简化为：测 SnapshotStore.Invalidate 被 RevocationPublisher 调用后 PeerApprovalEvaluator
    /// 全 rejected（语义等价：响应回不出，因为不可能命中）。
    /// </summary>
    [Trait("feature", "rustdesk-bridge-host")]
    [Trait("property", "Property 12: Revocation 推送与级联失效")]
    public sealed class RevocationCascadeProperty
    {
        private const string MachineId = "MACHINE-DEADBEEF";

        private static readonly Lazy<RSA> PolicySigner = new(() => RSA.Create(2048));

        private sealed class FakeClock : IClock
        {
            public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1730000000000L);
        }

        private sealed class FakeValidator : IPolicyPubkeyValidator
        {
            public string CurrentPubkeyDigestHex => "fixture";
            public bool VerifyResponseSignature(ReadOnlySpan<byte> payload, string signatureBase64)
            {
                if (string.IsNullOrEmpty(signatureBase64)) return false;
                byte[] sig;
                try { sig = Convert.FromBase64String(signatureBase64); }
                catch (FormatException) { return false; }
                return PolicySigner.Value.VerifyData(payload.ToArray(), sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
        }

        private sealed class StaticSecretProvider : IBridgeSecretProvider
        {
            private readonly byte[] _secret;
            public StaticSecretProvider(uint version, byte[] secret) { CurrentSecretVersion = version; _secret = secret; }
            public uint CurrentSecretVersion { get; }
            public ReadOnlySpan<byte> GetActiveSecret() => _secret;
        }

        private sealed class AllowGate : IRegistrationGate
        {
            public bool IsRegisteredAndApproved => true;
        }

        private static byte[] FixtureSecret => Enumerable.Repeat((byte)0xAB, 32).ToArray();

        // ---------- (a)+(b) Revocation 推送后 SnapshotStore 失效 + PeerApproval 全 rejected ----------

        [Fact]
        public async Task PushDenied_InvalidatesSnapshot_PeerApprovalAllRejected()
        {
            using var io = new InMemoryObfuscation();
            var clock = new FakeClock();
            var store = new SnapshotStore(io, clock);
            var validator = new FakeValidator();
            var provider = new StaticSecretProvider(1u, FixtureSecret);
            var verifier = new HmacVerifier(provider);

            // 装载合法快照
            var nowMs = clock.UtcNow.ToUnixTimeMilliseconds();
            Assert.True(store.TryReplace(BuildSignedSnapshotJson(MachineId, 1, nowMs, "CTRL-A"), validator, out _));
            Assert.True(store.IsHealthy);

            // 验证：推 Revocation 之前命中正常
            var ev = new PeerApprovalEvaluator(store, new AllowGate());
            var preResp = ev.Evaluate(BuildPeerApprovalFrame("CTRL-A"), MachineId);
            Assert.Equal(PeerApprovalResponse.ResultApproved, preResp.Result);

            var (buffer, dir) = BuildBuffer();
            try
            {
                var publisher = new RevocationPublisher(
                    verifier, store, buffer, clock, provider,
                    getActiveSession: () => null,
                    cancelInflightRequests: _ => Task.CompletedTask);

                Assert.True(await publisher.PushDeniedAsync());

                // 推送后：SnapshotStore.Invalidate 已被调用 → IsHealthy=false
                Assert.False(store.IsHealthy);

                // PeerApproval 全 rejected
                var postResp = ev.Evaluate(BuildPeerApprovalFrame("CTRL-A"), MachineId);
                Assert.Equal(PeerApprovalResponse.ResultRejected, postResp.Result);
                Assert.Null(postResp.TtlMs);

                // (b) 下一次查表持续 rejected 直到成功拉取一份新快照
                for (var i = 0; i < 5; i++)
                {
                    var r = ev.Evaluate(BuildPeerApprovalFrame("CTRL-A"), MachineId);
                    Assert.Equal(PeerApprovalResponse.ResultRejected, r.Result);
                }

                // 模拟下一次成功拉取（snapshotSeq +1）→ 恢复
                Assert.True(store.TryReplace(BuildSignedSnapshotJson(MachineId, 2, nowMs, "CTRL-A"), validator, out _));
                Assert.True(store.IsHealthy);
                var recoveredResp = ev.Evaluate(BuildPeerApprovalFrame("CTRL-A"), MachineId);
                Assert.Equal(PeerApprovalResponse.ResultApproved, recoveredResp.Result);
            }
            finally
            {
                buffer.Dispose();
                Cleanup(dir);
            }
        }

        // ---------- (c) 连续两次同 (reason, secretVersion) 第二次 no-op ----------

        [Fact]
        public async Task ConsecutiveSamePush_SecondIsNoop_NoCrashNoLeak()
        {
            using var io = new InMemoryObfuscation();
            var clock = new FakeClock();
            var store = new SnapshotStore(io, clock);
            var provider = new StaticSecretProvider(7u, FixtureSecret);
            var verifier = new HmacVerifier(provider);

            var (buffer, dir) = BuildBuffer();
            try
            {
                var cancelInflightCalled = 0;
                var publisher = new RevocationPublisher(
                    verifier, store, buffer, clock, provider,
                    getActiveSession: () => null,
                    cancelInflightRequests: _ =>
                    {
                        cancelInflightCalled++;
                        return Task.CompletedTask;
                    });

                Assert.True(await publisher.PushDeniedAsync());
                Assert.False(await publisher.PushDeniedAsync(), "第二次 denied push 应当 no-op");
                Assert.False(await publisher.PushDeniedAsync(), "第三次 denied push 应当 no-op");
                Assert.Equal(1, cancelInflightCalled);

                // 不同 reason 在同 secretVersion 下应当能再次 push
                Assert.True(await publisher.PushSecretOutdatedAsync(),
                    "不同 reason 第一次必须实际发出");
                Assert.False(await publisher.PushSecretOutdatedAsync(),
                    "同 reason 第二次必须 no-op");
                Assert.Equal(2, cancelInflightCalled);
            }
            finally
            {
                buffer.Dispose();
                Cleanup(dir);
            }
        }

        // ---------- (d) MachineLogBuffer 中 revocation_pushed 条目不含 controllerId / controllerName ----------

        [Fact]
        public async Task RevocationPushedLog_DoesNotContainControllerIdOrName()
        {
            using var io = new InMemoryObfuscation();
            var clock = new FakeClock();
            var store = new SnapshotStore(io, clock);
            var provider = new StaticSecretProvider(1u, FixtureSecret);
            var verifier = new HmacVerifier(provider);

            // 即使快照中包含 controllerId / controllerName，revocation_pushed 日志也不该泄漏
            var validator = new FakeValidator();
            var nowMs = clock.UtcNow.ToUnixTimeMilliseconds();
            Assert.True(store.TryReplace(
                BuildSignedSnapshotJson(MachineId, 1, nowMs, "CTRL-SECRET-87654321"),
                validator, out _));

            var (buffer, dir) = BuildBuffer();
            try
            {
                var publisher = new RevocationPublisher(
                    verifier, store, buffer, clock, provider,
                    getActiveSession: () => null,
                    cancelInflightRequests: _ => Task.CompletedTask);

                Assert.True(await publisher.PushDeniedAsync());
                Assert.True(await publisher.PushSecretOutdatedAsync());

                var pending = buffer.GetPendingBatch(buffer.CurrentSessionId, 0, 100);
                var revLogs = pending.Where(e => e.EventKey == "revocation_pushed").ToList();
                Assert.True(revLogs.Count >= 2, $"期望 ≥2 条 revocation_pushed 日志，实际 {revLogs.Count}");

                foreach (var log in revLogs)
                {
                    Assert.DoesNotContain("CTRL-SECRET-87654321", log.Message);
                    Assert.DoesNotContain("CTRL-SECRET-87654321", log.RawText);
                    Assert.DoesNotContain("controllerId", log.Message);
                    Assert.DoesNotContain("controllerName", log.Message);

                    // Metadata 不能包含敏感键
                    foreach (var kv in log.Metadata)
                    {
                        Assert.DoesNotContain("controllerId", kv.Key, StringComparison.OrdinalIgnoreCase);
                        Assert.DoesNotContain("controllerName", kv.Key, StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
            finally
            {
                buffer.Dispose();
                Cleanup(dir);
            }
        }

        // ---------- 辅助 ----------

        private static (MachineLogBuffer buffer, string dir) BuildBuffer()
        {
            var dir = Path.Combine(Path.GetTempPath(), "vhdm-bridge-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var spoolPath = Path.Combine(dir, "spool.jsonl");
            return (new MachineLogBuffer(spoolPath, "test-session-rev", 1024 * 1024), dir);
        }

        private static void Cleanup(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch (IOException) { /* ignore */ }
        }

        private static string BuildSignedSnapshotJson(
            string machineId, long snapshotSeq, long issuedAtMs, string controllerId)
        {
            using var entriesMs = new MemoryStream();
            using (var w = new Utf8JsonWriter(entriesMs, new JsonWriterOptions { Indented = false }))
            {
                w.WriteStartArray();
                w.WriteStartObject();
                w.WriteString("controllerId", controllerId);
                w.WriteNull("controllerHwidHash");
                w.WriteString("scope", "global");
                w.WriteBoolean("enabled", true);
                w.WriteNull("expiresAt");
                w.WriteEndObject();
                w.WriteEndArray();
            }
            var entriesBytes = entriesMs.ToArray();

            var canonical = JcsCanonicalizer.Canonicalize(JsonDocument.Parse(entriesBytes).RootElement);
            var entriesDigestHex = Convert.ToHexString(SHA256.HashData(canonical)).ToLowerInvariant();
            var payload = string.Concat(
                "TrustedControllersSnapshotV1\n",
                machineId, "\n",
                snapshotSeq, "\n",
                issuedAtMs, "\n",
                entriesDigestHex);
            var sig = PolicySigner.Value.SignData(
                Encoding.ASCII.GetBytes(payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var sigBase64 = Convert.ToBase64String(sig);

            using var outerMs = new MemoryStream();
            using (var w = new Utf8JsonWriter(outerMs, new JsonWriterOptions { Indented = false }))
            {
                w.WriteStartObject();
                w.WriteString("machineId", machineId);
                w.WriteNumber("snapshotSeq", snapshotSeq);
                w.WriteNumber("issuedAt", issuedAtMs);
                w.WritePropertyName("entries");
                using var entriesDoc = JsonDocument.Parse(entriesBytes);
                entriesDoc.RootElement.WriteTo(w);
                w.WriteString("signature", sigBase64);
                w.WriteEndObject();
            }
            return Encoding.UTF8.GetString(outerMs.ToArray());
        }

        private static PeerApprovalFrame BuildPeerApprovalFrame(string controllerId)
        {
            return new PeerApprovalFrame
            {
                Protocol = PeerApprovalFrame.ProtocolLiteral,
                SecretVersion = 1,
                ControlledMachineId = MachineId,
                ControllerId = controllerId,
                ControllerName = "ignored",
                ControllerPlatform = "Windows",
                ControllerHwid = "ignored",
                PeerSocketAddr = "192.0.2.1:51820",
                ConnectionType = PeerApprovalFrame.ConnectionTypeControlled,
                RequestNonce = "n",
                TimestampMs = 1730000000000L,
                Mac = "ignored",
            };
        }
    }
}
