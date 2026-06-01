# WheelEmulator - G27 on Windows 11

> **Logitech G27 stopped working after a Windows 11 update?**
> This app fixes it - no kernel driver, no disabling Memory Integrity.

---

## The problem

Windows 11 **24H2** introduced stricter **Memory Integrity (HVCI)** enforcement.
Logitech Gaming Software (LGS) installs a kernel filter driver (`WmFilter.sys` via
`wmjoyhid.inf`) that fails the HVCI check, putting the G27 HID device into
error code 39 - the wheel shows up in Device Manager with a yellow exclamation
mark and no game can open it.

Logitech has not updated LGS for modern Windows 11.

---

## The solution

WheelEmulator is a small tray application that:

1. **Removes the conflicting LGS kernel driver** (one-time, on-click)
2. **Sends the G27 mode-switch command** every time the wheel is plugged in,
   switching it from the limited compatibility PID (`0xC294`) to full native
   mode (`0xC29B`)
3. **Does nothing else** - Windows' own HID class driver and DirectInput stack
   handle all game input and force-feedback natively

```
G27 plugs in
  +-- PID 0xC294 (compat mode, limited axes)
       +-- WheelEmulator sends mode-switch (HidD_SetOutputReport)
            +-- G27 re-enumerates as PID 0xC29B (native mode)
                 +-- Windows hidclass.sys + DirectInput + FFB
                      +-- Game sees a fully working G27
```

No ViGEm, no virtual controller, no kernel driver - just the standard Windows HID API.

---

## Requirements

- Windows 11 (tested on 24H2+)
- Logitech G27 Racing Wheel
- **.NET 8 Desktop Runtime** - [download here](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
  *(not needed if you use the self-contained release build)*

> Memory Integrity / HVCI does **not** need to be disabled.

---

## Installation

1. Download `WheelEmulator.App.exe` from the [Releases](../../releases) page
2. Run it - a tray icon appears in the system tray
3. **First time only:** if the wheel still shows error 39 in Device Manager,
   right-click the tray icon -> **Fix LGS Driver Conflict (requires admin)...**
   and confirm the UAC prompt. This removes the old LGS filter driver.
4. Unplug and replug the wheel - it will switch to native mode automatically

**Auto-start with Windows:**
Press `Win+R` -> `shell:startup` -> paste a shortcut to `WheelEmulator.App.exe` there.

---

## Usage

The app runs silently in the background. Right-click the tray icon for options:

| Menu item | Action |
|-----------|--------|
| Status | Shows whether the G27 is active in native mode |
| Test / Calibrate wheel... | Opens the Windows Game Controllers panel (`joy.cpl`) |
| Fix LGS Driver Conflict... | Removes the old LGS kernel driver (shown only if needed) |
| Exit | Stops the app |

The wheel self-calibrates (rotates to its physical stops) on every USB connect -
no manual calibration is required. Use `joy.cpl` -> Properties -> Test to verify
all axes and buttons are working.

---

## Game compatibility

After the mode switch the G27 is a standard **DirectInput** device. It works in
any racing game that supports DirectInput wheels:

- Euro Truck Simulator 2 / American Truck Simulator
- Assetto Corsa / Assetto Corsa Competizione
- rFactor / rFactor 2
- DiRT series
- F1 series
- iRacing
- Richard Burns Rally
- ...and most other PC racing titles

**XInput-only games** (some console ports) won't see the G27 as a gamepad - use
[x360ce](https://www.x360ce.com/) for that mapping, which requires no kernel driver.

---

## Building from source

```powershell
git clone https://github.com/Creator84/WheelEmulator.git
cd WheelEmulator
dotnet build WheelEmulator.sln -c Release
```

**Publish a self-contained single-file executable:**

```powershell
dotnet publish src/WheelEmulator.App -c Release -o publish/
```

Produces a single `WheelEmulator.App.exe` (~70 MB) with no external dependencies.

---

## How it works (technical)

The G27 uses a two-step USB mode-switch sequence from the Linux kernel's
[hid-lg4ff.c](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-lg4ff.c):

| Step | Command | Purpose |
|------|---------|---------|
| 1 | `F8 0A 00 00 00 00 00` | Revert to factory mode on next USB reset |
| 2 | `F8 09 04 01 00 00 00` | Switch to G27 native mode with USB detach |

Both commands must be sent as **USB HID Set_Report control transfers**
(`HidD_SetOutputReport` on Windows, `hid_hw_request(HID_REQ_SET_REPORT)` on Linux).
Using `WriteFile` (interrupt OUT endpoint) does not work - the wheel ignores it.

WheelEmulator monitors device plug/unplug via HidSharp's `DeviceList.Changed`
event (backed by `RegisterDeviceNotification`) and re-sends the sequence on
every connect - no polling, no kernel component.

---

## Project structure

```
src/
  WheelEmulator.Core/
    Devices/
      G27Constants.cs               USB IDs, mode-switch bytes, FFB constants
      G27ForceFeedbackCommand.cs    Build HID output report payloads for FFB
      G27Device.cs                  Mode-switch + FFB write + device monitoring
      DriverConflictDetector.cs     Detect and remove LGS wmjoyhid.inf driver
    WheelBridge.cs                  Wire G27 events to default FFB config
  WheelEmulator.App/
    Program.cs                      Entry point, DI setup, --fix-driver CLI mode
    TrayApplicationContext.cs       System tray icon & context menu
    Services/
      WheelEmulatorHostedService.cs Start/stop the bridge with host lifetime
    app.manifest                    UAC asInvoker manifest
docs/
  G27Protocol.md                    HID protocol byte reference
```

---

## Known limitations

- **G27 only.** The G25, DFGT, Driving Force Pro, and G29 use different PIDs
  and different mode-switch sequences. Support for other wheels is not planned
  but PRs are welcome - the constants and command sequence are the only things
  that need changing.
- **DirectInput FFB only.** Force-feedback works via the Windows native HID PID
  stack. Game-specific FFB tuning (gain, clipping) is done in-game.
- **No XInput emulation.** Intentionally omitted - use x360ce if needed.

---

## Contributing

The FFB command parameters in `G27ForceFeedbackCommand.cs` are derived from
community reverse-engineering. If you have a Windows 10 machine with LGS
installed, capturing USB traffic with **Wireshark + USBPcap** and comparing it
against `docs/G27Protocol.md` is the most valuable contribution you can make.

Bug reports and PRs welcome.

---

## License

MIT