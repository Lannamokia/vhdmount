using System;
using System.Collections.Generic;

namespace VHDMounter.SoftwareDeploy
{
    public class DeployPreCheck
    {
        public int minDiskSpaceMB { get; set; }
        public List<string> stopProcesses { get; set; } = new();
    }

    public class DeployManifest
    {
        public string name { get; set; } = string.Empty;
        public string version { get; set; } = string.Empty;
        public string type { get; set; } = string.Empty;
        public string targetPath { get; set; } = string.Empty;
        public string signer { get; set; } = string.Empty;
        public string createdAt { get; set; } = string.Empty;
        public string expiresAt { get; set; } = string.Empty;
        public string installScript { get; set; } = string.Empty;
        public string uninstallScript { get; set; } = "uninstall.ps1";
        public bool requiresAdmin { get; set; }
        public DeployPreCheck preCheck { get; set; } = new();

        public bool IsSoftwareDeploy => string.Equals(type, "software-deploy", StringComparison.OrdinalIgnoreCase);
        public bool IsFileDeploy => string.Equals(type, "file-deploy", StringComparison.OrdinalIgnoreCase);
    }
}
