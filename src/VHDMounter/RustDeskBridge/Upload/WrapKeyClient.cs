using System;
using System.Globalization;
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
    /// Password_Wrap_Key 客户端（Requirement 6.7.1 / 6.7.2 / 6.7.3 / 6.7.6 / 6.7.7）。
    ///
    /// <list type="bullet">
    /// <item><see cref="EnsureCurrentAsync"/>：K 不存在或距 issuedAt ≥ ttlMs - 60_000 时刷新</item>
    /// <item><see cref="OnWrapKeyExpired"/>：服务端 errorCode == "WRAP_KEY_EXPIRED" 时强制刷新</item>
    /// </list>
    ///
    /// POST /rustdesk/wrap-key（VHDMounterWrapKeyV1 签名），响应签名 payload：
    /// <code>VHDMounterWrapKeyResponseV1\n&lt;machineId&gt;\n&lt;wrapKeyId&gt;\n&lt;sha256Hex(wrapKeyCipher base64)&gt;\n&lt;issuedAt&gt;\n&lt;ttlMs&gt;</code>
    /// 用 PolicyPubkeyClient 验签 → TPM 私钥 RSA-OAEP-SHA256 解包 → 32 字节 K 仅放进程内存。
    ///
    /// K 替换 / Dispose / 进程退出时调用 <see cref="CryptographicOperations.ZeroMemory"/> 抹零所有副本。
    /// 即时重试 3 次（每次 1s）后失败上抛。
    /// </summary>
    internal sealed class WrapKeyClient : IDisposable
    {
        private const string ResponsePayloadVersion = "VHDMounterWrapKeyResponseV1";
        private const int RefreshLeadMs = 60_000;
        private const int FetchMaxAttempts = 3;
        private static readonly TimeSpan FetchRetryDelay = TimeSpan.FromSeconds(1);

        private readonly BridgeConfig _bridgeConfig;
        private readonly string _machineId;
        private readonly string _serverBaseUrl;
        private readonly HttpClient _httpClient;
        private readonly IPolicyPubkeyValidator _policyPubkey;
        private readonly Func<DateTimeOffset> _utcNow;
        private readonly Action<string> _diagnostics;
        private readonly SemaphoreSlim _refreshGate = new SemaphoreSlim(1, 1);
        private readonly object _stateGate = new object();

        private string _wrapKeyId = string.Empty;
        private byte[] _wrapKeyMaterial = Array.Empty<byte>();
        private long _issuedAtMs;
        private int _ttlMs;
        private bool _hasValue;
        private bool _disposed;

        public WrapKeyClient(
            BridgeConfig bridgeConfig,
            string machineId,
            string serverBaseUrl,
            HttpClient httpClient,
            IPolicyPubkeyValidator policyPubkey,
            Func<DateTimeOffset> utcNow = null,
            Action<string> diagnostics = null)
        {
            _bridgeConfig = bridgeConfig ?? throw new ArgumentNullException(nameof(bridgeConfig));
            _machineId = machineId ?? throw new ArgumentNullException(nameof(machineId));
            _serverBaseUrl = (serverBaseUrl ?? string.Empty).TrimEnd('/');
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _policyPubkey = policyPubkey ?? throw new ArgumentNullException(nameof(policyPubkey));
            _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
            _diagnostics = diagnostics ?? (_ => { });
        }

        /// <summary>
        /// 取当前 K 的快照（wrapKeyId + 32 字节材料的副本）。调用方在使用完毕后
        /// SHALL 抹零返回的 byte[] 副本。
        ///
        /// 当 K 不存在或距 issuedAt ≥ ttlMs - 60_000ms 时会先触发刷新。
        /// </summary>
        public async Task<PasswordWrapKey> EnsureCurrentAsync(CancellationToken ct)
        {
            ThrowIfDisposed();

            if (NeedsRefreshLocked())
            {
                await RefreshLockedAsync(ct).ConfigureAwait(false);
            }

            lock (_stateGate)
            {
                if (!_hasValue)
                {
                    throw new WrapKeyFetchException("Password_Wrap_Key 当前不可用（未成功拉取过）");
                }
                var copy = new byte[_wrapKeyMaterial.Length];
                Buffer.BlockCopy(_wrapKeyMaterial, 0, copy, 0, _wrapKeyMaterial.Length);
                return new PasswordWrapKey(_wrapKeyId, copy, _issuedAtMs, _ttlMs);
            }
        }

        /// <summary>
        /// 服务端返回 WRAP_KEY_EXPIRED 时调用：强制刷新一次。
        /// </summary>
        public async Task OnWrapKeyExpiredAsync(string expiredWrapKeyId, CancellationToken ct)
        {
            ThrowIfDisposed();
            lock (_stateGate)
            {
                if (string.Equals(_wrapKeyId, expiredWrapKeyId, StringComparison.Ordinal))
                {
                    _hasValue = false;
                    CryptographicOperations.ZeroMemory(_wrapKeyMaterial);
                    _wrapKeyMaterial = Array.Empty<byte>();
                }
            }
            await RefreshLockedAsync(ct).ConfigureAwait(false);
        }

        public bool IsLoaded
        {
            get { lock (_stateGate) return _hasValue; }
        }

        public string CurrentWrapKeyId
        {
            get { lock (_stateGate) return _wrapKeyId; }
        }

        // ---------- 内部 ----------

        private bool NeedsRefreshLocked()
        {
            lock (_stateGate)
            {
                if (!_hasValue) return true;
                var nowMs = _utcNow().ToUnixTimeMilliseconds();
                return (nowMs - _issuedAtMs) >= (_ttlMs - RefreshLeadMs);
            }
        }

        private async Task RefreshLockedAsync(CancellationToken ct)
        {
            await _refreshGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!NeedsRefreshLocked()) return;

                Exception lastException = null;
                for (var attempt = 1; attempt <= FetchMaxAttempts; attempt++)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        await DoFetchOnceAsync(ct).ConfigureAwait(false);
                        return;
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        _diagnostics($"WrapKey 拉取失败（第 {attempt}/{FetchMaxAttempts} 次）：{ex.Message}");
                        if (attempt < FetchMaxAttempts)
                        {
                            try
                            {
                                await Task.Delay(FetchRetryDelay, ct).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException) when (ct.IsCancellationRequested)
                            {
                                throw;
                            }
                        }
                    }
                }

                throw lastException ?? new WrapKeyFetchException("WrapKey 拉取重试 3 次后仍然失败");
            }
            finally
            {
                _refreshGate.Release();
            }
        }

        private async Task DoFetchOnceAsync(CancellationToken ct)
        {
            var url = $"{_serverBaseUrl}/api/machines/{Uri.EscapeDataString(_machineId)}/rustdesk/wrap-key";
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(string.Empty, Encoding.UTF8, "application/json");
            var keyId = RustDeskReportSigner.BuildDefaultKeyId(_machineId);
            // 注意：bodyHash 必须基于"实际写入 body 的字符串"，POST 空 body 时即为空串 sha256
            // RustDeskReportSigner.SignWrapKeyFetch 内部把 body 当作空串处理 —— 与 §15.6 约定一致
            RustDeskReportSigner.SignWrapKeyFetch(request, _machineId, keyId);

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new WrapKeyFetchException(
                    $"POST /rustdesk/wrap-key 返回 {(int)response.StatusCode}：{Truncate(body, 200)}");
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            var root = doc.RootElement;

            var wrapKeyId = root.TryGetProperty("wrapKeyId", out var idEl) ? idEl.GetString() ?? string.Empty : string.Empty;
            var wrapKeyCipherB64 = root.TryGetProperty("wrapKeyCipher", out var cEl) ? cEl.GetString() ?? string.Empty : string.Empty;
            var issuedAtMs = root.TryGetProperty("issuedAt", out var iaEl) && iaEl.TryGetInt64(out var iaVal) ? iaVal : 0L;
            var ttlMs = root.TryGetProperty("ttlMs", out var ttlEl) && ttlEl.TryGetInt32(out var ttlVal) ? ttlVal : 600_000;
            var signatureB64 = root.TryGetProperty("signature", out var sigEl) ? sigEl.GetString() ?? string.Empty : string.Empty;

            if (string.IsNullOrEmpty(wrapKeyId) || string.IsNullOrEmpty(wrapKeyCipherB64) || string.IsNullOrEmpty(signatureB64))
            {
                throw new WrapKeyFetchException("WrapKey 响应缺少必需字段");
            }

            var cipherDigestHex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(wrapKeyCipherB64))).ToLowerInvariant();
            var payloadAscii = string.Concat(
                ResponsePayloadVersion, "\n",
                _machineId, "\n",
                wrapKeyId, "\n",
                cipherDigestHex, "\n",
                issuedAtMs.ToString(CultureInfo.InvariantCulture), "\n",
                ttlMs.ToString(CultureInfo.InvariantCulture));
            var payloadBytes = Encoding.ASCII.GetBytes(payloadAscii);

            if (!_policyPubkey.VerifyResponseSignature(payloadBytes, signatureB64))
            {
                throw new WrapKeyFetchException("WrapKey 响应签名校验失败");
            }

            byte[] cipherBytes;
            try
            {
                cipherBytes = Convert.FromBase64String(wrapKeyCipherB64);
            }
            catch (FormatException ex)
            {
                throw new WrapKeyFetchException("wrapKeyCipher base64 解码失败", ex);
            }

            byte[] plainKey;
            try
            {
                using var rsa = VHDManager.EnsureOrCreateTpmRsa(_machineId);
                plainKey = rsa.Decrypt(cipherBytes, RSAEncryptionPadding.OaepSHA256);
            }
            catch (CryptographicException ex)
            {
                throw new WrapKeyFetchException("WrapKey TPM 解包失败", ex);
            }

            if (plainKey.Length != 32)
            {
                CryptographicOperations.ZeroMemory(plainKey);
                throw new WrapKeyFetchException(
                    $"WrapKey 长度异常：期望 32 字节，实际 {plainKey.Length}");
            }

            lock (_stateGate)
            {
                CryptographicOperations.ZeroMemory(_wrapKeyMaterial);
                _wrapKeyMaterial = plainKey;
                _wrapKeyId = wrapKeyId;
                _issuedAtMs = issuedAtMs;
                _ttlMs = ttlMs > 0 ? ttlMs : 600_000;
                _hasValue = true;
                _diagnostics($"WrapKey active 槽已更新 wrapKeyId={wrapKeyId} ttlMs={_ttlMs}");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(WrapKeyClient));
        }

        public void Dispose()
        {
            if (_disposed) return;
            lock (_stateGate)
            {
                CryptographicOperations.ZeroMemory(_wrapKeyMaterial);
                _wrapKeyMaterial = Array.Empty<byte>();
                _hasValue = false;
                _disposed = true;
            }
            _refreshGate.Dispose();
        }

        private static string Truncate(string s, int maxLength)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= maxLength) return s ?? string.Empty;
            return s.Substring(0, maxLength);
        }
    }

    /// <summary>
    /// Password_Wrap_Key 快照（仅在调用方栈上短暂持有）。
    /// </summary>
    internal readonly struct PasswordWrapKey
    {
        public PasswordWrapKey(string wrapKeyId, byte[] material, long issuedAtMs, int ttlMs)
        {
            WrapKeyId = wrapKeyId;
            Material = material;
            IssuedAtMs = issuedAtMs;
            TtlMs = ttlMs;
        }

        public string WrapKeyId { get; }
        public byte[] Material { get; }
        public long IssuedAtMs { get; }
        public int TtlMs { get; }

        public void ZeroOut()
        {
            CryptographicOperations.ZeroMemory(Material);
        }
    }

    internal sealed class WrapKeyFetchException : Exception
    {
        public WrapKeyFetchException(string message, Exception inner = null) : base(message, inner) { }
    }
}
