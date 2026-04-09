using System.Text.Json.Serialization;

namespace BrowserMux.Core.Models;

public enum RuleMatchType { Domain, Glob, Regex }

public record Rule
{
    public required string Pattern { get; init; }
    public required RuleMatchType MatchType { get; init; }

    /// <summary>
    /// Target browser/profile name.
    /// Special value "_picker" = force showing the picker.
    /// </summary>
    public required string BrowserName { get; init; }

    public bool IsEnabled { get; init; } = true;
}
