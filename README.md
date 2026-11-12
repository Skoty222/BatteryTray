# Battery Tray (Windows 11)

System-tray + window battery monitor for wireless peripherals. WPF on .NET 8, using only Windows' own APIs
(Windows.Devices.Bluetooth / Enumeration / HumanInterfaceDevice, Windows.Gaming.Input). No drivers, no NuGet packages.

**Status: written but not yet compiled or run.** It was authored in a Linux sandbox with no .NET SDK, no Windows and no network.
Treat the first build on your PC as the real test; see "First run checklist".

## Requirements
- End users: Windows 11 (Windows 10 2004+ also works), 64-bit. Nothing else.

## Packaging (what end users get)
End users get ONE file, `BatteryTray-Setup.exe` (per-user install, no admin prompt). It bundles the .NET runtime, so nothing else is
installed. Their steps are in `INSTALL.txt`. Unsigned installers trigger a one-time Windows SmartScreen "More info > Run anyway";
that can only be removed with a paid code-signing certificate.

### Make the installer (you, once) - pick ONE
**A. GitHub (nothing to install on your PC):** create a free GitHub repo, upload the contents of this folder (keep `.github`),
open the **Actions** tab > "Build installer" > Run workflow. When it finishes, download `BatteryTray-Setup` from the run's Artifacts.
The workflow also installs, launches and uninstalls the app on a fresh Windows runner and fails if any step breaks.

**B. On your own PC:** double-click `build-installer.bat`. It installs the .NET 8 SDK and Inno Setup with winget if missing,
then builds `dist\BatteryTray-Setup.exe`.

(Developers: `dotnet run` for a quick try; `dotnet publish -c Release -o publish` produces the single self-contained exe.)

## First run checklist
1. Run `BatteryTray.exe --diagnose`. It writes `%AppData%\BatteryTray\diagnostics.txt` and opens it. It lists every paired Bluetooth device
   with all properties Windows gives (including the battery property if present), HID mice (with VID/PID), game controllers and their
   battery reports, and finally what the app would show. If something is wrong or "Unavailable", this file shows why.
2. Pair/connect your headset and controller, then check they show names + connection status.
3. Errors are logged to `%AppData%\BatteryTray\log.txt`.

## How batteries are read (and what "Unavailable" means)
| Device type | Method | Notes |
|---|---|---|
| Bluetooth headset / mouse / controller | Windows battery property on the paired device (same data Settings > Bluetooth shows), then the standard Bluetooth LE Battery Service for LE devices | Only if the device reports it. Many headsets only report via Bluetooth HFP/vendor protocols. |
| Game controller | `Windows.Gaming.Input` battery report | Real % only if Windows gets capacity numbers. Wired = Unavailable. |
| USB 2.4 GHz receivers | **Presence only** (HID mouse interface). No battery is read. | Vendor protocols are not generic. |

Whenever none of these gives a level, the app shows **Unavailable**. It never estimates a current level.

## Glorious Model O 2 PRO
- It is **2.4 GHz only** (USB receiver), not Bluetooth.
- Glorious documents battery checks only via the **DPI-button LED colour code** (red 0-25, orange 26-60, yellow 61-90, green 91-100) and
  **Glorious CORE**. I found no public/documented API or Windows battery interface.
- Community tools reverse-engineered CORE's HID protocol for older Model O/D Wireless mice. That is undocumented, model-specific, and
  reviewers report CORE's own O 2 PRO 4K reading being unreliable. This app therefore does **not** talk to the mouse's HID protocol.
- What you get: the receiver is detected (vendor id 258A heuristic, confirm with `--diagnose`) and shown as "Receiver detected", battery
  **Unavailable**. Fallback: **Set manually...** lets you type the % you read from CORE/LED; it is labelled "Manual entry", shown with its
  age, and excluded from notifications, history and estimates.
- If your receiver has a different vendor id, add it to `ExtraUsbIds` in `%AppData%\BatteryTray\settings.json`, e.g. `["VID_258A"]`.

## Settings (gear button in the window; saved to %AppData%\BatteryTray\settings.json)
Low-battery threshold (default 15%) and on/off, start with Windows, theme (Windows/Light/Dark), scan interval, which details to show,
which devices to show. Scans run automatically (default every 60 s), when a controller is plugged/unplugged, and after resume from sleep.

## History / time remaining
Real readings are stored in `history.json` (30 days; one point per change or per 30 min). The estimate uses the current discharge run and
is shown only with >= 3 points over >= 30 min and a drop of >= 2 points; otherwise "not enough data yet". Windows doesn't tell us whether a
device is charging, so a rising level simply restarts the run.

## Known limitations
- Tray notifications use balloon tips (shown as Windows toasts). Focus Assist can hide them.
- Dark theme covers the app's own surfaces and title bar; stock WPF buttons/combo boxes keep the default look.
- Closing the window hides it to the tray; use tray > Exit to quit.
