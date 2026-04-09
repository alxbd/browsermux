using System.Reflection;

namespace BrowserMux.Core;

/// <summary>
/// Single source of truth for the app name and identifiers.
/// To rename the app, modify ONLY this file.
/// </summary>
public static class AppInfo
{
#if DEBUG
    /// <summary>Display name shown in UI, registry, and logs. (Dev channel)</summary>
    public const string AppName = "BrowserMux Dev";
#else
    /// <summary>Display name shown in UI, registry, and logs.</summary>
    public const string AppName = "BrowserMux";
#endif

    /// <summary>Semver version string, read from the assembly's InformationalVersion
    /// (defined once in Directory.Build.props at the repo root).</summary>
    public static string AppVersion { get; } =
        typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion?.Split('+')[0] ?? "0.0.0";

    /// <summary>Current schema version of preferences.json. Bump when the model changes
    /// and add a migration step in PreferencesService.MigratePreferences.</summary>
    public const int PreferencesSchemaVersion = 1;

    /// <summary>Current schema version of rules.json. Bump when the model changes
    /// and add a migration step in PreferencesService.MigrateRules.</summary>
    public const int RulesSchemaVersion = 1;

#if DEBUG
    /// <summary>IPC pipe name (Handler → App). (Dev channel)</summary>
    public const string PipeName = "BrowserMuxDevPipe";

    /// <summary>Single-instance mutex name. (Dev channel)</summary>
    public const string MutexName = "BrowserMux_Dev_SingleInstance";

    /// <summary>ProgId registered in Windows registry. (Dev channel)</summary>
    public const string ProgId = "BrowserMuxDevURL";

    /// <summary>Subfolder in %LOCALAPPDATA% for logs, cache, etc. (Dev channel)</summary>
    public const string LocalAppDataFolder = "BrowserMux Dev";

    /// <summary>Subfolder in %APPDATA% for user config. (Dev channel)</summary>
    public const string RoamingAppDataFolder = "BrowserMux Dev";

    /// <summary>Crash dump filename in %TEMP%. (Dev channel)</summary>
    public const string CrashFileName = "BrowserMux_Dev_crash.txt";
#else
    /// <summary>IPC pipe name (Handler → App).</summary>
    public const string PipeName = "BrowserMuxPipe";

    /// <summary>Single-instance mutex name.</summary>
    public const string MutexName = "BrowserMux_SingleInstance";

    /// <summary>ProgId registered in Windows registry.</summary>
    public const string ProgId = "BrowserMuxURL";

    /// <summary>Subfolder in %LOCALAPPDATA% for logs, cache, etc.</summary>
    public const string LocalAppDataFolder = "BrowserMux";

    /// <summary>Subfolder in %APPDATA% for user config.</summary>
    public const string RoamingAppDataFolder = "BrowserMux";

    /// <summary>Crash dump filename in %TEMP%.</summary>
    public const string CrashFileName = "BrowserMux_crash.txt";
#endif

    /// <summary>Main exe name (without path). Same in dev and prod — only the
    /// AssemblyName drives this and we don't want to rename the binary.</summary>
    public const string AppExeName = "BrowserMux.exe";

    /// <summary>GitHub owner/repo for release checks.</summary>
    public const string GitHubRepo = "alxbd/browsermux";

    // ── Derived paths (computed once) ─────────────────────────────────────

    public static readonly string LocalDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        LocalAppDataFolder);

    public static readonly string RoamingDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        RoamingAppDataFolder);

    public static readonly string LogDir = Path.Combine(LocalDataDir, "logs");
    public static readonly string LogPath = Path.Combine(LogDir, "app.log");
    public static readonly string PreferencesPath = Path.Combine(LocalDataDir, "preferences.json");
    public static readonly string RulesPath = Path.Combine(LocalDataDir, "rules.json");
    public static readonly string CrashPath = Path.Combine(Path.GetTempPath(), CrashFileName);
}
