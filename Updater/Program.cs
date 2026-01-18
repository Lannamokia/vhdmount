using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Updater
{
    class Program
    {
        public class UpdateManifestFile
        {
            public string path { get; set; } = string.Empty;
            public string target { get; set; } = string.Empty;
            public long size { get; set; }
            public string sha256 { get; set; } = string.Empty;
        }
        public class UpdateManifest
        {
            public string version { get; set; } = string.Empty;
            public string minVersion { get; set; } = string.Empty;
            public string type { get; set; } = string.Empty;
            public string signer { get; set; } = string.Empty;
            public string createdAt { get; set; } = string.Empty;
            public string expiresAt { get; set; } = string.Empty;
            public System.Collections.Generic.List<UpdateManifestFile> files { get; set; } = new();
        }
        private const int MOVEFILE_REPLACE_EXISTING = 0x1;
        private const int MOVEFILE_DELAY_UNTIL_REBOOT = 0x4;
        private const int MOVEFILE_WRITE_THROUGH = 0x8;
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool MoveFileEx(string lpExistingFileName, string lpNewFileName, int dwFlags);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(IntPtr TokenHandle, int TokenInformationClass, IntPtr TokenInformation, int TokenInformationLength, out int ReturnLength);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);
        private const uint TOKEN_QUERY = 0x0008;
        private const int TokenElevation = 20;
        private const long LOG_MAX_BYTES = 10 * 1024 * 1024;
        private static void StartLogSizeMonitor(string logPath, TextWriterTraceListener listener, CancellationToken token)
        {
            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        listener?.Flush();
                        var fi = new FileInfo(logPath);
                        if (fi.Exists && fi.Length > LOG_MAX_BYTES)
                        {
                            if (listener.Writer is StreamWriter sw)
                            {
                                sw.Flush();
                                var baseStream = sw.BaseStream;
                                baseStream.Seek(0, SeekOrigin.Begin);
                                baseStream.SetLength(0);
                                sw.WriteLine($"==== 日志达到上限，循环覆盖于 {DateTime.Now:yyyy-MM-dd HH:mm:ss} ====");
                                sw.Flush();
                            }
                            else
                            {
                                using var fs = new FileStream(logPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                                fs.Seek(0, SeekOrigin.Begin);
                                fs.SetLength(0);
                                using var tw = new StreamWriter(fs);
                                tw.WriteLine($"==== 日志达到上限，循环覆盖于 {DateTime.Now:yyyy-MM-dd HH:mm:ss} ====");
                                tw.Flush();
                            }
                        }
                    }
                    catch { }
                    await Task.Delay(1000);
                }
            });
        }
        private static bool IsProcessElevated()
        {
            try
            {
                if (!OpenProcessToken(Process.GetCurrentProcess().Handle, TOKEN_QUERY, out var hToken)) return false;
                try
                {
                    int size = 0;
                    GetTokenInformation(hToken, TokenElevation, IntPtr.Zero, 0, out size);
                    var ptr = Marshal.AllocHGlobal(size);
                    try
                    {
                        if (GetTokenInformation(hToken, TokenElevation, ptr, size, out _))
                        {
                            int elevated = Marshal.ReadInt32(ptr);
                            return elevated != 0;
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(ptr);
                    }
                }
                finally
                {
                    CloseHandle(hToken);
                }
            }
            catch { }
            try
            {
                var wi = WindowsIdentity.GetCurrent();
                var wp = new WindowsPrincipal(wi);
                return wp.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { }
            return false;
        }
        private static int RelaunchSelfElevated(string[] args)
        {
            try
            {
                var exe = Process.GetCurrentProcess().MainModule?.FileName ?? "Updater.exe";
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = AppContext.BaseDirectory,
                    Arguments = string.Join(" ", args.Select(a => a.Contains(" ") ? ("\"" + a + "\"") : a))
                };
                Process.Start(psi);
                return 0;
            }
            catch
            {
                return 1;
            }
        }
        private static UpdateManifest LoadManifest(string manifestPath)
        {
            var json = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(json);
            return manifest ?? new UpdateManifest();
        }
        private static bool VerifyManifestSignature(string manifestPath, string signaturePath, string trustedKeysPemPath)
        {
            if (!File.Exists(trustedKeysPemPath)) return false;
            if (!File.Exists(signaturePath)) return false;
            var manifestBytes = File.ReadAllBytes(manifestPath);
            var sigBytes = ReadSignatureBytes(signaturePath);
            var rsas = LoadRsaKeysFromPem(trustedKeysPemPath);
            foreach (var rsa in rsas)
            {
                try
                {
                    if (rsa.VerifyData(manifestBytes, sigBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
                    {
                        return true;
                    }
                }
                catch { }
            }
            return false;
        }
        private static byte[] ReadSignatureBytes(string signaturePath)
        {
            var raw = File.ReadAllText(signaturePath).Trim();
            try
            {
                return Convert.FromBase64String(raw);
            }
            catch
            {
                return File.ReadAllBytes(signaturePath);
            }
        }
        private static System.Collections.Generic.List<RSA> LoadRsaKeysFromPem(string pemPath)
        {
            var text = File.ReadAllText(pemPath);
            var blocks = SplitPemBlocks(text);
            var list = new System.Collections.Generic.List<RSA>();
            foreach (var b in blocks)
            {
                try
                {
                    var data = Convert.FromBase64String(b);
                    var rsa = RSA.Create();
                    rsa.ImportSubjectPublicKeyInfo(data, out _);
                    if (rsa.KeySize < 2048) rsa.KeySize = 2048;
                    list.Add(rsa);
                }
                catch { }
            }
            return list;
        }
        private static System.Collections.Generic.List<string> SplitPemBlocks(string pem)
        {
            var lines = pem.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder();
            var blocks = new System.Collections.Generic.List<string>();
            var capture = false;
            foreach (var l in lines)
            {
                var s = l.Trim();
                if (s.StartsWith("-----BEGIN PUBLIC KEY-----"))
                {
                    sb.Clear();
                    capture = true;
                    continue;
                }
                if (s.StartsWith("-----END PUBLIC KEY-----"))
                {
                    capture = false;
                    blocks.Add(sb.ToString());
                    sb.Clear();
                    continue;
                }
                if (capture)
                {
                    sb.Append(s);
                }
            }
            return blocks;
        }
        private static bool VerifyFileHash(string path, string expectedSha256, long expectedSize)
        {
            if (!File.Exists(path)) return false;
            var fi = new FileInfo(path);
            if (expectedSize > 0 && fi.Length != expectedSize) return false;
            using var sha = SHA256.Create();
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hash = sha.ComputeHash(fs);
            var hex = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            return string.Equals(hex, expectedSha256?.ToLowerInvariant(), StringComparison.Ordinal);
        }
        private static bool AtomicReplace(string stagedPath, string targetPath)
        {
            var dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return MoveFileEx(stagedPath, targetPath, MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH);
        }
        private static bool DelayReplaceUntilReboot(string stagedPath, string targetPath)
        {
            var dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return MoveFileEx(stagedPath, targetPath, MOVEFILE_REPLACE_EXISTING | MOVEFILE_DELAY_UNTIL_REBOOT);
        }
        static int Main(string[] args)
        {
            if (!IsProcessElevated())
            {
                var code = RelaunchSelfElevated(args);
                return code;
            }
            string manifest = null;
            int pid = 0;
            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (string.Equals(a, "--manifest", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) manifest = args[++i];
                else if (string.Equals(a, "--pid", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) int.TryParse(args[++i], out pid);
            }
            if (string.IsNullOrWhiteSpace(manifest)) return 2;
            var manifestDir = Path.GetDirectoryName(manifest) ?? AppContext.BaseDirectory;
            var sigPath = Path.Combine(manifestDir, "manifest.sig");
            var baseDir = AppContext.BaseDirectory;
            var logPath = Path.Combine(baseDir, "updater.log");
            var logStream = new FileStream(logPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
            logStream.Seek(0, SeekOrigin.End);
            var fileListener = new TextWriterTraceListener(logStream, "UpdaterFileLogger");
            Trace.Listeners.Add(fileListener);
            Trace.AutoFlush = true;
            Trace.WriteLine($"==== Updater 启动 {DateTime.Now:yyyy-MM-dd HH:mm:ss} ====");
            var cts = new CancellationTokenSource();
            StartLogSizeMonitor(logPath, fileListener, cts.Token);
            var trustedKeys = Path.Combine(baseDir, "trusted_keys.pem");
            if (!File.Exists(trustedKeys)) return 3;
            if (!File.Exists(sigPath)) return 4;
            var ok = VerifyManifestSignature(manifest, sigPath, trustedKeys);
            Trace.WriteLine(ok ? "清单验签通过" : "清单验签失败");
            if (!ok) return 5;
            var m = LoadManifest(manifest);
            if (!string.Equals(m.type, "app-update", StringComparison.OrdinalIgnoreCase)) return 6;
            DateTime now = DateTime.UtcNow;
            DateTime exp;
            if (!string.IsNullOrWhiteSpace(m.expiresAt) && DateTime.TryParse(m.expiresAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var e))
            {
                exp = e;
            }
            else if (DateTime.TryParse(m.createdAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var c))
            {
                exp = c.AddDays(3);
            }
            else
            {
                return 8;
            }
            if (now > exp)
            {
                Trace.WriteLine("程序更新过期，拒绝更新");
                return 9;
            }
            var flagPath = Path.Combine(baseDir, "update_done.flag");
            string localVersion = null;
            bool flagExists = false;
            try
            {
                flagExists = File.Exists(flagPath);
                if (flagExists) localVersion = File.ReadAllText(flagPath).Trim();
            }
            catch { }
            if (flagExists)
            {
                var cmp = string.Compare(localVersion ?? "", m.minVersion ?? "", StringComparison.Ordinal);
                if (cmp > 0)
                {
                    Trace.WriteLine($"拒绝程序更新：当前版本高于清单的最小版本要求（local={localVersion}, min={m.minVersion}）");
                    return 10;
                }
                if (cmp == 0)
                {
                    Trace.WriteLine($"跳过程序更新：当前版本等于清单的最小版本（{localVersion}）");
                    return 0;
                }
            }
            if (pid > 0)
            {
                try
                {
                    var p = Process.GetProcessById(pid);
                    try { p.WaitForExit(); } catch { }
                }
                catch { }
            }
            bool delayed = false;
            foreach (var f in m.files)
            {
                var src = Path.Combine(manifestDir, f.path.Replace('/', Path.DirectorySeparatorChar));
                var tgtRaw = f.target.Replace('/', Path.DirectorySeparatorChar);
                var tgt = Path.IsPathRooted(tgtRaw) ? tgtRaw : Path.Combine(baseDir, tgtRaw);
                if (!VerifyFileHash(src, f.sha256, f.size))
                {
                    Trace.WriteLine($"校验失败：{Path.GetFileName(src)}");
                    return 7;
                }
                var tgtDir = Path.GetDirectoryName(tgt);
                if (!string.IsNullOrEmpty(tgtDir) && !Directory.Exists(tgtDir)) Directory.CreateDirectory(tgtDir);
                var staged = src + ".staged";
                using (var s = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var d = new FileStream(staged, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    s.CopyTo(d);
                    d.Flush(true);
                }
                var replaced = AtomicReplace(staged, tgt);
                if (!replaced)
                {
                    DelayReplaceUntilReboot(staged, tgt);
                    delayed = true;
                }
                Trace.WriteLine($"替换目标：{tgt} {(delayed ? "延迟到重启" : "立即生效")}");
            }
            try
            {
                var flag = Path.Combine(baseDir, "update_done.flag");
                File.WriteAllText(flag, m.version ?? "");
                Trace.WriteLine($"写入更新标记：{m.version}");
            }
            catch { }
            if (!delayed)
            {
                try
                {
                    var exe = Path.Combine(baseDir, "VHDMounter.exe");
                    if (File.Exists(exe))
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = exe,
                            UseShellExecute = true,
                            Verb = "runas",
                            WorkingDirectory = baseDir
                        };
                        Process.Start(psi);
                        Trace.WriteLine("已重新拉起主程序");
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"拉起主程序失败：{ex.Message}");
                }
            }
            return 0;
        }
    }
}
