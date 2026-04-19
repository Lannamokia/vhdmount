using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;
using Vortice.DXGI;
using static Vortice.DXGI.DXGI;

namespace VHDMounter
{
    internal sealed class SystemInfoService : IDisposable
    {
        private readonly Computer computer;
        private readonly object lifecycleSync = new object();
        private CancellationTokenSource refreshCts;
        private Task refreshTask;
        private CpuTimesSample lastCpuSample;
        private bool disposed;

        public SystemInfoService()
        {
            computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
            };

            try
            {
                computer.Open();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"SYSINFO_OPEN_FAILED: {ex}");
            }
        }

        public event Action<SystemInfoSnapshot> SnapshotUpdated;

        public SystemInfoSnapshot LatestSnapshot { get; private set; } = SystemInfoSnapshot.Empty;

        public void Start()
        {
            lock (lifecycleSync)
            {
                ThrowIfDisposed();
                if (refreshTask != null && !refreshTask.IsCompleted)
                {
                    return;
                }

                refreshCts = new CancellationTokenSource();
                refreshTask = Task.Run(() => RefreshLoopAsync(refreshCts.Token));
            }
        }

        public void Stop()
        {
            CancellationTokenSource cts;

            lock (lifecycleSync)
            {
                cts = refreshCts;
                refreshCts = null;
                refreshTask = null;
            }

            if (cts != null)
            {
                try
                {
                    cts.Cancel();
                }
                catch
                {
                }

                cts.Dispose();
            }
        }

        public async Task<SystemInfoSnapshot> RefreshNowAsync()
        {
            ThrowIfDisposed();
            var snapshot = await Task.Run(BuildSnapshot);
            PublishSnapshot(snapshot);
            return snapshot;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Stop();

            try
            {
                computer.Close();
            }
            catch
            {
            }
        }

        private async Task RefreshLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var snapshot = await Task.Run(BuildSnapshot, token);
                    PublishSnapshot(snapshot);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"SYSINFO_REFRESH_FAILED: {ex}");
                }

                try
                {
                    await Task.Delay(1000, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private SystemInfoSnapshot BuildSnapshot()
        {
            RefreshHardware();

            var dedicatedAdapter = SelectDedicatedAdapter();
            var videoControllers = QueryVideoControllers();
            var matchedController = MatchVideoController(dedicatedAdapter, videoControllers);
            var gpuSensors = ReadGpuSensors(dedicatedAdapter?.Name);

            return new SystemInfoSnapshot
            {
                CapturedAt = DateTimeOffset.Now,
                Cpu = new CpuInfo
                {
                    UsagePercent = ReadCpuUsage(),
                    TemperatureCelsius = ReadCpuTemperature(),
                },
                Memory = ReadMemoryInfo(),
                Gpu = BuildGpuInfo(dedicatedAdapter, matchedController, gpuSensors),
                Drives = ReadDriveInfos(),
            };
        }

        private void PublishSnapshot(SystemInfoSnapshot snapshot)
        {
            LatestSnapshot = snapshot ?? SystemInfoSnapshot.Empty;

            try
            {
                SnapshotUpdated?.Invoke(LatestSnapshot);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"SYSINFO_CALLBACK_FAILED: {ex}");
            }
        }

        private void RefreshHardware()
        {
            try
            {
                foreach (var hardware in computer.Hardware)
                {
                    UpdateHardwareRecursive(hardware);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"SYSINFO_HARDWARE_REFRESH_FAILED: {ex}");
            }
        }

        private static void UpdateHardwareRecursive(IHardware hardware)
        {
            if (hardware == null)
            {
                return;
            }

            hardware.Update();
            foreach (var subHardware in hardware.SubHardware)
            {
                UpdateHardwareRecursive(subHardware);
            }
        }

        private double? ReadCpuUsage()
        {
            if (!NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
            {
                return null;
            }

            var currentSample = new CpuTimesSample
            {
                Idle = ToUInt64(idle),
                Kernel = ToUInt64(kernel),
                User = ToUInt64(user),
            };

            if (lastCpuSample == null)
            {
                lastCpuSample = currentSample;
                return null;
            }

            var deltaIdle = currentSample.Idle - lastCpuSample.Idle;
            var deltaKernel = currentSample.Kernel - lastCpuSample.Kernel;
            var deltaUser = currentSample.User - lastCpuSample.User;
            var deltaTotal = deltaKernel + deltaUser;
            lastCpuSample = currentSample;

            if (deltaTotal == 0)
            {
                return null;
            }

            var deltaUsed = deltaTotal > deltaIdle ? deltaTotal - deltaIdle : 0;
            return Math.Clamp(deltaUsed * 100.0 / deltaTotal, 0.0, 100.0);
        }

        private double? ReadCpuTemperature()
        {
            double? packageTemperature = null;
            double? fallbackTemperature = null;

            foreach (var hardware in EnumerateHardware())
            {
                if (hardware.HardwareType != HardwareType.Cpu)
                {
                    continue;
                }

                foreach (var sensor in hardware.Sensors)
                {
                    if (sensor.SensorType != SensorType.Temperature || !sensor.Value.HasValue)
                    {
                        continue;
                    }

                    if (sensor.Name.IndexOf("package", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        packageTemperature = sensor.Value.Value;
                    }
                    else if (!fallbackTemperature.HasValue || sensor.Value.Value > fallbackTemperature.Value)
                    {
                        fallbackTemperature = sensor.Value.Value;
                    }
                }
            }

            return packageTemperature ?? fallbackTemperature;
        }

        private static MemoryInfo ReadMemoryInfo()
        {
            var memoryStatus = new NativeMethods.MEMORYSTATUSEX
            {
                dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>(),
            };

            if (!NativeMethods.GlobalMemoryStatusEx(ref memoryStatus))
            {
                return new MemoryInfo();
            }

            var usedBytes = memoryStatus.ullTotalPhys > memoryStatus.ullAvailPhys
                ? memoryStatus.ullTotalPhys - memoryStatus.ullAvailPhys
                : 0;

            return new MemoryInfo
            {
                TotalBytes = memoryStatus.ullTotalPhys,
                UsedBytes = usedBytes,
                UsagePercent = memoryStatus.ullTotalPhys == 0
                    ? null
                    : Math.Clamp(usedBytes * 100.0 / memoryStatus.ullTotalPhys, 0.0, 100.0),
            };
        }

        private static GpuInfo BuildGpuInfo(DedicatedAdapterCandidate dedicatedAdapter, VideoControllerSnapshot controller, GpuSensorSnapshot sensors)
        {
            if (dedicatedAdapter == null)
            {
                return new GpuInfo
                {
                    HasDedicatedGpu = false,
                    DriverVersion = "未检测到独立显卡",
                };
            }

            return new GpuInfo
            {
                HasDedicatedGpu = true,
                Name = !string.IsNullOrWhiteSpace(controller?.Name) ? controller.Name : dedicatedAdapter.Name,
                DedicatedVideoMemoryBytes = dedicatedAdapter.DedicatedVideoMemoryBytes,
                UsagePercent = sensors?.UsagePercent,
                TemperatureCelsius = sensors?.TemperatureCelsius,
                DriverVersion = !string.IsNullOrWhiteSpace(controller?.DriverVersion) ? controller.DriverVersion : "不可用",
            };
        }

        private static IReadOnlyList<DriveUsageInfo> ReadDriveInfos()
        {
            var drives = new List<DriveUsageInfo>();

            foreach (var drive in DriveInfo.GetDrives().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (!drive.IsReady)
                    {
                        continue;
                    }

                    if (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable)
                    {
                        continue;
                    }

                    var usedBytes = drive.TotalSize > drive.TotalFreeSpace
                        ? (ulong)(drive.TotalSize - drive.TotalFreeSpace)
                        : 0;

                    drives.Add(new DriveUsageInfo
                    {
                        Name = drive.Name.TrimEnd('\\'),
                        VolumeLabel = drive.VolumeLabel ?? string.Empty,
                        TotalBytes = (ulong)drive.TotalSize,
                        UsedBytes = usedBytes,
                        FreeBytes = (ulong)drive.AvailableFreeSpace,
                        UsagePercent = drive.TotalSize == 0
                            ? 0
                            : Math.Clamp((drive.TotalSize - drive.TotalFreeSpace) * 100.0 / drive.TotalSize, 0.0, 100.0),
                    });
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"SYSINFO_DRIVE_READ_FAILED: {drive.Name} {ex.Message}");
                }
            }

            return drives;
        }

        private DedicatedAdapterCandidate SelectDedicatedAdapter()
        {
            try
            {
                using var factory = CreateDXGIFactory1<IDXGIFactory6>();
                var candidates = new List<DedicatedAdapterCandidate>();

                for (var index = 0; ; index++)
                {
                    var result = factory.EnumAdapterByGpuPreference((uint)index, GpuPreference.HighPerformance, out IDXGIAdapter1 adapter);
                    if (result.Failure || adapter == null)
                    {
                        break;
                    }

                    using (adapter)
                    {
                        var description = adapter.Description1;
                        if ((description.Flags & AdapterFlags.Software) != 0)
                        {
                            continue;
                        }

                        var dedicatedBytes = Convert.ToUInt64(description.DedicatedVideoMemory);
                        if (dedicatedBytes == 0)
                        {
                            continue;
                        }

                        candidates.Add(new DedicatedAdapterCandidate
                        {
                            Name = (description.Description ?? string.Empty).Trim(),
                            DedicatedVideoMemoryBytes = dedicatedBytes,
                        });
                    }
                }

                return candidates
                    .OrderByDescending(candidate => candidate.DedicatedVideoMemoryBytes)
                    .FirstOrDefault();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"SYSINFO_DXGI_ENUM_FAILED: {ex}");
                return null;
            }
        }

        private static List<VideoControllerSnapshot> QueryVideoControllers()
        {
            var controllers = new List<VideoControllerSnapshot>();

            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, DriverVersion, AdapterRAM, PNPDeviceID, CurrentHorizontalResolution, CurrentVerticalResolution FROM Win32_VideoController");
                foreach (ManagementObject obj in searcher.Get())
                {
                    controllers.Add(new VideoControllerSnapshot
                    {
                        Name = obj["Name"]?.ToString() ?? string.Empty,
                        DriverVersion = obj["DriverVersion"]?.ToString() ?? string.Empty,
                        PnpDeviceId = obj["PNPDeviceID"]?.ToString() ?? string.Empty,
                        AdapterRamBytes = ConvertToUInt64(obj["AdapterRAM"]),
                        IsActive = ConvertToInt32(obj["CurrentHorizontalResolution"]) > 0 &&
                                   ConvertToInt32(obj["CurrentVerticalResolution"]) > 0,
                    });
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"SYSINFO_VIDEO_WMI_FAILED: {ex}");
            }

            return controllers;
        }

        private static VideoControllerSnapshot MatchVideoController(DedicatedAdapterCandidate adapter, IReadOnlyList<VideoControllerSnapshot> controllers)
        {
            if (adapter == null || controllers == null || controllers.Count == 0)
            {
                return null;
            }

            var normalizedAdapterName = NormalizeName(adapter.Name);
            return controllers
                .Select(controller => new
                {
                    Controller = controller,
                    Score = ScoreControllerMatch(normalizedAdapterName, controller),
                })
                .Where(item => item.Score > 0)
                .OrderByDescending(item => item.Score)
                .ThenByDescending(item => item.Controller.IsActive)
                .ThenByDescending(item => item.Controller.AdapterRamBytes)
                .Select(item => item.Controller)
                .FirstOrDefault();
        }

        private GpuSensorSnapshot ReadGpuSensors(string adapterName)
        {
            if (string.IsNullOrWhiteSpace(adapterName))
            {
                return new GpuSensorSnapshot();
            }

            var normalizedAdapterName = NormalizeName(adapterName);
            var bestHardware = EnumerateHardware()
                .Where(hardware => hardware.HardwareType == HardwareType.GpuNvidia ||
                                   hardware.HardwareType == HardwareType.GpuAmd ||
                                   hardware.HardwareType == HardwareType.GpuIntel)
                .Select(hardware => new
                {
                    Hardware = hardware,
                    Score = ScoreHardwareMatch(normalizedAdapterName, hardware.Name),
                })
                .OrderByDescending(item => item.Score)
                .FirstOrDefault();

            if (bestHardware == null || bestHardware.Score <= 0)
            {
                return new GpuSensorSnapshot();
            }

            var temperature = bestHardware.Hardware.Sensors
                .Where(sensor => sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue)
                .OrderByDescending(sensor => sensor.Name.IndexOf("core", StringComparison.OrdinalIgnoreCase) >= 0)
                .ThenByDescending(sensor => sensor.Value.Value)
                .Select(sensor => (double?)sensor.Value.Value)
                .FirstOrDefault();

            var usageSensor = bestHardware.Hardware.Sensors
                .Where(sensor => sensor.SensorType == SensorType.Load && sensor.Value.HasValue)
                .OrderByDescending(sensor => sensor.Name.IndexOf("total", StringComparison.OrdinalIgnoreCase) >= 0)
                .ThenByDescending(sensor => sensor.Name.IndexOf("core", StringComparison.OrdinalIgnoreCase) >= 0)
                .ThenByDescending(sensor => sensor.Value.Value)
                .FirstOrDefault();

            return new GpuSensorSnapshot
            {
                UsagePercent = usageSensor?.Value,
                TemperatureCelsius = temperature,
            };
        }

        private IEnumerable<IHardware> EnumerateHardware()
        {
            foreach (var hardware in computer.Hardware)
            {
                foreach (var item in EnumerateHardwareRecursive(hardware))
                {
                    yield return item;
                }
            }
        }

        private static IEnumerable<IHardware> EnumerateHardwareRecursive(IHardware hardware)
        {
            if (hardware == null)
            {
                yield break;
            }

            yield return hardware;

            foreach (var subHardware in hardware.SubHardware)
            {
                foreach (var item in EnumerateHardwareRecursive(subHardware))
                {
                    yield return item;
                }
            }
        }

        private static ulong ToUInt64(NativeMethods.FILETIME fileTime)
        {
            return ((ulong)fileTime.dwHighDateTime << 32) | fileTime.dwLowDateTime;
        }

        private static ulong ConvertToUInt64(object value)
        {
            if (value == null)
            {
                return 0;
            }

            try
            {
                return Convert.ToUInt64(value);
            }
            catch
            {
                return 0;
            }
        }

        private static int ConvertToInt32(object value)
        {
            if (value == null)
            {
                return 0;
            }

            try
            {
                return Convert.ToInt32(value);
            }
            catch
            {
                return 0;
            }
        }

        private static int ScoreControllerMatch(string normalizedAdapterName, VideoControllerSnapshot controller)
        {
            var normalizedControllerName = NormalizeName(controller.Name);
            if (string.IsNullOrWhiteSpace(normalizedAdapterName) || string.IsNullOrWhiteSpace(normalizedControllerName))
            {
                return 0;
            }

            var score = 0;
            if (string.Equals(normalizedAdapterName, normalizedControllerName, StringComparison.OrdinalIgnoreCase))
            {
                score += 500;
            }
            else if (normalizedAdapterName.Contains(normalizedControllerName, StringComparison.OrdinalIgnoreCase) ||
                     normalizedControllerName.Contains(normalizedAdapterName, StringComparison.OrdinalIgnoreCase))
            {
                score += 350;
            }

            var adapterTokens = normalizedAdapterName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var controllerTokens = normalizedControllerName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            score += adapterTokens.Intersect(controllerTokens, StringComparer.OrdinalIgnoreCase).Count() * 40;
            if (controller.IsActive)
            {
                score += 80;
            }

            return score;
        }

        private static int ScoreHardwareMatch(string normalizedAdapterName, string hardwareName)
        {
            var normalizedHardwareName = NormalizeName(hardwareName);
            if (string.IsNullOrWhiteSpace(normalizedAdapterName) || string.IsNullOrWhiteSpace(normalizedHardwareName))
            {
                return 0;
            }

            if (string.Equals(normalizedAdapterName, normalizedHardwareName, StringComparison.OrdinalIgnoreCase))
            {
                return 500;
            }

            var score = 0;
            if (normalizedAdapterName.Contains(normalizedHardwareName, StringComparison.OrdinalIgnoreCase) ||
                normalizedHardwareName.Contains(normalizedAdapterName, StringComparison.OrdinalIgnoreCase))
            {
                score += 320;
            }

            var adapterTokens = normalizedAdapterName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var hardwareTokens = normalizedHardwareName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            score += adapterTokens.Intersect(hardwareTokens, StringComparer.OrdinalIgnoreCase).Count() * 45;
            return score;
        }

        private static string NormalizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var chars = value
                .ToLowerInvariant()
                .Where(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch))
                .ToArray();
            return new string(chars).Trim();
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(SystemInfoService));
            }
        }

        private sealed class CpuTimesSample
        {
            public ulong Idle { get; set; }

            public ulong Kernel { get; set; }

            public ulong User { get; set; }
        }

        private sealed class DedicatedAdapterCandidate
        {
            public string Name { get; set; } = string.Empty;

            public ulong DedicatedVideoMemoryBytes { get; set; }
        }

        private sealed class VideoControllerSnapshot
        {
            public string Name { get; set; } = string.Empty;

            public string DriverVersion { get; set; } = string.Empty;

            public string PnpDeviceId { get; set; } = string.Empty;

            public ulong AdapterRamBytes { get; set; }

            public bool IsActive { get; set; }
        }

        private sealed class GpuSensorSnapshot
        {
            public double? UsagePercent { get; set; }

            public double? TemperatureCelsius { get; set; }
        }
    }
}