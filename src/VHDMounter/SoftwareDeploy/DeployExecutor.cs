using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace VHDMounter.SoftwareDeploy
{
    public class DeployExecutionResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public int ExitCode { get; set; }
    }

    public static class DeployExecutor
    {
        public const long MaxDeployPayloadBytes = 2L * 1024 * 1024 * 1024; // 2GB

        public static bool CheckDiskSpace(string path, long requiredMB)
        {
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(path) ?? "C:\\");
                if (!drive.IsReady) return false;
                long availableMB = drive.AvailableFreeSpace / (1024 * 1024);
                return availableMB >= requiredMB;
            }
            catch { return false; }
        }

        public static bool StopProcesses(string[] processNames)
        {
            bool allStopped = true;
            foreach (var name in processNames ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                try
                {
                    var processes = Process.GetProcessesByName(name);
                    foreach (var p in processes)
                    {
                        try { p.Kill(); p.WaitForExit(5000); }
                        catch { allStopped = false; }
                    }
                }
                catch { allStopped = false; }
            }
            return allStopped;
        }

        public static DeployExecutionResult ExecuteSoftwareDeploy(string extractDir, DeployManifest manifest)
        {
            var result = new DeployExecutionResult();

            string scriptPath = Path.Combine(extractDir, manifest.installScript);
            if (!File.Exists(scriptPath))
            {
                result.ErrorMessage = "install.ps1 不存在";
                return result;
            }

            // 前置检查
            if (manifest.preCheck != null)
            {
                if (manifest.preCheck.minDiskSpaceMB > 0)
                {
                    string checkPath = !string.IsNullOrWhiteSpace(manifest.targetPath)
                        ? manifest.targetPath
                        : extractDir;
                    if (!CheckDiskSpace(checkPath, manifest.preCheck.minDiskSpaceMB))
                    {
                        result.ErrorMessage = $"磁盘空间不足，需要 {manifest.preCheck.minDiskSpaceMB}MB";
                        return result;
                    }
                }

                if (manifest.preCheck.stopProcesses?.Count > 0)
                {
                    StopProcesses(manifest.preCheck.stopProcesses.ToArray());
                }
            }

            // 执行 PowerShell
            string deployJsonPath = Path.Combine(extractDir, "deploy.json");
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\" -DeployJson \"{deployJsonPath}\"",
                UseShellExecute = true,
                Verb = manifest.requiresAdmin ? "runas" : null,
                WorkingDirectory = extractDir,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
            };

            try
            {
                using var process = Process.Start(psi);
                if (process == null)
                {
                    result.ErrorMessage = "无法启动 PowerShell 进程";
                    return result;
                }
                process.WaitForExit();
                result.ExitCode = process.ExitCode;
                result.Success = process.ExitCode == 0;
                if (!result.Success)
                {
                    result.ErrorMessage = $"install.ps1 退出码: {process.ExitCode}";
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"执行 install.ps1 异常: {ex.Message}";
            }

            return result;
        }

        public static DeployExecutionResult ExecuteFileDeploy(string extractDir, DeployManifest manifest)
        {
            var result = new DeployExecutionResult();

            string payloadDir = Path.Combine(extractDir, "payload");
            if (!Directory.Exists(payloadDir))
            {
                result.ErrorMessage = "payload 目录不存在";
                return result;
            }

            if (string.IsNullOrWhiteSpace(manifest.targetPath))
            {
                result.ErrorMessage = "file-deploy 缺少 targetPath";
                return result;
            }

            // 路径安全检查
            if (manifest.targetPath.Contains("..") || IsSystemPath(manifest.targetPath))
            {
                result.ErrorMessage = "targetPath 不合法";
                return result;
            }

            try
            {
                Directory.CreateDirectory(manifest.targetPath);
                CopyDirectory(payloadDir, manifest.targetPath);
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"文件复制失败: {ex.Message}";
            }

            return result;
        }

        public static DeployExecutionResult RollbackSoftwareDeploy(string extractDir, DeployManifest manifest)
        {
            var result = new DeployExecutionResult();

            string uninstallScript = Path.Combine(extractDir, "uninstall.ps1");
            if (!File.Exists(uninstallScript))
            {
                result.ErrorMessage = "uninstall.ps1 不存在，无法回滚";
                return result;
            }

            string deployJsonPath = Path.Combine(extractDir, "deploy.json");
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-ExecutionPolicy Bypass -File \"{uninstallScript}\" -DeployJson \"{deployJsonPath}\"",
                UseShellExecute = true,
                Verb = manifest.requiresAdmin ? "runas" : null,
                WorkingDirectory = extractDir,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
            };

            try
            {
                using var process = Process.Start(psi);
                if (process != null)
                {
                    process.WaitForExit();
                    result.ExitCode = process.ExitCode;
                    result.Success = process.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"回滚异常: {ex.Message}";
            }

            return result;
        }

        static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }

            foreach (var subDir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
                CopyDirectory(subDir, destSubDir);
            }
        }

        static bool IsSystemPath(string path)
        {
            var normalized = path.Trim().ToLowerInvariant();
            var systemPaths = new[]
            {
                @"c:\windows", @"c:\program files", @"c:\program files (x86)",
                @"c:\programdata", @"c:\users", @"c:\system",
            };
            return systemPaths.Any(sp => normalized.StartsWith(sp, StringComparison.OrdinalIgnoreCase));
        }
    }
}
