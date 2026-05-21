using System;
using System.Collections.Generic;
using System.IO;
using VHDMounter.SoftwareDeploy;
using Xunit;

namespace VHDMounter.Tests
{
    /// <summary>
    /// 缺陷条件探索测试 — 部署安全漏洞利用（.NET 机端）
    ///
    /// **Validates: Requirements 1.2, 1.3, 1.4, 1.5**
    ///
    /// 此测试编码了期望行为（修复后应通过）。
    /// 在未修复代码上运行时，测试应 FAIL —— 失败即确认缺陷存在。
    ///
    /// 攻击面覆盖：
    /// - A: 路径边界（UninstallFiles 接受 targetPath 之外的路径）
    /// - A: 白名单绕过（IsValidTargetPath 接受 UNC/扩展路径/含 .. 段的路径）
    /// - B: 命令注入（UninstallSoftware 接受含特殊字符的脚本名）
    /// </summary>
    public sealed class DeploySecurityBugExplorationTests : IDisposable
    {
        private readonly string _tempDir;

        public DeploySecurityBugExplorationTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "deploy-security-bug-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
            catch { }
        }

        // ========== 测试 A - 路径边界：UninstallFiles 应拒绝 targetPath 之外的路径 ==========

        [Fact]
        public void TestA_PathBoundary_UninstallFiles_ShouldRejectPathsOutsideTargetPath()
        {
            // **Validates: Requirements 1.2**
            // 期望行为：UninstallFiles 对每条路径验证 StartsWith(record.targetPath)，越界路径应被跳过
            // 当前缺陷：UninstallFiles 直接 File.Delete 无边界检查

            // 创建一个 "受害者" 文件在 targetPath 之外
            var victimDir = Path.Combine(_tempDir, "victim");
            Directory.CreateDirectory(victimDir);
            var victimFile = Path.Combine(victimDir, "important.dll");
            File.WriteAllText(victimFile, "important system file");

            // targetPath 是 C:\SOFT\pkg-abc\ 模拟
            var targetDir = Path.Combine(_tempDir, "pkg-abc");
            Directory.CreateDirectory(targetDir);

            var record = new DeployRecord
            {
                targetPath = targetDir,
                fileManifest = new List<string>
                {
                    victimFile, // 这个路径在 targetPath 之外！
                },
            };

            var result = DeployUninstaller.UninstallFiles(targetDir, record);

            // 期望：越界路径的文件不应被删除
            Assert.True(File.Exists(victimFile),
                $"越界路径 {victimFile} 不应被删除，但文件已不存在 — 证明路径边界检查缺失");
        }

        [Fact]
        public void TestA_PathBoundary_UninstallFiles_ShouldRejectWindowsSystemPath()
        {
            // **Validates: Requirements 1.2**
            // 模拟 fileManifest 被篡改为系统路径的场景

            var targetDir = Path.Combine(_tempDir, "pkg-target");
            Directory.CreateDirectory(targetDir);

            // 创建一个模拟的 "系统文件" 在完全不同的目录
            var outsideDir = Path.Combine(_tempDir, "Windows", "system32");
            Directory.CreateDirectory(outsideDir);
            var outsideFile = Path.Combine(outsideDir, "file.dll");
            File.WriteAllText(outsideFile, "fake system file");

            var record = new DeployRecord
            {
                targetPath = targetDir,
                fileManifest = new List<string>
                {
                    outsideFile, // 完全在 targetPath 之外
                },
            };

            var result = DeployUninstaller.UninstallFiles(targetDir, record);

            // 期望：targetPath 之外的文件不应被删除
            Assert.True(File.Exists(outsideFile),
                $"targetPath 之外的文件 {outsideFile} 不应被删除 — 证明路径边界检查缺失");
        }

        // ========== 测试 A - 白名单绕过：IsValidTargetPath 应拒绝危险路径 ==========

        [Theory]
        [InlineData(@"\\?\C:\Windows")]       // 扩展路径前缀绕过
        [InlineData(@"\\evil\share")]          // UNC 路径
        [InlineData(@"\\server\c$\Windows")]   // UNC 管理共享
        public void TestA_WhitelistBypass_IsValidTargetPath_ShouldRejectUncAndExtendedPaths(string dangerousPath)
        {
            // **Validates: Requirements 1.3, 1.4**
            // 期望行为：以 \\ 开头的路径（UNC/扩展路径）应被立即拒绝
            // 当前缺陷：IsValidTargetPath 仅检查 Contains("..") 和系统路径黑名单，
            //           不拒绝 UNC 路径和 \\?\ 扩展路径

            var result = DeploySecurityPolicy.IsValidTargetPath(dangerousPath);

            Assert.False(result,
                $"危险路径 \"{dangerousPath}\" 应被 IsValidTargetPath 拒绝，但返回了 true — 证明白名单绕过存在");
        }

        [Fact]
        public void TestA_WhitelistBypass_IsValidTargetPath_ShouldRejectDotDotSegmentAfterNormalization()
        {
            // **Validates: Requirements 1.3**
            // 期望行为：经 Path.GetFullPath 规范化后仅允许白名单前缀
            // 当前缺陷：Contains("..") 检查可被绕过（如使用不含 .. 但解析后指向系统目录的路径）
            //           或者 C:\SOFT\..\Windows\ 虽含 .. 但当前实现是黑名单而非白名单

            // 这个路径含 .. 会被当前实现拒绝，但我们测试白名单模式：
            // 即使不含 .. 但解析后不在白名单内的路径也应被拒绝
            var pathsOutsideWhitelist = new[]
            {
                @"C:\Games\SomeApp",       // 不在 C:\SOFT\ 白名单内
                @"D:\Anything",            // 不在白名单前缀内
                @"C:\",                    // 驱动器根目录
            };

            foreach (var testPath in pathsOutsideWhitelist)
            {
                var result = DeploySecurityPolicy.IsValidTargetPath(testPath);

                // 期望：修复后仅允许 C:\SOFT\ 和 %ProgramData%\VHDMounter\ 前缀
                Assert.False(result,
                    $"路径 \"{testPath}\" 不在白名单前缀内，应被拒绝，但返回了 true — 证明白名单未实施");
            }
        }

        [Fact]
        public void TestA_WhitelistBypass_IsValidTargetPath_ShouldOnlyAllowWhitelistedPrefixes()
        {
            // **Validates: Requirements 1.3, 1.4**
            // 期望行为：仅 C:\SOFT\ 和 %ProgramData%\VHDMounter\ 前缀被接受
            // 当前缺陷：当前实现是黑名单模式（排除系统路径），不是白名单模式

            // 白名单内的路径应被接受
            Assert.True(DeploySecurityPolicy.IsValidTargetPath(@"C:\SOFT\pkg-abc"),
                @"C:\SOFT\pkg-abc 应在白名单内被接受");

            // 白名单外但非系统路径 — 当前黑名单模式会接受，但白名单模式应拒绝
            var outsideWhitelist = @"C:\Games\SomeApp";
            var result = DeploySecurityPolicy.IsValidTargetPath(outsideWhitelist);

            Assert.False(result,
                $"路径 \"{outsideWhitelist}\" 不在白名单前缀 (C:\\SOFT\\) 内，应被拒绝 — 证明当前是黑名单而非白名单模式");
        }

        // ========== 测试 B - 命令注入：UninstallSoftware 应拒绝含特殊字符的脚本名 ==========

        [Fact]
        public void TestB_CommandInjection_UninstallSoftware_ShouldRejectMaliciousScriptName()
        {
            // **Validates: Requirements 1.5**
            // 期望行为：uninstallScript 应通过 IsValidScriptName 白名单校验，
            //           含特殊字符的脚本名应被拒绝
            // 当前缺陷：UninstallSoftware 不调用 IsValidScriptName，
            //           直接将 uninstallScript 拼接到 PowerShell Arguments 字符串中

            var targetDir = Path.Combine(_tempDir, "pkg-inject");
            Directory.CreateDirectory(targetDir);

            // 创建一个 "恶意" 脚本名 — 包含引号闭合和命令注入
            var maliciousScriptName = "\"; Remove-Item -Recurse C:\\ -Force;\"";

            var record = new DeployRecord
            {
                targetPath = targetDir,
                uninstallScript = maliciousScriptName,
                requiresAdmin = false,
            };

            var result = DeployUninstaller.UninstallSoftware(record);

            // 期望：含特殊字符的脚本名应被拒绝，返回错误而非尝试执行
            Assert.False(result.Success,
                "含命令注入字符的脚本名不应被执行");
            Assert.True(result.ErrorMessage.Contains("不合法"),
                $"应返回脚本名不合法的错误信息，实际: {result.ErrorMessage} — 证明未调用 IsValidScriptName 校验");
        }

        [Theory]
        [InlineData("malware.exe")]
        [InlineData("script.bat")]
        [InlineData("..\\..\\evil.ps1")]
        [InlineData("install.cmd")]
        public void TestB_CommandInjection_UninstallSoftware_ShouldRejectNonWhitelistedScriptNames(string scriptName)
        {
            // **Validates: Requirements 1.5**
            // 期望行为：仅 install.ps1 和 uninstall.ps1 在白名单内
            // 当前缺陷：UninstallSoftware 不检查脚本名白名单

            var targetDir = Path.Combine(_tempDir, "pkg-script-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(targetDir);

            // 创建脚本文件使其存在（绕过文件存在性检查）
            var scriptPath = Path.Combine(targetDir, scriptName);
            try
            {
                var dir = Path.GetDirectoryName(scriptPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(scriptPath, "Write-Host 'test'");
            }
            catch
            {
                // 某些脚本名可能包含非法路径字符，这本身就应该被拒绝
            }

            var record = new DeployRecord
            {
                targetPath = targetDir,
                uninstallScript = scriptName,
                requiresAdmin = false,
            };

            var result = DeployUninstaller.UninstallSoftware(record);

            // 期望：非白名单脚本名应被拒绝
            Assert.False(result.Success, $"非白名单脚本名 \"{scriptName}\" 不应被执行");
            Assert.True(result.ErrorMessage.Contains("不合法"),
                $"应返回脚本名不合法的错误信息，实际: \"{result.ErrorMessage}\" — 证明未调用 IsValidScriptName");
        }
    }
}
