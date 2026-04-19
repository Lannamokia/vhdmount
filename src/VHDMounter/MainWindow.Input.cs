using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace VHDMounter
{
    public partial class MainWindow
    {
        private async Task HandleKeyDownAsync(KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                var now = DateTime.Now;
                if ((now - lastDelPressTime) <= delPressWindow)
                {
                    delPressCount++;
                }
                else
                {
                    delPressCount = 1;
                }

                lastDelPressTime = now;
                if (delPressCount >= 3)
                {
                    delPressCount = 0;
                    e.Handled = true;
                    await SafeShutdown();
                    return;
                }
            }

            var action = TranslateKeyToUiInputAction(e.Key);
            if (action == UiInputAction.None)
            {
                return;
            }

            e.Handled = true;
            await HandleInputActionAsync(action);
        }

        private static UiInputAction TranslateKeyToUiInputAction(Key key)
        {
            switch (key)
            {
                case Key.Up:
                    return UiInputAction.Up;
                case Key.Down:
                    return UiInputAction.Down;
                case Key.Enter:
                    return UiInputAction.Confirm;
                case Key.Escape:
                    return UiInputAction.Back;
                case Key.F12:
                    return UiInputAction.OpenServiceMenu;
                default:
                    return UiInputAction.None;
            }
        }

        private async Task HandleInputActionAsync(UiInputAction action)
        {
            if (action == UiInputAction.None)
            {
                return;
            }

#if FEATURE_HID_MENU
            if (await HandleOverlayInputAsync(action))
            {
                return;
            }
#endif

            if (VHDSelector.Visibility != Visibility.Visible)
            {
                return;
            }

            switch (action)
            {
                case UiInputAction.Up:
                    if (VHDListBox.SelectedIndex > 0)
                    {
                        VHDListBox.SelectedIndex--;
                    }
                    break;
                case UiInputAction.Down:
                    if (VHDListBox.SelectedIndex < VHDListBox.Items.Count - 1)
                    {
                        VHDListBox.SelectedIndex++;
                    }
                    break;
                case UiInputAction.Confirm:
                    if (!isProcessing && VHDListBox.SelectedIndex >= 0 && availableVHDs != null)
                    {
                        isProcessing = true;
                        var selectedVhd = availableVHDs[VHDListBox.SelectedIndex];
                        await ProcessSelectedVHD(selectedVhd);
                    }
                    break;
            }
        }

        private void HideWindowForGameMonitoring()
        {
#if FEATURE_HID_MENU
            SetWindowHiddenForGame(true);
            if (isServiceMenuOpen)
            {
                return;
            }
#endif

            Dispatcher.Invoke(() =>
            {
                WindowState = WindowState.Minimized;
                ShowInTaskbar = false;
            });
        }
    }
}