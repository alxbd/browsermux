---
name: x-build
description: Build and run BrowserMux. Use this skill whenever you need to compile, rebuild, clean, or launch the app — it enforces the project's build rules and avoids the stale-binary trap that `dotnet build` causes.
---

# x-build — BrowserMux build & run

The ONLY reliable build entry point is `build.ps1`. `dotnet build` produces silently stale
binaries because it does not force rebuild of referenced projects (`BrowserMux.Core`
changes are ignored, and the old DLL is reused).

## Commands

Run from repo root.

| Goal | Command |
|---|---|
| Build only | `pwsh build.ps1` |
| Build + run | `pwsh build.ps1 -Run` |
| Build + run with URL | `pwsh build.ps1 -Run -Url "https://github.com"` |
| Full clean rebuild + run | `pwsh build.ps1 -Clean -Run` |
| Release build (installer) | `pwsh build.ps1 -Config Release` |
| Build + compile installer | `pwsh build-installer.ps1` |

`build.ps1` auto-handles: kill running instance → MSBuild via VS 2026 → optional launch.

## Never use

```bash
dotnet build BrowserMux.sln --no-incremental          # Core changes silently ignored
dotnet build src/BrowserMux.App/... --no-incremental  # same bug
```

## Known build issues

**`Pri.Tasks.dll` not found** — `Directory.Build.props` redirects `MSBuildExtensionsPath`
to VS 2026 Insiders. If VS is reinstalled elsewhere, update `_VsFallbackDir` in
`Directory.Build.props`.

**`SubtleButtonStyle` not found at runtime** — this style does not exist in unpackaged
WinUI 3 apps (no MSIX). Never use `{StaticResource SubtleButtonStyle}` in XAML.

**Unpackaged-safe `ThemeResource` brushes**
- `TextFillColorSecondaryBrush` — OK
- `SystemControlForegroundChromeGrayBrush` — use instead of `DividerStrokeColorDefaultBrush`
- `SubtleButtonStyle` — NOT available

## Local dev helpers (Debug build only)

The installer is the normal way to register BrowserMux as a browser and create the Start
Menu shortcut. For a local Debug build (`BrowserMux Dev` channel) you don't run the
installer — use these helper scripts instead. Both target the Debug output paths and the
`BrowserMux Dev` identifiers.

| Script | Purpose |
|---|---|
| `manually-register-browser.ps1` | Writes HKCU ProgId + Capabilities + RegisteredApplications for the Debug build, then opens `ms-settings:defaultapps` so you can pick it. |
| `manually-create-start-menu-shortcut.ps1` | Creates a `BrowserMux Dev.lnk` in the user Start Menu pointing at the Debug `BrowserMux.exe`. |

Run after a successful `pwsh build.ps1`. Safe to re-run (idempotent). Not needed for
Release / installer builds.

## Logs & crash (quick pointers)

- App log: `tail -f "$LOCALAPPDATA/BrowserMux/logs/app.log"` (written by `AppLogger`, rotated at 500 lines)
- Unhandled crash: `cat "$TEMP/BrowserMux_crash.txt"` (from `App.xaml.cs` UnhandledException)
