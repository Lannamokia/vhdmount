using System;
using System.IO;
using System.IO.Compression;
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

        // ---------- 软件部署包打包器 ----------

        private void PackBrowseInstallScript(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "PowerShell Scripts|*.ps1|All Files|*.*",
            };
            if (dialog.ShowDialog() == true)
            {
                PackInstallScript.Text = dialog.FileName;
            }
        }

        private void PackBrowseUninstallScript(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "PowerShell Scripts|*.ps1|All Files|*.*",
            };
            if (dialog.ShowDialog() == true)
            {
                PackUninstallScript.Text = dialog.FileName;
            }
        }

        private void PackBrowsePayloadDir(object sender, RoutedEventArgs e)
        {
            if (TrySelectFolder(out var selectedPath))
            {
                PackPayloadDir.Text = selectedPath;
            }
        }

        private void PackBrowsePrivateKey(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "PEM Files|*.pem|All Files|*.*",
            };
            if (dialog.ShowDialog() == true)
            {
                PackPrivateKey.Text = dialog.FileName;
            }
        }

        private void PackBrowseOutDir(object sender, RoutedEventArgs e)
        {
            if (TrySelectFolder(out var selectedPath))
            {
                PackOutDir.Text = selectedPath;
            }
        }

        private void PackAndSign(object sender, RoutedEventArgs e)
        {
            try
            {
                var type = ((System.Windows.Controls.ComboBoxItem)PackTypeBox.SelectedItem).Content?.ToString()?.Trim() ?? "software-deploy";
                var isSoftwareDeploy = string.Equals(type, "software-deploy", StringComparison.OrdinalIgnoreCase);
                var installScriptPath = PackInstallScript.Text?.Trim() ?? "";
                var uninstallScriptPath = PackUninstallScript.Text?.Trim() ?? "";
                var payloadDir = PackPayloadDir.Text?.Trim() ?? "";
                var name = PackName.Text?.Trim() ?? "";
                var version = PackVersion.Text?.Trim() ?? "";
                var signer = PackSigner.Text?.Trim() ?? "";
                var privateKeyPath = PackPrivateKey.Text?.Trim() ?? "";
                var outputDir = string.IsNullOrWhiteSpace(PackOutDir.Text) ? "." : PackOutDir.Text.Trim();

                if (string.IsNullOrWhiteSpace(name))
                    throw new InvalidOperationException("包名称不能为空");
                if (string.IsNullOrWhiteSpace(version))
                    throw new InvalidOperationException("版本号不能为空");
                if (string.IsNullOrWhiteSpace(signer))
                    throw new InvalidOperationException("签名者不能为空");
                if (string.IsNullOrWhiteSpace(privateKeyPath) || !File.Exists(privateKeyPath))
                    throw new InvalidOperationException("私钥文件不存在");
                if (isSoftwareDeploy && (string.IsNullOrWhiteSpace(installScriptPath) || !File.Exists(installScriptPath)))
                    throw new InvalidOperationException("software-deploy 类型必须提供 install.ps1");
                if (!string.IsNullOrWhiteSpace(payloadDir) && !Directory.Exists(payloadDir))
                    throw new InvalidOperationException("文件负载目录不存在");

                Directory.CreateDirectory(outputDir);
                var safeName = SanitizeFileName($"{name}-{version}");
                var zipPath = Path.Combine(outputDir, $"{safeName}.zip");
                var sigPath = Path.Combine(outputDir, $"{safeName}.zip.sig");

                // 创建临时 staging 目录
                var stagingDir = Path.Combine(Path.GetTempPath(), $"vhd-deploy-pack-{Guid.NewGuid()}");
                try
                {
                    Directory.CreateDirectory(stagingDir);

                    // 复制文件负载
                    if (!string.IsNullOrWhiteSpace(payloadDir) && Directory.Exists(payloadDir))
                    {
                        CopyDirectoryContents(payloadDir, stagingDir);
                    }

                    // software-deploy 类型：复制脚本到根目录
                    if (isSoftwareDeploy)
                    {
                        File.Copy(installScriptPath, Path.Combine(stagingDir, "install.ps1"), overwrite: true);
                        if (!string.IsNullOrWhiteSpace(uninstallScriptPath) && File.Exists(uninstallScriptPath))
                        {
                            File.Copy(uninstallScriptPath, Path.Combine(stagingDir, "uninstall.ps1"), overwrite: true);
                        }
                    }

                    // 生成 deploy.json 元数据
                    var deployManifest = new
                    {
                        name,
                        version,
                        type,
                        signer,
                        createdAt = DateTime.UtcNow.ToString("o"),
                    };
                    var manifestJson = JsonSerializer.Serialize(deployManifest, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(Path.Combine(stagingDir, "deploy.json"), manifestJson, Encoding.UTF8);

                    // 打包成 ZIP
                    if (File.Exists(zipPath))
                    {
                        File.Delete(zipPath);
                    }
                    ZipFile.CreateFromDirectory(stagingDir, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);

                    // 签名 ZIP
                    var privateKeyBytes = ReadPem(privateKeyPath, "PRIVATE KEY");
                    using var rsa = RSA.Create();
                    rsa.ImportPkcs8PrivateKey(privateKeyBytes, out _);
                    var zipBytes = File.ReadAllBytes(zipPath);
                    var signature = rsa.SignData(zipBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
                    File.WriteAllText(sigPath, Convert.ToBase64String(signature), Encoding.UTF8);

                    StatusText.Text = $"打包完成: {safeName}.zip + .zip.sig";
                }
                finally
                {
                    try
                    {
                        if (Directory.Exists(stagingDir))
                        {
                            Directory.Delete(stagingDir, recursive: true);
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"打包失败: {ex.Message}";
            }
        }

        private static void CopyDirectoryContents(string sourceDir, string targetDir)
        {
            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourceDir, file);
                var destPath = Path.Combine(targetDir, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                File.Copy(file, destPath, overwrite: true);
            }
        }
    }
}