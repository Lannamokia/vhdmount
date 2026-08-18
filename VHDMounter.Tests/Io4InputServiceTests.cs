using System.Collections.Generic;
using System.Diagnostics;
using Xunit;

namespace VHDMounter.Tests
{
    public sealed class Io4InputServiceTests
    {
        [Fact]
        public void ParsesSwitchesFromReportWithReportId()
        {
            var report = new byte[Io4Constants.InputReportLengthWithId];
            report[0] = Io4Constants.InputReportId;
            report[1 + Io4Constants.SwitchesOffset] = 0x40;
            report[1 + Io4Constants.SwitchesOffset + 1] = 0x02;
            report[1 + Io4Constants.SwitchesOffset + 2] = 0x01;
            report[1 + Io4Constants.SwitchesOffset + 3] = 0x80;
            report[1 + Io4Constants.CoinCountOffset] = 0x07;

            Assert.True(Io4InputService.TryParseReportForTesting(report, report.Length, out var snapshot));
            Assert.Equal((ushort)0x0240, snapshot.Player1Switches);
            Assert.Equal((ushort)0x8001, snapshot.Player2Switches);
            Assert.Equal((byte)7, snapshot.CoinCount);
            Assert.True(snapshot.IsPressed(Io4Button.Service));
            Assert.True(snapshot.IsPressed(Io4Button.Test));
            Assert.True(snapshot.IsPressed(Io4Button.P2Button4));
        }

        [Fact]
        public void ParsesReportWithoutReportIdForHidBackendsThatStripIt()
        {
            var payload = new byte[Io4Constants.InputReportDataLength];
            payload[Io4Constants.SwitchesOffset] = (byte)Io4Constants.ServiceSwitchMask;

            Assert.True(Io4InputService.TryParseReportForTesting(payload, payload.Length, out var snapshot));
            Assert.Equal(Io4Constants.ServiceSwitchMask, snapshot.Player1Switches);
            Assert.True(snapshot.IsPressed(Io4Button.Service));
            Assert.False(snapshot.IsPressed(Io4Button.Test));
        }

        [Fact]
        public void ServiceAndTestEmitOnlyOnPressEdges()
        {
            using var service = new Io4InputService();
            var buttons = new List<Io4Button>();
            service.ButtonPressed += (_, eventArgs) => buttons.Add(eventArgs.Button);

            service.ProcessSnapshotForTesting(new Io4InputSnapshot(
                (ushort)(Io4Constants.ServiceSwitchMask | Io4Constants.TestSwitchMask), 0));
            service.ProcessSnapshotForTesting(new Io4InputSnapshot(
                (ushort)(Io4Constants.ServiceSwitchMask | Io4Constants.TestSwitchMask), 0));
            service.ProcessSnapshotForTesting(Io4InputSnapshot.Empty);
            service.ProcessSnapshotForTesting(new Io4InputSnapshot(Io4Constants.TestSwitchMask, 0));

            Assert.Equal(new[] { Io4Button.Service, Io4Button.Test, Io4Button.Test }, buttons);
        }

        [Fact]
        public void RejectsShortReports()
        {
            Assert.False(Io4InputService.TryParseReportForTesting(
                new byte[Io4Constants.InputReportDataLength - 1],
                Io4Constants.InputReportDataLength - 1,
                out _));
        }

        [Fact]
        public void Mai2hookPlayerOneMasksAreExposedWithTheExpectedButtonNames()
        {
            Assert.Equal(Io4Constants.P1Button1Mask, Io4Constants.GetMask(Io4Button.P1Button1));
            Assert.Equal(Io4Constants.P1Button2Mask, Io4Constants.GetMask(Io4Button.P1Button2));
            Assert.Equal(Io4Constants.P1Button3Mask, Io4Constants.GetMask(Io4Button.P1Button3));
            Assert.Equal(Io4Constants.P1Button4Mask, Io4Constants.GetMask(Io4Button.P1Button4));
            Assert.Equal(Io4Constants.P1Button5Mask, Io4Constants.GetMask(Io4Button.P1Button5));
            Assert.Equal(Io4Constants.P1Button6Mask, Io4Constants.GetMask(Io4Button.P1Button6));
            Assert.Equal(Io4Constants.P1Button7Mask, Io4Constants.GetMask(Io4Button.P1Button7));
            Assert.Equal(Io4Constants.P1Button8Mask, Io4Constants.GetMask(Io4Button.P1Button8));
            Assert.Equal(Io4Constants.P1SelectMask, Io4Constants.GetMask(Io4Button.P1Select));
            Assert.Equal(Io4Constants.P2Button4Mask, Io4Constants.GetPlayer2Mask(Io4Button.P2Button4));
            Assert.Equal(Io4Constants.P2Button5Mask, Io4Constants.GetPlayer2Mask(Io4Button.P2Button5));
            Assert.Equal(Io4Constants.P2SelectMask, Io4Constants.GetPlayer2Mask(Io4Button.P2Select));
        }

        [Fact]
        public void NavigationModeMapsMai2hookButtonsToServiceMenuActions()
        {
            using var service = new Io4InputService(() => 0);
            var actions = new List<UiInputAction>();
            service.ActionRaised += (_, eventArgs) => actions.Add(eventArgs.Action);

            var expected = new[]
            {
                (Io4Constants.P1Button6Mask, UiInputAction.Up),
                (Io4Constants.P1Button3Mask, UiInputAction.Down),
                (Io4Constants.P1Button4Mask, UiInputAction.Confirm),
                (Io4Constants.P1Button5Mask, UiInputAction.Back),
            };

            foreach (var (mask, action) in expected)
            {
                service.ProcessSnapshotForTesting(new Io4InputSnapshot(mask, 0));
                service.ProcessSnapshotForTesting(Io4InputSnapshot.Empty);
                Assert.Contains(action, actions);
            }
        }

        [Fact]
        public void NetworkEditorMapsButtonsAndSystemButtonsToDigits()
        {
            using var service = new Io4InputService(() => 0)
            {
                InputMode = Io4InputRoutingMode.NetworkIpv4Edit,
            };
            var digits = new List<int>();
            service.RawInputRaised += (_, eventArgs) =>
            {
                if (eventArgs.Kind == Io4RawInputKind.Digit && eventArgs.Digit.HasValue)
                {
                    digits.Add(eventArgs.Digit.Value);
                }
            };

            for (var buttonNumber = 1; buttonNumber <= 8; buttonNumber++)
            {
                var button = (Io4Button)(buttonNumber - 1);
                service.ProcessSnapshotForTesting(new Io4InputSnapshot(Io4Constants.GetMask(button), 0));
                service.ProcessSnapshotForTesting(Io4InputSnapshot.Empty);
            }

            service.ProcessSnapshotForTesting(new Io4InputSnapshot(Io4Constants.ServiceSwitchMask, 0));
            service.ProcessSnapshotForTesting(Io4InputSnapshot.Empty);
            service.ProcessSnapshotForTesting(new Io4InputSnapshot(Io4Constants.TestSwitchMask, 0));
            service.ProcessSnapshotForTesting(Io4InputSnapshot.Empty);

            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 0, 9 }, digits);
        }

        [Fact]
        public void NetworkEditorMapsPlayerTwoButtonFourAndFiveToShortLongAndBack()
        {
            var ticksPerMillisecond = Stopwatch.Frequency / 1000d;
            long now = 0;
            using var service = new Io4InputService(() => now)
            {
                InputMode = Io4InputRoutingMode.NetworkIpv4Edit,
            };
            var rawInputs = new List<Io4RawInputKind>();
            var actions = new List<UiInputAction>();
            service.RawInputRaised += (_, eventArgs) => rawInputs.Add(eventArgs.Kind);
            service.ActionRaised += (_, eventArgs) => actions.Add(eventArgs.Action);

            service.ProcessSnapshotForTesting(new Io4InputSnapshot(0, 0));

            service.ProcessSnapshotForTesting(new Io4InputSnapshot(0, Io4Constants.P2Button4Mask));
            now = (long)(500 * ticksPerMillisecond);
            service.ProcessSnapshotForTesting(new Io4InputSnapshot(0, Io4Constants.P2Button4Mask));
            service.ProcessSnapshotForTesting(Io4InputSnapshot.Empty);

            now = (long)(1000 * ticksPerMillisecond);
            service.ProcessSnapshotForTesting(new Io4InputSnapshot(0, Io4Constants.P2Button4Mask));
            now = (long)(2001 * ticksPerMillisecond);
            service.ProcessSnapshotForTesting(new Io4InputSnapshot(0, Io4Constants.P2Button4Mask));
            service.ProcessSnapshotForTesting(Io4InputSnapshot.Empty);

            service.ProcessSnapshotForTesting(new Io4InputSnapshot(0, Io4Constants.P2Button5Mask));
            service.ProcessSnapshotForTesting(Io4InputSnapshot.Empty);

            Assert.Equal(new[]
            {
                Io4RawInputKind.CoinShortPress,
                Io4RawInputKind.CoinLongPressConfirm,
            }, rawInputs);
            Assert.Equal(new[] { UiInputAction.Back }, actions);
        }

        [Fact]
        public void CoinCounterChangeProducesShortPressAfterThePulseEnds()
        {
            var ticksPerMillisecond = Stopwatch.Frequency / 1000d;
            long now = 0;
            using var service = new Io4InputService(() => now)
            {
                InputMode = Io4InputRoutingMode.NetworkIpv4Edit,
            };
            var rawInputs = new List<Io4RawInputKind>();
            service.RawInputRaised += (_, eventArgs) => rawInputs.Add(eventArgs.Kind);

            service.ProcessSnapshotForTesting(new Io4InputSnapshot(0, 0, 0));
            now = (long)(10 * ticksPerMillisecond);
            service.ProcessSnapshotForTesting(new Io4InputSnapshot(0, 0, 1));
            now = (long)(111 * ticksPerMillisecond);
            service.ProcessSnapshotForTesting(new Io4InputSnapshot(0, 0, 1));

            Assert.Equal(new[] { Io4RawInputKind.CoinShortPress }, rawInputs);
        }

        [Fact]
        public void RepeatedCoinCounterChangesStillProduceOnlyOneShortPressAfterRelease()
        {
            var ticksPerMillisecond = Stopwatch.Frequency / 1000d;
            long now = 0;
            using var service = new Io4InputService(() => now)
            {
                InputMode = Io4InputRoutingMode.NetworkIpv4Edit,
            };
            var rawInputs = new List<Io4RawInputKind>();
            service.RawInputRaised += (_, eventArgs) => rawInputs.Add(eventArgs.Kind);

            service.ProcessSnapshotForTesting(new Io4InputSnapshot(0, 0, 0));
            service.ProcessSnapshotForTesting(new Io4InputSnapshot(0, 0, 1));
            for (var count = 2; count <= 21; count++)
            {
                now = (long)((count - 1) * 50 * ticksPerMillisecond);
                service.ProcessSnapshotForTesting(new Io4InputSnapshot(0, 0, (byte)count));
            }

            now = (long)(1200 * ticksPerMillisecond);
            service.ProcessSnapshotForTesting(new Io4InputSnapshot(0, 0, 21));

            Assert.Equal(new[] { Io4RawInputKind.CoinShortPress }, rawInputs);
        }

        [Fact]
        public void CoinCounterResetDoesNotCreateAFalseCoinPress()
        {
            var ticksPerMillisecond = Stopwatch.Frequency / 1000d;
            long now = 0;
            using var service = new Io4InputService(() => now)
            {
                InputMode = Io4InputRoutingMode.NetworkIpv4Edit,
            };
            var rawInputs = new List<Io4RawInputKind>();
            service.RawInputRaised += (_, eventArgs) => rawInputs.Add(eventArgs.Kind);

            service.ProcessSnapshotForTesting(new Io4InputSnapshot(0, 0, 20));
            now = (long)(200 * ticksPerMillisecond);
            service.ProcessSnapshotForTesting(new Io4InputSnapshot(0, 0, 0));
            now = (long)(400 * ticksPerMillisecond);
            service.ProcessSnapshotForTesting(new Io4InputSnapshot(0, 0, 0));

            Assert.Empty(rawInputs);
        }

        [Fact]
        public void CoinCounterDoesNotOpenTheMenuInNavigationMode()
        {
            var ticksPerMillisecond = Stopwatch.Frequency / 1000d;
            long now = 0;
            using var service = new Io4InputService(() => now);
            var actions = new List<UiInputAction>();
            service.ActionRaised += (_, eventArgs) => actions.Add(eventArgs.Action);

            service.ProcessSnapshotForTesting(new Io4InputSnapshot(0, 0, 0));
            service.ProcessSnapshotForTesting(new Io4InputSnapshot(0, 0, 1));
            for (var count = 2; count <= 301; count++)
            {
                now = (long)((count - 1) * 50 * ticksPerMillisecond);
                service.ProcessSnapshotForTesting(new Io4InputSnapshot(0, 0, (byte)count));
            }

            Assert.DoesNotContain(UiInputAction.OpenServiceMenu, actions);
        }

        [Fact]
        public void TestLongPressOpensTheMenuOnceWhileShortPressDoesNot()
        {
            var ticksPerMillisecond = Stopwatch.Frequency / 1000d;
            long now = 0;
            using var service = new Io4InputService(() => now);
            var actions = new List<UiInputAction>();
            service.ActionRaised += (_, eventArgs) => actions.Add(eventArgs.Action);

            service.ProcessSnapshotForTesting(new Io4InputSnapshot(0, 0));
            service.ProcessSnapshotForTesting(new Io4InputSnapshot(Io4Constants.TestSwitchMask, 0));
            now = (long)(500 * ticksPerMillisecond);
            service.ProcessSnapshotForTesting(new Io4InputSnapshot(Io4Constants.TestSwitchMask, 0));
            service.ProcessSnapshotForTesting(Io4InputSnapshot.Empty);
            Assert.Empty(actions);

            now = (long)(1000 * ticksPerMillisecond);
            service.ProcessSnapshotForTesting(new Io4InputSnapshot(Io4Constants.TestSwitchMask, 0));
            now = (long)(2001 * ticksPerMillisecond);
            service.ProcessSnapshotForTesting(new Io4InputSnapshot(Io4Constants.TestSwitchMask, 0));
            service.ProcessSnapshotForTesting(new Io4InputSnapshot(Io4Constants.TestSwitchMask, 0));
            service.ProcessSnapshotForTesting(Io4InputSnapshot.Empty);

            Assert.Equal(new[] { UiInputAction.OpenServiceMenu }, actions);
        }
    }
}
