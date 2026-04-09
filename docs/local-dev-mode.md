# Local Dev Mode (dual channel)

BrowserMux can be installed from the official `.exe` **and** developed locally on the
same machine without conflict. Debug builds use a separate identity (`BrowserMux Dev`)
with their own preferences folder, registry entries, IPC pipe, and tray entry.

This means you can:

- Install the latest released `BrowserMux-Setup-x.y.z.exe` and use it as your real default
  browser
- At the same time run `pwsh build.ps1 -Run` to test your in-progress changes
- Both apps run side-by-side, never overwrite each other's settings, and never fight over
  the same single-instance mutex or named pipe

---

## How it works

The channel is determined at compile time by the standard `DEBUG` constant.

| Build | Channel | Identity | When you get this |
|---|---|---|---|
| Debug (default) | **Dev** | suffixed with `Dev` | `pwsh build.ps1`, `pwsh build.ps1 -Run`, F5 from VS |
| Release | **Prod** | clean `BrowserMux` | `pwsh build.ps1 -Config Release`, `build-installer.ps1`, the installer |

The single source of truth is `src/BrowserMux.Core/AppInfo.cs`, which uses
`#if DEBUG / #else / #endif` to switch every identifier:

```csharp
#if DEBUG
    public const string AppName             = "BrowserMux Dev";
    public const string LocalAppDataFolder  = "BrowserMux Dev";
    public const string RoamingAppDataFolder= "BrowserMux Dev";
    public const string ProgId              = "BrowserMuxDevURL";
    public const string PipeName            = "BrowserMuxDevPipe";
    public const string MutexName           = "BrowserMux_Dev_SingleInstance";
    public const string CrashFileName       = "BrowserMux_Dev_crash.txt";
#else
    public const string AppName             = "BrowserMux";
    public const string LocalAppDataFolder  = "BrowserMux";
    public const string RoamingAppDataFolder= "BrowserMux";
    public const string ProgId              = "BrowserMuxURL";
    public const string PipeName            = "BrowserMuxPipe";
    public const string MutexName           = "BrowserMux_SingleInstance";
    public const string CrashFileName       = "BrowserMux_crash.txt";
#endif
```

Everything downstream — `LogPath`, `PreferencesPath`, `RulesPath`, etc. — is built from
those constants via `Path.Combine` and inherits the right folder automatically.

### Exe metadata (FileDescription / ProductName)

Windows 11's **Settings → Default apps** page reads the entry title from the exe's
`FileDescription` field, **not** from the `Capabilities\ApplicationName` registry value.
So having `AppName = "BrowserMux Dev"` in `AppInfo.cs` isn't enough — without matching
exe metadata, both channels would still appear as "BrowserMux" in that screen.

To fix this, `Directory.Build.props` injects a Configuration-conditional `Product` /
`AssemblyTitle` so the Debug build's exe is stamped with `BrowserMux Dev`:

```xml
<PropertyGroup>
  <Product Condition="'$(Configuration)' == 'Debug'">BrowserMux Dev</Product>
  <Product Condition="'$(Configuration)' != 'Debug'">BrowserMux</Product>
  <AssemblyTitle>$(Product)</AssemblyTitle>
  <Company>BrowserMux</Company>
</PropertyGroup>
```

This applies to every project in the solution (App, Handler, Core) since
`Directory.Build.props` is inherited. Verify with:

```powershell
(Get-Item "src\BrowserMux.App\bin\x64\Debug\net9.0-windows10.0.22621.0\BrowserMux.exe").VersionInfo |
    Format-List FileDescription, ProductName
```

Note: Windows caches these metadata values; if the Default Apps screen still shows the
old name after a rebuild, close and reopen the Settings app (or sign out/in).

### What is **not** suffixed

| | Why |
|---|---|
| `AppExeName` (`BrowserMux.exe`) | The disk filename is driven by the csproj `<AssemblyName>`. Renaming it would cascade through 20+ files (registry paths, install scripts, doc references). The display name is enough to disambiguate. |
| `GitHubRepo` | Same upstream — both channels look at the same release feed |
| `PreferencesSchemaVersion`, `RulesSchemaVersion` | Same JSON shape — only the location differs |

So on disk you'll have **two `BrowserMux.exe` binaries** at different paths, with different
display names in their version info but the same filename. That's by design.

---

## What lives where

### Dev channel (`pwsh build.ps1`)

| Element | Path / value |
|---|---|
| Binary | `src/BrowserMux.App/bin/x64/Debug/net9.0-windows10.0.22621.0/BrowserMux.exe` |
| Handler | `src/BrowserMux.Handler/bin/x64/Debug/net9.0-windows10.0.22621.0/BrowserMux.Handler.exe` |
| Settings | `%LOCALAPPDATA%\BrowserMux Dev\preferences.json` |
| Rules | `%LOCALAPPDATA%\BrowserMux Dev\rules.json` |
| Logs | `%LOCALAPPDATA%\BrowserMux Dev\logs\app.log` |
| Crash dump | `%TEMP%\BrowserMux_Dev_crash.txt` |
| IPC pipe | `\\.\pipe\BrowserMuxDevPipe` |
| Mutex | `BrowserMux_Dev_SingleInstance` |
| ProgId | `BrowserMuxDevURL` |
| Display name | `BrowserMux Dev` |
| Registry root (via `register.ps1`) | `HKCU\Software\Classes\BrowserMuxDevURL` and `HKCU\Software\Clients\StartMenuInternet\BrowserMux Dev` |

### Prod channel (installed via `BrowserMux-Setup-x.y.z.exe`)

| Element | Path / value |
|---|---|
| Binary | `C:\Program Files\BrowserMux\BrowserMux.exe` (or `%LOCALAPPDATA%\Programs\BrowserMux\` in per-user mode) |
| Handler | `…\BrowserMux\BrowserMux.Handler.exe` |
| Settings | `%LOCALAPPDATA%\BrowserMux\preferences.json` |
| Rules | `%LOCALAPPDATA%\BrowserMux\rules.json` |
| Logs | `%LOCALAPPDATA%\BrowserMux\logs\app.log` |
| Crash dump | `%TEMP%\BrowserMux_crash.txt` |
| IPC pipe | `\\.\pipe\BrowserMuxPipe` |
| Mutex | `BrowserMux_SingleInstance` |
| ProgId | `BrowserMuxURL` |
| Display name | `BrowserMux` |
| Registry root | `HKLM`/`HKCU` (depending on hybrid install mode — see [installer-inno.md](installer-inno.md)) `\Software\Classes\BrowserMuxURL` etc. |

---

## Day-to-day workflow

### Just iterating on code
```powershell
pwsh build.ps1 -Run
```
This builds Debug → Dev channel. Your dev tray icon shows up alongside the installed
prod tray icon. The two apps don't see each other.

### Making the dev build the actual default browser
By default the prod install is your real default browser; the dev build is just an extra
process you can launch manually. If you want the **dev build** to intercept all clicks,
register it in HKCU:

```powershell
pwsh register.ps1
```

This writes:
- `HKCU\Software\Classes\BrowserMuxDevURL`
- `HKCU\Software\Clients\StartMenuInternet\BrowserMux Dev`
- `HKCU\Software\RegisteredApplications\BrowserMux Dev`

…then opens **Settings → Default Apps** so you can pick "BrowserMux Dev" as your default
web browser. To go back to the released version, just pick "BrowserMux" again from the same
list.

`register.ps1` is HKCU-only (no admin needed) and only sets up the dev channel — it never
touches the prod channel's keys.

### Building the installer
```powershell
pwsh build-installer.ps1
```
This compiles in **Release** → Prod channel. The resulting `dist/BrowserMux-Setup-*.exe`
contains a binary with the clean `BrowserMux` identity.

---

## How both apps coexist

### IPC pipe
Each channel has its own pipe (`BrowserMuxPipe` vs `BrowserMuxDevPipe`). When the prod
handler receives a URL it forwards it to `BrowserMuxPipe`, where only the prod app is
listening. The dev handler talks to the dev app via `BrowserMuxDevPipe`. They never collide.

### Single-instance mutex
Each channel has its own mutex name. The prod app and dev app can both be running
simultaneously because they hold different mutexes. Only one instance per channel.

### Tray icons
Both apps add their own tray icon. Visually they look the same (same `>M` icon) — you
can distinguish them by the tooltip: hovering shows "BrowserMux" or "BrowserMux Dev".

### Settings → Default Apps
You'll see two entries in Windows: **BrowserMux** and **BrowserMux Dev**. Each has its
own URL associations. Whichever is selected as the default web browser is the one that
intercepts clicks system-wide.

### Preferences and rules
Completely separate. Tweaking a rule in the dev build doesn't affect the prod install
and vice versa. If you want them to share state, you can either copy the JSON files
manually or symlink one folder to the other (advanced — at your own risk).

---

## Migrating existing dev data

If you had a Dev build running **before** this dual-channel split, your settings live in
`%LOCALAPPDATA%\BrowserMux\` (the old shared location). After this change, your dev build
will look at `%LOCALAPPDATA%\BrowserMux Dev\` instead and start fresh.

Two options:

**Option A — start fresh** (recommended)
Just let the dev build create a new empty config. Easiest.

**Option B — copy the old data**
```powershell
Copy-Item -Recurse "$env:LOCALAPPDATA\BrowserMux" "$env:LOCALAPPDATA\BrowserMux Dev"
```
Run this **before** launching the new dev build for the first time. Now your dev build
inherits all your old prefs/rules. The old `BrowserMux\` folder is left untouched (it
becomes the "real" prod install's data once you install it).

---

## Troubleshooting

### "I see two BrowserMux entries in tray, but they look identical"
That's expected. Same icon, different processes. The tooltip distinguishes them
("BrowserMux" vs "BrowserMux Dev"). You can also check Task Manager — the binaries are
at different paths.

### "Clicking a link doesn't open my dev build"
Whichever channel is your **default browser** in Windows is the one that gets links.
Run `pwsh register.ps1` and pick "BrowserMux Dev" in Settings → Default Apps to make the
dev build the default.

### "I built Release locally and now my prod install behaves weird"
`pwsh build.ps1 -Config Release -Run` produces a Prod-channel binary that points at the
**same** `%LOCALAPPDATA%\BrowserMux\` folder as your installed app. Running it manually
won't break the install but **it might race the installed app for the single-instance
mutex** and cause one of them to immediately exit. Either:
- Don't use `-Config Release` outside of `build-installer.ps1`, or
- Stop the installed app from the tray before running a local Release build

### "Dev settings are gone after rebuilding"
You probably had data in the old `%LOCALAPPDATA%\BrowserMux\` location. See
[Migrating existing dev data](#migrating-existing-dev-data).

### "I want to wipe just the dev channel data"
```powershell
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\BrowserMux Dev"
```
Safe — touches only the dev folder. Your installed prod data in
`%LOCALAPPDATA%\BrowserMux\` is untouched.

### "I want to unregister the dev build from Default Apps"
```powershell
Remove-Item -Recurse -Force "HKCU:\Software\Classes\BrowserMuxDevURL" -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force "HKCU:\Software\Clients\StartMenuInternet\BrowserMux Dev" -ErrorAction SilentlyContinue
Remove-ItemProperty -Path "HKCU:\Software\RegisteredApplications" -Name "BrowserMux Dev" -ErrorAction SilentlyContinue
```

---

## File map

| File | Role in dual-channel |
|---|---|
| `src/BrowserMux.Core/AppInfo.cs` | Source of truth — `#if DEBUG` branches between Dev and Prod identities |
| `register.ps1` | Dev-only — registers `BrowserMux Dev` in HKCU so it can be picked as default |
| `installer/setup.iss` | Prod-only — Inno Setup script. Built in Release → uses prod identifiers automatically |
| `build.ps1` | Builds Debug or Release; the channel falls out of the config |
| `build-installer.ps1` | Wraps `build.ps1 -Config Release` + Inno Setup → always Prod |
| `docs/installer-inno.md` | Installer reference (sibling document) |
