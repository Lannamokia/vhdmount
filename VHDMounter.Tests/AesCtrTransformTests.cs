using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using VHDMounter.SoftwareDeploy;
using Xunit;

namespace VHDMounter.Tests
{
    public class AesCtrTransformTests
    {
        // ---------- Basic correctness ----------

        [Fact]
        public void EncryptDecryptRoundtrip_ShortMessage()
        {
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            var iv = new byte[16];
            RandomNumberGenerator.Fill(iv);

            var plaintext = Encoding.UTF8.GetBytes("Hello, CTR!");

            // Encrypt
            byte[] ciphertext;
            using (var aesCtr = new AesCtrTransform(key, iv))
            using (var ms = new MemoryStream())
            {
                ciphertext = aesCtr.TransformFinalBlock(plaintext, 0, plaintext.Length);
            }

            // Decrypt (CTR encryption = decryption)
            byte[] decrypted;
            using (var aesCtr = new AesCtrTransform(key, iv))
            {
                decrypted = aesCtr.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
            }

            Assert.Equal(plaintext, decrypted);
        }

        [Fact]
        public void EncryptDecryptRoundtrip_MultiBlockMessage()
        {
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            var iv = new byte[16];
            RandomNumberGenerator.Fill(iv);

            // 5 blocks (80 bytes) + 3 bytes = 83 bytes
            var plaintext = Encoding.UTF8.GetBytes(
                "This is a longer message that spans multiple 16-byte blocks for CTR mode testing.");

            byte[] ciphertext;
            using (var aesCtr = new AesCtrTransform(key, iv))
            {
                ciphertext = aesCtr.TransformFinalBlock(plaintext, 0, plaintext.Length);
            }

            byte[] decrypted;
            using (var aesCtr = new AesCtrTransform(key, iv))
            {
                decrypted = aesCtr.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
            }

            Assert.Equal(plaintext, decrypted);
        }

        [Fact]
        public void EncryptDecryptRoundtrip_EmptyMessage()
        {
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            var iv = new byte[16];
            RandomNumberGenerator.Fill(iv);

            var plaintext = Array.Empty<byte>();

            byte[] ciphertext;
            using (var aesCtr = new AesCtrTransform(key, iv))
            {
                ciphertext = aesCtr.TransformFinalBlock(plaintext, 0, 0);
            }

            Assert.Empty(ciphertext);
        }

        [Fact]
        public void EncryptDecryptRoundtrip_ExactBlockBoundary()
        {
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            var iv = new byte[16];
            RandomNumberGenerator.Fill(iv);

            // Exactly 32 bytes = 2 blocks
            var plaintext = Encoding.UTF8.GetBytes("0123456789ABCDEF0123456789ABCDEF");
            Assert.Equal(32, plaintext.Length);

            byte[] ciphertext;
            using (var aesCtr = new AesCtrTransform(key, iv))
            {
                ciphertext = aesCtr.TransformFinalBlock(plaintext, 0, plaintext.Length);
            }

            byte[] decrypted;
            using (var aesCtr = new AesCtrTransform(key, iv))
            {
                decrypted = aesCtr.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
            }

            Assert.Equal(plaintext, decrypted);
        }

        // ---------- CryptoStream integration ----------

        [Fact]
        public void CryptoStream_LargeFileRoundtrip()
        {
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            var iv = new byte[16];
            RandomNumberGenerator.Fill(iv);

            // 1 MB of random data
            var plaintext = new byte[1024 * 1024];
            RandomNumberGenerator.Fill(plaintext);

            byte[] ciphertext;
            using (var input = new MemoryStream(plaintext))
            using (var aesCtr = new AesCtrTransform(key, iv))
            using (var cryptoStream = new CryptoStream(input, aesCtr, CryptoStreamMode.Read))
            using (var output = new MemoryStream())
            {
                cryptoStream.CopyTo(output);
                ciphertext = output.ToArray();
            }

            byte[] decrypted;
            using (var input = new MemoryStream(ciphertext))
            using (var aesCtr = new AesCtrTransform(key, iv))
            using (var cryptoStream = new CryptoStream(input, aesCtr, CryptoStreamMode.Read))
            using (var output = new MemoryStream())
            {
                cryptoStream.CopyTo(output);
                decrypted = output.ToArray();
            }

            Assert.Equal(plaintext, decrypted);
        }

        [Fact]
        public void CryptoStream_ByteByByteRead()
        {
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            var iv = new byte[16];
            RandomNumberGenerator.Fill(iv);

            var plaintext = Encoding.UTF8.GetBytes("Streaming byte by byte!");

            byte[] ciphertext;
            using (var input = new MemoryStream(plaintext))
            using (var aesCtr = new AesCtrTransform(key, iv))
            using (var cryptoStream = new CryptoStream(input, aesCtr, CryptoStreamMode.Read))
            {
                ciphertext = new byte[plaintext.Length];
                for (int i = 0; i < plaintext.Length; i++)
                {
                    ciphertext[i] = (byte)cryptoStream.ReadByte();
                }
            }

            byte[] decrypted;
            using (var input = new MemoryStream(ciphertext))
            using (var aesCtr = new AesCtrTransform(key, iv))
            using (var cryptoStream = new CryptoStream(input, aesCtr, CryptoStreamMode.Read))
            {
                decrypted = new byte[ciphertext.Length];
                for (int i = 0; i < ciphertext.Length; i++)
                {
                    decrypted[i] = (byte)cryptoStream.ReadByte();
                }
            }

            Assert.Equal(plaintext, decrypted);
        }

        // ---------- Interoperability with Node.js crypto.createCipheriv('aes-256-ctr') ----------

        /// <summary>
        /// Known test vector generated by Node.js:
        ///   key = Buffer.alloc(32, 0xAB)
        ///   iv  = Buffer.concat([Buffer.alloc(8, 0xCD), Buffer.alloc(8)])
        ///   plaintext = 'Hello, AES-256-CTR interop test! This is a longer message to span multiple blocks.'
        /// </summary>
        [Fact]
        public void NodeJsInterop_KnownVector_Decrypt()
        {
            var key = Convert.FromBase64String("q6urq6urq6urq6urq6urq6urq6urq6urq6urq6urq6s=");
            var iv = Convert.FromBase64String("zc3Nzc3Nzc0AAAAAAAAAAA==");
            var ciphertext = Convert.FromBase64String(
                "1GsdB3vkSUkwM1fMmX4W6E0Q1s4PJhKrQxLoOYMXgJY3EO+y6E5Ra+Xx6J90jUdD3ZmYREvj1RiHIkZZb9ybh2IeXxxFXXNEb1ZIZ4Wx7484Ow==");
            var expectedPlaintext = Encoding.UTF8.GetBytes(
                "Hello, AES-256-CTR interop test! This is a longer message to span multiple blocks.");

            byte[] decrypted;
            using (var aesCtr = new AesCtrTransform(key, iv))
            {
                decrypted = aesCtr.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
            }

            Assert.Equal(expectedPlaintext, decrypted);
        }

        [Fact]
        public void NodeJsInterop_KnownVector_Encrypt()
        {
            var key = Convert.FromBase64String("q6urq6urq6urq6urq6urq6urq6urq6urq6urq6urq6s=");
            var iv = Convert.FromBase64String("zc3Nzc3Nzc0AAAAAAAAAAA==");
            var plaintext = Encoding.UTF8.GetBytes(
                "Hello, AES-256-CTR interop test! This is a longer message to span multiple blocks.");
            var expectedCiphertext = Convert.FromBase64String(
                "1GsdB3vkSUkwM1fMmX4W6E0Q1s4PJhKrQxLoOYMXgJY3EO+y6E5Ra+Xx6J90jUdD3ZmYREvj1RiHIkZZb9ybh2IeXxxFXXNEb1ZIZ4Wx7484Ow==");

            byte[] ciphertext;
            using (var aesCtr = new AesCtrTransform(key, iv))
            {
                ciphertext = aesCtr.TransformFinalBlock(plaintext, 0, plaintext.Length);
            }

            Assert.Equal(expectedCiphertext, ciphertext);
        }

        // ---------- Input validation ----------

        [Fact]
        public void Constructor_RejectsShortKey()
        {
            var shortKey = new byte[16]; // AES-128, not 256
            var iv = new byte[16];
            Assert.Throws<ArgumentException>(() => new AesCtrTransform(shortKey, iv));
        }

        [Fact]
        public void Constructor_RejectsShortIv()
        {
            var key = new byte[32];
            var shortIv = new byte[8];
            Assert.Throws<ArgumentException>(() => new AesCtrTransform(key, shortIv));
        }

        [Fact]
        public void Constructor_RejectsNullKey()
        {
            var iv = new byte[16];
            Assert.Throws<ArgumentException>(() => new AesCtrTransform(null!, iv));
        }

        [Fact]
        public void Constructor_RejectsNullIv()
        {
            var key = new byte[32];
            Assert.Throws<ArgumentException>(() => new AesCtrTransform(key, null!));
        }

        // ---------- Counter behavior ----------

        [Fact]
        public void CounterIncrements_ByOnePerBlock()
        {
            // If we encrypt the same plaintext twice with the same key but
            // IVs that differ only by counter=0 vs counter=1, the ciphertext
            // should differ in a predictable way (the keystream is shifted).
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);

            var nonce = new byte[8];
            RandomNumberGenerator.Fill(nonce);
            var iv0 = new byte[16];
            Buffer.BlockCopy(nonce, 0, iv0, 0, 8);
            // iv0[8..15] already zero

            var iv1 = new byte[16];
            Buffer.BlockCopy(nonce, 0, iv1, 0, 8);
            iv1[15] = 1; // counter = 1

            var plaintext = new byte[32]; // 2 blocks of zeros

            byte[] cipher0, cipher1;
            using (var aes0 = new AesCtrTransform(key, iv0))
            {
                cipher0 = aes0.TransformFinalBlock(plaintext, 0, plaintext.Length);
            }
            using (var aes1 = new AesCtrTransform(key, iv1))
            {
                cipher1 = aes1.TransformFinalBlock(plaintext, 0, plaintext.Length);
            }

            // First block should differ because counter differs
            var block0_0 = new byte[16];
            var block0_1 = new byte[16];
            Buffer.BlockCopy(cipher0, 0, block0_0, 0, 16);
            Buffer.BlockCopy(cipher1, 0, block0_1, 0, 16);
            Assert.NotEqual(block0_0, block0_1);

            // Second block of cipher0 should equal first block of cipher1
            // because counter in cipher0 block 1 = 1, which equals counter in cipher1 block 0
            var block1_0 = new byte[16];
            Buffer.BlockCopy(cipher0, 16, block1_0, 0, 16);
            Assert.Equal(block1_0, block0_1);
        }

        [Fact]
        public void TransformBlock_ProcessesMultipleBlocks()
        {
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            var iv = new byte[16];
            RandomNumberGenerator.Fill(iv);

            var plaintext = Encoding.UTF8.GetBytes("0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF");
            var output = new byte[plaintext.Length];

            using var aesCtr = new AesCtrTransform(key, iv);
            int transformed = aesCtr.TransformBlock(plaintext, 0, plaintext.Length, output, 0);

            Assert.Equal(plaintext.Length, transformed);
            // Verify roundtrip
            var roundtrip = new byte[plaintext.Length];
            using var aesCtr2 = new AesCtrTransform(key, iv);
            aesCtr2.TransformBlock(output, 0, output.Length, roundtrip, 0);
            Assert.Equal(plaintext, roundtrip);
        }
    }
}
