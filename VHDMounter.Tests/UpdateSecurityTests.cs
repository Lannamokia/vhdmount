using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace VHDMounter.Tests
{
    public sealed class UpdateSecurityTests : IDisposable
    {
        private readonly string tempDir;

        public UpdateSecurityTests()
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

        private string WriteTempFile(string name, string content)
        {
            var path = Path.Combine(tempDir, name);
            File.WriteAllText(path, content, Encoding.UTF8);
            return path;
        }

        private string WriteTempFileBytes(string name, byte[] content)
        {
            var path = Path.Combine(tempDir, name);
            File.WriteAllBytes(path, content);
            return path;
        }

        [Fact]
        public void LoadManifest_ParsesValidJson()
        {
            var manifest = new
            {
                version = "2.1.0",
                minVersion = "2.0.0",
                type = "app-update",
                signer = "test",
                createdAt = DateTime.UtcNow.ToString("O"),
                expiresAt = DateTime.UtcNow.AddDays(7).ToString("O"),
                files = new[]
                {
                    new { path = "app.exe", target = "app.exe", size = 1024L, sha256 = "abc123" },
                },
            };
            var manifestPath = WriteTempFile("manifest.json", JsonSerializer.Serialize(manifest));

            var result = UpdateSecurity.LoadManifest(manifestPath);

            Assert.Equal("2.1.0", result.version);
            Assert.Equal("app-update", result.type);
            Assert.Single(result.files);
        }

        [Fact]
        public void LoadManifest_ThrowsForInvalidJson()
        {
            var manifestPath = WriteTempFile("manifest.json", "not json");

            Assert.Throws<System.Text.Json.JsonException>(() => UpdateSecurity.LoadManifest(manifestPath));
        }

        [Fact]
        public void ValidateAppUpdatePayloadSize_TrueForNonAppUpdate()
        {
            var manifest = new UpdateManifest { type = "config-update" };

            var result = UpdateSecurity.ValidateAppUpdatePayloadSize(manifest, out var totalBytes, out var error);

            Assert.True(result);
            Assert.Equal(0, totalBytes);
            Assert.Empty(error);
        }

        [Fact]
        public void ValidateAppUpdatePayloadSize_TrueForValidSize()
        {
            var manifest = new UpdateManifest
            {
                type = "app-update",
                files = new System.Collections.Generic.List<UpdateManifestFile>
                {
                    new UpdateManifestFile { size = 1024 * 1024 },
                    new UpdateManifestFile { size = 2048 * 1024 },
                },
            };

            var result = UpdateSecurity.ValidateAppUpdatePayloadSize(manifest, out var totalBytes, out var error);

            Assert.True(result);
            Assert.Equal(3072 * 1024, totalBytes);
            Assert.Empty(error);
        }

        [Fact]
        public void ValidateAppUpdatePayloadSize_FalseForOversizedPayload()
        {
            var manifest = new UpdateManifest
            {
                type = "app-update",
                files = new System.Collections.Generic.List<UpdateManifestFile>
                {
                    new UpdateManifestFile { size = UpdateSecurity.MaxAppUpdatePayloadBytes + 1 },
                },
            };

            var result = UpdateSecurity.ValidateAppUpdatePayloadSize(manifest, out var totalBytes, out var error);

            Assert.False(result);
            Assert.True(totalBytes > UpdateSecurity.MaxAppUpdatePayloadBytes);
            Assert.False(string.IsNullOrEmpty(error));
        }

        [Fact]
        public void ValidateAppUpdatePayloadSize_HandlesNullFiles()
        {
            var manifest = new UpdateManifest { type = "app-update" };

            var result = UpdateSecurity.ValidateAppUpdatePayloadSize(manifest, out var totalBytes, out var error);

            Assert.True(result);
            Assert.Equal(0, totalBytes);
        }

        [Fact]
        public void ValidateAppUpdatePayloadSize_HandlesNegativeSize()
        {
            var manifest = new UpdateManifest
            {
                type = "app-update",
                files = new System.Collections.Generic.List<UpdateManifestFile>
                {
                    new UpdateManifestFile { size = -100 },
                },
            };

            var result = UpdateSecurity.ValidateAppUpdatePayloadSize(manifest, out var totalBytes, out var error);

            Assert.True(result);
            Assert.Equal(0, totalBytes);
        }

        [Fact]
        public void VerifyFileHash_TrueForMatchingHash()
        {
            var content = Encoding.UTF8.GetBytes("test file content");
            var hash = SHA256.HashData(content);
            var expectedHash = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            var filePath = WriteTempFileBytes("test.txt", content);

            var result = UpdateSecurity.VerifyFileHash(filePath, expectedHash, content.Length);

            Assert.True(result);
        }

        [Fact]
        public void VerifyFileHash_FalseForWrongHash()
        {
            var content = Encoding.UTF8.GetBytes("test file content");
            var filePath = WriteTempFileBytes("test.txt", content);

            var result = UpdateSecurity.VerifyFileHash(filePath, "0000000000000000000000000000000000000000000000000000000000000000", content.Length);

            Assert.False(result);
        }

        [Fact]
        public void VerifyFileHash_FalseForMissingFile()
        {
            var result = UpdateSecurity.VerifyFileHash(Path.Combine(tempDir, "missing.txt"), "abc", 0);

            Assert.False(result);
        }

        [Fact]
        public void VerifyFileHash_FalseForSizeMismatch()
        {
            var content = Encoding.UTF8.GetBytes("test file content");
            var hash = SHA256.HashData(content);
            var expectedHash = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            var filePath = WriteTempFileBytes("test.txt", content);

            var result = UpdateSecurity.VerifyFileHash(filePath, expectedHash, content.Length + 1);

            Assert.False(result);
        }

        [Fact]
        public void VerifyFileHash_IgnoresSizeWhenZero()
        {
            var content = Encoding.UTF8.GetBytes("test file content");
            var hash = SHA256.HashData(content);
            var expectedHash = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            var filePath = WriteTempFileBytes("test.txt", content);

            var result = UpdateSecurity.VerifyFileHash(filePath, expectedHash, 0);

            Assert.True(result);
        }

        [Fact]
        public void VerifyFileHash_IsCaseInsensitive()
        {
            var content = Encoding.UTF8.GetBytes("test file content");
            var hash = SHA256.HashData(content);
            var expectedHash = BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
            var filePath = WriteTempFileBytes("test.txt", content);

            var result = UpdateSecurity.VerifyFileHash(filePath, expectedHash, content.Length);

            Assert.True(result);
        }

        [Fact]
        public void VerifyManifestSignature_FalseForInvalidSignature()
        {
            var manifestContent = "test manifest content";
            var manifestPath = WriteTempFile("manifest.json", manifestContent);

            using var rsa1 = RSA.Create(2048);
            using var rsa2 = RSA.Create(2048);
            var publicKeyPem = ExportPublicKeyPem(rsa1);
            var keysPath = WriteTempFile("keys.pem", publicKeyPem);

            var wrongContent = Encoding.UTF8.GetBytes("wrong content");
            var signatureBytes = rsa2.SignData(wrongContent, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
            var signaturePath = WriteTempFileBytes("manifest.sig", signatureBytes);

            var result = UpdateSecurity.VerifyManifestSignature(manifestPath, signaturePath, keysPath);

            Assert.False(result);
        }

        [Fact]
        public void VerifyManifestSignature_FalseForMissingKeysFile()
        {
            var manifestPath = WriteTempFile("manifest.json", "{}");
            var signaturePath = WriteTempFile("manifest.sig", "sig");

            var result = UpdateSecurity.VerifyManifestSignature(manifestPath, signaturePath, Path.Combine(tempDir, "missing.pem"));

            Assert.False(result);
        }

        [Fact]
        public void VerifyManifestSignature_FalseForMissingSignatureFile()
        {
            var manifestPath = WriteTempFile("manifest.json", "{}");
            using var rsa = RSA.Create(2048);
            var keysPath = WriteTempFile("keys.pem", ExportPublicKeyPem(rsa));

            var result = UpdateSecurity.VerifyManifestSignature(manifestPath, Path.Combine(tempDir, "missing.sig"), keysPath);

            Assert.False(result);
        }

        [Fact]
        public void VerifyManifestSignature_ReturnsFalseWhenNoKeysMatch()
        {
            var manifestPath = WriteTempFile("manifest.json", "{}");
            var signaturePath = WriteTempFile("manifest.sig", "dummy");

            using var rsa = RSA.Create(2048);
            var keysPath = WriteTempFile("keys.pem", ExportPublicKeyPem(rsa));

            var result = UpdateSecurity.VerifyManifestSignature(manifestPath, signaturePath, keysPath);

            Assert.False(result);
        }

        [Fact]
        public void PemExportAndImport_RoundTrip()
        {
            using var rsa = RSA.Create(2048);
            var pem = ExportPublicKeyPem(rsa);
            var pkcs8 = Convert.FromBase64String(string.Concat(pem.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(l => !l.StartsWith("-----") && !l.EndsWith("-----"))));
            var imported = RSA.Create();
            imported.ImportSubjectPublicKeyInfo(pkcs8, out _);
            Assert.Equal(rsa.KeySize, imported.KeySize);
        }

        private static string ExportPublicKeyPem(RSA rsa)
        {
            return rsa.ExportSubjectPublicKeyInfoPem();
        }
    }
}
