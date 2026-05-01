using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VHDMounter
{
    /// <summary>
    /// 机台密钥统一注册入口。
    /// 所有涉及机台密钥注册的逻辑统一收敛到此类，避免各模块各自维护一份注册实现。
    /// </summary>
    public static class MachineKeyRegistration
    {
        private static readonly HttpClient HttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        private static DateTimeOffset? _nextRegistrationAttempt;

        public enum RegistrationState
        {
            Unknown,
            NotRegistered,
            Submitted,
            Approved,
        }

        public static RegistrationState CurrentState
        {
            get { lock (_stateLock) return _currentState; }
            private set { lock (_stateLock) _currentState = value; }
        }
        public static bool IsRegisteredAndApproved => CurrentState == RegistrationState.Approved;

        private static readonly object _stateLock = new object();
        private static RegistrationState _currentState = RegistrationState.Unknown;

        /// <summary>
        /// 阻塞式统一注册入口。
        /// 先探测服务端状态，若未注册则提交注册，然后阻塞轮询等待管理员审批通过。
        /// 返回 true 表示已审批通过，可以安全使用机台密钥；返回 false 表示被取消或发生不可恢复错误。
        /// </summary>
        public static async Task<bool> EnsureRegisteredAsync(
            string machineId,
            string serverUrl,
            Action<string> statusCallback,
            CancellationToken ct)
        {
            using var rsa = VHDManager.EnsureOrCreateTpmRsa(machineId);
            var pubPem = VHDManager.ExportPublicKeyPem(rsa);
            var baseUrl = serverUrl.TrimEnd('/');

            while (!ct.IsCancellationRequested)
            {
                var state = await ProbeServerStateAsync(baseUrl, machineId, ct);
                CurrentState = state;

                if (state == RegistrationState.Approved)
                {
                    Trace.WriteLine("[MachineKeyRegistration] 机台已注册且已审批");
                    return true;
                }

                if (state == RegistrationState.NotRegistered)
                {
                    statusCallback?.Invoke("请联系管理员注册机台，正等待注册结果回传");

                    // 遵守退避间隔，避免触发服务端限流
                    if (_nextRegistrationAttempt.HasValue && DateTimeOffset.UtcNow < _nextRegistrationAttempt.Value)
                    {
                        var wait = _nextRegistrationAttempt.Value - DateTimeOffset.UtcNow;
                        Trace.WriteLine($"[MachineKeyRegistration] 退避中，{wait.TotalSeconds:F0} 秒后重试提交注册");
                        await Task.Delay(wait, ct);
                    }

                    var submitted = await SubmitRegistrationAsync(baseUrl, machineId, pubPem, ct);
                    if (!submitted)
                    {
                        // 提交失败，等 5 秒后重新探测
                        await Task.Delay(5000, ct);
                        continue;
                    }
                    CurrentState = RegistrationState.Submitted;
                }

                // 已提交或未审批，继续阻塞等待
                statusCallback?.Invoke("请联系管理员注册机台，正等待注册结果回传");
                await Task.Delay(2000, ct);
            }

            return false;
        }

        /// <summary>
        /// 轻量级探测服务端注册状态。
        /// 复用 /api/evhd-envelope 端点作为探针（不需要签名，只传 machineId）。
        /// </summary>
        private static async Task<RegistrationState> ProbeServerStateAsync(
            string baseUrl, string machineId, CancellationToken ct)
        {
            try
            {
                var url = $"{baseUrl}/api/evhd-envelope?machineId={Uri.EscapeDataString(machineId)}";
                var response = await HttpClient.GetAsync(url, ct);

                if (response.IsSuccessStatusCode)
                {
                    return RegistrationState.Approved;
                }

                var body = await response.Content.ReadAsStringAsync(ct);
                string err = null;
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("error", out var ee)) err = ee.GetString();
                }
                catch { }

                if ((int)response.StatusCode == 400 && (err ?? string.Empty).Contains("未注册公钥"))
                {
                    return RegistrationState.NotRegistered;
                }

                // 403（未审批/已吊销）或其他错误，统一视为已提交但尚未通过
                return RegistrationState.Submitted;
            }
            catch (TaskCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[MachineKeyRegistration] 探测服务端状态异常: {ex.Message}");
                return RegistrationState.Unknown;
            }
        }

        /// <summary>
        /// 向服务端提交公钥注册请求。
        /// </summary>
        private static async Task<bool> SubmitRegistrationAsync(
            string baseUrl, string machineId, string pubPem, CancellationToken ct)
        {
            try
            {
                var config = ReadConfig();
                using var registrationCertificate = LoadRegistrationCertificate(config);

                var keyId = $"VHDMounterKey_{machineId}";
                var keyType = "RSA";
                var normalizedPubkeyPem = VHDManager.NormalizePemText(pubPem);
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var nonce = GenerateNonce();

                using var registrationPrivateKey = registrationCertificate.GetRSAPrivateKey();
                if (registrationPrivateKey == null)
                {
                    Trace.WriteLine("[MachineKeyRegistration] 注册证书不包含 RSA 私钥");
                    return false;
                }

                var signingPayload = VHDManager.BuildRegistrationSigningPayload(
                    machineId, keyId, keyType, normalizedPubkeyPem, timestamp, nonce);
                var signatureBytes = registrationPrivateKey.SignData(
                    Encoding.UTF8.GetBytes(signingPayload),
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);

                var regUrl = $"{baseUrl}/api/machines/{Uri.EscapeDataString(machineId)}/keys";
                var payload = new
                {
                    keyId,
                    keyType,
                    pubkeyPem = normalizedPubkeyPem,
                    registrationCertificatePem = ExportCertificatePem(registrationCertificate),
                    signature = Convert.ToBase64String(signatureBytes),
                    timestamp,
                    nonce,
                };

                var json = JsonSerializer.Serialize(payload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await HttpClient.PostAsync(regUrl, content, ct);
                var body = await response.Content.ReadAsStringAsync(ct);

                if (response.IsSuccessStatusCode)
                {
                    _nextRegistrationAttempt = null;
                    Trace.WriteLine("[MachineKeyRegistration] 公钥注册提交成功");
                    return true;
                }

                // 429 限流时设置退避
                if ((int)response.StatusCode == 429)
                {
                    var retryAfter = ParseRetryAfter(response, body);
                    _nextRegistrationAttempt = DateTimeOffset.UtcNow.Add(retryAfter ?? TimeSpan.FromMinutes(1));
                    Trace.WriteLine($"[MachineKeyRegistration] 注册过于频繁，退避 {retryAfter?.TotalSeconds ?? 60}s");
                }
                else
                {
                    _nextRegistrationAttempt = DateTimeOffset.UtcNow.AddSeconds(30);
                }

                Trace.WriteLine($"[MachineKeyRegistration] 公钥注册提交失败: {(int)response.StatusCode} {body}");
                return false;
            }
            catch (Exception ex)
            {
                _nextRegistrationAttempt = DateTimeOffset.UtcNow.AddSeconds(30);
                Trace.WriteLine($"[MachineKeyRegistration] 注册提交异常: {ex.Message}");
                return false;
            }
        }

        private static Dictionary<string, string> ReadConfig()
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, "vhdmonter_config.ini");
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(configPath)) return values;

            foreach (var rawLine in File.ReadAllLines(configPath))
            {
                var line = rawLine?.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";") || line.StartsWith("[")) continue;
                var parts = line.Split('=', 2);
                if (parts.Length == 2) values[parts[0].Trim()] = parts[1].Trim();
            }
            return values;
        }

        private static X509Certificate2 LoadRegistrationCertificate(Dictionary<string, string> config)
        {
            if (!config.TryGetValue("RegistrationCertificatePath", out var path) || string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("未配置 RegistrationCertificatePath");

            var resolved = Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
            if (!File.Exists(resolved))
                throw new FileNotFoundException($"注册证书文件不存在: {resolved}");

            var password = config.TryGetValue("RegistrationCertificatePassword", out var pw) ? pw : string.Empty;
            var cert = new X509Certificate2(resolved, password,
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);

            if (!cert.HasPrivateKey)
            {
                cert.Dispose();
                throw new InvalidOperationException("注册证书缺少私钥");
            }
            return cert;
        }

        private static string ExportCertificatePem(X509Certificate2 certificate)
        {
            var certBytes = certificate.Export(X509ContentType.Cert);
            var b64 = Convert.ToBase64String(certBytes);
            var sb = new StringBuilder();
            sb.AppendLine("-----BEGIN CERTIFICATE-----");
            for (int i = 0; i < b64.Length; i += 64)
                sb.AppendLine(b64.Substring(i, Math.Min(64, b64.Length - i)));
            sb.AppendLine("-----END CERTIFICATE-----");
            return sb.ToString().Trim();
        }

        private static string GenerateNonce()
        {
            return Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        }

        private static TimeSpan? ParseRetryAfter(HttpResponseMessage response, string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("retryAfterSeconds", out var el)
                    && el.ValueKind == JsonValueKind.Number
                    && el.TryGetInt32(out var secs)
                    && secs > 0)
                {
                    return TimeSpan.FromSeconds(secs);
                }
            }
            catch { }

            if (response?.Headers?.RetryAfter?.Delta is TimeSpan d && d > TimeSpan.Zero)
                return d;

            return null;
        }
    }
}
