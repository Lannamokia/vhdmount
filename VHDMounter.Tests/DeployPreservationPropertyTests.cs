using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VHDMounter.SoftwareDeploy;
using Xunit;

namespace VHDMounter.Tests
{
    /// <summary>
    /// 保持性属性测试 — 现有部署流程行为（.NET 机端）
    ///
    /// **Validates: Requirements 3.4, 3.5, 3.6, 3.16**
    ///
    /// 这些测试保护现有正确行为不被修复引入的回归破坏。
    /// 在未修复代码上运行时，测试应 PASS —— 通过即确认基线行为正常。
    ///
    /// 覆盖观察：
    /// - C:\SOFT\&lt;packageId&gt;\ 内的合法路径被 IsValidTargetPath 接受
    /// - 合法的 install.ps1/uninstall.ps1 脚本名通过校验
    /// - 包含有效记录的 deploy_history.json 正确加载和持久化
    /// - 使用有效 key/IV 的 AES-CTR 加密流正确解密
    /// </summary>
    public sealed class DeployPreservationPropertyTests : IDisposable
    {
        private readonly string _tempDir;

        public DeployPreservationPropertyTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "deploy-preservation-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
            catch { }
        }

        // ========== 属性测试 1：合法 packageId 路径被 IsValidTargetPath 接受 ==========

        [Theory]
        [InlineData(@"C:\SOFT\pkg-0123456789abcdef0123456789abcdef")]
        [InlineData(@"C:\SOFT\pkg-aaaabbbbccccddddeeeeffffaaaabbbb")]
        [InlineData(@"C:\SOFT\pkg-deadbeefdeadbeefdeadbeefdeadbeef\subdir")]
        [InlineData(@"C:\SOFT\pkg-1234567890abcdef1234567890abcdef\payload\file.txt")]
        public void Preservation_IsValidTargetPath_AcceptsLegitSoftPaths(string validPath)
        {
            // **Validates: Requirements 3.4, 3.16**
            // 观察：C:\SOFT\<packageId>\ 内的合法路径在未修复代码上被路径校验接受
            var result = DeploySecurityPolicy.IsValidTargetPath(validPath);
            Assert.True(result, $"合法路径 \"{validPath}\" 应被 IsValidTargetPath 接受");
        }

        [Fact]
        public void Preservation_IsValidTargetPath_AcceptsRandomLegitPackageIdPaths()
        {
            // **Validates: Requirements 3.4, 3.16**
            // 属性测试：对所有以 C:\SOFT\ + 合法 packageId 开头的路径，IsValidTargetPath 返回 true
            var random = new Random(42); // 固定种子确保可重现

            for (int i = 0; i < 20; i++)
            {
                // 生成随机合法 packageId
                var bytes = new byte[16];
                random.NextBytes(bytes);
                var packageId = $"pkg-{BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant()}";

                var path = $@"C:\SOFT\{packageId}";
                var result = DeploySecurityPolicy.IsValidTargetPath(path);
                Assert.True(result, $"合法路径 \"{path}\" 应被 IsValidTargetPath 接受");

                // 带子目录
                var pathWithSubdir = $@"C:\SOFT\{packageId}\payload";
                result = DeploySecurityPolicy.IsValidTargetPath(pathWithSubdir);
                Assert.True(result, $"合法路径 \"{pathWithSubdir}\" 应被 IsValidTargetPath 接受");
            }
        }

        // ========== 属性测试 2：合法脚本名通过校验 ==========

        [Theory]
        [InlineData("install.ps1")]
        [InlineData("uninstall.ps1")]
        [InlineData("INSTALL.PS1")]
        [InlineData("Uninstall.PS1")]
        [InlineData("Install.Ps1")]
        public void Preservation_IsValidScriptName_AcceptsLegitScripts(string scriptName)
        {
            // **Validates: Requirements 3.5**
            // 观察：合法的 install.ps1/uninstall.ps1 脚本在未修复代码上正常执行
            var result = DeploySecurityPolicy.IsValidScriptName(scriptName);
            Assert.True(result, $"合法脚本名 \"{scriptName}\" 应通过 IsValidScriptName 校验");
        }

        // ========== 属性测试 3：deploy_history.json 正确加载和持久化 ==========

        [Fact]
        public void Preservation_DeployHistoryStore_LoadAndPersistValidRecords()
        {
            // **Validates: Requirements 3.6**
            // 观察：包含有效记录的 deploy_history.json 在未修复代码上正确加载和持久化
            var storeDir = Path.Combine(_tempDir, "history-test");
            Directory.CreateDirectory(storeDir);
            var store = new DeployHistoryStore(storeDir);

            // 添加多条有效记录
            var records = new List<DeployRecord>();
            for (int i = 0; i < 5; i++)
            {
                var record = new DeployRecord
                {
                    recordId = $"rec-{Guid.NewGuid():N}",
                    packageId = $"pkg-{Guid.NewGuid():N}",
                    name = $"TestPackage-{i}",
                    version = $"{i + 1}.0.0",
                    type = "software-deploy",
                    status = "success",
                    targetPath = $@"C:\SOFT\pkg-{Guid.NewGuid():N}",
                    deployedAt = DateTime.UtcNow.AddHours(-i).ToString("O"),
                    uninstallScript = "uninstall.ps1",
                    requiresAdmin = false,
                    fileManifest = new List<string> { $@"C:\SOFT\file{i}.txt" },
                };
                records.Add(record);
                store.AddRecord(record);
            }

            // 重新加载并验证
            var store2 = new DeployHistoryStore(storeDir);
            var loaded = store2.GetAllRecords();

            Assert.Equal(records.Count, loaded.Count);
            for (int i = 0; i < records.Count; i++)
            {
                var found = loaded.Find(r => r.recordId == records[i].recordId);
                Assert.NotNull(found);
                Assert.Equal(records[i].name, found.name);
                Assert.Equal(records[i].version, found.version);
                Assert.Equal(records[i].type, found.type);
                Assert.Equal(records[i].status, found.status);
            }
        }

        [Fact]
        public void Preservation_DeployHistoryStore_UpdateRecordStatus()
        {
            // **Validates: Requirements 3.6**
            var storeDir = Path.Combine(_tempDir, "history-update-test");
            Directory.CreateDirectory(storeDir);
            var store = new DeployHistoryStore(storeDir);

            var record = new DeployRecord
            {
                recordId = "rec-update-001",
                packageId = "pkg-update-001",
                name = "UpdateTest",
                version = "1.0.0",
                type = "software-deploy",
                status = "success",
                targetPath = @"C:\SOFT\pkg-update-001",
                deployedAt = DateTime.UtcNow.ToString("O"),
                uninstallScript = "uninstall.ps1",
            };
            store.AddRecord(record);

            // 更新状态为 uninstalled
            store.UpdateRecordStatus("rec-update-001", "uninstalled");

            var loaded = store.FindRecord("rec-update-001");
            Assert.NotNull(loaded);
            Assert.Equal("uninstalled", loaded.status);
            Assert.False(string.IsNullOrEmpty(loaded.uninstalledAt));
        }

        [Fact]
        public void Preservation_DeployHistoryStore_OverwritesSameNameSuccessRecord()
        {
            // **Validates: Requirements 3.6**
            // 验证同名成功部署会覆盖旧记录
            var storeDir = Path.Combine(_tempDir, "history-overwrite-test");
            Directory.CreateDirectory(storeDir);
            var store = new DeployHistoryStore(storeDir);

            var record1 = new DeployRecord
            {
                recordId = "rec-v1",
                packageId = "pkg-v1",
                name = "SameApp",
                version = "1.0.0",
                type = "software-deploy",
                status = "success",
                targetPath = @"C:\SOFT\pkg-v1",
                deployedAt = DateTime.UtcNow.AddHours(-1).ToString("O"),
            };
            store.AddRecord(record1);

            var record2 = new DeployRecord
            {
                recordId = "rec-v2",
                packageId = "pkg-v2",
                name = "SameApp",
                version = "2.0.0",
                type = "software-deploy",
                status = "success",
                targetPath = @"C:\SOFT\pkg-v2",
                deployedAt = DateTime.UtcNow.ToString("O"),
            };
            store.AddRecord(record2);

            var all = store.GetAllRecords();
            var sameAppRecords = all.FindAll(r => r.name == "SameApp" && r.status == "success");
            Assert.Single(sameAppRecords);
            Assert.Equal("2.0.0", sameAppRecords[0].version);
        }

        // ========== 属性测试 4：AES-CTR 加密流正确解密 ==========

        [Fact]
        public void Preservation_AesCtr_EncryptDecryptRoundtrip_RandomKeyIv()
        {
            // **Validates: Requirements 3.1, 3.2**
            // 观察：使用有效 key/IV 的 AES-CTR 加密流在未修复代码上正确解密
            var random = new Random(42);

            for (int trial = 0; trial < 10; trial++)
            {
                // 生成随机有效 key (32 bytes) 和 IV (16 bytes, nonce(8) + counter(8))
                var key = new byte[32];
                RandomNumberGenerator.Fill(key);
                var iv = new byte[16];
                RandomNumberGenerator.Fill(iv);
                // 确保 counter 部分从 0 开始（模拟服务端行为）
                Array.Clear(iv, 8, 8);

                // 生成随机长度的明文 (1 到 4096 bytes)
                var plaintextLength = random.Next(1, 4097);
                var plaintext = new byte[plaintextLength];
                RandomNumberGenerator.Fill(plaintext);

                // 加密
                byte[] ciphertext;
                using (var aesCtr = new AesCtrTransform(key, iv))
                {
                    ciphertext = aesCtr.TransformFinalBlock(plaintext, 0, plaintext.Length);
                }

                // 解密
                byte[] decrypted;
                using (var aesCtr = new AesCtrTransform(key, iv))
                {
                    decrypted = aesCtr.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
                }

                Assert.Equal(plaintext, decrypted);
            }
        }

        [Fact]
        public void Preservation_AesCtr_StreamingDecrypt_MatchesFinalBlock()
        {
            // **Validates: Requirements 3.1, 3.2**
            // 验证 CryptoStream 流式解密与 TransformFinalBlock 结果一致
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            var iv = new byte[16];
            RandomNumberGenerator.Fill(iv);
            Array.Clear(iv, 8, 8);

            var plaintext = new byte[1024];
            RandomNumberGenerator.Fill(plaintext);

            // 使用 TransformFinalBlock 加密
            byte[] ciphertext;
            using (var aesCtr = new AesCtrTransform(key, iv))
            {
                ciphertext = aesCtr.TransformFinalBlock(plaintext, 0, plaintext.Length);
            }

            // 使用 CryptoStream 流式解密
            byte[] streamDecrypted;
            using (var input = new MemoryStream(ciphertext))
            using (var aesCtr = new AesCtrTransform(key, iv))
            using (var cryptoStream = new System.Security.Cryptography.CryptoStream(input, aesCtr, System.Security.Cryptography.CryptoStreamMode.Read))
            using (var output = new MemoryStream())
            {
                cryptoStream.CopyTo(output);
                streamDecrypted = output.ToArray();
            }

            Assert.Equal(plaintext, streamDecrypted);
        }

        [Fact]
        public void Preservation_AesCtr_OffsetDecrypt_SimulatesRangeResume()
        {
            // **Validates: Requirements 3.3**
            // 验证从偏移位置开始的 CTR 解密（模拟 Range 断点续传）
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            var iv = new byte[16];
            RandomNumberGenerator.Fill(iv);
            Array.Clear(iv, 8, 8);

            var plaintext = new byte[256];
            RandomNumberGenerator.Fill(plaintext);

            // 完整加密
            byte[] fullCiphertext;
            using (var aesCtr = new AesCtrTransform(key, iv))
            {
                fullCiphertext = aesCtr.TransformFinalBlock(plaintext, 0, plaintext.Length);
            }

            // 从 offset=64 开始解密（模拟 Range 续传）
            int offset = 64;
            int blockSize = 16;
            int counter = offset / blockSize;
            int blockOffset = offset % blockSize;

            // 构造偏移后的 IV
            var offsetIv = new byte[16];
            Buffer.BlockCopy(iv, 0, offsetIv, 0, 8);
            // 写入 counter 值（big-endian）
            var counterBytes = BitConverter.GetBytes((long)counter);
            if (BitConverter.IsLittleEndian) Array.Reverse(counterBytes);
            Buffer.BlockCopy(counterBytes, 0, offsetIv, 8, 8);

            // 从 offset 开始的密文
            var partialCiphertext = new byte[fullCiphertext.Length - offset];
            Buffer.BlockCopy(fullCiphertext, offset, partialCiphertext, 0, partialCiphertext.Length);

            // 使用偏移 IV 解密
            byte[] partialDecrypted;
            using (var aesCtr = new AesCtrTransform(key, offsetIv))
            {
                // 跳过 blockOffset 字节的 keystream
                if (blockOffset > 0)
                {
                    var skip = new byte[blockOffset];
                    aesCtr.TransformBlock(skip, 0, blockOffset, skip, 0);
                }
                partialDecrypted = aesCtr.TransformFinalBlock(partialCiphertext, 0, partialCiphertext.Length);
            }

            // 验证解密结果与原始明文的对应部分一致
            var expectedPlaintext = new byte[plaintext.Length - offset];
            Buffer.BlockCopy(plaintext, offset, expectedPlaintext, 0, expectedPlaintext.Length);
            Assert.Equal(expectedPlaintext, partialDecrypted);
        }
    }
}
