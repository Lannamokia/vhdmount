using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FsCheck;
using FsCheck.Xunit;
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
    /// Property 14: Secret_Version 热轮换正确性。
    ///
    /// Validates: Requirements 13.4, 13.5, 13.6
    ///
    /// (a) 切换后旧版本帧（用 S_old 算 proof）经过 HmacVerifier 必失败
    /// (b) 新版本帧（用 S_new 算 proof）经过 HmacVerifier 必被接受
    /// (c) 切换瞬间 SnapshotStore.Invalidate() 被调用 → IsHealthy == false → PeerApproval 全 rejected
    /// (d) RevocationPublisher.PushSecretOutdatedAsync() 推送一次合法 RevocationFrame，
    ///     连续两次同 (reason, secretVersion) 第二次为 no-op
    ///
    /// 工程权衡：BridgeSecretClient / BridgeSecretRotator 都是 sealed 且依赖 TPM 句柄，无法直接构造。
    /// 本测试用一个最小可控的 <c>MutableSecretProvider</c> 实现 <see cref="IBridgeSecretProvider"/>，
    /// 等价于 BridgeSecretClient 的运行期热轮换语义；手工调用 <see cref="SnapshotStore.Invalidate"/> +
    /// <see cref="RevocationPublisher.PushSecretOutdatedAsync"/>，等价于 BridgeSecretRotator
    /// 在检测到版本变化时的级联动作。
    /// </summary>
    [Trait("feature", "rustdesk-bridge-host")]
    [Trait("property", "Property 14: Secret_Version 热轮换")]
    public sealed class SecretRotationProperty
    {
        private const string MachineId = "MACHINE-DEADBEEF";

        // 跨 property test 复用 RSA-2048（keygen 较慢）
        private static readonly Lazy<RSA> PolicySigner = new(() => RSA.Create(2048));

        private sealed class FakeClock : IClock
        {
            public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1730000000000L);
        }

        /// <summary>
        /// 可切换 active secret + version 的提供者。模拟 BridgeSecretClient 的运行期热轮换。
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
                lock (_gate)
                {
                    return _active.AsSpan();
                }
            }

            public void Switch(byte[] newSecret, uint newVersion)
            {
                if (newSecret == null) throw new ArgumentNullException(nameof(newSecret));
                lock (_gate)
                {
                    CryptographicOperations.ZeroMemory(_active);
                    _active = (byte[])newSecret.Clone();
                    _version = newVersion;
                }
            }
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

        private sealed class AllowGate : IRegistrationGate
        {
            public bool IsRegisteredAndApproved => true;
        }

        private static byte[] DerivedSecret(int seed)
        {
            var bytes = new byte[32];
            var src = SHA256.HashData(BitConverter.GetBytes(seed));
            for (var i = 0; i < 32; i++) bytes[i] = src[i % src.Length];
            return bytes;
        }

        private static HandshakeFrame BuildHandshakeFrame(
            uint secretVersion, byte[] secret, string nonce, long timestampMs)
        {
            var input = HmacVerifier.BuildHandshakeHmacInput(secretVersion, nonce, timestampMs);
            var proof = HmacVerifier.ComputeMacBase64WithKey(secret, input);
            return new HandshakeFrame
            {
                Protocol = HandshakeFrame.ProtocolLiteral,
                SecretVersion = secretVersion,
                Nonce = nonce,
                TimestampMs = timestampMs,
                ClientKind = "rustdesk",
                ClientVersion = "test/1.0",
                Proof = proof,
            };
        }

        // ---------- (a)(b) HmacVerifier 在切换前后的接受/拒绝 ----------

        [Property(MaxTest = 30)]
        public Property OldSecret_ProofRejected_AfterRotation(int seedOld, int seedNew, byte versionDeltaSeed)
        {
            // 强制 versionOld != versionNew + secret 不同
            var versionOld = (uint)(seedOld & 0xff) + 1u;
            var versionNew = versionOld + (uint)(versionDeltaSeed | 1);
            if (seedOld == seedNew) seedNew = seedOld + 1;

            var sOld = DerivedSecret(seedOld);
            var sNew = DerivedSecret(seedNew);

            var provider = new MutableSecretProvider(sOld, versionOld);
            var verifier = new HmacVerifier(provider);

            // 用 sOld 签的握手帧在切换前应当被接受
            var frameOld = BuildHandshakeFrame(versionOld, sOld, "nonce-pre-rotation", 1730000000000L);
            var preOk = verifier.VerifyHandshake(frameOld);
            if (!preOk) return false.ToProperty();

            // 切换 active 槽到 (sNew, versionNew)
            provider.Switch(sNew, versionNew);

            // 切换后：旧 frame 失败（HmacVerifier.VerifyHandshake 内部判断 secretVersion 不等就直接返回 false）
            var afterOldFrameRejected = !verifier.VerifyHandshake(frameOld);

            // 切换后：用 sNew + versionNew 签的新 frame 接受
            var frameNew = BuildHandshakeFrame(versionNew, sNew, "nonce-post-rotation", 1730000000500L);
            var afterNewFrameAccepted = verifier.VerifyHandshake(frameNew);

            return (afterOldFrameRejected && afterNewFrameAccepted).ToProperty();
        }

        // ---------- (c) SnapshotStore.Invalidate 抹零 + IsHealthy=false ----------

        [Fact]
        public void Rotation_InvalidatesSnapshot_PeerApprovalAllRejected()
        {
            using var io = new InMemoryObfuscation();
            var clock = new FakeClock();
            var store = new SnapshotStore(io, clock);
            var validator = new FakeValidator();

            var nowMs = clock.UtcNow.ToUnixTimeMilliseconds();
            var snapshotJson = BuildSignedSnapshotJson(MachineId, 1, nowMs);
            Assert.True(store.TryReplace(snapshotJson, validator, out _));
            Assert.True(store.IsHealthy);

            store.Invalidate();
            Assert.False(store.IsHealthy);

            var ev = new PeerApprovalEvaluator(store, new AllowGate());
            var resp = ev.Evaluate(BuildPeerApprovalFrame("CONTROLLER-A"), MachineId);
            Assert.Equal(PeerApprovalResponse.ResultRejected, resp.Result);
            Assert.Null(resp.TtlMs);
        }

        // ---------- (d) RevocationPublisher 推送一次 + 第二次去重 ----------

        [Fact]
        public async System.Threading.Tasks.Task RevocationPublisher_SecretOutdated_PushedOnce_SecondCallIsNoop()
        {
            using var io = new InMemoryObfuscation();
            var clock = new FakeClock();
            var store = new SnapshotStore(io, clock);

            var sNew = DerivedSecret(0xC0FFEE);
            var provider = new MutableSecretProvider(sNew, initialVersion: 2u);
            var verifier = new HmacVerifier(provider);

            var (buffer, dir) = BuildBuffer();
            try
            {
                var cancelInflightCalled = 0;
                var publisher = new RevocationPublisher(
                    verifier,
                    store,
                    buffer,
                    clock,
                    provider,
                    getActiveSession: () => null,
                    cancelInflightRequests: ct =>
                    {
                        cancelInflightCalled++;
                        return System.Threading.Tasks.Task.CompletedTask;
                    });

                var first = await publisher.PushSecretOutdatedAsync();
                Assert.True(first, "第一次推送 secret_outdated 必须实际发出");
                Assert.Equal(1, cancelInflightCalled);

                var second = await publisher.PushSecretOutdatedAsync();
                Assert.False(second, "连续两次相同 (reason, secretVersion) 第二次必须 no-op");
                Assert.Equal(1, cancelInflightCalled);

                provider.Switch(sNew, 3u);
                var third = await publisher.PushSecretOutdatedAsync();
                Assert.True(third, "secretVersion 变化后再推必须实际发出");
                Assert.Equal(2, cancelInflightCalled);

                var pending = buffer.GetPendingBatch(buffer.CurrentSessionId, 0, 100);
                Assert.Contains(pending, e => e.EventKey == "revocation_pushed");
                foreach (var e in pending.Where(p => p.EventKey == "revocation_pushed"))
                {
                    Assert.DoesNotContain("controllerId", e.Message);
                    Assert.DoesNotContain("controllerName", e.Message);
                }
            }
            finally
            {
                buffer.Dispose();
                Cleanup(dir);
            }
        }

        // ---------- (a)+(c)+(d) 联合：完整轮换序列 ----------

        [Property(MaxTest = 10)]
        public Property FullRotation_HandlesRevocationAndSnapshotInvalidate(int seedOld, int seedNew)
        {
            if (seedOld == seedNew) seedNew = seedOld + 1;

            var sOld = DerivedSecret(seedOld);
            var sNew = DerivedSecret(seedNew);
            var provider = new MutableSecretProvider(sOld, 1u);
            var verifier = new HmacVerifier(provider);

            using var io = new InMemoryObfuscation();
            var clock = new FakeClock();
            var store = new SnapshotStore(io, clock);
            var validator = new FakeValidator();

            var nowMs = clock.UtcNow.ToUnixTimeMilliseconds();
            var snapshotJson = BuildSignedSnapshotJson(MachineId, 1, nowMs);
            if (!store.TryReplace(snapshotJson, validator, out _)) return false.ToProperty();
            if (!store.IsHealthy) return false.ToProperty();

            var oldFrame = BuildHandshakeFrame(1u, sOld, "n-old", nowMs);
            if (!verifier.VerifyHandshake(oldFrame)) return false.ToProperty();

            // 模拟 Rotator 的级联：(a) Invalidate snapshot；(b) 切换 active；
            store.Invalidate();
            provider.Switch(sNew, 2u);

            var oldRejected = !verifier.VerifyHandshake(oldFrame);
            var newFrame = BuildHandshakeFrame(2u, sNew, "n-new", nowMs + 1000);
            var newAccepted = verifier.VerifyHandshake(newFrame);
            var snapshotInvalidated = !store.IsHealthy;

            return (oldRejected && newAccepted && snapshotInvalidated).ToProperty();
        }

        // ---------- 内部辅助 ----------

        private static (MachineLogBuffer buffer, string dir) BuildBuffer()
        {
            var dir = Path.Combine(Path.GetTempPath(), "vhdm-bridge-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var spoolPath = Path.Combine(dir, "spool.jsonl");
            return (new MachineLogBuffer(spoolPath, "test-session-rotation", 1024 * 1024), dir);
        }

        private static void Cleanup(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch (IOException) { /* ignore */ }
        }

        private static string BuildSignedSnapshotJson(string machineId, long snapshotSeq, long issuedAtMs)
        {
            using var entriesMs = new MemoryStream();
            using (var w = new Utf8JsonWriter(entriesMs, new JsonWriterOptions { Indented = false }))
            {
                w.WriteStartArray();
                w.WriteStartObject();
                w.WriteString("controllerId", "TRUSTED-CTRL");
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
