using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using VHDMounter.RustDeskBridge.Policy;
using Xunit;

namespace VHDMounter.Tests.RustDeskBridge.Properties
{
    [Trait("feature", "rustdesk-bridge-host")]
    [Trait("property", "Property 10: 快照不持久化与 In_Memory_Obfuscation round-trip")]
    public sealed class InMemoryObfuscationTests
    {
        [Fact]
        public void Wrap_Unwrap_RoundTripsBytes()
        {
            using var io = new InMemoryObfuscation();
            var plain = Encoding.UTF8.GetBytes("hello world payload");
            var wrapped = io.Wrap(plain);
            var unwrapped = io.Unwrap(wrapped);
            Assert.Equal(plain, unwrapped);
        }

        [Fact]
        public void Wrap_TwoCallsSamePlain_ProduceDifferentCipher()
        {
            using var io = new InMemoryObfuscation();
            var plain = Encoding.UTF8.GetBytes("same input bytes");
            var w1 = io.Wrap(plain);
            var w2 = io.Wrap(plain);
            Assert.NotEqual(w1.Iv, w2.Iv);
            Assert.NotEqual(w1.Cipher, w2.Cipher);
        }

        [Fact]
        public void Wrap_EmptyBytes_RoundTrip()
        {
            using var io = new InMemoryObfuscation();
            var wrapped = io.Wrap(Array.Empty<byte>());
            var unwrapped = io.Unwrap(wrapped);
            Assert.Empty(unwrapped);
        }

        [Fact]
        public void TamperedCipher_ThrowsOnUnwrap()
        {
            using var io = new InMemoryObfuscation();
            var plain = Encoding.UTF8.GetBytes("payload");
            var wrapped = io.Wrap(plain);
            // 翻转 cipher 任一字节 → AES-GCM 必须 reject
            wrapped.Cipher[0] ^= 0x01;
            Assert.ThrowsAny<CryptographicException>(() => io.Unwrap(wrapped));
        }

        [Fact]
        public void TamperedAuthTag_ThrowsOnUnwrap()
        {
            using var io = new InMemoryObfuscation();
            var plain = Encoding.UTF8.GetBytes("payload");
            var wrapped = io.Wrap(plain);
            wrapped.AuthTag[0] ^= 0x01;
            Assert.ThrowsAny<CryptographicException>(() => io.Unwrap(wrapped));
        }

        [Fact]
        public void DifferentSessions_DoNotShareKey()
        {
            using var io1 = new InMemoryObfuscation();
            using var io2 = new InMemoryObfuscation();
            var plain = Encoding.UTF8.GetBytes("payload");
            var wrapped = io1.Wrap(plain);
            // io2 持有不同的随机 32 字节会话密钥 → 必然解开失败
            Assert.ThrowsAny<CryptographicException>(() => io2.Unwrap(wrapped));
        }

        [Fact]
        public void DeterministicSessionKey_FixtureRoundTrip()
        {
            var key = new byte[32];
            for (var i = 0; i < 32; i++) key[i] = (byte)i;
            using var io = new InMemoryObfuscation(key);
            var plain = Encoding.UTF8.GetBytes("fixture-deterministic-payload");
            var wrapped = io.Wrap(plain);
            var roundtrip = io.Unwrap(wrapped);
            Assert.Equal(plain, roundtrip);
        }
    }
}
