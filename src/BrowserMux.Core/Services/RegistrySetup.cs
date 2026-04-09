using Microsoft.Win32;

namespace BrowserMux.Core.Services;

/// <summary>
/// Checks that BrowserMux is properly registered as a browser in the Windows registry.
/// Does NOT write keys — that's the installer's job (requires admin).
/// </summary>
public static class RegistrySetup
{
    public static RegistryStatus Check()
    {
        var status = new RegistryStatus();

        // 1. Check ProgId exists (HKLM or HKCU)
        status.HasProgId = KeyExists(Registry.LocalMachine, $@"Software\Classes\{AppInfo.ProgId}\shell\open\command")
                        || KeyExists(Registry.CurrentUser,  $@"Software\Classes\{AppInfo.ProgId}\shell\open\command");

        // 2. Check Capabilities registered
        status.HasCapabilities = KeyExists(Registry.LocalMachine, $@"Software\Clients\StartMenuInternet\{AppInfo.AppName}\Capabilities")
                              || KeyExists(Registry.CurrentUser,  $@"Software\Clients\StartMenuInternet\{AppInfo.AppName}\Capabilities");

        // 3. Check RegisteredApplications entry
        status.HasRegisteredApp = ValueExists(Registry.LocalMachine, @"Software\RegisteredApplications", AppInfo.AppName)
                               || ValueExists(Registry.CurrentUser,  @"Software\RegisteredApplications", AppInfo.AppName);

        // 4. Check if currently the default browser for https
        status.IsDefaultBrowser = IsDefaultForHttps();

        AppLogger.Info($"[RegistrySetup] ProgId={status.HasProgId}, Capabilities={status.HasCapabilities}, " +
                       $"RegisteredApp={status.HasRegisteredApp}, IsDefault={status.IsDefaultBrowser}");

        return status;
    }

    private static bool IsDefaultForHttps()
    {
        try
        {
            // UserChoice for https is at HKCU\Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");
            var progId = key?.GetValue("ProgId") as string;
            return string.Equals(progId, AppInfo.ProgId, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool KeyExists(RegistryKey root, string subkey)
    {
        try
        {
            using var key = root.OpenSubKey(subkey);
            return key is not null;
        }
        catch { return false; }
    }

    private static bool ValueExists(RegistryKey root, string subkey, string valueName)
    {
        try
        {
            using var key = root.OpenSubKey(subkey);
            return key?.GetValue(valueName) is not null;
        }
        catch { return false; }
    }
}

public class RegistryStatus
{
    public bool HasProgId { get; set; }
    public bool HasCapabilities { get; set; }
    public bool HasRegisteredApp { get; set; }
    public bool IsDefaultBrowser { get; set; }

    public bool IsFullyRegistered => HasProgId && HasCapabilities && HasRegisteredApp;
}
