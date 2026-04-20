using System.Collections.Generic;
using Xunit;

namespace VHDMounter.Tests
{
    public sealed class MaimollerInputServiceMappingTests
    {
        [Fact]
        public void NetworkEditorModeMapsPlayerButtonsToDigitsOneThroughEight()
        {
            using var service = new MaimollerInputService
            {
                InputMode = MaimollerInputRoutingMode.NetworkIpv4Edit,
            };

            var digits = new List<int>();
            service.RawInputRaised += (_, eventArgs) =>
            {
                if (eventArgs.Kind == MaimollerRawInputKind.Digit && eventArgs.Digit.HasValue)
                {
                    digits.Add(eventArgs.Digit.Value);
                }
            };

            for (var buttonNumber = 1; buttonNumber <= 8; buttonNumber++)
            {
                service.ProcessSnapshotForTesting(new MaimollerInputSnapshot((byte)(1 << (buttonNumber - 1)), 0));
                service.ProcessSnapshotForTesting(MaimollerInputSnapshot.Empty);
            }

            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8 }, digits);
        }

        [Fact]
        public void NetworkEditorModeMapsServiceAndTestAccordingToHidDocument()
        {
            using var service = new MaimollerInputService
            {
                InputMode = MaimollerInputRoutingMode.NetworkIpv4Edit,
            };

            var digits = new List<int>();
            service.RawInputRaised += (_, eventArgs) =>
            {
                if (eventArgs.Kind == MaimollerRawInputKind.Digit && eventArgs.Digit.HasValue)
                {
                    digits.Add(eventArgs.Digit.Value);
                }
            };

            service.ProcessSnapshotForTesting(new MaimollerInputSnapshot(0, (byte)MaimollerSystemButton.Service));
            service.ProcessSnapshotForTesting(MaimollerInputSnapshot.Empty);
            service.ProcessSnapshotForTesting(new MaimollerInputSnapshot(0, (byte)MaimollerSystemButton.Test));

            Assert.Equal(new[] { 0, 9 }, digits);
        }

        [Fact]
        public void NavigationModeKeepsButtonFourAndFiveAsConfirmAndBack()
        {
            using var service = new MaimollerInputService
            {
                InputMode = MaimollerInputRoutingMode.Navigation,
            };

            var actions = new List<UiInputAction>();
            service.ActionRaised += (_, eventArgs) => actions.Add(eventArgs.Action);

            service.ProcessSnapshotForTesting(new MaimollerInputSnapshot(1 << 3, 0));
            service.ProcessSnapshotForTesting(MaimollerInputSnapshot.Empty);
            service.ProcessSnapshotForTesting(new MaimollerInputSnapshot(1 << 4, 0));

            Assert.Equal(new[] { UiInputAction.Confirm, UiInputAction.Back }, actions);
        }
    }
}