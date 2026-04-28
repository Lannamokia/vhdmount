using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace VHDMounter.SoftwareDeploy
{
    public class UninstallResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public static class DeployUninstaller
    {
        public static UninstallResult UninstallSoftware(DeployRecord record)
        {
            var result = new UninstallResult();

            if (string.IsNullOrWhiteSpace(record.targetPath))
            {
                result.ErrorMessage = "software-deploy 缺少本地安装目录";
                return result;
            }

            string uninstallScriptName = string.IsNullOrWhiteSpace(record.uninstallScript)
                ? "uninstall.ps1"
                : record.uninstallScript;
            string uninstallScript = Path.Combine(record.targetPath, uninstallScriptName);
            if (!File.Exists(uninstallScript))
            {
                result.ErrorMessage = "uninstall.ps1 不存在";
                return result;
            }

            string deployJsonPath = Path.Combine(record.targetPath, "deploy.json");
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-ExecutionPolicy Bypass -File \"{uninstallScript}\" -DeployJson \"{deployJsonPath}\"",
                UseShellExecute = true,
                Verb = record.requiresAdmin ? "runas" : null,
                WorkingDirectory = record.targetPath,
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
                result.Success = process.ExitCode == 0;
                if (!result.Success)
                {
                    result.ErrorMessage = $"uninstall.ps1 退出码: {process.ExitCode}";
                }
                else
                {
                    TryDeleteDirectory(record.targetPath);
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"执行 uninstall.ps1 异常: {ex.Message}";
            }

            return result;
        }

        public static UninstallResult UninstallFiles(string extractDir, DeployRecord record)
        {
            var result = new UninstallResult();

            if (record.fileManifest == null || record.fileManifest.Count == 0)
            {
                result.ErrorMessage = "fileManifest 为空，无法确定要删除的文件";
                return result;
            }

            // 按清单逐条删除文件
            foreach (var filePath in record.fileManifest)
            {
                try
                {
                    if (File.Exists(filePath))
                        File.Delete(filePath);
                }
                catch { /* 忽略单个文件删除失败 */ }
            }

            // 删除因此变空的目录（从下往上删）
            var directories = record.fileManifest
                .Select(f => Path.GetDirectoryName(f))
                .Where(d => !string.IsNullOrEmpty(d))
                .Distinct()
                .OrderByDescending(d => d.Length);

            foreach (var dir in directories)
            {
                try
                {
                    if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                        Directory.Delete(dir);
                }
                catch { }
            }

            result.Success = true;
            return result;
        }

        private static void TryDeleteDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return;
            }

            try
            {
                Directory.Delete(path, true);
            }
            catch
            {
            }
        }
    }
}
