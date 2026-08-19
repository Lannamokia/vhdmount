using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace VHDMounter
{
    internal sealed class SecondaryDisplayMirrorWindow : Window, ISecondaryDisplayMirrorWindow
    {
        private readonly Visual sourceVisual;
        private readonly Image mirrorSurface;
        private readonly DispatcherTimer refreshTimer;
        private bool renderFailureLogged;
        private bool disposed;

        public SecondaryDisplayMirrorWindow(Visual sourceVisual)
        {
            if (sourceVisual == null)
            {
                throw new ArgumentNullException(nameof(sourceVisual));
            }

            this.sourceVisual = sourceVisual;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            IsHitTestVisible = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Topmost = true;
            Background = Brushes.Black;
            Opacity = 0;

            mirrorSurface = new Image
            {
                Stretch = Stretch.Fill,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                IsHitTestVisible = false,
                Focusable = false,
            };

            var mirrorHost = new Grid
            {
                Background = Brushes.White,
                IsHitTestVisible = false,
            };
            mirrorHost.Children.Add(mirrorSurface);
            Content = mirrorHost;

            refreshTimer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(66),
            };
            refreshTimer.Tick += OnRefreshTimerTick;
        }

        public DisplayMonitorBounds CurrentMonitor { get; private set; }

        bool ISecondaryDisplayMirrorWindow.IsVisible => base.IsVisible;

        public void ShowOn(DisplayMonitorBounds monitor)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(SecondaryDisplayMirrorWindow));
            }

            if (monitor == null)
            {
                throw new ArgumentNullException(nameof(monitor));
            }

            CurrentMonitor = monitor;
            if (!IsVisible)
            {
                Show();
            }

            var hwnd = new WindowInteropHelper(this).EnsureHandle();
            var extendedStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExstyle).ToInt64();
            extendedStyle |= NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow;
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExstyle, new IntPtr(extendedStyle));
            NativeMethods.SetWindowPos(
                hwnd,
                NativeMethods.HwndTopmost,
                monitor.Left,
                monitor.Top,
                monitor.Width,
                monitor.Height,
                NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
            Opacity = 1;

            RefreshSnapshot();
            refreshTimer.Start();
        }

        void ISecondaryDisplayMirrorWindow.Hide()
        {
            HideMirror();
        }

        private void HideMirror()
        {
            if (disposed)
            {
                return;
            }

            if (base.IsVisible)
            {
                base.Hide();
            }

            refreshTimer.Stop();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            refreshTimer.Stop();
            refreshTimer.Tick -= OnRefreshTimerTick;
            HideMirror();

            Close();
            mirrorSurface.Source = null;
            CurrentMonitor = null;
        }

        private void OnRefreshTimerTick(object sender, EventArgs e)
        {
            if (!disposed && base.IsVisible)
            {
                RefreshSnapshot();
            }
        }

        private void RefreshSnapshot()
        {
            if (disposed || sourceVisual == null ||
                (sourceVisual is UIElement sourceElement && !sourceElement.IsVisible))
            {
                return;
            }

            if (!TryGetSourceSize(out var width, out var height))
            {
                return;
            }

            try
            {
                var pixelWidth = Math.Max(1, (int)Math.Ceiling(width));
                var pixelHeight = Math.Max(1, (int)Math.Ceiling(height));
                // RenderTargetBitmap.Render appends the visual to the existing
                // bitmap; it does not clear the previous frame. Reusing one
                // instance therefore makes every update draw another copy of
                // the UI on top of the last one (visible as stacked text on
                // the secondary display). Always render into a fresh bitmap.
                var snapshot = new RenderTargetBitmap(
                    pixelWidth,
                    pixelHeight,
                    96,
                    96,
                    PixelFormats.Pbgra32);
                snapshot.Render(sourceVisual);
                snapshot.Freeze();
                mirrorSurface.Source = snapshot;
                renderFailureLogged = false;
            }
            catch (Exception ex)
            {
                if (!renderFailureLogged)
                {
                    Trace.WriteLine($"DISPLAY_MIRROR_SNAPSHOT_FAILED: {ex.Message}");
                    renderFailureLogged = true;
                }
            }
        }

        private bool TryGetSourceSize(out double width, out double height)
        {
            width = 0;
            height = 0;
            if (sourceVisual is FrameworkElement element)
            {
                width = element.ActualWidth;
                height = element.ActualHeight;
                if (width <= 0 || height <= 0)
                {
                    width = element.RenderSize.Width;
                    height = element.RenderSize.Height;
                }
            }

            return width > 0 && height > 0;
        }
    }
}
