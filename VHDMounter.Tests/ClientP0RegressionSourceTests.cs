using System;
using System.IO;
using System.Reflection;
using System.Threading;
using Xunit;

namespace VHDMounter.Tests
{
    public sealed class ClientP0RegressionSourceTests
    {
        [Fact]
        public void MainWindowFiles_DoNotUseSynchronousDispatcherInvoke()
        {
            Assert.DoesNotContain("Dispatcher.Invoke(", ReadSource("src/VHDMounter/MainWindow.xaml.cs"), StringComparison.Ordinal);
            Assert.DoesNotContain("Dispatcher.Invoke(", ReadSource("src/VHDMounter/MainWindow.Input.cs"), StringComparison.Ordinal);
        }

        [Fact]
        public void MainWindow_PropagatesAppLifetimeTokenIntoEvhdMountFlow()
        {
            var source = ReadSource("src/VHDMounter/MainWindow.xaml.cs");

            Assert.Contains("MountEVHDAndAttachDecryptedVHD(vhdPath, _appLifetimeToken)", source, StringComparison.Ordinal);
        }

        [Fact]
        public void VhdManager_ExposesCancellationAwareEvhdMountOverload()
        {
            var method = typeof(VHDManager).GetMethod(
                "MountEVHDAndAttachDecryptedVHD",
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(string), typeof(CancellationToken) },
                modifiers: null);

            Assert.NotNull(method);
        }

        [Fact]
        public void VhdManager_MountFlowUsesCancellationAwarePasswordFetch()
        {
            var source = ReadSource("src/VHDMounter/VHDManager.cs");

            Assert.Contains("var password = await GetEvhdPasswordFromServerWithBlockingRetry(ct);", source, StringComparison.Ordinal);
        }

        [Fact]
        public void VhdManager_DisposeStopsEncryptedMountProcess()
        {
            var source = ReadSource("src/VHDMounter/VHDManager.cs");

            Assert.Contains("try { StopEncryptedEvhdMount(); } catch { }", source, StringComparison.Ordinal);
        }

        [Fact]
        public void MachineKeyRegistration_GuardsCriticalP0Patterns()
        {
            var source = ReadSource("src/VHDMounter/MachineKeyRegistration.cs");

            Assert.Contains("using var rsa = VHDManager.EnsureOrCreateTpmRsa(machineId);", source, StringComparison.Ordinal);
            Assert.Contains("catch (OperationCanceledException) when (ct.IsCancellationRequested)", source, StringComparison.Ordinal);
            Assert.Contains("string.Equals(errorCode, \"MACHINE_NOT_REGISTERED\"", source, StringComparison.Ordinal);
        }

        [Fact]
        public void MainWindow_ShutdownPathsUseUnifiedTeardownEntry()
        {
            var source = ReadSource("src/VHDMounter/MainWindow.xaml.cs");

            Assert.Contains("RequestTeardownAsync(VHDManager.TeardownReason.DisposeFallback)", source, StringComparison.Ordinal);
            Assert.Contains("RequestTeardownAsync(VHDManager.TeardownReason.SessionEnding)", source, StringComparison.Ordinal);
            Assert.Contains("RequestTeardownAsync(VHDManager.TeardownReason.UserExit)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("await vhdManager.UnmountVHD();", source, StringComparison.Ordinal);
            Assert.DoesNotContain("vhdManager.StopEncryptedEvhdMount();", source, StringComparison.Ordinal);
        }

        [Fact]
        public void HidMenuPowerAction_UsesUnifiedTeardownEntry()
        {
            var source = ReadSource("src/VHDMounter/MainWindow.HidMenuFeatures.cs");

            Assert.Contains("RequestTeardownAsync(VHDManager.TeardownReason.PowerAction)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("await vhdManager.UnmountVHD();", source, StringComparison.Ordinal);
            Assert.DoesNotContain("vhdManager.StopEncryptedEvhdMount();", source, StringComparison.Ordinal);
        }

        [Fact]
        public void VhdManager_MountFlowUsesUnifiedMountSwitchTeardown()
        {
            var source = ReadSource("src/VHDMounter/VHDManager.cs");

            Assert.Contains("TeardownReason.MountSwitch", source, StringComparison.Ordinal);
            Assert.Contains("RequestTeardownAsync(", source, StringComparison.Ordinal);
        }

        [Fact]
        public void VhdManager_EvhdFlowDoesNotStopFreshMountToolBeforeAttach()
        {
            var source = ReadSource("src/VHDMounter/VHDManager.cs");

            Assert.Contains("MountVHD(", source, StringComparison.Ordinal);
            Assert.Contains("performMountSwitchTeardown: false", source, StringComparison.Ordinal);
            Assert.Contains("stopEvhdOnMountSwitch: false", source, StringComparison.Ordinal);
        }

        private static string ReadSource(string relativePath)
        {
            var root = FindRepositoryRoot();
            return File.ReadAllText(Path.Combine(root, relativePath));
        }

        private static string FindRepositoryRoot()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "vhdmount.sln")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("无法从测试输出目录定位仓库根目录。");
        }
    }
}
