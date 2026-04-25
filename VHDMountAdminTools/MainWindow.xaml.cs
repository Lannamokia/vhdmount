using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VHDMountAdminTools
{
    public partial class MainWindow : Window
    {
        private const long MaxAppUpdatePayloadBytes = 1L << 30;
        private HttpClient? deployHttpClient;
        private CookieContainer deployCookieContainer = new();
        private bool deployLoggedIn = false;

        public MainWindow()
        {
            InitializeComponent();
            VersionBox.Text = DateTime.UtcNow.ToString("yyyy.MM.dd.HHmmss");
            RegistrationPasswordBox.Password = "ChangeThisPfxPassword";
        }

        private void CloseApp(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BrowseFolderKeyOut(object sender, RoutedEventArgs e)
        {
            if (TrySelectFolder(out var selectedPath))
            {
                KeyOutDir.Text = selectedPath;
            }
        }

        private void BrowseFolderPayload(object sender, RoutedEventArgs e)
        {
            if (TrySelectFolder(out var selectedPath))
            {
                PayloadDir.Text = selectedPath;
            }
        }

        private void BrowseFolderManifestOut(object sender, RoutedEventArgs e)
        {
            if (TrySelectFolder(out var selectedPath))
            {
                ManifestOutDir.Text = selectedPath;
            }
        }

        private void BrowseFolderRegistrationOut(object sender, RoutedEventArgs e)
        {
            if (TrySelectFolder(out var selectedPath))
            {
                RegistrationOutDir.Text = selectedPath;
            }
        }

        private void BrowsePrivateKey(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "PEM Files|*.pem|All Files|*.*",
            };

            if (dialog.ShowDialog() == true)
            {
                PrivateKeyPath.Text = dialog.FileName;
            }
        }

        private void GenerateSigningKey(object sender, RoutedEventArgs e)
        {
            try
            {
                var outputDirectory = string.IsNullOrWhiteSpace(KeyOutDir.Text) ? "." : KeyOutDir.Text;
                var keyId = string.IsNullOrWhiteSpace(KeyIdBox.Text)
                    ? $"update-key-{DateTime.UtcNow:yyyyMMdd}"
                    : KeyIdBox.Text.Trim();

                Directory.CreateDirectory(outputDirectory);

                var privateKeyPath = Path.Combine(outputDirectory, $"private_key_{keyId}.pem");
                var publicKeyPath = Path.Combine(outputDirectory, $"public_key_{keyId}.pem");
                var trustPath = Path.Combine(outputDirectory, "trusted_keys.pem");

                using var rsa = RSA.Create(3072);
                WritePem(privateKeyPath, "PRIVATE KEY", rsa.ExportPkcs8PrivateKey());
                WritePem(publicKeyPath, "PUBLIC KEY", rsa.ExportSubjectPublicKeyInfo());
                AppendPem(trustPath, "PUBLIC KEY", rsa.ExportSubjectPublicKeyInfo());

                StatusText.Text = "更新签名密钥生成完成";
            }
            catch (Exception ex)
            {
                StatusText.Text = ex.Message;
            }
        }

        private void MakeManifest(object sender, RoutedEventArgs e)
        {
            try
            {
                var payloadDirectory = PayloadDir.Text;
                var outputDirectory = string.IsNullOrWhiteSpace(ManifestOutDir.Text) ? "." : ManifestOutDir.Text;
                var type = ((System.Windows.Controls.ComboBoxItem)TypeBox.SelectedItem).Content?.ToString() ?? "app-update";
                var minVersion = string.IsNullOrWhiteSpace(MinVersionBox.Text) ? "1.7.0" : MinVersionBox.Text.Trim();
                var version = string.IsNullOrWhiteSpace(VersionBox.Text) ? DateTime.UtcNow.ToString("yyyy.MM.dd.HHmmss") : VersionBox.Text.Trim();

                if (string.IsNullOrWhiteSpace(payloadDirectory) || !Directory.Exists(payloadDirectory))
                {
                    throw new InvalidOperationException("payload 目录不存在");
                }

                Directory.CreateDirectory(outputDirectory);
                var privateKeyBytes = ReadPem(PrivateKeyPath.Text, "PRIVATE KEY");
                var createdAt = DateTime.UtcNow;
                var files = Directory.GetFiles(payloadDirectory, "*", SearchOption.AllDirectories);
                var manifestFiles = new System.Collections.Generic.List<object>();
                long totalPayloadBytes = 0;

                foreach (var file in files)
                {
                    var relativePath = Path.GetRelativePath(payloadDirectory, file).Replace('\\', '/');
                    using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var sha = SHA256.Create();
                    var hash = sha.ComputeHash(stream);
                    checked
                    {
                        totalPayloadBytes += stream.Length;
                    }

                    manifestFiles.Add(new
                    {
                        path = Path.GetFileName(relativePath),
                        target = string.Equals(type, "app-update", StringComparison.OrdinalIgnoreCase)
                            ? relativePath
                            : Path.GetFileName(relativePath),
                        size = stream.Length,
                        sha256 = Convert.ToHexString(hash).ToLowerInvariant(),
                    });
                }

                if (string.Equals(type, "app-update", StringComparison.OrdinalIgnoreCase) && totalPayloadBytes > MaxAppUpdatePayloadBytes)
                {
                    throw new InvalidOperationException($"app-update 内容过大: {FormatBytes(totalPayloadBytes)}，上限为 {FormatBytes(MaxAppUpdatePayloadBytes)}。请改用 vhd-data 类型。 ");
                }

                var manifest = new
                {
                    version,
                    minVersion,
                    type,
                    signer = "admin-tools",
                    createdAt = createdAt.ToString("o"),
                    expiresAt = createdAt.AddDays(3).ToString("o"),
                    files = manifestFiles,
                };

                var manifestPath = Path.Combine(outputDirectory, "manifest.json");
                var signaturePath = Path.Combine(outputDirectory, "manifest.sig");
                var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(manifestPath, json, Encoding.UTF8);

                using var signingRsa = RSA.Create();
                signingRsa.ImportPkcs8PrivateKey(privateKeyBytes, out _);
                var signature = signingRsa.SignData(File.ReadAllBytes(manifestPath), HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
                File.WriteAllText(signaturePath, Convert.ToBase64String(signature), Encoding.UTF8);

                StatusText.Text = "清单与签名生成完成";
            }
            catch (Exception ex)
            {
                StatusText.Text = ex.Message;
            }
        }

        private void GenerateRegistrationBundle(object sender, RoutedEventArgs e)
        {
            try
            {
                var outputDirectory = string.IsNullOrWhiteSpace(RegistrationOutDir.Text) ? "." : RegistrationOutDir.Text;
                var bundleName = SanitizeFileName(string.IsNullOrWhiteSpace(RegistrationBundleNameBox.Text)
                    ? "machine-registration"
                    : RegistrationBundleNameBox.Text.Trim());
                var subjectCommonName = string.IsNullOrWhiteSpace(RegistrationSubjectBox.Text)
                    ? "VHDMount Machine Registration"
                    : RegistrationSubjectBox.Text.Trim();
                var password = RegistrationPasswordBox.Password ?? string.Empty;

                if (password.Length < 8)
                {
                    throw new InvalidOperationException("PFX 密码长度至少为 8 位");
                }

                if (!int.TryParse(RegistrationValidDaysBox.Text, out var validDays) || validDays < 1 || validDays > 3650)
                {
                    throw new InvalidOperationException("有效天数必须在 1 到 3650 之间");
                }

                Directory.CreateDirectory(outputDirectory);

                using var rsa = RSA.Create(3072);
                var request = new CertificateRequest(
                    new X500DistinguishedName($"CN={subjectCommonName}"),
                    rsa,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);
                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
                request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

                var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
                var notAfter = notBefore.AddDays(validDays);
                using var selfSigned = request.CreateSelfSigned(notBefore, notAfter);
                using var exportableCertificate = new X509Certificate2(
                    selfSigned.Export(X509ContentType.Pfx, password),
                    password,
                    X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);

                var certificatePem = ExportCertificatePem(exportableCertificate);
                var pfxPath = Path.Combine(outputDirectory, $"{bundleName}.pfx");
                var pemPath = Path.Combine(outputDirectory, $"{bundleName}.pem");
                var trustJsonPath = Path.Combine(outputDirectory, $"{bundleName}.trust.json");
                var clientConfigPath = Path.Combine(outputDirectory, $"{bundleName}.client-config.ini");

                File.WriteAllBytes(pfxPath, exportableCertificate.Export(X509ContentType.Pfx, password));
                File.WriteAllText(pemPath, certificatePem + Environment.NewLine, Encoding.ASCII);

                using var sha = SHA256.Create();
                var fingerprint256 = Convert.ToHexString(sha.ComputeHash(exportableCertificate.RawData));
                var trustDocument = new
                {
                    name = bundleName,
                    subject = exportableCertificate.Subject,
                    fingerprint256,
                    validFrom = exportableCertificate.NotBefore.ToUniversalTime().ToString("o"),
                    validTo = exportableCertificate.NotAfter.ToUniversalTime().ToString("o"),
                    certificatePem,
                };
                File.WriteAllText(
                    trustJsonPath,
                    JsonSerializer.Serialize(trustDocument, new JsonSerializerOptions { WriteIndented = true }),
                    Encoding.UTF8);

                var clientConfig = new StringBuilder();
                clientConfig.AppendLine("; Add the following lines to vhdmonter_config.ini");
                clientConfig.AppendLine($"RegistrationCertificatePath={Path.GetFileName(pfxPath)}");
                clientConfig.AppendLine($"RegistrationCertificatePassword={password}");
                File.WriteAllText(clientConfigPath, clientConfig.ToString(), Encoding.UTF8);

                StatusText.Text = "预配置注册证书包生成完成";
            }
            catch (Exception ex)
            {
                StatusText.Text = ex.Message;
            }
        }

        private bool TrySelectFolder(out string selectedPath)
        {
            using var dialog = new FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                selectedPath = dialog.SelectedPath;
                return true;
            }

            selectedPath = string.Empty;
            return false;
        }

        private string SanitizeFileName(string value)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                builder.Append(Array.IndexOf(invalidChars, ch) >= 0 ? '_' : ch);
            }
            return builder.ToString();
        }

        private string ExportCertificatePem(X509Certificate2 certificate)
        {
            return ToPem("CERTIFICATE", certificate.Export(X509ContentType.Cert));
        }

        private void WritePem(string path, string type, byte[] der)
        {
            File.WriteAllText(path, ToPem(type, der) + Environment.NewLine, Encoding.ASCII);
        }

        private void AppendPem(string path, string type, byte[] der)
        {
            File.AppendAllText(path, ToPem(type, der) + Environment.NewLine + Environment.NewLine, Encoding.ASCII);
        }

        private string ToPem(string type, byte[] der)
        {
            var base64 = Convert.ToBase64String(der);
            var builder = new StringBuilder();
            builder.AppendLine($"-----BEGIN {type}-----");
            for (int i = 0; i < base64.Length; i += 64)
            {
                builder.AppendLine(base64.Substring(i, Math.Min(64, base64.Length - i)));
            }
            builder.AppendLine($"-----END {type}-----");
            return builder.ToString().Trim();
        }

        private byte[] ReadPem(string path, string type)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                throw new FileNotFoundException("未找到指定的 PEM 文件", path);
            }

            var text = File.ReadAllText(path);
            var start = $"-----BEGIN {type}-----";
            var end = $"-----END {type}-----";
            var startIndex = text.IndexOf(start, StringComparison.Ordinal);
            var endIndex = text.IndexOf(end, StringComparison.Ordinal);
            if (startIndex < 0 || endIndex < 0)
            {
                throw new InvalidOperationException($"PEM 文件中未找到 {type}");
            }

            startIndex += start.Length;
            var base64 = text.Substring(startIndex, endIndex - startIndex).Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
            return Convert.FromBase64String(base64);
        }

        private string FormatBytes(long bytes)
        {
            return $"{bytes / 1024d / 1024d:F2} MB";
        }

        // ---------- 软件部署管理 ----------

        private void EnsureHttpClient()
        {
            if (deployHttpClient != null) return;
            var handler = new HttpClientHandler { CookieContainer = deployCookieContainer };
            deployHttpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        }

        private string DeployApiUrl(string path)
        {
            var baseUrl = DeployServerUrl.Text.Trim().TrimEnd('/');
            return $"{baseUrl}{path}";
        }

        private async void DeployConnect(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureHttpClient();
                var url = DeployApiUrl("/api/auth/login");
                var content = new StringContent(
                    JsonSerializer.Serialize(new { password = DeployAdminPassword.Password }),
                    Encoding.UTF8, "application/json");
                var response = await deployHttpClient!.PostAsync(url, content);
                if (response.IsSuccessStatusCode)
                {
                    deployLoggedIn = true;
                    DeployLoginStatus.Text = "已登录";
                    DeployLoginStatus.Foreground = System.Windows.Media.Brushes.Green;
                    StatusText.Text = "部署管理已连接";
                    await DeployLoadPackagesAsync();
                }
                else
                {
                    deployLoggedIn = false;
                    DeployLoginStatus.Text = "登录失败";
                    DeployLoginStatus.Foreground = System.Windows.Media.Brushes.Red;
                    StatusText.Text = $"登录失败: {response.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"连接失败: {ex.Message}";
            }
        }

        private async void DeployRefreshPackages(object sender, RoutedEventArgs e)
        {
            await DeployLoadPackagesAsync();
        }

        private async System.Threading.Tasks.Task DeployLoadPackagesAsync()
        {
            if (!deployLoggedIn || deployHttpClient == null) return;
            try
            {
                var response = await deployHttpClient.GetAsync(DeployApiUrl("/api/deployments/packages"));
                if (!response.IsSuccessStatusCode) return;
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<DeployListResponse>(json);
                DeployPackagesGrid.ItemsSource = result?.packages ?? new System.Collections.Generic.List<DeployPackageItem>();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"刷新包列表失败: {ex.Message}";
            }
        }

        private void DeployBrowseZip(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "ZIP Files|*.zip|All Files|*.*" };
            if (dialog.ShowDialog() == true) DeployZipPath.Text = dialog.FileName;
        }

        private void DeployBrowseSig(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "SIG Files|*.sig|All Files|*.*" };
            if (dialog.ShowDialog() == true) DeploySigPath.Text = dialog.FileName;
        }

        private async void DeployUploadPackage(object sender, RoutedEventArgs e)
        {
            if (!deployLoggedIn || deployHttpClient == null)
            {
                StatusText.Text = "请先连接并登录服务端";
                return;
            }

            try
            {
                var zipPath = DeployZipPath.Text;
                var sigPath = DeploySigPath.Text;
                if (!File.Exists(zipPath) || !File.Exists(sigPath))
                {
                    StatusText.Text = "请选择 ZIP 包和签名文件";
                    return;
                }

                var name = DeployPackageName.Text.Trim();
                var version = DeployPackageVersion.Text.Trim();
                var type = ((ComboBoxItem)DeployPackageType.SelectedItem).Content?.ToString() ?? "software-deploy";
                var signer = DeployPackageSigner.Text.Trim();

                var content = new MultipartFormDataContent();
                content.Add(new StreamContent(File.OpenRead(zipPath)), "package", Path.GetFileName(zipPath));
                content.Add(new StreamContent(File.OpenRead(sigPath)), "signature", Path.GetFileName(sigPath));
                content.Add(new StringContent(name), "name");
                content.Add(new StringContent(version), "version");
                content.Add(new StringContent(type), "type");
                content.Add(new StringContent(signer), "signer");

                var response = await deployHttpClient.PostAsync(DeployApiUrl("/api/deployments/packages"), content);
                if (response.IsSuccessStatusCode)
                {
                    StatusText.Text = "部署包上传成功";
                    DeployZipPath.Text = "";
                    DeploySigPath.Text = "";
                    await DeployLoadPackagesAsync();
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    StatusText.Text = $"上传失败: {response.StatusCode} {error}";
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"上传异常: {ex.Message}";
            }
        }

        private async void DeployCreateTask(object sender, RoutedEventArgs e)
        {
            if (!deployLoggedIn || deployHttpClient == null)
            {
                StatusText.Text = "请先连接并登录服务端";
                return;
            }

            try
            {
                var machineIds = DeployTargetMachines.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (machineIds.Length == 0)
                {
                    StatusText.Text = "请输入目标机台ID";
                    return;
                }

                // 获取选中的包
                var selected = DeployPackagesGrid.SelectedItem as DeployPackageItem;
                if (selected == null)
                {
                    StatusText.Text = "请先从列表中选择一个部署包";
                    return;
                }

                var body = new
                {
                    packageId = selected.packageId,
                    targetMachineIds = machineIds,
                };
                var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                var response = await deployHttpClient.PostAsync(DeployApiUrl("/api/deployments/tasks"), content);
                if (response.IsSuccessStatusCode)
                {
                    var result = JsonSerializer.Deserialize<DeployTaskResponse>(await response.Content.ReadAsStringAsync());
                    StatusText.Text = $"任务创建成功，共 {result?.count ?? 0} 个任务";
                }
                else
                {
                    StatusText.Text = $"创建任务失败: {response.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"创建任务异常: {ex.Message}";
            }
        }

        private async void DeployQueryHistory(object sender, RoutedEventArgs e)
        {
            if (!deployLoggedIn || deployHttpClient == null)
            {
                StatusText.Text = "请先连接并登录服务端";
                return;
            }

            try
            {
                var machineId = DeployQueryMachine.Text.Trim();
                if (string.IsNullOrWhiteSpace(machineId))
                {
                    StatusText.Text = "请输入机台ID";
                    return;
                }

                var response = await deployHttpClient.GetAsync(DeployApiUrl($"/api/machines/{machineId}/deployments/history"));
                if (response.IsSuccessStatusCode)
                {
                    var result = JsonSerializer.Deserialize<DeployHistoryResponse>(await response.Content.ReadAsStringAsync());
                    DeployHistoryGrid.ItemsSource = result?.records ?? new System.Collections.Generic.List<DeployRecordItem>();
                    StatusText.Text = $"查询到 {result?.count ?? 0} 条记录";
                }
                else
                {
                    StatusText.Text = $"查询失败: {response.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"查询异常: {ex.Message}";
            }
        }

        private async void DeployUninstall_Click(object sender, RoutedEventArgs e)
        {
            if (!deployLoggedIn || deployHttpClient == null) return;

            try
            {
                var button = (System.Windows.Controls.Button)sender;
                var recordId = button.Tag?.ToString();
                if (string.IsNullOrWhiteSpace(recordId)) return;

                var machineId = DeployQueryMachine.Text.Trim();
                if (string.IsNullOrWhiteSpace(machineId)) return;

                var response = await deployHttpClient.PostAsync(
                    DeployApiUrl($"/api/machines/{machineId}/deployments/{recordId}/uninstall"), null);
                if (response.IsSuccessStatusCode)
                {
                    StatusText.Text = "卸载任务已创建";
                    await DeployQueryHistoryAsync();
                }
                else
                {
                    StatusText.Text = $"卸载失败: {response.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"卸载异常: {ex.Message}";
            }
        }

        private async System.Threading.Tasks.Task DeployQueryHistoryAsync()
        {
            try
            {
                var machineId = DeployQueryMachine.Text.Trim();
                if (string.IsNullOrWhiteSpace(machineId) || deployHttpClient == null) return;
                var response = await deployHttpClient.GetAsync(DeployApiUrl($"/api/machines/{machineId}/deployments/history"));
                if (response.IsSuccessStatusCode)
                {
                    var result = JsonSerializer.Deserialize<DeployHistoryResponse>(await response.Content.ReadAsStringAsync());
                    DeployHistoryGrid.ItemsSource = result?.records ?? new System.Collections.Generic.List<DeployRecordItem>();
                }
            }
            catch { }
        }

        // JSON 模型
        private class DeployListResponse
        {
            public bool success { get; set; }
            public System.Collections.Generic.List<DeployPackageItem> packages { get; set; } = new();
            public int count { get; set; }
        }

        private class DeployPackageItem
        {
            public string packageId { get; set; } = "";
            public string name { get; set; } = "";
            public string version { get; set; } = "";
            public string type { get; set; } = "";
            public long fileSize { get; set; }
            public string signer { get; set; } = "";
        }

        private class DeployTaskResponse
        {
            public bool success { get; set; }
            public int count { get; set; }
        }

        private class DeployHistoryResponse
        {
            public bool success { get; set; }
            public System.Collections.Generic.List<DeployRecordItem> records { get; set; } = new();
            public int count { get; set; }
        }

        private class DeployRecordItem
        {
            public string recordId { get; set; } = "";
            public string name { get; set; } = "";
            public string version { get; set; } = "";
            public string type { get; set; } = "";
            public string status { get; set; } = "";
            public string deployedAt { get; set; } = "";
        }
    }
}