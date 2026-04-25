using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VHDMounter.SoftwareDeploy
{
    public class DeployReporter
    {
        private readonly HttpClient _httpClient;
        private readonly string _serverUrl;
        private readonly string _machineId;
        private readonly string _appVersion;
        private const string UA_PREFIX = "VHDMount:";

        public DeployReporter(string serverUrl, string machineId)
        {
            _serverUrl = serverUrl.TrimEnd('/');
            _machineId = machineId;
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            _appVersion = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
        }

        public async Task ReportStatusAsync(string taskId, bool success, string errorMessage)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post,
                    $"{_serverUrl}/api/machines/{_machineId}/deployments/{taskId}/status");
                request.Headers.Add("User-Agent", $"{UA_PREFIX}{_appVersion}");
                request.Content = JsonContent.Create(new
                {
                    status = success ? "success" : "failed",
                    errorMessage = errorMessage ?? "",
                });

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[DeployReporter] 上报状态失败: {ex.Message}");
            }
        }

        public async Task SyncRecordsAsync(List<DeployRecord> records)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post,
                    $"{_serverUrl}/api/machines/{_machineId}/deployments/sync");
                request.Headers.Add("User-Agent", $"{UA_PREFIX}{_appVersion}");
                request.Content = JsonContent.Create(new { records });

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[DeployReporter] 同步记录失败: {ex.Message}");
            }
        }
    }
}
