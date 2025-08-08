using System;
using System.Windows;

namespace VHDMounter
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
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