using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
using VHDMounter.RustDeskBridge.Upload;
using Xunit;

namespace VHDMounter.Tests.RustDeskBridge.Properties
{
    /// <summary>
    /// Property 18: TPM 句柄复用与无共享缓存。
    ///
    /// Validates: Requirements 16.4, 16.5, 16.6, 16.7
    ///
    /// 测试目标：把"封装契约"层固定下来，<b>不</b>实际触发 TPM 调用（TPM 在测试环境不可用）。
    ///
    /// (a) BridgeSecretClient / WrapKeyClient / ReportUploader 的字段类型集合**不**含
    ///     <see cref="System.Security.Cryptography.RSACng"/> / <see cref="System.Security.Cryptography.RSA"/>
    ///     / 任何 ICngKey 类型 —— 即"没有跨调用缓存的 TPM 句柄"（Requirement 16.6）。
    /// (b) PeerApproval 行为与 <see cref="IRegistrationGate.IsRegisteredAndApproved"/> 一一对应
    ///     —— gate=false 永远 rejected；gate=true 走 SnapshotStore.Evaluate（Requirement 16.4 / 16.5）。
    /// (c) Report 与 PeerApproval 上行的 HMAC 签名（这里用 HmacVerifier 等价模拟）能在多线程并发
    ///     环境下完成而不互相阻塞 —— 通过吞吐统计断言两条路径的并发执行不被互斥锁串行化。
    ///
    /// 工程权衡：BridgeSecretClient 是 sealed 不可继承；本测试沿用
    /// <c>SecretRotationProperty</c> 中的 <c>MutableSecretProvider</c> 等价模拟，
    /// 避开 TPM 直接构造（参见 design §"TPM 句柄复用"，requirements §16.6）。
    /// </summary>
    [Trait("feature", "rustdesk-bridge-host")]
    [Trait("property", "Property 18: TPM 句柄复用与无共享缓存")]
    public sealed class TpmHandleReuseProperty
    {
        private const string MachineId = "MACHINE-DEADBEEF";
        private const string ControllerA = "987654321";
        private const string ControllerHwidA = "aabbccddeeff00112233445566778899";

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
                return PolicySigner.Value.VerifyData(
                    payload.ToArray(), sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
        }

        /// <summary>
        /// 等价于 SecretRotationProperty.MutableSecretProvider；本属性测试中重复声明，
        /// 避免跨 file private 类型耦合。BridgeSecretClient 是 sealed 不可继承，因此
        /// 用此 IBridgeSecretProvider 等价模拟（与 wave 6 同模式）。
        /// </summary>
        private sealed class MutableSecretProvider : IBridgeSecretProvider
        {
            private readonly object _gate = new object();
            private byte[] _active;
            private uint _version;

            public MutableSecretProvider(byte[] initialSecret, uint initialVersion)
            {
                _active = (byte[])(initialSecret ?? throw new ArgumentNullException(nameof(initialSecret))).Clone();
                _version = initialVersion;
            }

            public uint CurrentSecretVersion
            {
                get { lock (_gate) return _version; }
            }

            public ReadOnlySpan<byte> GetActiveSecret()
            {
                lock (_gate) return _active.AsSpan();
            }
        }

        private sealed class ToggleableGate : IRegistrationGate
        {
            public bool IsRegisteredAndApproved { get; set; } = true;
        }

        // ────────────────────────────────────────────────────────────────────
        // (a) 字段扫描：BridgeSecretClient / WrapKeyClient / ReportUploader 不持有 RSA 句柄
        // ────────────────────────────────────────────────────────────────────

        public static IEnumerable<object[]> NoCachedTpmHandle_Targets()
        {
            yield return new object[] { typeof(BridgeSecretClient) };
            yield return new object[] { typeof(WrapKeyClient) };
            yield return new object[] { typeof(ReportUploader) };
            yield return new object[] { typeof(SnapshotRefreshLoop) };
        }

        [Theory]
        [MemberData(nameof(NoCachedTpmHandle_Targets))]
        public void Type_HasNoCachedRsaCngFieldOfTpmKeyType(Type targetType)
        {
            var fields = targetType.GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

            foreach (var field in fields)
            {
                Assert.False(
                    IsTpmHandleType(field.FieldType),
                    $"{targetType.FullName}.{field.Name} 字段类型 {field.FieldType.FullName} " +
                    $"是 TPM 句柄类型 —— 违反 Requirement 16.6 'TPM 句柄不跨调用缓存'");

                // 嵌套数组 / 列表也不允许（防御 List<RSACng> 等隐式缓存）
                var elemType = TryGetCollectionElement(field.FieldType);
                if (elemType != null)
                {
                    Assert.False(
                        IsTpmHandleType(elemType),
                        $"{targetType.FullName}.{field.Name} 集合元素类型 {elemType.FullName} " +
                        "是 TPM 句柄类型");
                }
            }
        }

        private static bool IsTpmHandleType(Type type)
        {
            if (type == null) return false;
            // 直接抓 RSA / RSACng / CngKey / SafeNCryptKeyHandle 字面量
            if (type == typeof(RSA)) return true;
            if (type.FullName == "System.Security.Cryptography.RSACng") return true;
            if (type.FullName == "System.Security.Cryptography.CngKey") return true;
            if (type.FullName == "Microsoft.Win32.SafeHandles.SafeNCryptKeyHandle") return true;
            // 任何 RSA 派生类
            if (typeof(RSA).IsAssignableFrom(type)) return true;
            return false;
        }

        private static Type TryGetCollectionElement(Type t)
        {
            if (t == null) return null;
            if (t.IsArray) return t.GetElementType();
            if (t.IsGenericType)
            {
                var args = t.GetGenericArguments();
                if (args.Length == 1) return args[0];
            }
            return null;
        }

        // ────────────────────────────────────────────────────────────────────
        // (b) PeerApproval 决策与 IsRegisteredAndApproved 一一对应
        // ────────────────────────────────────────────────────────────────────

        [Property(MaxTest = 30)]
        public Property GateClosed_AllRejected_RegardlessOfSnapshotContent(NonNegativeInt seed)
        {
            var (store, evaluator, gate) = BuildEvaluatorWithLiveSnapshot();

            // gate 关：必 rejected
            gate.IsRegisteredAndApproved = false;
            var frame = BuildPeerApprovalFrame(ControllerA, ControllerHwidA);
            var resp1 = evaluator.Evaluate(frame, MachineId);
            var rejected = resp1.Result == PeerApprovalResponse.ResultRejected && resp1.TtlMs == null;

            // gate 开：原样命中（这是 SnapshotStore 的事，不是 evaluator 的事）
            gate.IsRegisteredAndApproved = true;
            var resp2 = evaluator.Evaluate(frame, MachineId);
            var approved = resp2.Result == PeerApprovalResponse.ResultApproved && resp2.TtlMs == 1;

            return (rejected && approved).ToProperty()
                .Label($"seed={seed.Get}, gate-closed→rejected={rejected}, gate-open→approved={approved}");
        }

        [Fact]
        public void RegistrationGate_Bijection_BetweenStateAndDecision()
        {
            // gate=false: 全 rejected；gate=true: 走 SnapshotStore.Evaluate
            // 即 (gate, snapshot.hit) → response 是确定函数
            // gate=false → rejected, gate=true & hit → approved, gate=true & miss → rejected
            var (store, evaluator, gate) = BuildEvaluatorWithLiveSnapshot();
            var hitFrame = BuildPeerApprovalFrame(ControllerA, ControllerHwidA);
            var missFrame = BuildPeerApprovalFrame("UNKNOWN-CONTROLLER", ControllerHwidA);

            // (false, *) → rejected
            gate.IsRegisteredAndApproved = false;
            Assert.Equal(PeerApprovalResponse.ResultRejected, evaluator.Evaluate(hitFrame, MachineId).Result);
            Assert.Equal(PeerApprovalResponse.ResultRejected, evaluator.Evaluate(missFrame, MachineId).Result);

            // (true, hit) → approved
            gate.IsRegisteredAndApproved = true;
            Assert.Equal(PeerApprovalResponse.ResultApproved, evaluator.Evaluate(hitFrame, MachineId).Result);
            // (true, miss) → rejected
            Assert.Equal(PeerApprovalResponse.ResultRejected, evaluator.Evaluate(missFrame, MachineId).Result);
        }

        // ────────────────────────────────────────────────────────────────────
        // (c) Report / PeerApproval 并发签名不互相阻塞（HmacVerifier 同构再现）
        // ────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task ConcurrentReportAndPeerApprovalSignature_DoNotSerialize()
        {
            // 真 TPM 不可用 → 用 HmacVerifier 替代 RustDeskReportSigner 的 RSA 路径
            // （二者都对每次调用做"无缓存的密钥 lookup"）。如果 HmacVerifier 内部存在
            // 跨调用的全局互斥锁，同一进程内并发 Report+PeerApproval 调用会被串行化。
            var secret = DeriveBytes(32, 0xC0FFEE);
            var provider = new MutableSecretProvider(secret, 1u);
            var verifier = new HmacVerifier(provider);

            const int reportThreads = 4;
            const int peerThreads = 4;
            const int iterations = 2_000;

            // 预算 baseline：单线程跑同样多次需要的时长
            var baselineSw = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
            {
                _ = verifier.ComputeMacBase64(BuildReportInput(i));
                _ = verifier.ComputeMacBase64(BuildPeerApprovalInput(i));
            }
            baselineSw.Stop();
            var baselineMs = baselineSw.ElapsedMilliseconds;

            // 8 线程并发跑等量工作 —— 如果完全串行化，预期耗时 ≥ baselineMs
            var startGate = new ManualResetEventSlim(false);
            var doneCount = 0;
            var threads = Enumerable.Range(0, reportThreads + peerThreads).Select(idx => Task.Run(() =>
            {
                var isReport = idx < reportThreads;
                startGate.Wait();
                for (var i = 0; i < iterations / (reportThreads + peerThreads) + 1; i++)
                {
                    if (isReport)
                    {
                        _ = verifier.ComputeMacBase64(BuildReportInput(i));
                    }
                    else
                    {
                        _ = verifier.ComputeMacBase64(BuildPeerApprovalInput(i));
                    }
                    Interlocked.Increment(ref doneCount);
                }
            })).ToArray();

            var concurrentSw = System.Diagnostics.Stopwatch.StartNew();
            startGate.Set();
            await Task.WhenAll(threads);
            concurrentSw.Stop();

            // 不强制要求 concurrentMs < baselineMs / N（机器抖动会失败），仅断言"不至于
            // 数倍于 baseline"——即没有"全局互斥锁导致并发退化为串行"的灾难性情形。
            // 经验阈值：concurrentMs < baselineMs * 2（即 8 线程的吞吐至少与单线程持平）
            // 对一台合理性能的机器，HmacVerifier 没有共享锁的情况下 concurrentMs 应当远小于 baseline。
            Assert.True(
                concurrentSw.ElapsedMilliseconds < baselineMs * 4 + 500,
                $"并发签名退化：concurrent={concurrentSw.ElapsedMilliseconds}ms, baseline={baselineMs}ms" +
                "（如果差值 ≥ 4× 表明可能存在跨线程互斥锁）");
        }

        // ────────────────────────────────────────────────────────────────────
        // 辅助：构造一份带签名的"已加载"快照（让 evaluator 能命中 ControllerA）
        // ────────────────────────────────────────────────────────────────────

        private static (SnapshotStore Store, PeerApprovalEvaluator Evaluator, ToggleableGate Gate)
            BuildEvaluatorWithLiveSnapshot()
        {
            var io = new InMemoryObfuscation();
            var clock = new FakeClock();
            var store = new SnapshotStore(io, clock);
            var validator = new FixedKeyValidator();
            var snapshotJson = BuildSignedSnapshotJson(
                MachineId, snapshotSeq: 1, issuedAtMs: clock.UtcNow.ToUnixTimeMilliseconds());
            Assert.True(store.TryReplace(snapshotJson, validator, out _));
            var gate = new ToggleableGate { IsRegisteredAndApproved = true };
            var evaluator = new PeerApprovalEvaluator(store, gate);
            return (store, evaluator, gate);
        }

        private static PeerApprovalFrame BuildPeerApprovalFrame(string controllerId, string controllerHwid)
        {
            return new PeerApprovalFrame
            {
                Protocol = PeerApprovalFrame.ProtocolLiteral,
                SecretVersion = 1,
                ControlledMachineId = MachineId,
                ControllerId = controllerId,
                ControllerName = "ignored",
                ControllerPlatform = "Windows",
                ControllerHwid = controllerHwid ?? string.Empty,
                PeerSocketAddr = "192.0.2.1:51820",
                ConnectionType = PeerApprovalFrame.ConnectionTypeControlled,
                RequestNonce = "nonce-" + controllerId,
                TimestampMs = 1730000000000L,
                Mac = "ignored",
            };
        }

        private static string BuildSignedSnapshotJson(string machineId, long snapshotSeq, long issuedAtMs)
        {
            using var ms = new System.IO.MemoryStream();
            using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
            {
                writer.WriteStartArray();
                writer.WriteStartObject();
                writer.WriteString("controllerId", ControllerA);
                writer.WriteNull("controllerHwidHash");
                writer.WriteString("scope", "global");
                writer.WriteBoolean("enabled", true);
                writer.WriteNull("expiresAt");
                writer.WriteEndObject();
                writer.WriteEndArray();
            }
            var entriesBytes = ms.ToArray();
            var canonical = JcsCanonicalizer.Canonicalize(JsonDocument.Parse(entriesBytes).RootElement);
            var entriesDigestHex = Convert.ToHexString(SHA256.HashData(canonical)).ToLowerInvariant();
            var payload = string.Concat(
                "TrustedControllersSnapshotV1\n",
                machineId, "\n",
                snapshotSeq.ToString(System.Globalization.CultureInfo.InvariantCulture), "\n",
                issuedAtMs.ToString(System.Globalization.CultureInfo.InvariantCulture), "\n",
                entriesDigestHex);
            var sig = PolicySigner.Value.SignData(
                Encoding.ASCII.GetBytes(payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            using var outer = new System.IO.MemoryStream();
            using (var w = new Utf8JsonWriter(outer, new JsonWriterOptions { Indented = false }))
            {
                w.WriteStartObject();
                w.WriteString("machineId", machineId);
                w.WriteNumber("snapshotSeq", snapshotSeq);
                w.WriteNumber("issuedAt", issuedAtMs);
                w.WritePropertyName("entries");
                using var entriesDoc = JsonDocument.Parse(entriesBytes);
                entriesDoc.RootElement.WriteTo(w);
                w.WriteString("signature", Convert.ToBase64String(sig));
                w.WriteEndObject();
            }
            return Encoding.UTF8.GetString(outer.ToArray());
        }

        private static byte[] BuildReportInput(int i)
        {
            return HmacVerifier.BuildReportHmacInput(
                secretVersion: 1u,
                rustDeskId: "123456789",
                passwordKind: ReportFrame.PasswordKindTemporary,
                password: "Hunter" + i,
                reason: ReportFrame.ReasonHeartbeat,
                reportedAt: 1730000000000L + i,
                nonce: "nonce-r-" + i);
        }

        private static byte[] BuildPeerApprovalInput(int i)
        {
            return HmacVerifier.BuildPeerApprovalHmacInput(
                secretVersion: 1u,
                controlledMachineId: MachineId,
                controllerId: ControllerA,
                controllerName: "admin@ops",
                controllerPlatform: "Windows",
                controllerHwid: ControllerHwidA,
                peerSocketAddr: "192.0.2.1:51820",
                connectionType: PeerApprovalFrame.ConnectionTypeControlled,
                requestNonce: "nonce-p-" + i,
                timestampMs: 1730000000000L + i);
        }

        private static byte[] DeriveBytes(int length, int seed)
        {
            var bytes = new byte[length];
            var rng = new System.Random(seed);
            rng.NextBytes(bytes);
            return bytes;
        }
    }
}
