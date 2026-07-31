using System;
using VHDMounter.RustDeskBridge.Frames;
using VHDMounter.RustDeskBridge.Upload;
using Xunit;

namespace VHDMounter.Tests.RustDeskBridge.Properties
{
    [Trait("feature", "rustdesk-bridge-host")]
    [Trait("property", "Property 6: Report 缓存与 ack 独立性")]
    public sealed class LastReportedSnapshotTests
    {
        private static ReportFrame BuildFrame(
            string id = "123456789",
            string kind = ReportFrame.PasswordKindTemporary,
            string password = "Hunter2!",
            string reason = ReportFrame.ReasonStartup,
            long reportedAt = 1730000000000L,
            uint version = 1)
        {
            return new ReportFrame
            {
                Protocol = ReportFrame.ProtocolLiteral,
                SecretVersion = version,
                RustDeskId = id,
                PasswordKind = kind,
                Password = password,
                Reason = reason,
                ReportedAt = reportedAt,
                Nonce = "deadbeefdeadbeefdeadbeefdeadbeef",
                Mac = "ignored-by-snapshot",
            };
        }

        [Fact]
        public void FirstFrame_TriggersUpload()
        {
            var snap = new LastReportedSnapshot();
            var changed = snap.TryReplace(BuildFrame(), out var requiresUpload, out var pwdSnapshot);
            Assert.True(changed);
            Assert.True(requiresUpload);
            Assert.NotNull(pwdSnapshot);
            Assert.True(snap.HasValue);
        }

        [Fact]
        public void SameTriplet_HeartbeatReason_OnlyRefreshesReportedAt()
        {
            var snap = new LastReportedSnapshot();
            snap.TryReplace(BuildFrame(reportedAt: 1000L), out _, out _);
            var changed = snap.TryReplace(BuildFrame(reason: ReportFrame.ReasonHeartbeat, reportedAt: 2000L),
                out var requiresUpload, out var pwdSnapshot);
            Assert.False(changed);
            Assert.False(requiresUpload);
            Assert.Null(pwdSnapshot);
            Assert.Equal(2000L, snap.ReportedAt);
        }

        [Fact]
        public void SameTriplet_NonHeartbeatReason_TriggersUpload()
        {
            var snap = new LastReportedSnapshot();
            snap.TryReplace(BuildFrame(), out _, out _);
            var changed = snap.TryReplace(BuildFrame(reason: ReportFrame.ReasonRotation),
                out var requiresUpload, out var pwdSnapshot);
            Assert.True(changed);
            Assert.True(requiresUpload);
            Assert.NotNull(pwdSnapshot);
        }

        [Fact]
        public void PasswordChange_TriggersUpload_EvenOnHeartbeat()
        {
            var snap = new LastReportedSnapshot();
            snap.TryReplace(BuildFrame(password: "old"), out _, out _);
            var changed = snap.TryReplace(BuildFrame(password: "new", reason: ReportFrame.ReasonHeartbeat),
                out var requiresUpload, out _);
            Assert.True(changed);
            Assert.True(requiresUpload);
        }

        [Fact]
        public void IdChange_TriggersUpload()
        {
            var snap = new LastReportedSnapshot();
            snap.TryReplace(BuildFrame(id: "id1"), out _, out _);
            var changed = snap.TryReplace(BuildFrame(id: "id2", reason: ReportFrame.ReasonHeartbeat),
                out var requiresUpload, out _);
            Assert.True(changed);
            Assert.True(requiresUpload);
        }

        [Fact]
        public void Clear_ResetsState()
        {
            var snap = new LastReportedSnapshot();
            snap.TryReplace(BuildFrame(), out _, out _);
            snap.Clear();
            Assert.False(snap.HasValue);
            Assert.Equal(0L, snap.ReportedAt);
        }

        [Fact]
        public void PasswordRedaction_ShortHashIsEightHexCharsForEmpty()
        {
            // sha256("") 前 4 字节 hex 是 e3b0c442
            Assert.Equal("e3b0c442", PasswordRedaction.ShortHash(string.Empty));
        }

        [Fact]
        public void PasswordRedaction_ShortHashIsEightHexCharsForNonEmpty()
        {
            var hash = PasswordRedaction.ShortHash("Hunter2!");
            Assert.Equal(8, hash.Length);
            // 协议文档 §6.4 给出的 sha256("Hunter2!") = 60726568...
            Assert.StartsWith("6072", hash);
        }
    }
}
