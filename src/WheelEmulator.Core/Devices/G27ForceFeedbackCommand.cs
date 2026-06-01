namespace WheelEmulator.Core.Devices;

/// <summary>
/// Builds 7-byte HID output reports for the G27's force-feedback subsystem.
///
/// Protocol reference:
///   • Linux kernel drivers/hid/hid-logitech-ff.c (lgff2 / logitech-hidpp)
///   • Wireshark captures of LGS ↔ G27 USB traffic (community research)
///
/// Each public method returns the full byte[] ready to hand to
/// <c>HidStream.Write()</c>. HidSharp requires the first byte to be the
/// HID report ID; the G27 uses report ID 0x00 (no explicit ID), so every
/// returned array begins with 0x00 followed by 7 data bytes (8 bytes total).
/// </summary>
public static class G27ForceFeedbackCommand
{
    // ── Internal helpers ─────────────────────────────────────────────────

    /// <summary>Prepend the 0x00 HID report-ID byte that HidSharp expects.</summary>
    private static byte[] WithId(byte[] payload)
    {
        var buf = new byte[payload.Length + 1];
        buf[0] = 0x00; // report ID
        payload.CopyTo(buf, 1);
        return buf;
    }

    // ── Public command factories ─────────────────────────────────────────

    /// <summary>
    /// Immediately stop all active force-feedback effects.
    /// Use this on startup/shutdown to ensure the wheel is in a known state.
    /// </summary>
    public static byte[] StopAll() =>
        WithId(new byte[] { G27Constants.FfbCmdConstantForce, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });

    /// <summary>
    /// Apply a constant (unidirectional) force to the wheel.
    /// </summary>
    /// <param name="magnitude">
    ///   0 – 255. Higher = stronger. The direction is set by <paramref name="pushRight"/>.
    /// </param>
    /// <param name="pushRight">
    ///   <c>true</c> = push the wheel to the right;
    ///   <c>false</c> = push the wheel to the left.
    /// </param>
    public static byte[] ConstantForce(byte magnitude, bool pushRight)
    {
        // Byte layout: [cmd, direction, magnitude, 0, 0, 0, 0]
        // Direction: 0x00 = left, 0x01 = right (confirmed via LGS USB capture)
        byte dir = pushRight ? (byte)0x01 : (byte)0x00;
        return WithId(new byte[] { G27Constants.FfbCmdConstantForce, dir, magnitude, 0x00, 0x00, 0x00, 0x00 });
    }

    /// <summary>
    /// Apply a signed constant force.
    /// Positive values push right; negative values push left.
    /// </summary>
    /// <param name="force">Range –255 … +255. Clamped internally.</param>
    public static byte[] ConstantForceSigned(int force)
    {
        int clamped = Math.Clamp(force, -255, 255);
        return ConstantForce((byte)Math.Abs(clamped), pushRight: clamped >= 0);
    }

    /// <summary>
    /// Configure a spring (centering) effect.
    /// The spring pulls the wheel back to the straight-ahead position.
    /// </summary>
    /// <param name="coefficient">
    ///   Spring stiffness 0 – 255. A value around 0x0D is a gentle centre spring;
    ///   0xFF is very stiff.
    /// </param>
    /// <param name="saturation">
    ///   Maximum output force 0 – 255 (0xFF = uncapped).
    /// </param>
    public static byte[] Spring(byte coefficient, byte saturation = 0xFF) =>
        // [cmd, deadzone+, deadzone-, K+, K-, saturation, 0]
        WithId(new byte[]
        {
            G27Constants.FfbCmdSpring,
            0x00,        // dead-zone positive side (0 = no dead-zone)
            0x00,        // dead-zone negative side
            coefficient, // spring coefficient positive direction
            coefficient, // spring coefficient negative direction
            saturation,
            0x00
        });

    /// <summary>
    /// Configure a damper (velocity-proportional resistance) effect.
    /// Heavier damping slows the wheel and reduces oscillation.
    /// </summary>
    /// <param name="coefficient">Damping coefficient 0 – 255.</param>
    public static byte[] Damper(byte coefficient) =>
        // [cmd, C+, C-, C+, C-, saturation, T]
        WithId(new byte[]
        {
            G27Constants.FfbCmdDamper,
            coefficient,
            coefficient,
            coefficient,
            coefficient,
            0xFF, // saturation
            0x00  // threshold
        });

    /// <summary>
    /// Enable or disable the G27's built-in autocenter spring.
    /// Disabling autocenter is recommended when applying your own spring effect.
    /// </summary>
    public static byte[] Autocenter(bool enable) =>
        // 0x14 command: byte[1]=0x08, byte[2]=0x80 enables; all zeros disables.
        enable
            ? WithId(new byte[] { G27Constants.FfbCmdAutocenter, 0x08, 0x80, 0x00, 0x00, 0x00, 0x00 })
            : WithId(new byte[] { G27Constants.FfbCmdAutocenter, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });
}
