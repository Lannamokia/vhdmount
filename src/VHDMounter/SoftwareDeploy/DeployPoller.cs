#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
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
        private readonly string _keyId;
        private bool _disposed;
        private readonly object _disposeLock = new object();
        private const string UA_PREFIX = "VHDMount/";
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
            _keyId = DeployRequestSigner.BuildDefaultKeyId(machineId);

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            _appVersion = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
        }

        public void Start()
        {
            lock (_disposeLock)
            {
                if (_disposed) return;
            }
            if (_pollTask != null) return;

            // 检查机台密钥注册状态
            if (!MachineKeyRegistration.IsRegisteredAndApproved)
            {
                Trace.WriteLine("[DeployPoller] 机台密钥未注册或审批，跳过部署轮询启动");
                return;
            }

            _pollTask = Task.Run(PollLoopAsync);
        }

        public void Stop()
        {
            try
            {
                if (!_cts.IsCancellationRequested) _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // 已 Dispose，忽略
            }
            try
            {
                _pollTask?.Wait(TimeSpan.FromSeconds(10));
            }
            catch (AggregateException)
            {
                // 忽略 task 内部抛出的异常，等待是为了让其优雅退出
            }
        }

        private async Task PollLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    if (!MachineKeyRegistration.IsRegisteredAndApproved)
                    {
                        Trace.WriteLine("[DeployPoller] 机台密钥未注册，跳过本轮轮询");
                    }
                    else
                    {
                        await PollOnceAsync(_cts.Token);
                    }
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
                DeployRequestSigner.Sign(request, _machineId, _keyId);

                var response = await _httpClient.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    var status = (int)response.StatusCode;
                    if (status == 400)
                    {
                        Trace.WriteLine("[DeployPoller] 获取部署任务失败: 机台公钥未注册");
                    }
                    else if (status == 403)
                    {
                        Trace.WriteLine("[DeployPoller] 获取部署任务失败: 机台密钥未审批或已吊销");
                    }
                    return Array.Empty<PendingTaskInfo>();
                }

                var result = await response.Content.ReadFromJsonAsync<PendingTasksResponse>(ct);
                var tasks = result?.tasks ?? Array.Empty<PendingTaskInfo>();

                // 解密每个任务的 AES 密钥（如果服务端返回了 keyCipher）
                using var rsa = VHDManager.EnsureOrCreateTpmRsa(_machineId);
                foreach (var task in tasks)
                {
                    if (!string.IsNullOrWhiteSpace(task.KeyCipher))
                    {
                        try
                        {
                            var keyCipherBytes = Convert.FromBase64String(task.KeyCipher);
                            var aesKeyBase64 = rsa.Decrypt(keyCipherBytes, RSAEncryptionPadding.OaepSHA1);
                            task.AesKey = Convert.FromBase64String(System.Text.Encoding.UTF8.GetString(aesKeyBase64));
                            task.IvBytes = Convert.FromBase64String(task.Iv);
                        }
                        catch (Exception ex)
                        {
                            Trace.WriteLine($"[DeployPoller] 解密任务 ZIP AES 密钥失败 ({task.TaskId}): {ex.Message}");
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(task.SignatureKeyCipher))
                    {
                        try
                        {
                            var keyCipherBytes = Convert.FromBase64String(task.SignatureKeyCipher);
                            var aesKeyBase64 = rsa.Decrypt(keyCipherBytes, RSAEncryptionPadding.OaepSHA1);
                            task.SignatureAesKey = Convert.FromBase64String(Encoding.UTF8.GetString(aesKeyBase64));
                            task.SignatureIvBytes = Convert.FromBase64String(task.SignatureIv);
                        }
                        catch (Exception ex)
                        {
                            Trace.WriteLine($"[DeployPoller] 解密任务签名 AES 密钥失败 ({task.TaskId}): {ex.Message}");
                        }
                    }
                }

                return tasks;
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
                if (task.AesKey == null || task.IvBytes == null || task.SignatureAesKey == null || task.SignatureIvBytes == null)
                {
                    await _reporter.ReportStatusAsync(task.TaskId, false, "缺少 AES 解密密钥");
                    return;
                }

                await _reporter.ReportTaskStateAsync(task.TaskId, "downloading");

                // 下载（使用任务内分别解密 ZIP 与签名的参数）
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

                await _reporter.ReportTaskStateAsync(task.TaskId, "running");

                // 执行
                DeployExecutionResult execResult;
                if (manifest.IsSoftwareDeploy)
                {
                    execResult = DeployExecutor.ExecuteSoftwareDeploy(extractDir, task.PackageId, manifest);
                }
                else
                {
                    execResult = DeployExecutor.ExecuteFileDeploy(extractDir, manifest);
                }

                if (!execResult.Success && manifest.IsSoftwareDeploy && !string.IsNullOrWhiteSpace(execResult.DeploymentPath))
                {
                    // 回滚
                    var rollbackResult = DeployExecutor.RollbackSoftwareDeploy(execResult.DeploymentPath, manifest);
                    if (!rollbackResult.Success)
                    {
                        execResult.ErrorMessage += $"; 回滚也失败: {rollbackResult.ErrorMessage}";
                    }
                    else
                    {
                        DeployExecutor.CleanupSoftwareDeployDirectory(execResult.DeploymentPath);
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
                    targetPath = manifest.IsSoftwareDeploy ? execResult.DeploymentPath : manifest.targetPath,
                    uninstallScript = manifest.uninstallScript,
                    requiresAdmin = manifest.requiresAdmin,
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
                    await _reporter.ReportTaskStateAsync(task.TaskId, "running");
                    result = DeployUninstaller.UninstallSoftware(record);
                }
                else
                {
                    await _reporter.ReportTaskStateAsync(task.TaskId, "running");
                    result = DeployUninstaller.UninstallFiles("", record);
                }

                if (result.Success)
                {
                    _historyStore.UpdateRecordStatus(record.recordId, "uninstalled");
                }
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
            lock (_disposeLock)
            {
                if (_disposed) return;
                _disposed = true;
            }

            // 先停止轮询任务并等待其退出，再销毁 _cts，避免任务运行时访问已 disposed 的 token
            try { Stop(); } catch { }
            try { _cts.Dispose(); } catch { }
            try { _httpClient.Dispose(); } catch { }
            try { _downloader.Dispose(); } catch { }
        }

        private class PendingTasksResponse
        {
            public bool success { get; set; }
            public PendingTaskInfo[] tasks { get; set; } = Array.Empty<PendingTaskInfo>();
        }
    }
}
