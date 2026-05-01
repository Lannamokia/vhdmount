using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using VHDMounter.SoftwareDeploy;
using Xunit;

namespace VHDMounter.Tests
{
    public sealed class DeployPollerLifecycleTests : IDisposable
    {
        private readonly string tempDir;
        private readonly string trustedKeysPath;

        public DeployPollerLifecycleTests()
        {
            tempDir = Path.Combine(Path.GetTempPath(), $"deploy-poller-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            trustedKeysPath = Path.Combine(tempDir, "trusted_keys.pem");
            File.WriteAllText(trustedKeysPath, "dummy");
            MachineKeyRegistration.ResetStateForTests();
        }

        public void Dispose()
        {
            MachineKeyRegistration.ResetStateForTests();
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
        public void Start_DoesNotCreatePollTask_WhenMachineIsNotApproved()
        {
            using var poller = new DeployPoller("http://127.0.0.1:18080", "TEST_MACHINE", trustedKeysPath, tempDir);

            poller.Start();

            Assert.Null(GetPrivateField<Task?>(poller, "_pollTask"));
        }

        [Fact]
        public void Dispose_CanBeCalledRepeatedly()
        {
            var poller = new DeployPoller("http://127.0.0.1:18080", "TEST_MACHINE", trustedKeysPath, tempDir);

            poller.Dispose();
            poller.Dispose();
        }

        [Fact]
        public void Start_AfterDispose_ReturnsWithoutCreatingTask()
        {
            using var poller = new DeployPoller("http://127.0.0.1:18080", "TEST_MACHINE", trustedKeysPath, tempDir);
            poller.Dispose();

            poller.Start();

            Assert.True(GetPrivateField<bool>(poller, "_disposed"));
            Assert.Null(GetPrivateField<Task?>(poller, "_pollTask"));
        }

        [Fact]
        public void Dispose_StopsRunningPollTask()
        {
            SetRegistrationState(MachineKeyRegistration.RegistrationState.Approved);
            var poller = new DeployPoller("http://127.0.0.1:1", "TEST_MACHINE", trustedKeysPath, tempDir);

            try
            {
                poller.Start();
                var pollTask = GetPrivateField<Task?>(poller, "_pollTask");

                Assert.NotNull(pollTask);

                poller.Dispose();

                Assert.True(pollTask!.Wait(TimeSpan.FromSeconds(2)));
            }
            finally
            {
                poller.Dispose();
            }
        }

        private static T GetPrivateField<T>(object instance, string fieldName)
        {
            var field = instance.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field);
            return (T)field!.GetValue(instance)!;
        }

        private static void SetRegistrationState(MachineKeyRegistration.RegistrationState state)
        {
            var field = typeof(MachineKeyRegistration).GetField("_currentState", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(field);
            field!.SetValue(null, state);
        }
    }
}
