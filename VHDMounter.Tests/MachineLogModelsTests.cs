using System;
using System.IO;
using System.Text;
using Xunit;

namespace VHDMounter.Tests
{
    public sealed class MachineLogModelsTests : IDisposable
    {
        private readonly string tempDir;

        public MachineLogModelsTests()
        {
            tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
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
        public void Load_ParsesIniFile()
        {
            var configPath = Path.Combine(tempDir, "test-config.ini");
            File.WriteAllText(configPath, @"
MachineId = TEST_001
ServerBaseUrl = http://192.168.1.100:8080
EnableLogUpload = true
MachineLogUploadIntervalMs = 5000
MachineLogUploadBatchSize = 100
MachineLogUploadMaxSpoolBytes = 10485760
RegistrationCertificatePath = C:\\cert.pem
RegistrationCertificatePassword = secret
", Encoding.UTF8);

            var config = MachineLogClientConfiguration.Load(configPath);

            Assert.Equal(configPath, config.ConfigPath);
            Assert.Equal("TEST_001", config.MachineId);
            Assert.Equal("http://192.168.1.100:8080", config.ServerBaseUrl);
            Assert.True(config.EnableLogUpload);
            Assert.Equal(5000, config.MachineLogUploadIntervalMs);
            Assert.Equal(100, config.MachineLogUploadBatchSize);
            Assert.Equal(10485760, config.MachineLogUploadMaxSpoolBytes);
            Assert.Equal("C:\\\\cert.pem", config.RegistrationCertificatePath);
            Assert.Equal("secret", config.RegistrationCertificatePassword);
        }

        [Fact]
        public void Load_UsesDefaultsForMissingFile()
        {
            var configPath = Path.Combine(tempDir, "nonexistent.ini");
            var diagnostics = new System.Collections.Generic.List<string>();

            var config = MachineLogClientConfiguration.Load(configPath, diagnostics.Add);

            Assert.Equal(configPath, config.ConfigPath);
            Assert.Equal("MACHINE_001", config.MachineId);
            Assert.Equal("http://127.0.0.1:8080", config.ServerBaseUrl);
            Assert.False(config.EnableLogUpload);
            Assert.Equal(3000, config.MachineLogUploadIntervalMs);
            Assert.Equal(200, config.MachineLogUploadBatchSize);
            Assert.Single(diagnostics);
        }

        [Fact]
        public void Load_SkipsCommentsAndEmptyLines()
        {
            var configPath = Path.Combine(tempDir, "test-config.ini");
            File.WriteAllText(configPath, @"
; this is a comment

MachineId = TEST_002
; another comment
[Section]
ServerBaseUrl = http://server.com
", Encoding.UTF8);

            var config = MachineLogClientConfiguration.Load(configPath);

            Assert.Equal("TEST_002", config.MachineId);
            Assert.Equal("http://server.com", config.ServerBaseUrl);
        }

        [Fact]
        public void Load_IsCaseInsensitive()
        {
            var configPath = Path.Combine(tempDir, "test-config.ini");
            File.WriteAllText(configPath, "machineid = CASE_001\nserverbaseurl = http://case.com\n", Encoding.UTF8);

            var config = MachineLogClientConfiguration.Load(configPath);

            Assert.Equal("CASE_001", config.MachineId);
            Assert.Equal("http://case.com", config.ServerBaseUrl);
        }

        [Fact]
        public void Load_ClampsIntervalMsWithinBounds()
        {
            var configPath = Path.Combine(tempDir, "test-config.ini");
            File.WriteAllText(configPath, "MachineLogUploadIntervalMs = 100\n", Encoding.UTF8);

            var config = MachineLogClientConfiguration.Load(configPath);

            Assert.Equal(250, config.MachineLogUploadIntervalMs);
        }

        [Fact]
        public void Load_ClampsBatchSizeWithinBounds()
        {
            var configPath = Path.Combine(tempDir, "test-config.ini");
            File.WriteAllText(configPath, "MachineLogUploadBatchSize = 500\n", Encoding.UTF8);

            var config = MachineLogClientConfiguration.Load(configPath);

            Assert.Equal(200, config.MachineLogUploadBatchSize);
        }

        [Fact]
        public void Load_ClampsMaxSpoolBytesWithinBounds()
        {
            var configPath = Path.Combine(tempDir, "test-config.ini");
            File.WriteAllText(configPath, "MachineLogUploadMaxSpoolBytes = 1000\n", Encoding.UTF8);

            var config = MachineLogClientConfiguration.Load(configPath);

            Assert.Equal(1048576, config.MachineLogUploadMaxSpoolBytes);
        }

        [Fact]
        public void Load_ParsesBoolCorrectly()
        {
            var configPath = Path.Combine(tempDir, "test-config.ini");
            File.WriteAllText(configPath, "EnableLogUpload = True\n", Encoding.UTF8);

            var config = MachineLogClientConfiguration.Load(configPath);

            Assert.True(config.EnableLogUpload);
        }

        [Fact]
        public void Load_InvalidBoolUsesDefault()
        {
            var configPath = Path.Combine(tempDir, "test-config.ini");
            File.WriteAllText(configPath, "EnableLogUpload = notabool\n", Encoding.UTF8);

            var config = MachineLogClientConfiguration.Load(configPath);

            Assert.False(config.EnableLogUpload);
        }

        [Fact]
        public void ResolveEnvelopeUrl_BuildsCorrectUrl()
        {
            var configPath = Path.Combine(tempDir, "test-config.ini");
            File.WriteAllText(configPath, "ServerBaseUrl = http://192.168.1.100:8080\n", Encoding.UTF8);

            var config = MachineLogClientConfiguration.Load(configPath);

            Assert.Equal("http://192.168.1.100:8080/api/evhd-envelope", config.ResolveEnvelopeUrl());
        }

        [Fact]
        public void ResolveWebSocketUri_BuildsCorrectUri()
        {
            var configPath = Path.Combine(tempDir, "test-config.ini");
            File.WriteAllText(configPath, "ServerBaseUrl = http://192.168.1.100:8080\n", Encoding.UTF8);

            var config = MachineLogClientConfiguration.Load(configPath);
            var uri = config.ResolveWebSocketUri();

            Assert.Equal("ws://192.168.1.100:8080/ws/machine-log", uri.AbsoluteUri);
        }

        [Fact]
        public void GenerateSessionId_HasCorrectFormat()
        {
            var sessionId = MachineLogClientConfiguration.GenerateSessionId();

            Assert.NotNull(sessionId);
            Assert.True(sessionId.Length > 16);
            Assert.Contains("-", sessionId);

            var parts = sessionId.Split('-');
            Assert.Equal(2, parts.Length);

            var timestampPart = parts[0];
            Assert.Equal(16, timestampPart.Length);
            Assert.True(timestampPart.Contains("T"));
            Assert.EndsWith("Z", timestampPart);

            var suffixPart = parts[1];
            Assert.Equal(6, suffixPart.Length);
            Assert.Matches("^[0-9a-f]{6}$", suffixPart);
        }

        [Fact]
        public void GenerateSessionId_ProducesUniqueValues()
        {
            var id1 = MachineLogClientConfiguration.GenerateSessionId();
            System.Threading.Thread.Sleep(10);
            var id2 = MachineLogClientConfiguration.GenerateSessionId();

            Assert.NotEqual(id1, id2);
        }

        [Fact]
        public void SpoolPath_IsInBaseDirectory()
        {
            var configPath = Path.Combine(tempDir, "test-config.ini");
            File.WriteAllText(configPath, "MachineId = TEST\n", Encoding.UTF8);

            var config = MachineLogClientConfiguration.Load(configPath);

            Assert.EndsWith("machine-log-spool.jsonl", config.SpoolPath);
        }
    }
}
