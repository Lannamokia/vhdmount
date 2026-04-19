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
    }

    internal enum ServiceMenuItemKind
    {
        RestartSystem = 0,
        ShutdownSystem,
        SystemInfo,
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