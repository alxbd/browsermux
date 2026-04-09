# BrowserMux Auto-Update

BrowserMux ships with an **in-app self-updater** that polls GitHub releases, downloads
the latest Inno installer, and reinstalls itself silently. No external updater service,
no MSIX, no winget dependency — just a plain HTTPS call to the GitHub REST API and a
silent re-run of the regular setup `.exe`.

Output of an update cycle: the same `BrowserMux-Setup-<version>.exe` already published
for manual downloads. The updater is purely a *consumer* of the existing release artifact.

---

## Quick overview

```
┌────────────────┐  GET /releases/latest    ┌──────────────┐
│  BrowserMux    │ ───────────────────────> │  GitHub API  │
│  (Settings)    │ <─────────────────────── │              │
└───────┬────────┘    JSON: tag, assets     └──────────────┘
        │ download asset
        v
   %TEMP%\BrowserMux-Setup-latest.exe
        │ /SILENT /SUPPRESSMSGBOXES /NORESTART /RELAUNCH
        v
   Inno installer ──> kills running app ──> copies files ──> relaunches BrowserMux
```

---

## Where it lives

| File | Role |
|---|---|
| `src/BrowserMux.App/Services/UpdateChecker.cs` | HTTP client, GitHub API parsing, version compare, download with progress, cooldown persistence |
| `src/BrowserMux.App/Windows/SettingsWindow.xaml` (`UpdateCard`) | UI — status text, progress bar, "Release notes" link, "Download & install" button, "Check now" button |
| `src/BrowserMux.App/Windows/SettingsWindow.xaml.cs` (`CheckForUpdatesAsync`, `UpdateButton_Click`) | Wires the UI to `UpdateChecker` and launches the installer |
| `src/BrowserMux.App/App.xaml.cs` (`CheckForUpdatesOnStartupAsync`) | Fires a background check 5s after launch (silent — only refreshes the cooldown) |
| `src/BrowserMux.Core/AppInfo.cs` (`GitHubRepo`, `AppVersion`) | Single source of truth for the repo slug and the running version |
| `installer/setup.iss` (`WantsSilentRelaunch`, `[Run]` relaunch entry) | Detects the `/RELAUNCH` switch and restarts BrowserMux post-install |

---

## API call

The updater hits the public GitHub REST endpoint — no token required, anonymous rate
limit (60 req/h/IP) is plenty given the 6-hour cooldown.

```
GET https://api.github.com/repos/{GitHubRepo}/releases/latest
Accept:     application/vnd.github+json
User-Agent: BrowserMux/<version>
```

Parsed fields (source-generated `JsonSerializerContext` to stay AOT-friendly):

| JSON field | Use |
|---|---|
| `tag_name` | Stripped of leading `v`, parsed via `System.Version`, compared against `AppInfo.AppVersion` |
| `html_url` | Surfaced as the "Release notes" hyperlink in the UI |
| `assets[].name` | Matched against `*Setup*.exe` (case-insensitive) — first hit wins |
| `assets[].browser_download_url` | Direct download URL for the matched asset |

If the API call fails (network, 5xx, rate limit), `CheckAsync` returns `null`. The UI
shows "You're up to date" — by design we don't surface transient failures to users; the
next check will retry.

---

## Cooldown

To avoid hammering GitHub, checks honor a **6-hour cooldown** persisted in
`%LOCALAPPDATA%\BrowserMux\preferences.json` under `Settings.LastUpdateCheck` (ISO-8601
UTC). Both auto-checks (startup, Settings window open) respect it; the **"Check now"**
button passes `force: true` and bypasses it.

```jsonc
// preferences.json
{
  "Settings": {
    "LastUpdateCheck": "2026-04-08T14:32:11.4521930Z",
    // ...
  }
}
```

---

## When checks happen

| Trigger | Forced? | Notes |
|---|---|---|
| 5 seconds after app launch | No | Background, no UI feedback. Just refreshes the cooldown so the Settings window opens with fresh data. |
| Settings window opens (first `Activated` event) | No | Hits the cooldown if checked recently. Updates the `UpdateCard` status text. |
| User clicks "Check now" | **Yes** | Bypasses the cooldown. Always hits GitHub. |

---

## Download & install

When the user clicks **"Download & install"**:

1. `UpdateChecker.DownloadInstallerAsync` streams the asset to
   `%TEMP%\BrowserMux-Setup-latest.exe` with chunked reads (81 920 B buffers) and reports
   progress to a `Progress<double>` bound to the `ProgressBar` in the UI.
2. On success, the installer is launched via `Process.Start` with these arguments:
   ```
   /SILENT /SUPPRESSMSGBOXES /NORESTART /RELAUNCH
   ```
   - `/SILENT` — small Inno progress window, no wizard pages, no Finish button
   - `/SUPPRESSMSGBOXES` — accept default answers for any prompt the script doesn't handle
   - `/NORESTART` — never trigger a Windows reboot from the installer
   - `/RELAUNCH` — **custom switch** consumed by `setup.iss` (see below)
3. `CloseApplications=yes` in `[Setup]` makes Inno SendMessage-WM_CLOSE the running
   BrowserMux instance before copying files, so file locks never block the update.

### The `/RELAUNCH` switch

`setup.iss` defines a Pascal helper that scans command-line parameters for `/RELAUNCH`:

```pascal
function WantsSilentRelaunch(): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), '/RELAUNCH') = 0 then
    begin
      Result := True;
      Exit;
    end;
end;
```

It gates a `[Run]` entry that restarts BrowserMux at the end of install:

```
Filename: "{app}\{#AppExeName}"; Flags: nowait runasoriginaluser; Check: WantsSilentRelaunch
```

Notes:
- No `postinstall` flag → the entry runs even in silent mode (entries with `postinstall`
  are skipped under `/SILENT`).
- `runasoriginaluser` → if Inno is elevated, the relaunch drops back to the original
  user's token so BrowserMux runs unprivileged like a normal app.
- Interactive installs (no `/RELAUNCH`) keep the existing user-toggled "Open Default
  Apps settings" entry and skip the relaunch entirely.

---

## Asset naming convention

The matcher in `UpdateChecker.CheckAsync` looks for an asset whose name **contains**
`Setup` (case-insensitive) and **ends with** `.exe`. This matches the default
`OutputBaseFilename` in `setup.iss`:

```
OutputBaseFilename=BrowserMux-Setup-{#AppVersion}
→ BrowserMux-Setup-1.0.0.exe   ✅ matched
```

If you ever publish multiple assets per release (e.g. a portable zip alongside the
installer), keep the installer name pattern stable or the updater will pick the wrong
asset.

---

## Release checklist

For the auto-updater to find a new version, each release must:

1. **Bump the version** in `Directory.Build.props` (`<Version>`) — `AppInfo.AppVersion`
   reads it from `AssemblyInformationalVersion`.
2. **Bump `AppVersion`** in `installer/setup.iss` to match. (See `docs/versionning.md`
   for the broader version strategy.)
3. **Build the installer**: `pwsh build-installer.ps1`.
4. **Create a GitHub release**:
   - Tag: `v<version>` (e.g. `v1.0.0`) — the leading `v` is stripped by `TrimStart('v')`.
   - Upload `dist/BrowserMux-Setup-<version>.exe` as a release asset.
   - Mark as **Latest release** (the API endpoint `/releases/latest` returns the most
     recent non-prerelease, non-draft release flagged as latest).
5. Within 6 hours, every running BrowserMux instance will pick it up on its next
   periodic check; users who open Settings will see it immediately.

---

## Permissions & UAC

The installer requires `PrivilegesRequired=lowest` with
`PrivilegesRequiredOverridesAllowed=dialog` (already set in `[Setup]`). On a normal
per-user install (default to `{autopf}` which resolves to `%LOCALAPPDATA%\Programs` for
non-admin users), no UAC prompt appears during self-update — the silent install runs
unattended.

If the original install was elevated to `C:\Program Files\BrowserMux`, Windows will
trigger a UAC prompt when the silent updater tries to write there. There is no clean way
to suppress that without a privileged service. **Recommendation**: install per-user.

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| "You're up to date" but a new release exists | Cooldown not expired (within 6h of last check) | Click **"Check now"** to force |
| Update found but download fails silently | Asset name doesn't match `*Setup*.exe` pattern | Rename the release asset on GitHub |
| Installer launches but wizard appears instead of silent | Inno < 6 doesn't support `/SILENT` exactly the same way; or another `.exe` got picked | Verify `setup.iss` is compiled with Inno Setup 6+ |
| BrowserMux doesn't relaunch after update | `/RELAUNCH` switch missing, or `WantsSilentRelaunch` not in `[Code]` | Check `setup.iss` matches the spec above; recompile |
| Self-update prompts for UAC every time | App was installed to `Program Files` (admin) | Reinstall per-user, or accept the prompt |

Logs are written to `%LOCALAPPDATA%\BrowserMux\logs\app.log`. The updater logs failures
under the `[UpdateChecker]` prefix (check + download paths both log on exception).
