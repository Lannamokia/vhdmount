using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.ServiceProcess;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace VHDMounter
{
    public class StartupManager
    {
        private const string SERVICE_NAME = "VHDMounterService";
        private const string SERVICE_DISPLAY_NAME = "VHD Mounter Service";
        private const string SERVICE_DESCRIPTION = "VHD文件自动挂载服务（开机自动启动，服务拉起窗口）";

        public static bool IsRegisteredForStartup()
        {
            try
            {
                // 检查服务是否存在
                bool serviceExists = false;
                var services = ServiceController.GetServices();
                foreach (var service in services)
                {
                    if (service.ServiceName == SERVICE_NAME)
                    {
                        serviceExists = true;
                        break;
                    }
                }
                
                return serviceExists;
            }
            catch
            {
                return false;
            }
        }

        public static bool RegisterForStartup()
        {
            try
            {
                // 先尝试删除已存在的服务
                UnregisterFromStartup();
                
                var exePath = Assembly.GetExecutingAssembly().Location;
                if (exePath.EndsWith(".dll"))
                {
                    // 如果是.dll，需要找到对应的.exe
                    exePath = exePath.Replace(".dll", ".exe");
                }

                // 创建Windows服务（开机自动启动）
                var createCommand = $"sc create {SERVICE_NAME} binPath= \"{exePath} --service\" start= auto DisplayName= \"{SERVICE_DISPLAY_NAME}\"";
                var createResult = RunCommand(createCommand);
                
                if (createResult)
                {
                    // 设置服务描述
                    var descCommand = $"sc description {SERVICE_NAME} \"{SERVICE_DESCRIPTION}\"";
                    RunCommand(descCommand);
                    
                    // 设置服务失败恢复选项
                    var failureCommand = $"sc failure {SERVICE_NAME} reset= 86400 actions= restart/60000/restart/60000/restart/60000";
                    RunCommand(failureCommand);
                    

                    
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"注册Windows服务失败: {ex.Message}");
                return false;
            }
        }

        public static bool UnregisterFromStartup()
        {
            try
            {
                // 停止服务
                var stopCommand = $"sc stop {SERVICE_NAME}";
                RunCommand(stopCommand);
                
                // 删除服务
                var deleteCommand = $"sc delete {SERVICE_NAME}";
                var deleteResult = RunCommand(deleteCommand);
                

                
                return deleteResult;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"删除Windows服务失败: {ex.Message}");
                return false;
            }
        }
        
        public static bool StartService()
        {
            try
            {
                var startCommand = $"sc start {SERVICE_NAME}";
                return RunCommand(startCommand);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"启动Windows服务失败: {ex.Message}");
                return false;
            }
        }
        
        public static bool StopService()
        {
            try
            {
                var stopCommand = $"sc stop {SERVICE_NAME}";
                return RunCommand(stopCommand);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"停止Windows服务失败: {ex.Message}");
                return false;
            }
        }

        private static bool RunCommand(string command)
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {command}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    Verb = "runas" // 以管理员权限运行
                };

                using (var process = Process.Start(processInfo))
                {
                    process?.WaitForExit();
                    return process?.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"执行命令失败: {command}, 错误: {ex.Message}");
                return false;
            }
        }
        

    }
}