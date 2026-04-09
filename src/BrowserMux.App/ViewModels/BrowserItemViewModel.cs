using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace BrowserMux.App.ViewModels;

#pragma warning disable MVVMTK0045

/// <summary>
/// ViewModel for a browser row in the Settings > Browsers list.
/// </summary>
public sealed partial class BrowserItemViewModel : ObservableObject
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ExePath { get; set; } = "";
    public bool IsCustom { get; set; }

    public Visibility RemoveButtonVisibility => IsCustom ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Raised when IsVisible changes via binding (drives the parent's save).</summary>
    public event Action? VisibilityChanged;

    [ObservableProperty]
    private bool _isVisible = true;

    partial void OnIsVisibleChanged(bool value) => VisibilityChanged?.Invoke();
}
