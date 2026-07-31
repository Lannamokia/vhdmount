#nullable enable
using System;
using System.IO;
using System.Linq;

namespace VHDMounter.SoftwareDeploy
{
    public static class DeploySecurityPolicy
    {
        public const long MaxPackageSizeBytes = 2L * 1024 * 1024 * 1024; // 2GB

        private static readonly string[] AllowedPrefixes = new[]
        {
            @"C:\SOFT\",
            Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData), "VHDMounter") + @"\",
        };

        private static readonly string[] AllowedScriptNames = new[]
        {
            "install.ps1", "uninstall.ps1",
        };

        public static bool IsValidTargetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            // 拒绝 UNC/扩展路径
            if (path.StartsWith(@"\\")) return false;

            string normalized;
            try
            {
                normalized = Path.GetFullPath(path);
            }
            catch
            {
                return false;
            }

            // 拒绝驱动器根目录
            if (normalized.Length <= 3) return false;

            // 白名单前缀检查
            return AllowedPrefixes.Any(prefix =>
                normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsValidScriptName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            // 拒绝包含路径分隔符或特殊字符的脚本名
            if (name.Contains(Path.DirectorySeparatorChar) || name.Contains(Path.AltDirectorySeparatorChar))
                return false;
            if (name.Contains('"') || name.Contains('\'') || name.Contains(';') || name.Contains('&') || name.Contains('|'))
                return false;
            return AllowedScriptNames.Contains(name, StringComparer.OrdinalIgnoreCase);
        }

        public static bool IsValidPackageSize(long size)
        {
            return size > 0 && size <= MaxPackageSizeBytes;
        }

        public static string? ValidateManifest(DeployManifest manifest)
        {
            if (manifest == null) return "manifest 为空";

            if (string.IsNullOrWhiteSpace(manifest.name)) return "name 不能为空";
            if (string.IsNullOrWhiteSpace(manifest.version)) return "version 不能为空";

            if (manifest.IsFileDeploy)
            {
                if (string.IsNullOrWhiteSpace(manifest.targetPath))
                    return "file-deploy 必须指定 targetPath";
                if (!IsValidTargetPath(manifest.targetPath))
                    return "targetPath 不合法或指向系统目录";
            }

            if (manifest.IsSoftwareDeploy)
            {
                if (string.IsNullOrWhiteSpace(manifest.installScript))
                    return "software-deploy 必须指定 installScript";
                if (!IsValidScriptName(manifest.installScript))
                    return $"不合法的脚本名: {manifest.installScript}";
                if (string.IsNullOrWhiteSpace(manifest.uninstallScript))
                    return "software-deploy 必须指定 uninstallScript";
                if (!IsValidScriptName(manifest.uninstallScript))
                    return $"不合法的脚本名: {manifest.uninstallScript}";
            }

            if (manifest.IsGameOptionDeploy)
            {
                if (!string.IsNullOrWhiteSpace(manifest.targetPath))
                    return "game-option-deploy 不允许指定 targetPath";
                if (!string.IsNullOrWhiteSpace(manifest.installScript))
                    return "game-option-deploy 不允许携带 installScript";
                if (!string.IsNullOrWhiteSpace(manifest.uninstallScript))
                    return "game-option-deploy 不允许携带 uninstallScript";
            }

            return null;
        }

        public static bool IsSystemPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            // A path is a system path if it's NOT in our whitelist
            return !IsValidTargetPath(path);
        }
    }
}
