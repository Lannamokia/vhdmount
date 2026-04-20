using System;
using System.Linq;
using Xunit;

namespace VHDMounter.Tests
{
    public sealed class NetworkIpv4EditorStateTests
    {
        [Fact]
        public void EditorAcceptsPlanSequenceForIpAddress19216811()
        {
            var editor = new NetworkIpv4EditorState(new NetworkIpv4Configuration(false, "0.0.0.0", "255.255.255.0", string.Empty, string.Empty, string.Empty));

            Assert.True(editor.TryEnterDigit(1, out _));
            Assert.True(editor.TryEnterDigit(9, out _));
            Assert.True(editor.TryEnterDigit(2, out _));
            editor.AdvanceSegment();

            Assert.True(editor.TryEnterDigit(1, out _));
            Assert.True(editor.TryEnterDigit(6, out _));
            Assert.True(editor.TryEnterDigit(8, out _));
            editor.AdvanceSegment();

            Assert.True(editor.TryEnterDigit(1, out _));
            editor.AdvanceSegment();
            Assert.True(editor.TryEnterDigit(1, out _));

            var ipSegments = editor.BuildFieldDisplays()[0].Segments.Select(segment => segment.Text).ToArray();
            Assert.Equal(new[] { "192", "168", "001", "001" }, ipSegments);
        }

        [Fact]
        public void EditorRejectsOctetsOver255()
        {
            var editor = new NetworkIpv4EditorState(new NetworkIpv4Configuration(false, "0.0.0.0", "255.255.255.0", string.Empty, string.Empty, string.Empty));

            Assert.True(editor.TryEnterDigit(2, out _));
            Assert.True(editor.TryEnterDigit(5, out _));
            Assert.False(editor.TryEnterDigit(6, out var error));

            Assert.Equal("当前输入段只能输入 0 到 255。", error);
            Assert.Equal("025", editor.BuildFieldDisplays()[0].Segments[0].Text);
        }

        [Fact]
        public void TimedPressSequenceTrackerRequiresThreeRapidPresses()
        {
            var tracker = new TimedPressSequenceTracker(TimeSpan.FromMilliseconds(800));
            var start = new DateTime(2026, 4, 20, 12, 0, 0, DateTimeKind.Utc);

            Assert.Equal(1, tracker.RegisterPress(start));
            Assert.Equal(2, tracker.RegisterPress(start.AddMilliseconds(400)));
            Assert.Equal(3, tracker.RegisterPress(start.AddMilliseconds(700)));

            tracker.Reset();

            Assert.Equal(1, tracker.RegisterPress(start.AddSeconds(3)));
            Assert.Equal(1, tracker.RegisterPress(start.AddSeconds(4)));
        }
    }
}