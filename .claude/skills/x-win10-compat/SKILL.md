---
name: x-win10-compat
description: Windows 10 compatibility checklist for BrowserMux. Use this skill when touching window chrome, backdrops, icon fonts, FontIcon glyphs, WinAppSDK version, or any XAML/visual feature that could break on Win10 1809+.
---

# x-win10-compat — Windows 10 + 11 support rules

BrowserMux targets **Windows 10 1809 (build 17763) and later**, including all Win11.
Win10 is a first-class target, not best-effort. The floor is dictated by Windows App
SDK 1.7 (which itself requires 1809+).

## Rules

### Mica is Win11-only
- **Never** assign `<Window.SystemBackdrop><MicaBackdrop/></Window.SystemBackdrop>` in XAML.
- Always go through `Services/SystemBackdropHelper.Apply(this)` from the window constructor.
  It checks `MicaController.IsSupported()` and falls back to a solid
  `ApplicationPageBackgroundThemeBrush` on Win10.
- Direct XAML Mica on Win10 = transparent, visually broken window.

### No Acrylic at all
- `Microsoft.UI.Xaml.Controls.AcrylicBrush` / `DesktopAcrylicController` are banned.
  Already unreliable in unpackaged WinUI 3, doubly so on Win10.

### Icon fonts — FontIcon, not hardcoded family
- Don't hardcode `FontFamily="Segoe Fluent Icons"`. That font ships only on Win11.
- Use `<FontIcon Glyph="&#xE...;"/>` with no `FontFamily` override — `FontIcon` has a
  built-in fallback chain to `Segoe MDL2 Assets`, which exists on Win10.

### Glyph code points — stay in MDL2 range
- Safe ranges: `E700`–`EE5F`, plus handful in `ED00`–`EDxx`.
- **Unsafe**: `F0xx`–`F8xx` — generally Segoe Fluent Icons-only, renders as tofu on Win10.
- Search the glyph in both font lists (MDL2 + Fluent) before committing.

### WinAppSDK 1.7 is the floor
- The installer downloads and installs the WinAppSDK 1.7 runtime on the fly via the
  bootstrapper (see `docs/installer-inno.md`).
- **Never** bump the target to 1.8+ without verifying the new minimum OS build.

### `ms-settings:` layout differences
- `ms-settings:defaultapps` works on both Win10 and Win11.
- Win10's Default Apps screen has no search box and no per-protocol shortcut — user must
  scroll to "Web browser" manually.
- Don't build UX flows that assume the Win11 layout (e.g. deep-linking to `https` protocol).

## Testing

- Test on a real Win10 VM before shipping anything touching window chrome, backdrops, or
  icon fonts. Visual issues are **silent** — they do not crash, they just look wrong, and
  they won't appear on a Win11 dev box.

## Related docs

- `docs/local-dev-mode.md` — dev/prod dual-channel build
- `docs/installer-inno.md` — prerequisite bootstrap flow that makes Win10 viable
