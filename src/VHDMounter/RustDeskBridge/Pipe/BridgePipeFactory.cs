using System;
using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace VHDMounter.RustDeskBridge.Pipe
{
    /// <summary>
    /// Requirement 1.1 / 1.2 / 14.6 / 14.7：构造严格 DACL 的命名管道实例。
    ///
    /// 仅授予以下两个主体 <c>FILE_GENERIC_READ | FILE_GENERIC_WRITE | FILE_CREATE_PIPE_INSTANCE</c>：
    /// <list type="bullet">
    /// <item>BUILTIN\Administrators (S-1-5-32-544)</item>
    /// <item>NT AUTHORITY\SYSTEM (S-1-5-18)</item>
    /// </list>
    /// 对 Users / Everyone / Authenticated Users / ANONYMOUS LOGON / NETWORK <b>不</b>授予任何 ACE。
    ///
    /// dwOpenMode 含 <c>PIPE_REJECT_REMOTE_CLIENTS (0x00000008)</c>；管道路径恒为
    /// <c>\\.\pipe\VHDMount.RustDeskBridge</c>。
    ///
    /// .NET 8 的 <see cref="PipeOptions"/> 枚举不含 <c>RejectRemoteClients</c>（.NET 9 才加），
    /// 因此本工厂 P/Invoke 直接调 <c>CreateNamedPipeW</c>，把
    /// <c>SECURITY_ATTRIBUTES.lpSecurityDescriptor</c> 指向我们手工构造的 DACL，
    /// dwOpenMode 显式 OR 上 <c>PIPE_REJECT_REMOTE_CLIENTS</c>。
    ///
    /// 拿到原生 HANDLE 之后再用 <see cref="SafePipeHandle"/> 包装，最后构造
    /// <see cref="NamedPipeServerStream"/>（<c>NamedPipeServerStream(SafePipeHandle, bool, bool)</c>），
    /// 这样上层代码完全用托管 Stream API 即可。
    ///
    /// DACL 构造失败 / <c>INVALID_HANDLE_VALUE</c> 一律抛
    /// <see cref="BridgePipeCreateException"/>，由调用方按 §1.7 退避（绝不降级到默认 DACL）。
    /// </summary>
    internal static class BridgePipeFactory
    {
        public const string DefaultPipeName = "VHDMount.RustDeskBridge";
        public const string FullPipePath = @"\\.\pipe\VHDMount.RustDeskBridge";

        // dwOpenMode 标志（CreateNamedPipeW 第二参）
        private const uint PIPE_ACCESS_DUPLEX = 0x00000003;
        private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        private const uint FILE_FLAG_FIRST_PIPE_INSTANCE = 0x00080000;
        private const uint PIPE_REJECT_REMOTE_CLIENTS = 0x00000008;

        // dwPipeMode 标志（CreateNamedPipeW 第三参）
        private const uint PIPE_TYPE_BYTE = 0x00000000;
        private const uint PIPE_READMODE_BYTE = 0x00000000;
        private const uint PIPE_WAIT = 0x00000000;

        // 缓冲 / 实例上限
        private const uint NMPWAIT_DEFAULT_TIMEOUT_MS = 0;
        private const uint MaxInstances = 1; // §1.4 同时刻最多 1 个 Bridge_Session

        private static readonly IntPtr InvalidHandleValue = new IntPtr(-1);

        public static NamedPipeServerStream CreatePipeInstance()
        {
            return CreatePipeInstance(DefaultPipeName);
        }

        public static NamedPipeServerStream CreatePipeInstance(string pipeName)
        {
            if (string.IsNullOrWhiteSpace(pipeName))
            {
                throw new ArgumentException("pipeName 不能为空", nameof(pipeName));
            }

            byte[] securityDescriptorBytes;
            try
            {
                securityDescriptorBytes = BuildSecurityDescriptorBinary();
            }
            catch (Exception ex)
            {
                throw new BridgePipeCreateException(
                    "构造 RustDesk 桥命名管道 DACL 失败：" + ex.Message, ex);
            }

            var sdHandle = GCHandle.Alloc(securityDescriptorBytes, GCHandleType.Pinned);
            try
            {
                var sa = new SECURITY_ATTRIBUTES
                {
                    nLength = (uint)Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
                    lpSecurityDescriptor = sdHandle.AddrOfPinnedObject(),
                    bInheritHandle = false,
                };

                var fullPath = $@"\\.\pipe\{pipeName}";
                var rawHandle = CreateNamedPipeW(
                    lpName: fullPath,
                    dwOpenMode: PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED | PIPE_REJECT_REMOTE_CLIENTS,
                    dwPipeMode: PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
                    nMaxInstances: MaxInstances,
                    nOutBufferSize: (uint)FrameCodec.MaxFrameBytes,
                    nInBufferSize: (uint)FrameCodec.MaxFrameBytes,
                    nDefaultTimeOut: NMPWAIT_DEFAULT_TIMEOUT_MS,
                    lpSecurityAttributes: ref sa);

                if (rawHandle == InvalidHandleValue)
                {
                    var win32 = Marshal.GetLastWin32Error();
                    throw new BridgePipeCreateException(
                        $"CreateNamedPipeW 失败 path={fullPath} win32Err={win32}",
                        new Win32Exception(win32));
                }

                var safeHandle = new SafePipeHandle(rawHandle, ownsHandle: true);
                try
                {
                    return new NamedPipeServerStream(
                        PipeDirection.InOut,
                        isAsync: true,
                        isConnected: false,
                        safePipeHandle: safeHandle);
                }
                catch
                {
                    safeHandle.Dispose();
                    throw;
                }
            }
            finally
            {
                sdHandle.Free();
            }
        }

        /// <summary>
        /// 构造仅含 SYSTEM + Administrators 两条 ACE 的自相对二进制安全描述符。
        /// 用 <see cref="RawSecurityDescriptor"/> + <see cref="RawAcl"/> 手工拼接，
        /// 写到独立 byte[]（与 P/Invoke 的 lpSecurityDescriptor 配合）。
        /// </summary>
        public static byte[] BuildSecurityDescriptorBinary()
        {
            // 我们授予的访问权 —— FILE_GENERIC_READ | FILE_GENERIC_WRITE | FILE_CREATE_PIPE_INSTANCE
            // FILE_GENERIC_READ  = 0x00120089
            // FILE_GENERIC_WRITE = 0x00120116
            // FILE_CREATE_PIPE_INSTANCE = 0x00000004
            // 0x00120089 | 0x00120116 | 0x00000004 = 0x0012019F
            const int RequiredAccess = 0x0012019F;

            var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var adminsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);

            var dacl = new RawAcl(revision: 2 /* ACL_REVISION */, capacity: 2);
            dacl.InsertAce(0, new CommonAce(
                AceFlags.None,
                AceQualifier.AccessAllowed,
                RequiredAccess,
                systemSid,
                isCallback: false,
                opaque: null));
            dacl.InsertAce(1, new CommonAce(
                AceFlags.None,
                AceQualifier.AccessAllowed,
                RequiredAccess,
                adminsSid,
                isCallback: false,
                opaque: null));

            var sd = new RawSecurityDescriptor(
                ControlFlags.SelfRelative | ControlFlags.DiscretionaryAclPresent,
                owner: adminsSid,
                group: adminsSid,
                systemAcl: null,
                discretionaryAcl: dacl);

            var binary = new byte[sd.BinaryLength];
            sd.GetBinaryForm(binary, 0);
            return binary;
        }

        // ---------- P/Invoke ----------

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateNamedPipeW")]
        private static extern IntPtr CreateNamedPipeW(
            [MarshalAs(UnmanagedType.LPWStr)] string lpName,
            uint dwOpenMode,
            uint dwPipeMode,
            uint nMaxInstances,
            uint nOutBufferSize,
            uint nInBufferSize,
            uint nDefaultTimeOut,
            ref SECURITY_ATTRIBUTES lpSecurityAttributes);

        [StructLayout(LayoutKind.Sequential)]
        private struct SECURITY_ATTRIBUTES
        {
            public uint nLength;
            public IntPtr lpSecurityDescriptor;
            [MarshalAs(UnmanagedType.Bool)]
            public bool bInheritHandle;
        }
    }

    /// <summary>
    /// Requirement 1.7：管道创建异常。调用方应退避重试，绝不降级到默认 DACL。
    /// </summary>
    internal sealed class BridgePipeCreateException : Exception
    {
        public BridgePipeCreateException(string message, Exception inner = null) : base(message, inner) { }
    }
}
