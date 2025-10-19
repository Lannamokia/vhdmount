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

namespace VHDMounter
{
    public class VHDManager
    {
        private const string TARGET_DRIVE = "M:";
        private readonly string[] TARGET_KEYWORDS = { "SDEZ", "SDHD", "SDDT" };
        private readonly string[] PROCESS_KEYWORDS = { "sinmai", "chusanapp", "mu3" };

        public event Action<string> StatusChanged;
        
        private static readonly HttpClient httpClient = new HttpClient();
        private string configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vhdmonter_config.ini");
        
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

        // 从USB设备中扫描VHD文件
        public List<string> ScanUSBForVHDFiles(DriveInfo usbDrive)
        {
            StatusChanged?.Invoke($"正在扫描USB设备 {usbDrive.Name} 中的VHD文件...");
            var vhdFiles = new List<string>();

            try
            {
                var rootFiles = Directory.GetFiles(usbDrive.RootDirectory.FullName, "*.vhd", SearchOption.TopDirectoryOnly)
                    .Where(f => TARGET_KEYWORDS.Any(keyword => Path.GetFileName(f).ToUpper().Contains(keyword)))
                    .ToList();
                
                vhdFiles.AddRange(rootFiles);
                StatusChanged?.Invoke($"在USB设备中找到 {vhdFiles.Count} 个VHD文件");
                
                foreach (var file in vhdFiles)
                {
                    Debug.WriteLine($"USB中找到VHD文件: {file}");
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

            bool anyReplaced = false;
            StatusChanged?.Invoke("正在替换本地VHD文件...");

            foreach (var usbFile in usbVhdFiles)
            {
                string usbFileName = Path.GetFileName(usbFile);
                string usbKeyword = ExtractKeyword(usbFileName);
                
                if (string.IsNullOrEmpty(usbKeyword))
                    continue;

                // 查找对应关键词的本地文件
                var matchingLocalFiles = localVhdFiles.Where(f => 
                    ExtractKeyword(Path.GetFileName(f)) == usbKeyword).ToList();

                if (matchingLocalFiles.Count > 0)
                {
                    foreach (var localFile in matchingLocalFiles)
                    {
                        try
                        {
                            // Win11兼容性检查和准备
                            StatusChanged?.Invoke($"检查Win11兼容性: {Path.GetFileName(localFile)}");
                            var localFileInfo = new FileInfo(localFile);
                            var usbFileInfo = new FileInfo(usbFile);
                            
                            // 记录文件属性用于诊断
                            Debug.WriteLine($"本地文件属性: {localFileInfo.Attributes}, 只读: {localFileInfo.IsReadOnly}");
                            Debug.WriteLine($"USB文件属性: {usbFileInfo.Attributes}, 大小: {usbFileInfo.Length} bytes");
                            Debug.WriteLine($"操作系统版本: {Environment.OSVersion}");
                            
                            // 检查文件是否被占用
                            if (IsFileInUse(localFile))
                            {
                                StatusChanged?.Invoke($"文件被占用，无法删除: {Path.GetFileName(localFile)}");
                                Debug.WriteLine($"文件被占用: {localFile}");
                                continue;
                            }
                            
                            // Win11兼容性处理
                            if (!PrepareFileForReplacement(localFile))
                            {
                                StatusChanged?.Invoke($"无法准备文件进行替换: {Path.GetFileName(localFile)}");
                                continue;
                            }
                            
                            // 直接删除本地文件
                            StatusChanged?.Invoke($"正在删除本地文件: {Path.GetFileName(localFile)}");
                            if (File.Exists(localFile))
                                File.Delete(localFile);
                            
                            // 等待文件系统完成删除操作
                            int retryCount = 0;
                            while (File.Exists(localFile) && retryCount < 10)
                            {
                                await Task.Delay(100);
                                retryCount++;
                            }
                            
                            if (File.Exists(localFile))
                            {
                                StatusChanged?.Invoke($"删除文件失败，文件仍然存在: {Path.GetFileName(localFile)}");
                                continue;
                            }
                            
                            // 复制USB文件到本地
                            StatusChanged?.Invoke($"正在用 {Path.GetFileName(usbFile)} 替换 {Path.GetFileName(localFile)}");
                            File.Copy(usbFile, localFile);
                            
                            // 验证复制结果
                            var newFileInfo = new FileInfo(localFile);
                            if (newFileInfo.Length != usbFileInfo.Length)
                            {
                                StatusChanged?.Invoke($"警告: 文件大小不匹配 - 原始: {usbFileInfo.Length}, 复制后: {newFileInfo.Length}");
                            }
                            
                            anyReplaced = true;
                            StatusChanged?.Invoke($"替换完成: {Path.GetFileName(localFile)}");
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            StatusChanged?.Invoke($"权限不足，无法替换文件: {Path.GetFileName(localFile)} - {ex.Message}");
                            Debug.WriteLine($"权限错误: {ex}");
                        }
                        catch (IOException ex)
                        {
                            StatusChanged?.Invoke($"文件IO错误: {Path.GetFileName(localFile)} - {ex.Message}");
                            Debug.WriteLine($"IO错误: {ex}");
                        }
                        catch (Exception ex)
                        {
                            StatusChanged?.Invoke($"替换文件时出错: {Path.GetFileName(localFile)} - {ex.Message}");
                            Debug.WriteLine($"替换文件时出错: {ex}");
                        }
                    }
                }
            }

            return anyReplaced;
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
                
                StatusChanged?.Invoke($"正在从远程获取VHD选择: {url}");
                
                var response = await httpClient.GetAsync(url);
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
                    StatusChanged?.Invoke($"远程选择的VHD关键词: {selectedKeyword}");
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
            StatusChanged?.Invoke("正在扫描VHD文件...");
            var vhdFiles = new List<string>();

            await Task.Run(() =>
            {
                var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed);
                
                foreach (var drive in drives)
                {
                    try
                    {
                        StatusChanged?.Invoke($"正在扫描驱动器 {drive.Name} 根目录...");
                        
                        // 只扫描根目录
                        var rootFiles = Directory.GetFiles(drive.RootDirectory.FullName, "*.vhd", SearchOption.TopDirectoryOnly)
                            .Where(f => TARGET_KEYWORDS.Any(keyword => Path.GetFileName(f).ToUpper().Contains(keyword)))
                            .ToList();
                        
                        vhdFiles.AddRange(rootFiles);
                        Debug.WriteLine($"在 {drive.Name} 根目录找到 {rootFiles.Count} 个符合条件的VHD文件");
                        
                        foreach (var file in rootFiles)
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
                
                StatusChanged?.Invoke($"扫描完成，共找到 {vhdFiles.Count} 个VHD文件");
                foreach (var file in vhdFiles)
                {
                    Debug.WriteLine($"找到VHD文件: {file}");
                }
            });

            return vhdFiles;
        }

        public async Task<bool> MountVHD(string vhdPath)
        {
            StatusChanged?.Invoke($"正在挂载VHD文件: {Path.GetFileName(vhdPath)}");
            
            try
            {
                // 基本校验：文件必须存在
                if (string.IsNullOrWhiteSpace(vhdPath) || !File.Exists(vhdPath))
                {
                    StatusChanged?.Invoke("挂载失败: VHD文件不存在或路径无效");
                    return false;
                }

                // 先清理盘符并分离可能已挂载的VHD，避免冲突
                await UnmountDrive();
                await UnmountVHD();

                var diskpartScript = $@"select vdisk file=""{vhdPath}""
attach vdisk
assign letter=M
exit";
                
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

                // 设定挂载超时（60秒），防止卡死
                var waitTask = process.WaitForExitAsync();
                var timeoutTask = Task.Delay(60000);
                var completed = await Task.WhenAny(waitTask, timeoutTask);
                
                if (completed == timeoutTask)
                {
                    try { process.Kill(); } catch { }
                    StatusChanged?.Invoke("挂载超时（超过60秒），已中止diskpart进程");
                    File.Delete(tempScript);
                    return false;
                }

                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();
                
                File.Delete(tempScript);
                
                // 等待系统完成卷挂载
                await Task.Delay(2000);
                
                bool mounted = Directory.Exists(TARGET_DRIVE);
                if (!mounted)
                {
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        StatusChanged?.Invoke($"挂载失败：{error.Trim()}");
                    }
                    else
                    {
                        // 提供有限的DiskPart输出以辅助定位问题
                        var trimmedOutput = output?.Length > 400 ? output.Substring(output.Length - 400) : output;
                        StatusChanged?.Invoke($"挂载失败：未检测到M盘。DiskPart输出片段：{trimmedOutput}");
                    }
                }

                return mounted;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"挂载失败: {ex.Message}");
                return false;
            }
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

        public Task<bool> StartBatchFile(string packagePath)
        {
            var startBatPath = Path.Combine(packagePath, "start.bat");
            
            if (!File.Exists(startBatPath))
            {
                StatusChanged?.Invoke("未找到start.bat文件");
                return Task.FromResult(false);
            }

            StatusChanged?.Invoke("正在启动start.bat...");
            
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
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"启动失败: {ex.Message}");
                return Task.FromResult(false);
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

        public async Task MonitorAndRestart(string packagePath)
        {
            StatusChanged?.Invoke("等待15秒后开始监控进程...");
            await Task.Delay(15000); // 等待15秒
            
            StatusChanged?.Invoke("开始监控目标进程...");
            
            while (true)
            {
                if (!IsTargetProcessRunning())
                {
                    StatusChanged?.Invoke("目标进程未运行，重新启动start.bat...");
                    await StartBatchFile(packagePath);
                    await Task.Delay(15000); // 重启后等待15秒
                }
                
                await Task.Delay(1000); // 每秒检查一次
            }
        }

        public async Task<bool> UnmountVHD()
        {
            StatusChanged?.Invoke("正在解除VHD挂载...");
            
            try
            {
                // 直接分离VHD文件，不移除驱动器字母
                var diskpartScript = "select vdisk file=*\ndetach vdisk\nexit";
                var tempScript = Path.GetTempFileName();
                await File.WriteAllTextAsync(tempScript, diskpartScript);
                
                StatusChanged?.Invoke("正在执行VHD解除挂载脚本...");
                
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
    }
}