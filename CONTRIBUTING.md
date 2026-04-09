# Contributing to BrowserMux

Thanks for your interest — PRs and issues are welcome.

## Dev setup

**Prerequisites**

- Windows 11
- Visual Studio 2026 with the **Windows App SDK C# workload**
- **.NET 9 SDK**
- [Inno Setup 6](https://jrsoftware.org/isdl.php) — only to build the installer locally

**Clone and build**

```powershell
git clone https://github.com/browsermux/browsermux.git
cd browsermux
pwsh build.ps1 -Run
```

`build.ps1` always goes through VS 2026's MSBuild because `dotnet build` does **not** correctly rebuild the referenced `BrowserMux.Core` project — stale DLLs silently mask changes. Always use `build.ps1`.

**Useful flags**

| Command | What it does |
|---|---|
| `pwsh build.ps1` | Build only |
| `pwsh build.ps1 -Run` | Build + launch the app |
| `pwsh build.ps1 -Clean -Run` | Clean (`bin/`, `obj/`) + rebuild + launch |
| `pwsh build.ps1 -Run -Url "https://example.com"` | Launch the picker on a URL |

**Register as default browser (dev)**

```powershell
pwsh register.ps1
```

Writes `HKCU` entries so dev builds can be set as the default browser without admin rights. Uninstall with `pwsh register.ps1 -Unregister`.

## Runtime data

| File | Purpose |
|---|---|
| `%LOCALAPPDATA%\BrowserMux\preferences.json` | App settings, pinned / hidden / custom browsers |
| `%LOCALAPPDATA%\BrowserMux\rules.json` | Routing rules |
| `%LOCALAPPDATA%\BrowserMux\logs\app.log` | Rolling log (500 lines) |
| `%TEMP%\BrowserMux_crash.txt` | Unhandled exception dump |

Tail the log live:

```powershell
Get-Content "$env:LOCALAPPDATA\BrowserMux\logs\app.log" -Wait -Tail 30
```

## Project layout

```
BrowserMux.Handler/   — AOT exe, receives URLs, forwards via named pipe
BrowserMux.App/       — WinUI 3 app (MVVM via CommunityToolkit.Mvvm)
BrowserMux.Core/      — Models + pure services (no UI deps)
installer/setup.iss   — Inno Setup 6 script
```

See [CLAUDE.md](CLAUDE.md) for the full architecture reference.

## Code style

- **English** for all code, comments, and docs — even in PR descriptions.
- Follow the existing style (no new formatters or analyzers added in a PR).
- Keep `BrowserMux.Core` free of UI dependencies (no `Microsoft.UI.*` types).
- Prefer `record` for immutable models.
- MVVM: use `[ObservableProperty]` and `[RelayCommand]` from `CommunityToolkit.Mvvm`.
- No `async void` except for XAML event handlers.

## Pull requests

1. Open an issue first for anything non-trivial so we can agree on the approach.
2. One feature / fix per PR. Keep the diff focused.
3. Test manually — there's no automated UI test suite. At minimum:
   - `pwsh build.ps1 -Clean -Run`
   - Click a link (or use `-Url`), verify picker renders and launches correctly.
   - Open Settings, round-trip through tabs.
4. Describe **what** changed and **why** in the PR body.

## Reporting bugs

Open an [issue](https://github.com/browsermux/browsermux/issues) with:

- OS version
- BrowserMux version (Settings → About)
- Steps to reproduce
- Contents of `app.log` (scrub anything personal)
- `BrowserMux_crash.txt` if the app crashed

## Releasing (maintainers)

1. Bump `Version` in `Directory.Build.props` and `AppVersion` in `installer/setup.iss`.
2. Commit: `chore: bump version to x.y.z`.
3. Tag: `git tag vx.y.z && git push origin vx.y.z`.
4. `.github/workflows/release.yml` builds the installer + portable zip and publishes to Releases.
