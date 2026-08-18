using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HidSharp;

namespace VHDMounter
{
    internal sealed class Io4InputService : IDisposable
    {
        private readonly CancellationTokenSource lifetimeCts = new CancellationTokenSource();
        private readonly object stateSync = new object();
        private readonly Func<long> timestampProvider;
        private Task workerTask;
        private Io4InputSnapshot previousSnapshot = Io4InputSnapshot.Empty;
        private long? coinActivityStartTimestamp;
        private long lastCoinPulseTimestamp;
        private bool coinLongPressConsumed;
        private bool hasPreviousSnapshot;
        private bool disposed;
        private bool isMenuOpen;
        private bool ignoreMenuOpenRequests;
        private Io4InputRoutingMode inputMode;

        public Io4InputService()
            : this(Stopwatch.GetTimestamp)
        {
        }

        internal Io4InputService(Func<long> timestampProvider)
        {
            this.timestampProvider = timestampProvider ?? throw new ArgumentNullException(nameof(timestampProvider));
        }

        public event EventHandler<Io4ButtonEventArgs> ButtonPressed;
        public event EventHandler<Io4ActionEventArgs> ActionRaised;
        public event EventHandler<Io4RawInputEventArgs> RawInputRaised;

        public bool IsMenuOpen
        {
            get
            {
                lock (stateSync)
                {
                    return isMenuOpen;
                }
            }
            set
            {
                lock (stateSync)
                {
                    isMenuOpen = value;
                }
            }
        }

        public bool IgnoreMenuOpenRequests
        {
            get
            {
                lock (stateSync)
                {
                    return ignoreMenuOpenRequests;
                }
            }
            set
            {
                lock (stateSync)
                {
                    ignoreMenuOpenRequests = value;
                }
            }
        }

        public Io4InputRoutingMode InputMode
        {
            get
            {
                lock (stateSync)
                {
                    return inputMode;
                }
            }
            set
            {
                lock (stateSync)
                {
                    if (inputMode == value)
                    {
                        return;
                    }

                    inputMode = value;
                    ResetCoinState();
                }
            }
        }

        public void Start()
        {
            ThrowIfDisposed();
            if (workerTask != null)
            {
                return;
            }

            workerTask = Task.Run(() => RunAsync(lifetimeCts.Token));
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            lifetimeCts.Cancel();
            try
            {
                workerTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
            }

            lifetimeCts.Dispose();
        }

        private async Task RunAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var device = SelectDevice();
                    if (device == null)
                    {
                        Trace.WriteLine("IO4_ENUM: no compatible IO4 device found");
                        await Task.Delay(2000, token);
                        continue;
                    }

                    if (!device.TryOpen(out HidStream stream))
                    {
                        Trace.WriteLine($"IO4_OPEN_FAILED: DevicePath={device.DevicePath}");
                        await Task.Delay(2000, token);
                        continue;
                    }

                    using (stream)
                    {
                        stream.ReadTimeout = Timeout.Infinite;
                        var buffer = new byte[Math.Max(device.GetMaxInputReportLength(), Io4Constants.InputReportLengthWithId)];
                        Trace.WriteLine($"IO4_OPEN: DevicePath={device.DevicePath}");

                        while (!token.IsCancellationRequested)
                        {
                            var bytesRead = stream.Read(buffer, 0, buffer.Length);
                            if (bytesRead <= 0)
                            {
                                throw new IOException("No IO4 HID data returned.");
                            }

                            if (!TryNormalizeInputReport(buffer, bytesRead, out var payload))
                            {
                                continue;
                            }

                            ProcessSnapshot(ParseSnapshot(payload));
                        }
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"IO4_RECONNECT: {ex.Message}");
                    ResetState();
                    await Task.Delay(2000, token);
                }
            }
        }

        private static HidDevice SelectDevice()
        {
            return DeviceList.Local
                .GetHidDevices(Io4Constants.VendorId, Io4Constants.ProductId)
                .FirstOrDefault();
        }

        private static bool TryNormalizeInputReport(byte[] buffer, int bytesRead, out byte[] payload)
        {
            payload = Array.Empty<byte>();
            if (buffer == null)
            {
                return false;
            }

            bytesRead = Math.Min(bytesRead, buffer.Length);
            if (bytesRead < Io4Constants.InputReportDataLength)
            {
                return false;
            }

            if (bytesRead >= Io4Constants.InputReportLengthWithId && buffer[0] == Io4Constants.InputReportId)
            {
                payload = buffer.Skip(1).Take(Io4Constants.InputReportDataLength).ToArray();
                return true;
            }

            payload = buffer.Take(Io4Constants.InputReportDataLength).ToArray();
            return true;
        }

        private static Io4InputSnapshot ParseSnapshot(byte[] payload)
        {
            var player1Switches = ReadUInt16LittleEndian(payload, Io4Constants.SwitchesOffset);
            var player2Switches = ReadUInt16LittleEndian(payload, Io4Constants.SwitchesOffset + sizeof(ushort));
            var coinCount = payload[Io4Constants.CoinCountOffset];
            return new Io4InputSnapshot(player1Switches, player2Switches, coinCount);
        }

        private static ushort ReadUInt16LittleEndian(byte[] buffer, int offset)
        {
            return (ushort)(buffer[offset] | (buffer[offset + 1] << 8));
        }

        private void ProcessSnapshot(Io4InputSnapshot snapshot)
        {
            snapshot ??= Io4InputSnapshot.Empty;

            var previous = previousSnapshot;
            var newPresses = (ushort)(snapshot.Player1Switches & ~previous.Player1Switches);

            foreach (var button in Io4Constants.Player1Buttons)
            {
                if ((newPresses & Io4Constants.GetMask(button)) != 0)
                {
                    RaiseButton(button);
                }
            }

            if ((newPresses & Io4Constants.P1SelectMask) != 0)
            {
                RaiseButton(Io4Button.P1Select);
            }

            if ((newPresses & Io4Constants.ServiceSwitchMask) != 0)
            {
                RaiseButton(Io4Button.Service);
            }

            if ((newPresses & Io4Constants.TestSwitchMask) != 0)
            {
                RaiseButton(Io4Button.Test);
            }

            if (hasPreviousSnapshot)
            {
                ProcessCoinCounter(previous.CoinCount, snapshot.CoinCount);
            }

            var currentInputMode = InputMode;
            if (currentInputMode == Io4InputRoutingMode.NetworkIpv4Edit)
            {
                ProcessNetworkIpv4EditorSnapshot(newPresses);
            }
            else
            {
                ProcessNavigationSnapshot(newPresses);
            }

            previousSnapshot = snapshot;
            hasPreviousSnapshot = true;
        }

        private void ProcessNavigationSnapshot(ushort newPresses)
        {
            if ((newPresses & Io4Constants.P1Button6Mask) != 0)
            {
                RaiseAction(UiInputAction.Up, "P1Button6");
            }

            if ((newPresses & Io4Constants.P1Button3Mask) != 0)
            {
                RaiseAction(UiInputAction.Down, "P1Button3");
            }

            if ((newPresses & Io4Constants.P1Button4Mask) != 0)
            {
                RaiseAction(UiInputAction.Confirm, "P1Button4");
            }

            if ((newPresses & Io4Constants.P1Button5Mask) != 0)
            {
                RaiseAction(UiInputAction.Back, "P1Button5");
            }

            // maimai's ninth 1P input is the physical select/start key. Treat
            // it as a confirm alias so a cabinet without a dedicated Button 4
            // can still operate the service pages.
            if ((newPresses & Io4Constants.P1SelectMask) != 0)
            {
                RaiseAction(UiInputAction.Confirm, "P1Select");
            }

            FinalizeCoinActivityIfReleased();
        }

        private void ProcessNetworkIpv4EditorSnapshot(ushort newPresses)
        {
            for (var buttonNumber = 1; buttonNumber <= 8; buttonNumber++)
            {
                var button = (Io4Button)(buttonNumber - 1);
                if ((newPresses & Io4Constants.GetMask(button)) != 0)
                {
                    RaiseDigit(buttonNumber, $"P1Button{buttonNumber}");
                }
            }

            if ((newPresses & Io4Constants.TestSwitchMask) != 0)
            {
                RaiseDigit(9, "Test");
            }

            if ((newPresses & Io4Constants.ServiceSwitchMask) != 0)
            {
                RaiseDigit(0, "Service");
            }

            FinalizeCoinActivityIfReleased();
        }

        private void ProcessCoinCounter(byte previousCount, byte currentCount)
        {
            if (previousCount == currentCount)
            {
                return;
            }

            // A board reset normally moves the count back to zero. Do not
            // convert that reset into a burst of coins. A normal byte wrap
            // (255 -> 0) remains a one-count delta.
            var delta = currentCount >= previousCount
                ? currentCount - previousCount
                : 256 - previousCount + currentCount;
            if (delta == 0 || delta > 32)
            {
                ResetCoinState();
                return;
            }

            var now = timestampProvider();
            for (var index = 0; index < delta; index++)
            {
                RegisterCoinPulse(now);
            }
        }

        private void RegisterCoinPulse(long now)
        {
            if (coinActivityStartTimestamp.HasValue &&
                ElapsedMilliseconds(lastCoinPulseTimestamp, now) >= Io4Constants.CoinReleaseGapMilliseconds)
            {
                FinalizeCoinActivity();
            }

            if (!coinActivityStartTimestamp.HasValue)
            {
                coinActivityStartTimestamp = now;
                coinLongPressConsumed = false;
            }

            RaiseButton(Io4Button.Coin);
            lastCoinPulseTimestamp = now;
            var currentInputMode = InputMode;
            var elapsedMilliseconds = ElapsedMilliseconds(coinActivityStartTimestamp.Value, now);

            if (currentInputMode == Io4InputRoutingMode.NetworkIpv4Edit)
            {
                if (!coinLongPressConsumed && elapsedMilliseconds >= Io4Constants.NetworkEditorCoinHoldMilliseconds)
                {
                    coinLongPressConsumed = true;
                    RaiseRawInput(Io4RawInputKind.CoinLongPressConfirm, "CoinHold1s");
                }
            }
            else if (!coinLongPressConsumed &&
                     !IsMenuOpen &&
                     !IgnoreMenuOpenRequests &&
                     elapsedMilliseconds >= Io4Constants.CoinHoldSeconds * 1000L)
            {
                coinLongPressConsumed = true;
                RaiseAction(UiInputAction.OpenServiceMenu, "CoinHold15s");
            }
        }

        private void FinalizeCoinActivityIfReleased()
        {
            if (!coinActivityStartTimestamp.HasValue)
            {
                return;
            }

            if (ElapsedMilliseconds(lastCoinPulseTimestamp, timestampProvider()) < Io4Constants.CoinReleaseGapMilliseconds)
            {
                return;
            }

            FinalizeCoinActivity();
        }

        private void FinalizeCoinActivity()
        {
            if (InputMode == Io4InputRoutingMode.NetworkIpv4Edit && !coinLongPressConsumed)
            {
                RaiseRawInput(Io4RawInputKind.CoinShortPress, "CoinShort");
            }

            ResetCoinState();
        }

        private long ElapsedMilliseconds(long start, long end)
        {
            return (long)((end - start) * 1000d / Stopwatch.Frequency);
        }

        internal void ProcessSnapshotForTesting(Io4InputSnapshot snapshot)
        {
            ProcessSnapshot(snapshot ?? Io4InputSnapshot.Empty);
        }

        internal static bool TryParseReportForTesting(byte[] buffer, int bytesRead, out Io4InputSnapshot snapshot)
        {
            snapshot = Io4InputSnapshot.Empty;
            if (!TryNormalizeInputReport(buffer, bytesRead, out var payload))
            {
                return false;
            }

            snapshot = ParseSnapshot(payload);
            return true;
        }

        private void RaiseButton(Io4Button button)
        {
            Trace.WriteLine($"IO4_BUTTON: Button={button}");
            try
            {
                ButtonPressed?.Invoke(this, new Io4ButtonEventArgs(button));
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"IO4_BUTTON_DISPATCH_FAILED: {ex}");
            }
        }

        private void RaiseAction(UiInputAction action, string source)
        {
            Trace.WriteLine($"IO4_ACTION: Action={action} Source={source}");
            try
            {
                ActionRaised?.Invoke(this, new Io4ActionEventArgs(action, source));
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"IO4_ACTION_DISPATCH_FAILED: {ex}");
            }
        }

        private void RaiseDigit(int digit, string source)
        {
            RaiseRawInput(Io4RawInputKind.Digit, source, digit);
        }

        private void RaiseRawInput(Io4RawInputKind kind, string source, int? digit = null)
        {
            Trace.WriteLine($"IO4_RAW_ACTION: Kind={kind} Digit={(digit.HasValue ? digit.Value.ToString() : "-")} Source={source}");
            try
            {
                RawInputRaised?.Invoke(this, new Io4RawInputEventArgs(kind, source, digit));
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"IO4_RAW_DISPATCH_FAILED: {ex}");
            }
        }

        private void ResetState()
        {
            previousSnapshot = Io4InputSnapshot.Empty;
            hasPreviousSnapshot = false;
            ResetCoinState();
        }

        private void ResetCoinState()
        {
            coinActivityStartTimestamp = null;
            lastCoinPulseTimestamp = 0;
            coinLongPressConsumed = false;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(Io4InputService));
            }
        }
    }
}
