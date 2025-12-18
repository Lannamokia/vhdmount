using System;
using System.Diagnostics;
using System.IO;
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

                // 初始化文件日志：将 Debug/Trace 输出到应用目录下的 vhdmounter.log
                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vhdmounter.log");
                var logStream = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.Read);
                var fileListener = new TextWriterTraceListener(logStream, "VHDMounterFileLogger");
                Trace.Listeners.Add(fileListener);
                Trace.AutoFlush = true;
                Trace.WriteLine($"==== VHDMounter 启动 {DateTime.Now:yyyy-MM-dd HH:mm:ss} ====");

                // 记录运行环境与 RID 相关路径
                try
                {
                    Trace.WriteLine($"BaseDirectory: {AppContext.BaseDirectory}");
                    var ridLibPath = Path.Combine(AppContext.BaseDirectory, "runtimes", "win10-x64", "lib", "netstandard1.6");
                    Trace.WriteLine($"RID lib path: {ridLibPath} Exists={Directory.Exists(ridLibPath)}");
                }
                catch { }

                // 绑定程序集解析事件，确保 Microsoft.Management.Infrastructure 能被解析
                AppDomain.CurrentDomain.AssemblyResolve += (sender, ev) =>
                {
                    try
                    {
                        if (ev.Name.StartsWith("Microsoft.Management.Infrastructure", StringComparison.OrdinalIgnoreCase))
                        {
                            var candidates = new[]
                            {
                                Path.Combine(AppContext.BaseDirectory, "Microsoft.Management.Infrastructure.dll"),
                                Path.Combine(AppContext.BaseDirectory, "runtimes", "win10-x64", "lib", "netstandard1.6", "Microsoft.Management.Infrastructure.dll"),
                                Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "lib", "netstandard1.6", "Microsoft.Management.Infrastructure.dll")
                            };
                            foreach (var p in candidates)
                            {
                                if (File.Exists(p))
                                {
                                    Trace.WriteLine($"AssemblyResolve: 加载 {p}");
                                    return System.Reflection.Assembly.LoadFrom(p);
                                }
                            }
                            Trace.WriteLine("AssemblyResolve: 未找到 Microsoft.Management.Infrastructure.dll");
                        }
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"AssemblyResolve 异常: {ex}");
                    }
                    return null;
                };

                var app = new Application();
                var mainWindow = new MainWindow();
                app.Run(mainWindow);

                try
                {
                    fileListener.Flush();
                    fileListener.Close();
                }
                catch { }
            }
        }
    }
}
