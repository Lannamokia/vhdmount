using System;
using System.Security.Cryptography;

namespace VHDMounter.RustDeskBridge.Policy
{
    /// <summary>
    /// Requirement 8.4 / 8.7 / 8.8：进程启动时一次性生成 32 字节会话密钥 O，对
    /// Trusted_Controllers_Snapshot 字节做 AES-256-GCM 包裹（12 字节 iv + 16 字节 authTag）。
    /// 每次 Wrap 都重新随机 iv；解开后调用方负责立即抹零工作 buffer。
    ///
    /// 这是"内存防扫描"轻量混淆，<b>不</b>是密码学保管 —— 真攻击者拿到进程地址空间能解开，
    /// 但能阻止一次性 dump 出明文 controllerId。
    /// </summary>
    internal sealed class InMemoryObfuscation : IDisposable
    {
        private readonly byte[] _sessionKey;
        private bool _disposed;

        public InMemoryObfuscation()
            : this(RandomNumberGenerator.GetBytes(32))
        {
        }

        // 测试夹具入口：注入确定性密钥
        internal InMemoryObfuscation(byte[] sessionKey)
        {
            if (sessionKey == null) throw new ArgumentNullException(nameof(sessionKey));
            if (sessionKey.Length != 32)
            {
                throw new ArgumentException("InMemoryObfuscation 会话密钥必须正好 32 字节", nameof(sessionKey));
            }
            _sessionKey = (byte[])sessionKey.Clone();
        }

        /// <summary>
        /// 包裹 plain → (cipher, iv, authTag)；plain 由调用方在使用完毕后抹零。
        /// </summary>
        public ObfuscatedBuffer Wrap(ReadOnlySpan<byte> plain)
        {
            ThrowIfDisposed();
            var iv = RandomNumberGenerator.GetBytes(12);
            var cipher = new byte[plain.Length];
            var tag = new byte[16];
            using var aes = new AesGcm(_sessionKey, tagSizeInBytes: 16);
            aes.Encrypt(iv, plain, cipher, tag);
            return new ObfuscatedBuffer(cipher, iv, tag);
        }

        /// <summary>
        /// 解开 ObfuscatedBuffer，返回明文字节。调用方使用完毕后 SHALL
        /// <see cref="CryptographicOperations.ZeroMemory"/> 抹零。
        /// </summary>
        public byte[] Unwrap(ObfuscatedBuffer buffer)
        {
            ThrowIfDisposed();
            if (buffer.Cipher == null) throw new ArgumentException("ObfuscatedBuffer 缺少 cipher");
            var plain = new byte[buffer.Cipher.Length];
            using var aes = new AesGcm(_sessionKey, tagSizeInBytes: 16);
            aes.Decrypt(buffer.Iv, buffer.Cipher, buffer.AuthTag, plain);
            return plain;
        }

        public void Dispose()
        {
            if (_disposed) return;
            CryptographicOperations.ZeroMemory(_sessionKey);
            _disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(InMemoryObfuscation));
        }
    }

    /// <summary>
    /// AES-256-GCM 包裹结果。所有字段都是只读字节副本；不可变。
    /// </summary>
    internal readonly struct ObfuscatedBuffer
    {
        public ObfuscatedBuffer(byte[] cipher, byte[] iv, byte[] authTag)
        {
            Cipher = cipher;
            Iv = iv;
            AuthTag = authTag;
        }

        public byte[] Cipher { get; }
        public byte[] Iv { get; }
        public byte[] AuthTag { get; }
    }
}
