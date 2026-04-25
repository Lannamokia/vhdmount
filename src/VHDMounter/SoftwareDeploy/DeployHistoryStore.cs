#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

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
        public string status { get; set; } = "success"; // success / failed / uninstalled
        public string targetPath { get; set; } = string.Empty;
        public List<string> fileManifest { get; set; } = new();
    }

    public class DeployHistory
    {
        public List<DeployRecord> records { get; set; } = new();
    }

    public class DeployHistoryStore
    {
        private readonly string _filePath;
        private readonly object _lock = new();

        public DeployHistoryStore(string baseDir)
        {
            _filePath = Path.Combine(baseDir, "deploy_history.json");
        }

        private DeployHistory Load()
        {
            lock (_lock)
            {
                if (!File.Exists(_filePath))
                    return new DeployHistory();
                try
                {
                    var json = File.ReadAllText(_filePath);
                    var history = JsonSerializer.Deserialize<DeployHistory>(json);
                    return history ?? new DeployHistory();
                }
                catch { return new DeployHistory(); }
            }
        }

        private void Save(DeployHistory history)
        {
            lock (_lock)
            {
                try
                {
                    var dir = Path.GetDirectoryName(_filePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    var json = JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_filePath, json);
                }
                catch { }
            }
        }

        public void AddRecord(DeployRecord record)
        {
            var history = Load();
            // 同名包新版本覆盖旧版本记录
            history.records.RemoveAll(r => r.name == record.name && r.status != "uninstalled");
            history.records.Add(record);
            Save(history);
        }

        public void UpdateRecordStatus(string recordId, string status)
        {
            var history = Load();
            var record = history.records.FirstOrDefault(r => r.recordId == recordId);
            if (record != null)
            {
                record.status = status;
                if (status == "uninstalled")
                {
                    record.fileManifest.Clear();
                }
                Save(history);
            }
        }

        public DeployRecord? FindRecord(string recordId)
        {
            var history = Load();
            return history.records.FirstOrDefault(r => r.recordId == recordId);
        }

        public DeployRecord? FindRecordByName(string name)
        {
            var history = Load();
            return history.records
                .Where(r => r.name == name && r.status != "uninstalled")
                .OrderByDescending(r => r.deployedAt)
                .FirstOrDefault();
        }

        public List<DeployRecord> GetAllRecords()
        {
            return Load().records;
        }

        public List<DeployRecord> GetRecordsForSync()
        {
            return Load().records.ToList();
        }

        public void GenerateFileManifest(string extractDir, string targetPath)
        {
            var history = Load();
            var record = history.records.LastOrDefault(r => r.targetPath == targetPath && r.status == "success");
            if (record == null) return;

            string payloadDir = Path.Combine(extractDir, "payload");
            if (!Directory.Exists(payloadDir)) return;

            var manifest = new List<string>();
            CollectFiles(payloadDir, targetPath, manifest);
            record.fileManifest = manifest;
            Save(history);
        }

        private static void CollectFiles(string sourceDir, string targetBase, List<string> manifest)
        {
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string relative = Path.GetRelativePath(sourceDir, file);
                string targetFile = Path.Combine(targetBase, relative);
                manifest.Add(targetFile);
            }
            foreach (var subDir in Directory.GetDirectories(sourceDir))
            {
                CollectFiles(subDir, targetBase, manifest);
            }
        }
    }
}
