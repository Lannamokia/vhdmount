using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace VHDMounter
{
    public partial class VHDMounterService : ServiceBase
    {
        private CancellationTokenSource cancellationTokenSource;
        private Task serviceTask;
        private VHDManager vhdManager;

        public VHDMounterService()
        {
            InitializeComponent();
            ServiceName = "VHDMounterService";
            CanStop = true;
            CanPauseAndContinue = false;
            AutoLog = true;
        }

        protected override void OnStart(string[] args)
        {
            try
            {
                EventLog.WriteEntry("VHD Mounter 服务开始启动", EventLogEntryType.Information);
                
                cancellationTokenSource = new CancellationTokenSource();
                vhdManager = new VHDManager();
                
                // 启动服务任务
                serviceTask = Task.Run(async () => await RunServiceAsync(cancellationTokenSource.Token));
                
                EventLog.WriteEntry("VHD Mounter 服务已启动", EventLogEntryType.Information);
                
                // 延迟启动WPF应用程序，等待用户登录
                Task.Delay(10000).ContinueWith(_ => 
                {
                    EventLog.WriteEntry("开始启动WPF应用程序", EventLogEntryType.Information);
                    StartWpfApplication();
                });
            }
            catch (Exception ex)
            {
                EventLog.WriteEntry($"VHD Mounter服务启动失败: {ex.Message}", EventLogEntryType.Error);
                throw;
            }
        }

        protected override void OnStop()
        {
            try
            {
                cancellationTokenSource?.Cancel();
                
                // 等待服务任务完成
                serviceTask?.Wait(TimeSpan.FromSeconds(30));
                
                // 卸载VHD
                vhdManager?.UnmountDrive()?.Wait(TimeSpan.FromSeconds(10));
                
                EventLog.WriteEntry("VHD Mounter服务已停止", EventLogEntryType.Information);
            }
            catch (Exception ex)
            {
                EventLog.WriteEntry($"VHD Mounter服务停止时发生错误: {ex.Message}", EventLogEntryType.Warning);
            }
        }

        private async Task RunServiceAsync(CancellationToken cancellationToken)
        {
            try
            {
                // 服务启动延迟10秒
                await Task.Delay(10000, cancellationToken);
                
                if (cancellationToken.IsCancellationRequested)
                    return;

                // 扫描VHD文件
                var vhdFiles = await vhdManager.ScanForVHDFiles();
                
                if (vhdFiles.Count == 0)
                {
                    EventLog.WriteEntry("未找到符合条件的VHD文件", EventLogEntryType.Warning);
                    return;
                }

                // 选择第一个VHD文件进行挂载
                string selectedVHD = vhdFiles[0];
                EventLog.WriteEntry($"选择VHD文件: {selectedVHD}", EventLogEntryType.Information);

                // 挂载VHD
                bool mounted = await vhdManager.MountVHD(selectedVHD);
                if (!mounted)
                {
                    EventLog.WriteEntry("VHD挂载失败", EventLogEntryType.Error);
                    return;
                }

                // 查找package文件夹
                string packagePath = await vhdManager.FindPackageFolder();
                if (string.IsNullOrEmpty(packagePath))
                {
                    EventLog.WriteEntry("未找到package文件夹", EventLogEntryType.Error);
                    return;
                }

                // 启动start.bat
                bool started = await vhdManager.StartBatchFile(packagePath);
                if (!started)
                {
                    EventLog.WriteEntry("启动start.bat失败", EventLogEntryType.Error);
                    return;
                }

                EventLog.WriteEntry("VHD挂载和程序启动成功，开始监控", EventLogEntryType.Information);

                // 开始监控和重启循环
                await vhdManager.MonitorAndRestart(packagePath);
            }
            catch (OperationCanceledException)
            {
                // 服务被取消，正常退出
                EventLog.WriteEntry("VHD Mounter服务任务被取消", EventLogEntryType.Information);
            }
            catch (Exception ex)
            {
                EventLog.WriteEntry($"VHD Mounter服务运行时发生错误: {ex.Message}", EventLogEntryType.Error);
            }
        }



        [DllImport("wtsapi32.dll", SetLastError = true)]
        static extern bool WTSQueryUserToken(uint SessionId, out IntPtr phToken);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern uint WTSGetActiveConsoleSessionId();

        private async void StartWpfApplication()
        {
            try
            {
                // 获取当前可执行文件路径
                string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (exePath.EndsWith(".dll"))
                {
                    exePath = exePath.Replace(".dll", ".exe");
                }

                EventLog.WriteEntry($"尝试启动WPF应用程序: {exePath}", EventLogEntryType.Information);

                // 检查文件是否存在
                if (!System.IO.File.Exists(exePath))
                {
                    EventLog.WriteEntry($"WPF应用程序文件不存在: {exePath}", EventLogEntryType.Error);
                    return;
                }

                // 获取活动控制台会话ID
                uint sessionId = WTSGetActiveConsoleSessionId();
                EventLog.WriteEntry($"当前活动控制台会话ID: {sessionId}", EventLogEntryType.Information);

                // 如果没有活动会话，等待一段时间再试
                if (sessionId == 0xFFFFFFFF) // INVALID_SESSION_ID
                {
                    EventLog.WriteEntry("没有活动的控制台会话，等待用户登录", EventLogEntryType.Warning);
                    await Task.Delay(5000);
                    sessionId = WTSGetActiveConsoleSessionId();
                }

                // 使用多种方法尝试启动WPF应用程序
                bool success = false;

                // 方法1: 使用任务计划程序启动（最可靠的方法）
                try
                {
                    string taskName = "VHDMounterAppLauncher";
                    string taskCommand = $"schtasks /create /tn \"{taskName}\" /tr \"\\\"{exePath}\\\"\" /sc once /st 00:00 /f /rl highest";
                    
                    var createTaskInfo = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c {taskCommand}",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    
                    var createProcess = Process.Start(createTaskInfo);
                    if (createProcess != null)
                    {
                        await createProcess.WaitForExitAsync();
                        if (createProcess.ExitCode == 0)
                        {
                            // 立即运行任务
                            var runTaskInfo = new ProcessStartInfo
                            {
                                FileName = "schtasks",
                                Arguments = $"/run /tn \"{taskName}\"",
                                UseShellExecute = false,
                                CreateNoWindow = true
                            };
                            
                            var runProcess = Process.Start(runTaskInfo);
                            if (runProcess != null)
                            {
                                await runProcess.WaitForExitAsync();
                                if (runProcess.ExitCode == 0)
                                {
                                    EventLog.WriteEntry($"通过任务计划程序启动WPF应用程序成功", EventLogEntryType.Information);
                                    success = true;
                                    
                                    // 删除临时任务
                                    await Task.Delay(2000);
                                    var deleteTaskInfo = new ProcessStartInfo
                                    {
                                        FileName = "schtasks",
                                        Arguments = $"/delete /tn \"{taskName}\" /f",
                                        UseShellExecute = false,
                                        CreateNoWindow = true
                                    };
                                    Process.Start(deleteTaskInfo);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    EventLog.WriteEntry($"通过任务计划程序启动失败: {ex.Message}", EventLogEntryType.Warning);
                }

                // 方法2: 使用PowerShell启动（适用于用户会话）
                if (!success)
                {
                    try
                    {
                        var psStartInfo = new ProcessStartInfo
                        {
                            FileName = "powershell.exe",
                            Arguments = $"-Command \"Start-Process -FilePath '{exePath}' -WindowStyle Normal\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        
                        var psProcess = Process.Start(psStartInfo);
                        if (psProcess != null)
                        {
                            await psProcess.WaitForExitAsync();
                            EventLog.WriteEntry($"通过PowerShell启动WPF应用程序成功", EventLogEntryType.Information);
                            success = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        EventLog.WriteEntry($"通过PowerShell启动失败: {ex.Message}", EventLogEntryType.Warning);
                    }
                }

                // 方法3: 使用explorer.exe启动
                if (!success)
                {
                    try
                    {
                        var explorerStartInfo = new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = $"\"{exePath}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        
                        var explorerProcess = Process.Start(explorerStartInfo);
                        if (explorerProcess != null)
                        {
                            EventLog.WriteEntry($"通过explorer.exe启动WPF应用程序成功", EventLogEntryType.Information);
                            success = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        EventLog.WriteEntry($"通过explorer.exe启动失败: {ex.Message}", EventLogEntryType.Warning);
                    }
                }

                if (!success)
                {
                    EventLog.WriteEntry("所有启动方法都失败了，请检查用户是否已登录以及应用程序权限", EventLogEntryType.Error);
                }
            }
            catch (Exception ex)
            {
                EventLog.WriteEntry($"启动WPF应用程序时发生错误: {ex.Message}\n堆栈跟踪: {ex.StackTrace}", EventLogEntryType.Error);
            }
        }

        private void InitializeComponent()
        {
            // 服务组件初始化
        }
    }

    // 注意：服务安装现在通过 install_service.bat 和 sc 命令进行管理
    // 不再需要 System.Configuration.Install.Installer
}