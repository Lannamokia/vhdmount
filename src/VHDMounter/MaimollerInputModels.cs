using System;

namespace VHDMounter
{
    internal enum MaimollerPlayerSide
    {
        Unknown = 0,
        Player1 = 1,
        Player2 = 2,
    }

    [Flags]
    internal enum MaimollerSystemButton : byte
    {
        None = 0,
        Coin = 1 << 0,
        Service = 1 << 1,
        Test = 1 << 2,
        Select = 1 << 3,
    }

    internal static class MaimollerConstants
    {
        public const int VendorId = 0x0E8F;
        public const int ProductId = 0x1224;
        public const byte ReportId = 0x01;
        public const int PayloadLength = 7;
        public const int CoinHoldSeconds = 15;

        public static bool IsCandidateDevicePath(string devicePath)
        {
            return !string.IsNullOrWhiteSpace(devicePath) &&
                   devicePath.IndexOf("&mi_00#", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    internal sealed class MaimollerInputSnapshot
    {
        public static MaimollerInputSnapshot Empty { get; } = new MaimollerInputSnapshot(0, 0);

        public MaimollerInputSnapshot(byte buttonMask, byte systemMask)
        {
            ButtonMask = buttonMask;
            SystemMask = (byte)(systemMask & 0x0F);
        }

        public byte ButtonMask { get; }

        public byte SystemMask { get; }

        public bool IsButtonPressed(int buttonNumber)
        {
            if (buttonNumber < 1 || buttonNumber > 8)
            {
                return false;
            }

            return (ButtonMask & (1 << (buttonNumber - 1))) != 0;
        }

        public bool IsSystemPressed(MaimollerSystemButton button)
        {
            return (SystemMask & (byte)button) != 0;
        }
    }

    internal sealed class MaimollerActionEventArgs : EventArgs
    {
        public MaimollerActionEventArgs(UiInputAction action, string source)
        {
            Action = action;
            Source = source ?? string.Empty;
        }

        public UiInputAction Action { get; }

        public string Source { get; }
    }
}