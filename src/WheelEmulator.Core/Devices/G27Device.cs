using System.Runtime.InteropServices;
using HidSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace WheelEmulator.Core.Devices;

/// <summary>
/// Monitors the USB bus for the G27 and manages the mode-switch + FFB output path.
///
/// After the mode-switch command is sent the G27 re-enumerates as PID 0xC29B.
/// Windows' built-in HID class driver + DirectInput then handle all game input
/// natively — no virtual controller layer is required.
///
/// Device monitoring uses HidSharp's <c>DeviceList.Changed</c> event, which in
/// turn uses <c>RegisterDeviceNotification</c> internally. No polling, no kernel
/// driver, no Memory Integrity conflict.
/// </summary>
public sealed class G27Device : IDisposable
{
    private readonly ILogger<G27Device> _logger;
    private readonly object _stateLock = new();
    private HidDevice? _nativeDevice;
    private bool _disposed;

    /// <summary>Fires once each time the G27 transitions into native mode (PID 0xC29B).</summary>
    public event EventHandler? NativeModeConnected;

    /// <summary>Fires when the G27 is unplugged (either mode).</summary>
    public event EventHandler? Disconnected;

    /// <summary>Whether the wheel is currently in native mode and ready.</summary>
    public bool IsNativeModeActive { get; private set; }

    public G27Device(ILogger<G27Device> logger) => _logger = logger;

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Subscribe to USB device-change events and perform an immediate check for
    /// devices already connected at startup.
    /// </summary>
    public void StartMonitoring()
    {
        DeviceList.Local.Changed += OnDeviceListChanged;
        CheckDeviceState();
        _logger.LogInformation("G27 device monitoring started.");
    }

    /// <summary>Unsubscribe from device-change events.</summary>
    public void StopMonitoring()
    {
        DeviceList.Local.Changed -= OnDeviceListChanged;
        _logger.LogDebug("G27 device monitoring stopped.");
    }

    /// <summary>
    /// Write a force-feedback output report.
    /// Opens a short-lived HID stream for each write so no exclusive handle is
    /// held between commands, allowing games to open the device simultaneously.
    /// Silently no-ops when the device is not connected.
    /// </summary>
    /// <param name="command">
    /// 8-byte array: byte[0] = HID report-ID (0x00), bytes[1-7] = FFB payload.
    /// Use <see cref="G27ForceFeedbackCommand"/> to build valid commands.
    /// </param>
    public void SendForceFeedback(byte[] command)
    {
        HidDevice? dev;
        lock (_stateLock) dev = _nativeDevice;
        if (dev is null) return;

        try
        {
            using var stream = dev.Open();
            stream.WriteTimeout = 500;
            stream.Write(command);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "FFB write failed (non-fatal).");
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private void OnDeviceListChanged(object? sender, EventArgs e) => CheckDeviceState();

    private void CheckDeviceState()
    {
        // 1. Native mode present (already switched, or switched on a previous run).
        var nativeDev = FindDevice(G27Constants.NativeModeProductId);
        if (nativeDev is not null)
        {
            bool alreadyActive;
            lock (_stateLock)
            {
                alreadyActive = IsNativeModeActive;
                _nativeDevice = nativeDev;
                IsNativeModeActive = true;
            }

            if (!alreadyActive)
            {
                _logger.LogInformation(
                    "G27 ready in native mode (PID {Pid:X4}).",
                    G27Constants.NativeModeProductId);
                NativeModeConnected?.Invoke(this, EventArgs.Empty);
            }
            return;
        }

        // 2. Compatibility mode present — send mode-switch.
        var compatDev = FindDevice(G27Constants.CompatibilityModeProductId);
        if (compatDev is not null)
        {
            lock (_stateLock)
            {
                _nativeDevice = null;
                IsNativeModeActive = false;
            }

            _logger.LogInformation(
                "G27 detected in compatibility mode (PID {Pid:X4}). Sending mode-switch…",
                G27Constants.CompatibilityModeProductId);

            TrySendModeSwitchCommand(compatDev);
            // The device re-enumerates; DeviceList.Changed will fire again and
            // we will enter branch 1 above.
            return;
        }

        // 3. Neither PID found — wheel is unplugged.
        bool wasActive;
        lock (_stateLock)
        {
            wasActive = IsNativeModeActive;
            _nativeDevice = null;
            IsNativeModeActive = false;
        }

        if (wasActive)
        {
            _logger.LogInformation("G27 disconnected. Waiting for reconnect…");
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool TrySendModeSwitchCommand(HidDevice device)
    {
        try
        {
            using var handle = Win32Hid.OpenDevice(device.DevicePath);
            if (handle.IsInvalid)
            {
                _logger.LogError("Failed to open G27 for mode-switch (Win32 error {E}).",
                    Marshal.GetLastWin32Error());
                return false;
            }

            // G27 requires a two-step sequence (hid-lg4ff.c, lg4ff_mode_switch_ext09_g27).
            // Each packet is 8 bytes: report-ID 0x00 + 7-byte command.
            static byte[] Packet(byte[] cmd)
            {
                var p = new byte[8];
                cmd.CopyTo(p, 1);
                return p;
            }

            bool ok1 = Win32Hid.SetOutputReport(handle, Packet(G27Constants.ModeSwitchCommand1));
            bool ok2 = Win32Hid.SetOutputReport(handle, Packet(G27Constants.ModeSwitchCommand2));

            _logger.LogDebug("Mode-switch sent (step1={S1} step2={S2}).", ok1, ok2);
            return ok1 && ok2;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send mode-switch command.");
            return false;
        }
    }

    private static HidDevice? FindDevice(int productId) =>
        DeviceList.Local
                  .GetHidDevices(G27Constants.VendorId, productId)
                  .FirstOrDefault();

    // ── Win32 HID P/Invoke ────────────────────────────────────────────────
    // HidSharp's Write() uses WriteFile (interrupt OUT endpoint).
    // The G27 mode-switch requires HidD_SetOutputReport (USB control SET_REPORT),
    // matching the Linux kernel's hid_hw_request(HID_REQ_SET_REPORT) call path.

    private static class Win32Hid
    {
        private const uint GenericRead   = 0x80000000;
        private const uint GenericWrite  = 0x40000000;
        private const uint ShareRead     = 0x00000001;
        private const uint ShareWrite    = 0x00000002;
        private const uint OpenExisting  = 3;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition,
            uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_SetOutputReport(
            SafeFileHandle HidDeviceObject, byte[] ReportBuffer, int ReportBufferLength);

        public static SafeFileHandle OpenDevice(string devicePath) =>
            CreateFile(devicePath, GenericRead | GenericWrite, ShareRead | ShareWrite,
                IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

        public static bool SetOutputReport(SafeFileHandle handle, byte[] report) =>
            HidD_SetOutputReport(handle, report, report.Length);
    }

    // ── IDisposable ───────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopMonitoring();
    }
}
