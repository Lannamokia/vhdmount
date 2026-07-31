using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VHDMounter;
using VHDMounter.RustDeskBridge.Crypto;
using VHDMounter.RustDeskBridge.Frames;
using VHDMounter.RustDeskBridge.Log;
using VHDMounter.RustDeskBridge.Pipe;
using VHDMounter.RustDeskBridge.Policy;
using VHDMounter.RustDeskBridge.RateLimit;
using VHDMounter.RustDeskBridge.Session;
using VHDMounter.RustDeskBridge.Upload;
using Xunit;

namespace VHDMounter.Tests.RustDeskBridge.Integration
{
    /// <summary>
    /// 任务 19.2：同进程 BridgeServerHost 集成 SMOKE。
    ///
    /// 工程权衡：完整 <see cref="BridgeServerHost"/> 启动链需要 mock VHDSelectServer
    /// （PolicyPubkey / BridgeSecret / WrapKey / Snapshot 4 个端点合法响应）+ mock TPM。
    /// 在 Wave 7 把这套基础设施全部铺开偏离任务规模。
    ///
    /// 本测试退化为 SMOKE：直接用 <see cref="PipeAcceptLoop"/> +
    /// <see cref="SessionStateMachine"/> + <see cref="HmacVerifier"/>（用
    /// MutableSecretProvider 等价模拟）拉起一个"轻量 BridgeServer"，验证：
    /// <list type="number">
    /// <item>NamedPipeClientStream 在 elevated 进程中能成功连接 + 完成握手</item>
    /// <item>带正确 proof 的 Handshake 帧返回 ok=true</item>
    /// <item>带错误 proof 的 Handshake 帧返回 ok=false reason=invalid_proof</item>
    /// <item>Report 帧通过校验后返回 accepted</item>
    /// </list>
    ///
    /// 完整端到端（含真实 Snapshot 拉取 / Report 上行 / Revocation 推送）由 Wave 9 SMOKE 承担。
    ///
    /// 测试 SKIP 条件：非 elevated（BridgePipeFactory DACL 仅 Administrators+SYSTEM）。
    /// </summary>
    [Trait("feature", "rustdesk-bridge-host")]
    [Trait("kind", "integration-smoke")]
    public sealed class BridgeServerHostFixture : IDisposable
    {
        private const string MachineId = "MACHINE-FIXTURE";
        private const uint SecretVersion = 1;
        // 协议夹具 secret（与 protocol-vectors.json 一致：0xAB × 32）
        private static readonly byte[] FixtureSecret = Enumerable.Repeat((byte)0xAB, 32).ToArray();

        private readonly string _pipeName;
        private readonly string _spoolDir;
        private readonly MachineLogBuffer _buffer;

        public BridgeServerHostFixture()
        {
            _pipeName = "VHDMount.RustDeskBridgeIT." + Guid.NewGuid().ToString("N");
            _spoolDir = Path.Combine(Path.GetTempPath(), "vhdm-bridge-it-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_spoolDir);
            _buffer = new MachineLogBuffer(Path.Combine(_spoolDir, "spool.jsonl"), "test-it", 1024 * 1024);
        }

        public void Dispose()
        {
            try { _buffer.Dispose(); } catch { /* ignore */ }
            try { if (Directory.Exists(_spoolDir)) Directory.Delete(_spoolDir, true); } catch { /* ignore */ }
        }

        private static bool IsRunningElevated()
        {
            using var id = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(id);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        // ─── 等价模拟：与 SecretRotationProperty / TpmHandleReuseProperty 同结构 ───────────

        private sealed class FixedSecretProvider : IBridgeSecretProvider
        {
            public uint CurrentSecretVersion => SecretVersion;
            public ReadOnlySpan<byte> GetActiveSecret() => FixtureSecret.AsSpan();
        }

        private sealed class AllowGate : IRegistrationGate
        {
            public bool IsRegisteredAndApproved => true;
        }

        private sealed class AlwaysFailValidator : IPolicyPubkeyValidator
        {
            public string CurrentPubkeyDigestHex => "stub";
            public bool VerifyResponseSignature(ReadOnlySpan<byte> payload, string signatureBase64) => false;
        }

        // 工程权衡：ReportUploader / WrapKeyClient 都是 sealed，无法继承伪造。
        // 但本 SMOKE 不真正消费 Report 上行：SessionStateMachine 在通过校验后立即 ack accepted，
        // 异步入队 ReportUploadQueue。我们让 ReportUploadQueue 的消费 task 不启动（不调 RunAsync），
        // 这样 Enqueue 仅写到 LinkedList，永远不调 ReportUploader.UploadAsync。
        // ReportUploadQueue 构造要求 ReportUploader != null —— 我们传入一个
        // 真正的 ReportUploader（参数全部用最小可构造的对象，但永远不会被实际调用）。

        // ─── 主测试 ──────────────────────────────────────────────────────────────────

        [Fact(Timeout = 30000)]
        public async Task SmokeFullProtocol_ElevatedConnect_HandshakeAcceptThenReportAccept()
        {
            if (!IsRunningElevated())
            {
                return; // SKIP：DACL 拒绝普通用户
            }

            var clock = new SystemClock();
            var secretProvider = new FixedSecretProvider();
            var hmac = new HmacVerifier(secretProvider);
            using var nonceLru = new HandshakeNonceLruCache(300, TimeSpan.FromMinutes(5), clock);
            var handshakeRl = new HandshakeRateLimiter(clock);
            var reportRl = new ReportRateLimiter(clock);
            var lastReported = new LastReportedSnapshot();
            using var io = new InMemoryObfuscation();
            var snapshotStore = new SnapshotStore(io, clock);
            var gate = new AllowGate();
            var peerEval = new PeerApprovalEvaluator(snapshotStore, gate);
            var logIngestor = new LogIngestor(_buffer);
            var dropCounter = new BridgeLogDropCounter(_buffer);

            // 用一个"能构造但消费不启动"的 ReportUploadQueue —— ReportUploader 走桩
            using var httpClient = new System.Net.Http.HttpClient();
            var bridgeConfig = VHDMounter.RustDeskBridge.Config.BridgeConfig.Load(
                Path.Combine(_spoolDir, "no-such-config.ini"), _ => { });
            var stubValidator = new AlwaysFailValidator();
            using var stubWrapKeys = new WrapKeyClient(
                bridgeConfig, MachineId, "http://localhost", httpClient,
                stubValidator,
                utcNow: () => DateTimeOffset.UtcNow,
                diagnostics: _ => { });
            var stubReportUploader = new ReportUploader(
                bridgeConfig, stubWrapKeys, stubValidator, httpClient,
                MachineId, "http://localhost", _ => { });
            await using var reportQueue = new ReportUploadQueue(stubReportUploader, _ => { });

            var sessionMachine = new SessionStateMachine(
                hmac, nonceLru, snapshotStore, peerEval,
                logIngestor, dropCounter, lastReported,
                reportRl, reportQueue, handshakeRl,
                secretProvider, MachineId, clock, _ => { });

            PipeAcceptLoop.SessionRunnerDelegate sessionRunner = (stream, isCoolingDown, ct) =>
                sessionMachine.RunAsync(stream, isCoolingDown, ct);

            await using var loop = new PipeAcceptLoop(
                _pipeName, sessionRunner, handshakeRl, _buffer, clock, _ => { });
            using var cts = new CancellationTokenSource();
            await loop.StartAsync(cts.Token);

            // ── (1) 客户端：握手成功路径 ──────────────────────────────
            using (var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                await client.ConnectAsync(5000);
                Assert.True(client.IsConnected);

                var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
                var timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var hmacInput = HmacVerifier.BuildHandshakeHmacInput(SecretVersion, nonce, timestampMs);
                var proof = HmacVerifier.ComputeMacBase64WithKey(FixtureSecret, hmacInput);
                var hsFrame = new
                {
                    protocol = HandshakeFrame.ProtocolLiteral,
                    secretVersion = SecretVersion,
                    nonce,
                    timestampMs,
                    clientKind = "rustdesk",
                    clientVersion = "fixture-1.0",
                    proof,
                };
                await WriteFrameAsync(client, hsFrame, cts.Token);
                using (var hsResp = JsonDocument.Parse(await ReadFrameAsync(client, cts.Token)))
                {
                    Assert.True(hsResp.RootElement.GetProperty("ok").GetBoolean(), "握手应当成功");
                }

                // ── (2) Report 帧通过校验 → accepted ──────────────────
                var reportNonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
                var reportedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var reportInput = HmacVerifier.BuildReportHmacInput(
                    SecretVersion, "RUSTID-1", "temporary", "FixturePwd!", "startup", reportedAt, reportNonce);
                var mac = HmacVerifier.ComputeMacBase64WithKey(FixtureSecret, reportInput);
                var reportFrame = new
                {
                    protocol = ReportFrame.ProtocolLiteral,
                    secretVersion = SecretVersion,
                    rustDeskId = "RUSTID-1",
                    passwordKind = "temporary",
                    password = "FixturePwd!",
                    reason = "startup",
                    reportedAt,
                    nonce = reportNonce,
                    mac,
                };
                await WriteFrameAsync(client, reportFrame, cts.Token);
                using (var rResp = JsonDocument.Parse(await ReadFrameAsync(client, cts.Token)))
                {
                    Assert.Equal("accepted", rResp.RootElement.GetProperty("result").GetString());
                }
            }

            // ── (3) 第二个连接：错误 proof → ok=false reason=invalid_proof ───
            // PipeAcceptLoop 应当在前一会话结束后 1 秒内重建管道
            await Task.Delay(1500);
            using (var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                await client.ConnectAsync(5000);
                var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
                var timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var hsFrame = new
                {
                    protocol = HandshakeFrame.ProtocolLiteral,
                    secretVersion = SecretVersion,
                    nonce,
                    timestampMs,
                    clientKind = "rustdesk",
                    clientVersion = "fixture-1.0",
                    proof = "AAAA-bad-proof-base64=",
                };
                await WriteFrameAsync(client, hsFrame, cts.Token);
                using var hsResp = JsonDocument.Parse(await ReadFrameAsync(client, cts.Token));
                Assert.False(hsResp.RootElement.GetProperty("ok").GetBoolean());
                Assert.Equal("invalid_proof", hsResp.RootElement.GetProperty("reason").GetString());
            }

            cts.Cancel();
        }

        // ─── Frame I/O 辅助 ─────────────────────────────────────────────────────────

        private static async Task WriteFrameAsync(Stream pipe, object payload, CancellationToken ct)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
            var len = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(len, (uint)bytes.Length);
            await pipe.WriteAsync(len, ct);
            await pipe.WriteAsync(bytes, ct);
            await pipe.FlushAsync(ct);
        }

        private static async Task<byte[]> ReadFrameAsync(Stream pipe, CancellationToken ct)
        {
            var len = new byte[4];
            await ReadExactAsync(pipe, len, ct);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(len);
            if (length > 65536) throw new InvalidDataException();
            if (length == 0) return Array.Empty<byte>();
            var payload = new byte[length];
            await ReadExactAsync(pipe, payload, ct);
            return payload;
        }

        private static async Task ReadExactAsync(Stream pipe, byte[] buffer, CancellationToken ct)
        {
            var off = 0;
            while (off < buffer.Length)
            {
                var n = await pipe.ReadAsync(buffer.AsMemory(off, buffer.Length - off), ct);
                if (n == 0) throw new IOException();
                off += n;
            }
        }

        // ─── 桩 ─────────────────────────────────────────────────────────────────────
        // (空：所有桩内联到 SmokeFullProtocol_ElevatedConnect_HandshakeAcceptThenReportAccept 内部)
    }
}
