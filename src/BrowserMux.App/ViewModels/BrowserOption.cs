namespace BrowserMux.App.ViewModels;

/// <summary>
/// Lightweight identifier for a browser/profile used in dropdowns and rule pickers.
/// </summary>
public sealed class BrowserOption(string id, string displayName)
{
    public string Id { get; } = id;
    public string DisplayName { get; } = displayName;
}
