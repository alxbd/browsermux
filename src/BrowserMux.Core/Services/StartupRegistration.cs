using Microsoft.Win32;

namespace BrowserMux.Core.Services;

/// <summary>
/// Manages the "Start with Windows" entry in HKCU\...\CurrentVersion\Run.
///
/// The registry value IS the state — nothing is mirrored in preferences.json, so the
/// settings toggle can never disagree with what Windows actually does (Task Manager >
/// Startup, cleanup tools, a reinstall).
///
/// Always HKCU, never HKLM: the entry must stay per-user so the in-app toggle works
/// without admin rights, even after an "all users" install.
/// </summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Value name in the Run key. Differs per channel (AppName is "BrowserMux Dev"
    /// in Debug), so dev and prod never collide.</summary>
    private static string ValueName => AppInfo.AppName;

    /// <summary>Full path to the running exe, quoted for the registry.</summary>
    private static string CommandLine => $"\"{ExePath}\"";

    private static string ExePath =>
        Environment.ProcessPath
        ?? Path.Combine(AppContext.BaseDirectory, AppInfo.AppExeName);

    /// <summary>True when BrowserMux is registered to start with Windows.</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is not null;
        }
        catch (Exception ex)
        {
            AppLogger.Error("[Startup] Failed to read the Run key", ex);
            return false;
        }
    }

    /// <summary>Registers the current exe to start with Windows. Returns false on failure.</summary>
    public static bool Enable()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
                ?? throw new InvalidOperationException($@"Could not open HKCU\{RunKey}");
            key.SetValue(ValueName, CommandLine, RegistryValueKind.String);
            AppLogger.Info($"[Startup] Enabled — {CommandLine}");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error("[Startup] Failed to enable start with Windows", ex);
            return false;
        }
    }

    /// <summary>Removes the startup entry. A missing value counts as success.</summary>
    public static bool Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null)
            {
                AppLogger.Info("[Startup] Already disabled (no Run key)");
                return true;
            }

            key.DeleteValue(ValueName, throwOnMissingValue: false);
            AppLogger.Info("[Startup] Disabled");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error("[Startup] Failed to disable start with Windows", ex);
            return false;
        }
    }

    /// <summary>
    /// Rewrites the startup entry when it points somewhere else than the running exe
    /// (app moved, per-user install replaced by a machine install, ...). Does nothing when
    /// startup is off. Called once at app launch so a stale entry never survives.
    /// </summary>
    public static void RepairIfNeeded()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(ValueName) is not string current) return;

            if (string.Equals(current.Trim(), CommandLine, StringComparison.OrdinalIgnoreCase))
                return;

            key.SetValue(ValueName, CommandLine, RegistryValueKind.String);
            AppLogger.Warn($"[Startup] Stale entry repaired: {current} -> {CommandLine}");
        }
        catch (Exception ex)
        {
            AppLogger.Error("[Startup] Failed to repair the startup entry", ex);
        }
    }
}
