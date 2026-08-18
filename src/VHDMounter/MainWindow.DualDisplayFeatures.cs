using System;
using System.Diagnostics;
using System.Windows.Interop;
using Microsoft.Win32;

namespace VHDMounter
{
    public partial class MainWindow
    {
        private DualDisplayMirrorCoordinator secondaryDisplayMirrorCoordinator;

        private void InitializeDualDisplayMirror()
        {
            secondaryDisplayMirrorCoordinator = new DualDisplayMirrorCoordinator(
                new NativeDisplayMonitorProvider(),
                () => new SecondaryDisplayMirrorWindow(DisplaySurfaceRoot));

            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            Loaded += OnDisplayLoaded;
            ContentRendered += OnDisplayContentRendered;
            StateChanged += OnDisplayStateChanged;
            IsVisibleChanged += OnDisplayVisibilityChanged;
        }

        private void DisposeDualDisplayMirror()
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            Loaded -= OnDisplayLoaded;
            ContentRendered -= OnDisplayContentRendered;
            StateChanged -= OnDisplayStateChanged;
            IsVisibleChanged -= OnDisplayVisibilityChanged;

            secondaryDisplayMirrorCoordinator?.Dispose();
            secondaryDisplayMirrorCoordinator = null;
        }

        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            Dispatcher.InvokeAsync(RefreshSecondaryDisplayMirror);
        }

        private void OnDisplayLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            RefreshSecondaryDisplayMirror();
        }

        private void OnDisplayContentRendered(object sender, EventArgs e)
        {
            RefreshSecondaryDisplayMirror();
        }

        private void OnDisplayStateChanged(object sender, EventArgs e)
        {
            RefreshSecondaryDisplayMirror();
        }

        private void OnDisplayVisibilityChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
        {
            RefreshSecondaryDisplayMirror();
        }

        private void RefreshSecondaryDisplayMirror()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.InvokeAsync(RefreshSecondaryDisplayMirror);
                return;
            }

            if (secondaryDisplayMirrorCoordinator == null || !IsLoaded)
            {
                return;
            }

            try
            {
                var handle = new WindowInteropHelper(this).EnsureHandle();
                var mainSurfaceVisible = IsVisible &&
                                         WindowState != System.Windows.WindowState.Minimized;
#if FEATURE_HID_MENU
                mainSurfaceVisible = !isWindowHiddenForGame && mainSurfaceVisible;
#endif
                secondaryDisplayMirrorCoordinator.Refresh(handle, mainSurfaceVisible);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"DISPLAY_MIRROR_REFRESH_FAILED: {ex.Message}");
            }
        }
    }
}
