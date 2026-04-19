using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;

namespace VHDMounter
{
    internal sealed class OverlayWindowActivationContext
    {
        public bool WasBackgroundHidden { get; set; }

        public bool PreviousShowInTaskbar { get; set; }

        public WindowState PreviousWindowState { get; set; }

        public bool PreviousTopmost { get; set; }

        public IntPtr PreviousForegroundWindow { get; set; }
    }

    internal sealed class WindowActivationService
    {
        public Task<OverlayWindowActivationContext> EnsureWindowVisibleForOverlayAsync(Window window)
        {
            if (window == null)
            {
                throw new ArgumentNullException(nameof(window));
            }

            return window.Dispatcher.InvokeAsync(() =>
            {
                var helper = new WindowInteropHelper(window);
                var hwnd = helper.EnsureHandle();
                var context = new OverlayWindowActivationContext
                {
                    WasBackgroundHidden = !window.ShowInTaskbar || window.WindowState == WindowState.Minimized || !window.IsVisible,
                    PreviousShowInTaskbar = window.ShowInTaskbar,
                    PreviousWindowState = window.WindowState,
                    PreviousTopmost = window.Topmost,
                    PreviousForegroundWindow = NativeMethods.GetForegroundWindow(),
                };

                if (!window.IsVisible)
                {
                    window.Show();
                }

                window.ShowInTaskbar = true;
                window.Topmost = true;

                if (window.WindowState == WindowState.Minimized)
                {
                    window.WindowState = WindowState.Normal;
                }

                window.WindowState = WindowState.Maximized;
                window.Activate();

                NativeMethods.ShowWindowAsync(hwnd, NativeMethods.SW_SHOWMAXIMIZED);
                NativeMethods.BringWindowToTop(hwnd);
                NativeMethods.SetForegroundWindow(hwnd);

                return context;
            }).Task;
        }

        public Task RestoreWindowAsync(Window window, OverlayWindowActivationContext context)
        {
            if (window == null)
            {
                throw new ArgumentNullException(nameof(window));
            }

            if (context == null || !context.WasBackgroundHidden)
            {
                return Task.CompletedTask;
            }

            return window.Dispatcher.InvokeAsync(() =>
            {
                window.ShowInTaskbar = context.PreviousShowInTaskbar;
                window.WindowState = context.PreviousWindowState;
                window.Topmost = context.PreviousTopmost;

                if (context.PreviousForegroundWindow != IntPtr.Zero)
                {
                    NativeMethods.BringWindowToTop(context.PreviousForegroundWindow);
                    NativeMethods.SetForegroundWindow(context.PreviousForegroundWindow);
                }
            }).Task;
        }
    }
}