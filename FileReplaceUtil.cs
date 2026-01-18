using System;
using System.IO;
using System.Runtime.InteropServices;

namespace VHDMounter
{
    public static class FileReplaceUtil
    {
        private const int MOVEFILE_REPLACE_EXISTING = 0x1;
        private const int MOVEFILE_DELAY_UNTIL_REBOOT = 0x4;
        private const int MOVEFILE_WRITE_THROUGH = 0x8;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool MoveFileEx(string lpExistingFileName, string lpNewFileName, int dwFlags);

        public static void EnsureDirectory(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        public static bool AtomicReplace(string stagedPath, string targetPath)
        {
            EnsureDirectory(targetPath);
            return MoveFileEx(stagedPath, targetPath, MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH);
        }

        public static bool DelayReplaceUntilReboot(string stagedPath, string targetPath)
        {
            EnsureDirectory(targetPath);
            return MoveFileEx(stagedPath, targetPath, MOVEFILE_REPLACE_EXISTING | MOVEFILE_DELAY_UNTIL_REBOOT);
        }
    }
}
