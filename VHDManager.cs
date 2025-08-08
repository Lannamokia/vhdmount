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
                        var files = Directory.GetFiles(drive.RootDirectory.FullName, "*.vhd", SearchOption.AllDirectories)
                            .Where(f => TARGET_KEYWORDS.Any(keyword => Path.GetFileName(f).ToUpper().Contains(keyword)))
                            .ToList();
                        
                        vhdFiles.AddRange(files);
                    }
                    catch (Exception ex)
                    {
                        // 忽略无法访问的目录
                        Debug.WriteLine($"扫描驱动器 {drive.Name} 时出错: {ex.Message}");
                    }
                }
            });

            return vhdFiles;
        }

        public async Task<bool> MountVHD(string vhdPath)
        {
            StatusChanged?.Invoke($"正在挂载VHD文件: {Path.GetFileName(vhdPath)}");
            
            try
            {
                // 先卸载M盘（如果已挂载）
                await UnmountDrive();

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

        public async Task<string> FindPackageFolder()
        {
            StatusChanged?.Invoke("正在搜索package文件夹...");
            
            if (!Directory.Exists(TARGET_DRIVE))
                return null;

            return await Task.Run(() =>
            {
                try
                {
                    var directories = Directory.GetDirectories(TARGET_DRIVE, "*", SearchOption.AllDirectories)
                        .Where(d => Path.GetFileName(d).ToLower() == "package")
                        .FirstOrDefault();
                    
                    return directories;
                }
                catch
                {
                    return null;
                }
            });
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
    }
}