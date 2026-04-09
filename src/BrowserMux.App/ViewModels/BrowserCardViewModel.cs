using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace BrowserMux.App.ViewModels;

#pragma warning disable MVVMTK0045

/// <summary>
/// ViewModel for a single browser/profile card in the picker.
/// </summary>
public sealed partial class BrowserCardViewModel : ObservableObject
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? SubText { get; init; }
    public string? ShortcutKey { get; set; }
    public string? ExePath { get; init; }
    public string? Args { get; init; }
    public string? IconPath { get; init; }
    public string? CurrentUrl { get; set; }
    public int IconSize { get; init; } = 32;
    public bool IsPinned { get; init; }
    public int? ProfileColor { get; init; }
    public Action? OnLaunched { get; set; }

    [ObservableProperty]
    private bool _isAlwaysOpenMode;

    public Visibility HasShortcut => ShortcutKey is not null ? Visibility.Visible : Visibility.Collapsed;
}
