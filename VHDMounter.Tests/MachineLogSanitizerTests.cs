using System.Linq;
using Xunit;

namespace VHDMounter.Tests
{
    public sealed class MachineLogSanitizerTests
    {
        [Fact]
        public void SanitizeSensitiveText_RemovesInlinePassword()
        {
            var result = MachineLogSanitizer.SanitizeSensitiveText("user password: secret123");

            Assert.Equal("user password: ***", result);
        }

        [Fact]
        public void SanitizeSensitiveText_RemovesInlineSecret()
        {
            var result = MachineLogSanitizer.SanitizeSensitiveText("secret=abc123 token=bearer_xyz");

            Assert.Contains("secret=***", result);
            Assert.Contains("token=***", result);
        }

        [Fact]
        public void SanitizeSensitiveText_RemovesQuerySecrets()
        {
            var result = MachineLogSanitizer.SanitizeSensitiveText("https://api.com?password=secret&token=abc");

            Assert.Contains("password=***", result);
        }

        [Fact]
        public void SanitizeSensitiveText_RemovesJsonSecrets()
        {
            var result = MachineLogSanitizer.SanitizeSensitiveText("{\"password\":\"secret\",\"user\":\"admin\"}");

            Assert.Contains("\"password\":\"***\"", result);
            Assert.Contains("\"user\":\"admin\"", result);
        }

        [Fact]
        public void SanitizeSensitiveText_HandlesEmptyString()
        {
            var result = MachineLogSanitizer.SanitizeSensitiveText("");

            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void SanitizeSensitiveText_HandlesNull()
        {
            var result = MachineLogSanitizer.SanitizeSensitiveText(null);

            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void SanitizeSensitiveText_HandlesWhitespace()
        {
            var result = MachineLogSanitizer.SanitizeSensitiveText("   ");

            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void SanitizeSensitiveText_RemovesNullCharacters()
        {
            var result = MachineLogSanitizer.SanitizeSensitiveText("test\0value");

            Assert.Equal("testvalue", result);
        }

        [Fact]
        public void BuildEntry_ParsesPrefixAndMessage()
        {
            var entry = MachineLogSanitizer.BuildEntry("session-1", 1, "STATUS: VHD mounted successfully");

            Assert.NotNull(entry);
            Assert.Equal("session-1", entry.SessionId);
            Assert.Equal(1, entry.Seq);
            Assert.Equal("VHDManager", entry.Component);
            Assert.Equal("STATUS", entry.EventKey);
            Assert.Equal("VHD mounted successfully", entry.Message);
        }

        [Fact]
        public void BuildEntry_HandlesLifecycleMarker()
        {
            var entry = MachineLogSanitizer.BuildEntry("session-1", 2, "==== Boot Complete ====");

            Assert.NotNull(entry);
            Assert.Equal("LIFECYCLE_MARKER", entry.EventKey);
        }

        [Fact]
        public void BuildEntry_HandlesNoPrefix()
        {
            var entry = MachineLogSanitizer.BuildEntry("session-1", 3, "Plain log message");

            Assert.NotNull(entry);
            Assert.Equal("TRACE_LINE", entry.EventKey);
            Assert.Equal("Program", entry.Component);
            Assert.Equal("Plain log message", entry.Message);
        }

        [Fact]
        public void BuildEntry_ParsesMetadata()
        {
            var entry = MachineLogSanitizer.BuildEntry("session-1", 4, "STATUS: mount ok path=\"/dev/sda1\" size=1024");

            Assert.NotNull(entry);
            Assert.True(entry.Metadata.ContainsKey("path"));
            Assert.Equal("/dev/sda1", entry.Metadata["path"]);
            Assert.True(entry.Metadata.ContainsKey("size"));
            Assert.Equal("1024", entry.Metadata["size"]);
        }

        [Fact]
        public void BuildEntry_ReturnsNullForEmptyText()
        {
            var entry = MachineLogSanitizer.BuildEntry("session-1", 5, "   ");

            Assert.Null(entry);
        }

        [Fact]
        public void BuildEntry_SanitizesMetadataValues()
        {
            var entry = MachineLogSanitizer.BuildEntry("session-1", 6, "STATUS: login password=\"secret123\"");

            Assert.NotNull(entry);
            Assert.Equal("***", entry.Metadata["password"]);
        }

        [Fact]
        public void BuildEntry_InfersErrorLevel()
        {
            var entry = MachineLogSanitizer.BuildEntry("session-1", 7, "STATUS: Some error occurred");

            Assert.Equal("error", entry.Level);
        }

        [Fact]
        public void BuildEntry_InfersWarnLevel()
        {
            var entry = MachineLogSanitizer.BuildEntry("session-1", 8, "WARN: retrying connection");

            Assert.Equal("warn", entry.Level);
        }

        [Fact]
        public void BuildEntry_InfersDebugLevelForTraceLine()
        {
            var entry = MachineLogSanitizer.BuildEntry("session-1", 9, "CURRENT_DIRECTORY: /app/bin");

            Assert.Equal("debug", entry.Level);
        }

        [Fact]
        public void BuildEntry_DefaultsToInfoLevel()
        {
            var entry = MachineLogSanitizer.BuildEntry("session-1", 10, "STATUS: normal operation");

            Assert.Equal("info", entry.Level);
        }

        [Fact]
        public void NormalizeEventKey_ReplacesNonAlphanumericWithUnderscore()
        {
            var result = MachineLogSanitizer.NormalizeEventKey("mount-start.v2");

            Assert.Equal("MOUNT_START_V2", result);
        }

        [Fact]
        public void NormalizeEventKey_TrimsLeadingAndTrailingUnderscores()
        {
            var result = MachineLogSanitizer.NormalizeEventKey("---event---");

            Assert.Equal("EVENT", result);
        }

        [Fact]
        public void NormalizeEventKey_HandlesEmptyString()
        {
            var result = MachineLogSanitizer.NormalizeEventKey("");

            Assert.Equal("TRACE_LINE", result);
        }

        [Fact]
        public void NormalizeEventKey_HandlesNull()
        {
            var result = MachineLogSanitizer.NormalizeEventKey(null);

            Assert.Equal("TRACE_LINE", result);
        }

        [Fact]
        public void NormalizeEventKey_ConvertsToUpperCase()
        {
            var result = MachineLogSanitizer.NormalizeEventKey("lowercase_event");

            Assert.Equal("LOWERCASE_EVENT", result);
        }

        [Fact]
        public void BuildEntry_InfersMainWindowComponent()
        {
            var entry = MachineLogSanitizer.BuildEntry("session-1", 11, "MAINWINDOW: button clicked");

            Assert.Equal("MainWindow", entry.Component);
        }

        [Fact]
        public void BuildEntry_InfersProgramComponentForSelfUpdate()
        {
            var entry = MachineLogSanitizer.BuildEntry("session-1", 12, "SELF_UPDATE: checking version");

            Assert.Equal("Program", entry.Component);
        }

        [Fact]
        public void BuildEntry_RawTextMatchesSanitizedInput()
        {
            var entry = MachineLogSanitizer.BuildEntry("session-1", 13, "STATUS: test message");

            Assert.Equal("STATUS: test message", entry.RawText);
        }

        [Fact]
        public void BuildEntry_SetsOccurredAtTimestamp()
        {
            var entry = MachineLogSanitizer.BuildEntry("session-1", 14, "STATUS: test");

            Assert.NotNull(entry.OccurredAt);
            Assert.NotEmpty(entry.OccurredAt);
        }

        [Fact]
        public void BuildEntry_HandlesEvhdMountPrefix()
        {
            var entry = MachineLogSanitizer.BuildEntry("session-1", 15, "EVHD_MOUNT_START: beginning mount");

            Assert.Equal("VHDManager", entry.Component);
        }

        [Fact]
        public void BuildEntry_DuplicateMetadataKeysKeepFirst()
        {
            var entry = MachineLogSanitizer.BuildEntry("session-1", 16, "STATUS: key=1 key=2");

            Assert.Equal("1", entry.Metadata["key"]);
        }
    }
}
