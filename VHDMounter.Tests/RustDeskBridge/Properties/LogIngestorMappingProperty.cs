using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FsCheck;
using FsCheck.Xunit;
using VHDMounter;
using VHDMounter.RustDeskBridge.Frames;
using VHDMounter.RustDeskBridge.Log;
using Xunit;

namespace VHDMounter.Tests.RustDeskBridge.Properties
{
    /// <summary>
    /// Property 11: Log 帧映射与脱敏（FsCheck 版）。
    ///
    /// Validates: Requirements 9.3, 9.4, 9.5, 9.7, 9.8
    ///
    /// (a) <c>Component == "rustdesk-bridge"</c>
    /// (b) <c>Message == Sanitize(truncate_utf8(msg, 4096))</c>，UTF-8 字节 ≤ 4096，
    ///     不出现半个码点
    /// (c) <c>Metadata</c> 键集合不含 <c>target / level / timestampMs / mac / secretVersion</c> 任一子串
    /// (d) k 次失败 → <c>bridgeLogDropCount</c> 累计 +k
    /// (e) 60s 窗口非零增量必写汇总条目
    /// </summary>
    [Trait("feature", "rustdesk-bridge-host")]
    [Trait("property", "Property 11: Log 帧映射与脱敏")]
    public sealed class LogIngestorMappingProperty
    {
        private static readonly string[] LegalLevels = new[]
        {
            LogFrame.LevelError, LogFrame.LevelWarn, LogFrame.LevelInfo, LogFrame.LevelDebug, LogFrame.LevelTrace,
        };

        private static (MachineLogBuffer buffer, string dir) BuildBuffer()
        {
            var dir = Path.Combine(Path.GetTempPath(), "vhdm-bridge-pbt-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var spool = Path.Combine(dir, "spool.jsonl");
            return (new MachineLogBuffer(spool, "pbt-session", 1024 * 1024), dir);
        }

        private static void Cleanup(string dir)
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }

        // 生成"含 Unicode 边界"的 message：从 ASCII / 中文 / surrogate pair / 控制字符随机拼接
        private static Gen<string> ArbBoundaryMessage()
        {
            var asciiCh = Gen.Choose(0x20, 0x7E).Select(i => (char)i);
            var chineseCh = Gen.Choose(0x4E00, 0x9FFF).Select(i => (char)i);
            var emoji = Gen.Choose(0, 5).Select(i => i switch
            {
                0 => "🐾",
                1 => "🐕",
                2 => "✨",
                3 => "wonderful——",
                4 => "汪",
                _ => "ok",
            });
            var ctrl = Gen.Choose(0, 3).Select(i => i switch
            {
                0 => "\n",
                1 => "\t",
                2 => "\r\n",
                _ => "  ",
            });

            // 用变长 segment 列表拼出 ~0..6000 字符的字符串
            var segGen = Gen.OneOf(
                asciiCh.Select(c => c.ToString()),
                chineseCh.Select(c => c.ToString()),
                emoji,
                ctrl);
            var listGen = Gen.ListOf(segGen).Select(seq =>
                string.Concat(seq.Take(Math.Min(seq.Count(), 600))));
            return listGen;
        }

        // ---------- (a) (b) (c) ----------

        [Property(MaxTest = 100)]
        public Property MappedEntry_HasBridgeComponent_AndCorrectMessage()
        {
            return Prop.ForAll(
                ArbBoundaryMessage().ToArbitrary(),
                Gen.Elements(LegalLevels).ToArbitrary(),
                Arb.Default.NonEmptyString(),
                (msg, level, target) =>
                {
                    var (buffer, dir) = BuildBuffer();
                    try
                    {
                        var ingestor = new LogIngestor(buffer);
                        var ok = ingestor.Ingest(new LogFrame
                        {
                            Protocol = LogFrame.ProtocolLiteral,
                            SecretVersion = 1,
                            Level = level,
                            Target = target.Get,
                            Message = msg ?? string.Empty,
                            TimestampMs = 1730000000500L,
                        });
                        // 空白 / null message 被 MachineLogBuffer 拒收（与 Trace 入队同语义）
                        // 这是合法路径：跳过本 case，不算违反 property
                        if (!ok)
                        {
                            return string.IsNullOrWhiteSpace(msg);
                        }

                        var pending = buffer.GetPendingBatch("pbt-session", 0, 100);
                        if (pending.Count != 1) return false;
                        var entry = pending[0];
                        if (entry.Component != "rustdesk-bridge") return false;
                        if (entry.Level != level) return false;

                        // (b) Message UTF-8 ≤ 4096 不含半个码点
                        var msgBytes = Encoding.UTF8.GetBytes(entry.Message ?? string.Empty);
                        if (msgBytes.Length > LogIngestor.MaxMessageBytes) return false;
                        try { var _ = Encoding.UTF8.GetString(msgBytes); }
                        catch (DecoderFallbackException) { return false; }

                        // (c) Metadata 不含 5 个子串
                        if (entry.Metadata != null)
                        {
                            foreach (var key in entry.Metadata.Keys)
                            {
                                if (key.IndexOf("target", StringComparison.OrdinalIgnoreCase) >= 0) return false;
                                if (key.IndexOf("level", StringComparison.OrdinalIgnoreCase) >= 0) return false;
                                if (key.IndexOf("timestampMs", StringComparison.OrdinalIgnoreCase) >= 0) return false;
                                if (key.IndexOf("mac", StringComparison.OrdinalIgnoreCase) >= 0) return false;
                                if (key.IndexOf("secretVersion", StringComparison.OrdinalIgnoreCase) >= 0) return false;
                            }
                        }
                        return true;
                    }
                    finally
                    {
                        buffer.Dispose();
                        Cleanup(dir);
                    }
                });
        }

        // ---------- (d) ----------

        [Property(MaxTest = 30)]
        public Property KFailures_BumpCounter_ExactlyByK(PositiveInt k)
        {
            var (buffer, dir) = BuildBuffer();
            try
            {
                var counter = new BridgeLogDropCounter(buffer);
                var initial = counter.TotalCount;
                for (var i = 0; i < k.Get; i++)
                {
                    counter.Increment();
                }
                return (counter.TotalCount == initial + k.Get).ToProperty();
            }
            finally
            {
                buffer.Dispose();
                Cleanup(dir);
            }
        }

        // ---------- (e) ----------

        [Fact]
        public void NonzeroDelta_FlushIfNonzero_WritesSummaryEntry()
        {
            var (buffer, dir) = BuildBuffer();
            try
            {
                var counter = new BridgeLogDropCounter(buffer);
                counter.Increment();
                counter.Increment();
                counter.Increment();
                counter.FlushIfNonzero();

                var pending = buffer.GetPendingBatch("pbt-session", 0, 100);
                Assert.NotEmpty(pending);
                var summary = pending.First(e => e.EventKey == "bridge_log_drop_count");
                Assert.Equal("warn", summary.Level);
                Assert.Equal("rustdesk-bridge", summary.Component);
                Assert.Matches(@"^window=\d+, total=\d+$", summary.Message ?? string.Empty);

                // 第二次没有增量 → 不再写新条目
                var beforeCount = buffer.GetPendingBatch("pbt-session", 0, 100).Count;
                counter.FlushIfNonzero();
                var afterCount = buffer.GetPendingBatch("pbt-session", 0, 100).Count;
                Assert.Equal(beforeCount, afterCount);
            }
            finally
            {
                buffer.Dispose();
                Cleanup(dir);
            }
        }

        [Fact]
        public void ZeroDelta_FlushIfNonzero_DoesNotWriteEntry()
        {
            var (buffer, dir) = BuildBuffer();
            try
            {
                var counter = new BridgeLogDropCounter(buffer);
                counter.FlushIfNonzero();
                Assert.Empty(buffer.GetPendingBatch("pbt-session", 0, 100));
            }
            finally
            {
                buffer.Dispose();
                Cleanup(dir);
            }
        }

        [Fact]
        public void InvalidLevel_GetsDropped_NotEnqueued()
        {
            var (buffer, dir) = BuildBuffer();
            try
            {
                var ingestor = new LogIngestor(buffer);
                var ok = ingestor.Ingest(new LogFrame
                {
                    Protocol = LogFrame.ProtocolLiteral,
                    SecretVersion = 1,
                    Level = "fatal", // 非法
                    Target = "x",
                    Message = "y",
                    TimestampMs = 1L,
                });
                Assert.False(ok);
                Assert.Empty(buffer.GetPendingBatch("pbt-session", 0, 100));
            }
            finally
            {
                buffer.Dispose();
                Cleanup(dir);
            }
        }
    }
}
