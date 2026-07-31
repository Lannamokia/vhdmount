using System;
using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace VHDMounter.RustDeskBridge.Pipe
{
    /// <summary>
    /// 拿当前 NamedPipeServerStream 客户端的诊断身份信息：PID、token SID、image basename、Win32
    /// session id。docs/vhd-rustdesk-bridge-controlled-side-handoff.md §10 事件 1 / 2。
    ///
    /// <para>
    /// **历史教训**：第一版用 <c>pipe.RunAsClient(...) + WindowsIdentity.GetCurrent</c> 拿 SID，
    /// 这条路径要求客户端**已经发过至少一个写**（MSDN: "the client must perform a write
    /// operation before a server can impersonate the client"）。我们 §10.10 排障要的恰恰
    /// 是"对端**没发任何字节就关 pipe**"这一类场景，impersonate 必然失败，SID 永远是
    /// unknown，等于这条诊断完全废掉。
    /// </para>
    ///
    /// <para>
    /// 现在直接走 PID → <c>OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)</c> →
    /// <c>OpenProcessToken</c> → <c>GetTokenInformation(TokenUser)</c>，不依赖客户端
    /// 写过任何字节、也不依赖管道还连着；典型场景下 <c>OpenProcess</c> 在客户端退出后
    /// 几秒内仍能成功（句柄宽限期）。同时拿 <c>QueryFullProcessImageNameW</c>
    /// 给到 image basename + <c>ProcessIdToSessionId</c> 给到 Win32 session id。
    /// </para>
    ///
    /// <para>失败一律返回 null —— best-effort 诊断信息，绝不抛出。</para>
    /// </summary>
    internal static class PipeClientIdentity
    {
        public sealed class ClientIdentityInfo
        {
            public uint? Pid { get; init; }
            public string SidString { get; init; }
            public string ImageBaseName { get; init; }
            public uint? SessionId { get; init; }
            public string LastError { get; init; }

            public bool IsLocalSystem =>
                string.Equals(SidString, "S-1-5-18", StringComparison.Ordinal);
        }

        public static ClientIdentityInfo TryQuery(NamedPipeServerStream pipe)
        {
            if (pipe == null || !pipe.IsConnected)
            {
                return new ClientIdentityInfo { LastError = "pipe_not_connected" };
            }

            uint? pid = null;
            string lastError = null;

            // 1) PID
            try
            {
                if (GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var rawPid))
                {
                    pid = rawPid;
                }
                else
                {
                    lastError = $"GetNamedPipeClientProcessId win32={Marshal.GetLastWin32Error()}";
                }
            }
            catch (Exception ex)
            {
                lastError = "GetNamedPipeClientProcessId_throw=" + ex.Message;
            }

            if (!pid.HasValue)
            {
                return new ClientIdentityInfo { LastError = lastError };
            }

            string sid = null;
            string image = null;
            uint? sessionId = null;

            // 2) sessionId（不依赖 OpenProcess）
            try
            {
                if (ProcessIdToSessionId(pid.Value, out var rawSession))
                {
                    sessionId = rawSession;
                }
            }
            catch
            {
                // best-effort
            }

            // 3) OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION) —— 不需要 admin，
            //    PROCESS_QUERY_INFORMATION 才需要更高权限。LIMITED 在 Vista+ 总能用。
            const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
            var hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid.Value);
            if (hProcess.IsInvalid)
            {
                lastError = $"OpenProcess win32={Marshal.GetLastWin32Error()}";
                return new ClientIdentityInfo
                {
                    Pid = pid,
                    SessionId = sessionId,
                    LastError = lastError,
                };
            }

            try
            {
                // 4) image basename
                try
                {
                    var sb = new StringBuilder(1024);
                    uint size = (uint)sb.Capacity;
                    if (QueryFullProcessImageNameW(hProcess, 0, sb, ref size))
                    {
                        var full = sb.ToString();
                        image = System.IO.Path.GetFileName(full);
                    }
                    else
                    {
                        lastError = $"QueryFullProcessImageNameW win32={Marshal.GetLastWin32Error()}";
                    }
                }
                catch (Exception ex)
                {
                    lastError = "QueryFullProcessImageNameW_throw=" + ex.Message;
                }

                // 5) OpenProcessToken + GetTokenInformation(TokenUser) → SID
                if (OpenProcessToken(hProcess, TOKEN_QUERY, out var hToken))
                {
                    try
                    {
                        sid = ResolveTokenUserSidOrNull(hToken, ref lastError);
                    }
                    finally
                    {
                        try { hToken.Dispose(); } catch { /* ignore */ }
                    }
                }
                else
                {
                    lastError = $"OpenProcessToken win32={Marshal.GetLastWin32Error()}";
                }
            }
            finally
            {
                hProcess.Dispose();
            }

            return new ClientIdentityInfo
            {
                Pid = pid,
                SidString = sid,
                ImageBaseName = image,
                SessionId = sessionId,
                LastError = lastError,
            };
        }

        // ---------- TokenUser → SID ----------

        private const uint TOKEN_QUERY = 0x0008;
        private const int TokenUserInfoClass = 1;

        private static string ResolveTokenUserSidOrNull(SafeAccessTokenHandle hToken, ref string lastError)
        {
            // 第一次调用：拿所需 buffer 长度
            uint dwSize = 0;
            GetTokenInformation(hToken, TokenUserInfoClass, IntPtr.Zero, 0, out dwSize);
            var win32 = Marshal.GetLastWin32Error();
            if (dwSize == 0 || (win32 != 0 /* OK */ && win32 != 122 /* ERROR_INSUFFICIENT_BUFFER */))
            {
                lastError = $"GetTokenInformation_size win32={win32}";
                return null;
            }

            var buffer = Marshal.AllocHGlobal((int)dwSize);
            try
            {
                if (!GetTokenInformation(hToken, TokenUserInfoClass, buffer, dwSize, out _))
                {
                    lastError = $"GetTokenInformation_data win32={Marshal.GetLastWin32Error()}";
                    return null;
                }
                // TOKEN_USER 第一个字段是 SID_AND_ATTRIBUTES，它的第一个字段又是 PSID
                var sidPtr = Marshal.ReadIntPtr(buffer);
                if (sidPtr == IntPtr.Zero)
                {
                    lastError = "TOKEN_USER.Sid=NULL";
                    return null;
                }
                try
                {
                    var sid = new SecurityIdentifier(sidPtr);
                    return sid.Value;
                }
                catch (Exception ex)
                {
                    lastError = "SecurityIdentifier_throw=" + ex.Message;
                    return null;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        // ---------- P/Invoke ----------

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetNamedPipeClientProcessId(IntPtr hNamedPipe, out uint clientPid);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ProcessIdToSessionId(uint dwProcessId, out uint pSessionId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern SafeProcessHandle OpenProcess(
            uint dwDesiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
            uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryFullProcessImageNameW(
            SafeProcessHandle hProcess,
            uint dwFlags,
            StringBuilder lpExeName,
            ref uint lpdwSize);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenProcessToken(
            SafeProcessHandle ProcessHandle,
            uint DesiredAccess,
            out SafeAccessTokenHandle TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetTokenInformation(
            SafeAccessTokenHandle TokenHandle,
            int TokenInformationClass,
            IntPtr TokenInformation,
            uint TokenInformationLength,
            out uint ReturnLength);
    }
}
