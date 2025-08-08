using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

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
            
            // 注册开机启动
            if (!StartupManager.IsRegisteredForStartup())
            {
                StartupManager.RegisterForStartup();
            }
            
            // 开始主流程
            _ = StartMainProcess();
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

        private async Task StartMainProcess()
        {
            try
            {
                isProcessing = true;
                
                // 扫描VHD文件
                var vhdFiles = await vhdManager.ScanForVHDFiles();
                
                if (vhdFiles.Count == 0)
                {
                    OnStatusChanged("未找到符合条件的VHD文件");
                    await Task.Delay(3000);
                    Application.Current.Shutdown();
                    return;
                }
                
                string selectedVHD;
                
                if (vhdFiles.Count == 1)
                {
                    selectedVHD = vhdFiles[0];
                    await ProcessSelectedVHD(selectedVHD);
                }
                else
                {
                    // 显示选择器
                    availableVHDs = vhdFiles;
                    ShowVHDSelector(vhdFiles);
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
                    Application.Current.Shutdown();
                    return;
                }

                // 查找package文件夹
                string packagePath = await vhdManager.FindPackageFolder();
                if (string.IsNullOrEmpty(packagePath))
                {
                    OnStatusChanged("未找到package文件夹");
                    await Task.Delay(3000);
                    Application.Current.Shutdown();
                    return;
                }

                // 启动start.bat
                bool started = await vhdManager.StartBatchFile(packagePath);
                if (!started)
                {
                    OnStatusChanged("启动start.bat失败");
                    await Task.Delay(3000);
                    Application.Current.Shutdown();
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
                await vhdManager.MonitorAndRestart(packagePath);
            }
            catch (Exception ex)
            {
                OnStatusChanged($"处理过程中发生错误: {ex.Message}");
                await Task.Delay(5000);
                Application.Current.Shutdown();
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
                    Application.Current.Shutdown();
                    break;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        protected override void OnClosed(EventArgs e)
        {
            // 程序关闭时卸载VHD
            try
            {
                _ = vhdManager.UnmountDrive();
            }
            catch { }
            
            base.OnClosed(e);
        }
    }
}