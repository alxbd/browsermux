using BrowserMux.Core.Models;

namespace BrowserMux.Core.Models;

public class RulesFile
{
    /// <summary>Schema version — see AppInfo.RulesSchemaVersion.</summary>
    public int SchemaVersion { get; set; } = AppInfo.RulesSchemaVersion;

    public List<DomainRule> Rules { get; set; } = [];
}

public class UserPreferences
{
    /// <summary>Schema version — see AppInfo.PreferencesSchemaVersion.</summary>
    public int SchemaVersion { get; set; } = AppInfo.PreferencesSchemaVersion;

    /// <summary>All browser IDs in the user's desired display order.</summary>
    public List<string> BrowserOrder { get; set; } = [];

    /// <summary>IDs to hide completely from the picker</summary>
    public HashSet<string> HiddenBrowserIds { get; set; } = [];

    /// <summary>Application settings (appearance, behavior)</summary>
    public AppSettings Settings { get; set; } = new();

    /// <summary>User-added browsers that don't appear in the registry (e.g. portable exes).</summary>
    public List<Browser> CustomBrowsers { get; set; } = [];
}

public record DomainRule
{
    public required string Pattern { get; init; }
    public required RuleMatchType MatchType { get; init; }
    public required string BrowserId { get; init; }
}
