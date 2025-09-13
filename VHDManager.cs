using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading.Tasks;

namespace VHDMounter
{
    public class VHDManager
    {
        private const string TARGET_DRIVE = "M:";
        private readonly string[] TARGET_KEYWORDS = { "SDEZ", "SDHD", "SDDT" };
        private readonly string[] PROCESS_KEYWORDS = { "sinmai", "chusanapp", "mu3" };

        public event Action<string> StatusChanged;
        public event Action<List<string>> VHDFilesFound;
        
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
                // 先分离现有VHD（如果已挂载）
                await UnmountVHD();

                // 使用diskpart挂载VHD
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
                await process.WaitForExitAsync();
                
                File.Delete(tempScript);
                
                // 等待挂载完成
                await Task.Delay(2000);
                
                return Directory.Exists(TARGET_DRIVE);
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
                    return true;

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
                        CreateNoWindow = true
                    }
                };
                
                process.Start();
                await process.WaitForExitAsync();
                
                File.Delete(tempScript);
                return true;
            }
            catch
            {
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
                StatusChanged?.Invoke("未找到start.bat文件");
                return false;
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
                return true;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"启动失败: {ex.Message}");
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
                await process.WaitForExitAsync();
                
                File.Delete(tempScript);
                
                StatusChanged?.Invoke("VHD解除挂载完成");
                return true;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"解除VHD挂载失败: {ex.Message}");
                return false;
            }
        }
    }
}