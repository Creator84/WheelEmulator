using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WheelEmulator.App;
using WheelEmulator.App.Services;
using WheelEmulator.Core;
using WheelEmulator.Core.Devices;

namespace WheelEmulator.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        // ── Driver-fix mode ───────────────────────────────────────────────
        // The tray app relaunches itself elevated with "--fix-driver oem262.inf"
        // when the user clicks "Fix LGS Driver Conflict". In this mode we just
        // run pnputil and exit — no host, no tray icon.
        if (args.Length >= 2 && args[0] == "--fix-driver")
        {
            string oemInf   = args[1];
            int    exitCode = DriverConflictDetector.DeleteDriver(oemInf);

            if (exitCode == 0)
            {
                MessageBox.Show(
                    "The old Logitech driver has been removed.\n\n" +
                    "Windows is rescanning devices — your G27 should reconnect automatically.\n" +
                    "If it doesn't appear within a few seconds, unplug and replug the USB cable.",
                    "WheelEmulator – Driver Fix",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(
                    $"pnputil returned exit code {exitCode}.\n\n" +
                    "The driver may not have been fully removed.\n" +
                    "Try running: pnputil /delete-driver " + oemInf + " /uninstall\n" +
                    "in an elevated command prompt.",
                    "WheelEmulator – Driver Fix Failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            return;
        }

        // ── Normal tray-app mode ──────────────────────────────────────────
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices(services =>
            {
                // Physical device monitor (singleton – one G27 per machine)
                services.AddSingleton<G27Device>();

                // Bridge: wires device monitor to initial FFB configuration
                services.AddSingleton<WheelBridge>();

                // Background service that starts/stops the bridge with the host
                services.AddHostedService<WheelEmulatorHostedService>();
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
#if DEBUG
                logging.AddDebug();
                logging.SetMinimumLevel(LogLevel.Debug);
#else
                // In release builds log to the Windows Event Log under
                // Applications and Services Logs > WheelEmulator.
                logging.AddEventLog(settings =>
                {
                    settings.SourceName = "WheelEmulator";
                });
                logging.SetMinimumLevel(LogLevel.Information);
#endif
            })
            .Build();

        var trayContext = new TrayApplicationContext(host);
        Application.Run(trayContext);
    }
}
