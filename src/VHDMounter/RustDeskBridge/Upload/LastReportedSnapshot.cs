using System;
using System.Security.Cryptography;
using System.Text;
using VHDMounter.RustDeskBridge.Frames;

namespace VHDMounter.RustDeskBridge.Upload
{
    /// <summary>
    /// Requirement 5 / 6.1–6.4 / 7.2：进程内存中的 (rustDeskId, passwordKind, password,
    /// reason, reportedAt, secretVersion) 元组缓存。Bridge_Server 在每帧 Report 通过校验后
    /// 调 <see cref="TryReplace"/> 判定是否要把 snapshot 上行 VHDSelectServer。
    ///
    /// 密码以 byte[] 形态保存，替换 / Clear 时调用
    /// <see cref="CryptographicOperations.ZeroMemory"/> 抹零。
    /// </summary>
    internal sealed class LastReportedSnapshot
    {
        private readonly object _gate = new object();
        private string _rustDeskId = string.Empty;
        private string _passwordKind = string.Empty;
        private byte[] _passwordBytes = Array.Empty<byte>();
        private string _reason = string.Empty;
        private long _reportedAt;
        private uint _secretVersion;
        private bool _hasValue;

        public bool HasValue
        {
            get { lock (_gate) return _hasValue; }
        }

        /// <summary>
        /// 当前缓存的 reportedAt（仅给诊断 / 测试使用）。无值时返回 0。
        /// </summary>
        public long ReportedAt
        {
            get { lock (_gate) return _reportedAt; }
        }

        /// <summary>
        /// 比较 (rustDeskId, passwordKind, password) 三元组：
        /// - 相等且 frame.reason == "heartbeat" → 仅刷新 reportedAt（<see cref="TouchHeartbeat"/> 等价）
        /// - 不等 OR reason ∈ {startup,id_change,password_change,rotation} → 替换缓存并设
        ///   <paramref name="requiresUpload"/> 为 true（Requirement 6.4）。
        ///
        /// 返回的 RawPasswordView 仅在调用方持有的局部变量中存在，**不**被本类长期持有。
        /// </summary>
        public bool TryReplace(ReportFrame frame, out bool requiresUpload, out byte[] passwordSnapshotForUpload)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            var incomingPwd = Encoding.UTF8.GetBytes(frame.Password ?? string.Empty);

            lock (_gate)
            {
                var sameTriplet = _hasValue
                    && string.Equals(_rustDeskId, frame.RustDeskId ?? string.Empty, StringComparison.Ordinal)
                    && string.Equals(_passwordKind, frame.PasswordKind ?? string.Empty, StringComparison.Ordinal)
                    && CryptographicOperations.FixedTimeEquals(_passwordBytes, incomingPwd);

                if (sameTriplet && string.Equals(frame.Reason, ReportFrame.ReasonHeartbeat, StringComparison.Ordinal))
                {
                    // §6.3：仅刷 reportedAt
                    _reportedAt = frame.ReportedAt;
                    _reason = frame.Reason ?? string.Empty;
                    requiresUpload = false;
                    passwordSnapshotForUpload = null;
                    CryptographicOperations.ZeroMemory(incomingPwd);
                    return false;
                }

                // 触发上行：要么三元组变化，要么 reason ∈ 非 heartbeat 集合
                CryptographicOperations.ZeroMemory(_passwordBytes);
                _passwordBytes = incomingPwd;
                _rustDeskId = frame.RustDeskId ?? string.Empty;
                _passwordKind = frame.PasswordKind ?? string.Empty;
                _reason = frame.Reason ?? string.Empty;
                _reportedAt = frame.ReportedAt;
                _secretVersion = frame.SecretVersion;
                _hasValue = true;

                requiresUpload = true;
                // 给上行路径一份独立副本（避免 ZeroMemory 抹掉调用方还在用的字节）
                passwordSnapshotForUpload = new byte[incomingPwd.Length];
                Buffer.BlockCopy(incomingPwd, 0, passwordSnapshotForUpload, 0, incomingPwd.Length);
                return true;
            }
        }

        /// <summary>
        /// 显式 heartbeat 刷新（少数纯诊断路径用；正常路径走 TryReplace）。
        /// </summary>
        public void TouchHeartbeat(long reportedAt)
        {
            lock (_gate)
            {
                if (!_hasValue) return;
                _reportedAt = reportedAt;
            }
        }

        /// <summary>
        /// Bridge_Session 结束 / 进程退出 / Revocation 推送时调用：抹零 password byte[]。
        /// </summary>
        public void Clear()
        {
            lock (_gate)
            {
                CryptographicOperations.ZeroMemory(_passwordBytes);
                _passwordBytes = Array.Empty<byte>();
                _rustDeskId = string.Empty;
                _passwordKind = string.Empty;
                _reason = string.Empty;
                _reportedAt = 0;
                _secretVersion = 0;
                _hasValue = false;
            }
        }
    }
}
