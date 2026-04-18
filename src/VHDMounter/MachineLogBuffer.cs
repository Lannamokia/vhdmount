using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace VHDMounter
{
    internal sealed class MachineLogBuffer : IDisposable
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private readonly object syncRoot = new object();
        private readonly List<MachineLogEntry> entries = new List<MachineLogEntry>();
        private readonly Dictionary<string, long> nextSeqBySession =
            new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly SemaphoreSlim entriesSignal = new SemaphoreSlim(0, int.MaxValue);
        private readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        private readonly string spoolPath;
        private readonly long maxSpoolBytes;
        private readonly Action<string> diagnostics;
        private StreamWriter writer;
        private long spoolBytes;
        private bool disposed;

        public MachineLogBuffer(
            string spoolPath,
            string currentSessionId,
            long maxSpoolBytes,
            Action<string> diagnostics = null)
        {
            this.spoolPath = spoolPath;
            this.maxSpoolBytes = maxSpoolBytes;
            this.diagnostics = diagnostics;
            CurrentSessionId = currentSessionId;

            lock (syncRoot)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(spoolPath) ?? AppDomain.CurrentDomain.BaseDirectory);
                LoadExistingEntriesLocked();
                if (!nextSeqBySession.ContainsKey(CurrentSessionId))
                {
                    nextSeqBySession[CurrentSessionId] = 1;
                }
                OpenWriterLocked();
            }
        }

        public string CurrentSessionId { get; }

        public bool TryAppendTraceLine(string traceText)
        {
            ThrowIfDisposed();

            lock (syncRoot)
            {
                var seq = GetNextSeqLocked(CurrentSessionId);
                var entry = MachineLogSanitizer.BuildEntry(CurrentSessionId, seq, traceText);
                if (entry == null)
                {
                    return false;
                }

                PersistEntryLocked(entry);
                TrimToBudgetLocked();
            }

            ReleaseEntriesSignal();
            return true;
        }

        public IReadOnlyList<string> GetPendingSessionIds()
        {
            ThrowIfDisposed();

            lock (syncRoot)
            {
                var ordered = new List<string>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in entries)
                {
                    if (seen.Add(entry.SessionId))
                    {
                        ordered.Add(entry.SessionId);
                    }
                }

                return ordered;
            }
        }

        public IReadOnlyList<MachineLogEntry> GetPendingBatch(string sessionId, long acknowledgedSeq, int batchSize)
        {
            ThrowIfDisposed();

            lock (syncRoot)
            {
                return entries
                    .Where((entry) => entry.SessionId == sessionId && entry.Seq > acknowledgedSeq)
                    .OrderBy((entry) => entry.Seq)
                    .Take(batchSize)
                    .Select(CloneEntry)
                    .ToList();
            }
        }

        public bool HasPendingEntries(string sessionId, long acknowledgedSeq)
        {
            ThrowIfDisposed();

            lock (syncRoot)
            {
                return entries.Any((entry) => entry.SessionId == sessionId && entry.Seq > acknowledgedSeq);
            }
        }

        public void Acknowledge(string sessionId, long acknowledgedSeq)
        {
            if (acknowledgedSeq <= 0)
            {
                return;
            }

            ThrowIfDisposed();

            lock (syncRoot)
            {
                var removed = entries.RemoveAll((entry) => entry.SessionId == sessionId && entry.Seq <= acknowledgedSeq);
                if (removed <= 0)
                {
                    return;
                }

                RecalculateSpoolBytesLocked();
                RewriteSpoolFileLocked();
            }
        }

        public async Task<bool> WaitForNewEntriesAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            try
            {
                return await entriesSignal.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            lock (syncRoot)
            {
                disposed = true;
                writer?.Flush();
                writer?.Dispose();
                writer = null;
            }

            entriesSignal.Dispose();
        }

        private static MachineLogEntry CloneEntry(MachineLogEntry entry)
        {
            return new MachineLogEntry
            {
                SessionId = entry.SessionId,
                Seq = entry.Seq,
                OccurredAt = entry.OccurredAt,
                Level = entry.Level,
                Component = entry.Component,
                EventKey = entry.EventKey,
                Message = entry.Message,
                RawText = entry.RawText,
                Metadata = new Dictionary<string, string>(entry.Metadata, StringComparer.OrdinalIgnoreCase),
                SerializedByteCount = entry.SerializedByteCount,
            };
        }

        private long GetNextSeqLocked(string sessionId)
        {
            if (!nextSeqBySession.TryGetValue(sessionId, out var currentSeq))
            {
                currentSeq = 1;
            }

            nextSeqBySession[sessionId] = currentSeq + 1;
            return currentSeq;
        }

        private void LoadExistingEntriesLocked()
        {
            if (!File.Exists(spoolPath))
            {
                return;
            }

            foreach (var line in File.ReadLines(spoolPath, Utf8NoBom))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    var entry = JsonSerializer.Deserialize<MachineLogEntry>(line, jsonOptions);
                    if (entry == null || string.IsNullOrWhiteSpace(entry.SessionId) || entry.Seq <= 0)
                    {
                        continue;
                    }

                    entry.SerializedByteCount = GetSerializedByteCount(line);
                    entries.Add(entry);
                    if (!nextSeqBySession.TryGetValue(entry.SessionId, out var nextSeq) || entry.Seq >= nextSeq)
                    {
                        nextSeqBySession[entry.SessionId] = entry.Seq + 1;
                    }
                    spoolBytes += entry.SerializedByteCount;
                }
                catch (Exception ex)
                {
                    diagnostics?.Invoke($"读取机台日志 spool 失败，已跳过损坏行: {ex.Message}");
                }
            }
        }

        private void PersistEntryLocked(MachineLogEntry entry)
        {
            OpenWriterLocked();

            var line = JsonSerializer.Serialize(entry, jsonOptions);
            entry.SerializedByteCount = GetSerializedByteCount(line);
            entries.Add(entry);
            spoolBytes += entry.SerializedByteCount;

            writer.WriteLine(line);
            writer.Flush();
        }

        private void TrimToBudgetLocked()
        {
            if (spoolBytes <= maxSpoolBytes)
            {
                return;
            }

            var removed = false;
            removed |= RemoveEntriesLocked((entry) => !IsPriorityLevel(entry.Level));
            if (spoolBytes > maxSpoolBytes)
            {
                removed |= RemoveEntriesLocked((_) => true);
            }

            if (removed)
            {
                RewriteSpoolFileLocked();
            }
        }

        private bool RemoveEntriesLocked(Func<MachineLogEntry, bool> predicate)
        {
            var removed = false;
            for (var index = 0; index < entries.Count && spoolBytes > maxSpoolBytes;)
            {
                if (!predicate(entries[index]))
                {
                    index += 1;
                    continue;
                }

                spoolBytes -= entries[index].SerializedByteCount;
                entries.RemoveAt(index);
                removed = true;
            }

            return removed;
        }

        private void RewriteSpoolFileLocked()
        {
            CloseWriterLocked();

            var tempPath = spoolPath + ".tmp";
            using (var tempWriter = new StreamWriter(
                new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.Read),
                Utf8NoBom))
            {
                foreach (var entry in entries)
                {
                    var line = JsonSerializer.Serialize(entry, jsonOptions);
                    entry.SerializedByteCount = GetSerializedByteCount(line);
                    tempWriter.WriteLine(line);
                }
            }

            File.Move(tempPath, spoolPath, true);
            RecalculateSpoolBytesLocked();
            OpenWriterLocked();
        }

        private void RecalculateSpoolBytesLocked()
        {
            spoolBytes = entries.Sum((entry) => entry.SerializedByteCount);
        }

        private void OpenWriterLocked()
        {
            if (writer != null)
            {
                return;
            }

            writer = new StreamWriter(
                new FileStream(spoolPath, FileMode.Append, FileAccess.Write, FileShare.Read),
                Utf8NoBom);
            writer.AutoFlush = true;
        }

        private void CloseWriterLocked()
        {
            writer?.Flush();
            writer?.Dispose();
            writer = null;
        }

        private void ReleaseEntriesSignal()
        {
            try
            {
                entriesSignal.Release();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SemaphoreFullException)
            {
            }
        }

        private static int GetSerializedByteCount(string line)
        {
            return Utf8NoBom.GetByteCount(line) + Utf8NoBom.GetByteCount(Environment.NewLine);
        }

        private static bool IsPriorityLevel(string level)
        {
            return string.Equals(level, "warn", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(level, "error", StringComparison.OrdinalIgnoreCase);
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(MachineLogBuffer));
            }
        }
    }
}