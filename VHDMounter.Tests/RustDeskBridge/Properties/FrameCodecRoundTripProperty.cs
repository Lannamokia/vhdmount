using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using FsCheck;
using FsCheck.Xunit;
using VHDMounter.RustDeskBridge.Pipe;
using Xunit;

namespace VHDMounter.Tests.RustDeskBridge.Properties
{
    /// <summary>
    /// Property 2: 帧编解码 round-trip + 边界拒绝（任务 2.2）
    /// Validates: Requirements 2.1, 2.2, 2.3
    /// </summary>
    [Trait("feature", "rustdesk-bridge-host")]
    [Trait("property", "Property 2: 帧编解码 round-trip")]
    public sealed class FrameCodecRoundTripProperty
    {
        [Property(MaxTest = 100)]
        public Property RoundTripsAnyValidJsonUnderMaxFrameBytes()
        {
            return Prop.ForAll(
                ArbJsonPayload(),
                jsonBytes =>
                {
                    using var pipe = new MemoryStream();
                    FrameCodec.WriteFrameAsync(pipe, jsonBytes, CancellationToken.None)
                        .AsTask().GetAwaiter().GetResult();
                    pipe.Position = 0;
                    var read = FrameCodec.ReadFrameAsync(pipe, CancellationToken.None)
                        .AsTask().GetAwaiter().GetResult();
                    return read.SequenceEqual(jsonBytes.ToArray());
                });
        }

        [Fact]
        public void ReadFrame_LengthPrefixOverMax_Throws()
        {
            var pipe = new MemoryStream();
            // 写入长度前缀 = MaxFrameBytes + 1，紧跟少量字节即可
            var lengthBytes = BitConverter.GetBytes((uint)(FrameCodec.MaxFrameBytes + 1));
            if (!BitConverter.IsLittleEndian) Array.Reverse(lengthBytes);
            pipe.Write(lengthBytes, 0, 4);
            pipe.WriteByte(0x7B); // '{'
            pipe.Position = 0;

            Assert.Throws<InvalidDataException>(() =>
                FrameCodec.ReadFrameAsync(pipe, CancellationToken.None).AsTask().GetAwaiter().GetResult());
        }

        [Fact]
        public void WriteFrame_PayloadOverMax_Throws()
        {
            var pipe = new MemoryStream();
            var oversized = new byte[FrameCodec.MaxFrameBytes + 1];
            Assert.Throws<ArgumentException>(() =>
                FrameCodec.WriteFrameAsync(pipe, oversized, CancellationToken.None)
                    .AsTask().GetAwaiter().GetResult());
        }

        [Fact]
        public void ReadFrame_TruncatedPayload_Throws()
        {
            var pipe = new MemoryStream();
            // 长度前缀 = 10 但只写 5 字节 payload
            var lengthBytes = BitConverter.GetBytes((uint)10);
            if (!BitConverter.IsLittleEndian) Array.Reverse(lengthBytes);
            pipe.Write(lengthBytes, 0, 4);
            pipe.Write(new byte[] { 0x31, 0x32, 0x33, 0x34, 0x35 }, 0, 5);
            pipe.Position = 0;

            Assert.Throws<InvalidDataException>(() =>
                FrameCodec.ReadFrameAsync(pipe, CancellationToken.None).AsTask().GetAwaiter().GetResult());
        }

        [Fact]
        public void RoundTrip_EmptyJsonObject()
        {
            var json = Encoding.UTF8.GetBytes("{}");
            using var pipe = new MemoryStream();
            FrameCodec.WriteFrameAsync(pipe, json, CancellationToken.None)
                .AsTask().GetAwaiter().GetResult();
            pipe.Position = 0;
            var read = FrameCodec.ReadFrameAsync(pipe, CancellationToken.None)
                .AsTask().GetAwaiter().GetResult();
            Assert.Equal(json, read);
        }

        [Fact]
        public void RoundTrip_AtExactlyMaxFrameBytes()
        {
            // 构造一份恰好 MaxFrameBytes 字节的合法 JSON（用一串重复 ASCII 字符做 string 字段）
            var prefix = "{\"k\":\"";
            var suffix = "\"}";
            var fillerLength = FrameCodec.MaxFrameBytes - prefix.Length - suffix.Length;
            var json = prefix + new string('a', fillerLength) + suffix;
            var bytes = Encoding.UTF8.GetBytes(json);
            Assert.Equal(FrameCodec.MaxFrameBytes, bytes.Length);

            using var pipe = new MemoryStream();
            FrameCodec.WriteFrameAsync(pipe, bytes, CancellationToken.None)
                .AsTask().GetAwaiter().GetResult();
            pipe.Position = 0;
            var read = FrameCodec.ReadFrameAsync(pipe, CancellationToken.None)
                .AsTask().GetAwaiter().GetResult();
            Assert.Equal(bytes, read);
        }

        // ---------- FsCheck 生成器：≤ MaxFrameBytes 的合法 UTF-8 JSON ----------

        private static Arbitrary<byte[]> ArbJsonPayload()
        {
            // 生成的 JSON 形态：{"v":<int>,"s":"<ascii printable>","arr":[<int>,<int>,...]}
            // 长度被 FsCheck 自动收缩，且我们在每次构造完毕后断言 ≤ MaxFrameBytes 才返回。
            var gen = Arb.Default.Int32().Generator
                .Select(seed =>
                {
                    var rng = new System.Random(seed);
                    var stringLen = rng.Next(0, 64);
                    var sb = new StringBuilder(stringLen);
                    for (var i = 0; i < stringLen; i++)
                    {
                        var ch = (char)rng.Next(0x20, 0x7E + 1);
                        if (ch == '\\' || ch == '"')
                        {
                            sb.Append('a');
                        }
                        else
                        {
                            sb.Append(ch);
                        }
                    }
                    var arrLen = rng.Next(0, 8);
                    var arr = string.Join(",", Enumerable.Range(0, arrLen).Select(_ => rng.Next(int.MinValue, int.MaxValue).ToString()));
                    var json = $"{{\"v\":{rng.Next(int.MinValue, int.MaxValue)},\"s\":\"{sb}\",\"arr\":[{arr}]}}";
                    return Encoding.UTF8.GetBytes(json);
                });

            return Arb.From(gen.Where(bytes =>
                bytes.Length <= FrameCodec.MaxFrameBytes && IsValidUtf8Json(bytes)));
        }

        private static bool IsValidUtf8Json(byte[] bytes)
        {
            try
            {
                using var doc = JsonDocument.Parse(bytes);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
