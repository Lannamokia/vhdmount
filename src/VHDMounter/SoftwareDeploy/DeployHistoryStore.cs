#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace VHDMounter.SoftwareDeploy
{
    public class DeployRecord
    {
        public string recordId { get; set; } = string.Empty;
        public string packageId { get; set; } = string.Empty;
        public string name { get; set; } = string.Empty;
        public string version { get; set; } = string.Empty;
        public string type { get; set; } = string.Empty;
        public string deployedAt { get; set; } = string.Empty;
        public string uninstalledAt { get; set; } = string.Empty;
        public string status { get; set; } = "success"; // success / failed / uninstalled
        public string targetPath { get; set; } = string.Empty;
        public string uninstallScript { get; set; } = string.Empty;
        public bool requiresAdmin { get; set; }
        public List<string> fileManifest { get; set; } = new();
    }

    public class DeployHistory
    {
        public List<DeployRecord> records { get; set; } = new();
    }

    internal class DeployHistoryEnvelope
    {
        public string data { get; set; } = string.Empty;
        public string hmac { get; set; } = string.Empty;
    }

    public class DeployHistoryStore
    {
        private readonly string _filePath;
        private readonly Mutex _fileMutex;
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
        private const string HMAC_FIXED_MESSAGE = "VHDMounter_DeployHistory_HMAC_Key_Derivation_v1";

        public DeployHistoryStore(string baseDir)
        {
            _filePath = Path.Combine(baseDir, "deploy_history.json");
            _fileMutex = new Mutex(false, @"Global\VHDMounter_DeployHistory");
        }

        private T WithFileLock<T>(Func<DeployHistory, T> operation)
        {
            _fileMutex.WaitOne();
            try
            {
                var history = LoadInternal();
                var result = operation(history);
                SaveAtomic(history);
                return result;
            }
            finally
            {
                _fileMutex.ReleaseMutex();
            }
        }

        private void WithFileLock(Action<DeployHistory> operation)
        {
            _fileMutex.WaitOne();
            try
            {
                var history = LoadInternal();
                operation(history);
                SaveAtomic(history);
            }
            finally
            {
                _fileMutex.ReleaseMutex();
            }
        }

        private DeployHistory LoadInternal()
        {
            if (!File.Exists(_filePath))
                return new DeployHistory();

            var rawJson = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(rawJson))
                return new DeployHistory();

            // Try to parse as HMAC envelope first
            try
            {
                var envelope = JsonSerializer.Deserialize<DeployHistoryEnvelope>(rawJson);
                if (envelope != null && !string.IsNullOrEmpty(envelope.data) && !string.IsNullOrEmpty(envelope.hmac))
                {
                    // Verify HMAC
                    var expectedHmac = ComputeHmac(envelope.data);
                    if (!string.Equals(expectedHmac, envelope.hmac, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("deploy_history.json HMAC 校验失败，文件可能被篡改");
                    }
                    var history = JsonSerializer.Deserialize<DeployHistory>(envelope.data);
                    return EnsureNullSafety(history);
                }
            }
            catch (JsonException)
            {
                // Not an envelope format, try legacy
            }
            catch (InvalidOperationException)
            {
                throw; // Re-throw HMAC failures
            }

            // Legacy format: plain JSON (auto-migrate on next save)
            try
            {
                var history = JsonSerializer.Deserialize<DeployHistory>(rawJson);
                return EnsureNullSafety(history);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"deploy_history.json 反序列化失败: {ex.Message}", ex);
            }
        }

        private static DeployHistory EnsureNullSafety(DeployHistory? history)
        {
            if (history == null)
                return new DeployHistory();
            history.records ??= new List<DeployRecord>();
            foreach (var record in history.records)
            {
                record.fileManifest ??= new List<string>();
            }
            return history;
        }

        private void SaveAtomic(DeployHistory history)
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var dataJson = JsonSerializer.Serialize(history, _jsonOptions);
            var hmac = ComputeHmac(dataJson);
            var envelope = new DeployHistoryEnvelope { data = dataJson, hmac = hmac };
            var envelopeJson = JsonSerializer.Serialize(envelope, _jsonOptions);

            var tempPath = _filePath + ".tmp";
            File.WriteAllText(tempPath, envelopeJson);

            if (File.Exists(_filePath))
            {
                File.Replace(tempPath, _filePath, _filePath + ".bak");
            }
            else
            {
                File.Move(tempPath, _filePath);
            }
        }

        private static string ComputeHmac(string data)
        {
            // Derive HMAC key from TPM-RSA private key signing a fixed message → SHA-256
            byte[] hmacKey;
            try
            {
                using var rsa = VHDManager.EnsureOrCreateTpmRsa(Environment.MachineName);
                var signatureBytes = rsa.SignData(
                    Encoding.UTF8.GetBytes(HMAC_FIXED_MESSAGE),
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);
                hmacKey = SHA256.HashData(signatureBytes);
            }
            catch
            {
                // Fallback: use machine-specific key if TPM unavailable
                hmacKey = SHA256.HashData(Encoding.UTF8.GetBytes($"VHDMounter_{Environment.MachineName}_FallbackKey"));
            }

            using var hmacAlg = new HMACSHA256(hmacKey);
            var hash = hmacAlg.ComputeHash(Encoding.UTF8.GetBytes(data));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        public void AddRecord(DeployRecord record)
        {
            WithFileLock(history =>
            {
                if (string.Equals(record.status, "success", StringComparison.OrdinalIgnoreCase))
                {
                    history.records.RemoveAll(r =>
                        r.name == record.name &&
                        r.type == record.type &&
                        string.Equals(r.status, "success", StringComparison.OrdinalIgnoreCase));
                }
                history.records.Add(record);
            });
        }

        public void UpdateRecordStatus(string recordId, string status)
        {
            WithFileLock(history =>
            {
                var record = history.records.FirstOrDefault(r => r.recordId == recordId);
                if (record != null)
                {
                    record.status = status;
                    if (status == "uninstalled")
                    {
                        record.uninstalledAt = DateTime.UtcNow.ToString("O");
                        record.fileManifest.Clear();
                    }
                }
            });
        }

        public DeployRecord? FindRecord(string recordId)
        {
            _fileMutex.WaitOne();
            try
            {
                var history = LoadInternal();
                return history.records.FirstOrDefault(r => r.recordId == recordId);
            }
            finally
            {
                _fileMutex.ReleaseMutex();
            }
        }

        public DeployRecord? FindRecordByName(string name)
        {
            _fileMutex.WaitOne();
            try
            {
                var history = LoadInternal();
                return history.records
                    .Where(r => r.name == name && r.status != "uninstalled")
                    .OrderByDescending(r => r.deployedAt)
                    .FirstOrDefault();
            }
            finally
            {
                _fileMutex.ReleaseMutex();
            }
        }

        public List<DeployRecord> GetAllRecords()
        {
            _fileMutex.WaitOne();
            try
            {
                return LoadInternal().records;
            }
            finally
            {
                _fileMutex.ReleaseMutex();
            }
        }

        public List<DeployRecord> GetRecordsForSync()
        {
            _fileMutex.WaitOne();
            try
            {
                return LoadInternal().records.ToList();
            }
            finally
            {
                _fileMutex.ReleaseMutex();
            }
        }

        public void GenerateFileManifest(string extractDir, string targetPath)
        {
            WithFileLock(history =>
            {
                var record = history.records.LastOrDefault(r => r.targetPath == targetPath && r.status == "success");
                if (record == null) return;

                string payloadDir = Path.Combine(extractDir, "payload");
                if (!Directory.Exists(payloadDir)) return;

                var manifest = new List<string>();
                CollectFiles(payloadDir, payloadDir, targetPath, manifest);
                record.fileManifest = manifest;
            });
        }

        private static void CollectFiles(string rootSourceDir, string sourceDir, string targetBase, List<string> manifest)
        {
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string relative = Path.GetRelativePath(rootSourceDir, file);
                string targetFile = Path.Combine(targetBase, relative);
                manifest.Add(targetFile);
            }
            foreach (var subDir in Directory.GetDirectories(sourceDir))
            {
                CollectFiles(rootSourceDir, subDir, targetBase, manifest);
            }
        }
    }
}
