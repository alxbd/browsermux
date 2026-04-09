# BrowserMux Installer (Inno Setup)

The installer is built with **Inno Setup 6** and produces a single self-contained `.exe`
that installs BrowserMux, registers it as a Windows browser, and (optionally) wipes user
data on uninstall.

Output: `dist/BrowserMux-Setup-<version>.exe` (~9 MB)

---

## Quick start

```powershell
# One-shot: build Release + compile installer
pwsh build-installer.ps1
```

That's it. The script builds in Release mode, deploys to `out/`, finds `iscc.exe`, and
compiles `installer/setup.iss` into `dist/`.

### Manual (equivalent)

```powershell
pwsh build.ps1 -Config Release
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" installer\setup.iss
```

---

## Prerequisites

### On the dev machine

- **Inno Setup 6**: `winget install -e --id JRSoftware.InnoSetup`
  - winget installs **per-user** to `%LOCALAPPDATA%\Programs\Inno Setup 6\` (not `Program Files`)
  - The script `build-installer.ps1` searches both locations
- **VS 2026 Insiders MSBuild** (already required by `build.ps1`)

### On the target machine

Both prerequisites are **detected and downloaded on the fly** by the installer if missing —
nothing is bundled, the installer stays small (~9 MB).

- **.NET 9 Desktop Runtime (x64)**
  - Detected by scanning `{commonpf}\dotnet\shared\Microsoft.WindowsDesktop.App\9.*`
  - If missing, downloaded silently from
    `https://aka.ms/dotnet/9.0/windowsdesktop-runtime-win-x64.exe` and installed with
    `/install /quiet /norestart`
- **Windows App SDK 1.7 Runtime (x64)** — required by WinUI 3, not preinstalled on Windows 10
  - Detected via `Get-AppxPackage Microsoft.WindowsAppRuntime.1.7`
  - If missing, downloaded from
    `https://aka.ms/windowsappsdk/1.7/latest/windowsappruntimeinstall-x64.exe` and installed
    with `--quiet`

Both downloads happen on the **"Ready to install"** wizard page via Inno Setup 6.1+'s built-in
`TDownloadWizardPage` (`CreateDownloadPage`). Exit codes `0`, `1641`, `3010` (the latter two
mean reboot required) are tolerated. Requires **Inno Setup 6.1.0+** for the download API.

---

## Install modes (hybrid)

The installer is **hybrid**: at launch, Inno asks the user "Install for all users / Install for me only".

| Mode | Privileges | Install path | Registry | UAC prompt |
|---|---|---|---|---|
| **Per-user** (default) | none | `%LOCALAPPDATA%\Programs\BrowserMux\` | `HKCU` | ❌ |
| **All users** | admin | `C:\Program Files\BrowserMux\` | `HKLM` | ✅ |

This is configured via:
```ini
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
```

And every `[Registry]` entry uses `Root: HKA` ("auto") instead of hardcoded `HKLM`/`HKCU`.
`{autopf}` similarly resolves to the right Program Files location for the chosen mode.

### Why hybrid?

- **No UAC prompt for the common case** — most users install for themselves
- Works on locked-down corporate machines where admin rights aren't available
- Still supports machine-wide install when needed (shared workstation, lab, etc.)
- Matches what Chrome, Firefox, VS Code do

---

## What the installer writes

### Files (`[Files]` section)

```ini
Source: "..\out\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
```

Copies everything from `out/` to `{app}` (= `Program Files\BrowserMux\` or per-user equivalent).
That folder is filled by `build.ps1` and contains:

- `BrowserMux.exe` — main WinUI 3 app
- `BrowserMux.Handler.exe` — AOT handler registered as the URL protocol target
- `BrowserMux.Core.dll` — shared library
- `*.dll` — .NET runtime + WinUI 3 + WinAppSDK
- `runtimes\` — native platform DLLs (win-x64, win-arm64)
- `Assets\AppIcon.ico` — the `>M` icon used everywhere

### Registry (`[Registry]` section)

All keys use `Root: HKA` so they go to HKLM (machine install) or HKCU (per-user install)
automatically. Each top-level key has `Flags: uninsdeletekey` so uninstall cleans up.

| Key | Purpose |
|---|---|
| `Software\Classes\BrowserMuxURL` | ProgId — declares the URL handler |
| `Software\Classes\BrowserMuxURL\DefaultIcon` | Icon shown next to BrowserMux in protocol pickers |
| `Software\Classes\BrowserMuxURL\shell\open\command` | `"{app}\BrowserMux.Handler.exe" "%1"` — what runs when a link is clicked |
| `Software\Clients\StartMenuInternet\BrowserMux` | Declares BrowserMux as a **browser** to Windows |
| `Software\Clients\StartMenuInternet\BrowserMux\Capabilities` | `ApplicationName`, `ApplicationDescription`, `ApplicationIcon` |
| `Software\Clients\StartMenuInternet\BrowserMux\Capabilities\URLAssociations` | `http`/`https` → `BrowserMuxURL` |
| `Software\RegisteredApplications\BrowserMux` | Index entry making it visible in Settings → Default Apps |

### Shortcuts (`[Icons]`)

- Start Menu: `{group}\BrowserMux` and `{group}\Uninstall BrowserMux`
- Desktop: optional, gated by the `desktopicon` task (unchecked by default)

### After install (`[Run]`)

- Optional postinstall step (unchecked by default): opens `ms-settings:defaultapps` so the
  user can pick BrowserMux as their default browser.
- We **never** write to `HKCU\...\UserChoice` directly — Windows 10 1903+ verifies it with a
  hash and silently reverts unauthorized writes. Going through Settings is the only reliable
  way (the OS computes the hash itself).

---

## Prerequisite detection & on-the-fly install

All logic lives in the `[Code]` section of `setup.iss`. Two helpers detect the prerequisites,
and `NextButtonClick(wpReady)` downloads + installs whatever is missing right before files are
copied.

### .NET 9 Desktop Runtime

```pascal
function IsDotNet9DesktopInstalled(): Boolean;
```

Scans `{commonpf}\dotnet\shared\Microsoft.WindowsDesktop.App` for any `9.*` subfolder. We do
not shell out to `dotnet --list-runtimes` because the CLI is not always on `PATH`.

### Windows App SDK 1.7

```pascal
function IsWindowsAppRuntime17Installed(): Boolean;
```

Runs `Get-AppxPackage -Name Microsoft.WindowsAppRuntime.1.7` via PowerShell, captures stdout
to a temp file, and considers the package installed if any non-empty version line comes back.
The framework MSIX is provisioned globally, so a per-user `Get-AppxPackage` query still finds
it.

### Download flow

`InitializeWizard` creates a `TDownloadWizardPage`. On `wpReady → Next`, we check both
prerequisites and queue only the missing installers:

| Missing | URL queued | Args |
|---|---|---|
| .NET 9 Desktop Runtime | `https://aka.ms/dotnet/9.0/windowsdesktop-runtime-win-x64.exe` | `/install /quiet /norestart` |
| Windows App Runtime 1.7 | `https://aka.ms/windowsappsdk/1.7/latest/windowsappruntimeinstall-x64.exe` | `--quiet` |

`DownloadPage.Download` runs the built-in downloader (with progress bar + cancel). Each
installer is then executed via `Exec`; exit codes `0`, `1641`, `3010` are accepted, anything
else aborts setup with a descriptive `MsgBox`.

If both prerequisites are already present, the user never sees the download page.

---

## Uninstall behavior

### What's always removed

- All files in `{app}\` (whole install dir)
- All registry keys we created (`uninsdeletekey` / `uninsdeletevalue` flags)
- Start Menu and Desktop shortcuts

### What we **ask** about

After files/registry are gone (`CurUninstallStep = usPostUninstall`), we check if
`%LOCALAPPDATA%\BrowserMux\` exists and prompt:

> Also remove your BrowserMux settings, rules and logs?
>
> Location: C:\Users\<you>\AppData\Local\BrowserMux
>
> Choose No to keep them for a future reinstall.

- **Default = No** (`MB_DEFBUTTON2`) — accidentally clicking the wrong button doesn't
  destroy data
- Yes → `DelTree` removes the entire folder (preferences.json, rules.json, logs/)

### Why a prompt and not automatic?

- Most apps keep user config across reinstalls — that's the standard expectation
- Cleaning up requires explicit consent so reinstalls don't lose carefully tuned rules
- Power users who want a clean uninstall get that option without having to manually delete

---

## License

**MIT License** — see `LICENSE` at repo root.

- Maximum permissive (use, modify, sell, sublicense)
- `AS IS` clause disclaims all liability and warranties
- Compatible with everything
- Recognized automatically by GitHub

The installer shows the LICENSE in the wizard (`LicenseFile=..\LICENSE`) and the user
must accept it to proceed. Standard "I accept the agreement / I do not accept" radio.

---

## Setup icon

The installer `.exe` itself uses `src/BrowserMux.App/Assets/AppIcon.ico`
(the `>M` JetBrains Mono Bold logo, 16/24/32/48/64/128/256 multi-resolution).

```ini
SetupIconFile=..\src\BrowserMux.App\Assets\AppIcon.ico
```

The same icon is reused for `UninstallDisplayIcon`, the registered browser icon
(`Capabilities\ApplicationIcon`), and the ProgId icon — single source of truth.

---

## File: `installer/setup.iss` reference

### Top constants

```ini
#define AppName      "BrowserMux"
#define AppVersion   "0.1.0"
#define AppPublisher "BrowserMux"
#define AppURL       "https://browsermux.com"
#define AppExeName   "BrowserMux.exe"
#define HandlerExe   "BrowserMux.Handler.exe"
#define ProgId       "BrowserMuxURL"
```

`AppId={B8A2F3E1-7C4D-4A5B-9E6F-1D2C3B4A5E6F}` — fixed GUID, **never change** (it's how
Windows identifies the app for upgrade vs fresh install).

### Override version on the CLI

```powershell
& iscc.exe /DAppVersion=0.2.0 installer\setup.iss
```

Any `#define` can be overridden with `/D<NAME>=<value>`.

---

## iscc.exe — useful flags

`iscc.exe` is mostly configured via the `.iss` file itself; CLI flags are minimal:

| Flag | Effect |
|---|---|
| `iscc setup.iss` | Compile (normal) |
| `/Q` | Quiet — no output unless error |
| `/Qp` | Quiet + progress bar |
| `/O<dir>` | Override `OutputDir` |
| `/F<name>` | Override `OutputBaseFilename` |
| `/D<NAME>=<value>` | Override a `#define` |
| `/S<name>=<cmd>` | Define a SignTool (for Authenticode signing) |

Everything else (license file, banner images, language, custom messages, install logic)
lives in the `.iss`.

---

## What's NOT yet configured

These could be added later if needed:

- **Code signing** — installer is unsigned, so SmartScreen will warn the first time
  - To sign: configure a `SignTool` in `[Setup]` and use `/S` on the iscc CLI with your
    cert (DigiCert, Sectigo, etc.). Free option: self-signed (warns harder) or
    `azuresigntool` if you have an Azure Key Vault cert.
- **Multi-language** — only English is shipped (`compiler:Default.isl`).
  - Add e.g. `Name: "french"; MessagesFile: "compiler:Languages\French.isl"` to `[Languages]`
- **Wizard branding images** — no custom `WizardImageFile` / `WizardSmallImageFile`
  - These are the side banner and small bitmap in the corner of the wizard
  - BMP only, sized 164×314 and 55×58 respectively
- **Auto-update** — not implemented; would need a release server + an in-app updater that
  downloads and runs the new installer silently. Out of scope for now.
- **`InfoBeforeFile` / `InfoAfterFile`** — extra readme pages in the wizard. Skipped to keep
  the wizard short; the LICENSE page is enough.

---

## Troubleshooting

### "Inno Setup 6 not found" from `build-installer.ps1`

Install it: `winget install -e --id JRSoftware.InnoSetup`

It goes to `%LOCALAPPDATA%\Programs\Inno Setup 6\` (per-user winget install). The script
also checks `Program Files (x86)` and `Program Files` for system-wide installs.

### "Type mismatch" compile error in `[Code]`

Inno's Pascal is strict. Common mistakes:
- `ShellExec(..., Result)` — `Result` is `Boolean`, but `ShellExec`'s last parameter is
  `var ErrorCode: Integer`. Use a local `var ErrCode: Integer` instead.
- Forgetting that `MsgBox` returns the button constant (`IDYES`, `IDNO`...), not a Boolean.

### Installer compiles but the app doesn't get registered as a browser

- Check that the user picked "All users" + admin OR per-user mode succeeded
- Verify `HKCU\Software\Clients\StartMenuInternet\BrowserMux` (per-user) or
  `HKLM\...` (machine) exists after install
- Open Settings → Default Apps and look for BrowserMux in the protocol list — if it doesn't
  appear, the `RegisteredApplications` entry didn't write
- Make sure `out\` was actually populated (re-run `build.ps1 -Config Release`)

### .NET 9 detection false negative

The `IsDotNet9Installed()` check is best-effort. If a user already has .NET 9 but the check
fails, they'll see the prompt. Worst case: they cancel, click Yes, get redirected to a page
they don't need, install nothing, and re-run. Not destructive, just annoying. Could be
improved later by parsing `dotnet --list-runtimes` output instead of just checking exit code.

---

## File map (everything related to the installer)

| File | Role |
|---|---|
| `installer/setup.iss` | The Inno Setup script (single source of truth) |
| `LICENSE` | MIT license shown in the wizard |
| `build.ps1` | Builds the app (Debug/Release), deploys to `out/` |
| `build-installer.ps1` | One-shot: `build.ps1 -Config Release` + iscc + report |
| `out/` | Build output staged for the installer (filled by `build.ps1`) |
| `dist/` | Compiled installer `.exe` (gitignored) |
| `src/BrowserMux.App/Assets/AppIcon.ico` | Icon used by setup, the app, the handler, and the ProgId |
| `src/BrowserMux.Core/AppInfo.cs` | Centralized app name + paths (must stay in sync with `setup.iss` `#define`s) |
| `register.ps1` | Dev-only equivalent: registers BrowserMux in HKCU without running the installer |
