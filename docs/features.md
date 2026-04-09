# BrowserMux — Features

Functional inventory of BrowserMux. Each `##` is a feature area, each `###` is a sub-feature.
Items marked _(not implemented)_ are documented intentions that don't yet exist in code.

---

## Picker window

### Show on URL click
When BrowserMux is the default browser, clicking any link launches the AOT handler, which forwards the URL through the named pipe to the running app and shows the picker.

### Launcher mode (no URL)
The picker can be opened without a URL to act as a quick browser launcher (from tray or global hotkey). No URL bar is shown in this mode.

### Open URL from clipboard
Tray menu entry that reads the clipboard, normalizes/validates the URL, and shows the picker pre-loaded with it.

### Cursor-anchored positioning
The picker appears under the mouse cursor, DPI-aware, constrained to the current monitor's work area (multi-monitor safe).

### Mica backdrop, custom chrome
Fluent Mica material, no system title bar, custom drag region, no minimize/maximize. Acrylic is intentionally not offered.

### Always on top
Optional setting to keep the picker above all other windows.

### Close on focus loss
Optional setting (off by default) to dismiss the picker when it loses focus.

### Appear / disappear animations
Fade + scale on show (~120 ms), fade on hide (~80 ms). Respects the Windows "reduce motion" system setting.

### URL bar
Displays the incoming URL (truncated, full URL on tooltip) with a copy-to-clipboard button that shows a checkmark on success.

### Browser list
Vertical command-palette style list of detected browsers and Chromium profiles, each row showing icon, display name, optional profile subtitle, and shortcut badge.

### Profile color accents
Chromium profile rows show the profile's highlight color (read from `Local State`) as a colored backdrop behind the icon.

### Default browser banner
If BrowserMux isn't the system default, the footer shows a link that opens `ms-settings:defaultapps`.

### Settings shortcut
Footer button to open the Settings window from the picker.

---

## Picker interactions

### Click to launch
Left-click a row to open the URL in that browser/profile.

### Number key shortcuts (1–9)
Press a digit to launch the Nth visible browser directly.

### Arrow / Tab navigation
Up/Down and Tab/Shift+Tab cycle through rows; Enter launches the selected one.

### Escape to dismiss
Closes the picker without launching anything.

### Copy URL hotkey
`C` copies the current URL to the clipboard.

### Alt = incognito / private
Holding Alt (or Alt+Click, Alt+Enter, or right-click → "Open in private") launches the chosen browser in private mode. Uses `-private-window` for Firefox/LibreWolf and `--incognito` for Chromium-family browsers. A visual indicator appears on hover while Alt is held.

### Shift = "always open with"
Holding Shift switches the picker into rule-creation mode: clicking a browser creates a domain rule for the current URL's host so future visits auto-launch in that browser. The footer hint dynamically shows the target domain.

---

## Routing rules

### Auto-launch on match
When a URL arrives, the rule engine is consulted before showing the picker. On a match, the target browser is launched silently and the picker is skipped.

### Domain rules
Case-insensitive host match, including subdomains (`github.com` matches `api.github.com`).

### Glob rules
Wildcard patterns (`*`, `?`) over the URL host.

### Regex rules
Full .NET regex over the full URL, with a compiled-pattern cache for performance.

### Force-picker override (`_picker`)
A rule whose target browser id is `_picker` forces the picker to appear even when the rule matches — useful to override broader rules for specific domains.

### Shift+Click rule creation
See "Shift = always open with" in the picker section. Creates a Domain rule from the URL's host (stripping `www.`).

### Test a URL
Settings → Rules has a test field that runs a URL through the rule engine and shows which browser would handle it (or "no match").

### Persistence
Rules live in `%LOCALAPPDATA%\BrowserMux\rules.json` with a schema version and migration/backup on upgrade.

---

## Browser detection

### Registry scan
Reads `SOFTWARE\Clients\StartMenuInternet\*` from both HKCU and HKLM to discover installed browsers.

### App Paths fallback
Scans `App Paths` for Firefox, Opera, Whale and other browsers not registered as StartMenuInternet clients (catches some Store/MSIX installs).

### Chromium profile discovery
For Chrome, Brave, Edge, Vivaldi, Opera, and Chromium: scans `User Data\Default` and `Profile *` folders, reading profile name and highlight color from `Local State` (with `Preferences` JSON as fallback). System and Guest profiles are filtered out.

### Toggle profile detection
Settings option to disable Chromium profile discovery entirely (shows just the base browser).

### Stable browser ids
Each browser/profile has a stable id (`chrome.exe:::Profile 1`) used for pin/hide lists and rules.

### Manual rescan
Settings → Browsers has a "Reload" button that re-runs full detection.

### Custom browser entries _(not implemented)_
Adding a browser by manually pointing at an `.exe` is intentionally not supported.

---

## Settings — Browsers tab

### Show / hide browsers
Per-row toggle to hide a browser or profile from the picker. Stored in `HiddenBrowserIds`.

### Reorder pinned browsers
Up/Down arrow buttons reorder browsers; the order is persisted as the pinned order and reflected in the picker.

### Drag-and-drop reorder _(not implemented)_
WinUI 3 ListView drag-drop is unreliable in unpackaged apps. Arrow buttons are the supported workaround.

---

## Settings — Rules tab

### Rule list editor
Add, edit, and delete domain routing rules inline (pattern, match type, target browser).

### URL tester
Paste a URL and see which rule (and browser) it would resolve to.

---

## Settings — General / Appearance

### Theme
System / Light / Dark. The Settings window is recreated on theme change to avoid a known WinUI 3 freeze.

### Card size
Compact (64 px) / Normal (80 px) / Large (96 px). Icon extraction size scales to match.

### Always on top toggle
Mirrors the picker setting.

### Close on focus loss toggle
Off by default (this is a deliberate user preference).

### Detect Chromium profiles toggle
See Browser detection.

### Launcher hotkey
Global hotkey to open the picker in launcher mode. Click-to-capture editor accepts Ctrl/Alt/Shift/Win + letter, digit, F1–F24, or common navigation keys. Cleared via dedicated button. Re-registered live on change.

---

## Settings — About tab

### Version display
Shows the running version and a link to browsermux.com.

### Default browser status
Shows whether BrowserMux is the current Windows default for `https`, with a button that opens `ms-settings:defaultapps`.

### Update check
Queries the GitHub Releases API for a newer version, with a 6-hour cooldown between automatic checks (one runs ~5 s after launch). Manual "check now" button. If an update is available, downloads the installer with a progress bar and launches it.

---

## System integration

### Default browser registration
Registry keys (`BrowserMuxURL` ProgId, `Capabilities`, `RegisteredApplications`, http/https `URLAssociations`) are written to HKLM by the Inno Setup installer, or to HKCU by `register.ps1` for development. The app never writes `UserChoice` directly.

### Registration health check
On startup the app verifies the expected registry keys are present and logs warnings if anything is missing.

### AOT handler
`BrowserMux.Handler.exe` is the small native-AOT executable actually registered as the browser. It starts in well under 100 ms, forwards the incoming URL to the running app over the named pipe, and launches the app if it isn't already running.

### Named-pipe IPC
`\\.\pipe\BrowserMuxPipe` carries one URL per connection from handler → app.

### Single-instance mutex
`BrowserMux_SingleInstance` ensures only one app process runs. A second launch forwards its URL argument over the pipe and exits.

### System tray icon
Tray icon with a context menu: Open launcher, Open URL from clipboard, Settings, Exit. Left-click opens the launcher.

### Clipboard helpers
Read URL from clipboard (with normalization and length cap) and write URL to clipboard from the picker.

---

## Persistence

### Preferences file
`%LOCALAPPDATA%\BrowserMux\preferences.json` — theme, card size, hotkey, toggles, pinned/hidden browser ids, last update check.

### Rules file
`%LOCALAPPDATA%\BrowserMux\rules.json` — domain routing rules.

### Schema versioning and migration
Both files carry a schema version. Older versions are migrated and the original is backed up to `*.v{old}.bak`. A future version triggers a `*.future.bak` and a fresh-default fallback.

### Centralized service + change events
A singleton `PreferencesService` owns load/save and raises a `SettingsChanged` event so the UI, hotkey service, and picker react to mutations live.

---

## Logging & diagnostics

### Rotating app log
`%LOCALAPPDATA%\BrowserMux\logs\app.log`, trimmed to ~500 lines at startup, with INFO/WARN/ERROR levels and millisecond timestamps. See [logger.md](logger.md).

### Crash dump
Unhandled exceptions are written to `%TEMP%\BrowserMux_crash.txt` with timestamp, message, and stack trace.

---

## Intentionally not implemented

These appear in the original design notes but are deliberately deferred or dropped:

- **Acrylic backdrop** — `DesktopAcrylicController` is unreliable in unpackaged WinUI 3; Mica is the only material.
- **Drag & drop browser reorder** — replaced with arrow buttons.
- **Add custom browser by exe path** — detection is registry-only.
- **Timed default browser** ("default for the next N minutes") — deferred post-launch.
