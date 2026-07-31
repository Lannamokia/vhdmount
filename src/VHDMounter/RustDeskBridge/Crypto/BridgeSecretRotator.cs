using System;
using System.Threading;
using System.Threading.Tasks;
using VHDMounter.RustDeskBridge.Policy;
using VHDMounter.RustDeskBridge.Revocation;

namespace VHDMounter.RustDeskBridge.Crypto
{
    /// <summary>
    /// Requirement 10.1.2 / 10.2 / 13.4：监听 <see cref="BridgeSecretClient.CurrentSecretVersion"/>
    /// 的版本切换并触发级联动作。
    ///
    /// <para>
    /// 行为（design §"路径 6：RustDeskClientSharedSecret 拉取" 末段）：
    /// <list type="number">
    /// <item>BridgeSecretClient 已经在 <c>TryFetchAndApplyAsync</c> 内部把 active 槽切到新版本
    ///       并对旧 byte[] 调用 <see cref="System.Security.Cryptography.CryptographicOperations.ZeroMemory"/>，
    ///       本类**不**重复抹零</item>
    /// <item>本类只观察 <see cref="BridgeSecretClient.CurrentSecretVersion"/>；当观察到值与上次记录不同时：
    ///   <list type="bullet">
    ///   <item>调 <see cref="RevocationPublisher.PushSecretOutdatedAsync"/>（不等飞行中 HTTP；
    ///         去重由 RevocationPublisher 自己负责，§12.6）</item>
    ///   <item>调 <see cref="SnapshotStore.Invalidate"/> 抹零内存中现有快照</item>
    ///   </list></item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// 不采用"BridgeSecretClient 暴露事件"模式（避免改动 Wave 4 已落地的代码）。简化方案：
    /// Rotator 自己跑一个 1 秒间隔的轮询循环检查 <see cref="BridgeSecretClient.CurrentSecretVersion"/>
    /// 与上次记录值比对。
    /// </para>
    ///
    /// <para>
    /// 启动时用一次性"初始同步"策略：第一次观察到的非零版本 SHALL **不**触发轮换动作，
    /// 仅记录为 <c>_lastSeenVersion</c>。之后任意非零变化都触发动作。这避免了
    /// "Bridge_Server 启动期 BridgeSecretClient 完成首次拉取"被误识别为热轮换。
    /// </para>
    /// </summary>
    internal sealed class BridgeSecretRotator : IAsyncDisposable
    {
        public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(1);

        private readonly BridgeSecretClient _secretClient;
        private readonly RevocationPublisher _revocations;
        private readonly SnapshotStore _snapshots;
        private readonly TimeSpan _pollInterval;
        private readonly Action<string> _diagnostics;

        private CancellationTokenSource _cts;
        private Task _runner;
        // 用 long 而非 uint，因为 Interlocked.Exchange<uint> / Read<uint> 不直接支持。
        // 实际值仍是 0..=uint.MaxValue，保留 long 仅是 API 适配。
        private long _lastSeenVersion;

        public BridgeSecretRotator(
            BridgeSecretClient secretClient,
            RevocationPublisher revocations,
            SnapshotStore snapshots,
            TimeSpan pollInterval = default,
            Action<string> diagnostics = null)
        {
            _secretClient = secretClient ?? throw new ArgumentNullException(nameof(secretClient));
            _revocations = revocations ?? throw new ArgumentNullException(nameof(revocations));
            _snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
            _pollInterval = pollInterval == default ? DefaultPollInterval : pollInterval;
            _diagnostics = diagnostics ?? (_ => { });
        }

        public uint LastSeenVersion => unchecked((uint)Interlocked.Read(ref _lastSeenVersion));

        public Task StartAsync(CancellationToken ct)
        {
            if (_runner != null)
            {
                throw new InvalidOperationException("BridgeSecretRotator 已经启动");
            }
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _runner = Task.Run(() => RunAsync(_cts.Token), CancellationToken.None);
            return Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            if (_cts == null) return;
            try { _cts.Cancel(); } catch (ObjectDisposedException) { /* already disposed */ }
            try
            {
                if (_runner != null)
                {
                    await _runner.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { /* expected */ }
            catch (Exception ex)
            {
                _diagnostics($"BridgeSecretRotator 停止异常：{ex.Message}");
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

        /// <summary>
        /// 单次检查：若 <see cref="BridgeSecretClient.CurrentSecretVersion"/> 与上次观察到的值不同
        /// 且二者都非 0（即 BridgeSecretClient 已经经历过至少一次成功加载），则触发级联动作并返回 true。
        /// </summary>
        public async Task<bool> CheckOnceAsync(CancellationToken ct = default)
        {
            var current = _secretClient.CurrentSecretVersion;
            var lastSeen = unchecked((uint)Interlocked.Read(ref _lastSeenVersion));

            if (current == 0)
            {
                // BridgeSecretClient 还没完成首次拉取，不做任何动作
                return false;
            }

            if (lastSeen == 0)
            {
                // 启动期"初始同步"：仅记录，不触发轮换
                Interlocked.Exchange(ref _lastSeenVersion, current);
                _diagnostics($"BridgeSecretRotator 初始同步到 secretVersion={current}");
                return false;
            }

            if (current == lastSeen)
            {
                return false;
            }

            // 检测到热轮换
            _diagnostics(
                $"BridgeSecretRotator 检测到 secretVersion 热轮换 {lastSeen} → {current}");

            // (a) 写入新观察到的版本（避免重复触发）
            Interlocked.Exchange(ref _lastSeenVersion, current);

            // (b) 抹零快照（不等 HTTP）
            try
            {
                _snapshots.Invalidate();
            }
            catch (Exception ex)
            {
                _diagnostics($"BridgeSecretRotator 抹零快照异常：{ex.Message}");
            }

            // (c) 推 Revocation 帧（去重 / 写帧 / cancelInflight / 关闭管道由 RevocationPublisher 内部处理）
            try
            {
                await _revocations.PushSecretOutdatedAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _diagnostics($"BridgeSecretRotator 推送 Revocation 异常：{ex.Message}");
            }

            return true;
        }

        // ---------- 内部循环 ----------

        private async Task RunAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_pollInterval, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }

                try
                {
                    await CheckOnceAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _diagnostics($"BridgeSecretRotator 轮询异常：{ex.Message}");
                }
            }
        }
    }
}
