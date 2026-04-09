using System.Text.Json;
using BrowserMux.Core.Models;
using Microsoft.Win32;

namespace BrowserMux.Core.Services;

public static class BrowserDetector
{
    // Known Chromium browsers with their User Data path
    private static readonly (string BrowserName, string RelativeUserDataPath)[] ChromiumBrowsers =
    [
        ("Google Chrome",  @"Google\Chrome\User Data"),
        ("Brave",          @"BraveSoftware\Brave-Browser\User Data"),
        ("Microsoft Edge", @"Microsoft\Edge\User Data"),
        ("Vivaldi",        @"Vivaldi\User Data"),
        ("Opera",          @"Opera Software\Opera Stable"),
        ("Chromium",       @"Chromium\User Data"),
    ];

    /// <summary>
    /// Returns detected browsers plus user-defined custom browsers from preferences.
    /// </summary>
    public static List<Browser> DetectAll(bool detectChromiumProfiles = true)
    {
        var list = Detect(detectChromiumProfiles);
        var custom = PreferencesService.Instance.Current.CustomBrowsers;
        foreach (var c in custom)
        {
            if (string.IsNullOrWhiteSpace(c.ExePath) || !File.Exists(c.ExePath)) continue;
            list.Add(c with { IsCustom = true });
        }
        return list;
    }

    public static List<Browser> Detect(bool detectChromiumProfiles = true)
    {
        AppLogger.Info("=== BrowserDetector.Detect() start ===");
        var browsers = new List<Browser>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Read from Windows registry (StartMenuInternet)
        foreach (var browser in ReadFromRegistry())
        {
            if (seen.Add(browser.ExePath))
            {
                AppLogger.Info($"  [registry] {browser.Name} → {browser.ExePath}");
                browsers.Add(browser);
            }
        }

        // 2. Fallback App Paths — catches Store browsers (e.g. Firefox MSIX)
        foreach (var browser in ReadFromAppPaths())
        {
            if (seen.Add(browser.ExePath))
            {
                AppLogger.Info($"  [apppaths] {browser.Name} → {browser.ExePath}");
                browsers.Add(browser);
            }
        }

        // 3. Enrich with Chromium profiles
        if (detectChromiumProfiles)
        {
            var enriched = new List<Browser>();
            foreach (var browser in browsers)
            {
                var exeName = Path.GetFileName(browser.ExePath).ToLowerInvariant();
                var profiles = TryGetChromiumProfiles(browser);
                if (profiles.Count > 0)
                {
                    AppLogger.Info($"  [profiles] {browser.Name} → {profiles.Count} profile(s):");
                    // Assign stable Id to each profile
                    var profilesWithIds = profiles
                        .Select(p => p with { Id = $"{exeName}:::{p.ProfileDirectory}" })
                        .ToList();
                    foreach (var p in profilesWithIds)
                        AppLogger.Info($"      {p.ProfileDirectory} → \"{p.Name}\" (id={p.Id})");
                    enriched.Add(browser with { Profiles = profilesWithIds, IsChromiumBased = true });
                    continue;
                }
                enriched.Add(browser with { Profiles = profiles, IsChromiumBased = false });
            }
            AppLogger.Info($"=== Detect() done — {enriched.Count} browser(s) total ===");
            return enriched;
        }

        AppLogger.Info($"=== Detect() done — {browsers.Count} browser(s) total ===");
        return browsers;
    }

    private static IEnumerable<Browser> ReadFromRegistry()
    {
        var roots = new[] { Registry.CurrentUser, Registry.LocalMachine };
        const string key = @"SOFTWARE\Clients\StartMenuInternet";

        foreach (var root in roots)
        {
            using var startMenu = root.OpenSubKey(key);
            if (startMenu is null) continue;

            foreach (var browserKey in startMenu.GetSubKeyNames())
            {
                using var entry = startMenu.OpenSubKey(browserKey);
                if (entry is null) continue;

                var exePath = GetExePath(entry);
                if (exePath is null || !File.Exists(exePath)) continue;

                var name = entry.OpenSubKey("Capabilities")
                               ?.GetValue("ApplicationName") as string
                           ?? browserKey;

                yield return new Browser
                {
                    Name = name,
                    ExePath = exePath,
                    IconPath = exePath,
                };
            }
        }
    }

    // Store or non-standard browsers that don't register in StartMenuInternet
    private static readonly (string ExeName, string DisplayName)[] KnownAppPathsBrowsers =
    [
        ("firefox.exe",  "Firefox"),
        ("opera.exe",    "Opera"),
        ("whale.exe",    "Naver Whale"),
    ];

    private static IEnumerable<Browser> ReadFromAppPaths()
    {
        const string key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";
        var roots = new[] { Registry.CurrentUser, Registry.LocalMachine };

        foreach (var (exeName, displayName) in KnownAppPathsBrowsers)
        {
            foreach (var root in roots)
            {
                using var entry = root.OpenSubKey($@"{key}\{exeName}");
                if (entry is null) continue;

                var path = entry.GetValue(null) as string;
                if (path is null || !File.Exists(path)) continue;

                yield return new Browser { Name = displayName, ExePath = path, IconPath = path };
                break; // found in HKCU, no need to check HKLM
            }
        }
    }

    private static string? GetExePath(RegistryKey browserEntry)
    {
        // Path 1: shell\open\command
        var cmd = browserEntry.OpenSubKey(@"shell\open\command")
                              ?.GetValue(null) as string;
        if (cmd is not null)
            return ExtractExeFromCommand(cmd);

        // Path 2: Capabilities\ApplicationIcon (format "path,index")
        var icon = browserEntry.OpenSubKey("Capabilities")
                               ?.GetValue("ApplicationIcon") as string;
        if (icon is not null)
        {
            var path = icon.Split(',')[0].Trim('"');
            return File.Exists(path) ? path : null;
        }

        return null;
    }

    private static string? ExtractExeFromCommand(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : null;
        }
        var space = command.IndexOf(' ');
        return space > 0 ? command[..space] : command;
    }

    private static List<BrowserProfile> TryGetChromiumProfiles(Browser browser)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        foreach (var (knownName, relativePath) in ChromiumBrowsers)
        {
            if (!browser.Name.Contains(knownName, StringComparison.OrdinalIgnoreCase))
                continue;

            // Opera stores its data in AppData/Roaming
            var basePath = knownName == "Opera"
                ? Path.Combine(appData, relativePath)
                : Path.Combine(localAppData, relativePath);

            if (!Directory.Exists(basePath)) continue;

            return ReadProfiles(basePath);
        }

        return [];
    }

    private static List<BrowserProfile> ReadProfiles(string userDataPath)
    {
        var profiles = new List<BrowserProfile>();

        // Local State contains profile names and colors
        var cache = ReadLocalStateCache(userDataPath);

        // Profile directories: "Default" + "Profile N"
        var candidates = Directory.EnumerateDirectories(userDataPath)
            .Where(d =>
            {
                var name = Path.GetFileName(d);
                return name == "Default" || name.StartsWith("Profile ");
            });

        foreach (var dir in candidates)
        {
            var profileDir = Path.GetFileName(dir);

            // Skip system profiles
            if (profileDir is "System Profile" or "Guest Profile") continue;

            // 1. Name + color from Local State (most reliable)
            if (cache.TryGetValue(profileDir, out var cached))
            {
                if (cached.Name is "System Profile" or "Guest Profile") continue;
                profiles.Add(new BrowserProfile
                {
                    Name = cached.Name,
                    ProfileDirectory = profileDir,
                    ProfileColor = cached.Color,
                });
                continue;
            }

            // 2. Fallback: Preferences (less reliable, no color)
            var prefsFile = Path.Combine(dir, "Preferences");
            if (!File.Exists(prefsFile)) continue;

            try
            {
                using var stream = File.OpenRead(prefsFile);
                var doc = JsonDocument.Parse(stream);
                var name = doc.RootElement
                    .GetProperty("profile")
                    .GetProperty("name")
                    .GetString() ?? profileDir;

                if (name is "System Profile" or "Guest Profile") continue;
                profiles.Add(new BrowserProfile { Name = name, ProfileDirectory = profileDir });
            }
            catch { /* Corrupted or locked Preferences → skip */ }
        }

        // "Default" first, then alphabetical order
        return [.. profiles.OrderBy(p => p.ProfileDirectory == "Default" ? 0 : 1)
                           .ThenBy(p => p.Name)];
    }

    private record ProfileCacheEntry(string Name, int? Color);

    /// <summary>
    /// Reads Local State → profile.info_cache to get profile names and colors.
    /// </summary>
    private static Dictionary<string, ProfileCacheEntry> ReadLocalStateCache(string userDataPath)
    {
        var result = new Dictionary<string, ProfileCacheEntry>(StringComparer.OrdinalIgnoreCase);
        var localStatePath = Path.Combine(userDataPath, "Local State");
        if (!File.Exists(localStatePath)) return result;

        try
        {
            using var stream = File.OpenRead(localStatePath);
            var doc = JsonDocument.Parse(stream);

            if (!doc.RootElement.TryGetProperty("profile", out var profileEl)) return result;
            if (!profileEl.TryGetProperty("info_cache", out var cache)) return result;

            foreach (var entry in cache.EnumerateObject())
            {
                var profileDir = entry.Name;
                string? name = null;
                int? color = null;

                if (entry.Value.TryGetProperty("name", out var nameEl))
                    name = nameEl.GetString();

                if (entry.Value.TryGetProperty("profile_highlight_color", out var colorEl)
                    && colorEl.TryGetInt32(out var colorInt))
                    color = colorInt;

                if (!string.IsNullOrWhiteSpace(name))
                    result[profileDir] = new ProfileCacheEntry(name, color);
            }
        }
        catch { /* Corrupted Local State → fallback to Preferences */ }

        return result;
    }
}
