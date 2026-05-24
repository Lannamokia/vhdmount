using System.Collections.Generic;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using VHDMounter.RustDeskBridge.Pipe;
using Xunit;

namespace VHDMounter.Tests.RustDeskBridge.Unit
{
    /// <summary>
    /// 任务 19.3 SMOKE：用 RawSecurityDescriptor 解析
    /// <see cref="BridgePipeFactory.BuildSecurityDescriptorBinary"/> 返回的二进制 SD，
    /// 断言 ACE 集合恰好包含 SYSTEM (S-1-5-18) + Builtin\Administrators (S-1-5-32-544)
    /// 两条 AccessAllowed，且**不**包含其它常见敏感主体（Everyone / Users /
    /// Authenticated Users / ANONYMOUS LOGON / NETWORK）。
    ///
    /// Validates: Requirements 14.6, 14.7（DACL 仅授予 SYSTEM + Administrators）
    /// </summary>
    [Trait("feature", "rustdesk-bridge-host")]
    [Trait("kind", "SMOKE")]
    public sealed class BridgePipeFactoryDaclTests
    {
        private static readonly SecurityIdentifier SystemSid =
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        private static readonly SecurityIdentifier AdministratorsSid =
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);

        private static readonly (string Label, SecurityIdentifier Sid)[] ForbiddenPrincipals = new[]
        {
            ("Everyone (S-1-1-0)", new SecurityIdentifier(WellKnownSidType.WorldSid, null)),
            ("BUILTIN\\Users (S-1-5-32-545)", new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null)),
            ("Authenticated Users (S-1-5-11)", new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null)),
            ("ANONYMOUS LOGON (S-1-5-7)", new SecurityIdentifier(WellKnownSidType.AnonymousSid, null)),
            ("NETWORK (S-1-5-2)", new SecurityIdentifier(WellKnownSidType.NetworkSid, null)),
        };

        [Fact]
        public void SecurityDescriptor_AceSet_OnlyContainsSystemAndAdministrators()
        {
            var binary = BridgePipeFactory.BuildSecurityDescriptorBinary();
            Assert.NotNull(binary);
            Assert.NotEmpty(binary);

            var sd = new RawSecurityDescriptor(binary, 0);
            Assert.NotNull(sd.DiscretionaryAcl);

            var allowedAces = sd.DiscretionaryAcl
                .OfType<CommonAce>()
                .Where(ace => ace.AceType == AceType.AccessAllowed)
                .ToList();

            // 恰好两条 AccessAllowed
            Assert.Equal(2, allowedAces.Count);

            var sids = new HashSet<SecurityIdentifier>(allowedAces.Select(a => a.SecurityIdentifier));
            Assert.Contains(SystemSid, sids);
            Assert.Contains(AdministratorsSid, sids);
        }

        [Fact]
        public void SecurityDescriptor_DoesNotIncludeWildcardOrSensitivePrincipals()
        {
            var binary = BridgePipeFactory.BuildSecurityDescriptorBinary();
            var sd = new RawSecurityDescriptor(binary, 0);

            var allSids = sd.DiscretionaryAcl
                .OfType<CommonAce>()
                .Select(ace => ace.SecurityIdentifier)
                .ToHashSet();

            foreach (var (label, sid) in ForbiddenPrincipals)
            {
                Assert.False(allSids.Contains(sid),
                    $"DACL 不应包含 {label}，但实际出现");
            }
        }

        [Fact]
        public void SecurityDescriptor_HasDaclPresentFlag_AndIsSelfRelative()
        {
            var binary = BridgePipeFactory.BuildSecurityDescriptorBinary();
            var sd = new RawSecurityDescriptor(binary, 0);

            Assert.True((sd.ControlFlags & ControlFlags.SelfRelative) == ControlFlags.SelfRelative,
                "Bridge 管道安全描述符必须为 self-relative 形式");
            Assert.True((sd.ControlFlags & ControlFlags.DiscretionaryAclPresent) == ControlFlags.DiscretionaryAclPresent,
                "Bridge 管道安全描述符必须显式标记 DACL 存在（拒绝隐式默认 DACL）");
        }
    }
}
