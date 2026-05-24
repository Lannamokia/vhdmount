using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using VHDMounter.RustDeskBridge.Frames;

namespace VHDMounter.RustDeskBridge.Crypto
{
    /// <summary>
    /// 5 类帧的 HMAC-SHA256 输入字节构造与校验（协议文档 §3 / §5.2 / §6.2 / §7.2 / §8.2 / §9.2）。
    ///
    /// 严格规则：
    /// - 字段分隔符为单个 LF（0x0A），无尾随换行
    /// - 整数为十进制 ASCII，无前导零、无 + 号
    /// - sha256Hex(x) = SHA-256(x as UTF-8 bytes) 的 64 位小写十六进制
    /// - 输出 32 字节摘要的标准 base64（带 = padding）
    /// - 比较使用 <see cref="CryptographicOperations.FixedTimeEquals"/>（Requirement 3.3 恒定时间）
    ///
    /// 通过 <see cref="IBridgeSecretProvider"/> 抽象拿密钥，<b>不</b>直接持有字段（任务 4.1 / Requirement 13.5）。
    /// </summary>
    internal sealed class HmacVerifier
    {
        private readonly IBridgeSecretProvider _secretProvider;

        public HmacVerifier(IBridgeSecretProvider secretProvider)
        {
            _secretProvider = secretProvider ?? throw new ArgumentNullException(nameof(secretProvider));
        }

        // ---------- HMAC 输入字节构造 ----------

        public static byte[] BuildHandshakeHmacInput(uint secretVersion, string nonce, long timestampMs)
        {
            return Utf8(string.Concat(
                HandshakeFrame.ProtocolLiteral, "\n",
                Decimal(secretVersion), "\n",
                nonce ?? string.Empty, "\n",
                Decimal(timestampMs)));
        }

        public static byte[] BuildReportHmacInput(
            uint secretVersion,
            string rustDeskId,
            string passwordKind,
            string password,
            string reason,
            long reportedAt,
            string nonce)
        {
            return Utf8(string.Concat(
                ReportFrame.ProtocolLiteral, "\n",
                Decimal(secretVersion), "\n",
                rustDeskId ?? string.Empty, "\n",
                passwordKind ?? string.Empty, "\n",
                Sha256Hex(password ?? string.Empty), "\n",
                reason ?? string.Empty, "\n",
                Decimal(reportedAt), "\n",
                nonce ?? string.Empty));
        }

        public static byte[] BuildLogHmacInput(
            uint secretVersion,
            string level,
            string target,
            string message,
            long timestampMs)
        {
            return Utf8(string.Concat(
                LogFrame.ProtocolLiteral, "\n",
                Decimal(secretVersion), "\n",
                level ?? string.Empty, "\n",
                target ?? string.Empty, "\n",
                Sha256Hex(message ?? string.Empty), "\n",
                Decimal(timestampMs)));
        }

        public static byte[] BuildPeerApprovalHmacInput(
            uint secretVersion,
            string controlledMachineId,
            string controllerId,
            string controllerName,
            string controllerPlatform,
            string controllerHwid,
            string peerSocketAddr,
            string connectionType,
            string requestNonce,
            long timestampMs)
        {
            return Utf8(string.Concat(
                PeerApprovalFrame.ProtocolLiteral, "\n",
                Decimal(secretVersion), "\n",
                controlledMachineId ?? string.Empty, "\n",
                controllerId ?? string.Empty, "\n",
                Sha256Hex(controllerName ?? string.Empty), "\n",
                controllerPlatform ?? string.Empty, "\n",
                Sha256Hex(controllerHwid ?? string.Empty), "\n",
                peerSocketAddr ?? string.Empty, "\n",
                connectionType ?? string.Empty, "\n",
                requestNonce ?? string.Empty, "\n",
                Decimal(timestampMs)));
        }

        public static byte[] BuildRevocationHmacInput(uint secretVersion, string reason, long issuedAt)
        {
            return Utf8(string.Concat(
                RevocationFrame.ProtocolLiteral, "\n",
                Decimal(secretVersion), "\n",
                reason ?? string.Empty, "\n",
                Decimal(issuedAt)));
        }

        // ---------- 计算 / 校验 ----------

        /// <summary>
        /// 用当前 active secret 算 HMAC-SHA256，返回 base64 字符串。
        /// </summary>
        public string ComputeMacBase64(ReadOnlySpan<byte> input)
        {
            var secret = _secretProvider.GetActiveSecret();
            Span<byte> mac = stackalloc byte[32];
            var written = HMACSHA256.HashData(secret, input, mac);
            if (written != 32)
            {
                throw new CryptographicException("HMAC-SHA256 输出长度异常");
            }
            return Convert.ToBase64String(mac);
        }

        /// <summary>
        /// 用任意密钥算 HMAC-SHA256（仅给 Revocation 推送 / 测试 / 跨语言夹具自检使用）。
        /// </summary>
        public static string ComputeMacBase64WithKey(ReadOnlySpan<byte> key, ReadOnlySpan<byte> input)
        {
            Span<byte> mac = stackalloc byte[32];
            HMACSHA256.HashData(key, input, mac);
            return Convert.ToBase64String(mac);
        }

        /// <summary>
        /// 恒定时间比较给定 base64 mac 与重算结果是否相等。
        /// 任何 base64 解码异常一律返回 false（与 mac 不匹配同码）。
        /// </summary>
        public bool VerifyMacBase64(ReadOnlySpan<byte> input, string macBase64)
        {
            byte[] expected;
            try
            {
                expected = Convert.FromBase64String(macBase64 ?? string.Empty);
            }
            catch (FormatException)
            {
                return false;
            }

            var secret = _secretProvider.GetActiveSecret();
            Span<byte> actual = stackalloc byte[32];
            HMACSHA256.HashData(secret, input, actual);

            if (expected.Length != 32)
            {
                return false;
            }

            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }

        // ---------- 帧级便捷入口 ----------

        public bool VerifyHandshake(HandshakeFrame frame)
        {
            if (frame == null) return false;
            if (frame.SecretVersion != _secretProvider.CurrentSecretVersion) return false;
            return VerifyMacBase64(
                BuildHandshakeHmacInput(frame.SecretVersion, frame.Nonce, frame.TimestampMs),
                frame.Proof);
        }

        public bool VerifyReport(ReportFrame frame)
        {
            if (frame == null) return false;
            return VerifyMacBase64(
                BuildReportHmacInput(
                    frame.SecretVersion, frame.RustDeskId, frame.PasswordKind,
                    frame.Password, frame.Reason, frame.ReportedAt, frame.Nonce),
                frame.Mac);
        }

        public bool VerifyLog(LogFrame frame)
        {
            if (frame == null) return false;
            return VerifyMacBase64(
                BuildLogHmacInput(frame.SecretVersion, frame.Level, frame.Target, frame.Message, frame.TimestampMs),
                frame.Mac);
        }

        public bool VerifyPeerApproval(PeerApprovalFrame frame)
        {
            if (frame == null) return false;
            return VerifyMacBase64(
                BuildPeerApprovalHmacInput(
                    frame.SecretVersion, frame.ControlledMachineId, frame.ControllerId,
                    frame.ControllerName, frame.ControllerPlatform, frame.ControllerHwid,
                    frame.PeerSocketAddr, frame.ConnectionType, frame.RequestNonce, frame.TimestampMs),
                frame.Mac);
        }

        /// <summary>
        /// 服务端推送 Revocation 帧时由 RevocationPublisher 调用。
        /// </summary>
        public string ComputeRevocationMac(uint secretVersion, string reason, long issuedAt)
        {
            return ComputeMacBase64(BuildRevocationHmacInput(secretVersion, reason, issuedAt));
        }

        // ---------- 内部辅助 ----------

        public static string Sha256Hex(string text)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private static string Decimal(long value) => value.ToString(CultureInfo.InvariantCulture);
        private static string Decimal(uint value) => value.ToString(CultureInfo.InvariantCulture);

        private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);
    }
}
