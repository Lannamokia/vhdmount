#nullable enable
using System;
using System.Security.Cryptography;

namespace VHDMounter.SoftwareDeploy
{
    /// <summary>
    /// AES-256-CTR 模式的 ICryptoTransform 实现。
    /// .NET 没有内置 CTR 模式，需通过 ECB 加密生成 keystream 再与明文 XOR 实现。
    /// 支持 CryptoStream 流式处理，内存占用恒定（仅一个 block）。
    /// </summary>
    public class AesCtrTransform : ICryptoTransform
    {
        private readonly ICryptoTransform _aesEncryptor;
        private readonly byte[] _counter;
        private readonly byte[] _keystream;
        private int _keystreamIndex;

        public AesCtrTransform(byte[] key, byte[] iv)
        {
            if (key == null || key.Length != 32)
                throw new ArgumentException("AES-256-CTR 需要 32 字节密钥", nameof(key));
            if (iv == null || iv.Length != 16)
                throw new ArgumentException("AES-256-CTR 需要 16 字节 IV", nameof(iv));

            var aes = Aes.Create();
            aes.Key = key;
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;
            _aesEncryptor = aes.CreateEncryptor();

            _counter = new byte[16];
            Buffer.BlockCopy(iv, 0, _counter, 0, 16);
            _keystream = new byte[16];
            _keystreamIndex = 16; // 强制首次调用时重新生成 keystream
        }

        public int InputBlockSize => 1;
        public int OutputBlockSize => 1;
        public bool CanTransformMultipleBlocks => true;
        public bool CanReuseTransform => false;

        public int TransformBlock(byte[] inputBuffer, int inputOffset, int inputCount, byte[] outputBuffer, int outputOffset)
        {
            for (int i = 0; i < inputCount; i++)
            {
                if (_keystreamIndex >= 16)
                {
                    _aesEncryptor.TransformBlock(_counter, 0, 16, _keystream, 0);
                    IncrementCounter();
                    _keystreamIndex = 0;
                }
                outputBuffer[outputOffset + i] = (byte)(inputBuffer[inputOffset + i] ^ _keystream[_keystreamIndex++]);
            }
            return inputCount;
        }

        public byte[] TransformFinalBlock(byte[] inputBuffer, int inputOffset, int inputCount)
        {
            var output = new byte[inputCount];
            TransformBlock(inputBuffer, inputOffset, inputCount, output, 0);
            return output;
        }

        private void IncrementCounter()
        {
            for (int i = 15; i >= 0; i--)
            {
                if (++_counter[i] != 0)
                    break;
            }
        }

        public void Dispose()
        {
            _aesEncryptor.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
