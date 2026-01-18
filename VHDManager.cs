using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using System.Timers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using Microsoft.Management.Infrastructure;

namespace VHDMounter
{
    // 文件替换进度模型
    public class FileReplaceProgress
    {
        public string CurrentFileName { get; set; }
        public int FileIndex { get; set; }
        public int TotalFiles { get; set; }
        public long BytesCopied { get; set; }
        public long TotalBytes { get; set; }
        public double Percentage { get; set; }
    }

    public class VHDManager : IDisposable
    {
        private const string TARGET_DRIVE = "M:";
        private readonly string[] TARGET_KEYWORDS = { "SDEZ", "SDGB", "SDHJ", "SDDT", "SDHD" };
        private readonly string[] PROCESS_KEYWORDS = { "sinmai", "chusanapp", "mu3" };

        public event Action<string> StatusChanged;
        public event Action<FileReplaceProgress> ReplaceProgressChanged;
        public event Action<bool, string> BlockingChanged;
        public event Action GameCrashed;
        public event Action GameStarted;
        
        // 在核心流程中使用的状态更新：仅写入文件日志，默认不等待
        private async Task ShowStatusAndWait(string message, int milliseconds = 0)
        {
            try
            {
                StatusChanged?.Invoke(message);
                System.Diagnostics.Trace.WriteLine($"STATUS: {message}");
            }
            catch { }
            if (milliseconds > 0)
            {
                await Task.Delay(milliseconds);
            }
        }
        
        private static readonly HttpClient httpClient = new HttpClient();
        private string configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vhdmonter_config.ini");
        
        // 保护检查相关字段
        private Timer protectionTimer;
        private string machineId;
        private bool isProtectionCheckEnabled;
        private string protectionCheckUrl;
        private int protectionCheckInterval;
        private bool isBlocking = false;
        private bool protectionWasRunning = false;

        // 加密EVHD挂载进程（需保持常驻直到主程序退出）
        private Process evhdMountProcess;
        
        // 构造函数
        public VHDManager()
        {
            InitializeProtectionCheck();
        }
        
        // 初始化保护检查功能
        private void InitializeProtectionCheck()
        {
            try
            {
                var config = ReadConfig();
                
                // 读取Machine ID
                machineId = config.TryGetValue("MachineId", out var id) ? id : "MACHINE_001";
                
                // 读取保护检查配置
                isProtectionCheckEnabled = config.TryGetValue("EnableProtectionCheck", out var enableCheck) && 
                                         bool.TryParse(enableCheck, out var enabled) && enabled;
                
                protectionCheckUrl = config.TryGetValue("ProtectionCheckUrl", out var url) ? url : "http://localhost:8080/api/protect";
                
                protectionCheckInterval = config.TryGetValue("ProtectionCheckInterval", out var interval) && 
                                        int.TryParse(interval, out var intervalMs) ? intervalMs : 500;
                
                if (isProtectionCheckEnabled)
                {
                    StartProtectionCheck();
                    StatusChanged?.Invoke($"保护检查已启用 - Machine ID: {machineId}, 检查间隔: {protectionCheckInterval}ms");
                }
                else
                {
                    StatusChanged?.Invoke("保护检查功能已禁用");
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"初始化保护检查失败: {ex.Message}");
            }
        }
        
        // 启动保护检查定时器
        private void StartProtectionCheck()
        {
            if (protectionTimer != null)
            {
                protectionTimer.Stop();
                protectionTimer.Dispose();
            }
            
            protectionTimer = new Timer(protectionCheckInterval);
            protectionTimer.Elapsed += async (sender, e) => await CheckProtectionStatus();
            protectionTimer.AutoReset = true;
            protectionTimer.Start();
        }
        
        // 检查保护状态
        private async Task CheckProtectionStatus()
        {
            try
            {
                if (!isProtectionCheckEnabled || string.IsNullOrWhiteSpace(protectionCheckUrl))
                    return;
                
                var requestUrl = $"{protectionCheckUrl}?machineId={Uri.EscapeDataString(machineId)}";
                var response = await httpClient.GetAsync(requestUrl);
                
                if (response.IsSuccessStatusCode)
                {
                    var jsonContent = await response.Content.ReadAsStringAsync();
                    var jsonDoc = JsonDocument.Parse(jsonContent);
                    
                    if (jsonDoc.RootElement.TryGetProperty("protected", out var protectedElement) && 
                        protectedElement.GetBoolean())
                    {
                        StatusChanged?.Invoke($"收到保护信号 - Machine ID: {machineId}，正在执行自动关机...");
                        await ExecuteShutdown();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"保护状态检查失败: {ex.Message}");
            }
        }
        
        // 执行自动关机
        private async Task ExecuteShutdown()
        {
            try
            {
                StatusChanged?.Invoke("正在执行自动关机...");
                
                // 停止保护检查定时器
                if (protectionTimer != null)
                {
                    protectionTimer.Stop();
                    protectionTimer.Dispose();
                    protectionTimer = null;
                }
                
                // 执行关机命令
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "shutdown",
                        Arguments = "/s /t 0",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                
                process.Start();
                await process.WaitForExitAsync();
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"自动关机执行失败: {ex.Message}");
            }
        }
        
        // 停止保护检查
        public void StopProtectionCheck()
        {
            if (protectionTimer != null)
            {
                protectionTimer.Stop();
                protectionTimer.Dispose();
                protectionTimer = null;
            }
        }
        
        // 调试方法：检查特定文件是否符合条件
        public bool IsVHDFileValid(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    Debug.WriteLine($"文件不存在: {filePath}");
                    return false;
                }
                
                var fileName = Path.GetFileName(filePath).ToUpper();
                var isValid = TARGET_KEYWORDS.Any(keyword => fileName.Contains(keyword));
                
                Debug.WriteLine($"检查文件: {filePath}");
                Debug.WriteLine($"文件名: {fileName}");
                Debug.WriteLine($"是否包含关键词: {isValid}");
                Debug.WriteLine($"关键词: {string.Join(", ", TARGET_KEYWORDS)}");
                
                return isValid;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"检查文件时出错: {ex.Message}");
                return false;
            }
        }

        // 检查是否存在卷标为NX_INS的USB设备
        public DriveInfo FindNXInsUSBDrive()
        {
            try
            {
                var usbDrives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Removable);
                foreach (var drive in usbDrives)
                {
                    try
                    {
                        if (drive.VolumeLabel == "NX_INS")
                        {
                            StatusChanged?.Invoke($"找到NX_INS USB设备: {drive.Name}");
                            return drive;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"读取驱动器 {drive.Name} 卷标时出错: {ex.Message}");
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"查找NX_INS USB设备时出错: {ex.Message}");
                return null;
            }
        }

        // 从USB设备中扫描VHD/EVHD文件（仅根目录）
        public List<string> ScanUSBForVHDFiles(DriveInfo usbDrive)
        {
            StatusChanged?.Invoke($"正在扫描USB设备 {usbDrive.Name} 中的VHD/EVHD文件...");
            var vhdFiles = new List<string>();

            try
            {
                var rootVhd = Directory.GetFiles(usbDrive.RootDirectory.FullName, "*.vhd", SearchOption.TopDirectoryOnly)
                    .Where(f => TARGET_KEYWORDS.Any(keyword => Path.GetFileName(f).ToUpper().Contains(keyword)))
                    .ToList();
                var rootEvhd = Directory.GetFiles(usbDrive.RootDirectory.FullName, "*.evhd", SearchOption.TopDirectoryOnly)
                    .Where(f => TARGET_KEYWORDS.Any(keyword => Path.GetFileName(f).ToUpper().Contains(keyword)))
                    .ToList();

                vhdFiles.AddRange(rootVhd);
                vhdFiles.AddRange(rootEvhd);
                StatusChanged?.Invoke($"在USB设备中找到 {vhdFiles.Count} 个VHD/EVHD文件");
                
                foreach (var file in vhdFiles)
                {
                    Debug.WriteLine($"USB中找到文件: {file}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"扫描USB设备 {usbDrive.Name} 时出错: {ex.Message}");
                StatusChanged?.Invoke($"扫描USB设备时出错: {ex.Message}");
            }

            return vhdFiles;
        }

        // 替换本地VHD文件
        public async Task<bool> ReplaceLocalVHDFiles(List<string> usbVhdFiles, List<string> localVhdFiles)
        {
            if (usbVhdFiles.Count == 0 || localVhdFiles.Count == 0)
                return false;

            // Win11兼容性诊断
            DiagnoseWin11Issues();

            var usbRoot = Path.GetPathRoot(usbVhdFiles[0]);
            UpdateManifest manifest = TryLoadVhdDataManifest(usbRoot);
            bool anyReplaced = false;
            StatusChanged?.Invoke("正在替换本地VHD文件...");
            // 预扫描：构建可替换队列（排除占用或无法准备的项）
            var copyQueue = new List<(string usbFile, string localFile, string destPath)>();
            foreach (var usbFile in usbVhdFiles)
            {
                string usbFileName = Path.GetFileName(usbFile);
                string usbKeyword = ExtractKeyword(usbFileName);
                if (string.IsNullOrEmpty(usbKeyword))
                    continue;

                var usbExt = Path.GetExtension(usbFile).ToLowerInvariant();
                var matchingLocalFiles = localVhdFiles.Where(f =>
                {
                    var localName = Path.GetFileName(f);
                    var localExt = Path.GetExtension(f).ToLowerInvariant();
                    var sameKeyword = ExtractKeyword(localName) == usbKeyword;
                    var typeAllowed = localExt == usbExt || (usbExt == ".evhd" && localExt == ".vhd");
                    return sameKeyword && typeAllowed;
                }).ToList();

                foreach (var localFile in matchingLocalFiles)
                {
                    try
                    {
                        var localFileInfo = new FileInfo(localFile);
                        var usbFileInfo = new FileInfo(usbFile);
                        Debug.WriteLine($"本地文件属性: {localFileInfo.Attributes}, 只读: {localFileInfo.IsReadOnly}");
                        Debug.WriteLine($"USB文件属性: {usbFileInfo.Attributes}, 大小: {usbFileInfo.Length} bytes");
                        Debug.WriteLine($"操作系统版本: {Environment.OSVersion}");

                        if (IsFileInUse(localFile))
                        {
                            StatusChanged?.Invoke($"文件被占用，无法替换: {Path.GetFileName(localFile)}");
                            continue;
                        }

                        StatusChanged?.Invoke($"检查Win11兼容性: {Path.GetFileName(localFile)}");
                        if (!PrepareFileForReplacement(localFile))
                        {
                            StatusChanged?.Invoke($"无法准备文件进行替换: {Path.GetFileName(localFile)}");
                            continue;
                        }

                        var destDir = Path.GetDirectoryName(localFile);
                        var destPath = Path.Combine(destDir ?? string.Empty, Path.GetFileName(usbFile));
                        copyQueue.Add((usbFile, localFile, destPath));
                    }
                    catch (Exception ex)
                    {
                        StatusChanged?.Invoke($"预扫描处理失败: {Path.GetFileName(localFile)} - {ex.Message}");
                    }
                }
            }

            if (copyQueue.Count == 0)
            {
                StatusChanged?.Invoke("无可替换的文件或无需替换");
                return false;
            }

            if (manifest != null)
            {
                try
                {
                    var rootPath = Path.GetPathRoot(copyQueue[0].destPath);
                    var tagPath = System.IO.Path.Combine(rootPath ?? "", "resource_version.tag");
                    string localVersion = null;
                    bool tagExists = false;
                    if (!string.IsNullOrEmpty(rootPath))
                    {
                        try
                        {
                            tagExists = System.IO.File.Exists(tagPath);
                            if (tagExists) localVersion = System.IO.File.ReadAllText(tagPath).Trim();
                        }
                        catch { }
                    }
                    if (tagExists)
                    {
                        var cmp = string.Compare(localVersion ?? "", manifest.minVersion ?? "", StringComparison.Ordinal);
                        if (cmp > 0)
                        {
                            StatusChanged?.Invoke($"拒绝资源更新：当前版本高于清单的最小版本要求（local={localVersion}, min={manifest.minVersion}）");
                            return false;
                        }
                        if (cmp == 0)
                        {
                            StatusChanged?.Invoke($"跳过资源更新：当前版本等于清单的最小版本（{localVersion}）");
                            return false;
                        }
                    }
                }
                catch { }
                var ok = await VerifyFilesWithProgress(manifest, usbRoot, copyQueue.Select(x => x.usbFile).ToList(), copyQueue.Count);
                if (!ok)
                {
                    StatusChanged?.Invoke("资源文件校验失败，已取消更新");
                    return false;
                }
            }

            // 执行替换并上报进度
            for (int i = 0; i < copyQueue.Count; i++)
            {
                var item = copyQueue[i];
                try
                {
                    // 删除本地文件
                    StatusChanged?.Invoke($"正在删除本地文件: {Path.GetFileName(item.localFile)}");
                    if (File.Exists(item.localFile))
                        File.Delete(item.localFile);

                    int retryCount = 0;
                    while (File.Exists(item.localFile) && retryCount < 10)
                    {
                        await Task.Delay(100);
                        retryCount++;
                    }

                    if (File.Exists(item.localFile))
                    {
                        StatusChanged?.Invoke($"删除文件失败，文件仍然存在: {Path.GetFileName(item.localFile)}");
                        continue;
                    }

                    // 复制（带进度）
                    StatusChanged?.Invoke($"正在复制: {Path.GetFileName(item.usbFile)} -> {Path.GetFileName(item.destPath)}");
                    await CopyFileWithProgressAsync(item.usbFile, item.destPath, i + 1, copyQueue.Count, manifest != null);

                    // 验证大小
                    var usbFileInfo = new FileInfo(item.usbFile);
                    var newFileInfo = new FileInfo(item.destPath);
                    if (newFileInfo.Length != usbFileInfo.Length)
                    {
                        StatusChanged?.Invoke($"警告: 文件大小不匹配 - 原始: {usbFileInfo.Length}, 复制后: {newFileInfo.Length}");
                    }

                    anyReplaced = true;
                    StatusChanged?.Invoke($"替换完成: {Path.GetFileName(item.destPath)}");
                }
                catch (UnauthorizedAccessException ex)
                {
                    StatusChanged?.Invoke($"权限不足，无法替换文件: {Path.GetFileName(item.localFile)} - {ex.Message}");
                    Debug.WriteLine($"权限错误: {ex}");
                }
                catch (IOException ex)
                {
                    StatusChanged?.Invoke($"文件IO错误: {Path.GetFileName(item.localFile)} - {ex.Message}");
                    Debug.WriteLine($"IO错误: {ex}");
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke($"替换文件时出错: {Path.GetFileName(item.localFile)} - {ex.Message}");
                    Debug.WriteLine($"替换文件时出错: {ex}");
                }
            }

            if (anyReplaced && manifest != null)
            {
                try
                {
                    var rootPath = Path.GetPathRoot(copyQueue[0].destPath);
                    if (!string.IsNullOrEmpty(rootPath))
                    {
                        var tagPath = System.IO.Path.Combine(rootPath, "resource_version.tag");
                        System.IO.File.WriteAllText(tagPath, manifest.version ?? "");
                    }
                }
                catch { }
            }

            return anyReplaced;
        }

        // 当本地列表为空时，将USB中的VHD/EVHD复制到指定盘符根目录（默认D:\）
        public async Task<bool> CopyUsbFilesToDriveRoot(List<string> usbVhdFiles, string targetDriveLetter = "D")
        {
            try
            {
                if (usbVhdFiles == null || usbVhdFiles.Count == 0)
                {
                    StatusChanged?.Invoke("USB中未找到可复制的VHD/EVHD文件");
                    return false;
                }

                // Win11兼容性诊断
                DiagnoseWin11Issues();

                var rootPath = targetDriveLetter + ":\\";
                if (!System.IO.Directory.Exists(rootPath))
                {
                    StatusChanged?.Invoke($"目标盘 {targetDriveLetter}: 不存在，无法复制");
                    return false;
                }

                StatusChanged?.Invoke($"正在将USB中的VHD/EVHD复制到 {rootPath} 根目录...");

                var usbRoot = Path.GetPathRoot(usbVhdFiles[0]);
                var manifest = TryLoadVhdDataManifest(usbRoot);
                if (manifest != null)
                {
                    var tagPath = System.IO.Path.Combine(rootPath, "resource_version.tag");
                    string localVersion = null;
                    bool tagExists = false;
                    try
                    {
                        tagExists = System.IO.File.Exists(tagPath);
                        if (tagExists) localVersion = System.IO.File.ReadAllText(tagPath).Trim();
                    }
                    catch { }
                    if (tagExists)
                    {
                        var cmp = string.Compare(localVersion ?? "", manifest.minVersion ?? "", StringComparison.Ordinal);
                        if (cmp > 0)
                        {
                            StatusChanged?.Invoke($"拒绝资源更新：当前版本高于清单的最小版本要求（local={localVersion}, min={manifest.minVersion}）");
                            return false;
                        }
                        if (cmp == 0)
                        {
                            StatusChanged?.Invoke($"跳过资源更新：当前版本等于清单的最小版本（{localVersion}）");
                            return false;
                        }
                    }
                }

                // 构建复制队列（目标为根目录，按文件名放置）
                var copyQueue = new List<(string usbFile, string destPath)>();
                foreach (var usbFile in usbVhdFiles)
                {
                    try
                    {
                        var name = System.IO.Path.GetFileName(usbFile);
                        var destPath = System.IO.Path.Combine(rootPath, name);

                        // 若目标已存在，尝试准备并删除后覆盖
                        if (System.IO.File.Exists(destPath))
                        {
                            if (!PrepareFileForReplacement(destPath))
                            {
                                StatusChanged?.Invoke($"无法准备现有文件进行覆盖: {name}");
                                continue;
                            }
                            try
                            {
                                System.IO.File.Delete(destPath);
                            }
                            catch (Exception ex)
                            {
                                StatusChanged?.Invoke($"删除现有文件失败: {name} - {ex.Message}");
                                continue;
                            }
                        }

                        copyQueue.Add((usbFile, destPath));
                    }
                    catch (Exception ex)
                    {
                        StatusChanged?.Invoke($"预处理复制队列失败: {System.IO.Path.GetFileName(usbFile)} - {ex.Message}");
                    }
                }

                if (copyQueue.Count == 0)
                {
                    StatusChanged?.Invoke("无可复制的文件");
                    return false;
                }

                if (manifest != null)
                {
                    var ok = await VerifyFilesWithProgress(manifest, usbRoot, copyQueue.Select(x => x.usbFile).ToList(), copyQueue.Count);
                    if (!ok)
                    {
                        StatusChanged?.Invoke("资源文件校验失败，已取消更新");
                        return false;
                    }
                }

                bool anyCopied = false;
                for (int i = 0; i < copyQueue.Count; i++)
                {
                    var item = copyQueue[i];
                    try
                    {
                        StatusChanged?.Invoke($"正在复制: {System.IO.Path.GetFileName(item.usbFile)} -> {System.IO.Path.GetFileName(item.destPath)}");
                        await CopyFileWithProgressAsync(item.usbFile, item.destPath, i + 1, copyQueue.Count, manifest != null);

                        // 验证大小
                        var usbInfo = new System.IO.FileInfo(item.usbFile);
                        var newInfo = new System.IO.FileInfo(item.destPath);
                        if (newInfo.Length != usbInfo.Length)
                        {
                            StatusChanged?.Invoke($"警告: 文件大小不匹配 - 原始: {usbInfo.Length}, 复制后: {newInfo.Length}");
                        }

                        anyCopied = true;
                        StatusChanged?.Invoke($"复制完成: {System.IO.Path.GetFileName(item.destPath)}");
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        StatusChanged?.Invoke($"权限不足，无法复制文件: {System.IO.Path.GetFileName(item.usbFile)} - {ex.Message}");
                    }
                    catch (System.IO.IOException ex)
                    {
                        StatusChanged?.Invoke($"文件IO错误: {System.IO.Path.GetFileName(item.usbFile)} - {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        StatusChanged?.Invoke($"复制文件时出错: {System.IO.Path.GetFileName(item.usbFile)} - {ex.Message}");
                    }
                }

                if (anyCopied && manifest != null)
                {
                    try
                    {
                        var tagPath = System.IO.Path.Combine(rootPath, "resource_version.tag");
                        System.IO.File.WriteAllText(tagPath, manifest.version ?? "");
                    }
                    catch { }
                }

                return anyCopied;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"复制USB文件到根目录失败: {ex.Message}");
                return false;
            }
        }

        // 带进度的文件复制
        private async Task CopyFileWithProgressAsync(string sourcePath, string destPath, int fileIndex, int totalFiles, bool scaleToSecondHalf = false)
        {
            var buffer = new byte[1024 * 1024];
            long totalBytes = 0;
            long copiedBytes = 0;

            try
            {
                var destDir = Path.GetDirectoryName(destPath) ?? string.Empty;
                var tempName = Path.GetFileName(destPath) + ".tmp";
                var tempPath = Path.Combine(destDir, tempName);
                using (var src = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var dst = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    totalBytes = src.Length;
                    int read;
                    while ((read = await src.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await dst.WriteAsync(buffer, 0, read);
                        copiedBytes += read;

                        var progress = new FileReplaceProgress
                        {
                            CurrentFileName = Path.GetFileName(sourcePath),
                            FileIndex = fileIndex,
                            TotalFiles = totalFiles,
                            BytesCopied = copiedBytes,
                            TotalBytes = totalBytes,
                            Percentage = totalBytes > 0
                                ? (scaleToSecondHalf ? (50.0 + (copiedBytes * 50.0 / totalBytes)) : (copiedBytes * 100.0 / totalBytes))
                                : 0
                        };
                        ReplaceProgressChanged?.Invoke(progress);
                    }
                    await dst.FlushAsync();
                }
                bool replaced = FileReplaceUtil.AtomicReplace(tempPath, destPath);
                if (!replaced)
                {
                    try { System.IO.File.Delete(tempPath); } catch { }
                    throw new IOException("Atomic replace failed");
                }
            }
            catch
            {
                // 出错时也通知一次，避免UI卡住
                var progress = new FileReplaceProgress
                {
                    CurrentFileName = Path.GetFileName(sourcePath),
                    FileIndex = fileIndex,
                    TotalFiles = totalFiles,
                    BytesCopied = copiedBytes,
                    TotalBytes = totalBytes,
                    Percentage = totalBytes > 0
                        ? (scaleToSecondHalf ? (50.0 + (copiedBytes * 50.0 / totalBytes)) : (copiedBytes * 100.0 / totalBytes))
                        : 0
                };
                ReplaceProgressChanged?.Invoke(progress);
                throw;
            }
        }

        private UpdateManifest TryLoadVhdDataManifest(string usbRoot)
        {
            try
            {
                var root = usbRoot;
                var candidates = new[]
                {
                    System.IO.Path.Combine(root, "manifest.json"),
                    System.IO.Path.Combine(root, "updates", "manifest.json")
                };
                string manifestPath = candidates.FirstOrDefault(System.IO.File.Exists);
                if (string.IsNullOrEmpty(manifestPath)) return null;
                var sigPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(manifestPath) ?? root, "manifest.sig");
                if (!System.IO.File.Exists(sigPath)) return null;
                var trustedKeys = System.IO.Path.Combine(AppContext.BaseDirectory, "trusted_keys.pem");
                if (!System.IO.File.Exists(trustedKeys)) return null;
                var ok = UpdateSecurity.VerifyManifestSignature(manifestPath, sigPath, trustedKeys);
                if (!ok) return null;
                var manifest = UpdateSecurity.LoadManifest(manifestPath);
                if (!string.Equals(manifest.type, "vhd-data", StringComparison.OrdinalIgnoreCase)) return null;
                return manifest;
            }
            catch
            {
                return null;
            }
        }

        private async Task<bool> VerifyFilesWithProgress(UpdateManifest manifest, string usbRoot, List<string> usbFiles, int totalFiles)
        {
            try
            {
                var dict = new Dictionary<string, UpdateManifestFile>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in manifest.files)
                {
                    var key = System.IO.Path.GetFileName(f.path);
                    if (!string.IsNullOrEmpty(key) && !dict.ContainsKey(key)) dict[key] = f;
                }
                for (int i = 0; i < usbFiles.Count; i++)
                {
                    var usbFile = usbFiles[i];
                    var name = System.IO.Path.GetFileName(usbFile);
                    if (!dict.TryGetValue(name, out var mf))
                    {
                        StatusChanged?.Invoke($"校验失败：清单中缺少文件 {name}");
                        return false;
                    }
                    var src = System.IO.Path.Combine(usbRoot, (mf.path ?? name).Replace('/', System.IO.Path.DirectorySeparatorChar));
                    if (!System.IO.File.Exists(src)) src = usbFile;
                    if (!System.IO.File.Exists(src))
                    {
                        StatusChanged?.Invoke($"校验失败：未找到文件 {name}");
                        return false;
                    }
                    using var sha = SHA256.Create();
                    using var fs = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var total = fs.Length;
                    var buf = new byte[1024 * 1024];
                    long processed = 0;
                    int r;
                    while ((r = await fs.ReadAsync(buf, 0, buf.Length)) > 0)
                    {
                        sha.TransformBlock(buf, 0, r, null, 0);
                        processed += r;
                        var progress = new FileReplaceProgress
                        {
                            CurrentFileName = name,
                            FileIndex = i + 1,
                            TotalFiles = totalFiles,
                            BytesCopied = processed,
                            TotalBytes = total,
                            Percentage = total > 0 ? (processed * 50.0 / total) : 0
                        };
                        ReplaceProgressChanged?.Invoke(progress);
                    }
                    sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    var hex = BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
                    if (!string.Equals(hex, mf.sha256?.ToLowerInvariant(), StringComparison.Ordinal))
                    {
                        StatusChanged?.Invoke($"校验失败：{name} 哈希不匹配");
                        return false;
                    }
                    if (mf.size > 0 && mf.size != total)
                    {
                        StatusChanged?.Invoke($"校验失败：{name} 大小不匹配");
                        return false;
                    }
                }
                StatusChanged?.Invoke("资源文件校验完成");
                return true;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"资源校验过程中发生错误: {ex.Message}");
                return false;
            }
        }

        // 提取VHD文件名中的关键词部分
        private string ExtractKeyword(string fileName)
        {
            foreach (var keyword in TARGET_KEYWORDS)
            {
                if (fileName.ToUpper().Contains(keyword))
                    return keyword;
            }
            return string.Empty;
        }
        
        // 检查文件是否被占用
        private bool IsFileInUse(string filePath)
        {
            try
            {
                using (FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    return false;
                }
            }
            catch (IOException)
            {
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
        }
        
        // Win11兼容性检查和修复
        private bool PrepareFileForReplacement(string filePath)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                
                // 检查文件是否存在
                if (!fileInfo.Exists)
                    return true;
                
                // 移除只读属性
                if (fileInfo.IsReadOnly)
                {
                    StatusChanged?.Invoke($"移除只读属性: {fileInfo.Name}");
                    fileInfo.IsReadOnly = false;
                }
                
                // 移除隐藏和系统属性（Win11可能设置这些属性）
                if ((fileInfo.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden)
                {
                    StatusChanged?.Invoke($"移除隐藏属性: {fileInfo.Name}");
                    fileInfo.Attributes &= ~FileAttributes.Hidden;
                }
                
                if ((fileInfo.Attributes & FileAttributes.System) == FileAttributes.System)
                {
                    StatusChanged?.Invoke($"移除系统属性: {fileInfo.Name}");
                    fileInfo.Attributes &= ~FileAttributes.System;
                }
                
                // 检查是否有写入权限
                try
                {
                    using (var stream = File.OpenWrite(filePath))
                    {
                        // 如果能打开写入流，说明有权限
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    StatusChanged?.Invoke($"权限不足，无法写入文件: {fileInfo.Name}");
                    return false;
                }
                
                return true;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"准备文件时出错: {ex.Message}");
                Debug.WriteLine($"PrepareFileForReplacement错误: {ex}");
                return false;
            }
        }
        
        // 检测Win11并提供诊断信息
        private void DiagnoseWin11Issues()
        {
            try
            {
                var osVersion = Environment.OSVersion;
                var isWin11 = osVersion.Version.Major >= 10 && osVersion.Version.Build >= 22000;
                
                StatusChanged?.Invoke($"操作系统: {osVersion.VersionString}");
                Debug.WriteLine($"操作系统详细信息: {osVersion}");
                Debug.WriteLine($"是否为Win11: {isWin11}");
                
                if (isWin11)
                {
                    StatusChanged?.Invoke("检测到Windows 11，启用兼容性模式");
                    
                    // 检查UAC状态
                    try
                    {
                        var principal = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent());
                        bool isAdmin = principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                        
                        StatusChanged?.Invoke($"管理员权限: {(isAdmin ? "是" : "否")}");
                        Debug.WriteLine($"当前进程是否以管理员身份运行: {isAdmin}");
                        
                        if (!isAdmin)
                        {
                            StatusChanged?.Invoke("建议：以管理员身份运行程序以避免权限问题");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"检查管理员权限时出错: {ex}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Win11诊断时出错: {ex}");
            }
        }
        
        // 读取配置文件
        private Dictionary<string, string> ReadConfig()
        {
            var config = new Dictionary<string, string>();
            
            try
            {
                if (!File.Exists(configFilePath))
                {
                    StatusChanged?.Invoke("配置文件不存在，使用默认设置");
                    return config;
                }
                
                var lines = File.ReadAllLines(configFilePath);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";") || line.StartsWith("["))
                        continue;
                        
                    var parts = line.Split('=', 2);
                    if (parts.Length == 2)
                    {
                        config[parts[0].Trim()] = parts[1].Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"读取配置文件失败: {ex.Message}");
            }
            
            return config;
        }
        
        // 远程获取VHD选择
        public async Task<string> GetRemoteVHDSelection()
        {
            try
            {
                var config = ReadConfig();
                
                if (!config.TryGetValue("EnableRemoteSelection", out var enableRemote) || 
                    !bool.TryParse(enableRemote, out var isEnabled) || !isEnabled)
                {
                    StatusChanged?.Invoke("远程VHD选择功能已禁用");
                    return null;
                }
                
                if (!config.TryGetValue("BootImageSelectUrl", out var url) || string.IsNullOrWhiteSpace(url))
                {
                    StatusChanged?.Invoke("未配置BootImageSelectUrl");
                    return null;
                }
                
                // 添加Machine ID参数
                var requestUrl = $"{url}?machineId={Uri.EscapeDataString(machineId)}";
                StatusChanged?.Invoke($"正在从远程获取VHD选择: {requestUrl}");
                
                var response = await httpClient.GetAsync(requestUrl);
                if (!response.IsSuccessStatusCode)
                {
                    StatusChanged?.Invoke($"远程请求失败: {response.StatusCode}");
                    return null;
                }
                
                var jsonContent = await response.Content.ReadAsStringAsync();
                var jsonDoc = JsonDocument.Parse(jsonContent);
                
                if (jsonDoc.RootElement.TryGetProperty("BootImageSelected", out var bootImageElement))
                {
                    var selectedKeyword = bootImageElement.GetString();
                    StatusChanged?.Invoke($"远程选择的VHD关键词: {selectedKeyword} (Machine ID: {machineId})");
                    return selectedKeyword;
                }
                else
                {
                    StatusChanged?.Invoke("远程响应中未找到BootImageSelected字段");
                    return null;
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"远程获取VHD选择失败: {ex.Message}");
                return null;
            }
        }

        // 远程获取EVHD密码（RSA封装信封，客户端用TPM私钥解密）
        public async Task<string> GetEvhdPasswordFromServer()
        {
            try
            {
                var config = ReadConfig();
                string url = null;
                if (config.TryGetValue("EvhdEnvelopeUrl", out var envelopeUrl) && !string.IsNullOrWhiteSpace(envelopeUrl))
                {
                    url = envelopeUrl;
                }
                else if (config.TryGetValue("EvhdPasswordUrl", out var legacyUrl) && !string.IsNullOrWhiteSpace(legacyUrl))
                {
                    url = legacyUrl.Replace("evhd-password", "evhd-envelope");
                }
                else if (config.TryGetValue("BootImageSelectUrl", out var bootUrl) && !string.IsNullOrWhiteSpace(bootUrl))
                {
                    url = bootUrl.Replace("boot-image-select", "evhd-envelope");
                }
                else
                {
                    StatusChanged?.Invoke("未配置EvhdEnvelopeUrl/EvhdPasswordUrl或BootImageSelectUrl");
                    return null;
                }

                var requestUrl = $"{url}?machineId={Uri.EscapeDataString(machineId)}";
                StatusChanged?.Invoke($"正在从远程获取EVHD封装信封: {requestUrl}");
                var response = await httpClient.GetAsync(requestUrl);

                if (response.IsSuccessStatusCode)
                {
                    var jsonContent = await response.Content.ReadAsStringAsync();
                    var jsonDoc = JsonDocument.Parse(jsonContent);
                    if (jsonDoc.RootElement.TryGetProperty("ciphertext", out var cipherElement))
                    {
                        var b64 = cipherElement.GetString();
                        if (!string.IsNullOrWhiteSpace(b64))
                        {
                            var rsa = EnsureOrCreateTpmRsa(machineId);
                            var cipherBytes = Convert.FromBase64String(b64);
                            var plainBytes = rsa.Decrypt(cipherBytes, RSAEncryptionPadding.OaepSHA1);
                            var plain = Encoding.UTF8.GetString(plainBytes);
                            return plain;
                        }
                    }
                    StatusChanged?.Invoke("远程响应中未找到ciphertext字段");
                    return null;
                }
                else
                {
                    var body = await response.Content.ReadAsStringAsync();
                    string err = null;
                    try
                    {
                        var doc = JsonDocument.Parse(body);
                        if (doc.RootElement.TryGetProperty("error", out var ee)) err = ee.GetString();
                    }
                    catch { }

                    if ((int)response.StatusCode == 403)
                    {
                        StatusChanged?.Invoke(err ?? "机台密钥未审批或已吊销");
                        return null;
                    }
                    else if ((int)response.StatusCode == 400)
                    {
                        // 仅在未注册公钥时执行注册，然后重试一次
                        if ((err ?? string.Empty).Contains("未注册公钥"))
                        {
                            var rsa = EnsureOrCreateTpmRsa(machineId);
                            var pubPem = ExportPublicKeyPem(rsa);
                            StatusChanged?.Invoke("机台未注册公钥，正在注册后重试...");
                            await RegisterPublicKeyAsync(url, machineId, pubPem);

                            var retry = await httpClient.GetAsync(requestUrl);
                            if (retry.IsSuccessStatusCode)
                            {
                                var jsonContent = await retry.Content.ReadAsStringAsync();
                                var jsonDoc = JsonDocument.Parse(jsonContent);
                                if (jsonDoc.RootElement.TryGetProperty("ciphertext", out var cipherElement))
                                {
                                    var b64 = cipherElement.GetString();
                                    if (!string.IsNullOrWhiteSpace(b64))
                                    {
                                        var cipherBytes = Convert.FromBase64String(b64);
                                        var plainBytes = rsa.Decrypt(cipherBytes, RSAEncryptionPadding.OaepSHA1);
                                        var plain = Encoding.UTF8.GetString(plainBytes);
                                        return plain;
                                    }
                                }
                            }
                            StatusChanged?.Invoke("重试后仍未下发ciphertext");
                            return null;
                        }
                        StatusChanged?.Invoke(err ?? "获取EVHD封装信封失败: 400");
                        return null;
                    }
                    else
                    {
                        StatusChanged?.Invoke($"获取EVHD封装信封失败: {response.StatusCode}");
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"远程获取EVHD密码失败: {ex.Message}");
                return null;
            }
        }

        // 远程获取EVHD密码（失败则阻塞并每秒轮询状态，仅在400未注册时注册公钥）
        public async Task<string> GetEvhdPasswordFromServerWithBlockingRetry()
        {
            try
            {
                var config = ReadConfig();
                string url = null;
                if (config.TryGetValue("EvhdEnvelopeUrl", out var envelopeUrl) && !string.IsNullOrWhiteSpace(envelopeUrl))
                {
                    url = envelopeUrl;
                }
                else if (config.TryGetValue("EvhdPasswordUrl", out var legacyUrl) && !string.IsNullOrWhiteSpace(legacyUrl))
                {
                    url = legacyUrl.Replace("evhd-password", "evhd-envelope");
                }
                else if (config.TryGetValue("BootImageSelectUrl", out var bootUrl) && !string.IsNullOrWhiteSpace(bootUrl))
                {
                    url = bootUrl.Replace("boot-image-select", "evhd-envelope");
                }
                else
                {
                    StatusChanged?.Invoke("未配置EvhdEnvelopeUrl/EvhdPasswordUrl或BootImageSelectUrl");
                    return null;
                }

                // 准备TPM RSA密钥（不主动注册，仅在400未注册公钥时注册）
                var rsa = EnsureOrCreateTpmRsa(machineId);
                var pubPem = ExportPublicKeyPem(rsa);

                var requestUrl = $"{url}?machineId={Uri.EscapeDataString(machineId)}";
                StatusChanged?.Invoke($"正在从远程获取EVHD封装信封: {requestUrl}");
                var response = await httpClient.GetAsync(requestUrl);
                if (response.IsSuccessStatusCode)
                {
                    var jsonContent = await response.Content.ReadAsStringAsync();
                    var jsonDoc = JsonDocument.Parse(jsonContent);
                    if (jsonDoc.RootElement.TryGetProperty("ciphertext", out var cipherElement))
                    {
                        var b64 = cipherElement.GetString();
                        if (!string.IsNullOrWhiteSpace(b64))
                        {
                            var cipherBytes = Convert.FromBase64String(b64);
                            var plainBytes = rsa.Decrypt(cipherBytes, RSAEncryptionPadding.OaepSHA1);
                            var plain = Encoding.UTF8.GetString(plainBytes);
                            return plain;
                        }
                    }
                }

                // 进入阻塞模式并开始轮询
                EnterBlockingMode("解密参数下发异常/机台未注册，正在检查注册状态...");
                while (true)
                {
                    try
                    {
                        // 每次轮询直接拉取封装信封，仅在400未注册时执行注册
                        var pollResponse = await httpClient.GetAsync(requestUrl);
                        var body = await pollResponse.Content.ReadAsStringAsync();

                        if (pollResponse.IsSuccessStatusCode)
                        {
                            string b64 = null;
                            try
                            {
                                var doc = JsonDocument.Parse(body);
                                if (doc.RootElement.TryGetProperty("ciphertext", out var ce))
                                {
                                    b64 = ce.GetString();
                                }
                            }
                            catch { }

                            if (!string.IsNullOrWhiteSpace(b64))
                            {
                                var cipherBytes = Convert.FromBase64String(b64);
                                var plainBytes = rsa.Decrypt(cipherBytes, RSAEncryptionPadding.OaepSHA1);
                                var plain = Encoding.UTF8.GetString(plainBytes);
                                ExitBlockingMode("已下发EVHD加密信封");
                                StatusChanged?.Invoke("已下发EVHD加密信封");
                                return plain;
                            }
                            else
                            {
                                StatusChanged?.Invoke("机台已注册，等待管理员设置EVHD密码...");
                            }
                        }
                        else
                        {
                            string err = null;
                            try
                            {
                                var doc = JsonDocument.Parse(body);
                                if (doc.RootElement.TryGetProperty("error", out var ee)) err = ee.GetString();
                            }
                            catch { }

                            if ((int)pollResponse.StatusCode == 403)
                            {
                                StatusChanged?.Invoke(err ?? "机台密钥未审批或已吊销，等待审批...");
                            }
                            else if ((int)pollResponse.StatusCode == 404)
                            {
                                StatusChanged?.Invoke(err ?? "机台不存在，等待注册...");
                            }
                            else if ((int)pollResponse.StatusCode == 400)
                            {
                                if ((err ?? string.Empty).Contains("未注册公钥"))
                                {
                                    StatusChanged?.Invoke("机台未注册公钥，已提交注册，等待服务生效...");
                                    await RegisterPublicKeyAsync(url, machineId, pubPem);
                                }
                                else
                                {
                                    StatusChanged?.Invoke(err ?? "获取EVHD封装信封失败: 400");
                                }
                            }
                            else
                            {
                                StatusChanged?.Invoke($"获取EVHD封装信封失败: {pollResponse.StatusCode}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        StatusChanged?.Invoke($"重试中: {ex.Message}");
                    }

                    await Task.Delay(1000);
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"远程获取EVHD密码失败: {ex.Message}");
                return null;
            }
        }

        private void EnterBlockingMode(string message)
        {
            try
            {
                isBlocking = true;
                protectionWasRunning = isProtectionCheckEnabled && protectionTimer != null;
                if (protectionTimer != null)
                {
                    protectionTimer.Stop();
                }
                BlockingChanged?.Invoke(true, message);
                StatusChanged?.Invoke(message);
            }
            catch { }
        }

        private void ExitBlockingMode(string message = null)
        {
            try
            {
                isBlocking = false;
                if (protectionWasRunning && protectionTimer != null)
                {
                    protectionTimer.Start();
                }
                BlockingChanged?.Invoke(false, message ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(message))
                {
                    StatusChanged?.Invoke(message);
                }
            }
            catch { }
        }

        private RSACng EnsureOrCreateTpmRsa(string machineId)
        {
            string keyName = $"VHDMounterKey_{machineId}";
            CngKey key = null;
            try
            {
                key = CngKey.Open(keyName, CngProvider.MicrosoftPlatformCryptoProvider);
            }
            catch
            {
                var creation = new CngKeyCreationParameters
                {
                    Provider = CngProvider.MicrosoftPlatformCryptoProvider,
                    KeyUsage = CngKeyUsages.AllUsages,
                    ExportPolicy = CngExportPolicies.None,
                    KeyCreationOptions = CngKeyCreationOptions.None
                };
                key = CngKey.Create(CngAlgorithm.Rsa, keyName, creation);
            }
            var rsa = new RSACng(key);
            if (rsa.KeySize < 2048) rsa.KeySize = 2048;
            return rsa;
        }

        private string ExportPublicKeyPem(RSA rsa)
        {
            var spki = rsa.ExportSubjectPublicKeyInfo();
            var b64 = Convert.ToBase64String(spki);
            var sb = new StringBuilder();
            sb.AppendLine("-----BEGIN PUBLIC KEY-----");
            for (int i = 0; i < b64.Length; i += 64)
            {
                sb.AppendLine(b64.Substring(i, Math.Min(64, b64.Length - i)));
            }
            sb.AppendLine("-----END PUBLIC KEY-----");
            return sb.ToString();
        }

        private async Task RegisterPublicKeyAsync(string envelopeUrl, string machineId, string pubkeyPem)
        {
            try
            {
                var baseUrl = envelopeUrl;
                var idx = baseUrl.IndexOf("/api/", StringComparison.OrdinalIgnoreCase);
                if (idx > 0) baseUrl = baseUrl.Substring(0, idx);
                var regUrl = $"{baseUrl}/api/machines/{Uri.EscapeDataString(machineId)}/keys";
                var payload = new
                {
                    keyId = $"VHDMounterKey_{machineId}",
                    keyType = "RSA",
                    pubkeyPem = pubkeyPem
                };
                var json = JsonSerializer.Serialize(payload);
                var req = new StringContent(json, Encoding.UTF8, "application/json");
                var res = await httpClient.PostAsync(regUrl, req);
                // 非严格要求成功（可能已注册），仅记录状态
                StatusChanged?.Invoke(res.IsSuccessStatusCode ? "已注册机台公钥" : $"注册公钥失败: {res.StatusCode}");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"注册公钥异常: {ex.Message}");
            }
        }

        // 挂载EVHD到N盘，并从其中提取解密后的VHD再挂载
        public async Task<bool> MountEVHDAndAttachDecryptedVHD(string evhdPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(evhdPath) || !File.Exists(evhdPath))
                {
                    await ShowStatusAndWait("挂载失败: EVHD文件不存在或路径无效");
                    return false;
                }

                // 获取密码
                var password = await GetEvhdPasswordFromServerWithBlockingRetry();
                if (string.IsNullOrEmpty(password))
                {
                    await ShowStatusAndWait("未能获取EVHD密码，挂载终止");
                    return false;
                }

                // 调用加密挂载工具
                await ShowStatusAndWait("正在调用加密VHD挂载工具...");
                var toolPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "encrypted-vhd-mount.exe");
                var fileName = File.Exists(toolPath) ? toolPath : "encrypted-vhd-mount.exe";

                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                // 使用 ArgumentList 逐项传参，确保密码中的特殊符号与空格不会造成截断
                psi.ArgumentList.Add("/p");
                psi.ArgumentList.Add(password);
                psi.ArgumentList.Add(evhdPath);
                psi.ArgumentList.Add("N:");

                var proc = new Process { StartInfo = psi };

                proc.Start();
                // 保存引用，确保工具在主程序生命周期内持续运行
                evhdMountProcess = proc;

                // 仅以检测到 N: 盘出现作为挂载成功依据（工具应保持常驻不退出）
                var timeoutMs = 60000;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                bool nReady = false;
                var nRoot = "N:";
                while (sw.ElapsedMilliseconds < timeoutMs)
                {
                    // 成功条件：仅检测到 N: 盘存在
                    if (Directory.Exists(nRoot))
                    {
                        nReady = true;
                        break;
                    }

                    await Task.Delay(500);
                }

                // 未检测到 N:，视为超时失败（不主动结束加密挂载进程）
                if (!nReady)
                {
                    await ShowStatusAndWait("EVHD挂载超时（超过60秒），未检测到N盘");
                    return false;
                }

                // 在N盘根目录查找解密后的VHD（若刚出现，给它一点准备时间）
                await ShowStatusAndWait("已检测到N盘，继续挂载解密后的VHD...");
                if (!Directory.Exists(nRoot))
                {
                    await Task.Delay(1000);
                }
                if (!Directory.Exists(nRoot))
                {
                    await ShowStatusAndWait("未找到N盘，EVHD挂载可能失败");
                    return false;
                }

                var keyword = ExtractKeyword(Path.GetFileName(evhdPath));
                var decryptedVhds = Directory.GetFiles(nRoot, "*.vhd", SearchOption.TopDirectoryOnly)
                    .Where(f => string.IsNullOrEmpty(keyword) || ExtractKeyword(Path.GetFileName(f)) == keyword)
                    .ToList();

                if (decryptedVhds.Count == 0)
                {
                    // 放宽匹配，随便拿一个
                    decryptedVhds = Directory.GetFiles(nRoot, "*.vhd", SearchOption.TopDirectoryOnly).ToList();
                }

                if (decryptedVhds.Count == 0)
                {
                    await ShowStatusAndWait("N盘中未找到解密后的VHD文件");
                    return false;
                }

                var vhdToAttach = decryptedVhds[0];
                await ShowStatusAndWait($"找到解密后的VHD: {Path.GetFileName(vhdToAttach)}，准备挂载...");

                // 使用现有MountVHD挂载到目标盘符
                return await MountVHD(vhdToAttach);
            }
            catch (Exception ex)
            {
                await ShowStatusAndWait($"挂载EVHD失败: {ex.Message}");
                return false;
            }
        }

        // 结束加密EVHD挂载进程（在主程序退出时调用）
        public void StopEncryptedEvhdMount()
        {
            try
            {
                if (evhdMountProcess != null && !evhdMountProcess.HasExited)
                {
                    StatusChanged?.Invoke("正在结束加密VHD挂载工具...");
                    try
                    {
                        // 尝试优雅结束
                        if (!evhdMountProcess.CloseMainWindow())
                        {
                            evhdMountProcess.Kill();
                        }
                    }
                    catch
                    {
                        try { evhdMountProcess.Kill(); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"结束加密VHD挂载工具时发生异常: {ex.Message}");
            }
            finally
            {
                evhdMountProcess = null;
            }
        }
        
        // 根据关键词查找对应的VHD文件
        public string FindVHDByKeyword(List<string> vhdFiles, string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword) || vhdFiles == null || vhdFiles.Count == 0)
                return null;
                
            var matchingFile = vhdFiles.FirstOrDefault(f => 
                ExtractKeyword(Path.GetFileName(f)).Equals(keyword, StringComparison.OrdinalIgnoreCase));
                
            if (matchingFile != null)
            {
                StatusChanged?.Invoke($"找到匹配关键词 '{keyword}' 的VHD文件: {Path.GetFileName(matchingFile)}");
            }
            else
            {
                StatusChanged?.Invoke($"未找到匹配关键词 '{keyword}' 的VHD文件");
            }
            
            return matchingFile;
        }

        public async Task<List<string>> ScanForVHDFiles()
        {
            StatusChanged?.Invoke("正在扫描VHD/EVHD文件...");
            var vhdFiles = new List<string>();

            await Task.Run(() =>
            {
                var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed);
                
                foreach (var drive in drives)
                {
                    try
                    {
                        StatusChanged?.Invoke($"正在扫描驱动器 {drive.Name} 根目录...");
                        
                        // 只扫描根目录：同时包含VHD与EVHD
                        var rootVhd = Directory.GetFiles(drive.RootDirectory.FullName, "*.vhd", SearchOption.TopDirectoryOnly)
                            .Where(f => TARGET_KEYWORDS.Any(keyword => Path.GetFileName(f).ToUpper().Contains(keyword)))
                            .ToList();
                        var rootEvhd = Directory.GetFiles(drive.RootDirectory.FullName, "*.evhd", SearchOption.TopDirectoryOnly)
                            .Where(f => TARGET_KEYWORDS.Any(keyword => Path.GetFileName(f).ToUpper().Contains(keyword)))
                            .ToList();
                        
                        vhdFiles.AddRange(rootVhd);
                        vhdFiles.AddRange(rootEvhd);
                        Debug.WriteLine($"在 {drive.Name} 根目录找到 {rootVhd.Count + rootEvhd.Count} 个符合条件的VHD/EVHD文件");
                        
                        foreach (var file in rootVhd.Concat(rootEvhd))
                        {
                            Debug.WriteLine($"  找到: {Path.GetFileName(file)}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"扫描驱动器 {drive.Name} 时出错: {ex.Message}");
                        StatusChanged?.Invoke($"扫描驱动器 {drive.Name} 时出错: {ex.Message}");
                    }
                }
                
                StatusChanged?.Invoke($"扫描完成，共找到 {vhdFiles.Count} 个VHD/EVHD文件");
                foreach (var file in vhdFiles)
                {
                    Debug.WriteLine($"找到文件: {file}");
                }
            });

            return vhdFiles;
        }

        public async Task<bool> MountVHD(string vhdPath)
        {
            await ShowStatusAndWait($"正在挂载VHD文件: {Path.GetFileName(vhdPath)}");
            
            try
            {
                // 基本校验：文件必须存在
                if (string.IsNullOrWhiteSpace(vhdPath) || !File.Exists(vhdPath))
                {
                    await ShowStatusAndWait("挂载失败: VHD文件不存在或路径无效");
                    return false;
                }

                // 先清理盘符并分离可能已挂载的VHD，避免冲突
                await UnmountDrive();
                await UnmountVHD();
                // 挂载前读取注册表 \DosDevices\* 快照
                var mountedBefore = GetMountedDevicesDosDevices();
                // 挂载前读取注册表 \??\Volume{GUID} 快照
                var volumeBefore = GetMountedDevicesVolumeGuids();
                // 挂载前盘符集合，用于对比新出现的盘符
                var lettersBefore = GetCurrentDriveLetters();

                // 第一步：仅挂载VHD，不分配盘符
                await ShowStatusAndWait("步骤1: 挂载VHD文件...");
                var attachScript = $@"select vdisk file=""{vhdPath}""
attach vdisk
exit";
                
                var tempScript = Path.GetTempFileName();
                await File.WriteAllTextAsync(tempScript, attachScript);
                
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "diskpart",
                        Arguments = $"/s \"{tempScript}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                
                process.Start();

                // 设定挂载超时（60秒），防止卡死
                var waitTask = process.WaitForExitAsync();
                var timeoutTask = Task.Delay(60000);
                var completed = await Task.WhenAny(waitTask, timeoutTask);
                
                if (completed == timeoutTask)
                {
                    try { process.Kill(); } catch { }
                    await ShowStatusAndWait("VHD挂载超时（超过60秒），已中止diskpart进程");
                    if (File.Exists(tempScript))
                        File.Delete(tempScript);
                    return false;
                }

                string attachOutput = await process.StandardOutput.ReadToEndAsync();
                string attachError = await process.StandardError.ReadToEndAsync();
                
                if (File.Exists(tempScript))
                    File.Delete(tempScript);
                
                if (process.ExitCode != 0)
                {
                    await ShowStatusAndWait($"VHD挂载失败: {attachError.Trim()}");
                    Debug.WriteLine($"VHD挂载输出: {attachOutput}");
                    Debug.WriteLine($"VHD挂载错误: {attachError}");
                    return false;
                }

                // 优先尝试：直接为该 VHD 的第一个分区分配盘符 M，避免依赖“新增盘符/注册表差异”
                await ShowStatusAndWait("VHD挂载成功，正在分配盘符 M（无 diskpart）...");
                var assignedOk = await AssignDriveLetterWithoutDiskpart(vhdPath);
                if (assignedOk)
                {
                    await ShowStatusAndWait("盘符 M 分配成功");
                    return true;
                }

                // 若直接分配失败，再回退到“新增盘符”检测路径（适配系统自动分配其他盘符的情况）
                await ShowStatusAndWait("分配盘符失败，回退到检测新增盘符...");
                var newDriveLetter = await WaitForNewDriveLetterAsync(lettersBefore, 30000);

                // 兜底：如果系统因既有注册表映射自动分配了 M:，虽然不是“新增”，也直接视为成功
                if (Directory.Exists(TARGET_DRIVE))
                {
                    await ShowStatusAndWait("检测到盘符 M 已可用");
                    return true;
                }

                var targetLetter = TARGET_DRIVE.TrimEnd(':');
                var targetEntry = $@"\DosDevices\{targetLetter}:";

                if (string.IsNullOrEmpty(newDriveLetter))
                {
                    await ShowStatusAndWait("未检测到新盘符，尝试通过卷GUID定位注册表条目...");
                    // 轮询寻找新增的 \??\Volume{GUID} 条目
                    var swVol = Stopwatch.StartNew();
                    string newVolName = null;
                    byte[] newVolValue = null;
                    while (swVol.ElapsedMilliseconds < 30000)
                    {
                        await Task.Delay(500);
                        var volumeAfter = GetMountedDevicesVolumeGuids();
                        foreach (var kv in volumeAfter)
                        {
                            if (!volumeBefore.ContainsKey(kv.Key))
                            {
                                newVolName = kv.Key;
                                newVolValue = kv.Value;
                                break;
                            }
                        }
                        if (newVolName != null) break;
                    }

                    if (newVolName == null || newVolValue == null)
                    {
                        // 此场景常见于：该 VHD/卷曾被手动挂载过，注册表条目（\\DosDevices\\X: 或 \\??\\Volume{GUID}）已存在，因此不会新增
                        // 继续尝试备用方法，直接为最后挂载磁盘的第一个分区分配盘符 M
                        await ShowStatusAndWait("未检测到新增卷GUID条目，尝试备用分配盘符方法...");
                        var assignedFallback = await AssignDriveLetterDirect();
                        if (assignedFallback)
                        {
                            await ShowStatusAndWait("备用方法分配盘符 M 成功");
                            return true;
                        }

                        await ShowStatusAndWait("未检测到新增的卷GUID注册表条目，且备用方法也失败");
                        return false;
                    }

                    await ShowStatusAndWait($"检测到新增卷GUID条目: {newVolName}，写入 \\DosDevices\\M:...");
                    var targetLetter2 = TARGET_DRIVE.TrimEnd(':');
                    var targetEntry2 = $@"\DosDevices\{targetLetter2}:";
                    var setOk2 = SetDosDevicesEntry(targetEntry2, newVolValue, deleteExistingTarget: true);
                    if (!setOk2)
                    {
                        await ShowStatusAndWait("写入 \\DosDevices\\M: 失败");
                        return false;
                    }
                    await ShowStatusAndWait("已将注册表盘符重映射为 M:，即将重启以应用更改...");
                    TryRebootSystem();
                    return true;
                }

                await ShowStatusAndWait($"检测到新盘符: {newDriveLetter}");

                if (string.Equals(newDriveLetter.Trim(), TARGET_DRIVE, StringComparison.OrdinalIgnoreCase))
                {
                    await ShowStatusAndWait("新增盘符已为目标 M:，无需更改");
                    return true;
                }

                // 优先使用 \DosDevices\X: 的二进制值
                var sourceEntry = GetDosDevicesEntryName(newDriveLetter);
                var sourceValue = ReadMountedDevicesBinary(sourceEntry);

                if (sourceValue != null)
                {
                    var remapOk = TryRemapDosDevicesEntry(sourceEntry, sourceValue, targetEntry);
                    if (!remapOk)
                    {
                        await ShowStatusAndWait("注册表盘符重映射失败");
                        return false;
                    }
                }
                else
                {
                    // 回退：通过卷GUID查找二进制值（\\?\\Volume{GUID} => \\??\\Volume{GUID}）
                    var driveRoot = GetDriveRoot(newDriveLetter);
                    var sb = new System.Text.StringBuilder(256);
                    var ok = GetVolumeNameForVolumeMountPoint(driveRoot, sb, (uint)sb.Capacity);
                    if (!ok)
                    {
                        await ShowStatusAndWait("无法获取新盘符的卷GUID路径");
                        return false;
                    }
                    var volRegName = ConvertVolumeGuidPathToRegName(sb.ToString());
                    var volValue = ReadMountedDevicesBinary(volRegName);
                    if (volValue == null)
                    {
                        await ShowStatusAndWait("无法读取卷GUID对应的注册表二进制值");
                        return false;
                    }
                    // 仅设置目标 M:，不删除卷GUID条目
                    var setOk = SetDosDevicesEntry(targetEntry, volValue, deleteExistingTarget: true);
                    if (!setOk)
                    {
                        await ShowStatusAndWait("写入 \\DosDevices\\M: 失败");
                        return false;
                    }
                    // 尝试删除原盘符条目（若存在）
                    TryDeleteMountedDevicesValue(sourceEntry);
                }

                await ShowStatusAndWait("已将注册表盘符重映射为 M:，即将重启以应用更改...");
                TryRebootSystem();
                return true;
            }
            catch (Exception ex)
            {
                await ShowStatusAndWait($"挂载失败: {ex.Message}");
                Debug.WriteLine($"MountVHD异常: {ex}");
                return false;
            }
        }

        private static bool IsDosDevicesDriveLetter(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (!name.StartsWith("\\DosDevices\\", StringComparison.OrdinalIgnoreCase)) return false;
            if (!name.EndsWith(":")) return false;
            var idx = name.LastIndexOf('\\');
            if (idx < 0 || idx + 2 >= name.Length) return false;
            var letter = name.Substring(idx + 1);
            return letter.Length == 2 && char.IsLetter(letter[0]) && letter[1] == ':';
        }

        private static Dictionary<string, byte[]> GetMountedDevicesDosDevices()
        {
            var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\MountedDevices", writable: false);
            if (key == null) return result;
            foreach (var name in key.GetValueNames())
            {
                try
                {
                    var kind = key.GetValueKind(name);
                    if (kind == RegistryValueKind.Binary && name.StartsWith("\\DosDevices\\", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = key.GetValue(name) as byte[];
                        if (val != null) result[name] = val;
                    }
                }
                catch { }
            }
            return result;
        }

        private static Dictionary<string, byte[]> GetMountedDevicesVolumeGuids()
        {
            var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\MountedDevices", writable: false);
            if (key == null) return result;
            foreach (var name in key.GetValueNames())
            {
                try
                {
                    var kind = key.GetValueKind(name);
                    if (kind == RegistryValueKind.Binary && IsVolumeGuidEntry(name))
                    {
                        var val = key.GetValue(name) as byte[];
                        if (val != null) result[name] = val;
                    }
                }
                catch { }
            }
            return result;
        }

        private static bool IsVolumeGuidEntry(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.StartsWith("\\??\\Volume{", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryRemapDosDevicesEntry(string sourceEntry, byte[] value, string targetEntry)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\MountedDevices", writable: true);
                if (key == null) return false;
                // 先删除来源条目，再写入目标条目，符合“取值后删除源，再新建目标”的流程
                try { key.DeleteValue(sourceEntry, throwOnMissingValue: false); } catch { }
                key.SetValue(targetEntry, value, RegistryValueKind.Binary);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TryRemapDosDevicesEntry失败: {ex}");
                return false;
            }
        }

        private static void TryRebootSystem()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "shutdown",
                    Arguments = "/r /t 0",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"触发重启失败: {ex}");
            }
        }

        // 驱动器字母监控与卷GUID映射辅助
        private static HashSet<string> GetCurrentDriveLetters()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    var root = d.Name.TrimEnd('\\');
                    if (root.Length >= 2 && char.IsLetter(root[0]) && root[1] == ':')
                    {
                        set.Add(root);
                    }
                }
                catch { }
            }
            return set;
        }

        private static async Task<string> WaitForNewDriveLetterAsync(HashSet<string> before, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                await Task.Delay(500);
                var after = GetCurrentDriveLetters();
                foreach (var l in after)
                {
                    if (!before.Contains(l))
                    {
                        return l;
                    }
                }
            }
            return null;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetVolumeNameForVolumeMountPoint(string lpszVolumeMountPoint, System.Text.StringBuilder lpszVolumeName, uint cchBufferLength);

        private static string ConvertVolumeGuidPathToRegName(string volumeGuidPath)
        {
            // 输入: \\?\Volume{GUID}\, 输出: \??\Volume{GUID}
            if (string.IsNullOrWhiteSpace(volumeGuidPath)) return null;
            var trimmed = volumeGuidPath.TrimEnd('\\');
            if (!trimmed.StartsWith(@"\\?\"))
                return null;
            return @"\??\" + trimmed.Substring(4);
        }

        private static string GetDriveRoot(string driveLetter)
        {
            var letter = driveLetter.Trim();
            if (letter.EndsWith("\\"))
                return letter;
            if (!letter.EndsWith(":"))
                letter += ":";
            return letter + "\\";
        }

        private static string GetDosDevicesEntryName(string driveLetter)
        {
            var letter = driveLetter.Trim().TrimEnd(':');
            return $@"\DosDevices\{letter}:";
        }

        private static byte[] ReadMountedDevicesBinary(string valueName)
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\MountedDevices", writable: false);
            if (key == null) return null;
            try
            {
                var kind = key.GetValueKind(valueName);
                if (kind == RegistryValueKind.Binary)
                {
                    return key.GetValue(valueName) as byte[];
                }
            }
            catch { }
            return null;
        }

        private static bool SetDosDevicesEntry(string targetEntry, byte[] value, bool deleteExistingTarget = true)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\MountedDevices", writable: true);
                if (key == null) return false;
                if (deleteExistingTarget)
                {
                    try { key.DeleteValue(targetEntry, throwOnMissingValue: false); } catch { }
                }
                key.SetValue(targetEntry, value, RegistryValueKind.Binary);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SetDosDevicesEntry失败: {ex}");
                return false;
            }
        }

        private static void TryDeleteMountedDevicesValue(string valueName)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\MountedDevices", writable: true);
                if (key == null) return;
                try { key.DeleteValue(valueName, throwOnMissingValue: false); } catch { }
            }
            catch { }
        }

        public async Task<bool> UnmountDrive()
        {
            try
            {
                if (!Directory.Exists(TARGET_DRIVE))
                {
                    StatusChanged?.Invoke("M盘符已不存在，无需移除");
                    return true;
                }

                StatusChanged?.Invoke("正在移除M盘符...");
                
                var diskpartScript = "select volume M\nremove\nexit";
                var tempScript = Path.GetTempFileName();
                await File.WriteAllTextAsync(tempScript, diskpartScript);
                
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "diskpart",
                        Arguments = $"/s \"{tempScript}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                
                process.Start();
                
                // 添加超时机制
                var waitTask = process.WaitForExitAsync();
                var timeoutTask = Task.Delay(20000); // 20秒超时
                var completed = await Task.WhenAny(waitTask, timeoutTask);
                
                if (completed == timeoutTask)
                {
                    try { process.Kill(); } catch { }
                    StatusChanged?.Invoke("移除M盘符超时，已中止diskpart进程");
                    if (File.Exists(tempScript))
                        File.Delete(tempScript);
                    return false;
                }
                
                if (File.Exists(tempScript))
                    File.Delete(tempScript);
                
                StatusChanged?.Invoke("M盘符移除完成");
                return true;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"移除M盘符失败: {ex.Message}");
                Debug.WriteLine($"UnmountDrive异常: {ex}");
                return false;
            }
        }

        public async Task<string> FindFolder(string folderName)
        {
            StatusChanged?.Invoke($"正在搜索{folderName}文件夹...");
            
            if (!Directory.Exists(TARGET_DRIVE))
            {
                StatusChanged?.Invoke($"目标驱动器 {TARGET_DRIVE} 不存在");
                return null;
            }

            return await Task.Run(() =>
            {
                try
                {
                    StatusChanged?.Invoke($"开始在 {TARGET_DRIVE} 中搜索{folderName}文件夹...");
                    
                    // 首先检查根目录
                    var rootDirs = Directory.GetDirectories(TARGET_DRIVE)
                        .Select(d => new { Path = d, Name = Path.GetFileName(d) })
                        .ToList();
                    
                    StatusChanged?.Invoke($"根目录下找到 {rootDirs.Count} 个文件夹:");
                    foreach (var dir in rootDirs)
                    {
                        StatusChanged?.Invoke($"  - {dir.Name}");
                        Debug.WriteLine($"根目录文件夹: {dir.Name}");
                    }
                    
                    // 检查根目录中是否有目标文件夹（不区分大小写）
                    var targetInRoot = rootDirs.FirstOrDefault(d => 
                        string.Equals(d.Name, folderName, StringComparison.OrdinalIgnoreCase));
                    
                    if (targetInRoot != null)
                    {
                        StatusChanged?.Invoke($"在根目录找到{folderName}文件夹: {targetInRoot.Path}");
                        return targetInRoot.Path;
                    }
                    
                    // 如果根目录没有，递归搜索所有子目录（不区分大小写）
                    StatusChanged?.Invoke($"根目录未找到{folderName}文件夹，开始递归搜索...");
                    
                    var allDirectories = Directory.GetDirectories(TARGET_DRIVE, "*", SearchOption.AllDirectories)
                        .Where(d => string.Equals(Path.GetFileName(d), folderName, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    
                    StatusChanged?.Invoke($"递归搜索找到 {allDirectories.Count} 个{folderName}文件夹");
                    foreach (var dir in allDirectories)
                    {
                        StatusChanged?.Invoke($"  找到: {dir}");
                        Debug.WriteLine($"找到{folderName}文件夹: {dir}");
                    }
                    
                    return allDirectories.FirstOrDefault();
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke($"搜索{folderName}文件夹时出错: {ex.Message}");
                    Debug.WriteLine($"FindFolder错误: {ex}");
                    return null;
                }
            });
        }

        public async Task<string> FindPackageFolder()
        {
            return await FindFolder("package");
        }

        public async Task<bool> StartBatchFile(string packagePath)
        {
            var startBatPath = Path.Combine(packagePath, "start.bat");
            
            if (!File.Exists(startBatPath))
            {
                await ShowStatusAndWait("未找到start.bat文件");
                return false;
            }

            await ShowStatusAndWait("正在启动start.bat...");
            
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = startBatPath,
                        WorkingDirectory = packagePath,
                        UseShellExecute = true,
                        CreateNoWindow = false
                    }
                };
                
                process.Start();
                return true;
            }
            catch (Exception ex)
            {
                await ShowStatusAndWait($"启动失败: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> StartGameBatchFile(string packagePath)
        {
            var startGameBatPath = Path.Combine(packagePath, "start_game.bat");

            if (!File.Exists(startGameBatPath))
            {
                await ShowStatusAndWait("未找到start_game.bat文件");
                return false;
            }

            await ShowStatusAndWait("正在启动start_game.bat...");

            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = startGameBatPath,
                        WorkingDirectory = packagePath,
                        UseShellExecute = true,
                        CreateNoWindow = false
                    }
                };

                process.Start();
                return true;
            }
            catch (Exception ex)
            {
                await ShowStatusAndWait($"启动失败: {ex.Message}");
                return false;
            }
        }

        public bool IsTargetProcessRunning()
        {
            try
            {
                var processes = Process.GetProcesses();
                return processes.Any(p => PROCESS_KEYWORDS.Any(keyword => 
                    p.ProcessName.ToLower().Contains(keyword.ToLower())));
            }
            catch
            {
                return false;
            }
        }

        public bool IsProcessRunningByName(string name)
        {
            try
            {
                var processes = Process.GetProcessesByName(name);
                return processes != null && processes.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        public Process GetFirstTargetProcess()
        {
            try
            {
                var processes = Process.GetProcesses();
                return processes.FirstOrDefault(p => PROCESS_KEYWORDS.Any(keyword =>
                    p.ProcessName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0));
            }
            catch
            {
                return null;
            }
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        public bool FocusProcessWindow(Process process)
        {
            try
            {
                if (process == null) return false;
                process.Refresh();
                var hWnd = process.MainWindowHandle;
                if (hWnd == IntPtr.Zero)
                {
                    // 尝试等待窗口句柄出现
                    for (int i = 0; i < 20; i++)
                    {
                        Task.Delay(500).Wait();
                        process.Refresh();
                        hWnd = process.MainWindowHandle;
                        if (hWnd != IntPtr.Zero) break;
                    }
                }
                if (hWnd == IntPtr.Zero) return false;
                ShowWindow(hWnd, SW_RESTORE);
                return SetForegroundWindow(hWnd);
            }
            catch
            {
                return false;
            }
        }

        public async Task MonitorAndRestart(string packagePath)
        {
            await ShowStatusAndWait("开始监控目标进程...");
            while (true)
            {
                if (!IsTargetProcessRunning())
                {
                    try { GameCrashed?.Invoke(); } catch { }

                    // 根据存在情况选择重启脚本：优先 start_game.bat；仅有 start.bat 时用 start.bat
                    var startGameBatPath = Path.Combine(packagePath, "start_game.bat");
                    var startBatPath = Path.Combine(packagePath, "start.bat");
                    bool hasStartGame = File.Exists(startGameBatPath);
                    bool hasStart = File.Exists(startBatPath);

                    if (hasStartGame && hasStart)
                    {
                        await ShowStatusAndWait("目标进程未运行，检测到start.bat与start_game.bat，重启使用start_game.bat...");
                        await StartGameBatchFile(packagePath);
                    }
                    else if (hasStartGame)
                    {
                        await ShowStatusAndWait("目标进程未运行，检测到start_game.bat，重启使用start_game.bat...");
                        await StartGameBatchFile(packagePath);
                    }
                    else if (hasStart)
                    {
                        await ShowStatusAndWait("目标进程未运行，仅检测到start.bat，重启使用start.bat...");
                        await StartBatchFile(packagePath);
                    }
                    else
                    {
                        await ShowStatusAndWait("目标进程未运行，未找到重启脚本(start.bat/start_game.bat)，等待脚本或进程出现...");
                    }

                    // 等待启动到可检测
                    var startWaitMs = 0;
                    while (!IsTargetProcessRunning() && startWaitMs < 30000)
                    {
                        await Task.Delay(1000);
                        startWaitMs += 1000;
                    }
                    if (IsTargetProcessRunning())
                    {
                        // 检测到进程后，等待 5 秒确保游戏窗口已创建并稳定
                        await ShowStatusAndWait("检测到游戏进程已启动，等待5秒稳定...");
                        await Task.Delay(5000);

                        // 尝试将游戏窗口前置到前台
                        var proc = GetFirstTargetProcess();
                        if (proc != null)
                        {
                            FocusProcessWindow(proc);
                        }

                        // 通知 UI 已成功重启，MainWindow 会在该事件中最小化/隐藏自身
                        try { GameStarted?.Invoke(); } catch { }
                        await ShowStatusAndWait("游戏进程已重启并前置");
                    }
                }
                await Task.Delay(1000); // 每秒检查一次
            }
        }

        /// <summary>
        /// 删除VHD第一个分区的现有盘符
        /// </summary>
        private async Task<bool> RemoveExistingDriveLetter(string vhdPath)
        {
            try
            {
                await ShowStatusAndWait("正在删除VHD第一个分区的现有盘符...");
                
                // 使用diskpart删除第一个分区的盘符
                var removeScript = $@"select vdisk file=""{vhdPath}""
list partition
select partition 1
remove letter=M
exit";
                
                var tempScript = Path.GetTempFileName();
                await File.WriteAllTextAsync(tempScript, removeScript);
                
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "diskpart",
                        Arguments = $"/s \"{tempScript}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                
                process.Start();
                
                var waitTask = process.WaitForExitAsync();
                var timeoutTask = Task.Delay(30000);
                var completed = await Task.WhenAny(waitTask, timeoutTask);
                
                if (completed == timeoutTask)
                {
                    try { process.Kill(); } catch { }
                    await ShowStatusAndWait("删除盘符超时");
                    if (File.Exists(tempScript))
                        File.Delete(tempScript);
                    return false;
                }

                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();
                
                if (File.Exists(tempScript))
                    File.Delete(tempScript);
                
                Debug.WriteLine($"删除盘符输出: {output}");
                if (!string.IsNullOrWhiteSpace(error))
                {
                    Debug.WriteLine($"删除盘符错误: {error}");
                }

                // 等待盘符删除完成
                await Task.Delay(2000);
                
                // 验证M盘是否已被删除
                bool driveRemoved = !Directory.Exists(TARGET_DRIVE);
                if (driveRemoved)
                {
                    await ShowStatusAndWait("盘符M删除成功");
                }
                else
                {
                    await ShowStatusAndWait("盘符M可能仍然存在，但继续处理");
                }
                
                return driveRemoved;
            }
            catch (Exception ex)
            {
                await ShowStatusAndWait($"删除盘符异常: {ex.Message}");
                Debug.WriteLine($"RemoveExistingDriveLetter异常: {ex}");
                return false;
            }
        }

        /// <summary>
        /// 为VHD的第一个分区分配盘符M
        /// </summary>
        private async Task<bool> AssignDriveLetterToFirstPartition(string vhdPath)
        {
            try
            {
                // 使用diskpart列出VHD的分区并为第一个分区分配盘符
                var listScript = $@"select vdisk file=""{vhdPath}""
list partition
select partition 1
assign letter=M
exit";
                
                var tempScript = Path.GetTempFileName();
                await File.WriteAllTextAsync(tempScript, listScript);
                
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "diskpart",
                        Arguments = $"/s \"{tempScript}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                
                process.Start();
                
                var waitTask = process.WaitForExitAsync();
                var timeoutTask = Task.Delay(30000);
                var completed = await Task.WhenAny(waitTask, timeoutTask);
                
                if (completed == timeoutTask)
                {
                    try { process.Kill(); } catch { }
                    await ShowStatusAndWait("分配盘符超时");
                    if (File.Exists(tempScript))
                        File.Delete(tempScript);
                    return false;
                }

                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();
                
                if (File.Exists(tempScript))
                    File.Delete(tempScript);
                
                Debug.WriteLine($"分区盘符分配输出: {output}");
                if (!string.IsNullOrWhiteSpace(error))
                {
                    Debug.WriteLine($"分区盘符分配错误: {error}");
                }

                // 等待盘符分配完成
                await Task.Delay(2000);
                
                bool driveExists = Directory.Exists(TARGET_DRIVE);
                if (driveExists)
                {
                    await ShowStatusAndWait("盘符M分配成功");
                }
                else
                {
                    await ShowStatusAndWait("盘符M分配失败，M盘不存在");
                }
                
                return driveExists;
            }
            catch (Exception ex)
            {
                await ShowStatusAndWait($"分配盘符异常: {ex.Message}");
                Debug.WriteLine($"AssignDriveLetterToFirstPartition异常: {ex}");
                return false;
            }
        }

        /// <summary>
        /// 备用方法：直接尝试分配盘符（适用于单分区VHD）
        /// </summary>
        private async Task<bool> AssignDriveLetterDirect()
        {
            try
            {
                await ShowStatusAndWait("尝试备用盘符分配方法...");
                
                // 尝试为最后挂载的磁盘分配盘符
                var assignScript = @"list disk
select disk 1
list partition
select partition 1
assign letter=M
exit";
                
                var tempScript = Path.GetTempFileName();
                await File.WriteAllTextAsync(tempScript, assignScript);
                
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "diskpart",
                        Arguments = $"/s \"{tempScript}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                
                process.Start();
                
                var waitTask = process.WaitForExitAsync();
                var timeoutTask = Task.Delay(30000);
                var completed = await Task.WhenAny(waitTask, timeoutTask);
                
                if (completed == timeoutTask)
                {
                    try { process.Kill(); } catch { }
                    await ShowStatusAndWait("备用盘符分配超时");
                    if (File.Exists(tempScript))
                        File.Delete(tempScript);
                    return false;
                }

                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();
                
                if (File.Exists(tempScript))
                    File.Delete(tempScript);
                
                Debug.WriteLine($"备用盘符分配输出: {output}");
                if (!string.IsNullOrWhiteSpace(error))
                {
                    Debug.WriteLine($"备用盘符分配错误: {error}");
                }

                // 等待盘符分配完成
                await Task.Delay(2000);
                
                bool driveExists = Directory.Exists(TARGET_DRIVE);
                if (driveExists)
                {
                    await ShowStatusAndWait("备用方法盘符M分配成功");
                }
                else
                {
                    await ShowStatusAndWait("备用方法盘符M分配也失败");
                }
                
                return driveExists;
            }
            catch (Exception ex)
            {
                await ShowStatusAndWait($"备用盘符分配异常: {ex.Message}");
                Debug.WriteLine($"AssignDriveLetterDirect异常: {ex}");
                return false;
            }
        }

        /// <summary>
        /// 更稳健的分配盘符方法：通过 detail vdisk 获取磁盘号后，为第一个分区分配 M 盘符
        /// </summary>
        private async Task<bool> AssignDriveLetterToFirstPartitionRobust(string vhdPath)
        {
            try
            {
                var detailScript = $@"select vdisk file=""{vhdPath}""
attach vdisk
detail vdisk
exit";
                var tempDetail = Path.GetTempFileName();
                await File.WriteAllTextAsync(tempDetail, detailScript);

                var detailProc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "diskpart",
                        Arguments = $"/s \"{tempDetail}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };

                detailProc.Start();
                var detailWait = detailProc.WaitForExitAsync();
                var detailTimeout = Task.Delay(30000);
                var detailCompleted = await Task.WhenAny(detailWait, detailTimeout);
                if (detailCompleted == detailTimeout)
                {
                    try { detailProc.Kill(); } catch { }
                    if (File.Exists(tempDetail)) File.Delete(tempDetail);
                    await ShowStatusAndWait("获取磁盘号超时");
                    return false;
                }

                var detailOut = await detailProc.StandardOutput.ReadToEndAsync();
                var detailErr = await detailProc.StandardError.ReadToEndAsync();
                if (File.Exists(tempDetail)) File.Delete(tempDetail);
                Debug.WriteLine($"detail vdisk 输出:\n{detailOut}");
                if (!string.IsNullOrWhiteSpace(detailErr)) Debug.WriteLine($"detail vdisk 错误:\n{detailErr}");

                int diskNumber = -1;
                var lines = detailOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    var match = System.Text.RegularExpressions.Regex.Match(trimmed, @"^(Disk number|磁盘编号)\s*[:：]\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (match.Success && int.TryParse(match.Groups[2].Value, out var num)) { diskNumber = num; break; }
                }

                if (diskNumber < 0)
                {
                    await ShowStatusAndWait("未能解析VHD的磁盘号");
                    return false;
                }

                // 确保磁盘在线并列出分区
                var listScript = $@"select disk {diskNumber}
online disk
list partition
exit";
                var tempList = Path.GetTempFileName();
                await File.WriteAllTextAsync(tempList, listScript);

                var listProc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "diskpart",
                        Arguments = $"/s \"{tempList}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };

                listProc.Start();
                var listWait = listProc.WaitForExitAsync();
                var listTimeout = Task.Delay(30000);
                var listCompleted = await Task.WhenAny(listWait, listTimeout);
                if (listCompleted == listTimeout)
                {
                    try { listProc.Kill(); } catch { }
                    if (File.Exists(tempList)) File.Delete(tempList);
                    await ShowStatusAndWait("列出分区超时");
                    return false;
                }

                var listOut = await listProc.StandardOutput.ReadToEndAsync();
                var listErr = await listProc.StandardError.ReadToEndAsync();
                if (File.Exists(tempList)) File.Delete(tempList);
                Debug.WriteLine($"分区列表输出:\n{listOut}");
                if (!string.IsNullOrWhiteSpace(listErr)) Debug.WriteLine($"分区列表错误:\n{listErr}");

                // 提取所有分区编号（兼容英文"Partition"与中文"分区"）
                var partitionNumbers = new List<int>();
                foreach (var line in listOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = line.Trim();
                    var match = System.Text.RegularExpressions.Regex.Match(trimmed, "^(Partition|分区)\\s+(\\d+)");
                    if (match.Success && int.TryParse(match.Groups[2].Value, out var pn))
                    {
                        partitionNumbers.Add(pn);
                    }
                }

                if (partitionNumbers.Count == 0)
                {
                    await ShowStatusAndWait("未找到任何分区");
                    return false;
                }

                partitionNumbers.Sort();

                // 逐一尝试为分区分配 M 盘符，直至成功
                foreach (var pn in partitionNumbers)
                {
                    var assignScript = $@"select disk {diskNumber}
select partition {pn}
assign letter=M
exit";
                    var tempAssign = Path.GetTempFileName();
                    await File.WriteAllTextAsync(tempAssign, assignScript);

                    var assignProc = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "diskpart",
                            Arguments = $"/s \"{tempAssign}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        }
                    };

                    assignProc.Start();
                    var assignWait = assignProc.WaitForExitAsync();
                    var assignTimeout = Task.Delay(30000);
                    var assignCompleted = await Task.WhenAny(assignWait, assignTimeout);
                    if (assignCompleted == assignTimeout)
                    {
                        try { assignProc.Kill(); } catch { }
                        if (File.Exists(tempAssign)) File.Delete(tempAssign);
                        continue; // 尝试下一个分区
                    }

                    var assignOut = await assignProc.StandardOutput.ReadToEndAsync();
                    var assignErr = await assignProc.StandardError.ReadToEndAsync();
                    if (File.Exists(tempAssign)) File.Delete(tempAssign);
                    Debug.WriteLine($"尝试分配分区 {pn} 输出:\n{assignOut}");
                    if (!string.IsNullOrWhiteSpace(assignErr)) Debug.WriteLine($"尝试分配分区 {pn} 错误:\n{assignErr}");

                    await Task.Delay(1000);
                    if (Directory.Exists(TARGET_DRIVE))
                    {
                        await ShowStatusAndWait($"已为分区 {pn} 分配盘符 M 成功");
                        return true;
                    }
                }

                await ShowStatusAndWait("所有分区尝试分配 M 盘符均失败");
                return false;
            }
            catch (Exception ex)
            {
                await ShowStatusAndWait($"分配盘符异常: {ex.Message}");
                Debug.WriteLine($"AssignDriveLetterToFirstPartitionRobust异常: {ex}");
                return false;
            }
        }

        // 无 diskpart 的盘符分配实现：枚举卷 → 解析扩展 → 识别虚拟磁盘卷 → 设置 M:\
        private async Task<bool> AssignDriveLetterWithoutDiskpart(string vhdPath)
        {
            try
            {
                if (Directory.Exists(TARGET_DRIVE))
                {
                    await ShowStatusAndWait("检测到盘符 M 已可用");
                    return true;
                }

                var volumes = EnumerateVolumes();
                // 优先尝试未分配盘符的卷，减少对已有盘符（如 N:）的影响
                foreach (var vol in volumes.OrderBy(v => string.IsNullOrWhiteSpace(v.DriveLetter) ? 0 : 1))
                {
                    await ShowStatusAndWait($"检测卷: {vol.DevicePath} FS={vol.FileSystem} DL={vol.DriveLetter ?? "(none)"}");
                    if (string.IsNullOrWhiteSpace(vol.FileSystem))
                        continue;

                    var diskNumbers = GetVolumeDiskNumbers(vol.DevicePath);
                    await ShowStatusAndWait($"卷磁盘号: {(diskNumbers == null || diskNumbers.Count == 0 ? "(none)" : string.Join(",", diskNumbers))}");
                    if (diskNumbers == null || diskNumbers.Count == 0)
                        continue;

                    if (!BelongsToVirtualDisk(diskNumbers))
                        continue;

                    await ShowStatusAndWait("识别为虚拟磁盘卷，尝试分配 M");
                    var ok = TrySetDriveLetterM(vol.DevicePath);
                    await ShowStatusAndWait($"分配接口返回: {ok}");
                    await Task.Delay(1000);
                    if (ok && Directory.Exists(TARGET_DRIVE))
                    {
                        return true;
                    }
                }

                // 回退优先：若仅有一个“无盘符且为 NTFS”的卷，优先为其分配 M（更贴近挂载的 VHD 数据分区）
                var noLetterNtfs = volumes.Where(v => string.IsNullOrWhiteSpace(v.DriveLetter) && string.Equals(v.FileSystem ?? string.Empty, "NTFS", StringComparison.OrdinalIgnoreCase)).ToList();
                if (noLetterNtfs.Count == 1)
                {
                    var targetId = NormalizeVolumeId(noLetterNtfs[0].DevicePath);
                    await ShowStatusAndWait("未识别到虚拟卷，但仅有一个无盘符的 NTFS 卷，尝试回退分配 M");
                    await ShowStatusAndWait($"开始调用 CIM AddMountPoint: DeviceID={targetId}");
                    var ok = TrySetDriveLetterM(noLetterNtfs[0].DevicePath);
                    await ShowStatusAndWait($"分配接口返回: {ok}");
                    await Task.Delay(1000);
                    if (ok && Directory.Exists(TARGET_DRIVE))
                    {
                        return true;
                    }
                }

                // 次级回退：如果系统中仅存在一个“无盘符且有文件系统”的卷（非 NTFS 场景），尝试分配 M
                var noLetterWithFs = volumes.Where(v => string.IsNullOrWhiteSpace(v.DriveLetter) && !string.IsNullOrWhiteSpace(v.FileSystem)).ToList();
                if (noLetterWithFs.Count == 1)
                {
                    await ShowStatusAndWait("未识别到虚拟卷，但仅有一个无盘符的文件系统卷，尝试回退分配 M");
                    var ok = TrySetDriveLetterM(noLetterWithFs[0].DevicePath);
                    await ShowStatusAndWait($"分配接口返回: {ok}");
                    await Task.Delay(1000);
                    if (ok && Directory.Exists(TARGET_DRIVE))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"AssignDriveLetterWithoutDiskpart 异常: {ex}");
                await ShowStatusAndWait($"盘符分配异常: {ex.Message}");
                return false;
            }
        }

        private class VolumeInfo
        {
            public string DevicePath { get; set; } = string.Empty; // 如 \\?\Volume{GUID}\\
            public string? FileSystem { get; set; }
            public string? DriveLetter { get; set; } // 如 "M:"，为空表示未分配
        }

        private List<VolumeInfo> EnumerateVolumes()
        {
            var list = new List<VolumeInfo>();
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher("SELECT DeviceID, FileSystem, DriveLetter FROM Win32_Volume");
                foreach (System.Management.ManagementObject obj in searcher.Get())
                {
                    var devId = obj["DeviceID"]?.ToString();
                    var fs = obj["FileSystem"]?.ToString();
                    var dl = obj["DriveLetter"]?.ToString();
                    if (string.IsNullOrWhiteSpace(devId)) continue;
                    list.Add(new VolumeInfo { DevicePath = devId!, FileSystem = fs, DriveLetter = dl });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EnumerateVolumes 异常: {ex}");
            }
            return list;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct DISK_EXTENT
        {
            public int DiskNumber;
            public long StartingOffset;
            public long ExtentLength;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct VOLUME_DISK_EXTENTS
        {
            public int NumberOfDiskExtents;
        }

        private const uint GENERIC_READ = 0x80000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint FILE_SHARE_DELETE = 0x00000004;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
        private const uint IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS = 0x00560000;

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            Microsoft.Win32.SafeHandles.SafeFileHandle hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            int nInBufferSize,
            IntPtr lpOutBuffer,
            int nOutBufferSize,
            out int lpBytesReturned,
            IntPtr lpOverlapped);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern bool SetVolumeMountPoint(string lpszVolumeMountPoint, string lpszVolumeName);

        private List<int> GetVolumeDiskNumbers(string volumeDevicePath)
        {
            var result = new List<int>();
            try
            {
                if (!volumeDevicePath.EndsWith("\\")) volumeDevicePath += "\\";
                using var handle = CreateFile(
                    volumeDevicePath,
                    GENERIC_READ,
                    FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    FILE_FLAG_BACKUP_SEMANTICS,
                    IntPtr.Zero);

                if (handle == null || handle.IsInvalid) return result;

                int outSize = System.Runtime.InteropServices.Marshal.SizeOf<VOLUME_DISK_EXTENTS>() + (System.Runtime.InteropServices.Marshal.SizeOf<DISK_EXTENT>() * 16);
                var outBuffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(outSize);
                try
                {
                    if (DeviceIoControl(handle, IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS, IntPtr.Zero, 0, outBuffer, outSize, out var bytesReturned, IntPtr.Zero))
                    {
                        int count = System.Runtime.InteropServices.Marshal.ReadInt32(outBuffer);
                        IntPtr current = outBuffer + System.Runtime.InteropServices.Marshal.SizeOf<VOLUME_DISK_EXTENTS>();
                        for (int i = 0; i < count; i++)
                        {
                            var extent = System.Runtime.InteropServices.Marshal.PtrToStructure<DISK_EXTENT>(current);
                            result.Add(extent.DiskNumber);
                            current += System.Runtime.InteropServices.Marshal.SizeOf<DISK_EXTENT>();
                        }
                    }
                }
                finally
                {
                    System.Runtime.InteropServices.Marshal.FreeHGlobal(outBuffer);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetVolumeDiskNumbers 异常: {ex}");
            }
            return result;
        }

        private bool BelongsToVirtualDisk(List<int> diskNumbers)
        {
            try
            {
                foreach (var dn in diskNumbers)
                {
                    using var searcher = new System.Management.ManagementObjectSearcher($"SELECT Index, Model FROM Win32_DiskDrive WHERE Index = {dn}");
                    foreach (System.Management.ManagementObject obj in searcher.Get())
                    {
                        var model = obj["Model"]?.ToString() ?? string.Empty;
                        if (model.Contains("Virtual", StringComparison.OrdinalIgnoreCase) || model.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"BelongsToVirtualDisk 异常: {ex}");
            }
            return false;
        }

        private bool TrySetDriveLetterM(string volumeDevicePath)
        {
            try
            {
                // 仅使用 CIM：用 Key 构建实例（避免查询和转义问题）
                using var session = CimSession.Create(null);
                var expectedId = NormalizeVolumeId(volumeDevicePath);
                Trace.WriteLine($"CIM 目标卷 DeviceID: {expectedId}");

                var volInstance = new CimInstance("Win32_Volume");
                volInstance.CimInstanceProperties.Add(
                    Microsoft.Management.Infrastructure.CimProperty.Create(
                        "DeviceID",
                        expectedId,
                        Microsoft.Management.Infrastructure.CimType.String,
                        Microsoft.Management.Infrastructure.CimFlags.Key));

                var parameters = new CimMethodParametersCollection();
                var mountDir = TARGET_DRIVE.EndsWith("\\") ? TARGET_DRIVE : TARGET_DRIVE + "\\";
                parameters.Add(Microsoft.Management.Infrastructure.CimMethodParameter.Create("Directory", mountDir, Microsoft.Management.Infrastructure.CimType.String, Microsoft.Management.Infrastructure.CimFlags.None));

                var result = session.InvokeMethod(@"root\cimv2", volInstance, "AddMountPoint", parameters);
                var rvParam = result.ReturnValue;
                if (rvParam != null)
                {
                    var val = rvParam.Value;
                    if (val is uint u && u == 0) return true;
                    if (val is int i && i == 0) return true;
                    Trace.WriteLine($"CIM AddMountPoint 返回码: {val}");
                }
                else
                {
                    Trace.WriteLine("CIM AddMountPoint 未返回码");
                }

                return false;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"TrySetDriveLetterM 异常: {ex}");
                return false;
            }
        }

        private static string NormalizeVolumeId(string id)
        {
            var s = (id ?? string.Empty).Trim();
            if (!s.EndsWith("\\")) s += "\\";
            return s;
        }

        public async Task<bool> UnmountVHD()
        {
            await ShowStatusAndWait("正在解除VHD挂载...");
            
            try
            {
                // 直接分离VHD文件，不移除驱动器字母
                var diskpartScript = "select vdisk file=*\ndetach vdisk\nexit";
                var tempScript = Path.GetTempFileName();
                await File.WriteAllTextAsync(tempScript, diskpartScript);
                
                await ShowStatusAndWait("正在执行VHD解除挂载脚本...");
                
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "diskpart",
                        Arguments = $"/s \"{tempScript}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                
                process.Start();
                
                // 添加超时机制，防止解除挂载时卡住
                var waitTask = process.WaitForExitAsync();
                var timeoutTask = Task.Delay(30000); // 30秒超时
                var completed = await Task.WhenAny(waitTask, timeoutTask);
                
                if (completed == timeoutTask)
                {
                    try { process.Kill(); } catch { }
                    StatusChanged?.Invoke("解除VHD挂载超时（超过30秒），已中止diskpart进程");
                    if (File.Exists(tempScript))
                        File.Delete(tempScript);
                    return false;
                }
                
                // 获取进程输出用于调试
                string output = "";
                string error = "";
                try
                {
                    output = await process.StandardOutput.ReadToEndAsync();
                    error = await process.StandardError.ReadToEndAsync();
                }
                catch { }
                
                if (File.Exists(tempScript))
                    File.Delete(tempScript);
                
                if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(error))
                {
                    StatusChanged?.Invoke($"解除VHD挂载警告: {error.Trim()}");
                    Debug.WriteLine($"UnmountVHD输出: {output}");
                    Debug.WriteLine($"UnmountVHD错误: {error}");
                }
                
                StatusChanged?.Invoke("VHD解除挂载完成");
                return true;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"解除VHD挂载失败: {ex.Message}");
                Debug.WriteLine($"UnmountVHD异常: {ex}");
                return false;
            }
        }
        
        // IDisposable实现
        public void Dispose()
        {
            StopProtectionCheck();
            httpClient?.Dispose();
        }
        
        // 析构函数
        ~VHDManager()
        {
            Dispose();
        }
    }
}
