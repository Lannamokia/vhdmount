using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

internal sealed record DecryptedVhdMountTestResult(
    string DecryptedVhdPath,
    string TargetDriveRoot,
    string TargetVolumeGuidPath,
    string ExpectedFolderName);

internal static class DecryptedVhdMountTester
{
    private static readonly string[] TargetKeywords = { "SDEZ", "SDGB", "SDHJ", "SDDT", "SDHD" };
    private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
    private const int ERROR_NO_MORE_FILES = 18;
    private const int ERROR_MORE_DATA = 234;

    private sealed class MountedVolumeInfo
    {
        public string VolumeGuidPath { get; set; } = string.Empty;
        public string? FileSystem { get; set; }
        public List<string> AccessPaths { get; set; } = new();
        public List<string> SampleEntries { get; set; } = new();
        public bool HasPackageFolder { get; set; }
        public bool HasBinFolder { get; set; }
        public bool HasStartBat { get; set; }
        public bool HasStartGameBat { get; set; }
        public bool HasDriveLetter => AccessPaths.Any(IsDriveLetterPath);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindFirstVolume(StringBuilder lpszVolumeName, uint cchBufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool FindNextVolume(IntPtr hFindVolume, StringBuilder lpszVolumeName, uint cchBufferLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FindVolumeClose(IntPtr hFindVolume);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumePathNamesForVolumeName(string lpszVolumeName, [Out] char[] lpszVolumePathNames, uint cchBufferLength, out uint lpcchReturnLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeInformation(string lpRootPathName, StringBuilder lpVolumeNameBuffer, uint nVolumeNameSize, out uint lpVolumeSerialNumber, out uint lpMaximumComponentLength, out uint lpFileSystemFlags, StringBuilder lpFileSystemNameBuffer, uint nFileSystemNameSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeNameForVolumeMountPoint(string lpszVolumeMountPoint, StringBuilder lpszVolumeName, uint cchBufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool DeleteVolumeMountPoint(string lpszVolumeMountPoint);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetVolumeMountPoint(string lpszVolumeMountPoint, string lpszVolumeName);

    public static string NormalizeTargetDrive(string targetDrive)
    {
        if (string.IsNullOrWhiteSpace(targetDrive))
        {
            return @"M:\";
        }

        var trimmed = targetDrive.Trim();
        if (Regex.IsMatch(trimmed, "^[A-Za-z]:\\?$"))
        {
            return trimmed.EndsWith("\\", StringComparison.Ordinal) ? trimmed : trimmed + "\\";
        }

        var fullPath = Path.GetFullPath(trimmed);
        return fullPath.EndsWith("\\", StringComparison.Ordinal) ? fullPath : fullPath + "\\";
    }

    public static async Task<DecryptedVhdMountTestResult?> TryMountAndValidateAsync(
        string evhdPath,
        string evhdMountRoot,
        string targetDriveRoot,
        int timeoutMs,
        Action<string> log,
        Action<string> logError)
    {
        var normalizedTargetDrive = NormalizeTargetDrive(targetDriveRoot);
        var expectedFolderName = Path.GetFileName(evhdPath).IndexOf("SDHD", StringComparison.OrdinalIgnoreCase) >= 0 ? "bin" : "package";
        var decryptedVhdPath = await WaitForDecryptedVhdAsync(evhdMountRoot, evhdPath, timeoutMs, log, logError);
        if (string.IsNullOrWhiteSpace(decryptedVhdPath))
        {
            return null;
        }

        var volumesBefore = CaptureCurrentVolumes();
        log($"VHD_MOUNT_TEST_START: DecryptedVhd={decryptedVhdPath}, TargetDrive={normalizedTargetDrive}, ExpectedFolder={expectedFolderName}, TimeoutMs={timeoutMs}");

        if (!await AttachVhdAsync(decryptedVhdPath, log, logError))
        {
            return null;
        }

        var keepMounted = false;
        try
        {
            var candidates = await WaitForMountedVolumeCandidatesAsync(volumesBefore, expectedFolderName, normalizedTargetDrive, timeoutMs, log);
            if (candidates.Count == 0)
            {
                logError("VHD_MOUNT_TEST_ERROR: 未识别到解密后 VHD 新增的数据卷。");
                return null;
            }

            foreach (var candidate in candidates)
            {
                log($"VHD_MOUNT_TEST_CANDIDATE: Volume={candidate.VolumeGuidPath}, FS={candidate.FileSystem ?? "(unknown)"}, Paths={FormatAccessPaths(candidate.AccessPaths)}, Sample={FormatSampleEntries(candidate.SampleEntries)}");

                if (!await TryAssignTargetDriveLetterAsync(candidate, normalizedTargetDrive, log, logError))
                {
                    continue;
                }

                var expectedFolderPath = Path.Combine(normalizedTargetDrive, expectedFolderName);
                if (!Directory.Exists(expectedFolderPath))
                {
                    log($"VHD_MOUNT_TEST_MISMATCH: 目标盘 {normalizedTargetDrive} 未找到 {expectedFolderName}，继续尝试下一候选卷。");
                    await ClearTargetDriveMountPointAsync(normalizedTargetDrive, log, logError, logStatus: false);
                    continue;
                }

                LogDirectorySnapshot("VHD_TEST_TARGET_DRIVE", normalizedTargetDrive, log);
                log($"VHD_MOUNT_TEST_SUCCESS: Volume={candidate.VolumeGuidPath}, TargetDrive={normalizedTargetDrive}, ExpectedFolder={expectedFolderPath}");
                keepMounted = true;
                return new DecryptedVhdMountTestResult(decryptedVhdPath, normalizedTargetDrive, candidate.VolumeGuidPath, expectedFolderName);
            }

            logError("VHD_MOUNT_TEST_ERROR: 所有候选卷都未通过挂载验证。");
            return null;
        }
        finally
        {
            if (!keepMounted)
            {
                await ClearTargetDriveMountPointAsync(normalizedTargetDrive, log, logError, logStatus: false);
                await DetachVhdAsync(decryptedVhdPath, log, logError);
            }
        }
    }

    public static async Task<bool> CleanupAsync(DecryptedVhdMountTestResult result, Action<string> log, Action<string> logError)
    {
        var cleared = await ClearTargetDriveMountPointAsync(result.TargetDriveRoot, log, logError, logStatus: true);
        var detached = await DetachVhdAsync(result.DecryptedVhdPath, log, logError);
        return cleared && detached;
    }

    private static async Task<string?> WaitForDecryptedVhdAsync(string evhdMountRoot, string evhdPath, int timeoutMs, Action<string> log, Action<string> logError)
    {
        var keyword = ExtractKeyword(Path.GetFileName(evhdPath));
        var sw = Stopwatch.StartNew();
        var loggedSnapshot = false;

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            try
            {
                if (Directory.Exists(evhdMountRoot))
                {
                    if (!loggedSnapshot)
                    {
                        LogDirectorySnapshot("VHD_SEARCH_ROOT", evhdMountRoot, log);
                        loggedSnapshot = true;
                    }

                    var allVhds = Directory.GetFiles(evhdMountRoot, "*.vhd", SearchOption.TopDirectoryOnly)
                        .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    var filtered = allVhds
                        .Where(path => string.IsNullOrEmpty(keyword) || string.Equals(ExtractKeyword(Path.GetFileName(path)), keyword, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    var selected = filtered.FirstOrDefault() ?? allVhds.FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(selected))
                    {
                        log($"VHD_MOUNT_TEST_SELECTED: Keyword={keyword}, File={selected}");
                        return selected;
                    }
                }
            }
            catch (Exception ex)
            {
                logError($"VHD_MOUNT_TEST_SEARCH_EXCEPTION: {ex.Message}");
            }

            await Task.Delay(500);
        }

        logError("VHD_MOUNT_TEST_ERROR: 在 EVHD 挂载点中未找到解密后的 VHD 文件。");
        return null;
    }

    private static async Task<bool> AttachVhdAsync(string vhdPath, Action<string> log, Action<string> logError)
    {
        var script = $@"select vdisk file=""{vhdPath}""
attach vdisk
exit";
        var result = await RunDiskPartScriptWithResultAsync(script, "attach-vhd", log, logError);
        if (result.TimedOut)
        {
            return false;
        }

        if (result.ExitCode == 0)
        {
            return true;
        }

        if (OutputContainsAny(result.StandardError, result.StandardOutput, "already attached", "already connected", "已经连接", "已连接"))
        {
            log("VHD_MOUNT_TEST_WARN: diskpart 报告目标 VHD 已连接，将继续后续卷识别流程。");
            return true;
        }

        logError($"VHD_MOUNT_TEST_ERROR: diskpart attach-vhd 退出码 {result.ExitCode}");
        return false;
    }

    private static async Task<bool> DetachVhdAsync(string vhdPath, Action<string> log, Action<string> logError)
    {
        if (!string.IsNullOrWhiteSpace(vhdPath) && File.Exists(vhdPath))
        {
            var script = $@"select vdisk file=""{vhdPath}""
detach vdisk
exit";
            var detachResult = await RunDiskPartScriptWithResultAsync(script, "detach-vhd", log, logError);
            if (detachResult.TimedOut)
            {
                return false;
            }

            if (detachResult.ExitCode == 0)
            {
                return true;
            }

            if (OutputContainsAny(detachResult.StandardError, detachResult.StandardOutput, "not attached", "not currently attached", "尚未连接", "未连接", "未附加"))
            {
                log("VHD_MOUNT_TEST_WARN: 目标 VHD 当前未连接，无需分离。");
                return true;
            }

            log("VHD_MOUNT_TEST_WARN: 按文件路径分离 VHD 失败。由于缺少可靠的无路径 fallback，本次不再使用 select vdisk file=*。");
            return false;
        }

        log("VHD_MOUNT_TEST_WARN: 原始 VHD 路径已不可访问，跳过 detach；避免使用无效的 select vdisk file=* fallback。");
        return false;
    }

    private sealed record DiskPartCommandResult(bool TimedOut, int ExitCode, string StandardOutput, string StandardError);

    private static bool OutputContainsAny(string stderr, string stdout, params string[] needles)
    {
        var combined = string.Concat(stderr ?? string.Empty, "\n", stdout ?? string.Empty);
        return needles.Any(needle => combined.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static async Task<bool> RunDiskPartScriptAsync(string script, string operation, Action<string> log, Action<string> logError, int timeoutMs = 30000)
    {
        var result = await RunDiskPartScriptWithResultAsync(script, operation, log, logError, timeoutMs);
        return !result.TimedOut && result.ExitCode == 0;
    }

    private static async Task<DiskPartCommandResult> RunDiskPartScriptWithResultAsync(string script, string operation, Action<string> log, Action<string> logError, int timeoutMs = 30000)
    {
        var tempScript = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempScript, script, Encoding.ASCII);

        try
        {
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
            var timeoutTask = Task.Delay(timeoutMs);
            var completed = await Task.WhenAny(waitTask, timeoutTask);
            if (completed == timeoutTask)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                logError($"VHD_MOUNT_TEST_ERROR: diskpart {operation} 超时。");
                return new DiskPartCommandResult(true, process.HasExited ? process.ExitCode : -1, string.Empty, string.Empty);
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            if (!string.IsNullOrWhiteSpace(output))
            {
                log($"VHD_MOUNT_TEST_DISKPART_{operation.ToUpperInvariant()}_OUTPUT:\n{output.TrimEnd()}");
            }
            if (!string.IsNullOrWhiteSpace(error))
            {
                logError($"VHD_MOUNT_TEST_DISKPART_{operation.ToUpperInvariant()}_ERROR:\n{error.TrimEnd()}");
            }

            return new DiskPartCommandResult(false, process.ExitCode, output, error);
        }
        finally
        {
            try
            {
                if (File.Exists(tempScript))
                {
                    File.Delete(tempScript);
                }
            }
            catch
            {
            }
        }
    }

    private static Dictionary<string, MountedVolumeInfo> CaptureCurrentVolumes()
    {
        var result = new Dictionary<string, MountedVolumeInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var volumeGuidPath in EnumerateVolumeGuidPaths())
        {
            var info = InspectMountedVolume(volumeGuidPath);
            result[info.VolumeGuidPath] = info;
        }
        return result;
    }

    private static List<string> EnumerateVolumeGuidPaths()
    {
        var result = new List<string>();
        var buffer = new StringBuilder(1024);
        var handle = FindFirstVolume(buffer, (uint)buffer.Capacity);
        if (handle == INVALID_HANDLE_VALUE)
        {
            return result;
        }

        try
        {
            while (true)
            {
                var volumeGuidPath = NormalizeVolumeId(buffer.ToString());
                if (!string.IsNullOrWhiteSpace(volumeGuidPath))
                {
                    result.Add(volumeGuidPath);
                }

                buffer.Clear();
                if (!FindNextVolume(handle, buffer, (uint)buffer.Capacity))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error != ERROR_NO_MORE_FILES)
                    {
                        break;
                    }
                    break;
                }
            }
        }
        finally
        {
            FindVolumeClose(handle);
        }

        return result;
    }

    private static MountedVolumeInfo InspectMountedVolume(string volumeGuidPath)
    {
        var normalized = NormalizeVolumeId(volumeGuidPath);
        return new MountedVolumeInfo
        {
            VolumeGuidPath = normalized,
            AccessPaths = GetVolumeAccessPaths(normalized),
            FileSystem = GetVolumeFileSystem(normalized),
            SampleEntries = GetVolumeEntrySample(normalized),
            HasPackageFolder = DirectoryExistsSafe(normalized, "package"),
            HasBinFolder = DirectoryExistsSafe(normalized, "bin"),
            HasStartBat = FileExistsSafe(normalized, "start.bat"),
            HasStartGameBat = FileExistsSafe(normalized, "start_game.bat")
        };
    }

    private static List<string> GetVolumeAccessPaths(string volumeGuidPath)
    {
        var buffer = new char[256];
        if (!GetVolumePathNamesForVolumeName(volumeGuidPath, buffer, (uint)buffer.Length, out var requiredLength))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ERROR_MORE_DATA || requiredLength == 0)
            {
                return new List<string>();
            }

            buffer = new char[requiredLength];
            if (!GetVolumePathNamesForVolumeName(volumeGuidPath, buffer, (uint)buffer.Length, out requiredLength))
            {
                return new List<string>();
            }
        }

        return ParseMultiStringBuffer(buffer);
    }

    private static string? GetVolumeFileSystem(string volumeGuidPath)
    {
        var volumeName = new StringBuilder(260);
        var fileSystemName = new StringBuilder(64);
        if (!GetVolumeInformation(volumeGuidPath, volumeName, (uint)volumeName.Capacity, out _, out _, out _, fileSystemName, (uint)fileSystemName.Capacity))
        {
            return null;
        }

        return fileSystemName.Length == 0 ? null : fileSystemName.ToString();
    }

    private static List<string> GetVolumeEntrySample(string volumeGuidPath, int limit = 8)
    {
        try
        {
            if (!Directory.Exists(volumeGuidPath))
            {
                return new List<string>();
            }

            return Directory.EnumerateFileSystemEntries(volumeGuidPath, "*", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Take(limit)
                .ToList()!;
        }
        catch
        {
            return new List<string>();
        }
    }

    private static bool DirectoryExistsSafe(string volumeGuidPath, string childName)
    {
        try
        {
            return Directory.Exists(Path.Combine(volumeGuidPath, childName));
        }
        catch
        {
            return false;
        }
    }

    private static bool FileExistsSafe(string volumeGuidPath, string childName)
    {
        try
        {
            return File.Exists(Path.Combine(volumeGuidPath, childName));
        }
        catch
        {
            return false;
        }
    }

    private static List<string> ParseMultiStringBuffer(char[] buffer)
    {
        var result = new List<string>();
        var start = 0;
        for (var i = 0; i < buffer.Length; i++)
        {
            if (buffer[i] != '\0')
            {
                continue;
            }

            if (i == start)
            {
                break;
            }

            result.Add(new string(buffer, start, i - start));
            start = i + 1;
        }
        return result;
    }

    private static bool IsDriveLetterPath(string path)
    {
        var value = (path ?? string.Empty).Trim();
        return value.Length >= 3 && char.IsLetter(value[0]) && value[1] == ':' && value[2] == '\\';
    }

    private static int ScoreMountedVolume(MountedVolumeInfo volume, string expectedFolderName, string targetDriveRoot)
    {
        var score = 0;
        var targetDrive = targetDriveRoot.TrimEnd('\\');

        if (volume.AccessPaths.Any(path => string.Equals(path.TrimEnd('\\'), targetDrive, StringComparison.OrdinalIgnoreCase)))
        {
            score += 1000;
        }

        if (string.Equals(volume.FileSystem, "NTFS", StringComparison.OrdinalIgnoreCase))
        {
            score += 500;
        }
        else if (string.Equals(volume.FileSystem, "exFAT", StringComparison.OrdinalIgnoreCase))
        {
            score += 250;
        }
        else if (!string.IsNullOrWhiteSpace(volume.FileSystem) && volume.FileSystem.StartsWith("FAT", StringComparison.OrdinalIgnoreCase))
        {
            score -= 120;
        }

        if (volume.AccessPaths.Count == 0)
        {
            score += 80;
        }
        else if (volume.HasDriveLetter)
        {
            score -= 20;
        }
        else
        {
            score += 20;
        }

        if (string.Equals(expectedFolderName, "package", StringComparison.OrdinalIgnoreCase) && volume.HasPackageFolder)
        {
            score += 700;
        }

        if (string.Equals(expectedFolderName, "bin", StringComparison.OrdinalIgnoreCase) && volume.HasBinFolder)
        {
            score += 700;
        }

        if (volume.HasPackageFolder)
        {
            score += 120;
        }
        if (volume.HasBinFolder)
        {
            score += 120;
        }
        if (volume.HasStartBat)
        {
            score += 100;
        }
        if (volume.HasStartGameBat)
        {
            score += 100;
        }
        if (volume.SampleEntries.Count > 0)
        {
            score += 10;
        }

        return score;
    }

    private static async Task<List<MountedVolumeInfo>> WaitForMountedVolumeCandidatesAsync(
        Dictionary<string, MountedVolumeInfo> volumesBefore,
        string expectedFolderName,
        string targetDriveRoot,
        int timeoutMs,
        Action<string> log)
    {
        var sw = Stopwatch.StartNew();
        long firstSeenAtMs = -1;
        string? lastSignature = null;

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            await Task.Delay(500);
            var volumesAfter = CaptureCurrentVolumes();
            var newVolumes = volumesAfter.Values
                .Where(v => !volumesBefore.ContainsKey(v.VolumeGuidPath))
                .OrderByDescending(v => ScoreMountedVolume(v, expectedFolderName, targetDriveRoot))
                .ThenBy(v => v.VolumeGuidPath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (newVolumes.Count == 0)
            {
                continue;
            }

            if (firstSeenAtMs < 0)
            {
                firstSeenAtMs = sw.ElapsedMilliseconds;
            }

            var signature = string.Join("|", newVolumes.Select(v => $"{v.VolumeGuidPath}:{v.FileSystem}:{string.Join(",", v.AccessPaths)}"));
            if (!string.Equals(signature, lastSignature, StringComparison.Ordinal))
            {
                lastSignature = signature;
                foreach (var volume in newVolumes)
                {
                    log($"VHD_MOUNT_TEST_VOLUME_DISCOVERED: Volume={volume.VolumeGuidPath}, FS={volume.FileSystem ?? "(unknown)"}, Paths={FormatAccessPaths(volume.AccessPaths)}, Sample={FormatSampleEntries(volume.SampleEntries)}");
                }
            }

            if (sw.ElapsedMilliseconds - firstSeenAtMs < 2000)
            {
                continue;
            }

            return newVolumes;
        }

        return new List<MountedVolumeInfo>();
    }

    private static async Task<bool> TryAssignTargetDriveLetterAsync(MountedVolumeInfo volume, string targetDriveRoot, Action<string> log, Action<string> logError)
    {
        var normalizedVolumeGuidPath = NormalizeVolumeId(volume.VolumeGuidPath);
        var currentTarget = TryGetMountedVolumeGuidForMountPoint(targetDriveRoot);
        if (string.Equals(currentTarget, normalizedVolumeGuidPath, StringComparison.OrdinalIgnoreCase) && Directory.Exists(targetDriveRoot))
        {
            log($"VHD_MOUNT_TEST_ASSIGN: {targetDriveRoot} 已指向目标卷 {normalizedVolumeGuidPath}");
            return true;
        }

        if (!await ClearTargetDriveMountPointAsync(targetDriveRoot, log, logError, logStatus: false))
        {
            return false;
        }

        if (!await RemoveCandidateDriveLettersAsync(normalizedVolumeGuidPath, volume.AccessPaths, targetDriveRoot, log, logError))
        {
            return false;
        }

        if (SetVolumeMountPoint(targetDriveRoot, normalizedVolumeGuidPath))
        {
            if (await WaitForTargetDriveStateAsync(normalizedVolumeGuidPath, targetDriveRoot, 5000))
            {
                log($"VHD_MOUNT_TEST_ASSIGN: SetVolumeMountPoint 已将 {normalizedVolumeGuidPath} 绑定到 {targetDriveRoot}");
                return true;
            }

            logError($"VHD_MOUNT_TEST_ERROR: SetVolumeMountPoint 返回成功，但 {targetDriveRoot} 校验未通过。");
        }
        else
        {
            logError($"VHD_MOUNT_TEST_ERROR: SetVolumeMountPoint 失败。Error={FormatWin32Error(Marshal.GetLastWin32Error())}");
        }

        log("VHD_MOUNT_TEST_ASSIGN: SetVolumeMountPoint 失败，尝试 mountvol 兜底...");
        if (!await TryRunMountVolAsync(new[] { targetDriveRoot, normalizedVolumeGuidPath }, "assign-target-drive", log, logError))
        {
            return false;
        }

        return await WaitForTargetDriveStateAsync(normalizedVolumeGuidPath, targetDriveRoot, 5000);
    }

    private static async Task<bool> RemoveCandidateDriveLettersAsync(string volumeGuidPath, IEnumerable<string> accessPaths, string targetDriveRoot, Action<string> log, Action<string> logError)
    {
        var driveLetterPaths = accessPaths
            .Where(IsDriveLetterPath)
            .Select(NormalizeDriveRootPath)
            .Where(path => !string.Equals(path, targetDriveRoot, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (driveLetterPaths.Count == 0)
        {
            return true;
        }

        log($"VHD_MOUNT_TEST_ASSIGN: 候选卷已有盘符 {FormatAccessPaths(driveLetterPaths)}，准备切换到 {targetDriveRoot}");

        foreach (var driveLetterPath in driveLetterPaths)
        {
            if (!DeleteVolumeMountPoint(driveLetterPath))
            {
                logError($"VHD_MOUNT_TEST_WARN: DeleteVolumeMountPoint({driveLetterPath}) 失败。Error={FormatWin32Error(Marshal.GetLastWin32Error())}");
                if (!await TryRunMountVolAsync(new[] { driveLetterPath, "/d" }, $"clear-candidate-drive-{driveLetterPath.TrimEnd('\\')}", log, logError))
                {
                    return false;
                }
            }
        }

        if (!await WaitForVolumeDriveLettersClearedAsync(volumeGuidPath, targetDriveRoot, 5000))
        {
            logError($"VHD_MOUNT_TEST_ERROR: 候选卷 {volumeGuidPath} 的旧盘符未能移除。CurrentPaths={FormatAccessPaths(GetVolumeAccessPaths(volumeGuidPath))}");
            return false;
        }

        return true;
    }

    private static async Task<bool> ClearTargetDriveMountPointAsync(string targetDriveRoot, Action<string> log, Action<string> logError, bool logStatus)
    {
        var currentVolume = TryGetMountedVolumeGuidForMountPoint(targetDriveRoot);
        if (string.IsNullOrEmpty(currentVolume) && !Directory.Exists(targetDriveRoot))
        {
            return true;
        }

        if (logStatus)
        {
            log($"VHD_MOUNT_TEST_CLEANUP: 检测到旧的目标盘映射 {targetDriveRoot} -> {currentVolume ?? "(unknown)"}，正在清理...");
        }

        if (!DeleteVolumeMountPoint(targetDriveRoot))
        {
            logError($"VHD_MOUNT_TEST_WARN: DeleteVolumeMountPoint 失败。Error={FormatWin32Error(Marshal.GetLastWin32Error())}");
            if (!await TryRunMountVolAsync(new[] { targetDriveRoot, "/d" }, "clear-target-drive", log, logError))
            {
                return false;
            }
        }

        return await WaitForTargetDriveStateAsync(null, targetDriveRoot, 5000);
    }

    private static async Task<bool> WaitForTargetDriveStateAsync(string? expectedVolumeGuidPath, string targetDriveRoot, int timeoutMs)
    {
        var normalizedExpected = string.IsNullOrWhiteSpace(expectedVolumeGuidPath) ? null : NormalizeVolumeId(expectedVolumeGuidPath);
        var sw = Stopwatch.StartNew();

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var currentVolume = TryGetMountedVolumeGuidForMountPoint(targetDriveRoot);
            if (string.IsNullOrEmpty(normalizedExpected))
            {
                if (string.IsNullOrEmpty(currentVolume) && !Directory.Exists(targetDriveRoot))
                {
                    return true;
                }
            }
            else if (string.Equals(currentVolume, normalizedExpected, StringComparison.OrdinalIgnoreCase) && Directory.Exists(targetDriveRoot))
            {
                return true;
            }

            await Task.Delay(200);
        }

        return false;
    }

    private static async Task<bool> WaitForVolumeDriveLettersClearedAsync(string volumeGuidPath, string targetDriveRoot, int timeoutMs)
    {
        var normalizedVolumeGuidPath = NormalizeVolumeId(volumeGuidPath);
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var currentPaths = GetVolumeAccessPaths(normalizedVolumeGuidPath);
            if (!currentPaths.Any(path => IsDriveLetterPath(path) && !string.Equals(NormalizeDriveRootPath(path), targetDriveRoot, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            await Task.Delay(200);
        }

        return false;
    }

    private static async Task<bool> TryRunMountVolAsync(IEnumerable<string> arguments, string operation, Action<string> log, Action<string> logError)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "mountvol",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var waitTask = process.WaitForExitAsync();
        var timeoutTask = Task.Delay(20000);
        var completed = await Task.WhenAny(waitTask, timeoutTask);
        if (completed == timeoutTask)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            logError($"VHD_MOUNT_TEST_ERROR: mountvol {operation} 超时。");
            return false;
        }

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        if (!string.IsNullOrWhiteSpace(output))
        {
            log($"VHD_MOUNT_TEST_MOUNTVOL_{operation.ToUpperInvariant()}_OUTPUT:\n{output.TrimEnd()}");
        }
        if (!string.IsNullOrWhiteSpace(error))
        {
            logError($"VHD_MOUNT_TEST_MOUNTVOL_{operation.ToUpperInvariant()}_ERROR:\n{error.TrimEnd()}");
        }

        if (process.ExitCode != 0)
        {
            logError($"VHD_MOUNT_TEST_ERROR: mountvol {operation} 退出码 {process.ExitCode}");
            return false;
        }

        return true;
    }

    private static string? TryGetMountedVolumeGuidForMountPoint(string mountPoint)
    {
        var buffer = new StringBuilder(64);
        if (!GetVolumeNameForVolumeMountPoint(mountPoint, buffer, (uint)buffer.Capacity))
        {
            return null;
        }

        return NormalizeVolumeId(buffer.ToString());
    }

    private static string NormalizeVolumeId(string id)
    {
        var value = (id ?? string.Empty).Trim();
        if (!value.EndsWith("\\", StringComparison.Ordinal))
        {
            value += "\\";
        }
        return value;
    }

    private static string ExtractKeyword(string fileName)
    {
        foreach (var keyword in TargetKeywords)
        {
            if ((fileName ?? string.Empty).ToUpperInvariant().Contains(keyword, StringComparison.Ordinal))
            {
                return keyword;
            }
        }

        return string.Empty;
    }

    private static void LogDirectorySnapshot(string phase, string path, Action<string> log)
    {
        try
        {
            var exists = Directory.Exists(path);
            log($"VHD_MOUNT_TEST_{phase}: Directory={path} Exists={exists}");
            if (!exists)
            {
                return;
            }

            var entries = Directory.EnumerateFileSystemEntries(path, "*", SearchOption.TopDirectoryOnly)
                .Take(20)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();

            log($"VHD_MOUNT_TEST_{phase}: EntryCountSample={entries.Count}");
            foreach (var entry in entries)
            {
                log($"VHD_MOUNT_TEST_{phase}_ENTRY: {entry}");
            }
        }
        catch (Exception ex)
        {
            log($"VHD_MOUNT_TEST_{phase}_SNAPSHOT_EXCEPTION: {ex.Message}");
        }
    }

    private static string FormatAccessPaths(IEnumerable<string> accessPaths)
    {
        var paths = accessPaths.Where(path => !string.IsNullOrWhiteSpace(path)).ToList();
        return paths.Count == 0 ? "(none)" : string.Join(", ", paths);
    }

    private static string FormatSampleEntries(IEnumerable<string> sampleEntries)
    {
        var entries = sampleEntries.Where(entry => !string.IsNullOrWhiteSpace(entry)).ToList();
        return entries.Count == 0 ? "(none)" : string.Join(", ", entries);
    }

    private static string NormalizeDriveRootPath(string path)
    {
        var value = (path ?? string.Empty).Trim();
        if (value.Length == 2 && char.IsLetter(value[0]) && value[1] == ':')
        {
            value += "\\";
        }
        else if (value.Length >= 2 && char.IsLetter(value[0]) && value[1] == ':' && !value.EndsWith("\\", StringComparison.Ordinal))
        {
            value += "\\";
        }

        return value;
    }

    private static string FormatWin32Error(int errorCode)
    {
        return $"{errorCode}: {new Win32Exception(errorCode).Message}";
    }
}