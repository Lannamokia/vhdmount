using System;
using System.IO;
using System.Linq;
using System.Text;
using VHDMounter;
using VHDMounter.RustDeskBridge.Frames;
using VHDMounter.RustDeskBridge.Log;
using Xunit;

namespace VHDMounter.Tests.RustDeskBridge.Properties
{
    [Trait("feature", "rustdesk-bridge-host")]
    [Trait("property", "Property 11: Log 帧映射与脱敏")]
    public sealed class LogIngestorTests
    {
        private static (MachineLogBuffer buffer, string spoolPath, string dir) BuildBuffer()
        {
            var dir = Path.Combine(Path.GetTempPath(), "vhdm-bridge-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var spoolPath = Path.Combine(dir, "spool.jsonl");
            return (new MachineLogBuffer(spoolPath, "test-session", 1024 * 1024), spoolPath, dir);
        }

        private static void Cleanup(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
            catch (IOException) { /* 忽略测试环境清理失败 */ }
        }

        [Fact]
        public void Ingest_ValidLevel_EnqueuesEntryWithBridgeComponent()
        {
            var (buffer, _, dir) = BuildBuffer();
            try
            {
                var ingestor = new LogIngestor(buffer);
                var ok = ingestor.Ingest(new LogFrame
                {
                    Protocol = LogFrame.ProtocolLiteral,
                    SecretVersion = 1,
                    Level = LogFrame.LevelWarn,
                    Target = "rustdesk::server::connection",
                    Message = "controlled login from 192.0.2.1",
                    TimestampMs = 1730000000500L,
                });
                Assert.True(ok);

                var pending = buffer.GetPendingBatch("test-session", 0, 100);
                Assert.Single(pending);
                Assert.Equal("rustdesk-bridge", pending[0].Component);
                Assert.Equal("warn", pending[0].Level);
                Assert.Contains("192.0.2.1", pending[0].Message);
            }
            finally
            {
                buffer.Dispose();
                Cleanup(dir);
            }
        }

        [Fact]
        public void Ingest_InvalidLevel_ReturnsFalse_DoesNotEnqueue()
        {
            var (buffer, _, dir) = BuildBuffer();
            try
            {
                var ingestor = new LogIngestor(buffer);
                var ok = ingestor.Ingest(new LogFrame
                {
                    Protocol = LogFrame.ProtocolLiteral,
                    SecretVersion = 1,
                    Level = "fatal", // 非法 level
                    Target = "x",
                    Message = "x",
                    TimestampMs = 1L,
                });
                Assert.False(ok);
                var pending = buffer.GetPendingBatch("test-session", 0, 100);
                Assert.Empty(pending);
            }
            finally
            {
                buffer.Dispose();
                Cleanup(dir);
            }
        }

        [Fact]
        public void Ingest_PasswordInMessage_GetsRedacted()
        {
            var (buffer, _, dir) = BuildBuffer();
            try
            {
                var ingestor = new LogIngestor(buffer);
                ingestor.Ingest(new LogFrame
                {
                    Protocol = LogFrame.ProtocolLiteral,
                    SecretVersion = 1,
                    Level = "info",
                    Target = "x",
                    Message = "password=SuperSecret123! and controllerHwid=abcd",
                    TimestampMs = 1L,
                });

                var pending = buffer.GetPendingBatch("test-session", 0, 100);
                Assert.Single(pending);
                Assert.DoesNotContain("SuperSecret123!", pending[0].Message);
                Assert.Contains("***", pending[0].Message);
            }
            finally
            {
                buffer.Dispose();
                Cleanup(dir);
            }
        }

        [Fact]
        public void TruncateUtf8_Boundary_DoesNotProduceHalfCodePoint()
        {
            var sb = new StringBuilder();
            while (Encoding.UTF8.GetByteCount(sb.ToString()) < 5000)
            {
                sb.Append('汪');
            }

            var truncated = LogIngestor.TruncateUtf8(sb.ToString(), LogIngestor.MaxMessageBytes);
            var bytes = Encoding.UTF8.GetBytes(truncated);
            Assert.True(bytes.Length <= LogIngestor.MaxMessageBytes);
            // 解出来不能抛 → 没有半个码点
            var roundtrip = Encoding.UTF8.GetString(bytes);
            Assert.Equal(truncated, roundtrip);
        }

        [Fact]
        public void TruncateUtf8_AlreadyShorter_NoTruncation()
        {
            var s = "abc";
            Assert.Equal("abc", LogIngestor.TruncateUtf8(s, 4096));
        }

        [Fact]
        public void FormatTimestampUtc_KnownEpoch()
        {
            // 2024-10-26T12:53:20.000Z 的毫秒戳是 1729947200000
            var ts = LogIngestor.FormatTimestampUtc(1729947200000L);
            Assert.Equal("2024-10-26T12:53:20.000Z", ts);
        }
    }
}
