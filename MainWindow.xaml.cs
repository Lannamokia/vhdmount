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

        // 使状态消息按队列显示，每条至少停留2秒
        private readonly Queue<string> statusQueue = new Queue<string>();
        private DispatcherTimer statusTimer;
        // 即时模式：用于启动前10秒倒计时，实时刷新不排队
        private bool statusImmediateMode = false;

        public MainWindow()
        {
            InitializeComponent();
            vhdManager = new VHDManager();
            vhdManager.StatusChanged += OnStatusChanged;
            vhdManager.ReplaceProgressChanged += OnReplaceProgress;
            vhdManager.BlockingChanged += OnBlockingChanged;
            
            // 注册关机事件监听
            SystemEvents.SessionEnding += OnSessionEnding;

            // 初始化状态队列显示定时器（每条消息显示2秒）
            statusTimer = new DispatcherTimer();
            statusTimer.Interval = TimeSpan.FromSeconds(2);
            statusTimer.Tick += StatusTimer_Tick;
            
            // 开始主流程（包含延迟启动）
            _ = StartMainProcessWithDelay();
        }

        private void OnStatusChanged(string status)
        {
            Dispatcher.Invoke(() =>
            {
                // 即时模式：直接显示当前消息，清空队列并停止计时器
                if (statusImmediateMode)
                {
                    statusTimer.Stop();
                    statusQueue.Clear();
                    StatusText.Text = status;
                    return;
                }
                // 入队消息；如果当前没有在显示循环中，则立即显示并启动定时器
                statusQueue.Enqueue(status);
                if (!statusTimer.IsEnabled)
                {
                    StatusText.Text = statusQueue.Dequeue();
                    statusTimer.Start();
                }
            });
        }

        // 每次触发时显示下一条状态消息；无消息时停止定时器
        private void StatusTimer_Tick(object sender, EventArgs e)
        {
            if (statusQueue.Count > 0)
            {
                StatusText.Text = statusQueue.Dequeue();
            }
            else
            {
                statusTimer.Stop();
            }
        }

        private void OnReplaceProgress(FileReplaceProgress progress)
        {
            Dispatcher.Invoke(() =>
            {
                // 聚合整体进度（当前文件在总数中的进度占比）
                double aggregated = 0;
                if (progress.TotalFiles > 0)
                {
                    aggregated = ((progress.FileIndex - 1) * 100.0 / progress.TotalFiles) + (progress.Percentage / progress.TotalFiles);
                }

                ProgressBar.IsIndeterminate = false;
                ProgressBar.Minimum = 0;
                ProgressBar.Maximum = 100;
                ProgressBar.Value = Math.Max(0, Math.Min(100, aggregated));

                StatusText.Text = $"正在复制 {progress.CurrentFileName} ({progress.FileIndex}/{progress.TotalFiles}) - {progress.Percentage:F1}%";
            });
        }



        private async Task StartMainProcessWithDelay()
        {
            try
            {
                // 开机延迟10秒启动
                statusImmediateMode = true; // 倒计时期间开启即时模式
                OnStatusChanged("程序启动中，等待10秒...");
                for (int i = 10; i > 0; i--)
                {
                    OnStatusChanged($"程序启动中，等待{i}秒...");
                    await Task.Delay(1000);
                }
                
                OnStatusChanged("延迟完成，开始主流程...");
                statusImmediateMode = false; // 倒计时结束，恢复队列显示（每条停留2秒）
                await StartMainProcess();
            }
            catch (Exception ex)
            {
                OnStatusChanged($"延迟启动过程中发生错误: {ex.Message}");
                await Task.Delay(5000);
                await SafeShutdown();
            }
        }

        private async Task StartMainProcess()
        {
            try
            {
                isProcessing = true;
                
                // 尝试远程获取VHD选择
                OnStatusChanged("正在检查远程VHD选择配置...");
                string remoteSelectedKeyword = await vhdManager.GetRemoteVHDSelection();
                OnStatusChanged($"远程VHD选择检查完成: {(string.IsNullOrWhiteSpace(remoteSelectedKeyword) ? "无远程选择" : $"选择关键词: {remoteSelectedKeyword}")}");
                await Task.Delay(2000); // 暂停2秒显示结果
                
                // 检查是否存在NX_INS USB设备
                OnStatusChanged("正在检查NX_INS USB设备...");
                var nxInsUSB = vhdManager.FindNXInsUSBDrive();
                List<string> usbVhdFiles = new List<string>();
                
                if (nxInsUSB != null)
                {
                    OnStatusChanged($"找到NX_INS USB设备: {nxInsUSB.Name}");
                    await Task.Delay(2000); // 暂停2秒显示结果
                    // 扫描USB设备中的VHD文件
                    OnStatusChanged("正在扫描USB设备中的VHD文件...");
                    usbVhdFiles = vhdManager.ScanUSBForVHDFiles(nxInsUSB);
                    OnStatusChanged($"在USB设备中找到 {usbVhdFiles.Count} 个VHD文件");
                    await Task.Delay(2000); // 暂停2秒显示结果
                }
                else
                {
                    OnStatusChanged("未找到NX_INS USB设备，将使用本地VHD文件");
                    await Task.Delay(2000); // 暂停2秒显示结果
                }
                
                // 扫描本地VHD文件
                OnStatusChanged("正在扫描本地VHD文件...");
                var localVhdFiles = await vhdManager.ScanForVHDFiles();
                OnStatusChanged($"本地VHD文件扫描完成，找到 {localVhdFiles.Count} 个文件");
                await Task.Delay(2000); // 暂停2秒显示结果

                // 如果找到USB设备和VHD文件，替换本地文件
                if (nxInsUSB != null && usbVhdFiles.Count > 0 && localVhdFiles.Count > 0)
                {
                    OnStatusChanged("开始替换本地VHD文件...");
                    Dispatcher.Invoke(() =>
                    {
                        ProgressBar.Visibility = Visibility.Visible;
                        ProgressBar.IsIndeterminate = false;
                        ProgressBar.Minimum = 0;
                        ProgressBar.Maximum = 100;
                        ProgressBar.Value = 0;
                    });
                    bool replaced = await vhdManager.ReplaceLocalVHDFiles(usbVhdFiles, localVhdFiles);
                    if (replaced)
                    {
                        OnStatusChanged("本地VHD文件已替换，重新扫描本地文件...");
                        // 重新扫描本地文件
                        localVhdFiles = await vhdManager.ScanForVHDFiles();
                        OnStatusChanged($"重新扫描完成，找到 {localVhdFiles.Count} 个文件");
                    }
                    else
                    {
                        OnStatusChanged("VHD文件替换失败或无需替换");
                    }
                    Dispatcher.Invoke(() =>
                    {
                        // 替换阶段结束，恢复不确定进度或清零
                        ProgressBar.IsIndeterminate = true;
                        ProgressBar.Value = 0;
                    });
                    await Task.Delay(2000); // 暂停2秒显示结果
                }

                // 当且仅当本地列表为空，且USB有结果时，直接复制到D:\ 根目录
                if (localVhdFiles.Count == 0 && nxInsUSB != null && usbVhdFiles.Count > 0)
                {
                    OnStatusChanged("本地无VHD/EVHD，准备将USB文件复制到 D:\\ 根目录...");
                    Dispatcher.Invoke(() =>
                    {
                        ProgressBar.Visibility = Visibility.Visible;
                        ProgressBar.IsIndeterminate = false;
                        ProgressBar.Minimum = 0;
                        ProgressBar.Maximum = 100;
                        ProgressBar.Value = 0;
                    });

                    bool copied = await vhdManager.CopyUsbFilesToDriveRoot(usbVhdFiles, "D");
                    if (copied)
                    {
                        OnStatusChanged("复制完成，重新扫描本地VHD文件...");
                        localVhdFiles = await vhdManager.ScanForVHDFiles();
                        OnStatusChanged($"重新扫描完成，找到 {localVhdFiles.Count} 个文件");
                    }
                    else
                    {
                        OnStatusChanged("未能复制USB上的VHD/EVHD文件到D盘");
                    }

                    Dispatcher.Invoke(() =>
                    {
                        ProgressBar.IsIndeterminate = true;
                        ProgressBar.Value = 0;
                    });
                    await Task.Delay(2000); // 暂停2秒显示结果
                }
                
                if (localVhdFiles.Count == 0)
                {
                    OnStatusChanged("未找到符合条件的VHD文件");
                    OnStatusChanged("请检查文件名是否包含SDEZ、SDHD或SDDT关键词");
                    await Task.Delay(5000);
                    await SafeShutdown();
                    return;
                }
                
                string selectedVHD;
                
                // 如果有远程选择的关键词，优先使用
                if (!string.IsNullOrWhiteSpace(remoteSelectedKeyword))
                {
                    OnStatusChanged($"正在根据远程关键词 '{remoteSelectedKeyword}' 查找VHD文件...");
                    selectedVHD = vhdManager.FindVHDByKeyword(localVhdFiles, remoteSelectedKeyword);
                    if (selectedVHD != null)
                    {
                        OnStatusChanged($"根据远程选择启动VHD: {System.IO.Path.GetFileName(selectedVHD)}");
                        await Task.Delay(2000); // 暂停2秒显示结果
                        await ProcessSelectedVHD(selectedVHD);
                        return;
                    }
                    else
                    {
                        OnStatusChanged($"远程选择的关键词 '{remoteSelectedKeyword}' 未找到对应VHD文件，将显示选择器");
                        await Task.Delay(2000); // 暂停2秒显示结果
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
                await Task.Delay(5000);
                Application.Current.Shutdown();
            }
        }

        private void OnBlockingChanged(bool blocking, string message)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    if (blocking)
                    {
                        // 阻塞UI交互并提示信息
                        VHDSelector.Visibility = Visibility.Collapsed;
                        ProgressBar.Visibility = Visibility.Visible;
                        ProgressBar.IsIndeterminate = true;
                        VHDListBox.IsEnabled = false;
                        CloseButton.IsEnabled = false;
                        if (!string.IsNullOrWhiteSpace(message))
                        {
                            StatusText.Text = message;
                        }
                    }
                    else
                    {
                        // 恢复UI交互
                        VHDListBox.IsEnabled = true;
                        CloseButton.IsEnabled = true;
                    }
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
                OnStatusChanged("请选择要挂载的VHD文件");
            });
        }

        private async Task ProcessSelectedVHD(string vhdPath)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    VHDSelector.Visibility = Visibility.Collapsed;
                    ProgressBar.Visibility = Visibility.Visible;
                });

                // 挂载VHD或EVHD
                var ext = System.IO.Path.GetExtension(vhdPath)?.ToLowerInvariant();
                bool isEvhd = ext == ".evhd";
                OnStatusChanged($"正在挂载{(isEvhd ? "EVHD" : "VHD")}文件: {System.IO.Path.GetFileName(vhdPath)}");
                bool mounted = isEvhd
                    ? await vhdManager.MountEVHDAndAttachDecryptedVHD(vhdPath)
                    : await vhdManager.MountVHD(vhdPath);
                if (!mounted)
                {
                    OnStatusChanged("挂载失败");
                    await Task.Delay(3000);
                    await SafeShutdown();
                    return;
                }
                OnStatusChanged("挂载成功");
                await Task.Delay(2000); // 暂停2秒显示结果

                // 根据文件名选择目标目录（SDHD -> bin，否则 -> package），忽略大小写
                bool isSDHD = System.IO.Path.GetFileName(vhdPath)
                    .IndexOf("SDHD", StringComparison.OrdinalIgnoreCase) >= 0;

                string targetFolder;
                if (isSDHD)
                {
                    OnStatusChanged("检测到 SDHD 文件，正在搜索 bin 目录...");
                    targetFolder = await vhdManager.FindFolder("bin");
                }
                else
                {
                    OnStatusChanged("正在搜索 package 目录...");
                    targetFolder = await vhdManager.FindPackageFolder();
                }

                if (string.IsNullOrEmpty(targetFolder))
                {
                    OnStatusChanged(isSDHD ? "未找到bin目录" : "未找到package文件夹");
                    await Task.Delay(3000);
                    await SafeShutdown();
                    return;
                }
                OnStatusChanged($"目标目录搜索完成: {targetFolder}");
                await Task.Delay(2000); // 暂停2秒显示结果

                // 启动start.bat
                OnStatusChanged("正在启动start.bat文件...");
                bool started = await vhdManager.StartBatchFile(targetFolder);
                if (!started)
                {
                    OnStatusChanged("启动start.bat失败");
                    await Task.Delay(3000);
                    await SafeShutdown();
                    return;
                }
                OnStatusChanged("start.bat启动成功");
                await Task.Delay(2000); // 暂停2秒显示结果

                OnStatusChanged("程序启动成功，开始监控...");
                
                // 隐藏窗口并开始监控
                Dispatcher.Invoke(() =>
                {
                    this.WindowState = WindowState.Minimized;
                    this.ShowInTaskbar = false;
                });

                // 开始监控和重启循环
                await vhdManager.MonitorAndRestart(targetFolder);
            }
            catch (Exception ex)
            {
                OnStatusChanged($"处理过程中发生错误: {ex.Message}");
                await Task.Delay(5000);
                await SafeShutdown();
            }
        }

        private async void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (isProcessing || VHDSelector.Visibility != Visibility.Visible)
                return;

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
                    if (VHDListBox.SelectedIndex >= 0 && availableVHDs != null)
                    {
                        isProcessing = true;
                        string selectedVHD = availableVHDs[VHDListBox.SelectedIndex];
                        await ProcessSelectedVHD(selectedVHD);
                    }
                    break;
                    
                case Key.Escape:
                    _ = SafeShutdown();
                    break;
            }
        }

        private async void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            await SafeShutdown();
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