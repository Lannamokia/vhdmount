using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace VHDMounter
{
    public partial class MainWindow : Window
    {
        private VHDManager vhdManager;
        private List<string> availableVHDs;
        private bool isProcessing = false;

        public MainWindow()
        {
            InitializeComponent();
            vhdManager = new VHDManager();
            vhdManager.StatusChanged += OnStatusChanged;
            vhdManager.VHDFilesFound += OnVHDFilesFound;
            
            // 注册关机事件监听
            SystemEvents.SessionEnding += OnSessionEnding;
            
            // 开始主流程（包含延迟启动）
            _ = StartMainProcessWithDelay();
        }

        private void OnStatusChanged(string status)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = status;
            });
        }

        private void OnVHDFilesFound(List<string> vhdFiles)
        {
            Dispatcher.Invoke(() =>
            {
                availableVHDs = vhdFiles;
                ShowVHDSelector(vhdFiles);
            });
        }

        private async Task StartMainProcessWithDelay()
        {
            try
            {
                // 开机延迟10秒启动
                OnStatusChanged("程序启动中，等待10秒...");
                for (int i = 10; i > 0; i--)
                {
                    OnStatusChanged($"程序启动中，等待{i}秒...");
                    await Task.Delay(1000);
                }
                
                OnStatusChanged("延迟完成，开始主流程...");
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
                
                // 检查是否存在NX_INS USB设备
                OnStatusChanged("正在检查NX_INS USB设备...");
                var nxInsUSB = vhdManager.FindNXInsUSBDrive();
                List<string> usbVhdFiles = new List<string>();
                
                if (nxInsUSB != null)
                {
                    OnStatusChanged($"找到NX_INS USB设备: {nxInsUSB.Name}");
                    // 扫描USB设备中的VHD文件
                    usbVhdFiles = vhdManager.ScanUSBForVHDFiles(nxInsUSB);
                    OnStatusChanged($"在USB设备中找到 {usbVhdFiles.Count} 个VHD文件");
                }
                else
                {
                    OnStatusChanged("未找到NX_INS USB设备，将使用本地VHD文件");
                }
                
                // 扫描本地VHD文件
                var localVhdFiles = await vhdManager.ScanForVHDFiles();
                
                // 如果找到USB设备和VHD文件，替换本地文件
                if (nxInsUSB != null && usbVhdFiles.Count > 0 && localVhdFiles.Count > 0)
                {
                    OnStatusChanged("开始替换本地VHD文件...");
                    bool replaced = await vhdManager.ReplaceLocalVHDFiles(usbVhdFiles, localVhdFiles);
                    if (replaced)
                    {
                        OnStatusChanged("本地VHD文件已替换，重新扫描本地文件...");
                        // 重新扫描本地文件
                        localVhdFiles = await vhdManager.ScanForVHDFiles();
                    }
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
                    selectedVHD = vhdManager.FindVHDByKeyword(localVhdFiles, remoteSelectedKeyword);
                    if (selectedVHD != null)
                    {
                        OnStatusChanged($"根据远程选择启动VHD: {System.IO.Path.GetFileName(selectedVHD)}");
                        await ProcessSelectedVHD(selectedVHD);
                        return;
                    }
                    else
                    {
                        OnStatusChanged($"远程选择的关键词 '{remoteSelectedKeyword}' 未找到对应VHD文件，将显示选择器");
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

                // 挂载VHD
                bool mounted = await vhdManager.MountVHD(vhdPath);
                if (!mounted)
                {
                    OnStatusChanged("VHD挂载失败");
                    await Task.Delay(3000);
                    await SafeShutdown();
                    return;
                }

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

                // 启动start.bat
                bool started = await vhdManager.StartBatchFile(targetFolder);
                if (!started)
                {
                    OnStatusChanged("启动start.bat失败");
                    await Task.Delay(3000);
                    await SafeShutdown();
                    return;
                }

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