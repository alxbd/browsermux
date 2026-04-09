# Versioning — BrowserMux

This document describes how the app version and the configuration file schemas
are managed. Any change to the model of `preferences.json` or `rules.json`
**must** follow the rules below.

---

## 1. Application version

### Single source of truth

The version lives in **`Directory.Build.props`** at the repo root:

```xml
<PropertyGroup>
  <Version>0.1.0</Version>
  <FileVersion>0.1.0.0</FileVersion>
  <InformationalVersion>0.1.0</InformationalVersion>
</PropertyGroup>
```

All projects (`BrowserMux.Core`, `BrowserMux.App`, `BrowserMux.Handler`)
inherit it automatically via MSBuild. Inno Setup picks up the version from the
exe when building the installer.

### Runtime read

`AppInfo.AppVersion` reads the assembly's `InformationalVersion` via reflection:

```csharp
public static string AppVersion { get; } =
    typeof(AppInfo).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
        .InformationalVersion?.Split('+')[0] ?? "0.0.0";
```

→ Displayed in Settings → About, used by `UpdateChecker` to compare against
the GitHub Releases tag.

### SemVer scheme

| Bump  | When                                                              |
| ----- | ----------------------------------------------------------------- |
| MAJOR | Breaking change in a config format, or feature removal           |
| MINOR | New backwards-compatible feature                                  |
| PATCH | Bug fix                                                           |

### How to release a new version

1. Bump `<Version>`, `<FileVersion>`, `<InformationalVersion>` in `Directory.Build.props`.
2. If a config schema has changed: see §2.
3. `pwsh build.ps1` — the exe and installer carry the new version.
4. Tag git `v0.1.1`.

**No other file to touch.** No version is hardcoded anywhere else.

---

## 2. Configuration file versioning

### Files in scope

| File                                          | C# model          | Version constant                   |
| --------------------------------------------- | ----------------- | ---------------------------------- |
| `%LOCALAPPDATA%\BrowserMux\preferences.json`  | `UserPreferences` | `AppInfo.PreferencesSchemaVersion` |
| `%LOCALAPPDATA%\BrowserMux\rules.json`        | `RulesFile`       | `AppInfo.RulesSchemaVersion`       |

Each file carries a `SchemaVersion` field as its first property:

```jsonc
// preferences.json
{
  "SchemaVersion": 1,
  "PinnedBrowserIds": [...],
  "HiddenBrowserIds": [...],
  "Settings": { ... }
}
```

```jsonc
// rules.json
{
  "SchemaVersion": 1,
  "Rules": [
    { "Pattern": "...", "MatchType": "Domain", "BrowserId": "..." }
  ]
}
```

> **Note**: `rules.json` was originally a root array. It was wrapped in an
> object so it could carry `SchemaVersion`. This is the last time the root
> shape can change without an explicit migration.

### Load rules (PreferencesService.LoadVersioned)

At startup, each file goes through `LoadVersioned<T>()` which applies exactly
the following rules:

| Case                              | Behavior                                                                                                                                                       |
| --------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| File missing                      | Returns a fresh instance (`freshFactory()`).                                                                                                                   |
| `SchemaVersion == current`        | Deserializes normally.                                                                                                                                         |
| `SchemaVersion < current`         | **Backup** to `<file>.v{old}.bak`, runs the chained `migrator`, immediately saves the result at the current schema.                                            |
| `SchemaVersion > current`         | **Refuses to read** (downgrade not supported). Backs up to `<file>.v{found}.future.bak`, logs a warning, returns a fresh instance. Next `Save()` overwrites.   |
| Exception (invalid JSON, etc.)    | Logs error, returns a fresh instance. (No backup — the corrupted file stays in place for debugging.)                                                           |

Rationale for the downgrade rule:
- If the user installs a recent version (which writes schema v3) then rolls
  back to an older one (which only knows v1), partial reads risk silent
  corruption. We prefer to **start from scratch** with a backup over
  corrupting data.
- The `.future.bak` file lets the user manually recover settings or reinstall
  the recent version.

### Save

`Save()` and `SaveRules()` **always** write the current version:

```csharp
public void Save()
{
    Current.SchemaVersion = AppInfo.PreferencesSchemaVersion;
    SaveJson(AppInfo.PreferencesPath, Current);
    ...
}

public void SaveRules()
{
    var file = new RulesFile
    {
        SchemaVersion = AppInfo.RulesSchemaVersion,
        Rules = DomainRules,
    };
    SaveJson(AppInfo.RulesPath, file);
}
```

→ It's impossible to write a file without a `SchemaVersion`.

---

## 3. How to add a migration (workflow)

When you change the model of a config (rename, removal, restructuring), you
**must**:

### Step 1 — Bump the constant

In `src/BrowserMux.Core/AppInfo.cs`:

```csharp
public const int PreferencesSchemaVersion = 2;  // was 1
```

### Step 2 — Write the migration

In `PreferencesService.MigratePreferences` (or `MigrateRules`), add a case to
the chain. Migrations are chained via `goto case`:

```csharp
private static UserPreferences MigratePreferences(JsonElement root, int fromVersion)
{
    var current = root.Deserialize<UserPreferences>(JsonOptions) ?? new UserPreferences();

    switch (fromVersion)
    {
        case 0:
            // ex: old field "Pinned" → "PinnedBrowserIds"
            if (root.TryGetProperty("Pinned", out var oldPinned))
                current.PinnedBrowserIds = oldPinned.Deserialize<List<string>>() ?? [];
            goto case 1;

        case 1:
            // v1 → v2 migration here
            goto case 2;

        case 2:
            break;
    }

    return current;
}
```

### Step 3 — Test with an old JSON

1. Drop a `preferences.json` at the old schema into `%LOCALAPPDATA%\BrowserMux\`.
2. Launch the app (`pwsh build.ps1 -Run`).
3. Check `app.log`:
   - Message `Migrating preferences.json v{N} → v{N+1}`.
   - Presence of the file `preferences.json.v{N}.bak`.
   - No read errors.
4. Verify settings are preserved in the UI.

### Step 4 — Bump app version

If the migration is non-trivial, bump the app version (`<Version>` in
`Directory.Build.props`) according to SemVer (usually MINOR, MAJOR if
breaking).

---

## 4. What **not** to do

- ❌ **Never** modify a config model without bumping `*SchemaVersion`.
- ❌ **Never** read `preferences.json` or `rules.json` directly with
  `JsonSerializer.Deserialize`, bypassing `PreferencesService` — migrations
  won't apply.
- ❌ **Never** hardcode the app version anywhere except `Directory.Build.props`.
- ❌ **Do not** auto-delete `.bak` files. They are the user's safety net.
- ❌ **Do not** attempt to read a file at a higher schema than the app
  ("best-effort downgrade") — it's a source of silent corruption.

---

## 5. Quick reference — key files

| File                                                             | Role                                                              |
| ---------------------------------------------------------------- | ----------------------------------------------------------------- |
| `Directory.Build.props`                                          | Source of truth for the app version                               |
| `src/BrowserMux.Core/AppInfo.cs`                                 | `AppVersion`, `PreferencesSchemaVersion`, `RulesSchemaVersion`    |
| `src/BrowserMux.Core/Models/UserPreferences.cs`                  | `UserPreferences`, `RulesFile` models                             |
| `src/BrowserMux.Core/Services/PreferencesService.cs`             | `LoadVersioned`, `MigratePreferences`, `MigrateRules`, `Save*`    |
| `%LOCALAPPDATA%\BrowserMux\preferences.json`                     | User config (settings, pinned/hidden)                             |
| `%LOCALAPPDATA%\BrowserMux\rules.json`                           | Routing rules                                                     |
| `%LOCALAPPDATA%\BrowserMux\*.v{N}.bak`                           | Auto backup before migration                                      |
| `%LOCALAPPDATA%\BrowserMux\*.v{N}.future.bak`                    | Auto backup when a downgrade is detected                          |

---

## 6. Current state

| Item                        | Value   |
| --------------------------- | ------- |
| App version                 | `0.1.0` |
| `PreferencesSchemaVersion`  | `1`     |
| `RulesSchemaVersion`        | `1`     |

No migrations in place yet — schema v1 is the first to carry `SchemaVersion`.
The `Migrate*` methods are skeletons ready to receive steps at the next bump.
