using System;

namespace VHDMounter
{
    internal interface ISecondaryDisplayMirrorWindow : IDisposable
    {
        bool IsVisible { get; }

        DisplayMonitorBounds CurrentMonitor { get; }

        void ShowOn(DisplayMonitorBounds monitor);

        void Hide();
    }

    internal sealed class DualDisplayMirrorCoordinator : IDisposable
    {
        private readonly IDisplayMonitorProvider monitorProvider;
        private readonly Func<ISecondaryDisplayMirrorWindow> mirrorWindowFactory;
        private ISecondaryDisplayMirrorWindow mirrorWindow;
        private DisplayMonitorBounds currentMonitor;
        private bool disposed;

        public DualDisplayMirrorCoordinator(
            IDisplayMonitorProvider monitorProvider,
            Func<ISecondaryDisplayMirrorWindow> mirrorWindowFactory)
        {
            this.monitorProvider = monitorProvider ?? throw new ArgumentNullException(nameof(monitorProvider));
            this.mirrorWindowFactory = mirrorWindowFactory ?? throw new ArgumentNullException(nameof(mirrorWindowFactory));
        }

        public void Refresh(IntPtr sourceWindowHandle, bool shouldShow)
        {
            ThrowIfDisposed();

            if (!shouldShow)
            {
                HideMirror();
                return;
            }

            var mainMonitor = monitorProvider.GetMonitorForWindow(sourceWindowHandle);
            var targetMonitor = DisplayMirrorTargetSelector.SelectSecondary(monitorProvider.GetMonitors(), mainMonitor);
            if (targetMonitor == null)
            {
                HideMirror();
                return;
            }

            mirrorWindow ??= mirrorWindowFactory();
            if (mirrorWindow == null)
            {
                throw new InvalidOperationException("The secondary display mirror window factory returned null.");
            }

            if (!mirrorWindow.IsVisible || !targetMonitor.Equals(currentMonitor))
            {
                mirrorWindow.ShowOn(targetMonitor);
                currentMonitor = targetMonitor;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            mirrorWindow?.Dispose();
            mirrorWindow = null;
            currentMonitor = null;
        }

        private void HideMirror()
        {
            if (mirrorWindow?.IsVisible == true)
            {
                mirrorWindow.Hide();
            }

            currentMonitor = null;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(DualDisplayMirrorCoordinator));
            }
        }
    }
}
