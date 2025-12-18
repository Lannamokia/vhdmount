using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using System.Windows.Threading;

namespace VHDMounter
{
    public partial class MainWindow : Window
    {
        private VHDManager vhdManager;
        private List<string> availableVHDs;
        private bool isProcessing = false;
        // 三次 Delete 关闭程序的检测
        private int delPressCount = 0;
        private DateTime lastDelPressTime = DateTime.MinValue;
        private readonly TimeSpan delPressWindow = TimeSpan.FromSeconds(2);

        private enum UiStage
        {
            PreLaunchDelay,
            PrepareGameFiles,
            ApplyLocalUpdate,
            PrepareToLaunch,
            UpdateAndLaunch,
            CrashRecovering,
            Error
        }

        private UiStage currentStage = UiStage.PreLaunchDelay;

        public MainWindow()
        {
            InitializeComponent();
            vhdManager = new VHDManager();
            vhdManager.StatusChanged += OnStatusChanged;
            vhdManager.ReplaceProgressChanged += OnReplaceProgress;
            vhdManager.BlockingChanged += OnBlockingChanged;
            vhdManager.GameCrashed += OnGameCrashed;
            vhdManager.GameStarted += OnGameStarted;
            
            // 注册关机事件监听
            SystemEvents.SessionEnding += OnSessionEnding;
            
            // 开始主流程（包含延迟启动）
            _ = StartMainProcessWithDelay();
        }

        private void SetStage(UiStage stage, string overrideText = null)
        {
            currentStage = stage;
            Dispatcher.Invoke(() =>
            {
                switch (stage)
                {
                    case UiStage.PreLaunchDelay:
                        StatusText.Text = overrideText ?? "程序启动准备中";
                        ProgressBar.Visibility = Visibility.Collapsed;
                        break;
                    case UiStage.PrepareGameFiles:
                        StatusText.Text = "正在准备游戏文件";
                        ProgressBar.Visibility = Visibility.Collapsed;
                        ProgressBar.IsIndeterminate = true;
                        break;
                    case UiStage.ApplyLocalUpdate:
                        StatusText.Text = "正在应用本地更新";
                        ProgressBar.Visibility = Visibility.Visible;
                        ProgressBar.IsIndeterminate = false;
                        ProgressBar.Minimum = 0;
                        ProgressBar.Maximum = 100;
                        ProgressBar.Value = 0;
                        break;
                    case UiStage.PrepareToLaunch:
                        StatusText.Text = "正在准备启动游戏";
                        ProgressBar.Visibility = Visibility.Collapsed;
                        ProgressBar.IsIndeterminate = true;
                        break;
                    case UiStage.UpdateAndLaunch:
                        StatusText.Text = "正在更新和启动游戏程序，请耐心等待";
                        ProgressBar.Visibility = Visibility.Collapsed;
                        ProgressBar.IsIndeterminate = true;
                        this.Topmost = true;
                        this.ShowInTaskbar = true;
                        this.WindowState = WindowState.Maximized;
                        break;
                    case UiStage.CrashRecovering:
                        StatusText.Text = "游戏程序异常退出，正在尝试重启";
                        ProgressBar.Visibility = Visibility.Collapsed;
                        ProgressBar.IsIndeterminate = false;
                        this.Topmost = true;
                        this.ShowInTaskbar = true;
                        this.WindowState = WindowState.Maximized;
                        this.Activate();
                        break;
                    case UiStage.Error:
                        StatusText.Text = "运行发生错误，请联系管理员调阅日志";
                        ProgressBar.Visibility = Visibility.Collapsed;
                        ProgressBar.IsIndeterminate = false;
                        this.Topmost = true;
                        this.ShowInTaskbar = true;
                        this.WindowState = WindowState.Maximized;
                        this.Activate();
                        break;
                }
            });
        }

        private void OnStatusChanged(string status)
        {
            try
            {
                System.Diagnostics.Trace.WriteLine($"UI_STATUS_LOG: {status}");
            }
            catch { }
        }

        private void OnReplaceProgress(FileReplaceProgress progress)
        {
            Dispatcher.Invoke(() =>
            {
                // 仅更新进度条，不显示文件名，避免泄露细节
                double aggregated = 0;
                if (progress.TotalFiles > 0)
                {
                    aggregated = ((progress.FileIndex - 1) * 100.0 / progress.TotalFiles) + (progress.Percentage / progress.TotalFiles);
                }
                ProgressBar.IsIndeterminate = false;
                ProgressBar.Minimum = 0;
                ProgressBar.Maximum = 100;
                ProgressBar.Value = Math.Max(0, Math.Min(100, aggregated));
            });
        }



        private async Task StartMainProcessWithDelay()
        {
            try
            {
                // 程序启动前的十秒准备阶段
                for (int i = 10; i > 0; i--)
                {
                    SetStage(UiStage.PreLaunchDelay, $"程序启动准备中（剩余 {i} 秒）");
                    await Task.Delay(1000);
                }
                SetStage(UiStage.PreLaunchDelay, "准备完成，开始初始化");
                await StartMainProcess();
            }
            catch (Exception ex)
            {
                OnStatusChanged($"延迟启动过程中发生错误: {ex.Message}");
                await ShowFatalErrorAndShutdownAfterDelay();
            }
        }

        private async Task StartMainProcess()
        {
            try
            {
                isProcessing = true;

                // 阶段：正在准备游戏文件（远程关键词获取 + 扫描）
                SetStage(UiStage.PrepareGameFiles);
                string remoteSelectedKeyword = await vhdManager.GetRemoteVHDSelection();
                var nxInsUSB = vhdManager.FindNXInsUSBDrive();
                List<string> usbVhdFiles = nxInsUSB != null ? vhdManager.ScanUSBForVHDFiles(nxInsUSB) : new List<string>();
                var localVhdFiles = await vhdManager.ScanForVHDFiles();

                // 如果找到USB设备和VHD文件，替换本地文件
                if (nxInsUSB != null && usbVhdFiles.Count > 0 && localVhdFiles.Count > 0)
                {
                    // 阶段：应用本地更新（显示进度）
                    SetStage(UiStage.ApplyLocalUpdate);
                    bool replaced = await vhdManager.ReplaceLocalVHDFiles(usbVhdFiles, localVhdFiles);
                    // 替换完成后重新扫描
                    localVhdFiles = await vhdManager.ScanForVHDFiles();
                    SetStage(UiStage.PrepareGameFiles);
                }

                // 当且仅当本地列表为空，且USB有结果时，直接复制到D:\ 根目录
                if (localVhdFiles.Count == 0 && nxInsUSB != null && usbVhdFiles.Count > 0)
                {
                    // 阶段：应用本地更新（USB复制到D盘）
                    SetStage(UiStage.ApplyLocalUpdate);
                    bool copied = await vhdManager.CopyUsbFilesToDriveRoot(usbVhdFiles, "D");
                    localVhdFiles = await vhdManager.ScanForVHDFiles();
                    SetStage(UiStage.PrepareGameFiles);
                }
                
                if (localVhdFiles.Count == 0)
                {
                    OnStatusChanged("未找到符合条件的VHD文件");
                    await ShowFatalErrorAndShutdownAfterDelay();
                    return;
                }
                
                string selectedVHD;
                
                // 如果有远程选择的关键词，优先使用
                if (!string.IsNullOrWhiteSpace(remoteSelectedKeyword))
                {
                    selectedVHD = vhdManager.FindVHDByKeyword(localVhdFiles, remoteSelectedKeyword);
                    if (selectedVHD != null)
                    {
                        await ProcessSelectedVHD(selectedVHD);
                        return;
                    }
                }
                
                if (localVhdFiles.Count == 1)
                {
                    selectedVHD = localVhdFiles[0];
                    await ProcessSelectedVHD(selectedVHD);
                }
                else
                {
                    // 显示选择器
                    availableVHDs = localVhdFiles;
                    ShowVHDSelector(localVhdFiles);
                }
            }
            catch (Exception ex)
            {
                OnStatusChanged($"发生错误: {ex.Message}");
                await ShowFatalErrorAndShutdownAfterDelay();
            }
        }

        private void OnBlockingChanged(bool blocking, string message)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    VHDListBox.IsEnabled = !blocking;
                }
                catch { }
            });
        }

        private void ShowVHDSelector(List<string> vhdFiles)
        {
            Dispatcher.Invoke(() =>
            {
                VHDListBox.ItemsSource = vhdFiles.Select(f => System.IO.Path.GetFileName(f)).ToList();
                VHDListBox.SelectedIndex = 0;
                VHDSelector.Visibility = Visibility.Visible;
                ProgressBar.Visibility = Visibility.Collapsed;
            });
        }

        private async Task ProcessSelectedVHD(string vhdPath)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    VHDSelector.Visibility = Visibility.Collapsed;
                });

                // 阶段：准备启动游戏（EVHD密钥获取至启动前）
                SetStage(UiStage.PrepareToLaunch);

                // 挂载VHD或EVHD
                var ext = System.IO.Path.GetExtension(vhdPath)?.ToLowerInvariant();
                bool isEvhd = ext == ".evhd";
                bool mounted = isEvhd
                    ? await vhdManager.MountEVHDAndAttachDecryptedVHD(vhdPath)
                    : await vhdManager.MountVHD(vhdPath);
                if (!mounted)
                {
                    OnStatusChanged("挂载失败");
                    await ShowFatalErrorAndShutdownAfterDelay();
                    return;
                }

                // 根据文件名选择目标目录（SDHD -> bin，否则 -> package），忽略大小写
                bool isSDHD = System.IO.Path.GetFileName(vhdPath)
                    .IndexOf("SDHD", StringComparison.OrdinalIgnoreCase) >= 0;

                string targetFolder;
                if (isSDHD)
                {
                    targetFolder = await vhdManager.FindFolder("bin");
                }
                else
                {
                    targetFolder = await vhdManager.FindPackageFolder();
                }

                if (string.IsNullOrEmpty(targetFolder))
                {
                    OnStatusChanged(isSDHD ? "未找到bin目录" : "未找到package文件夹");
                    await ShowFatalErrorAndShutdownAfterDelay();
                    return;
                }

                // 阶段：正在更新和启动游戏程序，请耐心等待
                SetStage(UiStage.UpdateAndLaunch);
                // 启动start.bat
                bool started = await vhdManager.StartBatchFile(targetFolder);
                if (!started)
                {
                    OnStatusChanged("启动start.bat失败");
                    await ShowFatalErrorAndShutdownAfterDelay();
                    return;
                }

                // 更新期：如果 NxClient.exe 正在运行，则保持置顶显示
                while (vhdManager.IsProcessRunningByName("NxClient"))
                {
                    await Task.Delay(1000);
                }

                // 等待游戏进程启动
                while (!vhdManager.IsTargetProcessRunning())
                {
                    await Task.Delay(1000);
                }
                // 游戏启动后，等待10秒再切换窗口
                await Task.Delay(10000);
                var proc = vhdManager.GetFirstTargetProcess();
                if (proc != null)
                {
                    var focused = vhdManager.FocusProcessWindow(proc);
                    if (!focused)
                    {
                        OnStatusChanged("切换到游戏窗口失败，窗口可能尚未就绪");
                    }
                }

                // 切换后隐藏自身 UI 并进入监控（异常重启使用 start_game.bat）
                Dispatcher.Invoke(() =>
                {
                    this.WindowState = WindowState.Minimized;
                    this.ShowInTaskbar = false;
                });
                await vhdManager.MonitorAndRestart(targetFolder);
            }
            catch (Exception ex)
            {
                OnStatusChanged($"处理过程中发生错误: {ex.Message}");
                await ShowFatalErrorAndShutdownAfterDelay();
            }
        }

        private async Task ShowFatalErrorAndShutdownAfterDelay()
        {
            try
            {
                SetStage(UiStage.Error);
            }
            catch { }
            // 展示 5 分钟
            await Task.Delay(TimeSpan.FromMinutes(5));
            // 执行关机指令
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "shutdown",
                    Arguments = "/s /t 0",
                    UseShellExecute = true,
                    CreateNoWindow = true
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                OnStatusChanged($"执行关机指令失败: {ex.Message}");
                // 失败时，至少关闭应用
                try { Application.Current.Shutdown(); } catch { }
            }
        }

        private void OnGameCrashed()
        {
            SetStage(UiStage.CrashRecovering);
        }

        private void OnGameStarted()
        {
            // 游戏已正常启动后，隐藏自身界面
            Dispatcher.Invoke(() =>
            {
                this.WindowState = WindowState.Minimized;
                this.ShowInTaskbar = false;
            });
        }

        private async void Window_KeyDown(object sender, KeyEventArgs e)
        {
            // 全局检测 Delete 三击
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
                    await SafeShutdown();
                    return;
                }
            }

            // 仅在选择器可见时处理上下与回车
            if (VHDSelector.Visibility == Visibility.Visible)
            {
                switch (e.Key)
                {
                    case Key.Up:
                        if (VHDListBox.SelectedIndex > 0)
                            VHDListBox.SelectedIndex--;
                        break;
                    case Key.Down:
                        if (VHDListBox.SelectedIndex < VHDListBox.Items.Count - 1)
                            VHDListBox.SelectedIndex++;
                        break;
                    case Key.Enter:
                        if (!isProcessing && VHDListBox.SelectedIndex >= 0 && availableVHDs != null)
                        {
                            isProcessing = true;
                            string selectedVHD = availableVHDs[VHDListBox.SelectedIndex];
                            await ProcessSelectedVHD(selectedVHD);
                        }
                        break;
                }
            }
        }

        

        protected override void OnClosed(EventArgs e)
        {
            // 程序关闭时卸载VHD
            try
            {
                _ = vhdManager.UnmountVHD();
                vhdManager.StopEncryptedEvhdMount();
            }
            catch { }
            
            // 取消注册关机事件
            SystemEvents.SessionEnding -= OnSessionEnding;
            
            base.OnClosed(e);
        }

        private async void OnSessionEnding(object sender, SessionEndingEventArgs e)
        {
            try
            {
                // 收到关机信号，解除VHD挂载
                OnStatusChanged("检测到系统关机，正在解除VHD挂载...");
                await vhdManager.UnmountVHD();
                vhdManager.StopEncryptedEvhdMount();
                OnStatusChanged("VHD解除挂载完成，程序即将退出");
            }
            catch (Exception ex)
            {
                OnStatusChanged($"关机时解除VHD挂载失败: {ex.Message}");
            }
        }

        private async Task SafeShutdown()
        {
            try
            {
                // 确保在退出前解除VHD挂载
                await vhdManager.UnmountVHD();
                // 然后结束加密EVHD挂载进程
                vhdManager.StopEncryptedEvhdMount();
            }
            catch (Exception ex)
            {
                OnStatusChanged($"退出时解除VHD挂载失败: {ex.Message}");
            }
            finally
            {
                Application.Current.Shutdown();
            }
        }
    }
}
