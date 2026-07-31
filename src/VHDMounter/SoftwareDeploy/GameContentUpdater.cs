#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace VHDMounter.SoftwareDeploy
{
    public class GameContentUpdater : IDisposable
    {
        private readonly string _serverUrl;
        private readonly string _machineId;
        private readonly string _trustedKeysPath;
        private readonly string _baseDir;
        private readonly TimeSpan _timeout;
        private readonly HttpClient _httpClient;
        private readonly DeployDownloader _downloader;
        private readonly DeployHistoryStore _historyStore;
        private readonly string _appVersion;
        private readonly string _keyId;
        private const string UA_PREFIX = "VHDMount/";
        private const int DEFAULT_TIMEOUT_MINUTES = 10;
        private bool _disposed;

        public GameContentUpdater(string serverUrl, string machineId, string trustedKeysPath, string baseDir, int timeoutMinutes = DEFAULT_TIMEOUT_MINUTES)
        {
            _serverUrl = serverUrl.TrimEnd('/');
            _machineId = machineId;
            _trustedKeysPath = trustedKeysPath;
            _baseDir = baseDir;
            _timeout = TimeSpan.FromMinutes(timeoutMinutes <= 0 ? DEFAULT_TIMEOUT_MINUTES : timeoutMinutes);
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            _downloader = new DeployDownloader();
            _historyStore = new DeployHistoryStore(baseDir);
            _keyId = DeployRequestSigner.BuildDefaultKeyId(machineId);

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            _appVersion = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
        }

        public async Task CheckAndApplyAsync(string currentPackagePath, CancellationToken ct)
        {
            if (!MachineKeyRegistration.IsRegisteredAndApproved)
            {
                Trace.WriteLine("[GameContentUpdater] 机台密钥未注册或审批，跳过游戏内容更新");
                return;
            }

            if (string.IsNullOrWhiteSpace(currentPackagePath))
            {
                Trace.WriteLine("[GameContentUpdater] 当前游戏路径为空，跳过");
                return;
            }

            if (!ValidateOptionTargetPath(currentPackagePath, out var optionPath))
            {
                Trace.WriteLine($"[GameContentUpdater] 目标 option 路径不合法: {currentPackagePath}");
                return;
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_timeout);

            try
            {
                var tasks = await FetchPendingTasksAsync(timeoutCts.Token);
                if (tasks == null || tasks.Length == 0)
                    return;

                foreach (var task in tasks)
                {
                    if (timeoutCts.Token.IsCancellationRequested)
                        break;
                    await ProcessTaskAsync(optionPath, task, timeoutCts.Token);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // 应用生命周期取消，静默退出
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[GameContentUpdater] 更新检查异常: {ex.Message}");
            }
        }

        private async Task<PendingTaskInfo[]> FetchPendingTasksAsync(CancellationToken ct)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get,
                    $"{_serverUrl}/api/machines/{_machineId}/game-content/pending");
                request.Headers.Add("User-Agent", $"{UA_PREFIX}{_appVersion}");
                DeployRequestSigner.Sign(request, _machineId, _keyId);

                var response = await _httpClient.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    var status = (int)response.StatusCode;
                    if (status == 400)
                        Trace.WriteLine("[GameContentUpdater] 获取游戏内容任务失败: 机台公钥未注册");
                    else if (status == 403)
                        Trace.WriteLine("[GameContentUpdater] 获取游戏内容任务失败: 机台密钥未审批或已吊销");
                    else
                        Trace.WriteLine($"[GameContentUpdater] 获取游戏内容任务失败: HTTP {status}");
                    return Array.Empty<PendingTaskInfo>();
                }

                var result = await response.Content.ReadFromJsonAsync<PendingTasksResponse>(ct);
                var tasks = result?.tasks ?? Array.Empty<PendingTaskInfo>();

                using var rsa = VHDManager.EnsureOrCreateTpmRsa(_machineId);
                foreach (var task in tasks)
                {
                    DecryptTaskKey(task, task.KeyCipher, task.Iv, (k, i) => { task.AesKey = k; task.IvBytes = i; }, rsa);
                    DecryptTaskKey(task, task.SignatureKeyCipher, task.SignatureIv,
                        (k, i) => { task.SignatureAesKey = k; task.SignatureIvBytes = i; }, rsa);
                }

                return tasks;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[GameContentUpdater] 获取任务失败: {ex.Message}");
                return Array.Empty<PendingTaskInfo>();
            }
        }

        private static void DecryptTaskKey(PendingTaskInfo task, string keyCipher, string iv, Action<byte[], byte[]> setter, RSA rsa)
        {
            if (string.IsNullOrWhiteSpace(keyCipher) || string.IsNullOrWhiteSpace(iv))
                return;

            try
            {
                var keyCipherBytes = Convert.FromBase64String(keyCipher);
                var aesKeyBase64 = rsa.Decrypt(keyCipherBytes, RSAEncryptionPadding.OaepSHA1);
                var aesKey = Convert.FromBase64String(System.Text.Encoding.UTF8.GetString(aesKeyBase64));
                var ivBytes = Convert.FromBase64String(iv);
                setter(aesKey, ivBytes);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[GameContentUpdater] 解密任务 AES 密钥失败 ({task.TaskId}): {ex.Message}");
            }
        }

        private async Task ProcessTaskAsync(string optionPath, PendingTaskInfo task, CancellationToken ct)
        {
            using var reporter = new DeployReporter(_serverUrl, _machineId);
            string? extractDir = null;
            string? zipPath = null;

            try
            {
                if (task.AesKey == null || task.IvBytes == null || task.SignatureAesKey == null || task.SignatureIvBytes == null)
                {
                    await reporter.ReportStatusAsync(task.TaskId, false, "缺少 AES 解密密钥", ct);
                    return;
                }

                await reporter.ReportTaskStateAsync(task.TaskId, "downloading", cancellationToken: ct);

                var dlResult = await _downloader.DownloadAsync(_serverUrl, _machineId, task, ct);
                if (!dlResult.Success)
                {
                    await reporter.ReportStatusAsync(task.TaskId, false, dlResult.ErrorMessage, ct);
                    return;
                }

                zipPath = dlResult.ZipPath;

                var verifyResult = DeployVerifier.VerifyAndExtract(dlResult.ZipPath, dlResult.SigPath, _trustedKeysPath);
                if (!verifyResult.Success)
                {
                    await reporter.ReportStatusAsync(task.TaskId, false, verifyResult.ErrorMessage, ct);
                    return;
                }

                extractDir = verifyResult.ExtractPath;
                var manifest = verifyResult.Manifest;
                if (!manifest.IsGameOptionDeploy)
                {
                    await reporter.ReportStatusAsync(task.TaskId, false, $"非游戏内容更新包类型: {manifest.type}", ct);
                    return;
                }

                await reporter.ReportTaskStateAsync(task.TaskId, "running", cancellationToken: ct);

                var applyResult = ApplyToOptionDirectory(extractDir, optionPath, task.TaskId);
                if (!applyResult.Success)
                {
                    await reporter.ReportStatusAsync(task.TaskId, false, applyResult.ErrorMessage, ct);
                    return;
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
                    status = "success",
                    targetPath = optionPath,
                    uninstallScript = string.Empty,
                    requiresAdmin = false,
                };

                try
                {
                    _historyStore.AddRecord(record);
                    _historyStore.GenerateFileManifest(extractDir, optionPath);
                    await reporter.SyncRecordsAsync(_historyStore.GetRecordsForSync(), ct);
                }
                catch (Exception historyEx)
                {
                    Trace.WriteLine($"[GameContentUpdater] 历史记录写入失败: {historyEx.Message}");
                }

                await reporter.ReportStatusAsync(task.TaskId, true, string.Empty, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                try { await reporter.ReportStatusAsync(task.TaskId, false, "更新超时或已取消", ct); } catch { }
            }
            catch (Exception ex)
            {
                try { await reporter.ReportStatusAsync(task.TaskId, false, $"更新异常: {ex.Message}", ct); } catch { }
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

        internal static DeployExecutionResult ApplyToOptionDirectory(string extractDir, string optionPath, string taskId)
        {
            var result = new DeployExecutionResult();

            string sourceDir = ResolveContentSourceDirectory(extractDir);
            if (sourceDir == null)
            {
                result.ErrorMessage = "未找到 option 内容源目录";
                return result;
            }

            string backupPath = optionPath + $".bak-{taskId}";

            try
            {
                // 清理旧备份
                if (Directory.Exists(backupPath))
                    SafeDeleteDirectory(backupPath);

                // 备份现有 option
                if (Directory.Exists(optionPath))
                {
                    var optionInfo = new DirectoryInfo(optionPath);
                    if ((optionInfo.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                    {
                        result.ErrorMessage = "option 目录为重解析点，拒绝操作";
                        return result;
                    }
                    Directory.Move(optionPath, backupPath);
                }

                // 写入新 option
                Directory.CreateDirectory(optionPath);
                CopyDirectory(sourceDir, optionPath);

                // 写入成功后清理本次备份，保留最近一份成功备份
                SafeDeleteDirectory(backupPath);

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"同步 option 目录失败: {ex.Message}";
                RestoreOptionBackup(optionPath, backupPath);
            }

            return result;
        }

        internal static string ResolveContentSourceDirectory(string extractDir)
        {
            string payloadDir = Path.Combine(extractDir, "payload");
            if (Directory.Exists(payloadDir))
                return payloadDir;

            // 如果没有 payload 目录，则使用包根目录
            return extractDir;
        }

        internal static void RestoreOptionBackup(string optionPath, string backupPath)
        {
            try
            {
                if (!Directory.Exists(backupPath))
                    return;

                if (Directory.Exists(optionPath))
                    SafeDeleteDirectory(optionPath);

                Directory.Move(backupPath, optionPath);
            }
            catch (Exception restoreEx)
            {
                Trace.WriteLine($"[GameContentUpdater] 回滚 option 备份失败: {restoreEx.Message}");
            }
        }

        private static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }
            foreach (var subDir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
                CopyDirectory(subDir, destSubDir);
            }
        }

        private static void SafeDeleteDirectory(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                return;
            try
            {
                Directory.Delete(path, true);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[GameContentUpdater] 删除目录失败 {path}: {ex.Message}");
            }
        }

        internal static bool ValidateOptionTargetPath(string currentPackagePath, out string optionPath)
        {
            optionPath = string.Empty;
            try
            {
                if (!Path.IsPathRooted(currentPackagePath))
                    return false;

                // 拒绝路径中的 .. 穿越
                if (currentPackagePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Contains("..", StringComparer.Ordinal))
                    return false;

                string fullCurrent = Path.GetFullPath(currentPackagePath);
                if (!fullCurrent.StartsWith(@"M:\", StringComparison.OrdinalIgnoreCase))
                    return false;

                optionPath = Path.GetFullPath(Path.Combine(fullCurrent, "option"));
                string currentWithSep = fullCurrent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!optionPath.StartsWith(currentWithSep, StringComparison.OrdinalIgnoreCase))
                    return false;

                if (!string.Equals(Path.GetFileName(optionPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), "option", StringComparison.OrdinalIgnoreCase))
                    return false;

                if (Directory.Exists(optionPath))
                {
                    var info = new DirectoryInfo(optionPath);
                    if ((info.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                        return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private class PendingTasksResponse
        {
            public PendingTaskInfo[] tasks { get; set; } = Array.Empty<PendingTaskInfo>();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _httpClient.Dispose();
            _downloader.Dispose();
        }
    }
}
