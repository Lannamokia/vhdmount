using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace VHDMounter.SoftwareDeploy
{
    public class DeployPoller : IDisposable
    {
        private readonly string _serverUrl;
        private readonly string _machineId;
        private readonly string _trustedKeysPath;
        private readonly string _baseDir;
        private readonly DeployHistoryStore _historyStore;
        private readonly DeployDownloader _downloader;
        private readonly DeployReporter _reporter;
        private readonly HttpClient _httpClient;
        private readonly CancellationTokenSource _cts;
        private Task? _pollTask;
        private readonly string _appVersion;
        private const string UA_PREFIX = "VHDMount:";
        private const int POLL_INTERVAL_MS = 60000; // 60s

        public event EventHandler<string>? OnDeployStarted;
        public event EventHandler<string>? OnDeployCompleted;

        public DeployPoller(string serverUrl, string machineId, string trustedKeysPath, string baseDir)
        {
            _serverUrl = serverUrl.TrimEnd('/');
            _machineId = machineId;
            _trustedKeysPath = trustedKeysPath;
            _baseDir = baseDir;
            _historyStore = new DeployHistoryStore(baseDir);
            _downloader = new DeployDownloader();
            _reporter = new DeployReporter(serverUrl, machineId);
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            _cts = new CancellationTokenSource();

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            _appVersion = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
        }

        public void Start()
        {
            if (_pollTask != null) return;
            _pollTask = Task.Run(PollLoopAsync);
        }

        public void Stop()
        {
            _cts.Cancel();
            _pollTask?.Wait(TimeSpan.FromSeconds(10));
        }

        private async Task PollLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await PollOnceAsync(_cts.Token);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[DeployPoller] 轮询异常: {ex.Message}");
                }

                try
                {
                    await Task.Delay(POLL_INTERVAL_MS, _cts.Token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        private async Task PollOnceAsync(CancellationToken ct)
        {
            var tasks = await FetchPendingTasksAsync(ct);
            if (tasks == null || tasks.Length == 0) return;

            foreach (var task in tasks)
            {
                if (ct.IsCancellationRequested) break;
                await ProcessTaskAsync(task, ct);
            }
        }

        private async Task<PendingTaskInfo[]> FetchPendingTasksAsync(CancellationToken ct)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get,
                    $"{_serverUrl}/api/machines/{_machineId}/deployments/pending");
                request.Headers.Add("User-Agent", $"{UA_PREFIX}{_appVersion}");

                var response = await _httpClient.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode) return Array.Empty<PendingTaskInfo>();

                var result = await response.Content.ReadFromJsonAsync<PendingTasksResponse>(ct);
                return result?.tasks ?? Array.Empty<PendingTaskInfo>();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[DeployPoller] 获取任务失败: {ex.Message}");
                return Array.Empty<PendingTaskInfo>();
            }
        }

        private async Task ProcessTaskAsync(PendingTaskInfo task, CancellationToken ct)
        {
            OnDeployStarted?.Invoke(this, task.TaskType == "uninstall" ? "正在卸载配套工具软件" : "正在更新配套工具软件，请耐心等待");

            try
            {
                if (task.TaskType == "uninstall")
                {
                    await ProcessUninstallTaskAsync(task, ct);
                }
                else
                {
                    await ProcessDeployTaskAsync(task, ct);
                }
            }
            finally
            {
                OnDeployCompleted?.Invoke(this, task.TaskId);
            }
        }

        private async Task ProcessDeployTaskAsync(PendingTaskInfo task, CancellationToken ct)
        {
            string? extractDir = null;
            string? zipPath = null;
            string? sigPath = null;

            try
            {
                // 下载
                var dlResult = await _downloader.DownloadAsync(_serverUrl, _machineId, task, ct);
                if (!dlResult.Success)
                {
                    await _reporter.ReportStatusAsync(task.TaskId, false, dlResult.ErrorMessage);
                    return;
                }
                zipPath = dlResult.ZipPath;
                sigPath = dlResult.SigPath;

                // 验证
                var verifyResult = DeployVerifier.VerifyAndExtract(zipPath, sigPath, _trustedKeysPath);
                if (!verifyResult.Success)
                {
                    await _reporter.ReportStatusAsync(task.TaskId, false, verifyResult.ErrorMessage);
                    return;
                }
                extractDir = verifyResult.ExtractPath;
                var manifest = verifyResult.Manifest;

                // 执行
                DeployExecutionResult execResult;
                if (manifest.IsSoftwareDeploy)
                {
                    execResult = DeployExecutor.ExecuteSoftwareDeploy(extractDir, manifest);
                }
                else
                {
                    execResult = DeployExecutor.ExecuteFileDeploy(extractDir, manifest);
                }

                if (!execResult.Success && manifest.IsSoftwareDeploy)
                {
                    // 回滚
                    var rollbackResult = DeployExecutor.RollbackSoftwareDeploy(extractDir, manifest);
                    if (!rollbackResult.Success)
                    {
                        execResult.ErrorMessage += $"; 回滚也失败: {rollbackResult.ErrorMessage}";
                    }
                }

                // 记录本地历史
                var record = new DeployRecord
                {
                    recordId = $"rec-{Guid.NewGuid():N}",
                    packageId = task.PackageId,
                    name = manifest.name,
                    version = manifest.version,
                    type = manifest.type,
                    deployedAt = DateTime.UtcNow.ToString("O"),
                    status = execResult.Success ? "success" : "failed",
                    targetPath = manifest.targetPath,
                };
                _historyStore.AddRecord(record);

                // file-deploy 生成文件清单
                if (execResult.Success && manifest.IsFileDeploy)
                {
                    _historyStore.GenerateFileManifest(extractDir, manifest.targetPath);
                }

                // 上报
                await _reporter.ReportStatusAsync(task.TaskId, execResult.Success, execResult.ErrorMessage);
                await _reporter.SyncRecordsAsync(_historyStore.GetRecordsForSync());
            }
            catch (Exception ex)
            {
                await _reporter.ReportStatusAsync(task.TaskId, false, $"部署异常: {ex.Message}");
            }
            finally
            {
                if (extractDir != null)
                    DeployVerifier.Cleanup(extractDir);
                if (zipPath != null)
                {
                    var dlDir = Path.GetDirectoryName(zipPath);
                    if (dlDir != null)
                        DeployDownloader.Cleanup(dlDir);
                }
            }
        }

        private async Task ProcessUninstallTaskAsync(PendingTaskInfo task, CancellationToken ct)
        {
            try
            {
                // 查找本地记录
                var record = _historyStore.GetAllRecords()
                    .FirstOrDefault(r => r.packageId == task.PackageId && r.status != "uninstalled");

                if (record == null)
                {
                    await _reporter.ReportStatusAsync(task.TaskId, false, "未找到对应的部署记录");
                    return;
                }

                UninstallResult result;
                if (record.type == "software-deploy")
                {
                    // software-deploy: 尝试从缓存的 ZIP 中解压 uninstall.ps1 执行
                    // 简化处理：如果本地没有缓存 ZIP，直接标记为已卸载
                    result = new UninstallResult
                    {
                        Success = true,
                        ErrorMessage = "software-deploy 卸载需要本地 ZIP 缓存",
                    };
                }
                else
                {
                    result = DeployUninstaller.UninstallFiles("", record);
                }

                _historyStore.UpdateRecordStatus(record.recordId, "uninstalled");
                await _reporter.ReportStatusAsync(task.TaskId, result.Success, result.ErrorMessage);
                await _reporter.SyncRecordsAsync(_historyStore.GetRecordsForSync());
            }
            catch (Exception ex)
            {
                await _reporter.ReportStatusAsync(task.TaskId, false, $"卸载异常: {ex.Message}");
            }
        }

        public void Dispose()
        {
            Stop();
            _cts.Dispose();
            _httpClient.Dispose();
            _downloader.Dispose();
        }

        private class PendingTasksResponse
        {
            public bool success { get; set; }
            public PendingTaskInfo[] tasks { get; set; } = Array.Empty<PendingTaskInfo>();
        }
    }
}
