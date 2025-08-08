using System;
using System.Threading.Tasks;

namespace VHDMounter
{
    class DebugProgram
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("VHD Mounter 调试工具");
            Console.WriteLine("===================");
            
            var debugger = new VHDDebugger();
            
            // 测试特定文件
            var testFile = @"C:\SDEZ_1.56.00_20250317134137.vhd";
            debugger.TestSpecificFile(testFile);
            
            Console.WriteLine("\n按任意键开始扫描所有驱动器...");
            Console.ReadKey();
            
            // 扫描所有VHD文件
            var vhdFiles = await debugger.DebugScanVHDFiles();
            
            Console.WriteLine("\n=== 总结 ===");
            if (vhdFiles.Count > 0)
            {
                Console.WriteLine($"✅ 成功找到 {vhdFiles.Count} 个符合条件的VHD文件");
                Console.WriteLine("\n如果主程序仍然找不到文件，可能的原因:");
                Console.WriteLine("1. 程序没有以管理员身份运行");
                Console.WriteLine("2. 文件被其他程序占用");
                Console.WriteLine("3. 权限不足");
            }
            else
            {
                Console.WriteLine("❌ 未找到符合条件的VHD文件");
                Console.WriteLine("\n请检查:");
                Console.WriteLine("1. VHD文件是否存在");
                Console.WriteLine("2. 文件名是否包含 SDEZ、SDHD 或 SDDT 关键词");
                Console.WriteLine("3. 文件扩展名是否为 .vhd");
            }
            
            Console.WriteLine("\n按任意键退出...");
            Console.ReadKey();
        }
    }
}