#nullable enable
using System;
using System.IO;
using System.Linq;

namespace VHDMounter.SoftwareDeploy
{
    public static class DeploySecurityPolicy
    {
        public const long MaxPackageSizeBytes = 2L * 1024 * 1024 * 1024; // 2GB

        private static readonly string[] SystemPaths = new[]
        {
            @"C:\Windows", @"C:\Program Files", @"C:\Program Files (x86)",
            @"C:\ProgramData", @"C:\Users", @"C:\System",
            @"C:\Windows\System32", @"C:\Windows\SysWOW64",
        };

        private static readonly string[] AllowedScriptNames = new[]
        {
            "install.ps1", "uninstall.ps1",
        };

        public static bool IsValidTargetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            // 禁止路径遍历（在 GetFullPath 解析前先检查）
            if (path.Contains("..")) return false;

            var normalized = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();

            // 禁止系统目录
            foreach (var sysPath in SystemPaths)
            {
                var sysNormalized = sysPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
                if (normalized.StartsWith(sysNormalized + "\\") || normalized == sysNormalized)
                    return false;
            }

            return true;
        }

        public static bool IsValidScriptName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
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
            }

            return null;
        }

        public static bool IsSystemPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var normalized = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();

            foreach (var sysPath in SystemPaths)
            {
                var sysNormalized = sysPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
                if (normalized.StartsWith(sysNormalized + "\\") || normalized == sysNormalized)
                    return true;
            }
            return false;
        }
    }
}
