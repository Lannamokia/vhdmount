using System;
using System.IO;
using VHDMounter.SoftwareDeploy;
using Xunit;

namespace VHDMounter.Tests
{
    public sealed class DeployExecutionTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _softwareRoot;
        private readonly string? _originalSoftwareRoot;

        public DeployExecutionTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"vhd-deploy-exec-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);

            _softwareRoot = Path.Combine(_tempDir, "SOFT");
            _originalSoftwareRoot = Environment.GetEnvironmentVariable("VHDMOUNT_SOFTWARE_DEPLOY_ROOT");
            Environment.SetEnvironmentVariable("VHDMOUNT_SOFTWARE_DEPLOY_ROOT", _softwareRoot);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("VHDMOUNT_SOFTWARE_DEPLOY_ROOT", _originalSoftwareRoot);
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, true);
                }
            }
            catch
            {
            }
        }

        [Fact]
        public void ExecuteSoftwareDeploy_PersistsPackageUnderStableDirectory()
        {
            var extractDir = Path.Combine(_tempDir, "extract-install");
            Directory.CreateDirectory(extractDir);
            File.WriteAllText(Path.Combine(extractDir, "deploy.json"), "{}");
            File.WriteAllText(
                Path.Combine(extractDir, "install.ps1"),
                "param([string]$DeployJson)`nexit 0");
            File.WriteAllText(
                Path.Combine(extractDir, "uninstall.ps1"),
                "param([string]$DeployJson)`nexit 0");
            File.WriteAllText(Path.Combine(extractDir, "payload.bin"), "payload");

            var manifest = new DeployManifest
            {
                name = "TestApp",
                version = "1.0.0",
                type = "software-deploy",
                installScript = "install.ps1",
                uninstallScript = "uninstall.ps1",
            };

            var result = DeployExecutor.ExecuteSoftwareDeploy(extractDir, "pkg-test-001", manifest);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(Path.Combine(_softwareRoot, "pkg-test-001"), result.DeploymentPath);
            Assert.True(File.Exists(Path.Combine(result.DeploymentPath, "install.ps1")));
            Assert.True(File.Exists(Path.Combine(result.DeploymentPath, "uninstall.ps1")));
            Assert.True(File.Exists(Path.Combine(result.DeploymentPath, "payload.bin")));
            Assert.True(File.Exists(Path.Combine(result.DeploymentPath, "deploy.json")));
        }

        [Fact]
        public void UninstallSoftware_UsesPersistedDirectoryAndRemovesItOnSuccess()
        {
            var installDir = Path.Combine(_softwareRoot, "pkg-test-002");
            Directory.CreateDirectory(installDir);

            File.WriteAllText(Path.Combine(installDir, "deploy.json"), "{ \"name\": \"TestApp\" }");
            File.WriteAllText(
                Path.Combine(installDir, "uninstall.ps1"),
                "param([string]$DeployJson)`nexit 0");

            var record = new DeployRecord
            {
                recordId = "rec-001",
                packageId = "pkg-test-002",
                name = "TestApp",
                version = "1.0.0",
                type = "software-deploy",
                targetPath = installDir,
                uninstallScript = "uninstall.ps1",
            };

            var result = DeployUninstaller.UninstallSoftware(record);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.False(Directory.Exists(installDir));
        }

        [Fact]
        public void GenerateFileManifest_PreservesNestedRelativePaths()
        {
            var historyStore = new DeployHistoryStore(_tempDir);
            var targetPath = Path.Combine(_tempDir, "file-target");
            Directory.CreateDirectory(targetPath);

            historyStore.AddRecord(new DeployRecord
            {
                recordId = "rec-file-001",
                packageId = "pkg-file-001",
                name = "FilePack",
                version = "1.0.0",
                type = "file-deploy",
                status = "success",
                targetPath = targetPath,
            });

            var extractDir = Path.Combine(_tempDir, "extract-file");
            var payloadDir = Path.Combine(extractDir, "payload");
            Directory.CreateDirectory(Path.Combine(payloadDir, "subdir"));
            File.WriteAllText(Path.Combine(payloadDir, "data.txt"), "root");
            File.WriteAllText(Path.Combine(payloadDir, "subdir", "nested.txt"), "nested");

            historyStore.GenerateFileManifest(extractDir, targetPath);

            var record = historyStore.FindRecord("rec-file-001");
            Assert.NotNull(record);
            Assert.Contains(Path.Combine(targetPath, "data.txt"), record!.fileManifest);
            Assert.Contains(Path.Combine(targetPath, "subdir", "nested.txt"), record.fileManifest);
        }
    }
}
