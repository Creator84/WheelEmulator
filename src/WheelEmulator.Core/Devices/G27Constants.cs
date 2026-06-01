namespace WheelEmulator.Core.Devices;

/// <summary>
/// USB/HID identifiers and protocol constants for the Logitech G27 Racing Wheel.
/// </summary>
public static class G27Constants
{
    // ── USB Identifiers ──────────────────────────────────────────────────────

    public const int VendorId = 0x046D;

    /// <summary>
    /// PID when the G27 first enumerates (limited "boot" / compatibility mode).
    /// In this mode the wheel reports only 2 axes and no paddle shifters.
    /// </summary>
    public const int CompatibilityModeProductId = 0xC294;

    /// <summary>
    /// PID after the mode-switch command is issued.
    /// Full 14-bit steering, all 24 buttons, H-shifter and FFB all work here.
    /// </summary>
    public const int NativeModeProductId = 0xC29B;

    // ── Mode-switch ──────────────────────────────────────────────────────────
    //
    // The G27 requires a TWO-step sequence (hid-lg4ff.c, lg4ff_mode_switch_ext09_g27).
    // Both commands must be sent as USB HID Set_Report control transfers
    // (HidD_SetOutputReport on Windows, hid_hw_request on Linux) — NOT via the
    // interrupt-OUT endpoint (WriteFile / hid_hw_output_report).

    /// <summary>
    /// Step 1: instructs the wheel to revert to its factory mode on the next USB reset.
    /// </summary>
    public static readonly byte[] ModeSwitchCommand1 =
        { 0xF8, 0x0A, 0x00, 0x00, 0x00, 0x00, 0x00 };

    /// <summary>
    /// Step 2: triggers a USB re-enumeration into G27 native mode (PID <see cref="NativeModeProductId"/>).
    /// </summary>
    public static readonly byte[] ModeSwitchCommand2 =
        { 0xF8, 0x09, 0x04, 0x01, 0x00, 0x00, 0x00 };

    /// <summary>Milliseconds to wait for re-enumeration after a mode switch.</summary>
    public const int ModeSwitchWaitMs = 2000;

    // ── Native-mode HID report layout ────────────────────────────────────────

    /// <summary>
    /// Byte length of one native-mode HID input report (excluding the report-ID
    /// prefix byte that HidSharp always prepends).
    /// </summary>
    public const int NativeInputReportSize = 11;

    public const int ButtonCount = 24;

    // Steering: 14-bit value, logical range 0 – 16 383
    public const int SteeringMin    = 0;
    public const int SteeringMax    = 16_383;
    public const int SteeringCenter = 8_192;

    // Pedals: inverted (0x00 = fully pressed, 0xFF = released)
    public const byte PedalFullyPressed = 0x00;
    public const byte PedalReleased     = 0xFF;

    // H-Shifter center position on both axes
    public const byte ShifterCenter = 128;

    // ── Force-feedback command IDs (byte 0 of the 7-byte output report) ─────
    //
    // Protocol derived from:
    //   • Linux kernel drivers/hid/hid-logitech-ff.c
    //   • Community Wireshark captures of LGS ↔ G27 USB traffic
    //
    // All commands are 7 bytes long and are sent as HID output reports.
    // HidSharp requires a leading 0x00 report-ID byte, making the Write()
    // call 8 bytes in total (see G27ForceFeedbackCommand).

    public const byte FfbCmdConstantForce = 0x00;
    public const byte FfbCmdSpring        = 0x0B;
    public const byte FfbCmdDamper        = 0x0C;
    public const byte FfbCmdAutocenter    = 0x14;

    /// <summary>7-byte payload that stops all active FFB effects.</summary>
    public static readonly byte[] FfbStopAll =
        { FfbCmdConstantForce, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
}
