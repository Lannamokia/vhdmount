using System;
using System.Security.Cryptography;
using System.Text;

namespace VHDMounter.RustDeskBridge.Upload
{
    /// <summary>
    /// Requirement 6.8：诊断日志 / 内部 trace 中所有"代指密码"的字面量统一走这里。
    /// **绝不**输出原始位数（避免泄露密码长度）。
    /// </summary>
    internal static class PasswordRedaction
    {
        /// <summary>固定字面量，凡是密码字段都可以用这一份替换。</summary>
        public const string Mask = "***";

        /// <summary>
        /// sha256(password) 前 8 个十六进制字符。空字符串得到固定常量
        /// <c>e3b0c442</c>（与协议文档 §6 / §8 的"sha256("") 常量"前缀一致）。
        /// </summary>
        public static string ShortHash(string password)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password ?? string.Empty));
            return Convert.ToHexString(bytes, 0, 4).ToLowerInvariant();
        }

        /// <summary>
        /// 同上但接受字节序列（避免明文密码再走一次 string 拷贝）。
        /// </summary>
        public static string ShortHash(ReadOnlySpan<byte> passwordUtf8)
        {
            Span<byte> hash = stackalloc byte[32];
            SHA256.HashData(passwordUtf8, hash);
            return Convert.ToHexString(hash[..4]).ToLowerInvariant();
        }
    }
}
