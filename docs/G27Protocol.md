# G27 USB / HID Protocol Reference

This document summarises the USB and HID details needed to communicate with the
Logitech G27 Racing Wheel (VID `0x046D`) without Logitech Gaming Software.

---

## 1  USB identifiers

| Mode | Product ID | Notes |
|------|-----------|-------|
| Compatibility (boot) | `0xC294` | Reported on first plug-in; only 2 axes visible |
| Native (extended) | `0xC29B` | Full 24 buttons, 14-bit steering, H-shifter, FFB |

The device re-enumerates after the mode-switch command, so the OS will remove and
re-add the device node.  This takes approximately 1–2 seconds.

---

## 2  Mode-switch command

Send to the device **while it is in compatibility mode** as a 7-byte HID output
report (8 bytes including the leading report-ID `0x00` required by HidSharp /
Windows HID class driver):

```
00  F8  09  00  00  00  00  00
^   ^
|   └─ payload (7 bytes)
└───── report ID (always 0x00 for this device)
```

Source: community Wireshark captures of LGS ↔ G27 USB traffic; confirmed in
the Linux kernel `hid-logitech-ff.c` driver.

---

## 3  Native-mode HID input report (11 bytes)

HidSharp prefixes one report-ID byte (`0x00`), making the `Read()` buffer 12 bytes.
The table below shows the 11 data bytes (after stripping the prefix).

| Byte | Bits | Content |
|------|------|---------|
| 0 | 7..0 | Buttons 8..1 (bit 0 = button 1) |
| 1 | 7..0 | Buttons 16..9 |
| 2 | 3..0 | Buttons 20..17 |
| 2 | 7..4 | D-pad hat (see §3.1) |
| 3 | 3..0 | Buttons 24..21 (incl. paddle shifters) |
| 3 | 7..4 | Reserved / unused |
| 4 | 7..0 | Steering low byte |
| 5 | 5..0 | Steering high 6 bits → `steering = byte4 \| ((byte5 & 0x3F) << 8)` |
| 6 | 7..0 | Gas / Throttle — **inverted**: `0xFF` = released, `0x00` = floored |
| 7 | 7..0 | Brake — inverted |
| 8 | 7..0 | Clutch — inverted |
| 9 | 7..0 | H-Shifter X: `0` = far-left, `128` = centre, `255` = far-right |
| 10 | 7..0 | H-Shifter Y: `0` = top row,  `128` = centre, `255` = bottom row |

### 3.1  D-pad encoding (nibble `byte2 >> 4`)

| Value | Direction |
|-------|-----------|
| 0 | North |
| 1 | North-East |
| 2 | East |
| 3 | South-East |
| 4 | South |
| 5 | South-West |
| 6 | West |
| 7 | North-West |
| 8 | Centre (not pressed) |

### 3.2  Steering range

14-bit logical range: **0** (full left) → **16 383** (full right), centre ≈ **8 192**.

---

## 4  HID output reports (Force Feedback)

Output reports are 7 bytes of payload; add a leading `0x00` report-ID byte for
HidSharp, giving 8 bytes per `Write()` call.

### 4.1  Command table

| Cmd byte | Effect | Remaining 6 bytes |
|----------|--------|-------------------|
| `0x00` | Stop all effects / constant force | `[dir, mag, 0, 0, 0, 0]` — `dir`: `0x00`=left `0x01`=right; `mag` 0–255 |
| `0x0B` | Spring (centering) | `[0, 0, Kp, Kn, sat, 0]` — Kp/Kn spring coefficients, sat = saturation |
| `0x0C` | Damper | `[C, C, C, C, sat, 0]` — C = damping coefficient |
| `0x14` | Autocenter | `[0x08, 0x80, 0, 0, 0, 0]` = enable; all zeros = disable |

### 4.2  Extended commands (prefix `0xF8`)

| Second byte | Purpose |
|-------------|---------|
| `0x09` | **Mode switch** (compatibility → native); see §2 |
| `0x01` | Autocenter on/off (alternative encoding) |

---

## 5  Calibration notes

The exact parameter bytes above are derived from community reverse-engineering
and the Linux kernel driver.  Values should be verified by:

1. Installing [WheelCheck](https://www.lfs.net/forum/thread/74754) on a Windows 10
   machine with LGS, and capturing USB traffic with Wireshark + USBPcap.
2. Comparing the captured packets with the constants in
   `G27ForceFeedbackCommand.cs` and `G27Constants.cs`.

Any corrections should be committed back here.
