using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VHDMounter.RustDeskBridge.Config;
using VHDMounter.RustDeskBridge.Policy;

namespace VHDMounter.RustDeskBridge.Upload
{
    /// <summary>
    /// Requirement 6.4 / 6.5 / 6.7.4 / 6.7.5 / 6.7.6 / 6.7.8 / 15.1：把 (rustDeskId, password)
    /// snapshot 用 Password_Wrap_Key 加密后上行 VHDSelectServer。
    ///
    /// 步骤：
    /// (1) <see cref="WrapKeyClient.EnsureCurrentAsync"/> 取当前 K
    /// (2) 随机 12 字节 iv → AES-256-GCM 加密 password；associated-data 严格按
    ///     <c>"VHDMounterRustDeskPasswordV1\n" || machineId || "\n" || rustDeskId || "\n" || passwordKind || "\n" || reportedAt</c>
    /// (3) POST /api/machines/:machineId/rustdesk/report（VHDMounterRustDeskReportV1 签名）
    /// (4) 5xx / 超时 → <see cref="ReportUploadOutcome.RetryableFailure"/>
    /// (5) errorCode == "WRAP_KEY_EXPIRED" → 调
    ///     <see cref="WrapKeyClient.OnWrapKeyExpiredAsync"/> 后**只**重试一次；仍失败 → 上抛给调用方
    /// (6) errorCode == "PAYLOAD_AAD_MISMATCH" / 401 / 403 → <see cref="ReportUploadOutcome.NonRecoverableFailure"/>
    ///
    /// passwordPlain byte[] 在 finally 里调 <see cref="CryptographicOperations.ZeroMemory"/>
    /// 抹零（Requirement 6.7 / 6.8 / 13.5）。
    ///
    /// 本类**不**实现重试队列（Wave 5 任务 7.5 落地 ReportUploadQueue）；本波 ReportUploader
    /// 仅返回 outcome 给调用方决定是否入队（design §"路径 2"）。
    /// </summary>
    internal sealed class ReportUploader
    {
        public const string PasswordAadVersion = "VHDMounterRustDeskPasswordV1";
        public const string ErrorCodeWrapKeyExpired = "WRAP_KEY_EXPIRED";
        public const string ErrorCodePayloadAadMismatch = "PAYLOAD_AAD_MISMATCH";

        private readonly BridgeConfig _bridgeConfig;
        private readonly WrapKeyClient _wrapKeys;
        private readonly IPolicyPubkeyValidator _policyPubkey;
        private readonly HttpClient _httpClient;
        private readonly string _machineId;
        private readonly string _serverBaseUrl;
        private readonly Action<string> _diagnostics;

        public ReportUploader(
            BridgeConfig bridgeConfig,
            WrapKeyClient wrapKeys,
            IPolicyPubkeyValidator policyPubkey,
            HttpClient httpClient,
            string machineId,
            string serverBaseUrl,
            Action<string> diagnostics = null)
        {
            _bridgeConfig = bridgeConfig ?? throw new ArgumentNullException(nameof(bridgeConfig));
            _wrapKeys = wrapKeys ?? throw new ArgumentNullException(nameof(wrapKeys));
            _policyPubkey = policyPubkey ?? throw new ArgumentNullException(nameof(policyPubkey));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _machineId = machineId ?? throw new ArgumentNullException(nameof(machineId));
            _serverBaseUrl = (serverBaseUrl ?? string.Empty).TrimEnd('/');
            _diagnostics = diagnostics ?? (_ => { });
        }

        public async Task<ReportUploadOutcome> UploadAsync(ReportPayload payload, CancellationToken ct)
        {
            // passwordPlain 由调用方提供 byte[] —— 我们在 finally 里抹零（哪怕 K 同份重试也只抹一次）
            var passwordPlain = payload.PasswordPlain ?? Array.Empty<byte>();
            try
            {
                // 第一次尝试
                var first = await SendOnceAsync(payload, passwordPlain, ct).ConfigureAwait(false);
                if (first.Outcome != ReportUploadOutcome.RetryWrapKeyExpired)
                {
                    return first.Outcome;
                }

                // §6.7.6：服务端返回 WRAP_KEY_EXPIRED → 立即刷新 K + 重试一次（仅一次）
                _diagnostics($"Report 上行命中 WRAP_KEY_EXPIRED wrapKeyId={first.WrapKeyId}，立即刷新后重试一次");
                await _wrapKeys.OnWrapKeyExpiredAsync(first.WrapKeyId, ct).ConfigureAwait(false);
                var second = await SendOnceAsync(payload, passwordPlain, ct).ConfigureAwait(false);

                // 重试再失败：把 RetryWrapKeyExpired 折叠成 RetryableFailure 让调用方入队
                return second.Outcome == ReportUploadOutcome.RetryWrapKeyExpired
                    ? ReportUploadOutcome.RetryableFailure
                    : second.Outcome;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(passwordPlain);
            }
        }

        // ---------- 内部 ----------

        private async Task<ReportUploadAttemptResult> SendOnceAsync(
            ReportPayload payload, byte[] passwordPlain, CancellationToken ct)
        {
            // (1) 取 K
            PasswordWrapKey wrap;
            try
            {
                wrap = await _wrapKeys.EnsureCurrentAsync(ct).ConfigureAwait(false);
            }
            catch (WrapKeyFetchException ex)
            {
                _diagnostics($"Report 上行取 WrapKey 失败：{ex.Message}");
                return ReportUploadAttemptResult.Of(ReportUploadOutcome.RetryableFailure, string.Empty);
            }

            try
            {
                // (2) AES-256-GCM 加密
                var passwordKind = payload.PasswordKind ?? string.Empty;
                var iv = RandomNumberGenerator.GetBytes(12);
                var cipher = new byte[passwordPlain.Length];
                var authTag = new byte[16];
                var aad = BuildAssociatedData(_machineId, payload.RustDeskId, passwordKind, payload.ReportedAtMs);

                using (var aes = new AesGcm(wrap.Material, tagSizeInBytes: 16))
                {
                    aes.Encrypt(iv, passwordPlain, cipher, authTag, aad);
                }

                // (3) 构造 body 与签名
                var bodyJson = BuildBodyJson(
                    rustDeskId: payload.RustDeskId,
                    passwordKind: passwordKind,
                    wrapKeyId: wrap.WrapKeyId,
                    iv: iv,
                    passwordCipher: cipher,
                    authTag: authTag,
                    secretVersion: payload.SecretVersion,
                    reportedAtMs: payload.ReportedAtMs);

                var url = $"{_serverBaseUrl}/api/machines/{Uri.EscapeDataString(_machineId)}/rustdesk/report";
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(bodyJson, Encoding.UTF8, "application/json"),
                };
                var keyId = RustDeskReportSigner.BuildDefaultKeyId(_machineId);
                RustDeskReportSigner.SignReport(request, _machineId, keyId, bodyJson);

                using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    return ReportUploadAttemptResult.Of(ReportUploadOutcome.Success, wrap.WrapKeyId);
                }

                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var errorCode = TryReadErrorCode(body);

                if (string.Equals(errorCode, ErrorCodeWrapKeyExpired, StringComparison.Ordinal))
                {
                    return ReportUploadAttemptResult.Of(ReportUploadOutcome.RetryWrapKeyExpired, wrap.WrapKeyId);
                }

                if (string.Equals(errorCode, ErrorCodePayloadAadMismatch, StringComparison.Ordinal))
                {
                    _diagnostics(
                        $"Report 上行命中 PAYLOAD_AAD_MISMATCH（machineId={_machineId} rustDeskId={payload.RustDeskId}），" +
                        "视为不可恢复错误，不入重试队列");
                    return ReportUploadAttemptResult.Of(ReportUploadOutcome.NonRecoverableFailure, wrap.WrapKeyId);
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized ||
                    response.StatusCode == HttpStatusCode.Forbidden)
                {
                    _diagnostics(
                        $"Report 上行返回 {(int)response.StatusCode}（机台未审批 / 凭据过期），跳过本周期");
                    return ReportUploadAttemptResult.Of(ReportUploadOutcome.NonRecoverableFailure, wrap.WrapKeyId);
                }

                if ((int)response.StatusCode >= 500)
                {
                    _diagnostics(
                        $"Report 上行返回 5xx ({(int)response.StatusCode})：{Truncate(body, 200)}");
                    return ReportUploadAttemptResult.Of(ReportUploadOutcome.RetryableFailure, wrap.WrapKeyId);
                }

                // 4xx 其它：客户端逻辑错误（schema 错），不入重试
                _diagnostics(
                    $"Report 上行返回 {(int)response.StatusCode}：{Truncate(body, 200)}");
                return ReportUploadAttemptResult.Of(ReportUploadOutcome.NonRecoverableFailure, wrap.WrapKeyId);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException ex)
            {
                _diagnostics($"Report 上行超时：{ex.Message}");
                return ReportUploadAttemptResult.Of(ReportUploadOutcome.RetryableFailure, wrap.WrapKeyId);
            }
            catch (HttpRequestException ex)
            {
                _diagnostics($"Report 上行 HTTP 异常：{ex.Message}");
                return ReportUploadAttemptResult.Of(ReportUploadOutcome.RetryableFailure, wrap.WrapKeyId);
            }
            catch (IOException ex)
            {
                _diagnostics($"Report 上行 I/O 异常：{ex.Message}");
                return ReportUploadAttemptResult.Of(ReportUploadOutcome.RetryableFailure, wrap.WrapKeyId);
            }
            finally
            {
                wrap.ZeroOut();
            }
        }

        public static byte[] BuildAssociatedData(string machineId, string rustDeskId, string passwordKind, long reportedAtMs)
        {
            var s = string.Concat(
                PasswordAadVersion, "\n",
                machineId ?? string.Empty, "\n",
                rustDeskId ?? string.Empty, "\n",
                passwordKind ?? string.Empty, "\n",
                reportedAtMs.ToString(CultureInfo.InvariantCulture));
            return Encoding.ASCII.GetBytes(s);
        }

        private static string BuildBodyJson(
            string rustDeskId,
            string passwordKind,
            string wrapKeyId,
            byte[] iv,
            byte[] passwordCipher,
            byte[] authTag,
            uint secretVersion,
            long reportedAtMs)
        {
            // 顺序无关（服务端按字段读），但与 design / Requirement 字段一致命名
            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
            {
                writer.WriteStartObject();
                writer.WriteString("rustDeskId", rustDeskId ?? string.Empty);
                writer.WriteString("passwordKind", passwordKind ?? string.Empty);
                writer.WriteString("wrapKeyId", wrapKeyId ?? string.Empty);
                writer.WriteString("iv", Convert.ToBase64String(iv));
                writer.WriteString("passwordCipher", Convert.ToBase64String(passwordCipher));
                writer.WriteString("authTag", Convert.ToBase64String(authTag));
                writer.WriteNumber("secretVersion", secretVersion);
                writer.WriteNumber("reportedAt", reportedAtMs);
                writer.WriteEndObject();
            }
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        private static string TryReadErrorCode(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("errorCode", out var ec) && ec.ValueKind == JsonValueKind.String)
                {
                    return ec.GetString();
                }
            }
            catch (JsonException) { /* swallow */ }
            return null;
        }

        private static string Truncate(string s, int maxLength)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= maxLength) return s ?? string.Empty;
            return s.Substring(0, maxLength);
        }

        private readonly struct ReportUploadAttemptResult
        {
            public ReportUploadAttemptResult(ReportUploadOutcome outcome, string wrapKeyId)
            {
                Outcome = outcome;
                WrapKeyId = wrapKeyId;
            }
            public ReportUploadOutcome Outcome { get; }
            public string WrapKeyId { get; }

            public static ReportUploadAttemptResult Of(ReportUploadOutcome outcome, string wrapKeyId)
                => new ReportUploadAttemptResult(outcome, wrapKeyId);
        }
    }

    /// <summary>
    /// Report 上行单次输入。<see cref="PasswordPlain"/> 在 ReportUploader 内部抹零，
    /// 调用方持有的副本也应当在调用结束后抹零。
    /// </summary>
    internal readonly struct ReportPayload
    {
        public ReportPayload(
            string rustDeskId,
            string passwordKind,
            byte[] passwordPlain,
            long reportedAtMs,
            uint secretVersion)
        {
            RustDeskId = rustDeskId ?? string.Empty;
            PasswordKind = passwordKind ?? string.Empty;
            PasswordPlain = passwordPlain ?? Array.Empty<byte>();
            ReportedAtMs = reportedAtMs;
            SecretVersion = secretVersion;
        }

        public string RustDeskId { get; }
        public string PasswordKind { get; }
        public byte[] PasswordPlain { get; }
        public long ReportedAtMs { get; }
        public uint SecretVersion { get; }
    }

    /// <summary>
    /// Report 上行单次结果。<see cref="RetryWrapKeyExpired"/> 是
    /// <see cref="ReportUploader.UploadAsync"/> 内部使用的中间状态，调用方仅会收到
    /// <see cref="Success"/> / <see cref="RetryableFailure"/> /
    /// <see cref="NonRecoverableFailure"/> 三种。
    /// </summary>
    internal enum ReportUploadOutcome
    {
        Success = 0,
        RetryableFailure = 1,
        NonRecoverableFailure = 2,
        /// <summary>仅供 ReportUploader 内部使用：服务端 errorCode == WRAP_KEY_EXPIRED。</summary>
        RetryWrapKeyExpired = 3,
    }
}
