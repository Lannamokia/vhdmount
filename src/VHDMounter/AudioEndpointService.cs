using System;
using NAudio.CoreAudioApi;

namespace VHDMounter
{
    internal sealed class AudioEndpointSnapshot
    {
        public AudioEndpointSnapshot(bool isAvailable, string deviceName, int volumePercent, string statusMessage)
        {
            IsAvailable = isAvailable;
            DeviceName = deviceName ?? string.Empty;
            VolumePercent = volumePercent;
            StatusMessage = statusMessage ?? string.Empty;
        }

        public bool IsAvailable { get; }

        public string DeviceName { get; }

        public int VolumePercent { get; }

        public string StatusMessage { get; }

        public static AudioEndpointSnapshot Unavailable(string statusMessage)
        {
            return new AudioEndpointSnapshot(false, string.Empty, 0, statusMessage);
        }
    }

    internal sealed class AudioEndpointService : IDisposable
    {
        private readonly MMDeviceEnumerator enumerator = new MMDeviceEnumerator();
        private bool disposed;

        public AudioEndpointSnapshot GetDefaultRenderSnapshot()
        {
            ThrowIfDisposed();

            try
            {
                using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                var volumePercent = (int)Math.Round(device.AudioEndpointVolume.MasterVolumeLevelScalar * 100d, MidpointRounding.AwayFromZero);
                return new AudioEndpointSnapshot(true, device.FriendlyName, Math.Clamp(volumePercent, 0, 100), "调整后立即生效。");
            }
            catch (Exception ex)
            {
                return AudioEndpointSnapshot.Unavailable($"未找到可用的默认输出设备：{ex.Message}");
            }
        }

        public AudioEndpointSnapshot AdjustVolumeByPercent(int deltaPercent)
        {
            ThrowIfDisposed();

            try
            {
                using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                var currentPercent = (int)Math.Round(device.AudioEndpointVolume.MasterVolumeLevelScalar * 100d, MidpointRounding.AwayFromZero);
                var nextPercent = Math.Clamp(currentPercent + deltaPercent, 0, 100);
                device.AudioEndpointVolume.MasterVolumeLevelScalar = nextPercent / 100f;
                return new AudioEndpointSnapshot(true, device.FriendlyName, nextPercent, "调整后立即生效。");
            }
            catch (Exception ex)
            {
                return AudioEndpointSnapshot.Unavailable($"音量调节失败：{ex.Message}");
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            enumerator.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(AudioEndpointService));
            }
        }
    }
}