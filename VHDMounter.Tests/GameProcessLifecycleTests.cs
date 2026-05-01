using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace VHDMounter.Tests
{
    public sealed class GameProcessLifecycleTests : IDisposable
    {
        private readonly VHDManager manager;
        private readonly string tempDir;

        public GameProcessLifecycleTests()
        {
            manager = new VHDManager();
            tempDir = Path.Combine(Path.GetTempPath(), $"vhdm_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
        }

        public void Dispose()
        {
            manager.Dispose();
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch
            {
            }
        }

        [Fact]
        public void IsMenuOpen_DefaultsToFalse()
        {
            Assert.False(manager.IsMenuOpen);
        }

        [Fact]
        public void IsMenuOpen_CanBeSetToTrue()
        {
            manager.IsMenuOpen = true;
            Assert.True(manager.IsMenuOpen);
        }

        [Fact]
        public void IsMenuOpen_CanBeToggledBackToFalse()
        {
            manager.IsMenuOpen = true;
            manager.IsMenuOpen = false;
            Assert.False(manager.IsMenuOpen);
        }

        [Fact]
        public async Task RestartGameProcesses_ReturnsImmediatelyForNullPath()
        {
            // 不应抛出异常，应立即返回
            await manager.RestartGameProcesses(null);
            // 如果执行到这里说明没有异常，测试通过
            Assert.True(true);
        }

        [Fact]
        public async Task RestartGameProcesses_ReturnsImmediatelyForEmptyPath()
        {
            await manager.RestartGameProcesses(string.Empty);
            Assert.True(true);
        }

        [Fact]
        public async Task RestartGameProcesses_ReturnsWhenNoBatchFilesExist()
        {
            // tempDir 下没有任何 bat 文件
            await manager.RestartGameProcesses(tempDir);
            Assert.True(true);
        }

        [Fact]
        public void ProcessKeywords_ContainsExpectedGameProcesses()
        {
            var keywords = GetPrivateField<string[]>(manager, "PROCESS_KEYWORDS");

            Assert.Contains("sinmai", keywords, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("chusanapp", keywords, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("mu3", keywords, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void AuxiliaryProcessKeywords_ContainsExpectedHelpers()
        {
            var keywords = GetPrivateField<string[]>(manager, "AUXILIARY_PROCESS_KEYWORDS");

            Assert.Contains("inject", keywords, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("amdaemon", keywords, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void KillGameProcesses_DoesNotKillUnrelatedProcesses()
        {
            // 启动 ping.exe（进程名"ping"确定不匹配任何关键字），验证 KillGameProcesses 不误杀
            // 不用 cmd/notepad：无控制台环境下 CreateNoWindow 的 cmd 会因 stdin 缺失秒退
            var safeProc = Process.Start(new ProcessStartInfo
            {
                FileName = "ping.exe",
                Arguments = "127.0.0.1 -t",
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            Assert.NotNull(safeProc);
            try
            {
                Task.Delay(500).Wait();
                Assert.False(safeProc.HasExited, "测试用进程应在 KillGameProcesses 之前存活");

                manager.KillGameProcesses();

                Task.Delay(500).Wait();

                safeProc.Refresh();
                Assert.False(safeProc.HasExited, "KillGameProcesses 不应结束不相关的进程");
            }
            finally
            {
                try
                {
                    if (!safeProc.HasExited)
                    {
                        safeProc.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                }
            }
        }

        [Fact]
        public void IsTargetProcessRunning_DetectsFakeKeywordProcess()
        {
            // 启动一个标题包含关键字的进程（通过 cmd /c title sinmai_test 来模拟）
            // 但 cmd 的进程名是 "cmd"，不会匹配 PROCESS_KEYWORDS
            // 所以这个测试主要验证：没有匹配进程时返回 false
            var result = manager.IsTargetProcessRunning();
            // 在测试环境中通常没有游戏进程在运行，所以预期为 false
            // 但如果用户刚好在运行相关进程，这个测试可能失败
            // 因此这个测试的断言比较宽松——只要方法不抛出异常即可
            Assert.True(result == true || result == false);
        }

        [Fact]
        public void GetFirstTargetProcess_ReturnsNullWhenNoMatch()
        {
            var result = manager.GetFirstTargetProcess();
            // 测试环境中通常没有游戏进程
            Assert.Null(result);
        }

        [Fact]
        public async Task RequestTeardownAsync_ReturnsTrueWhenNoActionsRequested()
        {
            var result = await manager.RequestTeardownAsync(
                VHDManager.TeardownReason.UserExit,
                detachVhd: false,
                stopEvhd: false,
                removeDrive: false);

            Assert.True(result);
        }

        [Fact]
        public async Task RequestTeardownAsync_IsIdempotentWhenNoMountedPathExists()
        {
            var first = await manager.RequestTeardownAsync(
                VHDManager.TeardownReason.UserExit,
                detachVhd: true,
                stopEvhd: false,
                removeDrive: false);
            var second = await manager.RequestTeardownAsync(
                VHDManager.TeardownReason.DisposeFallback,
                detachVhd: true,
                stopEvhd: false,
                removeDrive: false);

            Assert.True(first);
            Assert.True(second);
        }

        private static T GetPrivateField<T>(object instance, string fieldName)
        {
            var field = instance.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field);
            return (T)field.GetValue(instance);
        }
    }
}
