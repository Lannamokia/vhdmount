using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VHDMounter.RustDeskBridge.Crypto;
using VHDMounter.RustDeskBridge.Frames;
using VHDMounter.RustDeskBridge.Pipe;
using VHDMounter.RustDeskBridge.Policy;
using VHDMounter.RustDeskBridge.Session;

namespace VHDMounter.RustDeskBridge.Revocation
{
    /// <summary>
    /// Requirement 10.1 / 10.2 / 10.3 / 10.4 / 10.5 / 12.6：服务端 Revocation 推送实现。
    ///
    /// <see cref="PushDeniedAsync"/> / <see cref="PushSecretOutdatedAsync"/>:
    /// (a) 构造 <see cref="RevocationFrame"/>（HMAC over 协议文档 §9.2）
    /// (b) 写入当前 Bridge_Session 管道（<see cref="FrameCodec.WriteFrameAsync"/>）
    /// (c) 立即调 cancelInflightRequests 委托（取消所有飞行中 HTTP 上行 Future）
    /// (d) 调 <see cref="SnapshotStore.Invalidate"/>
    /// (e) 关闭当前 BridgeSession.Pipe 句柄（PipeAcceptLoop 在 1 秒内重建）
    ///
    /// <para>
    /// 写 <see cref="MachineLogBuffer"/> <c>EventKey="revocation_pushed"</c> /
    /// <c>Level="warn"</c> 日志，包含 <c>reason</c>，**不**包含 controllerId / controllerName
    /// （§10.5 / 11.3 隐私最小化）。
    /// </para>
    ///
    /// <para>
    /// 去重：连续两次相同 <c>(reason, secretVersion)</c> 第二次为 no-op；
    /// <c>now - issuedAt &gt; 300_000ms</c> 时自动重新构造 → 因此每次 push 都生成新
    /// <c>issuedAt</c> + 新 mac，不会重发上次的字节（§12.6）。
    /// </para>
    /// </summary>
    internal sealed class RevocationPublisher
    {
        public const long IssuedAtFreshnessMs = 300_000;

        private readonly HmacVerifier _hmac;
        private readonly SnapshotStore _snapshots;
        private readonly MachineLogBuffer _logBuffer;
        private readonly IClock _clock;
        private readonly Func<BridgeSession> _getActiveSession;
        private readonly Func<CancellationToken, Task> _cancelInflightRequests;
        private readonly IBridgeSecretProvider _secretProvider;
        private readonly Action<string> _diagnostics;

        private readonly object _gate = new object();
        private string _lastReason = string.Empty;
        private uint _lastSecretVersion = uint.MaxValue;
        private long _lastIssuedAtMs;

        public RevocationPublisher(
            HmacVerifier hmac,
            SnapshotStore snapshots,
            MachineLogBuffer logBuffer,
            IClock clock,
            IBridgeSecretProvider secretProvider,
            Func<BridgeSession> getActiveSession,
            Func<CancellationToken, Task> cancelInflightRequests,
            Action<string> diagnostics = null)
        {
            _hmac = hmac ?? throw new ArgumentNullException(nameof(hmac));
            _snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
            _logBuffer = logBuffer ?? throw new ArgumentNullException(nameof(logBuffer));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _secretProvider = secretProvider ?? throw new ArgumentNullException(nameof(secretProvider));
            _getActiveSession = getActiveSession ?? throw new ArgumentNullException(nameof(getActiveSession));
            _cancelInflightRequests = cancelInflightRequests ?? throw new ArgumentNullException(nameof(cancelInflightRequests));
            _diagnostics = diagnostics ?? (_ => { });
        }

        public Task<bool> PushDeniedAsync(CancellationToken ct = default)
            => PushAsync(RevocationFrame.ReasonDenied, ct);

        public Task<bool> PushSecretOutdatedAsync(CancellationToken ct = default)
            => PushAsync(RevocationFrame.ReasonSecretOutdated, ct);

        /// <summary>
        /// 推送一次 Revocation。返回 true 表示已实际发出帧；返回 false 表示 no-op
        /// （连续两次相同 (reason, secretVersion)）。
        /// </summary>
        public async Task<bool> PushAsync(string reason, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(reason)) throw new ArgumentException("reason 不能为空", nameof(reason));

            var nowMs = _clock.UtcNow.ToUnixTimeMilliseconds();
            var secretVersion = _secretProvider.CurrentSecretVersion;

            // §12.6 去重 + 时效性：相同 (reason, secretVersion) + 距上次 issuedAt < 300s → no-op
            lock (_gate)
            {
                if (string.Equals(_lastReason, reason, StringComparison.Ordinal) &&
                    _lastSecretVersion == secretVersion &&
                    (nowMs - _lastIssuedAtMs) < IssuedAtFreshnessMs)
                {
                    _diagnostics(
                        $"Revocation 推送 no-op：与上次相同 (reason={reason}, secretVersion={secretVersion})");
                    return false;
                }
            }

            // (a) 构造帧 + 计算 mac
            var mac = _hmac.ComputeRevocationMac(secretVersion, reason, nowMs);
            var frame = new RevocationFrame
            {
                Protocol = RevocationFrame.ProtocolLiteral,
                SecretVersion = secretVersion,
                Reason = reason,
                IssuedAt = nowMs,
                Mac = mac,
            };
            var frameBytes = SerializeFrameUtf8(frame);

            // (b) 写当前会话管道（容错：会话可能不存在或已断开）
            var session = _getActiveSession();
            var streamWritten = false;
            if (session != null)
            {
                var pipe = session.Pipe;
                if (pipe != null && pipe.IsConnected)
                {
                    try
                    {
                        await FrameCodec.WriteFrameAsync(pipe, frameBytes, ct).ConfigureAwait(false);
                        streamWritten = true;
                    }
                    catch (Exception ex)
                    {
                        _diagnostics($"Revocation 帧写入管道失败：{ex.Message}");
                    }
                }
            }

            // (c) 取消飞行中 HTTP（无论写帧成功与否）
            try
            {
                await _cancelInflightRequests(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _diagnostics($"Revocation 取消飞行中 HTTP 异常：{ex.Message}");
            }

            // (d) 失效快照（无论写帧成功与否）
            try
            {
                _snapshots.Invalidate();
            }
            catch (Exception ex)
            {
                _diagnostics($"Revocation 失效快照异常：{ex.Message}");
            }

            // (e) 关闭当前会话句柄（让 PipeAcceptLoop 1 秒内重建）
            try
            {
                session?.Close();
            }
            catch (Exception ex)
            {
                _diagnostics($"Revocation 关闭管道句柄异常：{ex.Message}");
            }

            // 日志：仅 reason，不包含 controllerId / controllerName
            WriteRevocationPushedLog(reason, streamWritten);

            lock (_gate)
            {
                _lastReason = reason;
                _lastSecretVersion = secretVersion;
                _lastIssuedAtMs = nowMs;
            }

            return true;
        }

        // ---------- 内部辅助 ----------

        private static byte[] SerializeFrameUtf8(RevocationFrame frame)
        {
            // 保持字段顺序与协议文档 §9.1 / 测试向量一致
            using var ms = new System.IO.MemoryStream();
            using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
            {
                writer.WriteStartObject();
                writer.WriteString("protocol", frame.Protocol);
                writer.WriteNumber("secretVersion", frame.SecretVersion);
                writer.WriteString("reason", frame.Reason ?? string.Empty);
                writer.WriteNumber("issuedAt", frame.IssuedAt);
                writer.WriteString("mac", frame.Mac ?? string.Empty);
                writer.WriteEndObject();
            }
            return ms.ToArray();
        }

        private void WriteRevocationPushedLog(string reason, bool streamWritten)
        {
            try
            {
                var entry = new MachineLogEntry
                {
                    SessionId = _logBuffer.CurrentSessionId,
                    OccurredAt = _clock.UtcNow.UtcDateTime.ToString(
                        "yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
                    Level = "warn",
                    Component = "rustdesk-bridge",
                    EventKey = "revocation_pushed",
                    Message = $"reason={reason}, written={(streamWritten ? "true" : "false")}",
                    RawText = $"reason={reason}, written={(streamWritten ? "true" : "false")}",
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                };
                _logBuffer.EnqueueRustDeskBridgeEntry(entry);
            }
            catch
            {
                // 日志写失败不影响主路径
            }
        }
    }
}
