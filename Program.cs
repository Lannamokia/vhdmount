using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Security.Cryptography;
using System.Text.Json;
using System.Globalization;

namespace VHDMounter
{
    class Program
    {
        private const long LOG_MAX_BYTES = 10 * 1024 * 1024; // 10MB

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
                            try
                            {
                                // 通过 StreamWriter 的 BaseStream 截断并回到文件开头
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
                                    // 兜底方案：直接用文件流截断
                                    using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                                    fs.SetLength(0);
                                }
                            }
                            catch (Exception ex)
                            {
                                Trace.WriteLine($"日志循环覆盖失败: {ex}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"日志大小监控异常: {ex}");
                    }
                    await Task.Delay(2000, token);
                }
            }, token);
        }

        private static void StartNxlogMonitor(string logPath, CancellationToken token)
        {
            Task.Run(async () =>
            {
                var lastCopiedWriteTimes = new System.Collections.Generic.Dictionary<string, DateTime>();
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var srcInfo = new FileInfo(logPath);
                        if (!srcInfo.Exists)
                        {
                            await Task.Delay(5000, token);
                            continue;
                        }

                        var nxlogDrives = DriveInfo.GetDrives()
                            .Where(d => d.IsReady && string.Equals(d.VolumeLabel, "NXLOG", StringComparison.OrdinalIgnoreCase)
                                        && d.DriveType == DriveType.Removable);

                        foreach (var drive in nxlogDrives)
                        {
                            var destPath = Path.Combine(drive.RootDirectory.FullName, "vhdmounter.log");
                            try
                            {
                                var key = drive.RootDirectory.FullName;
                                lastCopiedWriteTimes.TryGetValue(key, out var last);
                                if (srcInfo.LastWriteTimeUtc != last)
                                {
                                    using var src = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                                    using var dst = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                                    await src.CopyToAsync(dst, 81920, token);
                                    lastCopiedWriteTimes[key] = srcInfo.LastWriteTimeUtc;
                                    Trace.WriteLine($"日志已拷贝到 {destPath}");
                                }
                            }
                            catch (Exception ex)
                            {
                                Trace.WriteLine($"拷贝日志到 {drive.Name} 失败: {ex.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"NXLOG 设备监控异常: {ex.Message}");
                    }
                    await Task.Delay(5000, token);
                }
            }, token);
        }

        [STAThread]
        static void Main(string[] args)
        {
            // 检查是否已有实例运行
            using (var mutex = new System.Threading.Mutex(true, "VHDMounterApp", out bool createdNew))
            {
                if (!createdNew)
                {
                    return; // 已有实例运行，退出
                }

                // 初始化文件日志：将 Debug/Trace 输出到应用目录下的 vhdmounter.log
                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vhdmounter.log");
                var logStream = new FileStream(logPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
                // 追加写入：移动到文件末尾，便于后续截断实现循环覆盖
                logStream.Seek(0, SeekOrigin.End);
                var fileListener = new TextWriterTraceListener(logStream, "VHDMounterFileLogger");
                Trace.Listeners.Add(fileListener);
                Trace.AutoFlush = true;
                Trace.WriteLine($"==== VHDMounter 启动 {DateTime.Now:yyyy-MM-dd HH:mm:ss} ====");

                // 启动后台任务：日志大小循环覆盖 + NXLOG 设备监控拷贝
                var cts = new CancellationTokenSource();
                StartLogSizeMonitor(logPath, fileListener, cts.Token);
                StartNxlogMonitor(logPath, cts.Token);

                // 记录运行环境与 RID 相关路径
                try
                {
                    Trace.WriteLine($"BaseDirectory: {AppContext.BaseDirectory}");
                    var ridLibPath = Path.Combine(AppContext.BaseDirectory, "runtimes", "win10-x64", "lib", "netstandard1.6");
                    Trace.WriteLine($"RID lib path: {ridLibPath} Exists={Directory.Exists(ridLibPath)}");
                }
                catch { }

                // 绑定程序集解析事件，确保 Microsoft.Management.Infrastructure 能被解析
                AppDomain.CurrentDomain.AssemblyResolve += (sender, ev) =>
                {
                    try
                    {
                        if (ev.Name.StartsWith("Microsoft.Management.Infrastructure", StringComparison.OrdinalIgnoreCase))
                        {
                            var candidates = new[]
                            {
                                Path.Combine(AppContext.BaseDirectory, "Microsoft.Management.Infrastructure.dll"),
                                Path.Combine(AppContext.BaseDirectory, "runtimes", "win10-x64", "lib", "netstandard1.6", "Microsoft.Management.Infrastructure.dll"),
                                Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "lib", "netstandard1.6", "Microsoft.Management.Infrastructure.dll")
                            };
                            foreach (var p in candidates)
                            {
                                if (File.Exists(p))
                                {
                                    Trace.WriteLine($"AssemblyResolve: 加载 {p}");
                                    return System.Reflection.Assembly.LoadFrom(p);
                                }
                            }
                            Trace.WriteLine("AssemblyResolve: 未找到 Microsoft.Management.Infrastructure.dll");
                        }
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"AssemblyResolve 异常: {ex}");
                    }
                    return null;
                };

                var selfUpdated = TryPerformSelfUpdateFromUsb();
                if (selfUpdated)
                {
                    try
                    {
                        cts.Cancel();
                        fileListener.Flush();
                        fileListener.Close();
                    }
                    catch { }
                    return;
                }

                var app = new Application();
                var mainWindow = new MainWindow();
                app.Run(mainWindow);

                try
                {
                    cts.Cancel();
                    fileListener.Flush();
                    fileListener.Close();
                }
                catch { }
            }
        }

        private static bool TryPerformSelfUpdateFromUsb()
        {
            try
            {
                var baseDir = AppContext.BaseDirectory;
                var trustedKeys = Path.Combine(baseDir, "trusted_keys.pem");
                if (!File.Exists(trustedKeys)) return false;
                var nx = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && d.DriveType == DriveType.Removable && string.Equals(d.VolumeLabel, "NX_INS", StringComparison.OrdinalIgnoreCase));
                if (nx == null) return false;
                var root = nx.RootDirectory.FullName;
                var candidates = new[]
                {
                    Path.Combine(root, "manifest.json"),
                    Path.Combine(root, "updates", "manifest.json")
                };
                string manifestPath = candidates.FirstOrDefault(File.Exists);
                if (string.IsNullOrEmpty(manifestPath)) return false;
                var sigPath = Path.Combine(Path.GetDirectoryName(manifestPath) ?? root, "manifest.sig");
                if (!File.Exists(sigPath)) return false;
                var ok = UpdateSecurity.VerifyManifestSignature(manifestPath, sigPath, trustedKeys);
                if (!ok) return false;
                var manifest = UpdateSecurity.LoadManifest(manifestPath);
                if (!string.Equals(manifest.type, "app-update", StringComparison.OrdinalIgnoreCase)) return false;
                DateTime now = DateTime.UtcNow;
                DateTime exp;
                if (!string.IsNullOrWhiteSpace(manifest.expiresAt) && DateTime.TryParse(manifest.expiresAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var e))
                {
                    exp = e;
                }
                else if (DateTime.TryParse(manifest.createdAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var c))
                {
                    exp = c.AddDays(3);
                }
                else
                {
                    return false;
                }
                if (now > exp) return false;
                try
                {
                    var flagPath = Path.Combine(baseDir, "update_done.flag");
                    if (File.Exists(flagPath))
                    {
                        var localVersion = File.ReadAllText(flagPath).Trim();
                        var cmp = string.Compare(localVersion ?? "", manifest.version ?? "", StringComparison.Ordinal);
                        if (cmp >= 0)
                        {
                            Trace.WriteLine($"跳过自更新：update_done.flag({localVersion}) >= manifest.version({manifest.version})");
                            return false;
                        }
                    }
                }
                catch { }
                var staging = Path.Combine(baseDir, "staging");
                if (!Directory.Exists(staging)) Directory.CreateDirectory(staging);
                foreach (var f in manifest.files)
                {
                    var src = Path.Combine(Path.GetDirectoryName(manifestPath) ?? root, f.path.Replace('/', Path.DirectorySeparatorChar));
                    if (!UpdateSecurity.VerifyFileHash(src, f.sha256, f.size)) return false;
                    var dest = Path.Combine(staging, f.path.Replace('/', Path.DirectorySeparatorChar));
                    var destDir = Path.GetDirectoryName(dest) ?? staging;
                    if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
                    using var srcFs = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var dstFs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None);
                    srcFs.CopyTo(dstFs);
                    dstFs.Flush(true);
                }
                var stagingManifest = Path.Combine(staging, "manifest.json");
                var stagingSig = Path.Combine(staging, "manifest.sig");
                File.Copy(manifestPath, stagingManifest, true);
                File.Copy(sigPath, stagingSig, true);
                var updaterExe = Path.Combine(baseDir, "Updater.exe");
                var updaterDll = Path.Combine(baseDir, "Updater.dll");
                ProcessStartInfo psi;
                if (File.Exists(updaterExe))
                {
                    psi = new ProcessStartInfo
                    {
                        FileName = updaterExe,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                }
                else if (File.Exists(updaterDll))
                {
                    psi = new ProcessStartInfo
                    {
                        FileName = "dotnet",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    psi.ArgumentList.Add(updaterDll);
                }
                else
                {
                    return false;
                }
                psi.ArgumentList.Add("--manifest");
                psi.ArgumentList.Add(stagingManifest);
                psi.ArgumentList.Add("--pid");
                psi.ArgumentList.Add(Environment.ProcessId.ToString());
                var p = Process.Start(psi);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
