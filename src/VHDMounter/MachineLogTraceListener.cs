using System.Diagnostics;
using System.Text;

namespace VHDMounter
{
    internal sealed class MachineLogTraceListener : TraceListener
    {
        private readonly object syncRoot = new object();
        private readonly StringBuilder pending = new StringBuilder();
        private readonly MachineLogBuffer buffer;

        public MachineLogTraceListener(MachineLogBuffer buffer)
        {
            this.buffer = buffer;
        }

        public override void Write(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            lock (syncRoot)
            {
                pending.Append(message);
                DrainPendingLines(flushTail: false);
            }
        }

        public override void WriteLine(string message)
        {
            lock (syncRoot)
            {
                if (!string.IsNullOrEmpty(message))
                {
                    pending.Append(message);
                }

                DrainPendingLines(flushTail: true);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (syncRoot)
                {
                    DrainPendingLines(flushTail: true);
                }
            }

            base.Dispose(disposing);
        }

        private void DrainPendingLines(bool flushTail)
        {
            while (true)
            {
                var text = pending.ToString();
                var newlineIndex = text.IndexOfAny(new[] { '\r', '\n' });
                if (newlineIndex < 0)
                {
                    break;
                }

                var line = text.Substring(0, newlineIndex);
                pending.Remove(0, newlineIndex + ConsumeNewlineLength(text, newlineIndex));
                buffer.TryAppendTraceLine(line);
            }

            if (!flushTail || pending.Length == 0)
            {
                return;
            }

            var tail = pending.ToString();
            pending.Clear();
            buffer.TryAppendTraceLine(tail);
        }

        private static int ConsumeNewlineLength(string text, int newlineIndex)
        {
            if (newlineIndex + 1 >= text.Length)
            {
                return 1;
            }

            var current = text[newlineIndex];
            var next = text[newlineIndex + 1];
            return (current == '\r' && next == '\n') || (current == '\n' && next == '\r') ? 2 : 1;
        }
    }
}