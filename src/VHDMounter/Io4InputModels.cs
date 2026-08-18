using System;

namespace VHDMounter
{
    // Protocol layout follows XxLittleCxX/lkick-io4 (MIT) and the button
    // positions follow TeamTofuShop/segatools mai2hook/io4.c.
    // https://github.com/XxLittleCxX/lkick-io4
    // https://gitea.tendokyu.moe/TeamTofuShop/segatools
    // The receiver sees the active-high IO4 state after mai2hook has converted
    // maimai's active-low ring inputs to IO4 button bits.
    internal enum Io4Button
    {
        P1Button1 = 0,
        P1Button2,
        P1Button3,
        P1Button4,
        P1Button5,
        P1Button6,
        P1Button7,
        P1Button8,
        P1Select,
        P2Button1,
        P2Button2,
        P2Button3,
        P2Button4,
        P2Button5,
        P2Button6,
        P2Button7,
        P2Button8,
        P2Select,
        Coin,
        Service,
        Test,
    }

    internal static class Io4Constants
    {
        public const int VendorId = 0x0CA3;
        public const int ProductId = 0x0021;
        public const byte InputReportId = 0x01;
        public const int InputReportDataLength = 63;
        public const int InputReportLengthWithId = InputReportDataLength + 1;

        // struct io4_report_in from segatools/common/board/io4.c:
        // report_id, adcs[8], spinners[4], chutes[2], buttons[2], ...
        public const int ChutesOffset = 24;
        public const int CoinCountOffset = ChutesOffset + 1;
        public const int SwitchesOffset = 28;

        public const ushort P1Button1Mask = 1 << 2;
        public const ushort P1Button2Mask = 1 << 3;
        public const ushort P1Button3Mask = 1 << 0;
        public const ushort P1Button4Mask = 1 << 15;
        public const ushort P1Button5Mask = 1 << 14;
        public const ushort P1Button6Mask = 1 << 13;
        public const ushort P1Button7Mask = 1 << 12;
        public const ushort P1Button8Mask = 1 << 11;
        public const ushort P1SelectMask = 1 << 1;
        public const ushort P2Button1Mask = 1 << 2;
        public const ushort P2Button2Mask = 1 << 3;
        public const ushort P2Button3Mask = 1 << 0;
        public const ushort P2Button4Mask = 1 << 15;
        public const ushort P2Button5Mask = 1 << 14;
        public const ushort P2Button6Mask = 1 << 13;
        public const ushort P2Button7Mask = 1 << 12;
        public const ushort P2Button8Mask = 1 << 11;
        public const ushort P2SelectMask = 1 << 4;
        public const ushort ServiceSwitchMask = 1 << 6;
        public const ushort TestSwitchMask = 1 << 9;

        public const int TestHoldMilliseconds = 1000;
        public const int CoinReleaseGapMilliseconds = 100;

        public static readonly Io4Button[] Player1Buttons =
        {
            Io4Button.P1Button1,
            Io4Button.P1Button2,
            Io4Button.P1Button3,
            Io4Button.P1Button4,
            Io4Button.P1Button5,
            Io4Button.P1Button6,
            Io4Button.P1Button7,
            Io4Button.P1Button8,
        };

        public static readonly Io4Button[] Player2Buttons =
        {
            Io4Button.P2Button1,
            Io4Button.P2Button2,
            Io4Button.P2Button3,
            Io4Button.P2Button4,
            Io4Button.P2Button5,
            Io4Button.P2Button6,
            Io4Button.P2Button7,
            Io4Button.P2Button8,
        };

        public static ushort GetMask(Io4Button button)
        {
            return GetPlayer1Mask(button);
        }

        public static ushort GetPlayer1Mask(Io4Button button)
        {
            switch (button)
            {
                case Io4Button.P1Button1: return P1Button1Mask;
                case Io4Button.P1Button2: return P1Button2Mask;
                case Io4Button.P1Button3: return P1Button3Mask;
                case Io4Button.P1Button4: return P1Button4Mask;
                case Io4Button.P1Button5: return P1Button5Mask;
                case Io4Button.P1Button6: return P1Button6Mask;
                case Io4Button.P1Button7: return P1Button7Mask;
                case Io4Button.P1Button8: return P1Button8Mask;
                case Io4Button.P1Select: return P1SelectMask;
                case Io4Button.Service: return ServiceSwitchMask;
                case Io4Button.Test: return TestSwitchMask;
                default: return 0;
            }
        }

        public static ushort GetPlayer2Mask(Io4Button button)
        {
            switch (button)
            {
                case Io4Button.P2Button1: return P2Button1Mask;
                case Io4Button.P2Button2: return P2Button2Mask;
                case Io4Button.P2Button3: return P2Button3Mask;
                case Io4Button.P2Button4: return P2Button4Mask;
                case Io4Button.P2Button5: return P2Button5Mask;
                case Io4Button.P2Button6: return P2Button6Mask;
                case Io4Button.P2Button7: return P2Button7Mask;
                case Io4Button.P2Button8: return P2Button8Mask;
                case Io4Button.P2Select: return P2SelectMask;
                default: return 0;
            }
        }

        public static bool IsPlayer1Button(Io4Button button)
        {
            return button >= Io4Button.P1Button1 && button <= Io4Button.P1Button8;
        }

        public static bool IsPlayer2Button(Io4Button button)
        {
            return button >= Io4Button.P2Button1 && button <= Io4Button.P2Button8;
        }

        public static int GetPlayer1ButtonNumber(Io4Button button)
        {
            return IsPlayer1Button(button) ? (int)button + 1 : 0;
        }

        public static int GetPlayer2ButtonNumber(Io4Button button)
        {
            return IsPlayer2Button(button) ? (int)button - (int)Io4Button.P2Button1 + 1 : 0;
        }
    }

    internal sealed class Io4InputSnapshot
    {
        public static Io4InputSnapshot Empty { get; } = new Io4InputSnapshot(0, 0, 0);

        public Io4InputSnapshot(ushort player1Switches, ushort player2Switches)
            : this(player1Switches, player2Switches, 0)
        {
        }

        public Io4InputSnapshot(ushort player1Switches, ushort player2Switches, byte coinCount)
        {
            Player1Switches = player1Switches;
            Player2Switches = player2Switches;
            CoinCount = coinCount;
        }

        public ushort Player1Switches { get; }

        public ushort Player2Switches { get; }

        // IO4 chutes[0] is a packed coin condition/count value. Segatools
        // writes coins << 8, so the count is the high byte of the little-endian
        // report field (payload offset 25, raw report index 26 with report ID).
        public byte CoinCount { get; }

        public bool IsPressed(Io4Button button)
        {
            var player2Mask = Io4Constants.GetPlayer2Mask(button);
            if (player2Mask != 0)
            {
                return (Player2Switches & player2Mask) != 0;
            }

            var player1Mask = Io4Constants.GetPlayer1Mask(button);
            if (player1Mask != 0)
            {
                return (Player1Switches & player1Mask) != 0;
            }

            var systemMask = button == Io4Button.Service
                ? Io4Constants.ServiceSwitchMask
                : button == Io4Button.Test ? Io4Constants.TestSwitchMask : (ushort)0;
            return systemMask != 0 && (Player1Switches & systemMask) != 0;
        }
    }

    internal sealed class Io4ButtonEventArgs : EventArgs
    {
        public Io4ButtonEventArgs(Io4Button button)
        {
            Button = button;
        }

        public Io4Button Button { get; }

        public string Source => $"IO4.{Button}";
    }

    internal enum Io4InputRoutingMode
    {
        Navigation = 0,
        NetworkIpv4Edit,
    }

    internal enum Io4RawInputKind
    {
        None = 0,
        Digit,
        CoinShortPress,
        CoinLongPressConfirm,
    }

    internal sealed class Io4ActionEventArgs : EventArgs
    {
        public Io4ActionEventArgs(UiInputAction action, string source)
        {
            Action = action;
            Source = source ?? string.Empty;
        }

        public UiInputAction Action { get; }

        public string Source { get; }
    }

    internal sealed class Io4RawInputEventArgs : EventArgs
    {
        public Io4RawInputEventArgs(Io4RawInputKind kind, string source, int? digit = null)
        {
            Kind = kind;
            Source = source ?? string.Empty;
            Digit = digit;
        }

        public Io4RawInputKind Kind { get; }

        public string Source { get; }

        public int? Digit { get; }
    }
}
