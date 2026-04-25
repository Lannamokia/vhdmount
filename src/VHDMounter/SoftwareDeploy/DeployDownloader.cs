using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace VHDMounter.SoftwareDeploy
{
    public class DownloadResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public string ZipPath { get; set; } = string.Empty;
        public string SigPath { get; set; } = string.Empty;
    }

    public class DeployDownloader : IDisposable
    {
        private bool _disposed;
        private readonly HttpClient _httpClient;
        private readonly string _appVersion;
        private const string UA_PREFIX = "VHDMount:";
        private const int RETRY_INTERVAL_MS = 5000;
        private const int CONNECT_TIMEOUT_MS = 10000;
        private const int READ_TIMEOUT_MS = 30000;

        public DeployDownloader()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMilliseconds(READ_TIMEOUT_MS);

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            _appVersion = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
        }

        public async Task<DownloadResult> DownloadAsync(string serverUrl, string machineId, PendingTaskInfo task, CancellationToken ct)
        {
            var result = new DownloadResult();
            string tempDir = Path.Combine(Path.GetTempPath(), $"vhd-deploy-dl-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            string zipPath = Path.Combine(tempDir, "package.zip");
            string sigPath = Path.Combine(tempDir, "package.zip.sig");

            try
            {
                // 下载 ZIP
                bool zipOk = await DownloadWithRetryAsync(task.DownloadUrl, zipPath, machineId, ct);
                if (!zipOk)
                {
                    result.ErrorMessage = "ZIP 包下载失败";
                    Cleanup(tempDir);
                    return result;
                }

                // 下载签名
                bool sigOk = await DownloadWithRetryAsync(task.SignatureUrl, sigPath, machineId, ct);
                if (!sigOk)
                {
                    result.ErrorMessage = "签名文件下载失败";
                    Cleanup(tempDir);
                    return result;
                }

                result.Success = true;
                result.ZipPath = zipPath;
                result.SigPath = sigPath;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"下载异常: {ex.Message}";
                Cleanup(tempDir);
            }

            return result;
        }

        private async Task<bool> DownloadWithRetryAsync(string url, string destPath, string machineId, CancellationToken ct)
        {
            string tmpPath = destPath + ".tmp";
            long existingLength = File.Exists(tmpPath) ? new FileInfo(tmpPath).Length : 0;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Add("User-Agent", $"{UA_PREFIX}{_appVersion}");
                    request.Headers.Add("X-Machine-Id", machineId);

                    // 断点续传
                    if (existingLength > 0)
                    {
                        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingLength, null);
                    }

                    using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

                    if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        // 令牌过期，需要上层重新获取任务
                        return false;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        await Task.Delay(RETRY_INTERVAL_MS, ct);
                        continue;
                    }

                    var mode = existingLength > 0 && response.StatusCode == HttpStatusCode.PartialContent
                        ? FileMode.Append
                        : FileMode.Create;

                    using var fs = new FileStream(tmpPath, mode, FileAccess.Write, FileShare.None);
                    await using var stream = await response.Content.ReadAsStreamAsync(ct);
                    await stream.CopyToAsync(fs, ct);
                    await fs.FlushAsync(ct);

                    File.Move(tmpPath, destPath, true);
                    return true;
                }
                catch (TaskCanceledException)
                {
                    return false;
                }
                catch (HttpRequestException)
                {
                    // 网络问题，等待重试
                }
                catch (Exception)
                {
                    // 其他异常，清理并重试
                    try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
                    existingLength = 0;
                }

                try
                {
                    await Task.Delay(RETRY_INTERVAL_MS, ct);
                }
                catch (TaskCanceledException)
                {
                    return false;
                }
            }

            return false;
        }

        public static void Cleanup(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return;
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _httpClient.Dispose();
        }
    }

    public class PendingTaskInfo
    {
        public string TaskId { get; set; } = string.Empty;
        public string PackageId { get; set; } = string.Empty;
        public string TaskType { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string SignatureUrl { get; set; } = string.Empty;
    }
}
