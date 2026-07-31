using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

// 任务 19.1：跨进程 RustDesk 模拟器（独立控制台 exe）。
//
// 工程权衡：本程序**不**引用 VHDMounter.csproj —— VHDMounter 是 self-contained WPF
// 进程，依赖 HidSharp / NAudio / LibreHardwareMonitor 等仅在 EnableHidMenuFeatures=true
// 下可用的 NuGet 包，把它作为 ProjectReference 会把 MockController 拖进同样的依赖图。
// MockController 只需要 (a) 帧外壳编解码、(b) HMAC-SHA256 输入字节构造、(c) Frame
// 协议字面量；这三样我们在本文件内字节级再现一份，与协议文档 §3 / §5.2 / §6.2 / §7.2 /
// §8.2 + 测试夹具 protocol-vectors.json 字节级一致。
//
// 用法：
//   VHDMounter.Tests.RustDeskBridge.MockController.exe \
//       --pipe <pipeName> \
//       --secret-hex <64hex> \
//       --secret-version <u32> \
//       --machine-id <id> \
//       --action <handshake|report|log|peerapproval|sequence|handshake-fail-expected> \
//       [--nonce <32hex>] \
//       [--timeout-ms <int>]
//
// 退出码：
//   0  全部步骤成功
//   1  协议错误（HandshakeResponse.ok=false / 服务器返回非预期帧 / JSON 不合法）
//   2  连接 / I/O 超时
//   3  参数错误
//
// 标准输出：每完成一帧打印一行 JSON：{"step":"handshake","ok":true,...}

internal static class Program
{
    private const int ExitOk = 0;
    private const int ExitProtocol = 1;
    private const int ExitTimeout = 2;
    private const int ExitArgs = 3;

    private const int DefaultTimeoutMs = 5000;
    private const int MaxFrameBytes = 65536;

    // 协议字面量（与 src/VHDMounter/RustDeskBridge/Frames/*.cs 严格一致）
    private const string HandshakeProtocol = "VHDRustDeskBridgeHandshakeV1";
    private const string ReportProtocol = "VHDRustDeskBridgeReportV1";
    private const string LogProtocol = "VHDRustDeskBridgeLogV1";
    private const string PeerApprovalProtocol = "VHDRustDeskBridgePeerApprovalV1";

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var opts = ParseArgs(args);
            if (opts == null)
            {
                return ExitArgs;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(opts.TimeoutMs));
            var pipe = new NamedPipeClientStream(
                serverName: ".",
                pipeName: opts.PipeName,
                direction: PipeDirection.InOut,
                options: PipeOptions.Asynchronous);

            try
            {
                await pipe.ConnectAsync(opts.TimeoutMs, cts.Token).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                EmitJson(new { step = "connect", ok = false, error = "timeout" });
                return ExitTimeout;
            }
            catch (UnauthorizedAccessException ex)
            {
                EmitJson(new { step = "connect", ok = false, error = "access_denied", detail = ex.Message });
                return ExitProtocol;
            }

            // 握手永远是第一步
            var handshakeOk = await DoHandshakeAsync(pipe, opts, cts.Token).ConfigureAwait(false);
            if (!handshakeOk)
            {
                // 调用方期望握手失败时（"handshake-fail-expected"），失败也算成功
                return opts.Action == "handshake-fail-expected" ? ExitOk : ExitProtocol;
            }

            switch (opts.Action)
            {
                case "handshake":
                case "handshake-fail-expected":
                    return ExitOk;

                case "report":
                    return (await DoReportAsync(pipe, opts, cts.Token).ConfigureAwait(false)) ? ExitOk : ExitProtocol;

                case "log":
                    await DoLogAsync(pipe, opts, cts.Token).ConfigureAwait(false);
                    return ExitOk;

                case "peerapproval":
                    return (await DoPeerApprovalAsync(pipe, opts, cts.Token).ConfigureAwait(false))
                        ? ExitOk : ExitProtocol;

                case "sequence":
                    if (!await DoReportAsync(pipe, opts, cts.Token).ConfigureAwait(false)) return ExitProtocol;
                    if (!await DoPeerApprovalAsync(pipe, opts, cts.Token).ConfigureAwait(false)) return ExitProtocol;
                    return ExitOk;

                default:
                    EmitJson(new { step = "args", ok = false, error = "unknown_action", action = opts.Action });
                    return ExitArgs;
            }
        }
        catch (OperationCanceledException)
        {
            EmitJson(new { step = "global", ok = false, error = "timeout" });
            return ExitTimeout;
        }
        catch (Exception ex)
        {
            EmitJson(new { step = "global", ok = false, error = "exception", detail = ex.Message });
            return ExitProtocol;
        }
    }

    // ── 握手 ──────────────────────────────────────────────────────────────

    private static async Task<bool> DoHandshakeAsync(NamedPipeClientStream pipe, MockControllerOptions opts, CancellationToken ct)
    {
        var nonce = opts.Nonce ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var hmacInput = Encoding.UTF8.GetBytes(string.Concat(
            HandshakeProtocol, "\n",
            opts.SecretVersion.ToString(CultureInfo.InvariantCulture), "\n",
            nonce, "\n",
            timestampMs.ToString(CultureInfo.InvariantCulture)));
        var proof = ComputeMacBase64(opts.SecretBytes, hmacInput);

        var frame = new
        {
            protocol = HandshakeProtocol,
            secretVersion = opts.SecretVersion,
            nonce,
            timestampMs,
            clientKind = "rustdesk",
            clientVersion = "1.4.6-mock",
            proof,
        };
        await WriteFrameAsync(pipe, frame, ct).ConfigureAwait(false);
        var response = await ReadFrameAsync(pipe, ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(response);
        var ok = doc.RootElement.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
        var reason = doc.RootElement.TryGetProperty("reason", out var rEl) ? rEl.GetString() : null;
        EmitJson(new { step = "handshake", ok, reason, nonce });
        return ok;
    }

    // ── Report ────────────────────────────────────────────────────────────

    private static async Task<bool> DoReportAsync(NamedPipeClientStream pipe, MockControllerOptions opts, CancellationToken ct)
    {
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var reportedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        const string rustDeskId = "123456789";
        const string passwordKind = "temporary";
        const string password = "MockPwd!1";
        const string reason = "startup";

        var passwordSha256Hex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password)))
            .ToLowerInvariant();
        var hmacInput = Encoding.UTF8.GetBytes(string.Concat(
            ReportProtocol, "\n",
            opts.SecretVersion.ToString(CultureInfo.InvariantCulture), "\n",
            rustDeskId, "\n",
            passwordKind, "\n",
            passwordSha256Hex, "\n",
            reason, "\n",
            reportedAt.ToString(CultureInfo.InvariantCulture), "\n",
            nonce));
        var mac = ComputeMacBase64(opts.SecretBytes, hmacInput);

        var frame = new
        {
            protocol = ReportProtocol,
            secretVersion = opts.SecretVersion,
            rustDeskId,
            passwordKind,
            password,
            reason,
            reportedAt,
            nonce,
            mac,
        };
        await WriteFrameAsync(pipe, frame, ct).ConfigureAwait(false);
        var response = await ReadFrameAsync(pipe, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(response);
        var result = doc.RootElement.TryGetProperty("result", out var rEl) ? rEl.GetString() : null;
        var rejectReason = doc.RootElement.TryGetProperty("reason", out var reasonEl) ? reasonEl.GetString() : null;
        EmitJson(new { step = "report", ok = result == "accepted", result, reason = rejectReason });
        return result == "accepted";
    }

    // ── Log（fire-and-forget）─────────────────────────────────────────────

    private static async Task DoLogAsync(NamedPipeClientStream pipe, MockControllerOptions opts, CancellationToken ct)
    {
        var timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        const string level = "info";
        const string target = "rustdesk::server::connection";
        const string message = "mock controller log line";
        var msgSha256Hex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(message)))
            .ToLowerInvariant();
        var hmacInput = Encoding.UTF8.GetBytes(string.Concat(
            LogProtocol, "\n",
            opts.SecretVersion.ToString(CultureInfo.InvariantCulture), "\n",
            level, "\n",
            target, "\n",
            msgSha256Hex, "\n",
            timestampMs.ToString(CultureInfo.InvariantCulture)));
        var mac = ComputeMacBase64(opts.SecretBytes, hmacInput);

        var frame = new
        {
            protocol = LogProtocol,
            secretVersion = opts.SecretVersion,
            level,
            target,
            message,
            timestampMs,
            mac,
        };
        await WriteFrameAsync(pipe, frame, ct).ConfigureAwait(false);
        EmitJson(new { step = "log", ok = true });
    }

    // ── PeerApproval ──────────────────────────────────────────────────────

    private static async Task<bool> DoPeerApprovalAsync(NamedPipeClientStream pipe, MockControllerOptions opts, CancellationToken ct)
    {
        var requestNonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        const string controllerId = "987654321";
        const string controllerName = "admin@mock";
        const string controllerPlatform = "Windows";
        const string controllerHwid = "aabbccddeeff00112233445566778899";
        const string peerSocketAddr = "192.0.2.1:51820";
        const string connectionType = "controlled";

        var nameSha256Hex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(controllerName)))
            .ToLowerInvariant();
        var hwidSha256Hex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(controllerHwid)))
            .ToLowerInvariant();
        var hmacInput = Encoding.UTF8.GetBytes(string.Concat(
            PeerApprovalProtocol, "\n",
            opts.SecretVersion.ToString(CultureInfo.InvariantCulture), "\n",
            opts.MachineId, "\n",
            controllerId, "\n",
            nameSha256Hex, "\n",
            controllerPlatform, "\n",
            hwidSha256Hex, "\n",
            peerSocketAddr, "\n",
            connectionType, "\n",
            requestNonce, "\n",
            timestampMs.ToString(CultureInfo.InvariantCulture)));
        var mac = ComputeMacBase64(opts.SecretBytes, hmacInput);

        var frame = new
        {
            protocol = PeerApprovalProtocol,
            secretVersion = opts.SecretVersion,
            controlledMachineId = opts.MachineId,
            controllerId,
            controllerName,
            controllerPlatform,
            controllerHwid,
            peerSocketAddr,
            connectionType,
            requestNonce,
            timestampMs,
            mac,
        };
        await WriteFrameAsync(pipe, frame, ct).ConfigureAwait(false);
        var response = await ReadFrameAsync(pipe, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(response);
        var result = doc.RootElement.TryGetProperty("result", out var rEl) ? rEl.GetString() : null;
        EmitJson(new { step = "peerapproval", ok = result == "approved" || result == "rejected", result });
        return result != null;
    }

    // ── Frame I/O / HMAC ──────────────────────────────────────────────────

    private static string ComputeMacBase64(byte[] key, byte[] input)
    {
        Span<byte> mac = stackalloc byte[32];
        HMACSHA256.HashData(key, input, mac);
        return Convert.ToBase64String(mac);
    }

    private static async Task WriteFrameAsync(Stream pipe, object payload, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        if (bytes.Length > MaxFrameBytes)
        {
            throw new InvalidOperationException($"frame too large {bytes.Length}");
        }
        var len = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(len, (uint)bytes.Length);
        await pipe.WriteAsync(len, ct).ConfigureAwait(false);
        await pipe.WriteAsync(bytes, ct).ConfigureAwait(false);
        await pipe.FlushAsync(ct).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadFrameAsync(Stream pipe, CancellationToken ct)
    {
        var len = new byte[4];
        await ReadExactAsync(pipe, len, ct).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(len);
        if (length > MaxFrameBytes)
        {
            throw new InvalidDataException($"frame length too large {length}");
        }
        if (length == 0) return Array.Empty<byte>();
        var payload = new byte[length];
        await ReadExactAsync(pipe, payload, ct).ConfigureAwait(false);
        return payload;
    }

    private static async Task ReadExactAsync(Stream pipe, byte[] buffer, CancellationToken ct)
    {
        var off = 0;
        while (off < buffer.Length)
        {
            var n = await pipe.ReadAsync(buffer.AsMemory(off, buffer.Length - off), ct).ConfigureAwait(false);
            if (n == 0) throw new IOException("server closed pipe mid-frame");
            off += n;
        }
    }

    // ── 参数解析 ──────────────────────────────────────────────────────────

    private static MockControllerOptions? ParseArgs(string[] args)
    {
        string? pipeName = null;
        string? secretHex = null;
        string? machineId = null;
        string? action = null;
        string? nonce = null;
        uint secretVersion = 1;
        var timeoutMs = DefaultTimeoutMs;

        for (var i = 0; i + 1 < args.Length; i += 2)
        {
            switch (args[i])
            {
                case "--pipe": pipeName = args[i + 1]; break;
                case "--secret-hex": secretHex = args[i + 1]; break;
                case "--secret-version": secretVersion = uint.Parse(args[i + 1], CultureInfo.InvariantCulture); break;
                case "--machine-id": machineId = args[i + 1]; break;
                case "--action": action = args[i + 1]; break;
                case "--nonce": nonce = args[i + 1]; break;
                case "--timeout-ms": timeoutMs = int.Parse(args[i + 1], CultureInfo.InvariantCulture); break;
            }
        }

        if (string.IsNullOrWhiteSpace(pipeName) ||
            string.IsNullOrWhiteSpace(secretHex) ||
            string.IsNullOrWhiteSpace(machineId) ||
            string.IsNullOrWhiteSpace(action))
        {
            EmitJson(new { step = "args", ok = false, error = "missing_required_args" });
            return null;
        }

        if (secretHex.Length != 64)
        {
            EmitJson(new { step = "args", ok = false, error = "secret_hex_len_must_be_64" });
            return null;
        }

        return new MockControllerOptions
        {
            PipeName = pipeName,
            SecretBytes = Convert.FromHexString(secretHex),
            SecretVersion = secretVersion,
            MachineId = machineId,
            Action = action,
            Nonce = nonce,
            TimeoutMs = timeoutMs,
        };
    }

    private static void EmitJson(object payload)
    {
        Console.WriteLine(JsonSerializer.Serialize(payload));
    }

    private sealed class MockControllerOptions
    {
        public string PipeName { get; set; } = string.Empty;
        public byte[] SecretBytes { get; set; } = Array.Empty<byte>();
        public uint SecretVersion { get; set; } = 1;
        public string MachineId { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string? Nonce { get; set; }
        public int TimeoutMs { get; set; } = DefaultTimeoutMs;
    }
}
