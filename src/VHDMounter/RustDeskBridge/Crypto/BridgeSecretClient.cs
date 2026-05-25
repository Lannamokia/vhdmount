using System;
using System.Diagnostics;
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
using VHDMounter.RustDeskBridge.Upload;

namespace VHDMounter.RustDeskBridge.Crypto
{
    /// <summary>
    /// 决策点 1：RustDeskClientSharedSecret 拉取 + TPM 解包 + 内存 active 槽 + 服务端签名校验。
    ///
    /// <list type="bullet">
    /// <item><see cref="EnsureLoadedAsync"/> 启动期阻塞拉取，5s × 6 次重试</item>
    /// <item><see cref="RunRefreshLoopAsync"/> 按 SnapshotPullInterval 周期触发</item>
    /// <item><see cref="ForceRefreshAsync"/> Report 帧 secretVersion 不匹配时立即拉取</item>
    /// </list>
    ///
    /// 服务端 404 → <see cref="IsLoaded"/> 保持 false，PeerApprovalEvaluator 走 fail-closed、
    /// Report 上行被跳过（由调用方查询 IsLoaded 判断）。
    ///
    /// 内存 active 槽切换时旧 byte[] 调
    /// <see cref="CryptographicOperations.ZeroMemory"/> 抹零（Requirement 13.5）。
    ///
    /// 实现 <see cref="IBridgeSecretProvider"/> —— BridgeSecretClient 自身就是 secret 提供者，
    /// HmacVerifier 直接注入本类。
    /// </summary>
    internal sealed class BridgeSecretClient : IBridgeSecretProvider, IDisposable
    {
        private const string ResponsePayloadVersion = "BridgeSecretResponseV1";
        private const int StartupMaxAttempts = 6;
        private static readonly TimeSpan StartupRetryDelay = TimeSpan.FromSeconds(5);

        private readonly BridgeConfig _bridgeConfig;
        private readonly string _machineId;
        private readonly string _serverBaseUrl;
        private readonly HttpClient _httpClient;
        private readonly IPolicyPubkeyValidator _policyPubkey;
        private readonly Action<string> _diagnostics;
        private readonly object _gate = new object();

        private byte[] _activeSecret = Array.Empty<byte>();
        private uint _activeVersion;
        private long _activeIssuedAtMs;
        private bool _hasValue;
        private bool _disposed;

        public BridgeSecretClient(
            BridgeConfig bridgeConfig,
            string machineId,
            string serverBaseUrl,
            HttpClient httpClient,
            IPolicyPubkeyValidator policyPubkey,
            Action<string> diagnostics = null)
        {
            _bridgeConfig = bridgeConfig ?? throw new ArgumentNullException(nameof(bridgeConfig));
            _machineId = machineId ?? throw new ArgumentNullException(nameof(machineId));
            _serverBaseUrl = (serverBaseUrl ?? string.Empty).TrimEnd('/');
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _policyPubkey = policyPubkey ?? throw new ArgumentNullException(nameof(policyPubkey));
            _diagnostics = diagnostics ?? (_ => { });
        }

        // ---------- IBridgeSecretProvider ----------

        public uint CurrentSecretVersion
        {
            get { lock (_gate) return _activeVersion; }
        }

        public ReadOnlySpan<byte> GetActiveSecret()
        {
            lock (_gate)
            {
                if (!_hasValue)
                {
                    throw new InvalidOperationException(
                        "Bridge secret 尚未加载（PeerApproval 应当 fail-closed，Report 应当跳过上行）");
                }
                // 返回内部 byte[] 的 Span。调用方不会保留长期引用（HmacVerifier 只在 HMAC 计算瞬间使用）。
                return _activeSecret.AsSpan();
            }
        }

        // ---------- 公共接口 ----------

        public bool IsLoaded
        {
            get { lock (_gate) return _hasValue; }
        }

        public long ActiveIssuedAtMs
        {
            get { lock (_gate) return _activeIssuedAtMs; }
        }

        /// <summary>
        /// 启动期阻塞拉取：5s × 6 次重试。全部失败抛异常，由 BridgeServerHost
        /// 决定是否阻塞 SnapshotRefreshLoop（Requirement 13.3）。
        /// </summary>
        public async Task EnsureLoadedAsync(CancellationToken ct)
        {
            ThrowIfDisposed();
            _diagnostics(
                $"BridgeSecretClient[event=ensure_loaded_begin] " +
                $"machineId={_machineId} server={_serverBaseUrl} maxAttempts={StartupMaxAttempts}");

            Exception lastException = null;
            for (var attempt = 1; attempt <= StartupMaxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var outcome = await TryFetchAndApplyAsync(ct).ConfigureAwait(false);
                    if (outcome == FetchOutcome.Success || outcome == FetchOutcome.UnchangedVersion)
                    {
                        _diagnostics(
                            $"BridgeSecretClient[event=ensure_loaded_success] " +
                            $"attempt={attempt} secretVersion={_activeVersion} outcome={outcome}");
                        return;
                    }
                    if (outcome == FetchOutcome.NotConfigured)
                    {
                        // 服务端没有 active secret —— 启动期阻塞 PeerApproval，但本方法
                        // 仍以异常方式上抛（让 BridgeServerHost 走"失败但不致命"分支）
                        _diagnostics(
                            $"BridgeSecretClient[event=ensure_loaded_not_configured] " +
                            $"attempt={attempt} msg=服务端无 active RustDeskClientSharedSecret，" +
                            "握手将以 secret_outdated 全拒，请管理员到 admin 面板录入");
                        lastException = new BridgeSecretNotConfiguredException(
                            "VHDSelectServer 上不存在 active RustDeskClientSharedSecret 版本");
                    }
                    else
                    {
                        lastException = new BridgeSecretFetchException("Bridge secret 拉取失败 (outcome=" + outcome + ")");
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    _diagnostics(
                        $"BridgeSecretClient[event=ensure_loaded_attempt_failed] " +
                        $"attempt={attempt}/{StartupMaxAttempts} err={ex.Message}");
                }

                if (attempt < StartupMaxAttempts)
                {
                    try
                    {
                        await Task.Delay(StartupRetryDelay, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                }
            }

            _diagnostics(
                $"BridgeSecretClient[event=ensure_loaded_failed] " +
                $"machineId={_machineId} loaded={_hasValue} secretVersion={_activeVersion} " +
                $"last_err={lastException?.Message}");
            throw lastException ?? new BridgeSecretFetchException("Bridge secret 启动期装载失败");
        }

        /// <summary>
        /// 运行期周期刷新（SnapshotPullInterval 秒一次），失败仅写日志，不抛异常。
        /// </summary>
        public async Task RunRefreshLoopAsync(CancellationToken ct)
        {
            ThrowIfDisposed();
            var period = TimeSpan.FromSeconds(_bridgeConfig.SnapshotPullIntervalSeconds);
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(period, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }

                try
                {
                    await TryFetchAndApplyAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _diagnostics($"Bridge secret 周期刷新失败：{ex.Message}");
                }
            }
        }

        /// <summary>
        /// Report 帧 secretVersion 不匹配时调用：立即触发一次拉取。
        /// 拉取失败仅写日志（保留旧 active 槽）。
        /// </summary>
        public async Task ForceRefreshAsync(CancellationToken ct)
        {
            ThrowIfDisposed();
            try
            {
                await TryFetchAndApplyAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _diagnostics($"Bridge secret 强制刷新失败：{ex.Message}");
            }
        }

        // ---------- 内部 ----------

        private async Task<FetchOutcome> TryFetchAndApplyAsync(CancellationToken ct)
        {
            var url = $"{_serverBaseUrl}/api/machines/{Uri.EscapeDataString(_machineId)}/rustdesk/bridge-secret";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            var keyId = RustDeskReportSigner.BuildDefaultKeyId(_machineId);
            RustDeskReportSigner.SignBridgeSecretFetch(request, _machineId, keyId);

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return FetchOutcome.NotConfigured;
            }
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new BridgeSecretFetchException(
                    $"GET /rustdesk/bridge-secret 返回 {(int)response.StatusCode}：{Truncate(body, 200)}");
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            var root = doc.RootElement;

            var secretVersion = root.TryGetProperty("secretVersion", out var svEl) && svEl.TryGetUInt32(out var svVal)
                ? svVal
                : throw new BridgeSecretFetchException("响应缺少 secretVersion 或非 u32");
            var secretCipherBase64 = root.TryGetProperty("secretCipher", out var scEl)
                ? scEl.GetString() ?? string.Empty
                : throw new BridgeSecretFetchException("响应缺少 secretCipher");
            var issuedAtMs = root.TryGetProperty("issuedAt", out var iaEl) && iaEl.TryGetInt64(out var iaVal)
                ? iaVal
                : 0L;
            var signatureBase64 = root.TryGetProperty("signature", out var sigEl)
                ? sigEl.GetString() ?? string.Empty
                : throw new BridgeSecretFetchException("响应缺少 signature");

            // 服务端签名验签：BridgeSecretResponseV1\n<machineId>\n<secretVersion>\n<sha256Hex(secretCipher base64)>\n<issuedAt>
            var cipherDigestHex = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(secretCipherBase64))).ToLowerInvariant();
            var payloadAscii = string.Concat(
                ResponsePayloadVersion, "\n",
                _machineId, "\n",
                secretVersion.ToString(CultureInfo.InvariantCulture), "\n",
                cipherDigestHex, "\n",
                issuedAtMs.ToString(CultureInfo.InvariantCulture));
            var payloadBytes = Encoding.ASCII.GetBytes(payloadAscii);

            if (!_policyPubkey.VerifyResponseSignature(payloadBytes, signatureBase64))
            {
                throw new BridgeSecretFetchException("Bridge secret 响应签名校验失败");
            }

            byte[] cipherBytes;
            try
            {
                cipherBytes = Convert.FromBase64String(secretCipherBase64);
            }
            catch (FormatException ex)
            {
                throw new BridgeSecretFetchException("secretCipher base64 解码失败", ex);
            }

            // 用 TPM 私钥 RSA-OAEP-SHA256 解包
            byte[] plainSecret;
            try
            {
                using var rsa = VHDManager.EnsureOrCreateTpmRsa(_machineId);
                plainSecret = rsa.Decrypt(cipherBytes, RSAEncryptionPadding.OaepSHA256);
            }
            catch (CryptographicException ex)
            {
                throw new BridgeSecretFetchException("Bridge secret TPM 解包失败", ex);
            }

            if (plainSecret.Length != 32)
            {
                CryptographicOperations.ZeroMemory(plainSecret);
                throw new BridgeSecretFetchException(
                    $"Bridge secret 长度异常：期望 32 字节，实际 {plainSecret.Length}");
            }

            lock (_gate)
            {
                if (_hasValue && _activeVersion == secretVersion)
                {
                    // 同版本：仅刷 issuedAt，抹零新拉取的明文副本（保持 active 不变）
                    _activeIssuedAtMs = issuedAtMs;
                    CryptographicOperations.ZeroMemory(plainSecret);
                    return FetchOutcome.UnchangedVersion;
                }

                // 替换瞬间抹零旧 active byte[]
                CryptographicOperations.ZeroMemory(_activeSecret);
                _activeSecret = plainSecret;
                _activeVersion = secretVersion;
                _activeIssuedAtMs = issuedAtMs;
                _hasValue = true;
                _diagnostics($"Bridge secret active 槽已更新到 secretVersion={secretVersion}");
                return FetchOutcome.Success;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(BridgeSecretClient));
        }

        public void Dispose()
        {
            if (_disposed) return;
            lock (_gate)
            {
                CryptographicOperations.ZeroMemory(_activeSecret);
                _activeSecret = Array.Empty<byte>();
                _hasValue = false;
                _disposed = true;
            }
        }

        private static string Truncate(string s, int maxLength)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= maxLength) return s ?? string.Empty;
            return s.Substring(0, maxLength);
        }

        private enum FetchOutcome
        {
            Success,
            UnchangedVersion,
            NotConfigured,
        }
    }

    internal sealed class BridgeSecretFetchException : Exception
    {
        public BridgeSecretFetchException(string message, Exception inner = null) : base(message, inner) { }
    }

    internal sealed class BridgeSecretNotConfiguredException : Exception
    {
        public BridgeSecretNotConfiguredException(string message) : base(message) { }
    }
}
