using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace VHDMounter
{
    public partial class MainWindow
    {
        private const int NetworkAdaptersPerPage = 3;

        private readonly IReadOnlyList<SystemSettingsItem> systemSettingsItems = new[]
        {
            new SystemSettingsItem(SystemSettingsItemKind.NetworkSettings, "网络设置", "在 DHCP 和静态 IPv4 之间切换，并配置网络参数"),
            new SystemSettingsItem(SystemSettingsItemKind.AudioSettings, "音频设置", "调整系统默认输出设备音量"),
        };

        private readonly IReadOnlyList<NetworkModeOption> networkModeOptions = new[]
        {
            new NetworkModeOption(NetworkModeOptionKind.Dhcp, "DHCP", "自动获取 IPv4 地址和 DNS"),
            new NetworkModeOption(NetworkModeOptionKind.StaticIpv4, "静态 IPv4", "手动配置 IP、子网掩码、网关、主要 DNS 和备用 DNS"),
        };

        private readonly NetworkConfigurationService networkConfigurationService = new NetworkConfigurationService();
    private readonly TimedPressSequenceTracker networkEditorBackGestureTracker = new TimedPressSequenceTracker(TimeSpan.FromMilliseconds(800));
        private AudioEndpointService audioEndpointService;
        private IReadOnlyList<NetworkAdapterInfo> connectedNetworkAdapters = Array.Empty<NetworkAdapterInfo>();
        private NetworkAdapterInfo selectedNetworkAdapter;
        private NetworkIpv4EditorState networkIpv4EditorState;
        private AudioEndpointSnapshot audioEndpointSnapshot = AudioEndpointSnapshot.Unavailable("未找到可用的默认输出设备。");
        private int selectedSystemSettingsIndex;
        private int selectedNetworkAdapterIndex;
        private int selectedNetworkModeIndex;
        private bool networkAdapterSelectionRequired;
        private bool isNetworkOperationPending;
        private NetworkEditorViewStatus networkEditorViewStatus = NetworkEditorViewStatus.Editing;
        private string networkAdapterStatusMessage = string.Empty;
        private string networkModeStatusMessage = string.Empty;
        private string networkEditorStatusMessage = "未保存修改";

        private void InitializeSystemSettingsFeatures()
        {
            audioEndpointService = new AudioEndpointService();
            NetworkEditorFieldsItemsControl.ItemsSource = Array.Empty<NetworkEditorFieldDisplay>();
            audioEndpointSnapshot = audioEndpointService.GetDefaultRenderSnapshot();
            AudioVolumeProgressBar.Value = 0;
        }

        private void DisposeSystemSettingsFeatures()
        {
            if (audioEndpointService != null)
            {
                audioEndpointService.Dispose();
                audioEndpointService = null;
            }
        }

        private async Task<bool> HandleSpecializedOverlayInputAsync(UiInputAction action)
        {
            if (isNetworkOperationPending &&
                (currentOverlayState == OverlayState.NetworkDhcpConfirm || currentOverlayState == OverlayState.NetworkIpv4Edit))
            {
                return true;
            }

            switch (currentOverlayState)
            {
                case OverlayState.SystemSettingsHome:
                    HandleLinearSelection(action, systemSettingsItems.Count, ref selectedSystemSettingsIndex);
                    if (action == UiInputAction.Confirm)
                    {
                        await ConfirmSystemSettingsSelectionAsync();
                    }
                    else if (action == UiInputAction.Back)
                    {
                        selectedMenuIndex = serviceMenuItems.Count - 1;
                        currentOverlayState = OverlayState.ServiceMenuHome;
                        RenderOverlay();
                    }
                    else if (action == UiInputAction.Up || action == UiInputAction.Down)
                    {
                        RenderOverlay();
                    }

                    return true;
                case OverlayState.NetworkAdapterSelect:
                    if (connectedNetworkAdapters.Count == 0)
                    {
                        if (action == UiInputAction.Back)
                        {
                            currentOverlayState = OverlayState.SystemSettingsHome;
                            RenderOverlay();
                        }

                        return true;
                    }

                    HandleLinearSelection(action, connectedNetworkAdapters.Count, ref selectedNetworkAdapterIndex);
                    if (action == UiInputAction.Confirm)
                    {
                        EnterSelectedNetworkAdapter();
                    }
                    else if (action == UiInputAction.Back)
                    {
                        currentOverlayState = OverlayState.SystemSettingsHome;
                        RenderOverlay();
                    }
                    else if (action == UiInputAction.Up || action == UiInputAction.Down)
                    {
                        RenderOverlay();
                    }

                    return true;
                case OverlayState.NetworkModeSelect:
                    HandleLinearSelection(action, networkModeOptions.Count, ref selectedNetworkModeIndex);
                    if (action == UiInputAction.Confirm)
                    {
                        await ConfirmNetworkModeSelectionAsync();
                    }
                    else if (action == UiInputAction.Back)
                    {
                        ReturnFromNetworkModeSelect();
                    }
                    else if (action == UiInputAction.Up || action == UiInputAction.Down)
                    {
                        networkModeStatusMessage = string.Empty;
                        RenderOverlay();
                    }

                    return true;
                case OverlayState.NetworkDhcpConfirm:
                    if (action == UiInputAction.Confirm)
                    {
                        await ApplyDhcpConfigurationAsync();
                    }
                    else if (action == UiInputAction.Back)
                    {
                        currentOverlayState = OverlayState.NetworkModeSelect;
                        RenderOverlay();
                    }

                    return true;
                case OverlayState.NetworkIpv4Edit:
                    if (action == UiInputAction.Back)
                    {
                        HandleNetworkEditorBackRequest();
                    }

                    return true;
                case OverlayState.NetworkDiscardConfirm:
                    if (action == UiInputAction.Confirm)
                    {
                        currentOverlayState = OverlayState.NetworkModeSelect;
                        networkModeStatusMessage = "已放弃未保存的静态网络修改。";
                        RenderOverlay();
                    }
                    else if (action == UiInputAction.Back)
                    {
                        currentOverlayState = OverlayState.NetworkIpv4Edit;
                        RenderOverlay();
                    }

                    return true;
                case OverlayState.AudioSettings:
                    if (action == UiInputAction.Up)
                    {
                        AdjustAudioVolume(1);
                    }
                    else if (action == UiInputAction.Down)
                    {
                        AdjustAudioVolume(-1);
                    }
                    else if (action == UiInputAction.Back)
                    {
                        currentOverlayState = OverlayState.SystemSettingsHome;
                        RenderOverlay();
                    }

                    return true;
                default:
                    return false;
            }
        }

        private async Task HandleOverlayRawInputAsync(MaimollerRawInputEventArgs eventArgs)
        {
            if (eventArgs == null || !isServiceMenuOpen || currentOverlayState != OverlayState.NetworkIpv4Edit)
            {
                return;
            }

            if (networkEditorViewStatus == NetworkEditorViewStatus.Applying)
            {
                return;
            }

            switch (eventArgs.Kind)
            {
                case MaimollerRawInputKind.Digit:
                    ResetNetworkEditorCoinSequence();
                    HandleNetworkEditorDigitInput(eventArgs.Digit.GetValueOrDefault());
                    break;
                case MaimollerRawInputKind.CoinShortPress:
                    HandleNetworkEditorCoinShortPress();
                    break;
                case MaimollerRawInputKind.CoinLongPressConfirm:
                    ResetNetworkEditorCoinSequence();
                    await ApplyStaticNetworkConfigurationAsync();
                    break;
            }
        }

        private async Task ConfirmSystemSettingsSelectionAsync()
        {
            switch (systemSettingsItems[selectedSystemSettingsIndex].Kind)
            {
                case SystemSettingsItemKind.NetworkSettings:
                    await OpenNetworkSettingsAsync();
                    break;
                case SystemSettingsItemKind.AudioSettings:
                    OpenAudioSettings();
                    break;
            }
        }

        private async Task OpenNetworkSettingsAsync()
        {
            networkAdapterStatusMessage = string.Empty;
            networkModeStatusMessage = string.Empty;
            selectedNetworkAdapter = null;
            selectedNetworkAdapterIndex = 0;

            try
            {
                connectedNetworkAdapters = await networkConfigurationService.GetConnectedAdaptersAsync();
                networkAdapterSelectionRequired = connectedNetworkAdapters.Count > 1;

                if (connectedNetworkAdapters.Count == 0)
                {
                    currentOverlayState = OverlayState.NetworkAdapterSelect;
                    networkAdapterStatusMessage = "未检测到已连接的可配置 IPv4 网卡。";
                    RenderOverlay();
                    return;
                }

                selectedNetworkAdapterIndex = 0;
                if (networkAdapterSelectionRequired)
                {
                    currentOverlayState = OverlayState.NetworkAdapterSelect;
                    RenderOverlay();
                    return;
                }

                selectedNetworkAdapter = connectedNetworkAdapters[0];
                selectedNetworkModeIndex = GetSelectedNetworkModeIndex(selectedNetworkAdapter);
                currentOverlayState = OverlayState.NetworkModeSelect;
                RenderOverlay();
            }
            catch (Exception ex)
            {
                connectedNetworkAdapters = Array.Empty<NetworkAdapterInfo>();
                currentOverlayState = OverlayState.NetworkAdapterSelect;
                networkAdapterStatusMessage = $"读取网卡列表失败：{ex.Message}";
                RenderOverlay();
            }
        }

        private void EnterSelectedNetworkAdapter()
        {
            selectedNetworkAdapter = connectedNetworkAdapters.ElementAtOrDefault(selectedNetworkAdapterIndex);
            if (selectedNetworkAdapter == null)
            {
                return;
            }

            selectedNetworkModeIndex = GetSelectedNetworkModeIndex(selectedNetworkAdapter);
            networkModeStatusMessage = string.Empty;
            currentOverlayState = OverlayState.NetworkModeSelect;
            RenderOverlay();
        }

        private async Task ConfirmNetworkModeSelectionAsync()
        {
            if (selectedNetworkAdapter == null)
            {
                return;
            }

            switch (networkModeOptions[selectedNetworkModeIndex].Kind)
            {
                case NetworkModeOptionKind.Dhcp:
                    currentOverlayState = OverlayState.NetworkDhcpConfirm;
                    RenderOverlay();
                    break;
                case NetworkModeOptionKind.StaticIpv4:
                    await OpenNetworkIpv4EditorAsync();
                    break;
            }
        }

        private Task OpenNetworkIpv4EditorAsync()
        {
            networkIpv4EditorState = new NetworkIpv4EditorState(selectedNetworkAdapter?.CurrentConfiguration);
            networkEditorViewStatus = NetworkEditorViewStatus.Editing;
            networkEditorStatusMessage = "未保存修改";
            ResetNetworkEditorCoinSequence();
            currentOverlayState = OverlayState.NetworkIpv4Edit;
            RenderOverlay();
            return Task.CompletedTask;
        }

        private async Task ApplyDhcpConfigurationAsync()
        {
            if (selectedNetworkAdapter == null)
            {
                return;
            }

            networkModeStatusMessage = "正在切换为 DHCP...";
            isNetworkOperationPending = true;
            RenderOverlay();

            try
            {
                var result = await networkConfigurationService.ApplyDhcpAsync(selectedNetworkAdapter.InterfaceId);
                if (result.Success)
                {
                    var refreshedAdapter = await networkConfigurationService.GetAdapterAsync(selectedNetworkAdapter.InterfaceId)
                        ?? selectedNetworkAdapter.WithConfiguration(result.EffectiveConfiguration ?? selectedNetworkAdapter.CurrentConfiguration);
                    UpdateSelectedNetworkAdapter(refreshedAdapter);
                    currentOverlayState = OverlayState.NetworkModeSelect;
                    networkModeStatusMessage = string.IsNullOrWhiteSpace(result.Message) ? "已切换为 DHCP。" : result.Message;
                }
                else
                {
                    currentOverlayState = OverlayState.NetworkModeSelect;
                    networkModeStatusMessage = result.Message;
                }
            }
            catch (Exception ex)
            {
                currentOverlayState = OverlayState.NetworkModeSelect;
                networkModeStatusMessage = $"切换 DHCP 失败：{ex.Message}";
            }
            finally
            {
                isNetworkOperationPending = false;
            }

            RenderOverlay();
        }

        private async Task ApplyStaticNetworkConfigurationAsync()
        {
            if (selectedNetworkAdapter == null || networkIpv4EditorState == null)
            {
                return;
            }

            if (!networkIpv4EditorState.TryBuildConfiguration(out var configuration, out var error))
            {
                networkEditorViewStatus = NetworkEditorViewStatus.Error;
                networkEditorStatusMessage = error;
                RenderOverlay();
                return;
            }

            networkEditorViewStatus = NetworkEditorViewStatus.Applying;
            networkEditorStatusMessage = "正在应用网络设置，请勿断电或拔出网线。";
            isNetworkOperationPending = true;
            RenderOverlay();

            try
            {
                var result = await networkConfigurationService.ApplyStaticConfigurationAsync(selectedNetworkAdapter.InterfaceId, configuration);
                if (result.Success)
                {
                    var refreshedAdapter = await networkConfigurationService.GetAdapterAsync(selectedNetworkAdapter.InterfaceId)
                        ?? selectedNetworkAdapter.WithConfiguration(result.EffectiveConfiguration ?? configuration);
                    UpdateSelectedNetworkAdapter(refreshedAdapter);
                    networkIpv4EditorState.LoadConfiguration(refreshedAdapter.CurrentConfiguration);
                    networkEditorViewStatus = NetworkEditorViewStatus.Applied;
                    networkEditorStatusMessage = string.IsNullOrWhiteSpace(result.Message) ? "已应用" : result.Message;
                }
                else
                {
                    networkEditorViewStatus = NetworkEditorViewStatus.Error;
                    networkEditorStatusMessage = result.Message;
                }
            }
            catch (Exception ex)
            {
                networkEditorViewStatus = NetworkEditorViewStatus.Error;
                networkEditorStatusMessage = $"应用静态 IPv4 失败：{ex.Message}";
            }
            finally
            {
                isNetworkOperationPending = false;
            }

            RenderOverlay();
        }

        private void HandleNetworkEditorDigitInput(int digit)
        {
            if (networkIpv4EditorState == null)
            {
                return;
            }

            if (networkIpv4EditorState.TryEnterDigit(digit, out var error))
            {
                networkEditorViewStatus = NetworkEditorViewStatus.Editing;
                networkEditorStatusMessage = "未保存修改";
            }
            else
            {
                networkEditorViewStatus = NetworkEditorViewStatus.Error;
                networkEditorStatusMessage = error;
            }

            RenderOverlay();
        }

        private void HandleNetworkEditorCoinShortPress()
        {
            if (networkIpv4EditorState == null)
            {
                return;
            }

            if (networkEditorBackGestureTracker.RegisterPress(DateTime.UtcNow) >= 3)
            {
                ResetNetworkEditorCoinSequence();
                HandleNetworkEditorBackRequest();
                return;
            }

            networkIpv4EditorState.AdvanceSegment();
            if (networkEditorViewStatus == NetworkEditorViewStatus.Error)
            {
                networkEditorViewStatus = NetworkEditorViewStatus.Editing;
                networkEditorStatusMessage = "未保存修改";
            }

            RenderOverlay();
        }

        private void HandleNetworkEditorBackRequest()
        {
            if (networkIpv4EditorState != null && networkIpv4EditorState.HasUnsavedChanges)
            {
                currentOverlayState = OverlayState.NetworkDiscardConfirm;
            }
            else
            {
                currentOverlayState = OverlayState.NetworkModeSelect;
                networkModeStatusMessage = "已返回模式选择。";
            }

            RenderOverlay();
        }

        private void OpenAudioSettings()
        {
            audioEndpointSnapshot = audioEndpointService?.GetDefaultRenderSnapshot() ?? AudioEndpointSnapshot.Unavailable("未找到可用的默认输出设备。");
            currentOverlayState = OverlayState.AudioSettings;
            RenderOverlay();
        }

        private void AdjustAudioVolume(int deltaPercent)
        {
            if (audioEndpointService == null)
            {
                audioEndpointSnapshot = AudioEndpointSnapshot.Unavailable("未初始化音频服务。");
                RenderOverlay();
                return;
            }

            audioEndpointSnapshot = audioEndpointSnapshot.IsAvailable
                ? audioEndpointService.AdjustVolumeByPercent(deltaPercent)
                : audioEndpointService.GetDefaultRenderSnapshot();
            RenderOverlay();
        }

        private void RenderSystemSettingsOverlay()
        {
            ShowOverlayListMode();
            OverlayTitleText.Text = "系统设置调整";
            OverlaySubtitleText.Text = "选择要调整的系统功能。";
            OverlayPageIndicatorText.Text = string.Empty;
            OverlayFooterText.Text = "6 / 3 切换，4 进入，5 返回系统菜单。";

            for (var index = 0; index < systemSettingsItems.Count; index++)
            {
                var item = systemSettingsItems[index];
                overlayLines.Add(new OverlayDisplayLine(item.Title, index == selectedSystemSettingsIndex ? "当前选中" : string.Empty, item.Description, index == selectedSystemSettingsIndex));
            }
        }

        private void RenderNetworkAdapterSelectOverlay()
        {
            ShowOverlayListMode();

            if (connectedNetworkAdapters.Count == 0)
            {
                OverlayTitleText.Text = "网络设置";
                OverlaySubtitleText.Text = string.IsNullOrWhiteSpace(networkAdapterStatusMessage)
                    ? "未检测到已连接的可配置 IPv4 网卡。"
                    : networkAdapterStatusMessage;
                OverlayPageIndicatorText.Text = string.Empty;
                OverlayFooterText.Text = "按 5 返回系统设置调整。";
                overlayLines.Add(new OverlayDisplayLine("网络设置", "不可用", "当前没有可进入配置流程的网卡。", true));
                return;
            }

            var pageIndex = selectedNetworkAdapterIndex / NetworkAdaptersPerPage;
            var totalPages = (int)Math.Ceiling(connectedNetworkAdapters.Count / (double)NetworkAdaptersPerPage);
            var pageItems = connectedNetworkAdapters.Skip(pageIndex * NetworkAdaptersPerPage).Take(NetworkAdaptersPerPage).ToList();

            OverlayTitleText.Text = "网络设置 / 网卡选择";
            OverlaySubtitleText.Text = "只显示当前已连接的可配置网卡，请先选择目标网卡。";
            OverlayPageIndicatorText.Text = $"第 {pageIndex + 1} / {totalPages} 页";
            OverlayFooterText.Text = "每页最多 3 张网卡，4 进入模式选择，5 返回系统设置调整。";

            foreach (var adapter in pageItems)
            {
                var isSelected = string.Equals(adapter.InterfaceId, connectedNetworkAdapters[selectedNetworkAdapterIndex].InterfaceId, StringComparison.OrdinalIgnoreCase);
                overlayLines.Add(new OverlayDisplayLine(adapter.DisplayName, adapter.CurrentIpv4Text, adapter.DetailText, isSelected));
            }
        }

        private void RenderNetworkModeSelectOverlay()
        {
            ShowOverlayListMode();
            OverlayTitleText.Text = "网络设置 / 配置模式";
            OverlaySubtitleText.Text = selectedNetworkAdapter == null
                ? "请选择 DHCP 或静态 IPv4。"
                : $"{selectedNetworkAdapter.DisplayName} | 当前模式：{selectedNetworkAdapter.CurrentConfiguration.ModeText}";
            OverlayPageIndicatorText.Text = networkModeStatusMessage;
            OverlayFooterText.Text = networkAdapterSelectionRequired
                ? "6 / 3 切换模式，4 确认，5 返回网卡选择。"
                : "6 / 3 切换模式，4 确认，5 返回系统设置调整。";

            for (var index = 0; index < networkModeOptions.Count; index++)
            {
                var option = networkModeOptions[index];
                overlayLines.Add(new OverlayDisplayLine(option.Title, index == selectedNetworkModeIndex ? "当前选中" : string.Empty, option.Description, index == selectedNetworkModeIndex));
            }
        }

        private void RenderNetworkDhcpConfirmOverlay()
        {
            ShowOverlayListMode();
            OverlayTitleText.Text = "切换到 DHCP？";
            OverlaySubtitleText.Text = selectedNetworkAdapter == null
                ? "当前网卡会改为自动获取 IPv4 地址和 DNS。"
                : $"{selectedNetworkAdapter.DisplayName} 会改为自动获取 IPv4 地址和 DNS。";
            OverlayPageIndicatorText.Text = isNetworkOperationPending ? "正在应用 DHCP..." : string.Empty;
            OverlayFooterText.Text = isNetworkOperationPending
                ? "正在切换 DHCP，请等待当前操作完成。"
                : "4 确认切换，5 返回模式选择。";
            overlayLines.Add(new OverlayDisplayLine("DHCP 切换确认", isNetworkOperationPending ? "执行中" : "待执行", "确认后立即应用，不需要进入静态编辑页。", true));
        }

        private void RenderNetworkDiscardConfirmOverlay()
        {
            ShowOverlayListMode();
            OverlayTitleText.Text = "放弃未保存的网络修改？";
            OverlaySubtitleText.Text = "当前修改尚未应用，离开后会全部丢失。";
            OverlayPageIndicatorText.Text = string.Empty;
            OverlayFooterText.Text = "4 放弃修改，5 继续编辑。";
            overlayLines.Add(new OverlayDisplayLine("未保存修改", "待确认", "如果要保存，请回到编辑页长按 Coin 1 秒应用。", true));
        }

        private void RenderNetworkIpv4EditorOverlay()
        {
            ShowNetworkEditorMode();
            OverlayTitleText.Text = "网络设置 / 静态 IPv4";
            OverlaySubtitleText.Text = selectedNetworkAdapter == null
                ? "1 到 8 输入数字，Test=9，Service=0。"
                : $"{selectedNetworkAdapter.DisplayName} | 1-8=数字，Test=9，Service=0";
            OverlayPageIndicatorText.Text = networkEditorStatusMessage;
            OverlayFooterText.Text = BuildNetworkEditorFooterText();
            NetworkEditorAdapterText.Text = networkIpv4EditorState == null ? string.Empty : networkIpv4EditorState.GetActiveSegmentDescriptor();
            NetworkEditorHintText.Text = "Coin 短按切换下一段，连续短按三次返回，长按 1 秒应用。";
            NetworkEditorFieldsItemsControl.ItemsSource = networkIpv4EditorState?.BuildFieldDisplays() ?? Array.Empty<NetworkEditorFieldDisplay>();
        }

        private void RenderAudioSettingsOverlay()
        {
            ShowAudioSettingsMode();
            OverlayTitleText.Text = "音频设置";
            OverlaySubtitleText.Text = audioEndpointSnapshot.IsAvailable
                ? "6 增加音量，3 减少音量，调整实时生效。"
                : "未找到可用的默认输出设备。";
            OverlayPageIndicatorText.Text = audioEndpointSnapshot.StatusMessage;
            OverlayFooterText.Text = audioEndpointSnapshot.IsAvailable
                ? "不需要确认，5 返回系统设置调整。"
                : "按 5 返回系统设置调整。";
            AudioDeviceNameText.Text = audioEndpointSnapshot.IsAvailable ? audioEndpointSnapshot.DeviceName : "不可用";
            AudioVolumeValueText.Text = audioEndpointSnapshot.IsAvailable ? $"{audioEndpointSnapshot.VolumePercent}%" : "不可用";
            AudioVolumeProgressBar.Value = audioEndpointSnapshot.IsAvailable ? audioEndpointSnapshot.VolumePercent : 0;
            AudioStatusText.Text = audioEndpointSnapshot.IsAvailable ? "默认输出设备" : audioEndpointSnapshot.StatusMessage;
        }

        private void ShowOverlayListMode()
        {
            OverlayListScrollViewer.Visibility = Visibility.Visible;
            NetworkEditorPanel.Visibility = Visibility.Collapsed;
            AudioSettingsPanel.Visibility = Visibility.Collapsed;
        }

        private void ShowNetworkEditorMode()
        {
            OverlayListScrollViewer.Visibility = Visibility.Collapsed;
            NetworkEditorPanel.Visibility = Visibility.Visible;
            AudioSettingsPanel.Visibility = Visibility.Collapsed;
        }

        private void ShowAudioSettingsMode()
        {
            OverlayListScrollViewer.Visibility = Visibility.Collapsed;
            NetworkEditorPanel.Visibility = Visibility.Collapsed;
            AudioSettingsPanel.Visibility = Visibility.Visible;
        }

        private void ReturnFromNetworkModeSelect()
        {
            currentOverlayState = networkAdapterSelectionRequired ? OverlayState.NetworkAdapterSelect : OverlayState.SystemSettingsHome;
            RenderOverlay();
        }

        private void UpdateSelectedNetworkAdapter(NetworkAdapterInfo adapter)
        {
            if (adapter == null)
            {
                return;
            }

            selectedNetworkAdapter = adapter;
            var adapters = connectedNetworkAdapters.ToList();
            var existingIndex = adapters.FindIndex(candidate => string.Equals(candidate.InterfaceId, adapter.InterfaceId, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                adapters[existingIndex] = adapter;
                selectedNetworkAdapterIndex = existingIndex;
            }
            else
            {
                adapters.Add(adapter);
                selectedNetworkAdapterIndex = adapters.Count - 1;
            }

            connectedNetworkAdapters = adapters;
            selectedNetworkModeIndex = GetSelectedNetworkModeIndex(adapter);
        }

        private static void HandleLinearSelection(UiInputAction action, int itemCount, ref int selectedIndex)
        {
            if (itemCount <= 0)
            {
                selectedIndex = 0;
                return;
            }

            selectedIndex = Math.Clamp(selectedIndex, 0, itemCount - 1);

            if (action == UiInputAction.Up)
            {
                selectedIndex = Math.Max(0, selectedIndex - 1);
            }
            else if (action == UiInputAction.Down)
            {
                selectedIndex = Math.Min(itemCount - 1, selectedIndex + 1);
            }
        }

        private static int GetSelectedNetworkModeIndex(NetworkAdapterInfo adapter)
        {
            return adapter?.CurrentConfiguration.Mode == NetworkConfigurationMode.StaticIpv4 ? 1 : 0;
        }

        private string BuildNetworkEditorFooterText()
        {
            return networkEditorViewStatus switch
            {
                NetworkEditorViewStatus.Applying => "正在应用网络设置，请勿断电或拔出网线。",
                NetworkEditorViewStatus.Applied => "配置已生效，Coin 短按切段，连续短按三次返回。",
                _ => "Coin 在 20 个输入段中循环切换，连续短按三次返回，长按 1 秒应用。",
            };
        }

        private void ResetNetworkEditorCoinSequence()
        {
            networkEditorBackGestureTracker.Reset();
        }
    }
}