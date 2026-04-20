using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HidSharp;

namespace VHDMounter
{
    internal sealed class MaimollerInputService : IDisposable
    {
        private readonly CancellationTokenSource lifetimeCts = new CancellationTokenSource();
        private readonly object stateSync = new object();
        private Task workerTask;
        private MaimollerInputSnapshot previousSnapshot = MaimollerInputSnapshot.Empty;
        private long? coinHoldStartTimestamp;
        private bool coinHoldConsumed;
        private bool disposed;
        private bool isMenuOpen;
        private bool ignoreMenuOpenRequests;
        private MaimollerInputRoutingMode inputMode;

        public event EventHandler<MaimollerActionEventArgs> ActionRaised;
        public event EventHandler<MaimollerRawInputEventArgs> RawInputRaised;

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

        public MaimollerInputRoutingMode InputMode
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
                    var selectedDevice = SelectPreferredDevice();
                    if (selectedDevice == null)
                    {
                        Trace.WriteLine("HID_ENUM: no compatible Maimoller device found");
                        await Task.Delay(2000, token);
                        continue;
                    }

                    if (!selectedDevice.TryOpen(out HidStream stream))
                    {
                        Trace.WriteLine($"HID_OPEN_SHARED_FAILED: DevicePath={selectedDevice.DevicePath}");
                        await Task.Delay(2000, token);
                        continue;
                    }

                    using (stream)
                    {
                        stream.ReadTimeout = Timeout.Infinite;
                        var buffer = new byte[Math.Max(selectedDevice.GetMaxInputReportLength(), 8)];
                        Trace.WriteLine($"HID_OPEN: DevicePath={selectedDevice.DevicePath} Side={DetectPlayerSide(selectedDevice)} SharedOpen=True");

                        while (!token.IsCancellationRequested)
                        {
                            var bytesRead = stream.Read(buffer, 0, buffer.Length);
                            if (bytesRead <= 0)
                            {
                                throw new IOException("No HID data returned.");
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
                    Trace.WriteLine($"HID_RECONNECT: {ex.Message}");
                    ResetState();
                    await Task.Delay(2000, token);
                }
            }
        }

        private static HidDevice SelectPreferredDevice()
        {
            var devices = DeviceList.Local
                .GetHidDevices(MaimollerConstants.VendorId, MaimollerConstants.ProductId)
                .Where(device => MaimollerConstants.IsCandidateDevicePath(device.DevicePath))
                .Select(device => new
                {
                    Device = device,
                    Side = DetectPlayerSide(device),
                })
                .OrderBy(item => item.Side == MaimollerPlayerSide.Player1 ? 0 : item.Side == MaimollerPlayerSide.Player2 ? 1 : 2)
                .ToList();

            return devices.FirstOrDefault()?.Device;
        }

        private static MaimollerPlayerSide DetectPlayerSide(HidDevice device)
        {
            try
            {
                if (device == null || !device.TryOpen(out HidStream stream))
                {
                    return MaimollerPlayerSide.Unknown;
                }

                using (stream)
                {
                    var featureBuffer = new byte[Math.Max(device.GetMaxFeatureReportLength(), 8)];
                    featureBuffer[0] = MaimollerConstants.ReportId;
                    stream.GetFeature(featureBuffer);
                    return featureBuffer.Length > 4 && featureBuffer[4] == 2
                        ? MaimollerPlayerSide.Player2
                        : MaimollerPlayerSide.Player1;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"HID_ENUM_FEATURE_FAILED: {device?.DevicePath} {ex.Message}");
                return MaimollerPlayerSide.Unknown;
            }
        }

        private static bool TryNormalizeInputReport(byte[] buffer, int bytesRead, out byte[] payload)
        {
            payload = Array.Empty<byte>();
            if (buffer == null || bytesRead < MaimollerConstants.PayloadLength)
            {
                return false;
            }

            if (bytesRead >= MaimollerConstants.PayloadLength + 1 && buffer[0] == MaimollerConstants.ReportId)
            {
                payload = buffer.Skip(1).Take(MaimollerConstants.PayloadLength).ToArray();
                return true;
            }

            payload = buffer.Take(MaimollerConstants.PayloadLength).ToArray();
            return true;
        }

        private static MaimollerInputSnapshot ParseSnapshot(byte[] payload)
        {
            return new MaimollerInputSnapshot(payload[5], payload[6]);
        }

        private void ProcessSnapshot(MaimollerInputSnapshot snapshot)
        {
            var currentInputMode = InputMode;
            if (currentInputMode == MaimollerInputRoutingMode.NetworkIpv4Edit)
            {
                ProcessNetworkIpv4EditorSnapshot(snapshot);
            }
            else
            {
                ProcessNavigationSnapshot(snapshot);
            }

            previousSnapshot = snapshot;
        }

        internal void ProcessSnapshotForTesting(MaimollerInputSnapshot snapshot)
        {
            ProcessSnapshot(snapshot);
        }

        private void ProcessNavigationSnapshot(MaimollerInputSnapshot snapshot)
        {
            var newPressMask = (byte)(snapshot.ButtonMask & ~previousSnapshot.ButtonMask);
            if ((newPressMask & (1 << 5)) != 0)
            {
                RaiseAction(UiInputAction.Up, "Button6");
            }

            if ((newPressMask & (1 << 2)) != 0)
            {
                RaiseAction(UiInputAction.Down, "Button3");
            }

            if ((newPressMask & (1 << 3)) != 0)
            {
                RaiseAction(UiInputAction.Confirm, "Button4");
            }

            if ((newPressMask & (1 << 4)) != 0)
            {
                RaiseAction(UiInputAction.Back, "Button5");
            }

            HandleCoinHold(snapshot);
        }

        private void ProcessNetworkIpv4EditorSnapshot(MaimollerInputSnapshot snapshot)
        {
            // maimoller_hid.md: payload[5] bit 0-7 = Button 1-8, payload[6] bit1 = Service, bit2 = Test.
            var newButtonPressMask = (byte)(snapshot.ButtonMask & ~previousSnapshot.ButtonMask);
            for (var buttonNumber = 1; buttonNumber <= 8; buttonNumber++)
            {
                var bitMask = 1 << (buttonNumber - 1);
                if ((newButtonPressMask & bitMask) != 0)
                {
                    RaiseDigit(buttonNumber, $"Button{buttonNumber}");
                }
            }

            var newSystemPressMask = (byte)(snapshot.SystemMask & ~previousSnapshot.SystemMask);
            if ((newSystemPressMask & (byte)MaimollerSystemButton.Test) != 0)
            {
                RaiseDigit(9, "Test");
            }

            if ((newSystemPressMask & (byte)MaimollerSystemButton.Service) != 0)
            {
                RaiseDigit(0, "Service");
            }

            HandleNetworkEditorCoin(snapshot);
        }

        private void HandleCoinHold(MaimollerInputSnapshot snapshot)
        {
            if (!snapshot.IsSystemPressed(MaimollerSystemButton.Coin))
            {
                ResetCoinState();
                return;
            }

            if (!coinHoldStartTimestamp.HasValue)
            {
                coinHoldStartTimestamp = Stopwatch.GetTimestamp();
                coinHoldConsumed = false;
                return;
            }

            if (coinHoldConsumed || IsMenuOpen || IgnoreMenuOpenRequests)
            {
                return;
            }

            var elapsedSeconds = (Stopwatch.GetTimestamp() - coinHoldStartTimestamp.Value) / (double)Stopwatch.Frequency;
            if (elapsedSeconds < MaimollerConstants.CoinHoldSeconds)
            {
                return;
            }

            coinHoldConsumed = true;
            RaiseAction(UiInputAction.OpenServiceMenu, "CoinHold15s");
        }

        private void HandleNetworkEditorCoin(MaimollerInputSnapshot snapshot)
        {
            var isCoinPressed = snapshot.IsSystemPressed(MaimollerSystemButton.Coin);
            if (!isCoinPressed)
            {
                if (coinHoldStartTimestamp.HasValue)
                {
                    var elapsedMilliseconds = (Stopwatch.GetTimestamp() - coinHoldStartTimestamp.Value) * 1000d / Stopwatch.Frequency;
                    if (elapsedMilliseconds >= MaimollerConstants.NetworkEditorCoinHoldMilliseconds)
                    {
                        RaiseRawInput(MaimollerRawInputKind.CoinLongPressConfirm, "CoinHold1s");
                    }
                    else
                    {
                        RaiseRawInput(MaimollerRawInputKind.CoinShortPress, "CoinShort");
                    }
                }

                ResetCoinState();
                return;
            }

            if (!coinHoldStartTimestamp.HasValue)
            {
                coinHoldStartTimestamp = Stopwatch.GetTimestamp();
            }
        }

        private void ResetState()
        {
            previousSnapshot = MaimollerInputSnapshot.Empty;
            ResetCoinState();
        }

        private void ResetCoinState()
        {
            coinHoldStartTimestamp = null;
            coinHoldConsumed = false;
        }

        private void RaiseAction(UiInputAction action, string source)
        {
            Trace.WriteLine($"HID_ACTION: Action={action} Source={source}");
            try
            {
                ActionRaised?.Invoke(this, new MaimollerActionEventArgs(action, source));
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"HID_ACTION_DISPATCH_FAILED: {ex}");
            }
        }

        private void RaiseDigit(int digit, string source)
        {
            RaiseRawInput(MaimollerRawInputKind.Digit, source, digit);
        }

        private void RaiseRawInput(MaimollerRawInputKind kind, string source, int? digit = null)
        {
            Trace.WriteLine($"HID_RAW_ACTION: Kind={kind} Digit={(digit.HasValue ? digit.Value.ToString() : "-")} Source={source}");
            try
            {
                RawInputRaised?.Invoke(this, new MaimollerRawInputEventArgs(kind, source, digit));
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"HID_RAW_DISPATCH_FAILED: {ex}");
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(MaimollerInputService));
            }
        }
    }
}