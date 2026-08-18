using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace VHDMounter
{
    internal sealed class SecondaryDisplayMirrorWindow : Window, ISecondaryDisplayMirrorWindow
    {
        private readonly Rectangle mirrorSurface;
        private readonly VisualBrush mirrorBrush;
        private bool disposed;

        public SecondaryDisplayMirrorWindow(Visual sourceVisual)
        {
            if (sourceVisual == null)
            {
                throw new ArgumentNullException(nameof(sourceVisual));
            }

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

            mirrorBrush = new VisualBrush(sourceVisual)
            {
                Stretch = Stretch.Fill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center,
            };
            mirrorSurface = new Rectangle
            {
                Fill = mirrorBrush,
                IsHitTestVisible = false,
                Focusable = false,
            };
            Content = mirrorSurface;
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
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            HideMirror();

            Close();
            mirrorBrush.Visual = null;
            CurrentMonitor = null;
        }
    }
}
