using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using System.Windows.Forms;

namespace UpdatePackagerGUI
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            VersionBox.Text = DateTime.UtcNow.ToString("yyyy.MM.dd.HHmmss");
        }

        private void CloseApp(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BrowseFolderKeyOut(object sender, RoutedEventArgs e)
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog();
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                KeyOutDir.Text = dlg.SelectedPath;
            }
        }

        private void BrowseFolderPayload(object sender, RoutedEventArgs e)
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog();
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                PayloadDir.Text = dlg.SelectedPath;
            }
        }

        private void BrowseFolderManifestOut(object sender, RoutedEventArgs e)
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog();
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                ManifestOutDir.Text = dlg.SelectedPath;
            }
        }

        private void BrowsePrivateKey(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Filter = "PEM Files|*.pem|All Files|*.*";
            if (dlg.ShowDialog() == true)
            {
                PrivateKeyPath.Text = dlg.FileName;
            }
        }

        private void GenerateKey(object sender, RoutedEventArgs e)
        {
            try
            {
                var outDir = string.IsNullOrWhiteSpace(KeyOutDir.Text) ? "." : KeyOutDir.Text;
                var keyId = string.IsNullOrWhiteSpace(KeyIdBox.Text) ? ("key-" + DateTime.UtcNow.ToString("yyyyMMdd")) : KeyIdBox.Text;
                Directory.CreateDirectory(outDir);
                var privPath = System.IO.Path.Combine(outDir, "private_key_" + keyId + ".pem");
                var pubPath = System.IO.Path.Combine(outDir, "public_key_" + keyId + ".pem");
                var trustPath = System.IO.Path.Combine(outDir, "trusted_keys.pem");
                using var rsa = RSA.Create(3072);
                var pkcs8 = rsa.ExportPkcs8PrivateKey();
                var spki = rsa.ExportSubjectPublicKeyInfo();
                WritePem(privPath, "PRIVATE KEY", pkcs8);
                WritePem(pubPath, "PUBLIC KEY", spki);
                AppendPem(trustPath, "PUBLIC KEY", spki);
                StatusText.Text = "密钥生成完成";
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
                var payloadDir = PayloadDir.Text;
                var outDir = string.IsNullOrWhiteSpace(ManifestOutDir.Text) ? "." : ManifestOutDir.Text;
                var type = ((System.Windows.Controls.ComboBoxItem)TypeBox.SelectedItem).Content?.ToString() ?? "app-update";
                var minVersion = string.IsNullOrWhiteSpace(MinVersionBox.Text) ? "1.5.0" : MinVersionBox.Text;
                var version = string.IsNullOrWhiteSpace(VersionBox.Text) ? DateTime.UtcNow.ToString("yyyy.MM.dd.HHmmss") : VersionBox.Text;
                var created = DateTime.UtcNow;
                var createdStr = created.ToString("o");
                var expiresStr = created.AddDays(3).ToString("o");
                var files = Directory.GetFiles(payloadDir, "*", SearchOption.AllDirectories);
                var manifest = new
                {
                    version = version,
                    minVersion = minVersion,
                    type = type,
                    signer = "gui",
                    createdAt = createdStr,
                    expiresAt = expiresStr,
                    files = new System.Collections.Generic.List<object>()
                };
                Directory.CreateDirectory(outDir);
                var list = new System.Collections.Generic.List<object>();
                foreach (var file in files)
                {
                    var rel = System.IO.Path.GetRelativePath(payloadDir, file).Replace('\\', '/');
                    var path = System.IO.Path.GetFileName(rel);
                    var target = string.Equals(type, "app-update", StringComparison.OrdinalIgnoreCase)
                        ? rel
                        : System.IO.Path.GetFileName(rel);
                    using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var size = fs.Length;
                    using var sha = SHA256.Create();
                    var hash = sha.ComputeHash(fs);
                    var hex = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                    list.Add(new { path = path, target = target, size = size, sha256 = hex });
                }
                var manifestObj = new
                {
                    version = version,
                    minVersion = minVersion,
                    type = type,
                    signer = "gui",
                    createdAt = createdStr,
                    expiresAt = expiresStr,
                    files = list
                };
                var manifestOut = System.IO.Path.Combine(outDir, "manifest.json");
                var manifestSig = System.IO.Path.Combine(outDir, "manifest.sig");
                var json = JsonSerializer.Serialize(manifestObj, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(manifestOut, json, Encoding.UTF8);
                var privPath = PrivateKeyPath.Text;
                var priv = ReadPem(privPath, "PRIVATE KEY");
                using var rsaSign = RSA.Create();
                rsaSign.ImportPkcs8PrivateKey(priv, out _);
                var data = File.ReadAllBytes(manifestOut);
                var sig = rsaSign.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
                File.WriteAllText(manifestSig, Convert.ToBase64String(sig), Encoding.UTF8);
                StatusText.Text = "清单与签名生成完成";
            }
            catch (Exception ex)
            {
                StatusText.Text = ex.Message;
            }
        }

        private void WritePem(string path, string type, byte[] der)
        {
            var b64 = Convert.ToBase64String(der);
            var sb = new StringBuilder();
            sb.AppendLine("-----BEGIN " + type + "-----");
            for (int i = 0; i < b64.Length; i += 64)
            {
                sb.AppendLine(b64.Substring(i, Math.Min(64, b64.Length - i)));
            }
            sb.AppendLine("-----END " + type + "-----");
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private void AppendPem(string path, string type, byte[] der)
        {
            var b64 = Convert.ToBase64String(der);
            var sb = new StringBuilder();
            sb.AppendLine("-----BEGIN " + type + "-----");
            for (int i = 0; i < b64.Length; i += 64)
            {
                sb.AppendLine(b64.Substring(i, Math.Min(64, b64.Length - i)));
            }
            sb.AppendLine("-----END " + type + "-----");
            File.AppendAllText(path, sb.ToString() + Environment.NewLine, Encoding.UTF8);
        }

        private ReadOnlySpan<byte> ReadPem(string path, string type)
        {
            var txt = File.ReadAllText(path);
            var start = "-----BEGIN " + type + "-----";
            var end = "-----END " + type + "-----";
            var s = txt.IndexOf(start, StringComparison.Ordinal);
            var e = txt.IndexOf(end, StringComparison.Ordinal);
            if (s < 0 || e < 0) return default;
            s += start.Length;
            var b64 = txt.Substring(s, e - s).Replace("\r", "").Replace("\n", "").Trim();
            var bytes = Convert.FromBase64String(b64);
            return bytes;
        }
    }
}
