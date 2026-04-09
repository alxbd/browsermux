# BrowserMux — CLAUDE.md

## General rules

- Whichever the language used in the prompt, always output code, comments and md files in English.

**Website**: browsermux.com

Modern Windows browser selector. Replaces Hurl and BrowserSelect (abandoned).
Intercepts system link clicks and shows a picker to choose which browser (or profile) to open.

---

## Documentation & offloaded knowledge

Most reference material lives outside this file so it can be loaded on demand.

### `docs/` — long-form reference

Terse AI-readable docs. **Discover before reading source** — list `docs/` first, filenames
are self-describing:

```bash
ls docs/
```

Consult as much as needed for non-trivial tasks. Covers installer, auto-updater, dev/prod
dual-channel build, versioning, and the full feature inventory.

When implementing a new feature, changing documented behavior, or on request, invoke the
**`x-docs-writer`** skill — it defines doc types, conventions, and keeps `docs/features.md`
in sync with subsystem references.

### Project skills (`.claude/skills/x-*`)

Project-local skills are **prefixed with `x-`** to distinguish them from global skills.

| Skill | Trigger |
|---|---|
| `x-build` | Any build / run / rebuild / installer compile. Enforces `build.ps1` and contains known build issues. |
| `x-win10-compat` | Window chrome, backdrops, icon fonts, glyphs, WinAppSDK version — Win10 1809+ rules. |
| `x-docs-writer` | Writing / updating files under `docs/`. |
| `x-release` | Cutting a new release, bumping version, pushing a tag. Enforces tag-driven release flow. |

When creating a new skill: folder `.claude/skills/x-<name>/`, `name: x-<name>` in frontmatter.

---

## App name

Display name, identifiers (pipe, mutex, ProgId, folders), and exe names are centralized in
**`src/BrowserMux.Core/AppInfo.cs`**. To rename the app, modify that file + the `$appName`
variables in `build.ps1` and `register.ps1` (see "keep in sync" comment at the top of each).

---

## Tech stack

| Component | Choice | Reason |
|---|---|---|
| UI | **WinUI 3** (Windows App SDK 1.7) | Fluent Design, Mica, native Windows 11 |
| Language | **C# / .NET 9** | Registry, Windows APIs, WinUI 3 |
| Handler exe | **C# AOT** (`PublishAot=true`) | Startup < 50ms, no runtime required |
| IPC | **Named Pipe** `\\.\pipe\BrowserMuxPipe` | Single-instance, standard Windows |
| Config | **JSON** in `%LOCALAPPDATA%\BrowserMux\` | Human-readable, versionable |
| MVVM | **CommunityToolkit.Mvvm** | `[ObservableProperty]`, `[RelayCommand]` |
| DI | **Microsoft.Extensions.Hosting** | Clean services, testable |
| Installer | **Inno Setup 6** | HKLM / HKCU hybrid, see `docs/installer-inno.md` |

---

## Solution architecture

```
BrowserMux.sln
├── BrowserMux.Handler/           # C# AOT — exe registered as default browser
│   └── Program.cs                  # Receives URL → named pipe → launches App if needed
│
├── BrowserMux.App/               # Main WinUI 3 application (MVVM)
│   ├── App.xaml.cs                 # Entry point, single-instance mutex, named pipe listener,
│   │                               #   tray icon (H.NotifyIcon), launcher hotkey setup
│   ├── Windows/
│   │   ├── PickerWindow.xaml[.cs]  # View — window chrome, animations, DPI positioning
│   │   └── SettingsWindow.xaml[.cs]# View — theme, navigation, hotkey capture
│   ├── ViewModels/
│   │   ├── PickerViewModel.cs      # Picker state + logic (show, launch, rules, modifiers)
│   │   ├── SettingsViewModel.cs    # Settings state + logic (save, rules, browsers, test)
│   │   ├── BrowserCardViewModel.cs # Single browser card
│   │   ├── BrowserItemViewModel.cs # Browser row in Settings > Browsers
│   │   ├── RuleItemViewModel.cs    # Rule row in Settings > Rules
│   │   └── BrowserOption.cs        # Lightweight id+name for dropdowns
│   ├── Controls/
│   │   └── BrowserCard.xaml[.cs]   # Browser/profile card (UI + launch logic)
│   └── Services/
│       ├── GlobalHotkeyService.cs  # Win32 RegisterHotKey on dedicated STA thread
│       ├── IconExtractor.cs        # Shell32 icon extraction with cache
│       ├── CursorHelper.cs         # DPI-aware cursor position (P/Invoke)
│       ├── SystemBackdropHelper.cs # Mica with Win10 fallback — use this, never raw XAML Mica
│       ├── UpdateChecker.cs        # Auto-updater — see docs/auto-update.md
│       └── WindowExtensions.cs     # HWND helpers
│
├── BrowserMux.Core/              # Shared library (no UI dependency)
│   ├── AppInfo.cs                  # Centralized app name, paths, pipe/mutex names
│   ├── Models/                     # Browser, BrowserProfile, UserPreferences, AppSettings, Rule
│   └── Services/
│       ├── BrowserDetector.cs      # Registry scan + Chromium profile discovery
│       ├── RuleEngine.cs           # Domain / Regex / Glob matching (compiled regex cache)
│       ├── PreferencesService.cs   # Singleton — loads/saves preferences.json + rules.json
│       ├── RegistrySetup.cs        # Checks ProgId, Capabilities, IsDefaultBrowser
│       └── AppLogger.cs            # File logger — see docs/logger.md
│
└── installer/
    └── setup.iss                   # Inno Setup — see docs/installer-inno.md
```

Feature inventory: `docs/features.md`. System paths, registry keys, and the full default
browser registration flow: `docs/installer-inno.md`.

---

## Design language

BrowserMux follows **Fluent Design**.

- **Material**: **Mica only**. No Acrylic — `DesktopAcrylicController` is unreliable in
  unpackaged WinUI 3, and Mica is the right choice for an app window anyway.
- Mica is managed by Windows: it samples the desktop wallpaper once and tints with the
  system accent color. Not opacity-tunable.
- Use Fluent typography, spacing, and `ThemeResource` brushes (unpackaged-safe subset — see
  `x-build` skill).
- Apply backdrops via `SystemBackdropHelper.Apply(this)`, never raw XAML Mica (Win10 breaks).
  See `x-win10-compat`.

---

## Important constraints

- **Never write to `UserChoice` directly** — silently reverted by Win10 1903+. Route the user
  through `ms-settings:defaultapps` instead.
- **Handler must start in < 100ms** — AOT mandatory, zero heavy dependencies.
- **DPI-awareness** — `SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)`
  in Handler.
- **Multi-monitor** — picker must appear on the screen where the cursor is.
- **Core stays UI-free** — all business logic in `BrowserMux.Core`, never reference
  WinUI/WinAppSDK from Core.
- **Logs everywhere** — every important service logs its actions via `AppLogger`.
