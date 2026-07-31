using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VHDMounter.RustDeskBridge.Config;
using VHDMounter.RustDeskBridge.Crypto;
using VHDMounter.RustDeskBridge.Upload;

namespace VHDMounter.RustDeskBridge.Policy
{
    /// <summary>
    /// Requirement 8.1 / 8.2 / 8.6 / 8.9 / 16.5：Trusted_Controllers_Snapshot 启动期 + 周期拉取。
    ///
    /// 启动 <see cref="StartAsync"/>：
    /// (1) 立即触发一次拉取（Requirement 8.1）；
    /// (2) 之后按 <see cref="BridgeConfig.SnapshotPullIntervalSeconds"/>（夹紧 60–600）周期拉取（Requirement 8.2）；
    /// (3) <see cref="IRegistrationGate.IsRegisteredAndApproved"/> == false 期间跳过本周期拉取（§8.9）；
    /// (4) 拉取结果交 <see cref="SnapshotStore.TryReplace"/>（三步严格校验）；
    /// (5) 连续失败 ≥ 3 次 + 距上次成功 &gt; 600s → 触发 §8.6.4 fail-closed
    ///     （直接 <see cref="SnapshotStore.Invalidate"/>，下一次 PeerApproval 一律 rejected
    ///      直到下一次拉取成功）。
    ///
    /// 失败仅写日志，不抛异常 —— Bridge_Server 主循环不被本任务的临时网络错误击穿。
    /// </summary>
    internal sealed class SnapshotRefreshLoop : IAsyncDisposable
    {
        public const long FailClosedHealthMs = 600_000;
        public const int FailClosedFailureThreshold = 3;

        private readonly BridgeConfig _config;
        private readonly SnapshotStore _store;
        private readonly IPolicyPubkeyValidator _validator;
        private readonly HttpClient _httpClient;
        private readonly string _machineId;
        private readonly string _serverBaseUrl;
        private readonly IRegistrationGate _registrationGate;
        private readonly IClock _clock;
        private readonly Action<string> _diagnostics;

        private CancellationTokenSource _cts;
        private Task _runner;
        private long _consecutiveFailureCount;
        private long _lastSuccessUtcMs;

        public SnapshotRefreshLoop(
            BridgeConfig config,
            SnapshotStore store,
            IPolicyPubkeyValidator validator,
            HttpClient httpClient,
            string machineId,
            string serverBaseUrl,
            IRegistrationGate registrationGate,
            IClock clock,
            Action<string> diagnostics = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _machineId = machineId ?? throw new ArgumentNullException(nameof(machineId));
            _serverBaseUrl = (serverBaseUrl ?? string.Empty).TrimEnd('/');
            _registrationGate = registrationGate ?? throw new ArgumentNullException(nameof(registrationGate));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _diagnostics = diagnostics ?? (_ => { });
        }

        public Task StartAsync(CancellationToken ct)
        {
            if (_runner != null)
            {
                throw new InvalidOperationException("SnapshotRefreshLoop 已经启动");
            }
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _runner = Task.Run(() => RunAsync(_cts.Token), CancellationToken.None);
            return Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            if (_cts == null) return;
            try { _cts.Cancel(); } catch (ObjectDisposedException) { /* already stopped */ }
            try
            {
                if (_runner != null)
                {
                    await _runner.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { /* normal */ }
            catch (Exception ex)
            {
                _diagnostics($"SnapshotRefreshLoop 停止异常：{ex.Message}");
            }
            finally
            {
                try { _cts.Dispose(); } catch { /* ignore */ }
                _cts = null;
                _runner = null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);
        }

        // ---------- 内部 ----------

        private async Task RunAsync(CancellationToken ct)
        {
            // §8.1：启动期立即拉一次
            await TryPullOnceAsync(ct).ConfigureAwait(false);

            var period = TimeSpan.FromSeconds(ClampInterval(_config.SnapshotPullIntervalSeconds));
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

                await TryPullOnceAsync(ct).ConfigureAwait(false);
            }
        }

        private async Task TryPullOnceAsync(CancellationToken ct)
        {
            // §8.9：未注册 / 未审批 期间跳过拉取
            if (!_registrationGate.IsRegisteredAndApproved)
            {
                return;
            }

            try
            {
                var snapshotJson = await FetchSnapshotJsonAsync(ct).ConfigureAwait(false);
                var ok = _store.TryReplace(snapshotJson, _validator, out var rejectReason);
                if (ok)
                {
                    Interlocked.Exchange(ref _consecutiveFailureCount, 0);
                    Interlocked.Exchange(ref _lastSuccessUtcMs, _clock.UtcNow.ToUnixTimeMilliseconds());
                    return;
                }

                _diagnostics($"Trusted_Controllers_Snapshot 替换被拒绝：{rejectReason}");
                _store.RecordRefreshFailure();
                AdvanceFailureAndMaybeFailClosed();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _diagnostics($"Trusted_Controllers_Snapshot 拉取失败：{ex.Message}");
                _store.RecordRefreshFailure();
                AdvanceFailureAndMaybeFailClosed();
            }
        }

        private void AdvanceFailureAndMaybeFailClosed()
        {
            var failures = Interlocked.Increment(ref _consecutiveFailureCount);
            var lastSuccess = Interlocked.Read(ref _lastSuccessUtcMs);
            var nowMs = _clock.UtcNow.ToUnixTimeMilliseconds();

            // §8.6.4：连续 ≥ 3 次失败 + 距上次成功 > 600s（首次启动 lastSuccess == 0
            //         → nowMs - 0 > 600s 自然成立，所以三次失败也会触发 fail-closed）
            if (failures >= FailClosedFailureThreshold && (nowMs - lastSuccess) > FailClosedHealthMs)
            {
                _diagnostics(
                    $"Trusted_Controllers_Snapshot 连续 {failures} 次失败、距上次成功 {nowMs - lastSuccess}ms" +
                    "，触发 §8.6.4 fail-closed");
                _store.Invalidate();
            }
        }

        private async Task<string> FetchSnapshotJsonAsync(CancellationToken ct)
        {
            var url = $"{_serverBaseUrl}/api/machines/{Uri.EscapeDataString(_machineId)}/rustdesk/trusted-controllers";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            var keyId = RustDeskReportSigner.BuildDefaultKeyId(_machineId);
            RustDeskReportSigner.SignSnapshotFetch(request, _machineId, keyId);

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new IOException(
                    $"GET /rustdesk/trusted-controllers 返回 {(int)response.StatusCode}：{Truncate(body, 200)}");
            }
            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }

        private static int ClampInterval(int seconds)
        {
            if (seconds < BridgeConfig.SnapshotPullIntervalMinSeconds)
                return BridgeConfig.SnapshotPullIntervalMinSeconds;
            if (seconds > BridgeConfig.SnapshotPullIntervalMaxSeconds)
                return BridgeConfig.SnapshotPullIntervalMaxSeconds;
            return seconds;
        }

        private static string Truncate(string s, int maxLength)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= maxLength) return s ?? string.Empty;
            return s.Substring(0, maxLength);
        }
    }
}
