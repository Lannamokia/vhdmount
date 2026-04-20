using System.Collections.Generic;

namespace VHDMounter
{
    internal enum OverlayState
    {
        None = 0,
        ServiceMenuHome,
        ConfirmRestart,
        ConfirmShutdown,
        SystemInfo,
        SystemSettingsHome,
        NetworkAdapterSelect,
        NetworkModeSelect,
        NetworkDhcpConfirm,
        NetworkIpv4Edit,
        NetworkDiscardConfirm,
        AudioSettings,
    }

    internal enum ServiceMenuItemKind
    {
        RestartSystem = 0,
        ShutdownSystem,
        SystemInfo,
        SystemSettings,
    }

    internal enum SystemSettingsItemKind
    {
        NetworkSettings = 0,
        AudioSettings,
    }

    internal enum NetworkModeOptionKind
    {
        Dhcp = 0,
        StaticIpv4,
    }

    internal sealed class ServiceMenuItem
    {
        public ServiceMenuItem(ServiceMenuItemKind kind, string title, string description)
        {
            Kind = kind;
            Title = title;
            Description = description;
        }

        public ServiceMenuItemKind Kind { get; }

        public string Title { get; }

        public string Description { get; }
    }

    internal sealed class OverlayDisplayLine
    {
        public OverlayDisplayLine(string title, string value = "", string detail = "", bool isSelected = false)
        {
            Title = title ?? string.Empty;
            Value = value ?? string.Empty;
            Detail = detail ?? string.Empty;
            IsSelected = isSelected;
        }

        public string Title { get; }

        public string Value { get; }

        public string Detail { get; }

        public bool IsSelected { get; }
    }

    internal sealed class SystemSettingsItem
    {
        public SystemSettingsItem(SystemSettingsItemKind kind, string title, string description)
        {
            Kind = kind;
            Title = title ?? string.Empty;
            Description = description ?? string.Empty;
        }

        public SystemSettingsItemKind Kind { get; }

        public string Title { get; }

        public string Description { get; }
    }

    internal sealed class NetworkModeOption
    {
        public NetworkModeOption(NetworkModeOptionKind kind, string title, string description)
        {
            Kind = kind;
            Title = title ?? string.Empty;
            Description = description ?? string.Empty;
        }

        public NetworkModeOptionKind Kind { get; }

        public string Title { get; }

        public string Description { get; }
    }

    internal sealed class SystemInfoPage
    {
        public SystemInfoPage(string title, string subtitle, IReadOnlyList<OverlayDisplayLine> lines)
        {
            Title = title ?? string.Empty;
            Subtitle = subtitle ?? string.Empty;
            Lines = lines ?? new List<OverlayDisplayLine>();
        }

        public string Title { get; }

        public string Subtitle { get; }

        public IReadOnlyList<OverlayDisplayLine> Lines { get; }
    }
}