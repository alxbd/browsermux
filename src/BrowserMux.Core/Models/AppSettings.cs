namespace BrowserMux.Core.Models;

public enum AppTheme { System, Light, Dark }

public class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.System;

    public bool AlwaysOnTop { get; set; } = true;
    public bool CloseOnFocusLoss { get; set; } = false;
    public bool DetectChromiumProfiles { get; set; } = true;

    /// <summary>Global hotkey to open the picker as a browser launcher (no URL). e.g. "Ctrl+Alt+B"</summary>
    public string? LauncherHotkey { get; set; }

    /// <summary>Last time the app checked for updates (UTC ISO 8601).</summary>
    public string? LastUpdateCheck { get; set; }
}
