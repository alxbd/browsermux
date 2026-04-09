using CommunityToolkit.Mvvm.ComponentModel;

namespace BrowserMux.App.ViewModels;

#pragma warning disable MVVMTK0045

/// <summary>
/// ViewModel for a single rule row in the Settings > Rules list.
/// </summary>
public sealed partial class RuleItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _pattern = "";

    public List<BrowserOption> AvailableBrowsers { get; set; } = [];

    [ObservableProperty]
    private BrowserOption? _selectedBrowser;
}
