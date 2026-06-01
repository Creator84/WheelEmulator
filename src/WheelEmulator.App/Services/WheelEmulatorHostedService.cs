using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WheelEmulator.Core;

namespace WheelEmulator.App.Services;

/// <summary>
/// Starts the <see cref="WheelBridge"/> and keeps it alive for the host lifetime.
/// Reconnection on unplug/replug is handled event-driven by HidSharp's
/// <c>DeviceList.Changed</c> — no polling loop is required.
/// </summary>
public sealed class WheelEmulatorHostedService : BackgroundService
{
    private readonly WheelBridge _bridge;
    private readonly ILogger<WheelEmulatorHostedService> _logger;

    public WheelEmulatorHostedService(
        WheelBridge bridge,
        ILogger<WheelEmulatorHostedService> logger)
    {
        _bridge = bridge;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _bridge.Start();
        _logger.LogInformation("WheelEmulator service started. Monitoring for G27.");

        // Block until the host signals shutdown; device events are driven by
        // HidSharp's DeviceList.Changed — no polling needed.
        return Task.Delay(Timeout.Infinite, stoppingToken)
                   .ContinueWith(_ =>
                   {
                       _bridge.Stop();
                       _logger.LogInformation("WheelEmulator service stopped.");
                   }, TaskScheduler.Default);
    }
}
