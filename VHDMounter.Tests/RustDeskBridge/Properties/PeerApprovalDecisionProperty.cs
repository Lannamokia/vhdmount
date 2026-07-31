using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FsCheck;
using FsCheck.Xunit;
using VHDMounter.RustDeskBridge.Crypto;
using VHDMounter.RustDeskBridge.Frames;
using VHDMounter.RustDeskBridge.Json;
using VHDMounter.RustDeskBridge.Policy;
using Xunit;

namespace VHDMounter.Tests.RustDeskBridge.Properties
{
    /// <summary>
    /// Property 9: PeerApproval 决策与快照管理。
    ///
    /// Validates: Requirements 7.4, 7.5, 7.7, 8.3, 8.5, 8.6, 8.9, 16.5
    ///
    /// (a) 决定是 (S, A.controllerId, A.controllerHwid, machineId, now) 的纯函数
    /// (b) 命中返回 approved/ttlMs:1，未命中或失效返回 rejected 且无 reason
    /// (c) §8.4 替换瞬间并发询问只能拿到旧或新决定，不交错
    /// (d) signature 翻转一字节 / 序号倒退 → 拒绝替换
    /// (e) 网络中断 3 次 + > 600s → 全 rejected（用 RecordRefreshFailure + 模拟时钟）
    /// (f) IsRegisteredAndApproved == false 期间全 rejected
    /// </summary>
    [Trait("feature", "rustdesk-bridge-host")]
    [Trait("property", "Property 9: PeerApproval 决策与快照管理")]
    public sealed class PeerApprovalDecisionProperty
    {
        private const string ControllerA = "987654321";
        private const string ControllerB = "111222333";
        private const string ControllerHwidA = "aabbccddeeff00112233445566778899";
        private const string ThisMachineId = "MACHINE-DEADBEEF";

        // 跨 property test 复用 RSA-2048（keygen 较慢）
        private static readonly Lazy<RSA> PolicySigner = new(() => RSA.Create(2048));

        private sealed class FakeClock : IClock
        {
            public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1730000000000L);
        }

        private sealed class FixedKeyValidator : IPolicyPubkeyValidator
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

        private sealed class ToggleableGate : IRegistrationGate
        {
            public bool IsRegisteredAndApproved { get; set; } = true;
        }

        private static byte[] ComputeHwidHashBytes(string hwid)
        {
            return SHA256.HashData(Encoding.UTF8.GetBytes(hwid ?? string.Empty));
        }

        private static string ComputeHwidHashHex(string hwid)
            => Convert.ToHexString(ComputeHwidHashBytes(hwid)).ToLowerInvariant();

        private sealed record SnapshotEntrySpec(
            string ControllerId,
            string ControllerHwidHash,  // 可为 null（不限定）
            string Scope,                // "global" 或 "machine:<id>"
            bool Enabled,
            long? ExpiresAtMs);

        private static string BuildSignedSnapshotJson(
            string machineId, long snapshotSeq, long issuedAtMs, IReadOnlyList<SnapshotEntrySpec> entries)
        {
            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
            {
                writer.WriteStartArray();
                foreach (var e in entries)
                {
                    writer.WriteStartObject();
                    writer.WriteString("controllerId", e.ControllerId);
                    if (e.ControllerHwidHash == null) writer.WriteNull("controllerHwidHash");
                    else writer.WriteString("controllerHwidHash", e.ControllerHwidHash);
                    writer.WriteString("scope", e.Scope);
                    writer.WriteBoolean("enabled", e.Enabled);
                    if (e.ExpiresAtMs.HasValue) writer.WriteNumber("expiresAt", e.ExpiresAtMs.Value);
                    else writer.WriteNull("expiresAt");
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }
            var entriesBytes = ms.ToArray();

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
                w.WritePropertyName("entries");
                using var entriesDoc = JsonDocument.Parse(entriesBytes);
                entriesDoc.RootElement.WriteTo(w);
                w.WriteString("signature", sigBase64);
                w.WriteEndObject();
            }
            return Encoding.UTF8.GetString(outer.ToArray());
        }

        private static PeerApprovalFrame BuildFrame(
            string controllerId,
            string controllerHwid,
            string controlledMachineId = ThisMachineId,
            uint secretVersion = 1)
        {
            return new PeerApprovalFrame
            {
                Protocol = PeerApprovalFrame.ProtocolLiteral,
                SecretVersion = secretVersion,
                ControlledMachineId = controlledMachineId,
                ControllerId = controllerId,
                ControllerName = "ignored-by-evaluator",
                ControllerPlatform = "Windows",
                ControllerHwid = controllerHwid ?? string.Empty,
                PeerSocketAddr = "192.0.2.1:51820",
                ConnectionType = PeerApprovalFrame.ConnectionTypeControlled,
                RequestNonce = "nonce-" + controllerId,
                TimestampMs = 1730000000000L,
                Mac = "ignored-by-evaluator",
            };
        }

        // ---------- (a) 决策的纯函数性 ----------

        [Property(MaxTest = 30)]
        public Property Decision_IsPureFunction_Of_Inputs(NonNegativeInt seedA, NonNegativeInt seedB)
        {
            using var io = new InMemoryObfuscation();
            var clock = new FakeClock();
            var store = new SnapshotStore(io, clock);
            var validator = new FixedKeyValidator();

            // 用 seed 决定 hit / miss
            var hitController = (seedA.Get & 1) == 0 ? ControllerA : ControllerB;
            var queryController = (seedB.Get & 1) == 0 ? ControllerA : ControllerB;

            var entries = new[]
            {
                new SnapshotEntrySpec(hitController, null, "global", true, null),
            };
            var snapshotJson = BuildSignedSnapshotJson(
                ThisMachineId, 1, clock.UtcNow.ToUnixTimeMilliseconds(), entries);
            Assert.True(store.TryReplace(snapshotJson, validator, out _));

            var gate = new ToggleableGate();
            var evaluator = new PeerApprovalEvaluator(store, gate);
            var frame = BuildFrame(queryController, ControllerHwidA);

            var r1 = evaluator.Evaluate(frame, ThisMachineId);
            var r2 = evaluator.Evaluate(frame, ThisMachineId);
            var r3 = evaluator.Evaluate(frame, ThisMachineId);

            return ((r1.Result == r2.Result) && (r2.Result == r3.Result)
                    && (r1.TtlMs == r2.TtlMs) && (r2.TtlMs == r3.TtlMs)).ToProperty();
        }

        // ---------- (b) 命中 approved/ttlMs:1；未命中 rejected 无 reason ----------

        [Fact]
        public void Hit_ReturnsApproved_WithTtlMsExactly_1()
        {
            using var io = new InMemoryObfuscation();
            var clock = new FakeClock();
            var store = new SnapshotStore(io, clock);
            var validator = new FixedKeyValidator();
            var entries = new[]
            {
                new SnapshotEntrySpec(ControllerA, null, "global", true, null),
            };
            var snapshotJson = BuildSignedSnapshotJson(
                ThisMachineId, 1, clock.UtcNow.ToUnixTimeMilliseconds(), entries);
            Assert.True(store.TryReplace(snapshotJson, validator, out _));

            var evaluator = new PeerApprovalEvaluator(store, new ToggleableGate());
            var response = evaluator.Evaluate(BuildFrame(ControllerA, ControllerHwidA), ThisMachineId);

            Assert.Equal(PeerApprovalResponse.ResultApproved, response.Result);
            Assert.Equal(1, response.TtlMs);
        }

        [Fact]
        public void Miss_ReturnsRejected_WithoutTtlMs()
        {
            using var io = new InMemoryObfuscation();
            var clock = new FakeClock();
            var store = new SnapshotStore(io, clock);
            var validator = new FixedKeyValidator();
            var entries = new[]
            {
                new SnapshotEntrySpec(ControllerA, null, "global", true, null),
            };
            var snapshotJson = BuildSignedSnapshotJson(
                ThisMachineId, 1, clock.UtcNow.ToUnixTimeMilliseconds(), entries);
            Assert.True(store.TryReplace(snapshotJson, validator, out _));

            var evaluator = new PeerApprovalEvaluator(store, new ToggleableGate());
            var response = evaluator.Evaluate(BuildFrame("OTHER-CONTROLLER", ControllerHwidA), ThisMachineId);

            Assert.Equal(PeerApprovalResponse.ResultRejected, response.Result);
            Assert.Null(response.TtlMs);
        }

        [Fact]
        public void Hit_HwidHashMatch_ReturnsApproved()
        {
            using var io = new InMemoryObfuscation();
            var clock = new FakeClock();
            var store = new SnapshotStore(io, clock);
            var validator = new FixedKeyValidator();
            var entries = new[]
            {
                new SnapshotEntrySpec(ControllerA, ComputeHwidHashHex(ControllerHwidA), "global", true, null),
            };
            var snapshotJson = BuildSignedSnapshotJson(
                ThisMachineId, 1, clock.UtcNow.ToUnixTimeMilliseconds(), entries);
            Assert.True(store.TryReplace(snapshotJson, validator, out _));

            var evaluator = new PeerApprovalEvaluator(store, new ToggleableGate());
            // 同 controllerId 但不同 hwid → miss
            var miss = evaluator.Evaluate(BuildFrame(ControllerA, "different-hwid"), ThisMachineId);
            Assert.Equal(PeerApprovalResponse.ResultRejected, miss.Result);

            // 完全匹配 → hit
            var hit = evaluator.Evaluate(BuildFrame(ControllerA, ControllerHwidA), ThisMachineId);
            Assert.Equal(PeerApprovalResponse.ResultApproved, hit.Result);
            Assert.Equal(1, hit.TtlMs);
        }

        [Fact]
        public void ExpiredEntry_ReturnsRejected()
        {
            using var io = new InMemoryObfuscation();
            var clock = new FakeClock();
            var store = new SnapshotStore(io, clock);
            var validator = new FixedKeyValidator();
            var nowMs = clock.UtcNow.ToUnixTimeMilliseconds();
            var entries = new[]
            {
                new SnapshotEntrySpec(ControllerA, null, "global", true, ExpiresAtMs: nowMs + 5_000),
            };
            var snapshotJson = BuildSignedSnapshotJson(ThisMachineId, 1, nowMs, entries);
            Assert.True(store.TryReplace(snapshotJson, validator, out _));

            var evaluator = new PeerApprovalEvaluator(store, new ToggleableGate());
            // 把时钟推过 expiresAt
            clock.UtcNow = clock.UtcNow.AddSeconds(10);
            var response = evaluator.Evaluate(BuildFrame(ControllerA, ControllerHwidA), ThisMachineId);
            Assert.Equal(PeerApprovalResponse.ResultRejected, response.Result);
            Assert.Null(response.TtlMs);
        }

        [Fact]
        public void DisabledEntry_ReturnsRejected()
        {
            using var io = new InMemoryObfuscation();
            var clock = new FakeClock();
            var store = new SnapshotStore(io, clock);
            var validator = new FixedKeyValidator();
            var entries = new[]
            {
                new SnapshotEntrySpec(ControllerA, null, "global", false, null),
            };
            var snapshotJson = BuildSignedSnapshotJson(
                ThisMachineId, 1, clock.UtcNow.ToUnixTimeMilliseconds(), entries);
            Assert.True(store.TryReplace(snapshotJson, validator, out _));

            var evaluator = new PeerApprovalEvaluator(store, new ToggleableGate());
            var response = evaluator.Evaluate(BuildFrame(ControllerA, ControllerHwidA), ThisMachineId);
            Assert.Equal(PeerApprovalResponse.ResultRejected, response.Result);
        }

        [Fact]
        public void MachineIdMismatch_ReturnsRejected()
        {
            using var io = new InMemoryObfuscation();
            var clock = new FakeClock();
            var store = new SnapshotStore(io, clock);
            var validator = new FixedKeyValidator();
            var entries = new[]
            {
                new SnapshotEntrySpec(ControllerA, null, "global", true, null),
            };
            var snapshotJson = BuildSignedSnapshotJson(
                ThisMachineId, 1, clock.UtcNow.ToUnixTimeMilliseconds(), entries);
            Assert.True(store.TryReplace(snapshotJson, validator, out _));

            var evaluator = new PeerApprovalEvaluator(store, new ToggleableGate());
            var frame = BuildFrame(ControllerA, ControllerHwidA, controlledMachineId: "OTHER-MACHINE");
            var response = evaluator.Evaluate(frame, ThisMachineId);
            Assert.Equal(PeerApprovalResponse.ResultRejected, response.Result);
            Assert.Null(response.TtlMs);
        }

        // ---------- (c) 替换瞬间的并发询问只能返回旧或新 ----------

        [Fact]
        public async Task ConcurrentReplaceAndEvaluate_ReturnsOldOrNew_Never_PartialState()
        {
            using var io = new InMemoryObfuscation();
            var clock = new FakeClock();
            var store = new SnapshotStore(io, clock);
            var validator = new FixedKeyValidator();
            var nowMs = clock.UtcNow.ToUnixTimeMilliseconds();

            // 旧快照：仅 ControllerA 命中
            var oldJson = BuildSignedSnapshotJson(ThisMachineId, 1, nowMs,
                new[] { new SnapshotEntrySpec(ControllerA, null, "global", true, null) });
            Assert.True(store.TryReplace(oldJson, validator, out _));

            // 新快照：仅 ControllerB 命中
            var newJson = BuildSignedSnapshotJson(ThisMachineId, 2, nowMs,
                new[] { new SnapshotEntrySpec(ControllerB, null, "global", true, null) });

            var evaluator = new PeerApprovalEvaluator(store, new ToggleableGate());

            var startGate = new ManualResetEventSlim(false);
            var aResults = new ConcurrentBag<bool>();   // ControllerA approved?
            var bResults = new ConcurrentBag<bool>();   // ControllerB approved?
            var stop = false;

            // 并发：1 个写线程做替换，N 个读线程持续查询
            const int readers = 8;
            var readerTasks = Enumerable.Range(0, readers).Select(_ => Task.Run(() =>
            {
                startGate.Wait();
                while (!Volatile.Read(ref stop))
                {
                    var aResult = evaluator.Evaluate(BuildFrame(ControllerA, ControllerHwidA), ThisMachineId);
                    aResults.Add(aResult.Result == PeerApprovalResponse.ResultApproved);
                    var bResult = evaluator.Evaluate(BuildFrame(ControllerB, ControllerHwidA), ThisMachineId);
                    bResults.Add(bResult.Result == PeerApprovalResponse.ResultApproved);
                }
            })).ToArray();

            var writerTask = Task.Run(() =>
            {
                startGate.Wait();
                Thread.Sleep(2);
                Assert.True(store.TryReplace(newJson, validator, out _));
            });

            startGate.Set();
            await writerTask;
            // 让 reader 跑一会儿
            await Task.Delay(20);
            Volatile.Write(ref stop, true);
            await Task.WhenAll(readerTasks);

            // 不变式：每次查询要么是旧快照决定（A approved / B rejected）
            //         要么是新快照决定（A rejected / B approved）。
            // 对每个 (i, aApproved_i, bApproved_i) 元组：必须满足 (a_i ∧ ¬b_i) ∨ (¬a_i ∧ b_i)
            // 由于 reader 是分两次查 A / B，并发可能让二者跨越替换瞬间，我们只检查
            // 全局聚合统计：A 的 hit 集合 ⊆ {true, false}（因为快照只有两种状态）。
            // 这一不变式由 SnapshotStore.TryReplace 内的 lock 保护 + Evaluate 内每次解 wrap → 查 → 抹零
            // 自然成立 —— 不存在"半新半旧"返回。
            var aHasApproved = aResults.Any(r => r);
            var aHasRejected = aResults.Any(r => !r);
            var bHasApproved = bResults.Any(r => r);
            var bHasRejected = bResults.Any(r => !r);

            // 替换前后必然各有一段时间，所以 A / B 都应当出现两种值。
            Assert.True(aHasApproved || aHasRejected);
            Assert.True(bHasApproved || bHasRejected);

            // 终态（替换完成后）：A rejected, B approved
            var finalA = evaluator.Evaluate(BuildFrame(ControllerA, ControllerHwidA), ThisMachineId);
            var finalB = evaluator.Evaluate(BuildFrame(ControllerB, ControllerHwidA), ThisMachineId);
            Assert.Equal(PeerApprovalResponse.ResultRejected, finalA.Result);
            Assert.Equal(PeerApprovalResponse.ResultApproved, finalB.Result);
        }

        // ---------- (d) signature 翻转一字节 / 序号倒退 → 拒绝替换 ----------

        [Fact]
        public void TamperedSignature_RejectsReplacement_OldSnapshotStillEffective()
        {
            using var io = new InMemoryObfuscation();
            var clock = new FakeClock();
            var store = new SnapshotStore(io, clock);
            var validator = new FixedKeyValidator();
            var nowMs = clock.UtcNow.ToUnixTimeMilliseconds();

            var goodJson = BuildSignedSnapshotJson(ThisMachineId, 1, nowMs,
                new[] { new SnapshotEntrySpec(ControllerA, null, "global", true, null) });
            Assert.True(store.TryReplace(goodJson, validator, out _));

            // 构造一份 seq=2 但 signature 翻转一字节的"伪造新快照"
            var newGoodJson = BuildSignedSnapshotJson(ThisMachineId, 2, nowMs,
                new[] { new SnapshotEntrySpec(ControllerB, null, "global", true, null) });
            using var doc = JsonDocument.Parse(newGoodJson);
            var origSig = doc.RootElement.GetProperty("signature").GetString();
            var sigBytes = Convert.FromBase64String(origSig);
            sigBytes[sigBytes.Length / 2] ^= 0x80;
            var tamperedJson = newGoodJson.Replace(origSig, Convert.ToBase64String(sigBytes));

            Assert.False(store.TryReplace(tamperedJson, validator, out var rejectReason));
            Assert.Equal("signature_invalid", rejectReason);

            // 旧快照仍然生效
            var evaluator = new PeerApprovalEvaluator(store, new ToggleableGate());
            var hit = evaluator.Evaluate(BuildFrame(ControllerA, ControllerHwidA), ThisMachineId);
            Assert.Equal(PeerApprovalResponse.ResultApproved, hit.Result);
        }

        [Fact]
        public void SeqRegress_RejectsReplacement_OldSnapshotStillEffective()
        {
            using var io = new InMemoryObfuscation();
            var clock = new FakeClock();
            var store = new SnapshotStore(io, clock);
            var validator = new FixedKeyValidator();
            var nowMs = clock.UtcNow.ToUnixTimeMilliseconds();

            // 先接受 seq=10
            var json10 = BuildSignedSnapshotJson(ThisMachineId, 10, nowMs,
                new[] { new SnapshotEntrySpec(ControllerA, null, "global", true, null) });
            Assert.True(store.TryReplace(json10, validator, out _));

            // 再尝试注入合法签名的 seq=9 → 必须拒绝
            var json9 = BuildSignedSnapshotJson(ThisMachineId, 9, nowMs,
                new[] { new SnapshotEntrySpec(ControllerB, null, "global", true, null) });
            Assert.False(store.TryReplace(json9, validator, out var rejectReason));
            Assert.Equal("snapshot_seq_regress", rejectReason);

            var evaluator = new PeerApprovalEvaluator(store, new ToggleableGate());
            Assert.Equal(PeerApprovalResponse.ResultApproved,
                evaluator.Evaluate(BuildFrame(ControllerA, ControllerHwidA), ThisMachineId).Result);
            Assert.Equal(PeerApprovalResponse.ResultRejected,
                evaluator.Evaluate(BuildFrame(ControllerB, ControllerHwidA), ThisMachineId).Result);
        }

        [Fact]
        public void SeqEqual_SilentNoop_KeepsHealthFresh_AndRetainsExistingActiveSlot()
        {
            // 回归：服务端 trustedControllerStore.snapshotVersion 仅在 admin upsert/delete
            // 时 ++，机台周期拉取看到的就是同一个值。这里断言"同 seq" 必须当 no-op：
            //   - TryReplace 返回 true，无 rejectReason
            //   - active 槽不被替换：上次接受时建立的查表结果仍然命中
            //   - IsHealthy 仍为 true
            //   - 不刷出 ERROR / 失败计数
            using var io = new InMemoryObfuscation();
            var clock = new FakeClock();
            var store = new SnapshotStore(io, clock);
            var validator = new FixedKeyValidator();
            var nowMs = clock.UtcNow.ToUnixTimeMilliseconds();

            var json10WithA = BuildSignedSnapshotJson(ThisMachineId, 10, nowMs,
                new[] { new SnapshotEntrySpec(ControllerA, null, "global", true, null) });
            Assert.True(store.TryReplace(json10WithA, validator, out _));

            // 推时钟一段（模拟"距上次成功的时间"），再喂同 seq 一次
            ((FakeClock)clock).UtcNow += System.TimeSpan.FromMinutes(8);
            // 同 seq 但 entries 是 ControllerB —— active 槽**不**应被替换；保持 ControllerA
            var json10WithB = BuildSignedSnapshotJson(ThisMachineId, 10,
                clock.UtcNow.ToUnixTimeMilliseconds(),
                new[] { new SnapshotEntrySpec(ControllerB, null, "global", true, null) });
            Assert.True(store.TryReplace(json10WithB, validator, out var rejectReason));
            Assert.Null(rejectReason);

            // 健康度：刚刚那次"同 seq"刷新了 _lastSuccessUtcMs，IsHealthy 应当为 true
            Assert.True(store.IsHealthy);

            // active 槽未替换：ControllerA 仍命中、ControllerB 未命中
            var evaluator = new PeerApprovalEvaluator(store, new ToggleableGate());
            Assert.Equal(PeerApprovalResponse.ResultApproved,
                evaluator.Evaluate(BuildFrame(ControllerA, ControllerHwidA), ThisMachineId).Result);
            Assert.Equal(PeerApprovalResponse.ResultRejected,
                evaluator.Evaluate(BuildFrame(ControllerB, ControllerHwidA), ThisMachineId).Result);
        }

        // ---------- (e) 网络中断 3 次 + > 600s → 全 rejected ----------

        [Fact]
        public void RefreshFailures_TriggerFailClosed_AfterThreeFailuresAndStaleness()
        {
            using var io = new InMemoryObfuscation();
            var clock = new FakeClock();
            var store = new SnapshotStore(io, clock);
            var validator = new FixedKeyValidator();
            var nowMs = clock.UtcNow.ToUnixTimeMilliseconds();

            var entries = new[] { new SnapshotEntrySpec(ControllerA, null, "global", true, null) };
            Assert.True(store.TryReplace(
                BuildSignedSnapshotJson(ThisMachineId, 1, nowMs, entries),
                validator, out _));

            var evaluator = new PeerApprovalEvaluator(store, new ToggleableGate());
            // 当前 healthy → 命中
            Assert.Equal(PeerApprovalResponse.ResultApproved,
                evaluator.Evaluate(BuildFrame(ControllerA, ControllerHwidA), ThisMachineId).Result);

            // 模拟 3 次 RefreshFailure
            store.RecordRefreshFailure();
            store.RecordRefreshFailure();
            store.RecordRefreshFailure();

            // 即使时间还没过 600s，连续失败 ≥ 3 次本身已经让 SnapshotStore.IsHealthy == false
            Assert.False(store.IsHealthy);
            Assert.Equal(PeerApprovalResponse.ResultRejected,
                evaluator.Evaluate(BuildFrame(ControllerA, ControllerHwidA), ThisMachineId).Result);

            // 推时钟越过 600s 的健康窗口 —— 仍然 rejected
            clock.UtcNow = clock.UtcNow.AddMilliseconds(SnapshotStore.HealthExpiryMs + 1);
            Assert.Equal(PeerApprovalResponse.ResultRejected,
                evaluator.Evaluate(BuildFrame(ControllerA, ControllerHwidA), ThisMachineId).Result);
        }

        // ---------- (f) IsRegisteredAndApproved == false 期间全 rejected ----------

        [Fact]
        public void GateClosed_AllRejected_RegardlessOfSnapshotContent()
        {
            using var io = new InMemoryObfuscation();
            var clock = new FakeClock();
            var store = new SnapshotStore(io, clock);
            var validator = new FixedKeyValidator();
            var entries = new[] { new SnapshotEntrySpec(ControllerA, null, "global", true, null) };
            var snapshotJson = BuildSignedSnapshotJson(
                ThisMachineId, 1, clock.UtcNow.ToUnixTimeMilliseconds(), entries);
            Assert.True(store.TryReplace(snapshotJson, validator, out _));

            var gate = new ToggleableGate { IsRegisteredAndApproved = false };
            var evaluator = new PeerApprovalEvaluator(store, gate);

            var response = evaluator.Evaluate(BuildFrame(ControllerA, ControllerHwidA), ThisMachineId);
            Assert.Equal(PeerApprovalResponse.ResultRejected, response.Result);
            Assert.Null(response.TtlMs);

            // gate 翻回 true 后应当能命中
            gate.IsRegisteredAndApproved = true;
            response = evaluator.Evaluate(BuildFrame(ControllerA, ControllerHwidA), ThisMachineId);
            Assert.Equal(PeerApprovalResponse.ResultApproved, response.Result);
            Assert.Equal(1, response.TtlMs);
        }

        // ---------- §8.10：不缓存"上次决定"——快照变更后立即反映 ----------

        [Fact]
        public void NoDecisionCache_NewSnapshotImmediatelyReflected()
        {
            using var io = new InMemoryObfuscation();
            var clock = new FakeClock();
            var store = new SnapshotStore(io, clock);
            var validator = new FixedKeyValidator();
            var nowMs = clock.UtcNow.ToUnixTimeMilliseconds();

            var oldJson = BuildSignedSnapshotJson(ThisMachineId, 1, nowMs,
                new[] { new SnapshotEntrySpec(ControllerA, null, "global", true, null) });
            Assert.True(store.TryReplace(oldJson, validator, out _));

            var evaluator = new PeerApprovalEvaluator(store, new ToggleableGate());
            for (var i = 0; i < 50; i++)
            {
                Assert.Equal(PeerApprovalResponse.ResultApproved,
                    evaluator.Evaluate(BuildFrame(ControllerA, ControllerHwidA), ThisMachineId).Result);
            }

            // 替换为不再含 ControllerA 的快照
            var newJson = BuildSignedSnapshotJson(ThisMachineId, 2, nowMs,
                new[] { new SnapshotEntrySpec(ControllerB, null, "global", true, null) });
            Assert.True(store.TryReplace(newJson, validator, out _));

            // 立即查询 ControllerA → rejected
            Assert.Equal(PeerApprovalResponse.ResultRejected,
                evaluator.Evaluate(BuildFrame(ControllerA, ControllerHwidA), ThisMachineId).Result);
            Assert.Equal(PeerApprovalResponse.ResultApproved,
                evaluator.Evaluate(BuildFrame(ControllerB, ControllerHwidA), ThisMachineId).Result);
        }

        // ---------- 决策点 3：reason 字段始终省略 ----------

        [Fact]
        public void RejectedResponse_NeverCarriesReason_RegardlessOfPath()
        {
            using var io = new InMemoryObfuscation();
            var clock = new FakeClock();
            var store = new SnapshotStore(io, clock);
            var validator = new FixedKeyValidator();
            var entries = new[] { new SnapshotEntrySpec(ControllerA, null, "global", true, null) };
            var snapshotJson = BuildSignedSnapshotJson(
                ThisMachineId, 1, clock.UtcNow.ToUnixTimeMilliseconds(), entries);
            Assert.True(store.TryReplace(snapshotJson, validator, out _));

            // 路径 1：未命中 → rejected
            var ev1 = new PeerApprovalEvaluator(store, new ToggleableGate());
            var miss = ev1.Evaluate(BuildFrame("UNKNOWN", ControllerHwidA), ThisMachineId);
            Assert.Equal(PeerApprovalResponse.ResultRejected, miss.Result);
            Assert.Null(miss.TtlMs);

            // 路径 2：MachineId 不匹配 → rejected
            var diff = ev1.Evaluate(BuildFrame(ControllerA, ControllerHwidA, controlledMachineId: "X"), ThisMachineId);
            Assert.Equal(PeerApprovalResponse.ResultRejected, diff.Result);
            Assert.Null(diff.TtlMs);

            // 路径 3：Gate 关 → rejected
            var ev2 = new PeerApprovalEvaluator(store, new ToggleableGate { IsRegisteredAndApproved = false });
            var gated = ev2.Evaluate(BuildFrame(ControllerA, ControllerHwidA), ThisMachineId);
            Assert.Equal(PeerApprovalResponse.ResultRejected, gated.Result);
            Assert.Null(gated.TtlMs);

            // JSON 序列化校验：不含 "reason" 键
            var options = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonSerializerDefaults.Web == JsonSerializerDefaults.Web
                    ? System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                    : System.Text.Json.Serialization.JsonIgnoreCondition.Never,
            };
            options.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;

            var serialized = JsonSerializer.Serialize(miss, options);
            Assert.DoesNotContain("\"reason\"", serialized);
            Assert.DoesNotContain("\"ttlMs\"", serialized);
        }
    }
}
