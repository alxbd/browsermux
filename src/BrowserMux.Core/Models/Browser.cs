namespace BrowserMux.Core.Models;

public record Browser
{
    public required string Name { get; init; }
    public required string ExePath { get; init; }
    public string? Args { get; init; }
    public string? IconPath { get; init; }
    public bool IsChromiumBased { get; init; }

    /// <summary>True if this browser was added manually by the user (persisted in preferences).</summary>
    public bool IsCustom { get; init; }

    public List<BrowserProfile> Profiles { get; init; } = [];

    /// <summary>
    /// Stable browser ID. Computed by BrowserDetector.
    /// Without profile: "firefox.exe"
    /// With profile: computed in BrowserProfile.Id
    /// </summary>
    public string Id => Path.GetFileName(ExePath).ToLowerInvariant();
}
