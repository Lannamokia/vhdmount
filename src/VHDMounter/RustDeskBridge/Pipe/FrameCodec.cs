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
        /// <exception cref="EndOfStreamException">
        /// 对端在帧边界正常关闭管道（首字节都没读到）。这是 <b>正常</b> 会话结束，
        /// 不属于协议错误；调用方应当安静关闭会话，<b>不</b>记录为 ERROR / 协议异常。
        /// </exception>
        /// <exception cref="InvalidDataException">
        /// 长度前缀 &gt; <see cref="MaxFrameBytes"/>，或<b>读到一半</b>被对端关闭
        /// （已读了部分长度前缀字节或部分载荷字节再断开）。这才是真正的协议异常。
        /// </exception>
        public static async ValueTask<byte[]> ReadFrameAsync(Stream pipe, CancellationToken ct)
        {
            if (pipe == null) throw new ArgumentNullException(nameof(pipe));

            var lengthBuffer = new byte[4];
            // 关键区别：读 length-prefix 时第一次 read 就返回 0 → 视为对端在帧边界
            // 上正常关闭，抛 EndOfStreamException 让调用方安静收尾。
            // 已经读了部分前缀字节再断 → 协议异常。
            await ReadExactAsync(pipe, lengthBuffer, ct, allowCleanEofAtStart: true).ConfigureAwait(false);

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

            // 长度前缀已经读到了 → 必须把载荷读完，半截断开 = 协议异常
            var payload = new byte[length];
            await ReadExactAsync(pipe, payload, ct, allowCleanEofAtStart: false).ConfigureAwait(false);
            return payload;
        }

        /// <summary>
        /// 写入一帧。<paramref name="jsonUtf8"/> 必须已经是 UTF-8 编码（无 BOM）的 JSON 字节。
        /// 返回值是"实际写到 stream 的总字节数"（4 字节长度前缀 + payload），便于诊断埋点
        /// 与 RustDesk_Controlled 一侧对账（docs/vhd-rustdesk-bridge-controlled-side-handoff.md
        /// §9.Q8 / §10 事件 9）。
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="jsonUtf8"/> 长度 &gt; <see cref="MaxFrameBytes"/>。</exception>
        public static async ValueTask<int> WriteFrameAsync(Stream pipe, ReadOnlyMemory<byte> jsonUtf8, CancellationToken ct)
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
            return 4 + jsonUtf8.Length;
        }

        private static async ValueTask ReadExactAsync(
            Stream pipe, byte[] buffer, CancellationToken ct, bool allowCleanEofAtStart)
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = await pipe.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    if (allowCleanEofAtStart && offset == 0)
                    {
                        // 帧边界上对端正常关闭 —— 不是协议错误
                        throw new EndOfStreamException(
                            "RustDesk 桥管道已被对端关闭（无新帧）");
                    }
                    throw new InvalidDataException(
                        "RustDesk 桥管道在帧读取中途被对端关闭");
                }
                offset += read;
            }
        }
    }
}
