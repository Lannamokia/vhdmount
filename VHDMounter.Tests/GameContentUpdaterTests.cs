using System;
using System.IO;
using VHDMounter.SoftwareDeploy;
using Xunit;

namespace VHDMounter.Tests
{
    public sealed class GameContentUpdaterTests : IDisposable
    {
        private readonly string _tempDir;

        public GameContentUpdaterTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, true);
            }
            catch { }
        }

        [Fact]
        public void ValidateManifest_GameOption_AllowedWithoutTargetAndScripts()
        {
            var manifest = new DeployManifest
            {
                name = "OptionUpdate",
                version = "1.0.0",
                type = "game-option-deploy",
            };
            Assert.Null(DeploySecurityPolicy.ValidateManifest(manifest));
            Assert.True(manifest.IsGameOptionDeploy);
        }

        [Fact]
        public void ValidateManifest_GameOption_RejectsTargetPath()
        {
            var manifest = new DeployManifest
            {
                name = "OptionUpdate",
                version = "1.0.0",
                type = "game-option-deploy",
                targetPath = @"M:\game\option",
            };
            Assert.Equal("game-option-deploy 不允许指定 targetPath", DeploySecurityPolicy.ValidateManifest(manifest));
        }

        [Fact]
        public void ValidateManifest_GameOption_RejectsInstallScript()
        {
            var manifest = new DeployManifest
            {
                name = "OptionUpdate",
                version = "1.0.0",
                type = "game-option-deploy",
                installScript = "install.ps1",
            };
            Assert.Equal("game-option-deploy 不允许携带 installScript", DeploySecurityPolicy.ValidateManifest(manifest));
        }

        [Fact]
        public void ValidateManifest_GameOption_RejectsUninstallScript()
        {
            var manifest = new DeployManifest
            {
                name = "OptionUpdate",
                version = "1.0.0",
                type = "game-option-deploy",
                uninstallScript = "uninstall.ps1",
            };
            Assert.Equal("game-option-deploy 不允许携带 uninstallScript", DeploySecurityPolicy.ValidateManifest(manifest));
        }

        [Fact]
        public void ValidateOptionTargetPath_AcceptsMDrivePackage()
        {
            bool ok = GameContentUpdater.ValidateOptionTargetPath(@"M:\package", out string optionPath);
            Assert.True(ok);
            Assert.Equal(@"M:\package\option", optionPath);
        }

        [Fact]
        public void ValidateOptionTargetPath_AcceptsMDriveBin()
        {
            bool ok = GameContentUpdater.ValidateOptionTargetPath(@"M:\bin", out string optionPath);
            Assert.True(ok);
            Assert.Equal(@"M:\bin\option", optionPath);
        }

        [Fact]
        public void ValidateOptionTargetPath_RejectsCDrive()
        {
            bool ok = GameContentUpdater.ValidateOptionTargetPath(@"C:\game", out string optionPath);
            Assert.False(ok);
            Assert.Equal(string.Empty, optionPath);
        }

        [Fact]
        public void ValidateOptionTargetPath_RejectsRelativePath()
        {
            bool ok = GameContentUpdater.ValidateOptionTargetPath("game", out string optionPath);
            Assert.False(ok);
            Assert.Equal(string.Empty, optionPath);
        }

        [Fact]
        public void ValidateOptionTargetPath_RejectsTraversal()
        {
            bool ok = GameContentUpdater.ValidateOptionTargetPath(@"M:\game\..\Windows", out string optionPath);
            Assert.False(ok);
            Assert.Equal(string.Empty, optionPath);
        }

        [Fact]
        public void ResolveContentSourceDirectory_PrefersPayload()
        {
            string extractDir = Path.Combine(_tempDir, "extract");
            string payloadDir = Path.Combine(extractDir, "payload");
            Directory.CreateDirectory(payloadDir);
            string rootFile = Path.Combine(extractDir, "deploy.json");
            File.WriteAllText(rootFile, "{}");

            string source = GameContentUpdater.ResolveContentSourceDirectory(extractDir);
            Assert.Equal(payloadDir, source);
        }

        [Fact]
        public void ResolveContentSourceDirectory_FallsBackToRoot()
        {
            string extractDir = Path.Combine(_tempDir, "extract");
            Directory.CreateDirectory(extractDir);
            string rootFile = Path.Combine(extractDir, "a.txt");
            File.WriteAllText(rootFile, "x");

            string source = GameContentUpdater.ResolveContentSourceDirectory(extractDir);
            Assert.Equal(extractDir, source);
        }

        [Fact]
        public void ApplyToOptionDirectory_CreatesNewOptionFromPayload()
        {
            string extractDir = CreateExtractDirWithPayload();
            string optionPath = Path.Combine(_tempDir, "option");

            var result = GameContentUpdater.ApplyToOptionDirectory(extractDir, optionPath, "task-1");

            Assert.True(result.Success);
            Assert.True(Directory.Exists(optionPath));
            Assert.Equal("hello", File.ReadAllText(Path.Combine(optionPath, "data.txt")));
            Assert.False(Directory.Exists(optionPath + ".bak-task-1"));
        }

        [Fact]
        public void ApplyToOptionDirectory_BackupsExistingOption()
        {
            string extractDir = CreateExtractDirWithPayload();
            string optionPath = Path.Combine(_tempDir, "option");
            Directory.CreateDirectory(optionPath);
            File.WriteAllText(Path.Combine(optionPath, "old.txt"), "old");

            var result = GameContentUpdater.ApplyToOptionDirectory(extractDir, optionPath, "task-2");

            Assert.True(result.Success);
            Assert.True(File.Exists(Path.Combine(optionPath, "data.txt")));
            Assert.False(File.Exists(Path.Combine(optionPath, "old.txt")));
            Assert.False(Directory.Exists(optionPath + ".bak-task-2"));
        }

        [Fact]
        public void RestoreOptionBackup_RestoresPreviousOption()
        {
            string optionPath = Path.Combine(_tempDir, "option");
            string backupPath = optionPath + ".bak-task-3";
            Directory.CreateDirectory(backupPath);
            File.WriteAllText(Path.Combine(backupPath, "restored.txt"), "restored");

            GameContentUpdater.RestoreOptionBackup(optionPath, backupPath);

            Assert.True(Directory.Exists(optionPath));
            Assert.Equal("restored", File.ReadAllText(Path.Combine(optionPath, "restored.txt")));
        }

        private string CreateExtractDirWithPayload()
        {
            string extractDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            string payloadDir = Path.Combine(extractDir, "payload");
            Directory.CreateDirectory(payloadDir);
            File.WriteAllText(Path.Combine(payloadDir, "data.txt"), "hello");
            return extractDir;
        }
    }
}
