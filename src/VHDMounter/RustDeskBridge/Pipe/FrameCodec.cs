using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace VHDMounter.RustDeskBridge.Pipe
{
    /// <summary>
    /// 协议文档 §2.3 规定的"4 字节小端 u32 长度前缀 + UTF-8 JSON 载荷（无 BOM）"外壳
    /// 编解码。<see cref="MaxFrameBytes"/> 上限恒为 65536；前缀超过即抛
    /// <see cref="InvalidDataException"/>。
    ///
    /// 不做应用层 keepalive、不做外壳层 checksum——帧完整性完全由各帧 HMAC 保证
    /// （Requirement 2.4 / 2.5）。
    /// </summary>
    internal static class FrameCodec
    {
        /// <summary>协议文档 §2.3 规定的最大帧大小：64 KiB。</summary>
        public const int MaxFrameBytes = 65536;

        /// <summary>
        /// 按外壳层语义读取一帧。返回 UTF-8 编码的 JSON 字节数组（无尾随 NUL）。
        /// </summary>
        /// <exception cref="InvalidDataException">
        /// 长度前缀 &gt; <see cref="MaxFrameBytes"/>，或读取被对端意外关闭。
        /// </exception>
        public static async ValueTask<byte[]> ReadFrameAsync(Stream pipe, CancellationToken ct)
        {
            if (pipe == null) throw new ArgumentNullException(nameof(pipe));

            var lengthBuffer = new byte[4];
            await ReadExactAsync(pipe, lengthBuffer, ct).ConfigureAwait(false);

            var length = BinaryPrimitives.ReadUInt32LittleEndian(lengthBuffer);
            if (length > MaxFrameBytes)
            {
                throw new InvalidDataException(
                    $"RustDesk 桥帧长度前缀 {length} 超过上限 {MaxFrameBytes}");
            }

            if (length == 0)
            {
                return Array.Empty<byte>();
            }

            var payload = new byte[length];
            await ReadExactAsync(pipe, payload, ct).ConfigureAwait(false);
            return payload;
        }

        /// <summary>
        /// 写入一帧。<paramref name="jsonUtf8"/> 必须已经是 UTF-8 编码（无 BOM）的 JSON 字节。
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="jsonUtf8"/> 长度 &gt; <see cref="MaxFrameBytes"/>。</exception>
        public static async ValueTask WriteFrameAsync(Stream pipe, ReadOnlyMemory<byte> jsonUtf8, CancellationToken ct)
        {
            if (pipe == null) throw new ArgumentNullException(nameof(pipe));
            if (jsonUtf8.Length > MaxFrameBytes)
            {
                throw new ArgumentException(
                    $"RustDesk 桥写入帧 {jsonUtf8.Length} 超过上限 {MaxFrameBytes}",
                    nameof(jsonUtf8));
            }

            var lengthBuffer = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(lengthBuffer, (uint)jsonUtf8.Length);
            await pipe.WriteAsync(lengthBuffer.AsMemory(0, 4), ct).ConfigureAwait(false);
            if (jsonUtf8.Length > 0)
            {
                await pipe.WriteAsync(jsonUtf8, ct).ConfigureAwait(false);
            }
            await pipe.FlushAsync(ct).ConfigureAwait(false);
        }

        private static async ValueTask ReadExactAsync(Stream pipe, byte[] buffer, CancellationToken ct)
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = await pipe.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    throw new InvalidDataException("RustDesk 桥管道在帧读取中途被对端关闭");
                }
                offset += read;
            }
        }
    }
}
