using System;
using System.Linq;
using System.ServiceProcess;
using System.Windows;

namespace VHDMounter
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // 检查是否作为Windows服务运行
            if (args.Contains("--service") || Environment.UserInteractive == false)
            {
                // 作为Windows服务运行
                ServiceBase[] ServicesToRun;
                ServicesToRun = new ServiceBase[]
                {
                    new VHDMounterService()
                };
                ServiceBase.Run(ServicesToRun);
            }
            else
            {
                // 作为WPF应用程序运行
                RunAsApplication();
            }
        }

        private static void RunAsApplication()
        {
            // 检查是否已有实例运行
            using (var mutex = new System.Threading.Mutex(true, "VHDMounterApp", out bool createdNew))
            {
                if (!createdNew)
                {
                    return; // 已有实例运行，退出
                }

                var app = new Application();
                var mainWindow = new MainWindow();
                app.Run(mainWindow);
            }
        }
    }
}