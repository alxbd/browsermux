using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using BrowserMux.Core;
using BrowserMux.Core.Models;
using BrowserMux.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BrowserMux.App.ViewModels;

#pragma warning disable MVVMTK0045

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly PreferencesService _prefs = PreferencesService.Instance;
    private bool _loading;

    private AppSettings Settings => _prefs.Current.Settings;

    // ── Observable settings ─────────────────────────────────────────────────

    [ObservableProperty]
    private int _themeIndex;

    [ObservableProperty]
    private bool _alwaysOnTop;

    [ObservableProperty]
    private bool _closeOnFocusLoss;

    [ObservableProperty]
    private bool _detectChromiumProfiles;

    [ObservableProperty]
    private string _launcherHotkey = "";

    [ObservableProperty]
    private string _versionText = "";

    [ObservableProperty]
    private string _registryStatusText = "";

    [ObservableProperty]
    private string _testUrlText = "";

    [ObservableProperty]
    private string _testUrlResult = "";

    // ── Collections ─────────────────────────────────────────────────────────

    public ObservableCollection<BrowserItemViewModel> BrowserItems { get; } = [];
    public ObservableCollection<RuleItemViewModel> RuleItems { get; } = [];

    private List<BrowserOption> _browserOptions = [];
    public List<BrowserOption> BrowserOptions => _browserOptions;

    public SettingsViewModel()
    {
        LauncherHotkey = "";
        VersionText = "";
        RegistryStatusText = "";
        TestUrlText = "";
        TestUrlResult = "";

        BrowserItems.CollectionChanged += OnBrowserItemsChanged;
    }

    internal void OnBrowserItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Only react to drag-reorder moves; bulk Clear/Add during Load is ignored.
        if (_loading) return;
        if (e.Action == NotifyCollectionChangedAction.Move)
            SaveBrowserOrder();
    }

    // ── Initialization ──────────────────────────────────────────────────────

    public void Load()
    {
        _loading = true;

        BuildBrowserOptions();

        ThemeIndex = (int)Settings.Theme;
        AlwaysOnTop = Settings.AlwaysOnTop;
        CloseOnFocusLoss = Settings.CloseOnFocusLoss;
        DetectChromiumProfiles = Settings.DetectChromiumProfiles;
        LauncherHotkey = Settings.LauncherHotkey ?? "";


#if DEBUG
        VersionText = "dev";
#else
        VersionText = $"v{AppInfo.AppVersion}";
#endif
        var status = RegistrySetup.Check();
        RegistryStatusText = status.IsDefaultBrowser
            ? "BrowserMux is the default browser"
            : "Not set as default browser";

        LoadBrowserItems();
        LoadRuleItems();

        _loading = false;
    }

    // ── Settings change handlers ────────────────────────────────────────────

    partial void OnThemeIndexChanged(int value)
    { if (_loading) return; Settings.Theme = (AppTheme)value; Save(); }

    partial void OnAlwaysOnTopChanged(bool value)
    { if (_loading) return; Settings.AlwaysOnTop = value; Save(); }

    partial void OnCloseOnFocusLossChanged(bool value)
    { if (_loading) return; Settings.CloseOnFocusLoss = value; Save(); }

    partial void OnDetectChromiumProfilesChanged(bool value)
    { if (_loading) return; Settings.DetectChromiumProfiles = value; Save(); }

    private void Save() => _prefs.Save();

    // ── Hotkey ───────────────────────────────────────────────────────────────

    public void SetHotkey(string formatted)
    {
        LauncherHotkey = formatted;
        Settings.LauncherHotkey = formatted;
        Save();
    }

    [RelayCommand]
    private void ClearHotkey()
    {
        LauncherHotkey = "";
        Settings.LauncherHotkey = null;
        Save();
    }

    // ── Browser options ─────────────────────────────────────────────────────

    private readonly HashSet<string> _customIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _idToExePath = new(StringComparer.OrdinalIgnoreCase);

    private void BuildBrowserOptions()
    {
        var detected = BrowserDetector.DetectAll(detectChromiumProfiles: true);
        _browserOptions = [];
        _customIds.Clear();
        _idToExePath.Clear();

        foreach (var browser in detected)
        {
            if (browser.Profiles.Count > 0)
            {
                foreach (var profile in browser.Profiles)
                {
                    var id = $"{Path.GetFileName(browser.ExePath ?? "").ToLowerInvariant()}:::{profile.ProfileDirectory}";
                    _browserOptions.Add(new BrowserOption(id, $"{browser.Name} — {profile.Name}"));
                    _idToExePath[id] = browser.ExePath ?? "";
                }
            }
            else
            {
                var id = Path.GetFileName(browser.ExePath ?? "").ToLowerInvariant();
                _browserOptions.Add(new BrowserOption(id, browser.Name));
                _idToExePath[id] = browser.ExePath ?? "";
                if (browser.IsCustom) _customIds.Add(id);
            }
        }
    }

    private BrowserOption FindOrCreateOption(string browserId)
    {
        var existing = _browserOptions.FirstOrDefault(b => b.Id == browserId);
        if (existing is not null) return existing;
        var orphan = new BrowserOption(browserId, $"(orphan) {browserId}");
        _browserOptions.Add(orphan);
        return orphan;
    }

    // ── Browsers tab ────────────────────────────────────────────────────────

    private void LoadBrowserItems()
    {
        var hidden = _prefs.Current.HiddenBrowserIds;
        var browserOrder = _prefs.Current.BrowserOrder;

        var vms = _browserOptions.Select(b => new BrowserItemViewModel
        {
            Id = b.Id, DisplayName = b.DisplayName, IsVisible = !hidden.Contains(b.Id),
            IsCustom = _customIds.Contains(b.Id),
            ExePath = _idToExePath.TryGetValue(b.Id, out var p) ? p : "",
        }).ToList();

        var ordered = vms.Where(v => browserOrder.Contains(v.Id))
            .OrderBy(v => browserOrder.IndexOf(v.Id))
            .Concat(vms.Where(v => !browserOrder.Contains(v.Id)))
            .ToList();

        // Detach old handlers before clearing to avoid leaks on reload.
        foreach (var old in BrowserItems) old.VisibilityChanged -= OnBrowserVisibilityChanged;

        BrowserItems.Clear();
        foreach (var vm in ordered)
        {
            vm.VisibilityChanged += OnBrowserVisibilityChanged;
            BrowserItems.Add(vm);
        }
    }

    private void OnBrowserVisibilityChanged()
    {
        if (_loading) return;
        SaveBrowserVisibility();
    }

    public void SaveBrowserVisibility()
    {
        if (_loading) return;
        _prefs.Current.HiddenBrowserIds.Clear();
        foreach (var vm in BrowserItems.Where(b => !b.IsVisible))
            _prefs.Current.HiddenBrowserIds.Add(vm.Id);
        _prefs.Save();
    }

    internal void SaveBrowserOrder()
    {
        var ordered = BrowserItems.Select(b => b.Id).ToList();
        _prefs.SetBrowserOrder(ordered);
    }

    // ── Custom browsers ─────────────────────────────────────────────────────

    public void AddCustomBrowser(string name, string exePath, string? args)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(exePath)) return;
        if (!File.Exists(exePath)) return;

        var browser = new Browser
        {
            Name = name.Trim(),
            ExePath = exePath,
            Args = string.IsNullOrWhiteSpace(args) ? null : args.Trim(),
            IconPath = exePath,
            IsCustom = true,
        };
        _prefs.Current.CustomBrowsers.Add(browser);
        _prefs.Save();

        ReloadBrowsers();
        AppLogger.Info($"[SettingsVM] Added custom browser: {name} → {exePath}");
    }

    public void RemoveCustomBrowser(BrowserItemViewModel vm)
    {
        if (!vm.IsCustom) return;

        _prefs.Current.CustomBrowsers.RemoveAll(b =>
            string.Equals(Path.GetFileName(b.ExePath).ToLowerInvariant(), vm.Id, StringComparison.OrdinalIgnoreCase));
        _prefs.Save();

        ReloadBrowsers();
        AppLogger.Info($"[SettingsVM] Removed custom browser: {vm.Id}");
    }

    [RelayCommand]
    private void ReloadBrowsers()
    {
        BuildBrowserOptions();
        LoadBrowserItems();
        LoadRuleItems();
        AppLogger.Info("[SettingsVM] Browsers reloaded.");
    }

    // ── Rules tab ────────────────────────────────────────────────────────────

    private void LoadRuleItems()
    {
        RuleItems.Clear();
        foreach (var r in _prefs.DomainRules)
        {
            RuleItems.Add(new RuleItemViewModel
            {
                Pattern = r.Pattern,
                AvailableBrowsers = _browserOptions,
                SelectedBrowser = FindOrCreateOption(r.BrowserId),
            });
        }
    }

    public void SaveRules()
    {
        _prefs.DomainRules.Clear();
        foreach (var vm in RuleItems)
        {
            if (string.IsNullOrWhiteSpace(vm.Pattern) || vm.SelectedBrowser is null) continue;
            _prefs.DomainRules.Add(new DomainRule
            {
                Pattern = vm.Pattern.Trim(),
                MatchType = RuleMatchType.Domain,
                BrowserId = vm.SelectedBrowser.Id,
            });
        }
        _prefs.SaveRules();
    }

    public void DeleteRule(RuleItemViewModel vm)
    {
        RuleItems.Remove(vm);
        SaveRules();
    }

    [RelayCommand]
    private void AddRule()
    {
        RuleItems.Add(new RuleItemViewModel
        {
            Pattern = "",
            AvailableBrowsers = _browserOptions,
            SelectedBrowser = _browserOptions.FirstOrDefault(),
        });
    }

    [RelayCommand]
    private void TestUrl()
    {
        var url = TestUrlText.Trim();
        if (string.IsNullOrEmpty(url)) { TestUrlResult = ""; return; }
        if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            url = "https://" + url;

        var engine = new RuleEngine([]);
        var browserId = engine.MatchToBrowserId(url, _prefs.DomainRules);

        if (browserId is null)
            TestUrlResult = "No match — picker will show";
        else if (browserId == "_picker")
            TestUrlResult = "Match: force picker";
        else
        {
            var name = _browserOptions.FirstOrDefault(b => b.Id == browserId)?.DisplayName ?? browserId;
            TestUrlResult = $"→ {name}";
        }
    }

    [RelayCommand]
    private static void SetDefaultBrowser()
    {
        Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
    }
}
