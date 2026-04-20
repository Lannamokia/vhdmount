using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace VHDMounter
{
    public partial class MainWindow
    {
        private readonly ObservableCollection<OverlayDisplayLine> overlayLines = new ObservableCollection<OverlayDisplayLine>();
        private readonly IReadOnlyList<ServiceMenuItem> serviceMenuItems = new[]
        {
            new ServiceMenuItem(ServiceMenuItemKind.RestartSystem, "重启系统", "卸载当前挂载后立即重启 Windows"),
            new ServiceMenuItem(ServiceMenuItemKind.ShutdownSystem, "关闭系统", "卸载当前挂载后立即关闭 Windows"),
            new ServiceMenuItem(ServiceMenuItemKind.SystemInfo, "系统信息", "查看 CPU、内存、独显与磁盘占用情况"),
            new ServiceMenuItem(ServiceMenuItemKind.SystemSettings, "系统设置调整", "进入网络设置和音频设置"),
        };

        private readonly WindowActivationService windowActivationService = new WindowActivationService();
        private SystemInfoService systemInfoService;
        private MaimollerInputService maimollerInputService;
        private OverlayState currentOverlayState = OverlayState.None;
        private int selectedMenuIndex;
        private int currentSystemInfoPageIndex;
        private bool isServiceMenuOpen;
        private bool isPowerActionPending;
        private bool isWindowHiddenForGame;
        private OverlayWindowActivationContext overlayActivationContext;
        private SystemInfoSnapshot latestSystemInfoSnapshot = SystemInfoSnapshot.Empty;

        private void InitializeFeatureServices()
        {
            OverlayItemsControl.ItemsSource = overlayLines;
            OverlayFooterText.Text = "Coin 长按 15 秒或 F12 可打开系统菜单";

            systemInfoService = new SystemInfoService();
            systemInfoService.SnapshotUpdated += OnSystemInfoSnapshotUpdated;
            InitializeSystemSettingsFeatures();

            maimollerInputService = new MaimollerInputService();
            maimollerInputService.ActionRaised += OnMaimollerActionRaised;
            maimollerInputService.RawInputRaised += OnMaimollerRawInputRaised;
            maimollerInputService.Start();

            SyncFeatureInputState();
            RenderOverlay();
        }

        private void DisposeFeatureServices()
        {
            if (maimollerInputService != null)
            {
                maimollerInputService.ActionRaised -= OnMaimollerActionRaised;
                maimollerInputService.RawInputRaised -= OnMaimollerRawInputRaised;
                maimollerInputService.Dispose();
                maimollerInputService = null;
            }

            if (systemInfoService != null)
            {
                systemInfoService.SnapshotUpdated -= OnSystemInfoSnapshotUpdated;
                systemInfoService.Dispose();
                systemInfoService = null;
            }

            DisposeSystemSettingsFeatures();
        }

        private void SetWindowHiddenForGame(bool hidden)
        {
            isWindowHiddenForGame = hidden;
            if (!hidden)
            {
                overlayActivationContext = null;
            }
        }

        private void PromoteServiceMenuToForeground()
        {
            isWindowHiddenForGame = false;
            overlayActivationContext = null;
        }

        private async Task<bool> HandleOverlayInputAsync(UiInputAction action)
        {
            SyncFeatureInputState();

            if (action == UiInputAction.OpenServiceMenu)
            {
                if (currentStage == UiStage.Error || isPowerActionPending)
                {
                    return true;
                }

                await OpenServiceMenuAsync();
                return true;
            }

            if (!isServiceMenuOpen)
            {
                return false;
            }

            if (isPowerActionPending)
            {
                return true;
            }

            if (await HandleSpecializedOverlayInputAsync(action))
            {
                return true;
            }

            switch (currentOverlayState)
            {
                case OverlayState.ServiceMenuHome:
                    HandleLinearSelection(action, serviceMenuItems.Count, ref selectedMenuIndex);
                    if (action == UiInputAction.Up || action == UiInputAction.Down)
                    {
                        RenderOverlay();
                    }
                    else if (action == UiInputAction.Confirm)
                    {
                        await ConfirmServiceMenuSelectionAsync();
                    }
                    else if (action == UiInputAction.Back)
                    {
                        await CloseServiceMenuAsync();
                    }
                    return true;
                case OverlayState.SystemInfo:
                    if (action == UiInputAction.Up)
                    {
                        ChangeSystemInfoPage(-1);
                    }
                    else if (action == UiInputAction.Down)
                    {
                        ChangeSystemInfoPage(1);
                    }
                    else if (action == UiInputAction.Back)
                    {
                        systemInfoService?.Stop();
                        currentOverlayState = OverlayState.ServiceMenuHome;
                        RenderOverlay();
                    }
                    return true;
                case OverlayState.ConfirmRestart:
                    if (action == UiInputAction.Confirm)
                    {
                        await ExecutePowerActionAsync(reboot: true);
                    }
                    else if (action == UiInputAction.Back)
                    {
                        currentOverlayState = OverlayState.ServiceMenuHome;
                        RenderOverlay();
                    }
                    return true;
                case OverlayState.ConfirmShutdown:
                    if (action == UiInputAction.Confirm)
                    {
                        await ExecutePowerActionAsync(reboot: false);
                    }
                    else if (action == UiInputAction.Back)
                    {
                        currentOverlayState = OverlayState.ServiceMenuHome;
                        RenderOverlay();
                    }
                    return true;
                default:
                    return true;
            }
        }

        private async Task OpenServiceMenuAsync()
        {
            if (isServiceMenuOpen)
            {
                return;
            }

            overlayActivationContext = await windowActivationService.EnsureWindowVisibleForOverlayAsync(this);
            isServiceMenuOpen = true;
            currentOverlayState = OverlayState.ServiceMenuHome;
            currentSystemInfoPageIndex = 0;
            selectedMenuIndex = Math.Clamp(selectedMenuIndex, 0, serviceMenuItems.Count - 1);
            ServiceMenuOverlay.Visibility = System.Windows.Visibility.Visible;
            SyncFeatureInputState();
            RenderOverlay();
        }

        private async Task CloseServiceMenuAsync()
        {
            if (!isServiceMenuOpen)
            {
                return;
            }

            systemInfoService?.Stop();
            var activationContext = overlayActivationContext;
            overlayActivationContext = null;
            isServiceMenuOpen = false;
            currentOverlayState = OverlayState.None;
            ServiceMenuOverlay.Visibility = System.Windows.Visibility.Collapsed;
            SyncFeatureInputState();
            RenderOverlay();

            if (activationContext != null && activationContext.WasBackgroundHidden && isWindowHiddenForGame)
            {
                await windowActivationService.RestoreWindowAsync(this, activationContext);
                var process = vhdManager.GetFirstTargetProcess();
                if (process != null)
                {
                    vhdManager.FocusProcessWindow(process);
                }
            }
        }

        private async Task ConfirmServiceMenuSelectionAsync()
        {
            var selectedItem = serviceMenuItems[selectedMenuIndex];
            switch (selectedItem.Kind)
            {
                case ServiceMenuItemKind.RestartSystem:
                    currentOverlayState = OverlayState.ConfirmRestart;
                    RenderOverlay();
                    break;
                case ServiceMenuItemKind.ShutdownSystem:
                    currentOverlayState = OverlayState.ConfirmShutdown;
                    RenderOverlay();
                    break;
                case ServiceMenuItemKind.SystemInfo:
                    currentSystemInfoPageIndex = 0;
                    currentOverlayState = OverlayState.SystemInfo;
                    if (systemInfoService != null)
                    {
                        latestSystemInfoSnapshot = await systemInfoService.RefreshNowAsync();
                        systemInfoService.Start();
                    }
                    RenderOverlay();
                    break;
                case ServiceMenuItemKind.SystemSettings:
                    selectedSystemSettingsIndex = 0;
                    currentOverlayState = OverlayState.SystemSettingsHome;
                    RenderOverlay();
                    break;
            }
        }

        private async Task ExecutePowerActionAsync(bool reboot)
        {
            isPowerActionPending = true;
            SyncFeatureInputState();

            OverlayTitleText.Text = reboot ? "正在重启系统" : "正在关闭系统";
            OverlaySubtitleText.Text = "将先尝试卸载当前挂载，再执行系统电源操作。";
            OverlayPageIndicatorText.Text = string.Empty;
            OverlayFooterText.Text = "危险动作已确认，新的 Coin 长按请求会被忽略。";
            overlayLines.Clear();
            overlayLines.Add(new OverlayDisplayLine(
                reboot ? "准备重启" : "准备关机",
                "执行中",
                "如果卸载失败，仍继续执行系统电源动作。",
                true));

            try
            {
                await vhdManager.UnmountVHD();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"SERVICE_MENU_UNMOUNT_FAILED: {ex}");
            }

            try
            {
                vhdManager.StopEncryptedEvhdMount();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"SERVICE_MENU_STOP_EVHD_FAILED: {ex}");
            }

            try
            {
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "shutdown",
                    Arguments = reboot ? "/r /t 0" : "/s /t 0",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                Process.Start(processStartInfo);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"SERVICE_MENU_POWER_ACTION_FAILED: {ex}");
                isPowerActionPending = false;
                currentOverlayState = OverlayState.ServiceMenuHome;
                OverlaySubtitleText.Text = reboot ? "执行重启失败，请检查权限或系统策略。" : "执行关机失败，请检查权限或系统策略。";
                SyncFeatureInputState();
                RenderOverlay();
            }
        }

        private void OnMaimollerActionRaised(object sender, MaimollerActionEventArgs e)
        {
            Dispatcher.InvokeAsync(async () => await HandleInputActionAsync(e.Action));
        }

        private void OnMaimollerRawInputRaised(object sender, MaimollerRawInputEventArgs e)
        {
            Dispatcher.InvokeAsync(async () => await HandleOverlayRawInputAsync(e));
        }

        private void OnSystemInfoSnapshotUpdated(SystemInfoSnapshot snapshot)
        {
            latestSystemInfoSnapshot = snapshot ?? SystemInfoSnapshot.Empty;
            if (currentOverlayState != OverlayState.SystemInfo || !isServiceMenuOpen)
            {
                return;
            }

            Dispatcher.InvokeAsync(RenderOverlay);
        }

        private void SyncFeatureInputState()
        {
            if (maimollerInputService == null)
            {
                return;
            }

            maimollerInputService.IsMenuOpen = isServiceMenuOpen;
            maimollerInputService.IgnoreMenuOpenRequests = isPowerActionPending || currentStage == UiStage.Error;
            maimollerInputService.InputMode = isServiceMenuOpen && currentOverlayState == OverlayState.NetworkIpv4Edit
                ? MaimollerInputRoutingMode.NetworkIpv4Edit
                : MaimollerInputRoutingMode.Navigation;
        }

        private void RenderOverlay()
        {
            if (!isServiceMenuOpen || currentOverlayState == OverlayState.None)
            {
                overlayLines.Clear();
                ServiceMenuOverlay.Visibility = System.Windows.Visibility.Collapsed;
                SyncFeatureInputState();
                return;
            }

            ServiceMenuOverlay.Visibility = System.Windows.Visibility.Visible;
            overlayLines.Clear();
            ShowOverlayListMode();

            switch (currentOverlayState)
            {
                case OverlayState.ServiceMenuHome:
                    OverlayTitleText.Text = "系统菜单";
                    OverlaySubtitleText.Text = "使用 6 / 3 切换，4 确认，5 返回关闭菜单。";
                    var menuPageIndex = selectedMenuIndex / 2;
                    var totalMenuPages = (int)Math.Ceiling(serviceMenuItems.Count / 2d);
                    OverlayPageIndicatorText.Text = $"第 {menuPageIndex + 1} / {totalMenuPages} 页";
                    OverlayFooterText.Text = menuPageIndex == 0
                        ? "当前页 2 项，按 3 可进入下一页。"
                        : "按 6 可返回上一页，按 4 进入当前功能。";
                    var visibleStartIndex = menuPageIndex * 2;
                    var visibleCount = Math.Min(2, serviceMenuItems.Count - visibleStartIndex);
                    for (var offset = 0; offset < visibleCount; offset++)
                    {
                        var actualIndex = visibleStartIndex + offset;
                        var item = serviceMenuItems[actualIndex];
                        overlayLines.Add(new OverlayDisplayLine(item.Title, actualIndex == selectedMenuIndex ? "当前选中" : string.Empty, item.Description, actualIndex == selectedMenuIndex));
                    }
                    break;
                case OverlayState.ConfirmRestart:
                    OverlayTitleText.Text = "确认重启系统？";
                    OverlaySubtitleText.Text = "4 号键会立即执行重启，5 号键取消并返回系统菜单。";
                    OverlayPageIndicatorText.Text = string.Empty;
                    OverlayFooterText.Text = "确认页不响应上下切换。";
                    overlayLines.Add(new OverlayDisplayLine("危险动作确认", "重启", "会先尝试卸载当前挂载，再执行 shutdown /r /t 0。", true));
                    break;
                case OverlayState.ConfirmShutdown:
                    OverlayTitleText.Text = "确认关闭系统？";
                    OverlaySubtitleText.Text = "4 号键会立即执行关机，5 号键取消并返回系统菜单。";
                    OverlayPageIndicatorText.Text = string.Empty;
                    OverlayFooterText.Text = "确认页不响应上下切换。";
                    overlayLines.Add(new OverlayDisplayLine("危险动作确认", "关机", "会先尝试卸载当前挂载，再执行 shutdown /s /t 0。", true));
                    break;
                case OverlayState.SystemInfo:
                    RenderSystemInfoOverlay();
                    break;
                case OverlayState.SystemSettingsHome:
                    RenderSystemSettingsOverlay();
                    break;
                case OverlayState.NetworkAdapterSelect:
                    RenderNetworkAdapterSelectOverlay();
                    break;
                case OverlayState.NetworkModeSelect:
                    RenderNetworkModeSelectOverlay();
                    break;
                case OverlayState.NetworkDhcpConfirm:
                    RenderNetworkDhcpConfirmOverlay();
                    break;
                case OverlayState.NetworkIpv4Edit:
                    RenderNetworkIpv4EditorOverlay();
                    break;
                case OverlayState.NetworkDiscardConfirm:
                    RenderNetworkDiscardConfirmOverlay();
                    break;
                case OverlayState.AudioSettings:
                    RenderAudioSettingsOverlay();
                    break;
            }

            SyncFeatureInputState();
        }

        private void RenderSystemInfoOverlay()
        {
            var pages = BuildSystemInfoPages(latestSystemInfoSnapshot);
            if (pages.Count == 0)
            {
                pages.Add(new SystemInfoPage("系统信息", "暂无可展示数据", new[]
                {
                    new OverlayDisplayLine("系统信息", "采集中", "等待下一次刷新快照。", true),
                }));
            }

            currentSystemInfoPageIndex = Math.Clamp(currentSystemInfoPageIndex, 0, pages.Count - 1);
            var page = pages[currentSystemInfoPageIndex];

            OverlayTitleText.Text = page.Title;
            OverlaySubtitleText.Text = page.Subtitle;
            OverlayPageIndicatorText.Text = $"第 {currentSystemInfoPageIndex + 1} / {pages.Count} 页";
            OverlayFooterText.Text = "6 上一页  3 下一页  5 返回系统菜单";

            foreach (var line in page.Lines)
            {
                overlayLines.Add(line);
            }
        }

        private void ChangeSystemInfoPage(int delta)
        {
            var pages = BuildSystemInfoPages(latestSystemInfoSnapshot);
            if (pages.Count == 0)
            {
                return;
            }

            currentSystemInfoPageIndex = Math.Clamp(currentSystemInfoPageIndex + delta, 0, pages.Count - 1);
            RenderOverlay();
        }

        private List<SystemInfoPage> BuildSystemInfoPages(SystemInfoSnapshot snapshot)
        {
            snapshot ??= SystemInfoSnapshot.Empty;
            var pages = new List<SystemInfoPage>
            {
                new SystemInfoPage(
                    "系统信息 / CPU",
                    "第 1 页显示 CPU 占用率与 CPU 温度。",
                    new[]
                    {
                        new OverlayDisplayLine("CPU 占用率", FormatPercent(snapshot.Cpu.UsagePercent, "采样中"), "基于 GetSystemTimes 计算总 CPU 使用率。", true),
                        new OverlayDisplayLine("CPU 温度", FormatTemperature(snapshot.Cpu.TemperatureCelsius), "优先 CPU Package 传感器，缺失时显示不可用。"),
                    }),
                new SystemInfoPage(
                    "系统信息 / 内存",
                    "第 2 页显示内存占用率。",
                    new[]
                    {
                        new OverlayDisplayLine("内存占用率", FormatPercent(snapshot.Memory.UsagePercent, "不可用"), $"已用 {FormatGiB(snapshot.Memory.UsedBytes)} / 总量 {FormatGiB(snapshot.Memory.TotalBytes)}", true),
                    }),
            };

            if (!snapshot.Gpu.HasDedicatedGpu)
            {
                pages.Add(new SystemInfoPage(
                    "系统信息 / 独显",
                    "第 3 页显示独显占用率、温度与驱动版本。",
                    new[]
                    {
                        new OverlayDisplayLine("独立显卡", "未检测到", "当前系统未识别到可用独显。", true),
                    }));
            }
            else
            {
                pages.Add(new SystemInfoPage(
                    "系统信息 / 独显",
                    snapshot.Gpu.Name,
                    new[]
                    {
                        new OverlayDisplayLine("独显占用率", FormatPercent(snapshot.Gpu.UsagePercent, "不可用"), $"驱动版本 {snapshot.Gpu.DriverVersion}", true),
                        new OverlayDisplayLine("独显温度", FormatTemperature(snapshot.Gpu.TemperatureCelsius), $"显存 {FormatGiB(snapshot.Gpu.DedicatedVideoMemoryBytes)}", false),
                        new OverlayDisplayLine("显卡驱动版本", snapshot.Gpu.DriverVersion, "温度或占用率不可用时仍保留驱动版本展示。", false),
                    }));
            }

            var driveLines = snapshot.Drives
                .OrderBy(drive => drive.Name, StringComparer.OrdinalIgnoreCase)
                .Select(drive => new OverlayDisplayLine(
                    string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.Name : $"{drive.Name} {drive.VolumeLabel}",
                    FormatPercent(drive.UsagePercent, "0.0%"),
                    $"已用 {FormatGiB(drive.UsedBytes)} / 总量 {FormatGiB(drive.TotalBytes)} / 剩余 {FormatGiB(drive.FreeBytes)}"))
                .ToList();

            if (driveLines.Count == 0)
            {
                driveLines.Add(new OverlayDisplayLine("分区信息", "无可用分区", "当前没有可展示的固定盘或可移动盘。", true));
            }

            for (var index = 0; index < driveLines.Count; index += 3)
            {
                pages.Add(new SystemInfoPage(
                    $"系统信息 / 分区 {(index / 3) + 1}",
                    "第 4 页及以后每页最多显示 3 个分区。",
                    driveLines.Skip(index).Take(3).ToList()));
            }

            return pages;
        }

        private void DismissServiceMenuForFatalStage()
        {
            isServiceMenuOpen = false;
            currentOverlayState = OverlayState.None;
            overlayActivationContext = null;
            systemInfoService?.Stop();
            ServiceMenuOverlay.Visibility = System.Windows.Visibility.Collapsed;
            overlayLines.Clear();
            SyncFeatureInputState();
        }

        private static string FormatPercent(double? value, string fallback)
        {
            if (!value.HasValue)
            {
                return fallback;
            }

            return value.Value.ToString("0.0", CultureInfo.InvariantCulture) + "%";
        }

        private static string FormatTemperature(double? value)
        {
            if (!value.HasValue)
            {
                return "不可用";
            }

            return value.Value.ToString("0.0", CultureInfo.InvariantCulture) + " °C";
        }

        private static string FormatGiB(ulong bytes)
        {
            if (bytes == 0)
            {
                return "0.0 GiB";
            }

            var gib = bytes / 1024d / 1024d / 1024d;
            return gib.ToString("0.0", CultureInfo.InvariantCulture) + " GiB";
        }
    }
}