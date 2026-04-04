using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

internal static class Program
{
    private readonly record struct PasswordWriteInfo(
        int OriginalLength,
        int SentLength,
        bool HadLeadingBom,
        bool TrimmedTrailingTerminators,
        PasswordLineEnding LineEnding,
        bool ClosedStandardInput,
        int WriteDelayMs);

    private enum OutputMode
    {
        Redirect,
        Inherit
    }

    private enum PasswordLineEnding
    {
        Lf,
        CrLf,
        None
    }

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = ParseArguments(args);
            if (options.ShowHelp)
            {
                PrintUsage();
                return 0;
            }

            if (!options.IsValid(out var validationError))
            {
                Console.Error.WriteLine($"参数错误: {validationError}");
                Console.Error.WriteLine();
                PrintUsage();
                return 2;
            }

            var evhdPath = Path.GetFullPath(options.EvhdPath!);
            if (!File.Exists(evhdPath))
            {
                Console.Error.WriteLine($"EVHD 文件不存在: {evhdPath}");
                return 2;
            }

            var mountPoint = NormalizeMountPoint(options.MountPoint!);
            var executablePath = ResolveExecutableFromPath(options.ToolPath ?? "encrypted-vhd-mount.exe");
            var workingDirectory = Path.GetDirectoryName(executablePath);
            if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
            {
                workingDirectory = AppDomain.CurrentDomain.BaseDirectory;
            }

            if (!SupportsPasswordStdin(executablePath, workingDirectory, out var compatHelpSummary))
            {
                Console.Error.WriteLine($"EVHD_MOUNT_COMPAT_ERROR: 挂载工具版本不兼容，不支持 --password-stdin。FileName={executablePath}");
                if (!string.IsNullOrWhiteSpace(compatHelpSummary))
                {
                    Console.Error.WriteLine($"EVHD_MOUNT_COMPAT_HELP: {compatHelpSummary}");
                }
                return 3;
            }

            var redirectOutput = options.StdoutMode == OutputMode.Redirect;
            var createNoWindow = redirectOutput;

            var psi = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = createNoWindow,
                RedirectStandardInput = true,
                StandardInputEncoding = new UTF8Encoding(false),
                RedirectStandardOutput = redirectOutput,
                RedirectStandardError = redirectOutput
            };

            if (redirectOutput)
            {
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;
            }

            foreach (var argument in BuildEvhdMountArguments(evhdPath, mountPoint, options.ExtraArgs))
            {
                psi.ArgumentList.Add(argument);
            }

            Console.WriteLine($"EVHD_MOUNT_ENV: User={Environment.UserDomainName}\\{Environment.UserName}, IsAdministrator={IsRunningAsAdministrator()}, OSVersion={Environment.OSVersion}");
            LogMountToolInvocation(psi);
            Console.WriteLine($"EVHD_MOUNT_START: ProbeOptions=StdoutMode={options.StdoutMode}, CreateNoWindow={psi.CreateNoWindow}, StdinEol={options.StdinEol}, StdinDelayMs={options.StdinDelayMs}, KeepStdinOpen={options.KeepStdinOpen}");
            LogDirectorySnapshot("PRE_START_MOUNTPOINT", mountPoint);
            var launchUtc = DateTime.UtcNow;

            using var process = new Process { StartInfo = psi };
            if (!process.Start())
            {
                Console.Error.WriteLine("无法启动 encrypted-vhd-mount.exe");
                return 1;
            }

            Console.WriteLine($"EVHD_MOUNT_START: ProcessId={process.Id}");

            var stdoutBuffer = new MemoryStream();
            var stderrBuffer = new MemoryStream();

            Task stdoutTask = Task.CompletedTask;
            Task stderrTask = Task.CompletedTask;

            if (redirectOutput)
            {
                stdoutTask = PumpProcessStreamBytesAsync(process.StandardOutput.BaseStream, "STDOUT", stdoutBuffer, Console.OpenStandardOutput());
                stderrTask = PumpProcessStreamBytesAsync(process.StandardError.BaseStream, "STDERR", stderrBuffer, Console.OpenStandardError());
            }
            else
            {
                Console.WriteLine("EVHD_MOUNT_START: 子进程 stdout/stderr 继承当前控制台，不经过重定向。");
            }

            PasswordWriteInfo writeInfo;
            try
            {
                writeInfo = await WritePasswordToMountToolAsync(
                    process,
                    options.Password!,
                    options.StdinEol,
                    closeStandardInput: !options.KeepStdinOpen,
                    writeDelayMs: options.StdinDelayMs);
            }
            catch
            {
                if (process.HasExited)
                {
                    process.WaitForExit();
                    await Task.WhenAll(stdoutTask, stderrTask);

                    var stdout = DecodeOutputBuffer(stdoutBuffer);
                    var stderr = DecodeOutputBuffer(stderrBuffer);
                    LogMountToolSummary("EXIT_BEFORE_STDIN", process.ExitCode, stdout, stderr);
                    LogRecentWorkingDirectoryFiles("EXIT_BEFORE_STDIN", workingDirectory, launchUtc);
                    LogRunningMountProcesses("EXIT_BEFORE_STDIN", process.Id);
                }

                throw;
            }
            Console.WriteLine(
                "EVHD_MOUNT_STDIN: Password payload written to stdin and stream closed, " +
                $"OriginalLength={writeInfo.OriginalLength}, SentLength={writeInfo.SentLength}, " +
                $"HadLeadingBom={writeInfo.HadLeadingBom}, TrimmedTrailingTerminators={writeInfo.TrimmedTrailingTerminators}, " +
                $"LineEnding={writeInfo.LineEnding}, ClosedStandardInput={writeInfo.ClosedStandardInput}, WriteDelayMs={writeInfo.WriteDelayMs}");

            var timeoutMs = options.TimeoutMs ?? 60000;
            var sw = Stopwatch.StartNew();
            var mounted = false;
            Console.WriteLine($"EVHD_MOUNT_WAIT: Waiting for mount point {mountPoint} with timeout {timeoutMs}ms");

            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (Directory.Exists(mountPoint))
                {
                    mounted = true;
                    Console.WriteLine($"EVHD_MOUNT_WAIT: Mount point became available after {sw.ElapsedMilliseconds}ms");
                    break;
                }

                if (process.HasExited)
                {
                    process.WaitForExit();
                    await Task.WhenAll(stdoutTask, stderrTask);

                    var stdout = DecodeOutputBuffer(stdoutBuffer);
                    var stderr = DecodeOutputBuffer(stderrBuffer);

                    LogDirectorySnapshot("EARLY_EXIT_MOUNTPOINT", mountPoint);
                    LogMountToolSummary("EARLY_EXIT", process.ExitCode, stdout, stderr);
                    LogRecentWorkingDirectoryFiles("EARLY_EXIT", workingDirectory, launchUtc);
                    LogRunningMountProcesses("EARLY_EXIT", process.Id);
                    Console.Error.WriteLine(BuildMountToolFailureDetail(process.ExitCode, stderr, stdout));
                    return process.ExitCode == 0 ? 1 : process.ExitCode;
                }

                await Task.Delay(500);
            }

            if (!mounted)
            {
                if (process.HasExited)
                {
                    process.WaitForExit();
                    await Task.WhenAll(stdoutTask, stderrTask);

                    var stdout = DecodeOutputBuffer(stdoutBuffer);
                    var stderr = DecodeOutputBuffer(stderrBuffer);

                    LogDirectorySnapshot("TIMEOUT_EXIT_MOUNTPOINT", mountPoint);
                    LogMountToolSummary("TIMEOUT_EXIT", process.ExitCode, stdout, stderr);
                    LogRecentWorkingDirectoryFiles("TIMEOUT_EXIT", workingDirectory, launchUtc);
                    LogRunningMountProcesses("TIMEOUT_EXIT", process.Id);
                    Console.Error.WriteLine(BuildMountToolFailureDetail(process.ExitCode, stderr, stdout));
                    return process.ExitCode == 0 ? 1 : process.ExitCode;
                }

                var runningStdout = DecodeOutputBuffer(stdoutBuffer);
                var runningStderr = DecodeOutputBuffer(stderrBuffer);

                LogDirectorySnapshot("TIMEOUT_RUNNING_MOUNTPOINT", mountPoint);
                LogMountToolSummary("TIMEOUT_STILL_RUNNING", -1, runningStdout, runningStderr);
                Console.Error.WriteLine("EVHD挂载超时（超过等待时间），未检测到挂载点。");
                return 1;
            }

            Console.WriteLine("EVHD挂载点已出现。将继续透传子进程日志，按 Ctrl+C 结束测试。\n");

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("收到 Ctrl+C，停止等待子进程退出。");
                return 0;
            }
            finally
            {
                await Task.WhenAll(stdoutTask, stderrTask);
            }

            var finalStdout = DecodeOutputBuffer(stdoutBuffer);
            var finalStderr = DecodeOutputBuffer(stderrBuffer);
            LogMountToolSummary("FINAL_EXIT", process.ExitCode, finalStdout, finalStderr);
            LogRecentWorkingDirectoryFiles("FINAL_EXIT", workingDirectory, launchUtc);
            LogRunningMountProcesses("FINAL_EXIT", process.Id);

            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"执行异常: {ex}");
            return 1;
        }
    }

    private static string NormalizePasswordForMountStdin(
        string password,
        out int originalLength,
        out bool hadLeadingBom,
        out bool trimmedTrailingTerminators)
    {
        password ??= string.Empty;
        originalLength = password.Length;
        hadLeadingBom = password.Length > 0 && password[0] == '\uFEFF';

        var normalized = hadLeadingBom ? password.Substring(1) : password;
        var trimmed = normalized.TrimEnd('\r', '\n', '\0');
        trimmedTrailingTerminators = trimmed.Length != normalized.Length;
        return trimmed;
    }

    private static async Task<PasswordWriteInfo> WritePasswordToMountToolAsync(
        Process process,
        string password,
        PasswordLineEnding lineEnding,
        bool closeStandardInput,
        int writeDelayMs)
    {
        if (writeDelayMs > 0)
        {
            await Task.Delay(writeDelayMs);
        }

        if (process.HasExited)
        {
            throw new InvalidOperationException("加密VHD挂载工具在接收密码前已退出");
        }

        var normalizedPassword = NormalizePasswordForMountStdin(
            password,
            out var originalLength,
            out var hadLeadingBom,
            out var trimmedTrailingTerminators);

        if (string.IsNullOrEmpty(normalizedPassword))
        {
            throw new InvalidOperationException("EVHD密码为空或仅包含换行/空字符，无法写入挂载工具");
        }

        await using (var writer = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false), 1024, leaveOpen: true))
        {
            writer.NewLine = lineEnding == PasswordLineEnding.CrLf ? "\r\n" : "\n";
            await writer.WriteAsync(normalizedPassword);

            if (lineEnding != PasswordLineEnding.None)
            {
                await writer.WriteLineAsync();
            }

            await writer.FlushAsync();
        }

        if (closeStandardInput)
        {
            process.StandardInput.Close();
        }

        return new PasswordWriteInfo(
            OriginalLength: originalLength,
            SentLength: normalizedPassword.Length,
            HadLeadingBom: hadLeadingBom,
            TrimmedTrailingTerminators: trimmedTrailingTerminators,
            LineEnding: lineEnding,
            ClosedStandardInput: closeStandardInput,
            WriteDelayMs: writeDelayMs);
    }

    private static string NormalizeMountPoint(string mountPoint)
    {
        if (string.IsNullOrWhiteSpace(mountPoint))
        {
            return @"N:\";
        }

        var trimmed = mountPoint.Trim();
        if (Regex.IsMatch(trimmed, "^[A-Za-z]:\\?$"))
        {
            return trimmed.EndsWith("\\", StringComparison.Ordinal) ? trimmed : trimmed + "\\";
        }

        return Path.GetFullPath(trimmed);
    }

    private static List<string> BuildEvhdMountArguments(string evhdPath, string mountPoint, IReadOnlyList<string> extraArgs)
    {
        var arguments = new List<string> { "--password-stdin" };

        if (extraArgs != null)
        {
            foreach (var arg in extraArgs)
            {
                if (!string.IsNullOrWhiteSpace(arg))
                {
                    arguments.Add(arg);
                }
            }
        }

        arguments.Add(evhdPath);
        arguments.Add(mountPoint);
        return arguments;
    }

    private static bool IsRunningAsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static void LogRecentWorkingDirectoryFiles(string phase, string workingDirectory, DateTime launchUtc)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
            {
                Console.WriteLine($"EVHD_MOUNT_{phase}_RECENT_FILES: WorkingDirectory unavailable");
                return;
            }

            var patterns = new[] { "*.log", "*.txt", "*.json", "*.dmp" };
            var files = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (var pattern in patterns)
            {
                foreach (var file in Directory.EnumerateFiles(workingDirectory, pattern, SearchOption.TopDirectoryOnly))
                {
                    if (!files.ContainsKey(file))
                    {
                        files[file] = new FileInfo(file);
                    }
                }
            }

            var recent = files.Values
                .Where(file => file.LastWriteTimeUtc >= launchUtc.AddSeconds(-3))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(20)
                .ToList();

            Console.WriteLine($"EVHD_MOUNT_{phase}_RECENT_FILES: Directory={workingDirectory}, Count={recent.Count}");
            foreach (var file in recent)
            {
                Console.WriteLine($"EVHD_MOUNT_{phase}_RECENT_FILE: Name={file.Name}, LastWriteUtc={file.LastWriteTimeUtc:O}, Length={file.Length}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"EVHD_MOUNT_{phase}_RECENT_FILES_EXCEPTION: {ex.Message}");
        }
    }

    private static void LogRunningMountProcesses(string phase, int startedProcessId)
    {
        try
        {
            var candidates = Process.GetProcesses()
                .Where(process => process.Id != startedProcessId)
                .Where(process => process.ProcessName.Contains("encrypted-vhd-mount", StringComparison.OrdinalIgnoreCase))
                .OrderBy(process => process.Id)
                .ToList();

            Console.WriteLine($"EVHD_MOUNT_{phase}_PROCESS_SCAN: Count={candidates.Count}");
            foreach (var process in candidates)
            {
                Console.WriteLine($"EVHD_MOUNT_{phase}_PROCESS: Pid={process.Id}, Name={process.ProcessName}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"EVHD_MOUNT_{phase}_PROCESS_SCAN_EXCEPTION: {ex.Message}");
        }
    }

    private static string ResolveExecutableFromPath(string executableName)
    {
        if (string.IsNullOrWhiteSpace(executableName))
        {
            return executableName;
        }

        if (Path.IsPathRooted(executableName) || executableName.Contains(Path.DirectorySeparatorChar) || executableName.Contains(Path.AltDirectorySeparatorChar))
        {
            return Path.GetFullPath(executableName);
        }

        var searchDirectories = new List<string>();
        try
        {
            var currentDirectory = Environment.CurrentDirectory;
            if (!string.IsNullOrWhiteSpace(currentDirectory))
            {
                searchDirectories.Add(currentDirectory);
            }
        }
        catch
        {
        }

        if (!string.IsNullOrWhiteSpace(AppDomain.CurrentDomain.BaseDirectory))
        {
            searchDirectories.Add(AppDomain.CurrentDomain.BaseDirectory);
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        searchDirectories.AddRange(pathValue.Split(Path.PathSeparator)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim().Trim('"')));

        foreach (var directory in searchDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var candidate = Path.Combine(directory, executableName);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch
            {
            }
        }

        return executableName;
    }

    private static void LogMountToolInvocation(ProcessStartInfo startInfo)
    {
        Console.WriteLine($"EVHD_MOUNT_START: FileName={startInfo.FileName}");
        Console.WriteLine($"EVHD_MOUNT_START: Arguments={FormatProcessArgumentsForLog(startInfo)}");
        Console.WriteLine($"EVHD_MOUNT_START: WorkingDirectory={startInfo.WorkingDirectory}");
        Console.WriteLine($"EVHD_MOUNT_START: BaseDirectory={AppDomain.CurrentDomain.BaseDirectory}");
    }

    private static string FormatProcessArgumentsForLog(ProcessStartInfo startInfo)
    {
        return string.Join(" ", startInfo.ArgumentList.Select(argument =>
        {
            if (string.IsNullOrEmpty(argument))
            {
                return "\"\"";
            }

            return argument.IndexOfAny(new[] { ' ', '\t', '"' }) >= 0
                ? $"\"{argument.Replace("\"", "\\\"")}\""
                : argument;
        }));
    }

    private static void LogMountToolSummary(string phase, int exitCode, string stdout, string stderr)
    {
        Console.WriteLine($"EVHD_MOUNT_{phase}: ExitCode={exitCode}");

        if (!string.IsNullOrWhiteSpace(stdout))
        {
            Console.WriteLine($"EVHD_MOUNT_{phase}_STDOUT:\n{stdout.TrimEnd()}");
        }

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            Console.Error.WriteLine($"EVHD_MOUNT_{phase}_STDERR:\n{stderr.TrimEnd()}");
        }
    }

    private static async Task PumpProcessStreamBytesAsync(Stream source, string streamName, MemoryStream target, Stream sink)
    {
        var buffer = new byte[8192];

        while (true)
        {
            var read = await source.ReadAsync(buffer, 0, buffer.Length);
            if (read <= 0)
            {
                break;
            }

            await target.WriteAsync(buffer, 0, read);
            await sink.WriteAsync(buffer, 0, read);
            await sink.FlushAsync();
        }

        var tail = Encoding.UTF8.GetBytes($"{Environment.NewLine}EVHD_MOUNT_{streamName}: stream closed{Environment.NewLine}");
        await sink.WriteAsync(tail, 0, tail.Length);
        await sink.FlushAsync();
    }

    private static string DecodeOutputBuffer(MemoryStream buffer)
    {
        if (buffer == null || buffer.Length == 0)
        {
            return string.Empty;
        }

        var bytes = buffer.ToArray();
        var utf8Text = Encoding.UTF8.GetString(bytes);

        var replacementCount = utf8Text.Count(c => c == '\uFFFD');
        if (replacementCount > 0 && replacementCount * 10 > utf8Text.Length)
        {
            return Encoding.Default.GetString(bytes);
        }

        return utf8Text;
    }

    private static void LogDirectorySnapshot(string phase, string path)
    {
        try
        {
            var exists = Directory.Exists(path);
            Console.WriteLine($"EVHD_MOUNT_{phase}: Directory={path} Exists={exists}");
            if (!exists)
            {
                return;
            }

            var entries = Directory.EnumerateFileSystemEntries(path, "*", SearchOption.TopDirectoryOnly)
                .Take(20)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();

            Console.WriteLine($"EVHD_MOUNT_{phase}: EntryCountSample={entries.Count}");
            foreach (var entry in entries)
            {
                Console.WriteLine($"EVHD_MOUNT_{phase}_ENTRY: {entry}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"EVHD_MOUNT_{phase}_SNAPSHOT_EXCEPTION: {ex}");
        }
    }

    private static string BuildMountToolFailureDetail(int exitCode, string stderr, string stdout)
    {
        if (!string.IsNullOrWhiteSpace(stderr))
        {
            return $"EVHD挂载进程提前退出，退出码 {exitCode}: {stderr.Trim()}";
        }

        if (!string.IsNullOrWhiteSpace(stdout))
        {
            return $"EVHD挂载进程提前退出，退出码 {exitCode}: {stdout.Trim()}";
        }

        return $"EVHD挂载进程提前退出，退出码 {exitCode}";
    }

    private static Options ParseArguments(string[] args)
    {
        var options = new Options();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("--help", StringComparison.OrdinalIgnoreCase) || arg.Equals("-h", StringComparison.OrdinalIgnoreCase))
            {
                options.ShowHelp = true;
                continue;
            }

            if (TryReadOption(args, ref i, "--evhd", out var evhd) || TryReadOption(args, ref i, "-e", out evhd))
            {
                options.EvhdPath = evhd;
                continue;
            }

            if (TryReadOption(args, ref i, "--mount-point", out var mountPoint) || TryReadOption(args, ref i, "-m", out mountPoint))
            {
                options.MountPoint = mountPoint;
                continue;
            }

            if (TryReadOption(args, ref i, "--password", out var password) || TryReadOption(args, ref i, "-p", out password))
            {
                options.Password = password;
                continue;
            }

            if (TryReadOption(args, ref i, "--tool-path", out var toolPath) || TryReadOption(args, ref i, "-t", out toolPath))
            {
                options.ToolPath = toolPath;
                continue;
            }

            if (TryReadOption(args, ref i, "--timeout-ms", out var timeoutMsRaw))
            {
                if (!int.TryParse(timeoutMsRaw, out var timeoutMs) || timeoutMs <= 0)
                {
                    throw new ArgumentException($"无效的 --timeout-ms: {timeoutMsRaw}");
                }

                options.TimeoutMs = timeoutMs;
                continue;
            }

            if (TryReadOption(args, ref i, "--stdin-delay-ms", out var stdinDelayRaw))
            {
                if (!int.TryParse(stdinDelayRaw, out var stdinDelayMs) || stdinDelayMs < 0)
                {
                    throw new ArgumentException($"无效的 --stdin-delay-ms: {stdinDelayRaw}");
                }

                options.StdinDelayMs = stdinDelayMs;
                continue;
            }

            if (TryReadOption(args, ref i, "--stdin-eol", out var stdinEolRaw))
            {
                options.StdinEol = ParseLineEnding(stdinEolRaw);
                continue;
            }

            if (TryReadOption(args, ref i, "--stdio", out var stdioRaw))
            {
                options.StdoutMode = ParseOutputMode(stdioRaw);
                continue;
            }

            if (TryReadOption(args, ref i, "--extra-arg", out var extraArg) || TryReadOption(args, ref i, "-x", out extraArg))
            {
                options.ExtraArgs.Add(extraArg);
                continue;
            }

            if (arg.Equals("--keep-stdin-open", StringComparison.OrdinalIgnoreCase))
            {
                options.KeepStdinOpen = true;
                continue;
            }

            throw new ArgumentException($"未知参数: {arg}");
        }

        return options;
    }

    private static bool TryReadOption(string[] args, ref int index, string name, out string value)
    {
        value = string.Empty;
        var arg = args[index];

        if (!arg.StartsWith(name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (arg.Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            if (index + 1 >= args.Length)
            {
                throw new ArgumentException($"参数 {name} 缺少值");
            }

            value = args[++index];
            return true;
        }

        if (arg.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
        {
            value = arg.Substring(name.Length + 1);
            return true;
        }

        return false;
    }

    private static OutputMode ParseOutputMode(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "redirect" => OutputMode.Redirect,
            "inherit" => OutputMode.Inherit,
            _ => throw new ArgumentException($"无效的 --stdio: {value}，可选值: redirect, inherit")
        };
    }

    private static PasswordLineEnding ParseLineEnding(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "lf" => PasswordLineEnding.Lf,
            "crlf" => PasswordLineEnding.CrLf,
            "none" => PasswordLineEnding.None,
            _ => throw new ArgumentException($"无效的 --stdin-eol: {value}，可选值: lf, crlf, none")
        };
    }

    private static bool SupportsPasswordStdin(string executablePath, string workingDirectory, out string helpSummary)
    {
        helpSummary = string.Empty;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.UTF8,
                RedirectStandardError = true,
                StandardErrorEncoding = Encoding.UTF8
            };
            psi.ArgumentList.Add("--help");

            using var process = new Process { StartInfo = psi };
            if (!process.Start())
            {
                helpSummary = "failed to start --help process";
                return false;
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(5000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                helpSummary = "--help timed out";
                return false;
            }

            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();
            var merged = (stdout ?? string.Empty) + Environment.NewLine + (stderr ?? string.Empty);
            helpSummary = CompactForLog(merged, 600);

            return merged.IndexOf("--password-stdin", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch (Exception ex)
        {
            helpSummary = ex.Message;
            return false;
        }
    }

    private static string CompactForLog(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var compact = Regex.Replace(text, "\\s+", " ").Trim();
        if (compact.Length <= maxLength)
        {
            return compact;
        }

        return compact.Substring(0, maxLength) + "...";
    }

    private static void PrintUsage()
    {
        Console.WriteLine("EVHD 挂载探针（完全透传子进程日志）");
        Console.WriteLine();
        Console.WriteLine("用法:");
        Console.WriteLine("  dotnet run --project EVHDMountProbe -- --evhd <path> --mount-point <path> --password <pwd> [--tool-path <exe>] [--timeout-ms <ms>] [--stdio <mode>] [--stdin-eol <mode>] [--stdin-delay-ms <ms>] [--keep-stdin-open]");
        Console.WriteLine();
        Console.WriteLine("参数:");
        Console.WriteLine("  --evhd, -e         EVHD 文件路径（必填）");
        Console.WriteLine("  --mount-point, -m  挂载点，如 N:\\ 或 C:\\mount\\evhd（必填）");
        Console.WriteLine("  --password, -p     密码明文（必填，仅用于测试）");
        Console.WriteLine("  --tool-path, -t    encrypted-vhd-mount.exe 路径（默认从 PATH/当前目录解析）");
        Console.WriteLine("  --timeout-ms       等待挂载点出现超时（默认 60000）");
        Console.WriteLine("  --stdio            子进程输出模式: inherit(默认) / redirect");
        Console.WriteLine("  --stdin-eol        密码后换行: lf(默认) / crlf / none");
        Console.WriteLine("  --stdin-delay-ms   启动后延迟写入密码毫秒数（默认 0）");
        Console.WriteLine("  --keep-stdin-open  写入密码后不立即关闭 stdin");
        Console.WriteLine("  --extra-arg, -x    追加透传到工具的参数（可重复），例如 -x /d");
    }

    private sealed class Options
    {
        public bool ShowHelp { get; set; }

        public string? EvhdPath { get; set; }

        public string? MountPoint { get; set; }

        public string? Password { get; set; }

        public string? ToolPath { get; set; }

        public int? TimeoutMs { get; set; }

        public OutputMode StdoutMode { get; set; } = OutputMode.Inherit;

        public PasswordLineEnding StdinEol { get; set; } = PasswordLineEnding.Lf;

        public int StdinDelayMs { get; set; }

        public bool KeepStdinOpen { get; set; }

        public List<string> ExtraArgs { get; } = new();

        public bool IsValid(out string error)
        {
            if (string.IsNullOrWhiteSpace(EvhdPath))
            {
                error = "缺少 --evhd";
                return false;
            }

            if (string.IsNullOrWhiteSpace(MountPoint))
            {
                error = "缺少 --mount-point";
                return false;
            }

            if (string.IsNullOrEmpty(Password))
            {
                error = "缺少 --password";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }
}
