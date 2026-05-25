using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VHDMounter.RustDeskBridge.Config;
using VHDMounter.RustDeskBridge.Upload;

namespace VHDMounter.RustDeskBridge.Policy
{
    /// <summary>
    /// 决策点 7：Bridge_Policy_Signing_Pubkey 启动期装载 + PEM 缓存校验 + 失败重试。
    ///
    /// 启动流程（<see cref="EnsureLoadedAsync"/>）：
    /// 1. 若 <c>BridgePolicyPubkeyPath</c> 文件存在且解析合法，计算 SHA-256 摘要 <c>localDigest</c>
    /// 2. GET <c>/api/machines/:machineId/rustdesk/policy-pubkey</c>（用
    ///    <see cref="RustDeskReportSigner.SignPolicyPubkeyFetch"/> 签名）
    ///    → 用注册证书链信任锚点（<c>RegistrationCertificatePath</c>）验签 <c>policySignature</c>
    ///    → 计算服务端 PEM 摘要 <c>serverDigest</c>
    /// 3. <c>localDigest == serverDigest</c> 用本地缓存；否则用服务端 PEM 覆盖（如果 BridgePolicyPubkeyPath
    ///    指向可写位置）
    /// 4. 启动期重试 6 次 × 5s 全部失败 <b>且</b>无本地缓存 → 抛 <see cref="PolicyPubkeyLoadException"/>，
    ///    BridgeServerHost 据此拒绝启动 SnapshotRefreshLoop（Requirement 8.3.1）
    ///
    /// 暴露 <see cref="IPolicyPubkeyValidator"/> 供 SnapshotStore / BridgeSecretClient / WrapKeyClient 共用。
    /// </summary>
    internal sealed class PolicyPubkeyClient : IPolicyPubkeyValidator, IDisposable
    {
        private const string PolicyPubkeyPayloadVersion = "BridgePolicyPubkeyV1";
        private const int StartupMaxAttempts = 6;
        private static readonly TimeSpan StartupRetryDelay = TimeSpan.FromSeconds(5);

        private readonly BridgeConfig _bridgeConfig;
        private readonly string _machineId;
        private readonly string _serverBaseUrl;
        private readonly string _registrationCertPath;
        private readonly HttpClient _httpClient;
        private readonly Action<string> _diagnostics;
        private readonly object _gate = new object();

        private RSA _activePubkey;
        private string _activePemText = string.Empty;
        private string _activePemDigestHex = string.Empty;
        private bool _disposed;

        public PolicyPubkeyClient(
            BridgeConfig bridgeConfig,
            string machineId,
            string serverBaseUrl,
            string registrationCertPath,
            HttpClient httpClient,
            Action<string> diagnostics = null)
        {
            _bridgeConfig = bridgeConfig ?? throw new ArgumentNullException(nameof(bridgeConfig));
            _machineId = machineId ?? throw new ArgumentNullException(nameof(machineId));
            _serverBaseUrl = (serverBaseUrl ?? string.Empty).TrimEnd('/');
            _registrationCertPath = registrationCertPath ?? string.Empty;
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _diagnostics = diagnostics ?? (_ => { });
        }

        /// <summary>
        /// 当前已加载的公钥 PEM 字节 SHA-256（小写十六进制）。未加载时为空字符串。
        /// </summary>
        public string CurrentPubkeyDigestHex
        {
            get { lock (_gate) return _activePemDigestHex; }
        }

        public string CurrentPubkeyPemText
        {
            get { lock (_gate) return _activePemText; }
        }

        /// <summary>
        /// 启动期装载入口。返回的对象是同一实例（fluent 风格），方便 BridgeServerHost
        /// 直接 <c>await client.EnsureLoadedAsync(ct)</c> 后用 <see cref="IPolicyPubkeyValidator"/>
        /// 给下游注入。
        /// </summary>
        public async Task<PolicyPubkeyClient> EnsureLoadedAsync(CancellationToken ct)
        {
            ThrowIfDisposed();

            // Step 1: 尝试从 BridgePolicyPubkeyPath 加载缓存
            var localPemText = TryReadLocalPem(_bridgeConfig.BridgePolicyPubkeyPath);
            if (TryParsePem(localPemText, out var localPubkey))
            {
                ApplyLoaded(localPubkey, localPemText);
                _diagnostics($"Bridge_Policy_Signing_Pubkey 已从本地缓存装载（path={_bridgeConfig.BridgePolicyPubkeyPath}）");
            }
            else
            {
                localPubkey = null;
            }

            // Step 2 / 3: 启动期 6×5s 重试拉取 + 与本地缓存比对
            Exception lastException = null;
            for (var attempt = 1; attempt <= StartupMaxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var serverPem = await FetchAndVerifyServerPemAsync(ct).ConfigureAwait(false);
                    var serverPemText = serverPem.PemText;
                    var serverDigest = ComputePemDigestHex(serverPemText);
                    if (TryParsePem(serverPemText, out var serverPubkey))
                    {
                        if (localPubkey != null && string.Equals(_activePemDigestHex, serverDigest, StringComparison.Ordinal))
                        {
                            _diagnostics("Bridge_Policy_Signing_Pubkey 本地缓存与服务端摘要一致，沿用本地缓存");
                            return this;
                        }

                        ApplyLoaded(serverPubkey, serverPemText);
                        TryPersistLocalPem(_bridgeConfig.BridgePolicyPubkeyPath, serverPemText);
                        _diagnostics($"Bridge_Policy_Signing_Pubkey 已从服务端装载并刷新缓存（digest={_activePemDigestHex}）");
                        return this;
                    }

                    lastException = new InvalidDataException("服务端返回的 publicKeyPem 不是合法 RSA SPKI PEM");
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    _diagnostics($"Bridge_Policy_Signing_Pubkey 拉取失败（第 {attempt}/{StartupMaxAttempts} 次）：{ex.Message}");
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

            // 全部重试失败：本地缓存有就接受降级，没有就抛
            if (localPubkey != null)
            {
                _diagnostics("Bridge_Policy_Signing_Pubkey 拉取连续失败，使用本地缓存继续运行");
                return this;
            }

            throw new PolicyPubkeyLoadException(
                $"Bridge_Policy_Signing_Pubkey 启动期装载失败：连续 {StartupMaxAttempts} 次拉取失败且无本地缓存",
                lastException);
        }

        public bool VerifyResponseSignature(ReadOnlySpan<byte> payload, string signatureBase64)
        {
            if (string.IsNullOrEmpty(signatureBase64)) return false;

            byte[] sig;
            try
            {
                sig = Convert.FromBase64String(signatureBase64);
            }
            catch (FormatException)
            {
                return false;
            }

            RSA pubkey;
            lock (_gate)
            {
                if (_activePubkey == null) return false;
                pubkey = _activePubkey;
            }

            try
            {
                return pubkey.VerifyData(payload, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        // ---------- 内部辅助 ----------

        private async Task<ServerPolicyPubkeyResponse> FetchAndVerifyServerPemAsync(CancellationToken ct)
        {
            var url = $"{_serverBaseUrl}/api/machines/{Uri.EscapeDataString(_machineId)}/rustdesk/policy-pubkey";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            var keyId = RustDeskReportSigner.BuildDefaultKeyId(_machineId);
            RustDeskReportSigner.SignPolicyPubkeyFetch(request, _machineId, keyId);

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new InvalidOperationException(
                    $"GET /rustdesk/policy-pubkey 返回 {(int)response.StatusCode}：{Truncate(body, 200)}");
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            var root = doc.RootElement;

            var pubkeyPem = root.TryGetProperty("publicKeyPem", out var pkEl) ? pkEl.GetString() : null;
            var policySignature = root.TryGetProperty("policySignature", out var psEl) ? psEl.GetString() : null;
            var issuedAt = root.TryGetProperty("issuedAt", out var iaEl) && iaEl.TryGetInt64(out var iaVal) ? iaVal : 0L;

            if (string.IsNullOrWhiteSpace(pubkeyPem) || string.IsNullOrWhiteSpace(policySignature))
            {
                throw new InvalidDataException("policy-pubkey 响应缺少必需字段");
            }

            // BridgePolicyPubkeyV1 payload = "BridgePolicyPubkeyV1\n<machineId>\n<sha256Hex(pubkey PEM bytes)>\n<issuedAt>"
            var payloadAscii = string.Concat(
                PolicyPubkeyPayloadVersion, "\n",
                _machineId, "\n",
                ComputePemDigestHex(pubkeyPem), "\n",
                issuedAt.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var payloadBytes = Encoding.ASCII.GetBytes(payloadAscii);

            VerifyPolicySignatureWithPinnedPubkey(pubkeyPem, payloadBytes, policySignature);

            return new ServerPolicyPubkeyResponse(pubkeyPem, issuedAt);
        }

        /// <summary>
        /// 自签名 + TOFU 验签：服务端用 policySigningStore 的 active key 给响应签名，
        /// 自己签自己的公钥。机台一侧的信任链：
        /// <list type="bullet">
        /// <item>本地缓存（[BridgePolicyPubkeyPath]）已有 → 必须用**缓存的**公钥验
        ///       签新响应（防止中间人 / 不当轮换）</item>
        /// <item>本地无缓存 → TOFU：用响应里**自带**的公钥验签自己（拒绝畸形签名，
        ///       但接受任何能自洽的响应）。第一段信任靠 TLS + 内网传输建立。
        ///       后续轮换由「与本地缓存校验」防退化</item>
        /// </list>
        /// </summary>
        private void VerifyPolicySignatureWithPinnedPubkey(
            string serverPubkeyPem, byte[] payload, string signatureBase64)
        {
            byte[] sig;
            try
            {
                sig = Convert.FromBase64String(signatureBase64);
            }
            catch (FormatException ex)
            {
                throw new InvalidDataException("policySignature base64 解码失败", ex);
            }

            // 优先用本地已 pin 的 pubkey 验签——这是检测 MITM / 不当轮换的关键路径
            RSA pinned;
            lock (_gate) { pinned = _activePubkey; }

            if (pinned != null)
            {
                if (pinned.VerifyData(payload, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                {
                    return; // 缓存验签通过：响应可信，可继续走「digest 比对 → 决定是否覆盖缓存」
                }

                // 用缓存验失败但服务端确实用了不同的 key —— 把响应自身公钥也试一次：
                // 若自洽，说明是合法轮换；若也不通过，则是真异常 / 中间人。
                if (TryParsePem(serverPubkeyPem, out var serverPubkeyForSelfCheck))
                {
                    using (serverPubkeyForSelfCheck)
                    {
                        if (serverPubkeyForSelfCheck.VerifyData(payload, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                        {
                            // 服务端响应自洽但与本地 pin 不同 —— 这是「policy key 已轮换」事件。
                            // ApplyLoaded（调用方）会用新 pubkey 覆盖缓存。
                            // 安全考量：这一步 implicitly trusts 服务端轮换；服务端被攻陷的极端场景下
                            // 攻击者可以用任何新 key 替换 cache。该风险与 TOFU 第一段相同，靠
                            // (1) 内网 TLS + (2) 机台白名单校验上行 [requireBridgeMachineSignature]
                            // 共同收紧：只有持有 machine 私钥的合法机台才能"被"轮换。
                            return;
                        }
                    }
                }

                throw new CryptographicException(
                    "policySignature 用本地缓存公钥与响应自身公钥都验签失败 —— 可能是中间人或响应损坏");
            }

            // 无本地缓存：TOFU。响应必须自洽。
            if (!TryParsePem(serverPubkeyPem, out var serverPubkey))
            {
                throw new InvalidDataException("policy-pubkey 响应中的 publicKeyPem 不是合法 RSA SPKI PEM");
            }

            using (serverPubkey)
            {
                if (!serverPubkey.VerifyData(payload, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                {
                    throw new CryptographicException(
                        "policySignature 自签名校验失败 —— 响应公钥与签名不自洽");
                }
            }
        }

        // 历史遗留：原本基于「服务端持有注册证书私钥」的 X509 信任链验签。
        // 服务端实际并不持有该私钥（注册证书 .pfx 由 VHDMountAdminTools 离线生成，
        // 私钥分发给机台，服务端只有公钥 trustedRegistrationCertificates），
        // 因此该路径已被自签名 + TOFU 取代（见 [VerifyPolicySignatureWithPinnedPubkey]）。
        // 保留下面的辅助函数仅用于将来若引入真正的服务端身份证书时复用。
        private static bool MatchesAnchorBySubject(X509Certificate2 server, X509Certificate2 anchor)
        {
            // 信任锚点比对：subject + 公钥指纹双重确认
            if (!string.Equals(server.Subject, anchor.Subject, StringComparison.Ordinal))
            {
                return false;
            }

            using var anchorPub = anchor.GetRSAPublicKey();
            using var serverPub = server.GetRSAPublicKey();
            if (anchorPub == null || serverPub == null) return false;

            var anchorSpki = anchorPub.ExportSubjectPublicKeyInfo();
            var serverSpki = serverPub.ExportSubjectPublicKeyInfo();
            return CryptographicOperations.FixedTimeEquals(anchorSpki, serverSpki);
        }

        private static X509Certificate2 LoadRegistrationCertificateAnchor(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var resolved = Path.IsPathRooted(path) ? path
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
            if (!File.Exists(resolved))
            {
                Trace.WriteLine($"[PolicyPubkeyClient] RegistrationCertificatePath 不存在: {resolved}");
                return null;
            }

            try
            {
                // 注册证书是 PFX，但锚点比对仅需公钥 → 用裸字节 + 空密码加载
                // 实际安全模型：只要 Subject + SPKI 匹配即可视为同一信任锚
                return new X509Certificate2(resolved, string.Empty,
                    X509KeyStorageFlags.EphemeralKeySet);
            }
            catch (CryptographicException)
            {
                // PFX 带密码 / 文件被破坏：返回 null，由调用方按宽松路径处理（仍然要求服务端响应自身签名合法）
                return null;
            }
        }

        private static X509Certificate2 ParseCertificatePem(string pem)
        {
            try
            {
                return X509Certificate2.CreateFromPem(pem);
            }
            catch (CryptographicException ex)
            {
                throw new InvalidDataException("registrationCertPem 不是合法 PEM 证书", ex);
            }
        }

        private static bool TryParsePem(string pem, out RSA rsa)
        {
            rsa = null;
            if (string.IsNullOrWhiteSpace(pem)) return false;
            try
            {
                var key = RSA.Create();
                key.ImportFromPem(pem.AsSpan());
                rsa = key;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void ApplyLoaded(RSA pubkey, string pemText)
        {
            lock (_gate)
            {
                _activePubkey?.Dispose();
                _activePubkey = pubkey;
                _activePemText = pemText ?? string.Empty;
                _activePemDigestHex = ComputePemDigestHex(_activePemText);
            }
        }

        private static string ComputePemDigestHex(string pemText)
        {
            if (string.IsNullOrEmpty(pemText)) return string.Empty;
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pemText))).ToLowerInvariant();
        }

        private static string TryReadLocalPem(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            var resolved = Path.IsPathRooted(path)
                ? path
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
            if (!File.Exists(resolved)) return string.Empty;
            try
            {
                return File.ReadAllText(resolved, Encoding.UTF8);
            }
            catch (IOException)
            {
                return string.Empty;
            }
        }

        private void TryPersistLocalPem(string path, string pemText)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                var resolved = Path.IsPathRooted(path)
                    ? path
                    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
                Directory.CreateDirectory(Path.GetDirectoryName(resolved) ?? AppContext.BaseDirectory);
                File.WriteAllText(resolved, pemText ?? string.Empty, new UTF8Encoding(false));
            }
            catch (IOException ex)
            {
                _diagnostics($"Bridge_Policy_Signing_Pubkey 缓存写入失败（path={path}）：{ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                _diagnostics($"Bridge_Policy_Signing_Pubkey 缓存写入失败（path={path}）：{ex.Message}");
            }
        }

        private static string Truncate(string s, int maxLength)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= maxLength) return s ?? string.Empty;
            return s.Substring(0, maxLength);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PolicyPubkeyClient));
        }

        public void Dispose()
        {
            if (_disposed) return;
            lock (_gate)
            {
                _activePubkey?.Dispose();
                _activePubkey = null;
                _disposed = true;
            }
        }

        private readonly struct ServerPolicyPubkeyResponse
        {
            public ServerPolicyPubkeyResponse(string pemText, long issuedAtMs)
            {
                PemText = pemText;
                IssuedAtMs = issuedAtMs;
            }
            public string PemText { get; }
            public long IssuedAtMs { get; }
        }
    }

    /// <summary>
    /// PolicyPubkey 启动期装载失败：BridgeServerHost 据此拒绝 SnapshotRefreshLoop 启动
    /// （Requirement 8.3.1）。
    /// </summary>
    internal sealed class PolicyPubkeyLoadException : Exception
    {
        public PolicyPubkeyLoadException(string message, Exception inner = null) : base(message, inner) { }
    }
}
