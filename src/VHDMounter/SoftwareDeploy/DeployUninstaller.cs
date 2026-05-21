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

            // 脚本名白名单校验
            if (!DeploySecurityPolicy.IsValidScriptName(uninstallScriptName))
            {
                result.ErrorMessage = $"不合法的卸载脚本名: {uninstallScriptName}";
                return result;
            }

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
                UseShellExecute = true,
                Verb = record.requiresAdmin ? "runas" : null,
                WorkingDirectory = record.targetPath,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
            };
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-File");
            psi.ArgumentList.Add(uninstallScript);
            psi.ArgumentList.Add("-DeployJson");
            psi.ArgumentList.Add(deployJsonPath);

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

            var failures = new System.Collections.Generic.List<string>();

            // 计算基准路径用于边界校验
            string basePath = string.IsNullOrWhiteSpace(record.targetPath)
                ? string.Empty
                : Path.GetFullPath(record.targetPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            // 按清单逐条删除文件
            foreach (var filePath in record.fileManifest)
            {
                // 路径边界校验：确保文件在 targetPath 内
                if (!string.IsNullOrEmpty(basePath))
                {
                    try
                    {
                        string fullPath = Path.GetFullPath(filePath);
                        if (!fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
                        {
                            Trace.WriteLine($"[Uninstall] 拒绝越界路径: {filePath}");
                            failures.Add($"{filePath}: 路径越界");
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"[Uninstall] 路径规范化失败: {filePath} - {ex.Message}");
                        failures.Add($"{filePath}: 路径无效");
                        continue;
                    }
                }

                try
                {
                    if (File.Exists(filePath))
                        File.Delete(filePath);
                }
                catch (Exception ex)
                {
                    failures.Add($"{filePath}: {ex.Message}");
                }
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
                catch (Exception ex)
                {
                    failures.Add($"{dir}: {ex.Message}");
                }
            }

            result.Success = failures.Count == 0;
            if (!result.Success)
            {
                result.ErrorMessage = "部分文件/目录删除失败: " + string.Join(" | ", failures);
            }
            return result;
        }

        private static bool TryDeleteDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return false;
            }

            try
            {
                Directory.Delete(path, true);
                return true;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Uninstall] 目录删除失败 ({path}): {ex.Message}");
                return false;
            }
        }
    }
}
