using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using WheelEmulator.Core.Devices;

namespace WheelEmulator.App;

/// <summary>
/// Windows Forms <see cref="ApplicationContext"/> that shows a system tray icon
/// and hosts the .NET Generic Host (which runs the background service).
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly IHost       _host;
    private readonly NotifyIcon  _trayIcon;
    private bool _hostStopping;

    public TrayApplicationContext(IHost host)
    {
        _host = host;

        _trayIcon = new NotifyIcon
        {
            Text             = "WheelEmulator – G27 on Windows 11",
            Icon             = SystemIcons.Information,
            ContextMenuStrip = BuildContextMenu(),
            Visible          = true
        };

        // Start the background service (non-blocking).
        _ = StartHostAsync();
    }

    // ── Context menu ──────────────────────────────────────────────────────

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        var header = new ToolStripLabel("WheelEmulator")
        {
            Font = new Font(SystemFonts.DefaultFont!, FontStyle.Bold)
        };
        menu.Items.Add(header);
        menu.Items.Add(new ToolStripSeparator());

        var statusItem = new ToolStripMenuItem("Status: starting…") { Enabled = false };
        menu.Items.Add(statusItem);
        menu.Items.Add(new ToolStripSeparator());

        // Show a Fix option only if the conflicting OEM INF is still in the driver store.
        // (The WmFilter registry service key persists after removal, so we check the
        // driver store directly via pnputil to avoid a stale false-positive.)
        if (DriverConflictDetector.FindConflictingOemInf() is not null)
        {
            var fixItem = new ToolStripMenuItem(
                "⚠ Fix LGS Driver Conflict (requires admin)…",
                null,
                OnFixDriverClicked)
            {
                ForeColor = System.Drawing.Color.DarkRed
            };
            menu.Items.Add(fixItem);
            menu.Items.Add(new ToolStripSeparator());
        }

        menu.Items.Add("🎮 Test / Calibrate wheel…", null, OnOpenJoyCplClicked);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, OnExitClicked);

        // Update status label whenever the menu is opened.
        menu.Opening += (_, _) =>
        {
            var bridge = (WheelEmulator.Core.WheelBridge?)
                _host.Services.GetService(typeof(WheelEmulator.Core.WheelBridge));

            statusItem.Text = bridge?.IsRunning == true
                ? "Status: G27 active (native mode) ✓"
                : "Status: waiting for G27…";
        };

        return menu;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────

    private async Task StartHostAsync()
    {
        try
        {
            await _host.StartAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"WheelEmulator failed to start:\n\n{ex.Message}",
                "WheelEmulator",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            ExitThread();
        }
    }

    private void OnOpenJoyCplClicked(object? sender, EventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName        = "rundll32.exe",
            Arguments       = "shell32.dll,Control_RunDLL joy.cpl",
            UseShellExecute = false
        });
    }

    private void OnFixDriverClicked(object? sender, EventArgs e)
    {
        string? oemInf = DriverConflictDetector.FindConflictingOemInf();

        if (oemInf is null)
        {
            MessageBox.Show(
                "Could not find the wmjoyhid.inf driver package in the driver store.\n\n" +
                "Try uninstalling 'Logitech Gaming Software' manually via Settings → Apps.",
                "WheelEmulator – Driver Fix",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            $"WheelEmulator will remove the old Logitech Gaming Software HID filter driver:\n\n" +
            $"  {oemInf}  (wmjoyhid.inf)\n\n" +
            "After removal, Windows will use the generic HID driver for your G27.\n" +
            "The G27 will unplug and replug automatically — no manual action needed.\n\n" +
            "Proceed?",
            "Fix LGS Driver Conflict",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1);

        if (confirm != DialogResult.Yes) return;

        // Relaunch ourselves elevated with --fix-driver <oemInf>.
        // The elevated instance runs pnputil and exits; it never shows a tray icon.
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = Application.ExecutablePath,
                Arguments       = $"--fix-driver {oemInf}",
                UseShellExecute = true,  // required for Verb = "runas"
                Verb            = "runas"
            });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // User cancelled UAC prompt — do nothing.
        }
    }

    private void OnExitClicked(object? sender, EventArgs e)
    {
        if (_hostStopping) return;
        _hostStopping = true;

        _trayIcon.Visible = false;
        _ = StopHostAsync();
    }

    private async Task StopHostAsync()
    {
        try
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            Application.Exit();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayIcon.Dispose();
            _host.Dispose();
        }
        base.Dispose(disposing);
    }
}
