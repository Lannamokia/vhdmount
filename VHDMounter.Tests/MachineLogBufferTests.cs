using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace VHDMounter.Tests
{
    public sealed class MachineLogBufferTests : IDisposable
    {
        private readonly string tempDir;
        private readonly string spoolPath;

        public MachineLogBufferTests()
        {
            tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            spoolPath = Path.Combine(tempDir, "test-spool.jsonl");
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
            catch { }
        }

        [Fact]
        public void TryAppendTraceLine_AddsEntry()
        {
            using var buffer = new MachineLogBuffer(spoolPath, "session-1", 1024 * 1024);

            var result = buffer.TryAppendTraceLine("STATUS: test message");

            Assert.True(result);
        }

        [Fact]
        public void TryAppendTraceLine_ReturnsFalseForEmptyText()
        {
            using var buffer = new MachineLogBuffer(spoolPath, "session-1", 1024 * 1024);

            var result = buffer.TryAppendTraceLine("   ");

            Assert.False(result);
        }

        [Fact]
        public void GetPendingBatch_ReturnsEntries()
        {
            using var buffer = new MachineLogBuffer(spoolPath, "session-1", 1024 * 1024);
            buffer.TryAppendTraceLine("STATUS: message 1");
            buffer.TryAppendTraceLine("STATUS: message 2");

            var batch = buffer.GetPendingBatch("session-1", 0, 10);

            Assert.Equal(2, batch.Count);
            Assert.Equal(1, batch[0].Seq);
            Assert.Equal(2, batch[1].Seq);
        }

        [Fact]
        public void GetPendingBatch_RespectsAcknowledgedSeq()
        {
            using var buffer = new MachineLogBuffer(spoolPath, "session-1", 1024 * 1024);
            buffer.TryAppendTraceLine("STATUS: message 1");
            buffer.TryAppendTraceLine("STATUS: message 2");
            buffer.TryAppendTraceLine("STATUS: message 3");

            var batch = buffer.GetPendingBatch("session-1", 1, 10);

            Assert.Equal(2, batch.Count);
            Assert.Equal(2, batch[0].Seq);
        }

        [Fact]
        public void GetPendingBatch_RespectsBatchSize()
        {
            using var buffer = new MachineLogBuffer(spoolPath, "session-1", 1024 * 1024);
            buffer.TryAppendTraceLine("STATUS: message 1");
            buffer.TryAppendTraceLine("STATUS: message 2");
            buffer.TryAppendTraceLine("STATUS: message 3");

            var batch = buffer.GetPendingBatch("session-1", 0, 2);

            Assert.Equal(2, batch.Count);
        }

        [Fact]
        public void HasPendingEntries_TrueWhenEntriesExist()
        {
            using var buffer = new MachineLogBuffer(spoolPath, "session-1", 1024 * 1024);
            buffer.TryAppendTraceLine("STATUS: message 1");

            Assert.True(buffer.HasPendingEntries("session-1", 0));
        }

        [Fact]
        public void HasPendingEntries_FalseWhenAllAcknowledged()
        {
            using var buffer = new MachineLogBuffer(spoolPath, "session-1", 1024 * 1024);
            buffer.TryAppendTraceLine("STATUS: message 1");
            buffer.Acknowledge("session-1", 1);

            Assert.False(buffer.HasPendingEntries("session-1", 1));
        }

        [Fact]
        public void Acknowledge_RemovesEntries()
        {
            using var buffer = new MachineLogBuffer(spoolPath, "session-1", 1024 * 1024);
            buffer.TryAppendTraceLine("STATUS: message 1");
            buffer.TryAppendTraceLine("STATUS: message 2");

            buffer.Acknowledge("session-1", 1);

            var batch = buffer.GetPendingBatch("session-1", 0, 10);
            Assert.Single(batch);
            Assert.Equal(2, batch[0].Seq);
        }

        [Fact]
        public void Acknowledge_NoOpForZeroSeq()
        {
            using var buffer = new MachineLogBuffer(spoolPath, "session-1", 1024 * 1024);
            buffer.TryAppendTraceLine("STATUS: message 1");

            buffer.Acknowledge("session-1", 0);

            Assert.True(buffer.HasPendingEntries("session-1", 0));
        }

        [Fact]
        public void GetPendingSessionIds_ReturnsOrderedSessionIds()
        {
            using var buffer = new MachineLogBuffer(spoolPath, "session-1", 1024 * 1024);
            buffer.TryAppendTraceLine("STATUS: message 1");
            buffer.TryAppendTraceLine("STATUS: message 2");

            var sessionIds = buffer.GetPendingSessionIds();

            Assert.Single(sessionIds);
            Assert.Equal("session-1", sessionIds[0]);
        }

        [Fact]
        public void MultipleSessions_TrackedIndependently()
        {
            var spoolPath2 = Path.Combine(tempDir, "test-spool-2.jsonl");
            using var buffer = new MachineLogBuffer(spoolPath, "session-1", 1024 * 1024);
            buffer.TryAppendTraceLine("STATUS: session 1 msg");

            using var buffer2 = new MachineLogBuffer(spoolPath2, "session-2", 1024 * 1024);
            buffer2.TryAppendTraceLine("STATUS: session 2 msg");

            var batch1 = buffer.GetPendingBatch("session-1", 0, 10);
            Assert.Single(batch1);

            var batch2 = buffer2.GetPendingBatch("session-2", 0, 10);
            Assert.Single(batch2);
        }

        [Fact]
        public async Task WaitForNewEntriesAsync_ReturnsTrueWhenEntriesAdded()
        {
            using var buffer = new MachineLogBuffer(spoolPath, "session-1", 1024 * 1024);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            var task = buffer.WaitForNewEntriesAsync(TimeSpan.FromSeconds(5), cts.Token);
            buffer.TryAppendTraceLine("STATUS: async test");

            var result = await task;

            Assert.True(result);
        }

        [Fact]
        public async Task WaitForNewEntriesAsync_ReturnsFalseOnTimeout()
        {
            using var buffer = new MachineLogBuffer(spoolPath, "session-1", 1024 * 1024);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

            var result = await buffer.WaitForNewEntriesAsync(TimeSpan.FromMilliseconds(100), cts.Token);

            Assert.False(result);
        }

        [Fact]
        public void PersistsEntriesToSpoolFile()
        {
            using (var buffer = new MachineLogBuffer(spoolPath, "session-1", 1024 * 1024))
            {
                buffer.TryAppendTraceLine("STATUS: persistent message");
            }

            Assert.True(File.Exists(spoolPath));
            var content = File.ReadAllText(spoolPath);
            Assert.Contains("STATUS: persistent message", content);
        }

        [Fact]
        public void LoadsExistingEntriesOnCreation()
        {
            using (var buffer = new MachineLogBuffer(spoolPath, "session-1", 1024 * 1024))
            {
                buffer.TryAppendTraceLine("STATUS: pre-existing");
            }

            using var newBuffer = new MachineLogBuffer(spoolPath, "session-1", 1024 * 1024);
            var batch = newBuffer.GetPendingBatch("session-1", 0, 10);

            Assert.True(batch.Count >= 1);
            Assert.Contains(batch, e => e.RawText.Contains("pre-existing"));
        }

        [Fact]
        public void BudgetTrimming_RemovesNonPriorityEntriesFirst()
        {
            var maxBytes = 512L;
            using var buffer = new MachineLogBuffer(spoolPath, "session-1", maxBytes);

            for (var i = 0; i < 50; i++)
            {
                buffer.TryAppendTraceLine($"STATUS: message {i:D3}");
            }

            var batch = buffer.GetPendingBatch("session-1", 0, 1000);
            Assert.True(batch.Count < 50);
        }

        [Fact]
        public void BudgetTrimming_PreservesPriorityLevels()
        {
            var maxBytes = 512L;
            using var buffer = new MachineLogBuffer(spoolPath, "session-1", maxBytes);

            buffer.TryAppendTraceLine("WARN: warning message");
            buffer.TryAppendTraceLine("ERROR: error message");

            for (var i = 0; i < 50; i++)
            {
                buffer.TryAppendTraceLine($"STATUS: info message {i:D3}");
            }

            var batch = buffer.GetPendingBatch("session-1", 0, 1000);
            Assert.Contains(batch, e => e.Level == "warn");
            Assert.Contains(batch, e => e.Level == "error");
        }

        [Fact]
        public void Dispose_ThrowsObjectDisposedExceptionOnSubsequentAccess()
        {
            var buffer = new MachineLogBuffer(spoolPath, "session-1", 1024 * 1024);
            buffer.Dispose();

            Assert.Throws<ObjectDisposedException>(() => buffer.TryAppendTraceLine("test"));
            Assert.Throws<ObjectDisposedException>(() => buffer.GetPendingBatch("session-1", 0, 10));
            Assert.Throws<ObjectDisposedException>(() => buffer.HasPendingEntries("session-1", 0));
            Assert.Throws<ObjectDisposedException>(() => buffer.Acknowledge("session-1", 1));
        }

        [Fact]
        public void Diagnostics_CallbackReceivesMessages()
        {
            var diagnostics = new System.Collections.Generic.List<string>();
            File.WriteAllText(spoolPath, "not valid json\n");

            using var buffer = new MachineLogBuffer(spoolPath, "session-1", 1024 * 1024, diagnostics.Add);

            Assert.NotEmpty(diagnostics);
        }

        [Fact]
        public void SeqNumbersIncrementPerSession()
        {
            using var buffer = new MachineLogBuffer(spoolPath, "session-1", 1024 * 1024);
            buffer.TryAppendTraceLine("STATUS: msg 1");
            buffer.TryAppendTraceLine("STATUS: msg 2");
            buffer.TryAppendTraceLine("STATUS: msg 3");

            var batch = buffer.GetPendingBatch("session-1", 0, 10);

            Assert.Equal(1, batch[0].Seq);
            Assert.Equal(2, batch[1].Seq);
            Assert.Equal(3, batch[2].Seq);
        }

        [Fact]
        public void SeqNumbersContinueAfterReload()
        {
            using (var buffer = new MachineLogBuffer(spoolPath, "session-1", 1024 * 1024))
            {
                buffer.TryAppendTraceLine("STATUS: msg 1");
                buffer.TryAppendTraceLine("STATUS: msg 2");
            }

            using var newBuffer = new MachineLogBuffer(spoolPath, "session-1", 1024 * 1024);
            newBuffer.TryAppendTraceLine("STATUS: msg 3");

            var batch = newBuffer.GetPendingBatch("session-1", 0, 10);
            var msg3 = batch.Last();

            Assert.Equal(3, msg3.Seq);
        }

        [Fact]
        public void CloneEntry_CreatesIndependentCopy()
        {
            using var buffer = new MachineLogBuffer(spoolPath, "session-1", 1024 * 1024);
            buffer.TryAppendTraceLine("STATUS: msg with meta key=\"value\"");

            var batch = buffer.GetPendingBatch("session-1", 0, 10);
            var original = batch[0];
            original.Metadata["key"] = "modified";

            var batch2 = buffer.GetPendingBatch("session-1", 0, 10);

            Assert.Equal("value", batch2[0].Metadata["key"]);
        }
    }
}
