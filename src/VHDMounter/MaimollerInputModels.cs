// Copyright 2024 MuNET Team
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// This file has been adapted from AquaMai (https://github.com/MuNET-OSS/AquaMai).

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
        public const int NetworkEditorCoinHoldMilliseconds = 1000;

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

    internal enum MaimollerInputRoutingMode
    {
        Navigation = 0,
        NetworkIpv4Edit,
    }

    internal enum MaimollerRawInputKind
    {
        None = 0,
        Digit,
        CoinShortPress,
        CoinLongPressConfirm,
    }

    internal sealed class MaimollerRawInputEventArgs : EventArgs
    {
        public MaimollerRawInputEventArgs(MaimollerRawInputKind kind, string source, int? digit = null)
        {
            Kind = kind;
            Source = source ?? string.Empty;
            Digit = digit;
        }

        public MaimollerRawInputKind Kind { get; }

        public string Source { get; }

        public int? Digit { get; }
    }
}