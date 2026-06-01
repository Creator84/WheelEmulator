using Microsoft.Extensions.Logging;
using WheelEmulator.Core.Devices;

namespace WheelEmulator.Core;

/// <summary>
/// Wires the <see cref="G27Device"/> monitor to initial FFB configuration.
///
/// After the mode-switch the G27 is a fully functional DirectInput FFB joystick
/// under Windows' built-in HID class driver.  This class applies sensible FFB
/// defaults so the wheel doesn't flop around when no game is running.
/// </summary>
public sealed class WheelBridge : IDisposable
{
    private readonly G27Device            _g27;
    private readonly ILogger<WheelBridge> _logger;
    private bool _running;
    private bool _disposed;

    /// <summary>Whether the wheel is currently in native mode and ready for games.</summary>
    public bool IsRunning => _g27.IsNativeModeActive;

    public WheelBridge(G27Device g27, ILogger<WheelBridge> logger)
    {
        _g27    = g27;
        _logger = logger;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────

    public void Start()
    {
        if (_running) return;
        _running = true;

        _g27.NativeModeConnected += OnNativeModeConnected;
        _g27.Disconnected        += OnDisconnected;
        _g27.StartMonitoring();

        _logger.LogInformation("WheelBridge started – monitoring for G27.");
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;

        _g27.NativeModeConnected -= OnNativeModeConnected;
        _g27.Disconnected        -= OnDisconnected;
        _g27.StopMonitoring();

        _logger.LogInformation("WheelBridge stopped.");
    }

    // ── Event handlers ────────────────────────────────────────────────────

    private void OnNativeModeConnected(object? sender, EventArgs e)
    {
        _logger.LogInformation("Applying default FFB configuration…");

        // Disable the wheel's built-in autocenter spring so our spring
        // effect below is the only centering force.
        _g27.SendForceFeedback(G27ForceFeedbackCommand.Autocenter(false));

        // Apply a light center spring so the wheel doesn't flop when idle.
        // Games will override this with their own FFB effects once launched.
        _g27.SendForceFeedback(G27ForceFeedbackCommand.Spring(
            coefficient: 0x0D,
            saturation: 0x80));

        _logger.LogInformation(
            "G27 ready. The wheel is now usable in any DirectInput or legacy game.");
    }

    private void OnDisconnected(object? sender, EventArgs e) =>
        _logger.LogInformation("G27 disconnected – will reconnect automatically when re-plugged.");

    // ── IDisposable ───────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _g27.Dispose();
    }
}

