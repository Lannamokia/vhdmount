using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace VHDMounter
{
    public class VHDDebugger
    {
        private readonly string[] TARGET_KEYWORDS = { "SDEZ", "SDHD", "SDDT" };

        public async Task<List<string>> DebugScanVHDFiles()
        {
            Console.WriteLine("=== VHD文件扫描调试 ===");
            var vhdFiles = new List<string>();

            await Task.Run(() =>
            {
                var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed);
                Console.WriteLine($"找到 {drives.Count()} 个可用驱动器");

                foreach (var drive in drives)
                {
                    Console.WriteLine($"\n正在扫描驱动器: {drive.Name}");
                    Console.WriteLine($"驱动器类型: {drive.DriveType}");
                    Console.WriteLine($"可用空间: {drive.AvailableFreeSpace / (1024 * 1024 * 1024)} GB");

                    try
                    {
                        // 只扫描根目录
                        Console.WriteLine("扫描根目录...");
                        var allVhdFiles = Directory.GetFiles(drive.RootDirectory.FullName, "*.vhd", SearchOption.TopDirectoryOnly);
                        Console.WriteLine($"根目录找到 {allVhdFiles.Length} 个.vhd文件");

                        foreach (var file in allVhdFiles)
                        {
                            var fileName = Path.GetFileName(file);
                            var isMatch = TARGET_KEYWORDS.Any(keyword => fileName.ToUpper().Contains(keyword));
                            Console.WriteLine($"  文件: {fileName} - {(isMatch ? "匹配" : "不匹配")}");
                            
                            if (isMatch)
                            {
                                vhdFiles.Add(file);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"扫描驱动器 {drive.Name} 时出错: {ex.Message}");
                    }
                }

                Console.WriteLine($"\n=== 扫描结果 ===");
                Console.WriteLine($"共找到 {vhdFiles.Count} 个符合条件的VHD文件:");
                foreach (var file in vhdFiles)
                {
                    Console.WriteLine($"  {file}");
                }
            });

            return vhdFiles;
        }

        public void TestSpecificFile(string filePath)
        {
            Console.WriteLine($"\n=== 测试特定文件 ===");
            Console.WriteLine($"文件路径: {filePath}");
            
            if (!File.Exists(filePath))
            {
                Console.WriteLine("❌ 文件不存在");
                return;
            }
            
            Console.WriteLine("✅ 文件存在");
            
            var fileName = Path.GetFileName(filePath).ToUpper();
            Console.WriteLine($"文件名: {fileName}");
            Console.WriteLine($"目标关键词: {string.Join(", ", TARGET_KEYWORDS)}");
            
            foreach (var keyword in TARGET_KEYWORDS)
            {
                bool contains = fileName.Contains(keyword);
                Console.WriteLine($"  包含 '{keyword}': {(contains ? "是" : "否")}");
            }
            
            bool isValid = TARGET_KEYWORDS.Any(keyword => fileName.Contains(keyword));
            Console.WriteLine($"\n结果: {(isValid ? "✅ 符合条件" : "❌ 不符合条件")}");
        }
    }
}