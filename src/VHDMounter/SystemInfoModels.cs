using System;
using System.Collections.Generic;

namespace VHDMounter
{
    internal sealed class CpuInfo
    {
        public double? UsagePercent { get; set; }

        public double? TemperatureCelsius { get; set; }
    }

    internal sealed class MemoryInfo
    {
        public ulong TotalBytes { get; set; }

        public ulong UsedBytes { get; set; }

        public double? UsagePercent { get; set; }
    }

    internal sealed class GpuInfo
    {
        public bool HasDedicatedGpu { get; set; }

        public string Name { get; set; } = string.Empty;

        public ulong DedicatedVideoMemoryBytes { get; set; }

        public double? UsagePercent { get; set; }

        public double? TemperatureCelsius { get; set; }

        public string DriverVersion { get; set; } = "不可用";
    }

    internal sealed class DriveUsageInfo
    {
        public string Name { get; set; } = string.Empty;

        public string VolumeLabel { get; set; } = string.Empty;

        public ulong TotalBytes { get; set; }

        public ulong UsedBytes { get; set; }

        public ulong FreeBytes { get; set; }

        public double UsagePercent { get; set; }
    }

    internal sealed class SystemInfoSnapshot
    {
        public static SystemInfoSnapshot Empty { get; } = new SystemInfoSnapshot();

        public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.Now;

        public CpuInfo Cpu { get; set; } = new CpuInfo();

        public MemoryInfo Memory { get; set; } = new MemoryInfo();

        public GpuInfo Gpu { get; set; } = new GpuInfo();

        public IReadOnlyList<DriveUsageInfo> Drives { get; set; } = Array.Empty<DriveUsageInfo>();
    }
}