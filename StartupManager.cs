using Microsoft.Win32;
using System;
using System.IO;
using System.Reflection;

namespace VHDMounter
{
    public class StartupManager
    {
        private const string APP_NAME = "VHDMounter";
        private const string REGISTRY_KEY = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

        public static bool IsRegisteredForStartup()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(REGISTRY_KEY, false))
                {
                    return key?.GetValue(APP_NAME) != null;
                }
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
                var exePath = Assembly.GetExecutingAssembly().Location;
                if (exePath.EndsWith(".dll"))
                {
                    // 如果是.dll，需要找到对应的.exe
                    exePath = exePath.Replace(".dll", ".exe");
                }

                using (var key = Registry.CurrentUser.OpenSubKey(REGISTRY_KEY, true))
                {
                    key?.SetValue(APP_NAME, $"\"{exePath}\"");
                }
                
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"注册开机启动失败: {ex.Message}");
                return false;
            }
        }

        public static bool UnregisterFromStartup()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(REGISTRY_KEY, true))
                {
                    key?.DeleteValue(APP_NAME, false);
                }
                
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"取消开机启动失败: {ex.Message}");
                return false;
            }
        }
    }
}