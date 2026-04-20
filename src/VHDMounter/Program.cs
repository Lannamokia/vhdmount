using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Security.Cryptography;
using System.Text;
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

                MachineLogClientConfiguration machineLogConfig = null;
                MachineLogBuffer machineLogBuffer = null;
                MachineLogTraceListener machineLogTraceListener = null;
                MachineLogRealtimeChannel machineLogRealtimeChannel = null;
                var machineLogDiagnosticsPath = Path.Combine(AppContext.BaseDirectory, "machine-log-client.log");
                var machineLogDiagnosticsLock = new object();
                Action<string> machineLogDiagnostics = (message) =>
                {
                    try
                    {
                        var sanitized = MachineLogSanitizer.SanitizeSensitiveText(message);
                        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} MACHINE_LOG: {sanitized}{Environment.NewLine}";
                        lock (machineLogDiagnosticsLock)
                        {
                            File.AppendAllText(machineLogDiagnosticsPath, line, Encoding.UTF8);
                        }
                    }
                    catch
                    {
                    }
                };

                try
                {
                    var configPath = Path.Combine(AppContext.BaseDirectory, "vhdmonter_config.ini");
                    machineLogConfig = MachineLogClientConfiguration.Load(configPath, machineLogDiagnostics);
                    if (machineLogConfig.EnableLogUpload)
                    {
                        machineLogBuffer = new MachineLogBuffer(
                            machineLogConfig.SpoolPath,
                            MachineLogClientConfiguration.GenerateSessionId(),
                            machineLogConfig.MachineLogUploadMaxSpoolBytes,
                            machineLogDiagnostics);
                        machineLogTraceListener = new MachineLogTraceListener(machineLogBuffer);
                        Trace.Listeners.Add(machineLogTraceListener);
                        machineLogDiagnostics($"机台日志 spool 已初始化: {machineLogConfig.SpoolPath}");
                    }
                }
                catch (Exception ex)
                {
                    machineLogDiagnostics($"初始化机台日志本地 spool 失败: {ex.Message}");
                    machineLogTraceListener?.Dispose();
                    machineLogBuffer?.Dispose();
                    machineLogTraceListener = null;
                    machineLogBuffer = null;
                    machineLogConfig = null;
                }

                Trace.WriteLine($"==== VHDMounter 启动 {DateTime.Now:yyyy-MM-dd HH:mm:ss} ====");

                try
                {
                    var originalCurrentDirectory = Environment.CurrentDirectory;
                    Directory.SetCurrentDirectory(AppContext.BaseDirectory);
                    Trace.WriteLine($"CurrentDirectory: {originalCurrentDirectory} -> {Environment.CurrentDirectory}");
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"设置当前目录失败: {ex}");
                }

                AppDomain.CurrentDomain.UnhandledException += (sender, ev) =>
                {
                    try
                    {
                        Trace.WriteLine($"UnhandledException: {ev.ExceptionObject}");
                    }
                    catch { }
                };

                TaskScheduler.UnobservedTaskException += (sender, ev) =>
                {
                    try
                    {
                        Trace.WriteLine($"UnobservedTaskException: {ev.Exception}");
                    }
                    catch { }
                };

                // 启动后台任务：日志大小循环覆盖 + NXLOG 设备监控拷贝
                var cts = new CancellationTokenSource();
                StartLogSizeMonitor(logPath, fileListener, cts.Token);
                StartNxlogMonitor(logPath, cts.Token);

                // 记录运行环境与 RID 相关路径
                try
                {
                    Trace.WriteLine($"BaseDirectory: {AppContext.BaseDirectory}");
                    var ridLibPath = Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "lib", "netstandard1.6");
                    Trace.WriteLine($"RID lib path: {ridLibPath} Exists={Directory.Exists(ridLibPath)}");
                }
                catch { }

                Trace.WriteLine("准备执行自更新检查");
                var selfUpdated = TryPerformSelfUpdateFromUsb();
                Trace.WriteLine($"自更新检查完成 SelfUpdated={selfUpdated}");
                if (selfUpdated)
                {
                    try
                    {
                        cts.Cancel();
                        machineLogRealtimeChannel?.Dispose();
                        if (machineLogTraceListener != null)
                        {
                            Trace.Listeners.Remove(machineLogTraceListener);
                            machineLogTraceListener.Dispose();
                        }
                        machineLogBuffer?.Dispose();
                        fileListener.Flush();
                        fileListener.Close();
                    }
                    catch { }
                    return;
                }

                try
                {
                    if (machineLogConfig?.EnableLogUpload == true && machineLogBuffer != null)
                    {
                        machineLogRealtimeChannel = new MachineLogRealtimeChannel(
                            machineLogConfig,
                            machineLogBuffer,
                            machineLogDiagnostics);
                        machineLogRealtimeChannel.Start(cts.Token);
                    }
                }
                catch (Exception ex)
                {
                    machineLogDiagnostics($"启动机台日志实时通道失败: {ex.Message}");
                    machineLogRealtimeChannel?.Dispose();
                    machineLogRealtimeChannel = null;
                }

                Trace.WriteLine("准备创建 WPF Application");
                var app = new Application();
                Trace.WriteLine("WPF Application 创建完成");
                app.DispatcherUnhandledException += (sender, ev) =>
                {
                    try
                    {
                        Trace.WriteLine($"DispatcherUnhandledException: {ev.Exception}");
                    }
                    catch { }
                };

                Trace.WriteLine("准备创建 MainWindow");
                var mainWindow = new MainWindow();
                Trace.WriteLine("MainWindow 创建完成，进入消息循环");
                app.Run(mainWindow);

                try
                {
                    cts.Cancel();
                    machineLogRealtimeChannel?.Dispose();
                    if (machineLogTraceListener != null)
                    {
                        Trace.Listeners.Remove(machineLogTraceListener);
                        machineLogTraceListener.Dispose();
                    }
                    machineLogBuffer?.Dispose();
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
                Trace.WriteLine($"SELF_UPDATE: Begin BaseDir={baseDir}");
                var trustedKeys = Path.Combine(baseDir, "trusted_keys.pem");
                if (!File.Exists(trustedKeys))
                {
                    Trace.WriteLine($"SELF_UPDATE: trusted_keys.pem not found at {trustedKeys}");
                    return false;
                }

                Trace.WriteLine($"SELF_UPDATE: trusted_keys.pem found at {trustedKeys}");
                Trace.WriteLine("SELF_UPDATE: Enumerating removable drives");
                var nx = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && d.DriveType == DriveType.Removable && string.Equals(d.VolumeLabel, "NX_INS", StringComparison.OrdinalIgnoreCase));
                if (nx == null)
                {
                    Trace.WriteLine("SELF_UPDATE: NX_INS removable drive not found");
                    return false;
                }

                Trace.WriteLine($"SELF_UPDATE: NX_INS drive found at {nx.RootDirectory.FullName}");
                var root = nx.RootDirectory.FullName;
                var candidates = new[]
                {
                    Path.Combine(root, "manifest.json"),
                    Path.Combine(root, "updates", "manifest.json")
                };
                string manifestPath = candidates.FirstOrDefault(File.Exists);
                if (string.IsNullOrEmpty(manifestPath))
                {
                    Trace.WriteLine("SELF_UPDATE: manifest.json not found");
                    return false;
                }

                Trace.WriteLine($"SELF_UPDATE: manifest found at {manifestPath}");
                var sigPath = Path.Combine(Path.GetDirectoryName(manifestPath) ?? root, "manifest.sig");
                if (!File.Exists(sigPath))
                {
                    Trace.WriteLine($"SELF_UPDATE: manifest.sig not found at {sigPath}");
                    return false;
                }

                Trace.WriteLine($"SELF_UPDATE: manifest.sig found at {sigPath}");
                Trace.WriteLine("SELF_UPDATE: Verifying manifest signature");
                var ok = UpdateSecurity.VerifyManifestSignature(manifestPath, sigPath, trustedKeys);
                Trace.WriteLine($"SELF_UPDATE: Manifest signature verification result={ok}");
                if (!ok) return false;

                Trace.WriteLine("SELF_UPDATE: Loading manifest");
                var manifest = UpdateSecurity.LoadManifest(manifestPath);
                Trace.WriteLine($"SELF_UPDATE: Manifest loaded Type={manifest.type} Version={manifest.version}");
                if (!string.Equals(manifest.type, "app-update", StringComparison.OrdinalIgnoreCase))
                {
                    Trace.WriteLine("SELF_UPDATE: Manifest type is not app-update");
                    return false;
                }
                if (!UpdateSecurity.ValidateAppUpdatePayloadSize(manifest, out var totalPayloadBytes, out var payloadError))
                {
                    Trace.WriteLine($"SELF_UPDATE: {payloadError}");
                    return false;
                }
                Trace.WriteLine($"SELF_UPDATE: App-update payload size accepted Total={totalPayloadBytes} bytes");
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
                    Trace.WriteLine("SELF_UPDATE: Manifest time window parsing failed");
                    return false;
                }
                Trace.WriteLine($"SELF_UPDATE: TimeWindow Now={now:o} Exp={exp:o}");
                if (now > exp)
                {
                    Trace.WriteLine("SELF_UPDATE: Manifest expired");
                    return false;
                }
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
                Trace.WriteLine($"SELF_UPDATE: Staging directory ready at {staging}");
                foreach (var f in manifest.files)
                {
                    var src = Path.Combine(Path.GetDirectoryName(manifestPath) ?? root, f.path.Replace('/', Path.DirectorySeparatorChar));
                    Trace.WriteLine($"SELF_UPDATE: Verifying file {src}");
                    if (!UpdateSecurity.VerifyFileHash(src, f.sha256, f.size))
                    {
                        Trace.WriteLine($"SELF_UPDATE: File hash verification failed for {src}");
                        return false;
                    }
                    var dest = Path.Combine(staging, f.path.Replace('/', Path.DirectorySeparatorChar));
                    var destDir = Path.GetDirectoryName(dest) ?? staging;
                    if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
                    Trace.WriteLine($"SELF_UPDATE: Copying {src} -> {dest}");
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
                Trace.WriteLine($"SELF_UPDATE: Launching updater {psi.FileName}");
                var p = Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"SELF_UPDATE: Exception {ex}");
                return false;
            }
        }
    }
}
