using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace VHDMounter
{
    internal sealed class DisplayMonitorBounds : IEquatable<DisplayMonitorBounds>
    {
        public DisplayMonitorBounds(string deviceName, int left, int top, int width, int height, bool isPrimary)
        {
            if (string.IsNullOrWhiteSpace(deviceName))
            {
                throw new ArgumentException("Device name is required.", nameof(deviceName));
            }

            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            DeviceName = deviceName;
            Left = left;
            Top = top;
            Width = width;
            Height = height;
            IsPrimary = isPrimary;
        }

        public string DeviceName { get; }

        public int Left { get; }

        public int Top { get; }

        public int Width { get; }

        public int Height { get; }

        public bool IsPrimary { get; }

        public bool IsPortrait => Height > Width;

        public int Right => Left + Width;

        public int Bottom => Top + Height;

        public bool Equals(DisplayMonitorBounds other)
        {
            return other != null &&
                   string.Equals(DeviceName, other.DeviceName, StringComparison.OrdinalIgnoreCase) &&
                   Left == other.Left &&
                   Top == other.Top &&
                   Width == other.Width &&
                   Height == other.Height &&
                   IsPrimary == other.IsPrimary;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as DisplayMonitorBounds);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(DeviceName),
                Left,
                Top,
                Width,
                Height,
                IsPrimary);
        }

        public override string ToString()
        {
            return $"{DeviceName} {Left},{Top} {Width}x{Height} Primary={IsPrimary}";
        }
    }

    internal static class DisplayMirrorTargetSelector
    {
        public static DisplayMonitorBounds SelectSecondary(
            IReadOnlyList<DisplayMonitorBounds> monitors,
            DisplayMonitorBounds mainMonitor)
        {
            if (monitors == null || monitors.Count == 0 || mainMonitor == null)
            {
                return null;
            }

            return monitors
                .Where(monitor => monitor != null &&
                                  !string.Equals(monitor.DeviceName, mainMonitor.DeviceName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(monitor => monitor.Width == mainMonitor.Width && monitor.Height == mainMonitor.Height)
                .ThenByDescending(monitor => monitor.IsPortrait == mainMonitor.IsPortrait)
                .ThenBy(monitor => DistanceSquared(monitor, mainMonitor))
                .ThenBy(monitor => monitor.DeviceName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private static long DistanceSquared(DisplayMonitorBounds left, DisplayMonitorBounds right)
        {
            var leftCenterX = left.Left + left.Width / 2L;
            var leftCenterY = left.Top + left.Height / 2L;
            var rightCenterX = right.Left + right.Width / 2L;
            var rightCenterY = right.Top + right.Height / 2L;
            var deltaX = leftCenterX - rightCenterX;
            var deltaY = leftCenterY - rightCenterY;
            return deltaX * deltaX + deltaY * deltaY;
        }
    }

    internal interface IDisplayMonitorProvider
    {
        IReadOnlyList<DisplayMonitorBounds> GetMonitors();

        DisplayMonitorBounds GetMonitorForWindow(IntPtr windowHandle);
    }

    internal sealed class NativeDisplayMonitorProvider : IDisplayMonitorProvider
    {
        private const uint MonitorInfofPrimary = 1;
        private const uint MonitorDefaultToNearest = 2;

        public IReadOnlyList<DisplayMonitorBounds> GetMonitors()
        {
            var monitors = new List<DisplayMonitorBounds>();
            NativeMethods.MonitorEnumProc callback = (monitorHandle, _, _, _) =>
            {
                if (TryGetMonitorBounds(monitorHandle, out var monitor))
                {
                    monitors.Add(monitor);
                }

                return true;
            };

            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
            return monitors;
        }

        public DisplayMonitorBounds GetMonitorForWindow(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
            {
                return null;
            }

            var monitorHandle = NativeMethods.MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
            return TryGetMonitorBounds(monitorHandle, out var monitor) ? monitor : null;
        }

        private static bool TryGetMonitorBounds(IntPtr monitorHandle, out DisplayMonitorBounds monitor)
        {
            monitor = null;
            if (monitorHandle == IntPtr.Zero)
            {
                return false;
            }

            var info = new NativeMethods.MonitorInfoEx
            {
                CbSize = (uint)Marshal.SizeOf<NativeMethods.MonitorInfoEx>(),
                DeviceName = string.Empty,
            };
            if (!NativeMethods.GetMonitorInfo(monitorHandle, ref info) || string.IsNullOrWhiteSpace(info.DeviceName))
            {
                return false;
            }

            monitor = new DisplayMonitorBounds(
                info.DeviceName,
                info.Monitor.Left,
                info.Monitor.Top,
                info.Monitor.Right - info.Monitor.Left,
                info.Monitor.Bottom - info.Monitor.Top,
                (info.Flags & MonitorInfofPrimary) != 0);
            return true;
        }
    }
}
