using System.Text.Json;
using System.Text.Json.Serialization;
using BrowserMux.Core.Models;

namespace BrowserMux.Core.Services;

/// <summary>
/// Singleton managing persistence of preferences.json and rules.json
/// in %LOCALAPPDATA%\BrowserMux\
/// </summary>
public sealed class PreferencesService
{
    private static readonly Lazy<PreferencesService> _instance =
        new(() => new PreferencesService());

    public static PreferencesService Instance => _instance.Value;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public UserPreferences Current { get; private set; }
    public List<DomainRule> DomainRules { get; private set; }

    /// <summary>Fired after Save() — subscribers can refresh UI instantly.</summary>
    public event Action? SettingsChanged;

    private PreferencesService()
    {
        Current = LoadVersioned(
            AppInfo.PreferencesPath,
            AppInfo.PreferencesSchemaVersion,
            MigratePreferences,
            () => new UserPreferences());

        var rulesFile = LoadVersioned(
            AppInfo.RulesPath,
            AppInfo.RulesSchemaVersion,
            MigrateRules,
            () => new RulesFile());
        DomainRules = rulesFile.Rules;

        AppLogger.Info($"[PreferencesService] Loaded preferences + {DomainRules.Count} rule(s)");
    }

    // ── Versioned load with migration / future-version safety ───────────────

    /// <summary>
    /// Loads a JSON file with a "SchemaVersion" field.
    /// - Missing file → returns a fresh instance.
    /// - Missing/lower SchemaVersion → backup .v{old}.bak then run migrator.
    /// - Equal SchemaVersion → deserialize directly.
    /// - Higher SchemaVersion (downgrade) → backup .v{found}.future.bak, log warning,
    ///   return a fresh instance. The next Save() will overwrite with the current schema.
    /// </summary>
    private static T LoadVersioned<T>(
        string path,
        int currentVersion,
        Func<JsonElement, int, T> migrator,
        Func<T> freshFactory) where T : class
    {
        if (!File.Exists(path)) return freshFactory();

        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            int foundVersion = 0;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("SchemaVersion", out var v) &&
                v.ValueKind == JsonValueKind.Number)
            {
                foundVersion = v.GetInt32();
            }

            if (foundVersion > currentVersion)
            {
                var backup = path + $".v{foundVersion}.future.bak";
                File.Copy(path, backup, overwrite: true);
                AppLogger.Info(
                    $"[PreferencesService] {Path.GetFileName(path)} schema v{foundVersion} > app v{currentVersion}. " +
                    $"Backed up to {Path.GetFileName(backup)}, starting fresh.");
                return freshFactory();
            }

            if (foundVersion < currentVersion)
            {
                var backup = path + $".v{foundVersion}.bak";
                File.Copy(path, backup, overwrite: true);
                AppLogger.Info(
                    $"[PreferencesService] Migrating {Path.GetFileName(path)} v{foundVersion} → v{currentVersion}");
                var migrated = migrator(root, foundVersion);
                SaveJson(path, migrated);
                return migrated;
            }

            return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? freshFactory();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[PreferencesService] Error reading {Path.GetFileName(path)}", ex);
            return freshFactory();
        }
    }

    // ── Migration steps (chain new ones here) ───────────────────────────────

    private static UserPreferences MigratePreferences(JsonElement root, int fromVersion)
    {
        // Future migrations: switch (fromVersion) { case 0: ...; goto case 1; }
        return root.Deserialize<UserPreferences>(JsonOptions) ?? new UserPreferences();
    }

    private static RulesFile MigrateRules(JsonElement root, int fromVersion)
    {
        // Future migrations chain here.
        return root.Deserialize<RulesFile>(JsonOptions) ?? new RulesFile();
    }

    // ── Generic save ────────────────────────────────────────────────────────

    private static void SaveJson<T>(string path, T data)
    {
        try
        {
            var dir = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[PreferencesService] Error saving {Path.GetFileName(path)}", ex);
        }
    }

    public void Save()
    {
        Current.SchemaVersion = AppInfo.PreferencesSchemaVersion;
        SaveJson(AppInfo.PreferencesPath, Current);
        AppLogger.Info("[PreferencesService] preferences.json saved.");
        SettingsChanged?.Invoke();
    }

    public void SaveRules()
    {
        var file = new RulesFile
        {
            SchemaVersion = AppInfo.RulesSchemaVersion,
            Rules = DomainRules,
        };
        SaveJson(AppInfo.RulesPath, file);
        AppLogger.Info($"[PreferencesService] rules.json saved ({DomainRules.Count} rule(s)).");
    }

    // ── DomainRules ──────────────────────────────────────────────────────────

    public void AddDomainRule(DomainRule rule)
    {
        DomainRules.RemoveAll(r => r.Pattern == rule.Pattern);
        DomainRules.Add(rule);
        AppLogger.Info($"[PreferencesService] AddDomainRule: {rule.Pattern} → {rule.BrowserId}");
        SaveRules();
    }

    public void RemoveDomainRule(string pattern)
    {
        DomainRules.RemoveAll(r => r.Pattern == pattern);
        AppLogger.Info($"[PreferencesService] RemoveDomainRule: {pattern}");
        SaveRules();
    }

    // ── Browser order ──────────────────────────────────────────────────────

    public void SetBrowserOrder(List<string> ids)
    {
        Current.BrowserOrder = ids;
        AppLogger.Info($"[PreferencesService] SetBrowserOrder: [{string.Join(", ", ids)}]");
        Save();
    }

    // ── Hide/Show ────────────────────────────────────────────────────────────

    public void Hide(string id)
    {
        if (Current.HiddenBrowserIds.Add(id))
        {
            AppLogger.Info($"[PreferencesService] Hide: {id}");
            Save();
        }
    }

    public void Show(string id)
    {
        if (Current.HiddenBrowserIds.Remove(id))
        {
            AppLogger.Info($"[PreferencesService] Show: {id}");
            Save();
        }
    }
}
