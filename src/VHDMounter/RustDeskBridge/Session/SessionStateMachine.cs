using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using VHDMounter.RustDeskBridge.Crypto;
using VHDMounter.RustDeskBridge.Frames;
using VHDMounter.RustDeskBridge.Log;
using VHDMounter.RustDeskBridge.Pipe;
using VHDMounter.RustDeskBridge.Policy;
using VHDMounter.RustDeskBridge.RateLimit;
using VHDMounter.RustDeskBridge.Upload;

namespace VHDMounter.RustDeskBridge.Session
{
    /// <summary>
    /// 整个 RustDesk 桥的核心整合者：单次 Bridge_Session 帧路由器（任务 9.4）。
    ///
    /// 由 <see cref="PipeAcceptLoop"/> 在每次 ConnectNamedPipe 成功后调用一次
    /// <see cref="RunAsync"/>，完整跑完一次会话生命周期：
    ///
    /// <list type="number">
    /// <item>冷却期分支：<c>isCoolingDown == true</c> → 读首帧（不解析）→
    ///       直接回 <c>HandshakeResponse { ok: false, reason: "rate_limited" }</c> → 关闭管道 → return</item>
    /// <item>第一帧必须是 <c>VHDRustDeskBridgeHandshakeV1</c>，否则视为协议错误关闭（Requirement 4.2）</item>
    /// <item>握手帧字段约束：按 design §"错误码到 reason 字面量的映射表" 路由 reason
    ///       <list type="bullet">
    ///       <item>secretVersion 字段不存在 / 不是 JSON 数字 / 超出 u32 范围 → invalid_proof（Requirement 4.4）</item>
    ///       <item>nonce / timestampMs / clientKind / proof 任一不通过 → invalid_proof（Requirement 4.4 / 4.6 / 4.7）</item>
    ///       <item>clientKind != "rustdesk" → deny（Requirement 4.3）</item>
    ///       <item>secretVersion 是合法 u32 但版本不等 → secret_outdated（Requirement 4.5）</item>
    ///       <item>全部通过 → ok: true + LRU.TryAdd + session.MarkHandshaked</item>
    ///       </list></item>
    /// <item>Handshaked 状态下路由 Report / Log / PeerApproval；未握手收到任一 → 协议错误关闭（Requirement 5.2 / 7.2 / 9.2）</item>
    /// <item>任意帧解码失败 / I/O 异常 → 关闭会话；调用方负责重建管道</item>
    /// </list>
    ///
    /// 任何反向写出都会经过 <see cref="FrameCodec.WriteFrameAsync"/>，由 FrameCodec
    /// 负责 4 字节小端长度前缀外壳。
    /// </summary>
    internal sealed class SessionStateMachine
    {
        private static readonly JsonSerializerOptions ResponseSerializerOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        private readonly HmacVerifier _hmac;
        private readonly HandshakeNonceLruCache _nonceLru;
        private readonly SnapshotStore _snapshots;
        private readonly PeerApprovalEvaluator _peerApprovals;
        private readonly LogIngestor _logs;
        private readonly BridgeLogDropCounter _logDropCounter;
        private readonly LastReportedSnapshot _lastReported;
        private readonly ReportRateLimiter _reportRateLimiter;
        private readonly ReportUploadQueue _reportQueue;
        private readonly HandshakeRateLimiter _handshakeRateLimiter;
        private readonly IBridgeSecretProvider _secretProvider;
        private readonly string _thisMachineId;
        private readonly IClock _clock;
        private readonly Action<string> _diagnostics;

        public SessionStateMachine(
            HmacVerifier hmac,
            HandshakeNonceLruCache nonceLru,
            SnapshotStore snapshots,
            PeerApprovalEvaluator peerApprovals,
            LogIngestor logs,
            BridgeLogDropCounter logDropCounter,
            LastReportedSnapshot lastReported,
            ReportRateLimiter reportRateLimiter,
            ReportUploadQueue reportQueue,
            HandshakeRateLimiter handshakeRateLimiter,
            IBridgeSecretProvider secretProvider,
            string thisMachineId,
            IClock clock,
            Action<string> diagnostics = null)
        {
            _hmac = hmac ?? throw new ArgumentNullException(nameof(hmac));
            _nonceLru = nonceLru ?? throw new ArgumentNullException(nameof(nonceLru));
            _snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
            _peerApprovals = peerApprovals ?? throw new ArgumentNullException(nameof(peerApprovals));
            _logs = logs ?? throw new ArgumentNullException(nameof(logs));
            _logDropCounter = logDropCounter ?? throw new ArgumentNullException(nameof(logDropCounter));
            _lastReported = lastReported ?? throw new ArgumentNullException(nameof(lastReported));
            _reportRateLimiter = reportRateLimiter ?? throw new ArgumentNullException(nameof(reportRateLimiter));
            _reportQueue = reportQueue ?? throw new ArgumentNullException(nameof(reportQueue));
            _handshakeRateLimiter = handshakeRateLimiter ?? throw new ArgumentNullException(nameof(handshakeRateLimiter));
            _secretProvider = secretProvider ?? throw new ArgumentNullException(nameof(secretProvider));
            _thisMachineId = thisMachineId ?? throw new ArgumentNullException(nameof(thisMachineId));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _diagnostics = diagnostics ?? (_ => { });
        }

        /// <summary>
        /// 与 <see cref="PipeAcceptLoop.SessionRunnerDelegate"/> 签名一致。本方法
        /// 完整跑完一次会话；返回（正常 return / 抛异常）后 PipeAcceptLoop 关闭管道并重建。
        /// </summary>
        public async Task RunAsync(NamedPipeServerStream pipe, bool isCoolingDown, CancellationToken ct)
        {
            using var session = new BridgeSession(pipe);
            try
            {
                if (isCoolingDown)
                {
                    await HandleCoolingDownAsync(pipe, ct).ConfigureAwait(false);
                    return;
                }

                // 第一帧：握手
                if (!await HandleHandshakeAsync(pipe, session, ct).ConfigureAwait(false))
                {
                    return;
                }

                // 已握手 → 进入帧路由循环
                await RunHandshakedLoopAsync(pipe, session, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // 进程退出 / Bridge_Server 停止
            }
            catch (InvalidDataException ex)
            {
                _diagnostics($"Bridge session 协议错误：{ex.Message}");
            }
            catch (IOException ex)
            {
                _diagnostics($"Bridge session 管道 I/O 异常：{ex.Message}");
            }
            catch (Exception ex)
            {
                _diagnostics($"Bridge session 未预期异常：{ex.Message}");
            }
            finally
            {
                try { session.Close(); } catch { /* ignore */ }
            }
        }

        // ---------- 冷却期分支（Requirement 14.2） ----------

        private async Task HandleCoolingDownAsync(NamedPipeServerStream pipe, CancellationToken ct)
        {
            // 读首帧但不解析 —— 仅是为了让客户端能等到一个连接级响应再断开
            try
            {
                _ = await FrameCodec.ReadFrameAsync(pipe, ct).ConfigureAwait(false);
            }
            catch (InvalidDataException) { /* 客户端发了非法帧 —— 仍按 rate_limited 回复 */ }
            catch (IOException) { /* 客户端直接断开 */ }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }

            // 不解析、不计 LRU、不算 HMAC、不增 RateLimiter 失败计数 —— 仅写一帧
            await WriteHandshakeResponseAsync(
                pipe, HandshakeResponse.Failure("rate_limited"), ct).ConfigureAwait(false);
        }

        // ---------- 握手 ----------

        private async Task<bool> HandleHandshakeAsync(
            NamedPipeServerStream pipe, BridgeSession session, CancellationToken ct)
        {
            byte[] firstFrame;
            try
            {
                firstFrame = await FrameCodec.ReadFrameAsync(pipe, ct).ConfigureAwait(false);
            }
            catch (InvalidDataException ex)
            {
                _diagnostics($"Bridge 首帧外壳解析失败：{ex.Message}");
                return false; // 协议错误关闭
            }
            catch (IOException ex)
            {
                _diagnostics($"Bridge 首帧读取失败：{ex.Message}");
                return false;
            }

            // (1) 协议字面量 / secretVersion 字段类型校验：用 JsonDocument 二阶段解析，
            //     避免直接 Deserialize 时 secretVersion 字段为 string / float / 缺失被
            //     STJ 视为协议错误（应当走 invalid_proof）。
            using var doc = TryParseJsonDocument(firstFrame, out var parseFailure);
            if (doc == null)
            {
                _diagnostics("Bridge 首帧 JSON 解析失败：" + parseFailure);
                return false; // 协议错误关闭管道（与首帧 protocol 字面量错同等待遇）
            }

            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                _diagnostics("Bridge 首帧不是 JSON 对象");
                return false;
            }

            // 必须严格等于 HandshakeFrame.ProtocolLiteral，否则关闭（Requirement 4.2）
            if (!root.TryGetProperty("protocol", out var protocolEl)
                || protocolEl.ValueKind != JsonValueKind.String
                || !string.Equals(protocolEl.GetString(), HandshakeFrame.ProtocolLiteral, StringComparison.Ordinal))
            {
                _diagnostics("Bridge 首帧 protocol 字段不是 VHDRustDeskBridgeHandshakeV1");
                return false;
            }

            // (2) secretVersion 必须是 u32 范围内的非负整数（Requirement 4.4）
            if (!TryReadUInt32(root, "secretVersion", out var frameSecretVersion))
            {
                await RejectHandshakeAsync(pipe, "invalid_proof", ct).ConfigureAwait(false);
                _handshakeRateLimiter.RecordFailure();
                return false;
            }

            // (3) 取 nonce / timestampMs / clientKind 字段（DTO 反序列化）
            HandshakeFrame frame;
            try
            {
                frame = JsonSerializer.Deserialize<HandshakeFrame>(firstFrame);
            }
            catch (JsonException ex)
            {
                _diagnostics("Bridge 首帧反序列化失败：" + ex.Message);
                await RejectHandshakeAsync(pipe, "invalid_proof", ct).ConfigureAwait(false);
                _handshakeRateLimiter.RecordFailure();
                return false;
            }

            if (frame == null
                || string.IsNullOrEmpty(frame.Nonce)
                || string.IsNullOrEmpty(frame.Proof))
            {
                await RejectHandshakeAsync(pipe, "invalid_proof", ct).ConfigureAwait(false);
                _handshakeRateLimiter.RecordFailure();
                return false;
            }

            // 双 sanity check：DTO 解出的 SecretVersion 应当与 JsonDocument 解出的相同
            if (frame.SecretVersion != frameSecretVersion)
            {
                await RejectHandshakeAsync(pipe, "invalid_proof", ct).ConfigureAwait(false);
                _handshakeRateLimiter.RecordFailure();
                return false;
            }

            // (4) clientKind != "rustdesk" → deny（Requirement 4.3）
            if (!string.Equals(frame.ClientKind, "rustdesk", StringComparison.Ordinal))
            {
                await RejectHandshakeAsync(pipe, "deny", ct).ConfigureAwait(false);
                _handshakeRateLimiter.RecordFailure();
                return false;
            }

            // (5) 时间窗（Requirement 4.6）
            var nowMs = _clock.UtcNow.ToUnixTimeMilliseconds();
            const long TimeWindowMs = 300_000;
            if (Math.Abs(nowMs - frame.TimestampMs) > TimeWindowMs)
            {
                await RejectHandshakeAsync(pipe, "invalid_proof", ct).ConfigureAwait(false);
                _handshakeRateLimiter.RecordFailure();
                return false;
            }

            // (6) secretVersion 是合法 u32 但与本机 active 不等 → secret_outdated（Requirement 4.5）
            if (frame.SecretVersion != _secretProvider.CurrentSecretVersion)
            {
                await RejectHandshakeAsync(pipe, "secret_outdated", ct).ConfigureAwait(false);
                _handshakeRateLimiter.RecordFailure();
                return false;
            }

            // (7) HMAC proof（Requirement 3.1 / 3.4）—— 在 nonce LRU 写入之前先校验，避免错误的 proof 占据 LRU 槽位
            if (!_hmac.VerifyHandshake(frame))
            {
                await RejectHandshakeAsync(pipe, "invalid_proof", ct).ConfigureAwait(false);
                _handshakeRateLimiter.RecordFailure();
                return false;
            }

            // (8) nonce LRU（Requirement 4.7）
            if (!_nonceLru.TryAdd(frame.SecretVersion, frame.Nonce))
            {
                await RejectHandshakeAsync(pipe, "invalid_proof", ct).ConfigureAwait(false);
                _handshakeRateLimiter.RecordFailure();
                return false;
            }

            // 全部通过：握手成功
            session.MarkHandshaked(frame.SecretVersion);
            await WriteHandshakeResponseAsync(pipe, HandshakeResponse.Success(), ct).ConfigureAwait(false);
            _diagnostics(
                $"Bridge 握手成功 secretVersion={frame.SecretVersion} clientVersion={frame.ClientVersion}");
            return true;
        }

        private async Task RejectHandshakeAsync(
            NamedPipeServerStream pipe, string reason, CancellationToken ct)
        {
            await WriteHandshakeResponseAsync(pipe, HandshakeResponse.Failure(reason), ct).ConfigureAwait(false);
        }

        private static async Task WriteHandshakeResponseAsync(
            NamedPipeServerStream pipe, HandshakeResponse response, CancellationToken ct)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(response, ResponseSerializerOptions);
            await FrameCodec.WriteFrameAsync(pipe, bytes, ct).ConfigureAwait(false);
        }

        // ---------- 已握手帧路由 ----------

        private async Task RunHandshakedLoopAsync(
            NamedPipeServerStream pipe, BridgeSession session, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested && pipe.IsConnected)
                {
                    byte[] frameBytes;
                    try
                    {
                        frameBytes = await FrameCodec.ReadFrameAsync(pipe, ct).ConfigureAwait(false);
                    }
                    catch (InvalidDataException ex)
                    {
                        _diagnostics($"Bridge 帧外壳解析失败：{ex.Message}");
                        return; // 协议错误关闭
                    }
                    catch (IOException) { return; /* 客户端断开 */ }

                    using var doc = TryParseJsonDocument(frameBytes, out var parseFailure);
                    if (doc == null)
                    {
                        _diagnostics($"Bridge 帧 JSON 解析失败：{parseFailure}");
                        return;
                    }

                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object
                        || !root.TryGetProperty("protocol", out var protocolEl)
                        || protocolEl.ValueKind != JsonValueKind.String)
                    {
                        _diagnostics("Bridge 帧缺少 protocol 字段");
                        return;
                    }

                    var protocol = protocolEl.GetString();
                    switch (protocol)
                    {
                        case ReportFrame.ProtocolLiteral:
                            if (!await HandleReportAsync(pipe, session, frameBytes, ct).ConfigureAwait(false))
                            {
                                return;
                            }
                            break;

                        case LogFrame.ProtocolLiteral:
                            HandleLog(frameBytes, session);
                            break;

                        case PeerApprovalFrame.ProtocolLiteral:
                            if (!await HandlePeerApprovalAsync(pipe, session, frameBytes, ct).ConfigureAwait(false))
                            {
                                return;
                            }
                            break;

                        default:
                            _diagnostics(
                                $"Bridge 已握手会话上收到未知 protocol={protocol} —— 视为协议错误");
                            return;
                    }
                }
            }
            finally
            {
                _logDropCounter.NotifySessionEnded();
            }
        }

        // ---------- Report 帧 ----------

        private async Task<bool> HandleReportAsync(
            NamedPipeServerStream pipe, BridgeSession session, byte[] frameBytes, CancellationToken ct)
        {
            // Requirement 5.1：HMAC 校验之前先做 schema 严格校验，但响应路径 / 时延应不可统计区分
            ReportFrame frame;
            try
            {
                frame = JsonSerializer.Deserialize<ReportFrame>(frameBytes);
            }
            catch (JsonException ex)
            {
                _diagnostics($"Report 帧反序列化失败：{ex.Message}");
                return false; // 协议错误关闭
            }

            if (frame == null)
            {
                return false;
            }

            // Schema 校验
            var schemaOk = frame.PasswordKind != null
                && Array.IndexOf(ReportFrame.AllowedPasswordKinds, frame.PasswordKind) >= 0
                && frame.Reason != null
                && Array.IndexOf(ReportFrame.AllowedReasons, frame.Reason) >= 0
                && !(string.Equals(frame.PasswordKind, ReportFrame.PasswordKindAbsent, StringComparison.Ordinal)
                     && !string.IsNullOrEmpty(frame.Password));

            if (!schemaOk)
            {
                await WriteReportAckAsync(pipe, ReportAck.Rejected(ReportAck.ReasonInvalidMac), ct).ConfigureAwait(false);
                return true;
            }

            // Requirement 5.3：secretVersion 与会话握手时确认值不等
            if (frame.SecretVersion != session.NegotiatedSecretVersion)
            {
                await WriteReportAckAsync(
                    pipe, ReportAck.Rejected(ReportAck.ReasonSecretOutdated), ct).ConfigureAwait(false);
                return true;
            }

            // HMAC 校验（Requirement 3.1 / 3.4）
            if (!_hmac.VerifyReport(frame))
            {
                await WriteReportAckAsync(pipe, ReportAck.Rejected(ReportAck.ReasonInvalidMac), ct).ConfigureAwait(false);
                return true;
            }

            // Session 内 nonce HashSet（Requirement 5.4 / 14.5）
            if (!session.RecordReportNonce(frame.Nonce, out var overflow))
            {
                if (overflow)
                {
                    _diagnostics("Bridge session Report nonce HashSet 触达上限，关闭会话");
                    return false;
                }
                await WriteReportAckAsync(pipe, ReportAck.Rejected(ReportAck.ReasonInvalidMac), ct).ConfigureAwait(false);
                return true;
            }

            // Requirement 5.7：立即回 accepted；后续上行 / 入队失败都不影响 ack
            await WriteReportAckAsync(pipe, ReportAck.Accepted(), ct).ConfigureAwait(false);

            // 在 ack 后异步触发 LastReportedSnapshot.TryReplace + 入队
            try
            {
                if (_lastReported.TryReplace(frame, out var requiresUpload, out var passwordSnapshot))
                {
                    if (requiresUpload && passwordSnapshot != null)
                    {
                        // Requirement 14.3：1 秒内同三元组最多 1 次上行
                        if (_reportRateLimiter.TryAcquire(frame.RustDeskId, frame.PasswordKind, frame.Password))
                        {
                            var payload = new ReportPayload(
                                frame.RustDeskId,
                                frame.PasswordKind,
                                passwordSnapshot, // ReportUploadQueue 内部会复制再抹零
                                frame.ReportedAt,
                                frame.SecretVersion);
                            _reportQueue.Enqueue(payload);
                        }
                        else
                        {
                            // 1 秒内重复三元组：丢弃 passwordSnapshot 抹零（避免泄露）
                            System.Security.Cryptography.CryptographicOperations.ZeroMemory(passwordSnapshot);
                            _diagnostics(
                                $"Report 上行被 1 秒同三元组速率限制（rustDeskId 已被脱敏），丢弃 passwordSnapshot");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _diagnostics($"Report 异步上行准备阶段异常（已忽略）：{ex.Message}");
            }

            return true;
        }

        private static async Task WriteReportAckAsync(
            NamedPipeServerStream pipe, ReportAck ack, CancellationToken ct)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(ack, ResponseSerializerOptions);
            await FrameCodec.WriteFrameAsync(pipe, bytes, ct).ConfigureAwait(false);
        }

        // ---------- Log 帧 ----------

        private void HandleLog(byte[] frameBytes, BridgeSession session)
        {
            // Requirement 9.3：HMAC / secretVersion / level schema 失败 → 静默丢弃 + drop +1
            LogFrame frame;
            try
            {
                frame = JsonSerializer.Deserialize<LogFrame>(frameBytes);
            }
            catch (JsonException ex)
            {
                _logDropCounter.Increment();
                _diagnostics($"Log 帧反序列化失败：{ex.Message}");
                return;
            }

            if (frame == null
                || frame.SecretVersion != session.NegotiatedSecretVersion
                || !_hmac.VerifyLog(frame))
            {
                _logDropCounter.Increment();
                return;
            }

            // 通过校验 → LogIngestor.Ingest（仍要 secretVersion check 已经做过）
            // level schema 非法时 LogIngestor 会返回 false，本类按 §9.3 静默丢弃 + drop +1
            try
            {
                if (!_logs.Ingest(frame))
                {
                    _logDropCounter.Increment();
                }
            }
            catch (Exception ex)
            {
                _logDropCounter.Increment();
                _diagnostics($"Log 入队失败：{ex.Message}");
            }
        }

        // ---------- PeerApproval 帧 ----------

        private async Task<bool> HandlePeerApprovalAsync(
            NamedPipeServerStream pipe, BridgeSession session, byte[] frameBytes, CancellationToken ct)
        {
            PeerApprovalFrame frame;
            try
            {
                frame = JsonSerializer.Deserialize<PeerApprovalFrame>(frameBytes);
            }
            catch (JsonException ex)
            {
                _diagnostics($"PeerApproval 帧反序列化失败：{ex.Message}");
                return false; // 协议错误关闭
            }

            if (frame == null)
            {
                return false;
            }

            // Requirement 7.3：secretVersion 不等 → rejected（不附 reason）
            // Requirement 7.4：requestNonce 重放 → rejected（不附 reason）
            // Requirement 7.5：controlledMachineId 不等 → rejected（不附 reason）
            // 任一失败都按统一的 Rejected() 路径回，不暴露具体原因（决策点 3）

            if (frame.SecretVersion != session.NegotiatedSecretVersion)
            {
                await WritePeerApprovalAsync(pipe, PeerApprovalResponse.Rejected(), ct).ConfigureAwait(false);
                return true;
            }

            if (!_hmac.VerifyPeerApproval(frame))
            {
                await WritePeerApprovalAsync(pipe, PeerApprovalResponse.Rejected(), ct).ConfigureAwait(false);
                return true;
            }

            if (!session.RecordPeerApprovalNonce(frame.RequestNonce, out var overflow))
            {
                if (overflow)
                {
                    _diagnostics("Bridge session PeerApproval nonce HashSet 触达上限，关闭会话");
                    return false;
                }
                await WritePeerApprovalAsync(pipe, PeerApprovalResponse.Rejected(), ct).ConfigureAwait(false);
                return true;
            }

            // controlledMachineId 不等 → rejected（PeerApprovalEvaluator 内部也做了这个检查，
            // 这里早返回是为了不浪费 SnapshotStore 的查表开销）
            if (!string.Equals(frame.ControlledMachineId, _thisMachineId, StringComparison.Ordinal))
            {
                await WritePeerApprovalAsync(pipe, PeerApprovalResponse.Rejected(), ct).ConfigureAwait(false);
                return true;
            }

            // 走 PeerApprovalEvaluator（包含 IRegistrationGate / SnapshotStore.Evaluate / 决策点 3 reason 省略）
            var response = _peerApprovals.Evaluate(frame, _thisMachineId);
            await WritePeerApprovalAsync(pipe, response, ct).ConfigureAwait(false);
            return true;
        }

        private static async Task WritePeerApprovalAsync(
            NamedPipeServerStream pipe, PeerApprovalResponse response, CancellationToken ct)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(response, ResponseSerializerOptions);
            await FrameCodec.WriteFrameAsync(pipe, bytes, ct).ConfigureAwait(false);
        }

        // ---------- 内部辅助 ----------

        private static JsonDocument TryParseJsonDocument(byte[] bytes, out string failureMessage)
        {
            failureMessage = null;
            try
            {
                return JsonDocument.Parse(bytes);
            }
            catch (JsonException ex)
            {
                failureMessage = ex.Message;
                return null;
            }
        }

        private static bool TryReadUInt32(JsonElement obj, string propertyName, out uint value)
        {
            value = 0;
            if (!obj.TryGetProperty(propertyName, out var el)) return false;
            if (el.ValueKind != JsonValueKind.Number) return false;
            return el.TryGetUInt32(out value);
        }
    }
}
