using Microsoft.Win32;
using System.Diagnostics;

namespace WheelEmulator.Core.Devices;

/// <summary>
/// Detects and removes the old Logitech Gaming Software kernel driver
/// (WmFilter.sys / wmjoyhid.inf) that causes error code 39 on the G27
/// HID device when Windows 11 Memory Integrity (HVCI) is enabled.
///
/// Root cause:
///   LGS installs oem262.inf (original name: wmjoyhid.inf) which claims
///   HID\VID_046D&PID_C294 and loads WmFilter.sys as an upper filter.
///   On HVCI-enabled Windows 11, WmFilter.sys fails the signature/isolation
///   check, leaving the HID device in ConfigManagerErrorCode 39 ("driver
///   cannot be loaded"). No userspace process can open the device.
///
/// Fix:
///   Remove the offending OEM driver package from the Windows driver store.
///   Windows will then fall back to the generic HID class driver on the
///   next plug-in cycle.
/// </summary>
public static class DriverConflictDetector
{
    private const string WmFilterServiceKey =
        @"SYSTEM\CurrentControlSet\Services\WmFilter";

    /// <summary>Original INF name that registers WmFilter for G27-class devices.</summary>
    private const string ConflictingOriginalInf = "wmjoyhid.inf";

    // ── Detection ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> if the WmFilter service is present in the registry,
    /// indicating that the old LGS driver package is installed.
    /// </summary>
    public static bool IsWmFilterInstalled()
    {
        using var key = Registry.LocalMachine.OpenSubKey(WmFilterServiceKey, writable: false);
        return key is not null;
    }

    /// <summary>
    /// Runs <c>pnputil /enum-drivers</c> and finds the published name (e.g.
    /// <c>oem262.inf</c>) of the package whose original filename is
    /// <c>wmjoyhid.inf</c>.
    /// </summary>
    /// <returns>
    /// The published OEM INF name (e.g. <c>oem262.inf</c>), or <c>null</c> if
    /// the package is not found in the driver store.
    /// </returns>
    public static string? FindConflictingOemInf()
    {
        try
        {
            string output = RunProcessOutput(PnpUtilPath, "/enum-drivers");
            return ParsePublishedName(output, ConflictingOriginalInf);
        }
        catch
        {
            return null;
        }
    }

    // ── Fix (must be called from an elevated process) ─────────────────────

    /// <summary>
    /// Calls <c>pnputil /delete-driver &lt;oemInfName&gt; /uninstall</c> and
    /// then <c>pnputil /scan-devices</c> to trigger re-enumeration.
    /// This method must be called from a process running with administrator
    /// privileges.
    /// </summary>
    /// <returns>pnputil exit code: 0 = success.</returns>
    public static int DeleteDriver(string oemInfName)
    {
        int exitCode = RunProcess(PnpUtilPath, $"/delete-driver {oemInfName} /uninstall");

        if (exitCode == 0)
        {
            // Ask Windows to rescan so the device node is immediately recreated
            // with the generic HID driver (user doesn't have to unplug).
            RunProcess(PnpUtilPath, "/scan-devices");
        }

        return exitCode;
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private static string PnpUtilPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "pnputil.exe");

    private static string? ParsePublishedName(string output, string targetOriginalName)
    {
        // pnputil /enum-drivers output (one block per driver package):
        //   Published Name:     oem262.inf
        //   Original Name:      wmjoyhid.inf
        //   Provider Name:      Logitech
        //   ...
        string? currentPublished = null;

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();

            if (line.StartsWith("Published Name:", StringComparison.OrdinalIgnoreCase))
            {
                currentPublished = SplitValue(line);
            }
            else if (line.StartsWith("Original Name:", StringComparison.OrdinalIgnoreCase))
            {
                var original = SplitValue(line);
                if (original is not null &&
                    original.Equals(targetOriginalName, StringComparison.OrdinalIgnoreCase))
                {
                    return currentPublished;
                }
            }
        }

        return null;
    }

    private static string? SplitValue(string line)
    {
        var idx = line.IndexOf(':');
        return idx >= 0 ? line[(idx + 1)..].Trim() : null;
    }

    private static string RunProcessOutput(string exe, string args)
    {
        using var p = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName               = exe,
                Arguments              = args,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                CreateNoWindow         = true
            }
        };
        p.Start();
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return output;
    }

    private static int RunProcess(string exe, string args)
    {
        using var p = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName        = exe,
                Arguments       = args,
                UseShellExecute = false,
                CreateNoWindow  = true
            }
        };
        p.Start();
        p.WaitForExit();
        return p.ExitCode;
    }
}
