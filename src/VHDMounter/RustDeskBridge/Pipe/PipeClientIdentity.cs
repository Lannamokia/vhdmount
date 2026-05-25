using System;
using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace VHDMounter.RustDeskBridge.Pipe
{
    /// <summary>
    /// 拿当前 NamedPipeServerStream 客户端的 PID + token SID 字符串，用于诊断埋点
    /// （docs/vhd-rustdesk-bridge-controlled-side-handoff.md §10 事件 1 / 2）。
    ///
    /// <para>失败时返回 null —— 这是 best-effort 诊断信息，绝不抛出。</para>
    /// </summary>
    internal static class PipeClientIdentity
    {
        /// <summary>
        /// 拿到 (clientPid, tokenSidString)。任意一项失败该项就是 null。
        /// </summary>
        public static (uint? Pid, string SidString) TryQuery(NamedPipeServerStream pipe)
        {
            if (pipe == null || !pipe.IsConnected)
            {
                return (null, null);
            }

            uint? pid = null;
            string sid = null;

            // PID
            try
            {
                if (GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var rawPid))
                {
                    pid = rawPid;
                }
            }
            catch (Exception)
            {
                pid = null;
            }

            // SID（impersonate 后再读 WindowsIdentity，读完立刻 RevertToSelf）
            try
            {
                pipe.RunAsClient(() =>
                {
                    try
                    {
                        using var id = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
                        sid = id?.User?.Value;
                    }
                    catch (Exception)
                    {
                        sid = null;
                    }
                });
            }
            catch (Exception)
            {
                sid = null;
            }

            return (pid, sid);
        }

        // ---------- P/Invoke ----------

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetNamedPipeClientProcessId(IntPtr hNamedPipe, out uint clientPid);
    }
}
