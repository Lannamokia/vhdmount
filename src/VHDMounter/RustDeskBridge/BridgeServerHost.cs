using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VHDMounter.RustDeskBridge.Config;
using VHDMounter.RustDeskBridge.Crypto;
using VHDMounter.RustDeskBridge.Log;
using VHDMounter.RustDeskBridge.Pipe;
using VHDMounter.RustDeskBridge.Policy;
using VHDMounter.RustDeskBridge.RateLimit;
using VHDMounter.RustDeskBridge.Revocation;
using VHDMounter.RustDeskBridge.Session;
using VHDMounter.RustDeskBridge.Upload;

namespace VHDMounter.RustDeskBridge
{
    /// <summary>
    /// 任务 12.1：RustDesk 桥子系统总入口。
    ///
    /// 按 design §"进程内位置与启动顺序" 的 7 步顺序启动：
    /// <list type="number">
    /// <item><see cref="PolicyPubkeyClient.EnsureLoadedAsync"/> —— 必须先于 BridgeSecretClient
    ///       （后者要用它验签）；启动期 5×6 重试全失败 + 无本地缓存 →
    ///       <see cref="PolicyPubkeyLoadException"/>；BridgeServerHost 标记为"启动失败但不致命"，
    ///       下游全部 fail-closed</item>
    /// <item><see cref="BridgeSecretClient.EnsureLoadedAsync"/> —— 启动期 5×6 重试拉一次 secret；
    ///       服务端无 active secret (404) → 不抛、不阻塞 host，但 IsLoaded=false，下游 fail-closed</item>
    /// <item><see cref="SnapshotRefreshLoop.StartAsync"/> —— 启动一次 + 周期；只有当
    ///       PolicyPubkey 加载成功才允许启动（否则服务端响应签名无法验证）</item>
    /// <item><see cref="BridgeSecretRotator.StartAsync"/> —— 周期检查 secretVersion 切换</item>
    /// <item><see cref="ReportUploadQueue.RunAsync"/> —— 消费循环</item>
    /// <item><see cref="PipeAcceptLoop.StartAsync"/> —— 串行接受 + sessionRunner =
    ///       <see cref="SessionStateMachine.RunSessionAsync"/></item>
    /// <item><see cref="RevocationListener.StartAsync"/> —— HttpListener 监听 loopback
    ///       /rustdesk/revoke</item>
    /// </list>
    ///
    /// <para>
    /// <see cref="StopAsync"/> / <see cref="DisposeAsync"/> 反序停止：取消所有 long-running task →
    /// 关闭当前 Bridge_Session → Dispose 管道句柄 → 抹零 InMemoryObfuscation 会话密钥 /
    /// Password_Wrap_Key / RustDeskClientSharedSecret byte[]。
    /// </para>
    ///
    /// <para>
    /// SessionRunner wrapper：每次 PipeAcceptLoop 接受连接后，本类创建一个
    /// <see cref="BridgeSession"/>、把它写到 <see cref="_currentSession"/> 字段（lock 保护），
    /// 再调 <see cref="SessionStateMachine.RunSessionAsync"/>；finally 块中清空字段并 Dispose
    /// session。<see cref="RevocationPublisher"/> 通过 <c>getActiveSession</c> 委托读取当前
    /// session（推送 Revocation 帧到当前管道）。
    /// </para>
    /// </summary>
    internal sealed class BridgeServerHost : IAsyncDisposable
    {
        private readonly BridgeConfig _config;
        private readonly string _machineId;
        private readonly string _serverBaseUrl;
        private readonly string _registrationCertPath;
        private readonly HttpClient _httpClient;
        private readonly MachineLogBuffer _logBuffer;
        private readonly IRegistrationGate _registrationGate;
        private readonly IClock _clock;
        private readonly Action<string> _diagnostics;
        private readonly string _pipeName;

        // 持有的子组件（StartAsync 内部分阶段填充）
        private PolicyPubkeyClient _policyPubkey;
        private BridgeSecretClient _secretClient;
        private InMemoryObfuscation _obfuscation;
        private SnapshotStore _snapshotStore;
        private HandshakeNonceLruCache _nonceLru;
        private HandshakeRateLimiter _handshakeRateLimiter;
        private ReportRateLimiter _reportRateLimiter;
        private LastReportedSnapshot _lastReported;
        private LogIngestor _logIngestor;
        private BridgeLogDropCounter _logDropCounter;
        private WrapKeyClient _wrapKeys;
        private ReportUploader _reportUploader;
        private ReportUploadQueue _reportQueue;
        private PeerApprovalEvaluator _peerEvaluator;
        private SnapshotRefreshLoop _snapshotLoop;
        private BridgeSecretRotator _secretRotator;
        private PipeAcceptLoop _pipeAcceptLoop;
        private RevocationPublisher _revocationPublisher;
        private RevocationListener _revocationListener;
        private SessionStateMachine _sessionMachine;
        private HmacVerifier _hmacVerifier;

        // 长寿命任务
        private CancellationTokenSource _hostCts;
        private CancellationTokenSource _inflightCts; // 飞行中 HTTP 上行的取消源（Revocation 触发取消）
        private Task _logDropSummaryTask;
        private Task _secretRefreshTask;
        private Task _reportQueueTask;

        // 当前会话（用于 RevocationPublisher 推帧）
        private readonly object _sessionGate = new object();
        private BridgeSession _currentSession;

        private bool _running;
        private bool _disposed;

        public BridgeServerHost(
            BridgeConfig config,
            string machineId,
            string serverBaseUrl,
            string registrationCertPath,
            HttpClient httpClient,
            MachineLogBuffer machineLogBuffer,
            IRegistrationGate registrationGate,
            IClock clock,
            Action<string> diagnostics = null,
            string pipeNameOverride = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _machineId = machineId ?? throw new ArgumentNullException(nameof(machineId));
            _serverBaseUrl = (serverBaseUrl ?? string.Empty).TrimEnd('/');
            _registrationCertPath = registrationCertPath ?? string.Empty;
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logBuffer = machineLogBuffer ?? throw new ArgumentNullException(nameof(machineLogBuffer));
            _registrationGate = registrationGate ?? throw new ArgumentNullException(nameof(registrationGate));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _diagnostics = diagnostics ?? (_ => { });
            // 测试夹具用唯一管道名，让多个 BridgeServerHost 实例可以并行存在；
            // 生产路径默认走 BridgePipeFactory.DefaultPipeName
            _pipeName = string.IsNullOrWhiteSpace(pipeNameOverride)
                ? BridgePipeFactory.DefaultPipeName
                : pipeNameOverride;
        }

        /// <summary>
        /// 暴露 BridgeSecretClient（实现了 <see cref="IBridgeSecretProvider"/>），让外部
        /// 能查询 IsLoaded、CurrentSecretVersion 等运行期状态。
        /// </summary>
        public IBridgeSecretProvider SecretProvider => _secretClient;

        public bool IsRunning
        {
            get { lock (_sessionGate) return _running; }
        }

        /// <summary>
        /// 当前 BridgeServerHost 接受连接的管道名（不含 <c>\\.\pipe\</c> 前缀）。
        /// 仅给测试夹具引用 —— 让 NamedPipeClientStream 对接同一管道名。
        /// </summary>
        public string PipeName => _pipeName;

        public async Task StartAsync(CancellationToken ct)
        {
            ThrowIfDisposed();
            if (_running)
            {
                throw new InvalidOperationException("BridgeServerHost 已经启动");
            }

            _hostCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _inflightCts = CancellationTokenSource.CreateLinkedTokenSource(_hostCts.Token);
            var hostCt = _hostCts.Token;

            _diagnostics("BridgeServerHost 启动中…");

            // ---- Step 1: PolicyPubkeyClient ----
            _policyPubkey = new PolicyPubkeyClient(
                _config, _machineId, _serverBaseUrl, _registrationCertPath, _httpClient, _diagnostics);
            var policyLoaded = false;
            try
            {
                await _policyPubkey.EnsureLoadedAsync(hostCt).ConfigureAwait(false);
                policyLoaded = true;
            }
            catch (PolicyPubkeyLoadException ex)
            {
                _diagnostics(
                    $"BridgeServerHost: PolicyPubkey 启动期装载失败（{ex.Message}）；" +
                    "SnapshotRefreshLoop 不会启动，PeerApproval 全 rejected、Report 跳过上行 —— " +
                    "进程仍继续运行，等下一次重启重试");
            }
            catch (OperationCanceledException) when (hostCt.IsCancellationRequested)
            {
                throw;
            }

            // ---- Step 2: BridgeSecretClient ----
            _secretClient = new BridgeSecretClient(
                _config, _machineId, _serverBaseUrl, _httpClient, _policyPubkey, _diagnostics);
            try
            {
                await _secretClient.EnsureLoadedAsync(hostCt).ConfigureAwait(false);
            }
            catch (BridgeSecretNotConfiguredException ex)
            {
                _diagnostics(
                    $"BridgeServerHost: 服务端无 active RustDeskClientSharedSecret（{ex.Message}）；" +
                    "Bridge_Server 仍启动，但 IsLoaded=false → 握手必返回 invalid_proof、Report 跳过上行");
            }
            catch (BridgeSecretFetchException ex)
            {
                _diagnostics(
                    $"BridgeServerHost: BridgeSecret 启动期装载失败（{ex.Message}）；fail-closed 继续启动");
            }
            catch (OperationCanceledException) when (hostCt.IsCancellationRequested)
            {
                throw;
            }

            // ---- 创建运行期共享组件 ----
            _hmacVerifier = new HmacVerifier(_secretClient);
            _obfuscation = new InMemoryObfuscation();
            _snapshotStore = new SnapshotStore(_obfuscation, _clock);
            _nonceLru = new HandshakeNonceLruCache(
                _config.HandshakeNonceLruCapacity, TimeSpan.FromMinutes(5), _clock);
            _handshakeRateLimiter = new HandshakeRateLimiter(_clock);
            _reportRateLimiter = new ReportRateLimiter(_clock);
            _lastReported = new LastReportedSnapshot();
            _logIngestor = new LogIngestor(_logBuffer);
            _logDropCounter = new BridgeLogDropCounter(_logBuffer, utcNow: () => _clock.UtcNow);

            _wrapKeys = new WrapKeyClient(
                _config, _machineId, _serverBaseUrl, _httpClient, _policyPubkey,
                utcNow: () => _clock.UtcNow,
                diagnostics: _diagnostics);
            _reportUploader = new ReportUploader(
                _config, _wrapKeys, _policyPubkey, _httpClient,
                _machineId, _serverBaseUrl, _diagnostics);
            _reportQueue = new ReportUploadQueue(
                _reportUploader, _diagnostics, _config.ReportRetryQueueCapacity);

            _peerEvaluator = new PeerApprovalEvaluator(_snapshotStore, _registrationGate);
            _sessionMachine = new SessionStateMachine(
                _hmacVerifier, _nonceLru, _snapshotStore, _peerEvaluator,
                _logIngestor, _logDropCounter, _lastReported,
                _reportRateLimiter, _reportQueue, _handshakeRateLimiter,
                _secretClient, _machineId, _clock, _diagnostics);

            // RevocationPublisher：getActiveSession 委托从 _currentSession 字段读
            _revocationPublisher = new RevocationPublisher(
                _hmacVerifier, _snapshotStore, _logBuffer, _clock, _secretClient,
                getActiveSession: () => { lock (_sessionGate) return _currentSession; },
                cancelInflightRequests: CancelInflightRequestsAsync,
                diagnostics: _diagnostics);

            _secretRotator = new BridgeSecretRotator(
                _secretClient, _revocationPublisher, _snapshotStore,
                diagnostics: _diagnostics);

            // ---- Step 3: SnapshotRefreshLoop（仅 PolicyPubkey 装载成功时启动）----
            if (policyLoaded)
            {
                _snapshotLoop = new SnapshotRefreshLoop(
                    _config, _snapshotStore, _policyPubkey, _httpClient,
                    _machineId, _serverBaseUrl, _registrationGate, _clock, _diagnostics);
                await _snapshotLoop.StartAsync(hostCt).ConfigureAwait(false);
            }
            else
            {
                _diagnostics("BridgeServerHost: 跳过 SnapshotRefreshLoop 启动（PolicyPubkey 未加载）");
            }

            // ---- Step 4: BridgeSecretRotator ----
            await _secretRotator.StartAsync(hostCt).ConfigureAwait(false);

            // ---- 启动周期 secret 刷新循环（属于 BridgeSecretClient，不 throw） ----
            _secretRefreshTask = Task.Run(
                () => _secretClient.RunRefreshLoopAsync(hostCt), CancellationToken.None);

            // ---- Step 5: ReportUploadQueue 消费循环 ----
            _reportQueueTask = Task.Run(() => _reportQueue.RunAsync(hostCt), CancellationToken.None);

            // ---- BridgeLogDropCounter 60s 滚动汇总 ----
            _logDropSummaryTask = Task.Run(
                () => _logDropCounter.RunSummaryLoopAsync(hostCt), CancellationToken.None);

            // ---- Step 6: PipeAcceptLoop ----
            // sessionRunner wrapper：每次接受连接 → 创建 BridgeSession → 写 _currentSession →
            //   调 sessionMachine.RunSessionAsync → finally 清字段并 Dispose
            PipeAcceptLoop.SessionRunnerDelegate sessionRunner = async (pipeStream, coolingDown, runCt) =>
            {
                var session = new BridgeSession(pipeStream);
                lock (_sessionGate) { _currentSession = session; }
                try
                {
                    await _sessionMachine.RunSessionAsync(session, coolingDown, runCt).ConfigureAwait(false);
                }
                finally
                {
                    lock (_sessionGate) { _currentSession = null; }
                    try { session.Dispose(); } catch { /* ignore */ }
                }
            };
            _pipeAcceptLoop = new PipeAcceptLoop(
                _pipeName,
                sessionRunner,
                _handshakeRateLimiter,
                _logBuffer,
                _clock,
                _diagnostics);
            await _pipeAcceptLoop.StartAsync(hostCt).ConfigureAwait(false);

            // ---- Step 7: RevocationListener ----
            _revocationListener = new RevocationListener(_config, _revocationPublisher, _diagnostics);
            await _revocationListener.StartAsync(hostCt).ConfigureAwait(false);

            lock (_sessionGate) { _running = true; }
            _diagnostics(
                $"BridgeServerHost 已启动 secretLoaded={_secretClient.IsLoaded} " +
                $"policyLoaded={policyLoaded} pipe=\\\\.\\pipe\\{_pipeName}");
        }

        public async Task StopAsync()
        {
            if (_hostCts == null)
            {
                return;
            }

            _diagnostics("BridgeServerHost 停止中（反序）…");
            try { _hostCts.Cancel(); } catch (ObjectDisposedException) { /* ignore */ }
            try { _inflightCts?.Cancel(); } catch (ObjectDisposedException) { /* ignore */ }

            // 反序停止：RevocationListener → PipeAcceptLoop → 长寿命 Task → SecretRotator → SnapshotLoop
            await SafeStopAsync(() => _revocationListener?.StopAsync()).ConfigureAwait(false);
            await SafeStopAsync(() => _pipeAcceptLoop?.StopAsync()).ConfigureAwait(false);

            // 关闭当前会话（如果还在）
            BridgeSession lingering;
            lock (_sessionGate)
            {
                lingering = _currentSession;
                _currentSession = null;
            }
            try { lingering?.Close(); } catch { /* ignore */ }
            try { lingering?.Dispose(); } catch { /* ignore */ }

            await AwaitNoThrowAsync(_logDropSummaryTask).ConfigureAwait(false);
            await AwaitNoThrowAsync(_reportQueueTask).ConfigureAwait(false);
            await AwaitNoThrowAsync(_secretRefreshTask).ConfigureAwait(false);

            await SafeStopAsync(() => _secretRotator?.StopAsync()).ConfigureAwait(false);
            await SafeStopAsync(() => _snapshotLoop?.StopAsync()).ConfigureAwait(false);

            // ---- 抹零内存敏感字节 ----
            try { _logDropCounter?.FlushIfNonzero(); } catch { /* ignore */ }
            try { _lastReported?.Clear(); } catch { /* ignore */ }
            try { _wrapKeys?.Dispose(); } catch { /* ignore */ }
            try { _reportQueue?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
            try { _secretClient?.Dispose(); } catch { /* ignore */ }
            try { _obfuscation?.Dispose(); } catch { /* ignore */ }
            try { _nonceLru?.Dispose(); } catch { /* ignore */ }
            try { _policyPubkey?.Dispose(); } catch { /* ignore */ }

            try { _hostCts.Dispose(); } catch { /* ignore */ }
            try { _inflightCts?.Dispose(); } catch { /* ignore */ }
            _hostCts = null;
            _inflightCts = null;

            lock (_sessionGate) { _running = false; }
            _diagnostics("BridgeServerHost 已停止，敏感字节已抹零");
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            await StopAsync().ConfigureAwait(false);
        }

        // ---------- 内部辅助 ----------

        private Task CancelInflightRequestsAsync(CancellationToken externalCt)
        {
            // 通过取消 _inflightCts 让所有持有该 token 的 HTTP 请求 Future 抛 TaskCanceled
            var cts = _inflightCts;
            if (cts != null)
            {
                try { cts.Cancel(); } catch (ObjectDisposedException) { /* ignore */ }
                // 立即重新分配一个新的 inflightCts（关联 hostCts），下一次飞行 HTTP 用它
                try
                {
                    var hostCts = _hostCts;
                    if (hostCts != null && !hostCts.IsCancellationRequested)
                    {
                        var newCts = CancellationTokenSource.CreateLinkedTokenSource(hostCts.Token);
                        var old = Interlocked.Exchange(ref _inflightCts, newCts);
                        try { old?.Dispose(); } catch { /* ignore */ }
                    }
                }
                catch
                {
                    // 进程退出竞态：忽略
                }
            }
            return Task.CompletedTask;
        }

        private static async Task SafeStopAsync(Func<Task> stop)
        {
            if (stop == null) return;
            try
            {
                var t = stop();
                if (t != null) await t.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 反序停止路径不抛
            }
        }

        private static async Task AwaitNoThrowAsync(Task task)
        {
            if (task == null) return;
            try { await task.ConfigureAwait(false); }
            catch { /* ignore */ }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(BridgeServerHost));
        }
    }
}
