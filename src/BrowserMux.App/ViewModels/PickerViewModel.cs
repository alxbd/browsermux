using System.Collections.ObjectModel;
using System.Diagnostics;
using BrowserMux.Core;
using BrowserMux.Core.Models;
using BrowserMux.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;

namespace BrowserMux.App.ViewModels;

#pragma warning disable MVVMTK0045

public sealed partial class PickerViewModel : ObservableObject
{
    private const string DefaultHint = "Shift — always open with…  ·  Alt — open in incognito";
    private const string LauncherHint = "Alt — open in incognito";

    // ── Observable state ────────────────────────────────────────────────────

    [ObservableProperty]
    private string _currentUrl = string.Empty;

    [ObservableProperty]
    private bool _isLauncherMode;

    [ObservableProperty]
    private bool _isAltHeld;

    [ObservableProperty]
    private bool _isAlwaysOpenMode;

    [ObservableProperty]
    private string _hintText = "";

    [ObservableProperty]
    private double _hintOpacity;

    [ObservableProperty]
    private Visibility _urlBarVisibility;

    [ObservableProperty]
    private Visibility _setDefaultLinkVisibility;

    [ObservableProperty]
    private Visibility _shiftHintVisibility;

    [ObservableProperty]
    private int _selectedIndex;

    public ObservableCollection<BrowserCardViewModel> Browsers { get; } = [];

    public PickerViewModel()
    {
        CurrentUrl = string.Empty;
        HintText = DefaultHint;
        HintOpacity = 0.45;
        UrlBarVisibility = Visibility.Visible;
        SetDefaultLinkVisibility = Visibility.Collapsed;
        ShiftHintVisibility = Visibility.Visible;
        SelectedIndex = -1;
    }

    // ── Events for the View ─────────────────────────────────────────────────

    public event Action? RequestHide;
    public event Action? RequestShowSettings;
    public event Action? RequestActivate;

    // ── Show methods ────────────────────────────────────────────────────────

    public bool ShowForUrl(string url)
    {
        CurrentUrl = url;
        IsLauncherMode = false;
        UrlBarVisibility = Visibility.Visible;

        if (TryAutoLaunch(url))
            return false;

        ResetState();
        HintText = DefaultHint;
        LoadBrowsers();
        CheckRegistryStatus();

        RequestActivate?.Invoke();
        return true;
    }

    public void ShowAsLauncher()
    {
        CurrentUrl = string.Empty;
        IsLauncherMode = true;
        UrlBarVisibility = Visibility.Collapsed;

        ResetState();
        HintText = LauncherHint;
        LoadBrowsers();
        CheckRegistryStatus();

        RequestActivate?.Invoke();
    }

    private void ResetState()
    {
        IsAlwaysOpenMode = false;
        IsAltHeld = false;
        SelectedIndex = -1;
        HintOpacity = 0.45;
    }

    // ── Browser loading ─────────────────────────────────────────────────────

    private void LoadBrowsers()
    {
        var detected = BrowserDetector.DetectAll(detectChromiumProfiles: true);
        var cards = BuildCards(detected);

        Browsers.Clear();
        foreach (var card in cards)
            Browsers.Add(card);
    }

    private List<BrowserCardViewModel> BuildCards(List<Browser> browsers)
    {
        var prefs = PreferencesService.Instance;
        var hidden = prefs.Current.HiddenBrowserIds;
        var pinned = prefs.Current.PinnedBrowserIds;
        const int iconSize = 32;

        var all = new List<BrowserCardViewModel>();

        foreach (var browser in browsers)
        {
            if (browser.Profiles.Count > 0)
            {
                foreach (var profile in browser.Profiles)
                {
                    var id = profile.Id;
                    if (hidden.Contains(id)) continue;
                    all.Add(new BrowserCardViewModel
                    {
                        Id           = id,
                        DisplayName  = profile.Name,
                        SubText      = browser.Name,
                        ExePath      = browser.ExePath,
                        Args         = $"--profile-directory=\"{profile.ProfileDirectory}\"",
                        IconPath     = browser.IconPath,
                        IconSize     = iconSize,
                        CurrentUrl   = CurrentUrl,
                        OnLaunched   = () => RequestHide?.Invoke(),
                        IsPinned     = pinned.Contains(id),
                        ProfileColor = profile.ProfileColor,
                    });
                }
            }
            else
            {
                var exeName = Path.GetFileName(browser.ExePath ?? "").ToLowerInvariant();
                if (hidden.Contains(exeName)) continue;
                all.Add(new BrowserCardViewModel
                {
                    Id          = exeName,
                    DisplayName = browser.Name,
                    ExePath     = browser.ExePath,
                    Args        = browser.Args,
                    IconPath    = browser.IconPath,
                    IconSize    = iconSize,
                    CurrentUrl  = CurrentUrl,
                    OnLaunched  = () => RequestHide?.Invoke(),
                    IsPinned    = pinned.Contains(exeName),
                });
            }
        }

        var ordered = all
            .Where(c => c.IsPinned)
            .OrderBy(c => { var i = pinned.IndexOf(c.Id); return i < 0 ? int.MaxValue : i; })
            .Concat(all.Where(c => !c.IsPinned))
            .ToList();

        for (int i = 0; i < ordered.Count; i++)
            ordered[i].ShortcutKey = i < 9 ? (i + 1).ToString() : null;

        return ordered;
    }

    // ── Auto-launch ─────────────────────────────────────────────────────────

    private bool TryAutoLaunch(string url)
    {
        var prefs = PreferencesService.Instance;
        if (prefs.DomainRules.Count == 0) return false;

        var engine = new RuleEngine([]);
        var browserId = engine.MatchToBrowserId(url, prefs.DomainRules);
        if (browserId is null or "_picker") return false;

        var browsers = BrowserDetector.DetectAll(detectChromiumProfiles: true);
        BrowserCardViewModel? target = null;

        foreach (var browser in browsers)
        {
            if (browser.Profiles.Count > 0)
            {
                foreach (var profile in browser.Profiles)
                {
                    var id = $"{Path.GetFileName(browser.ExePath ?? "").ToLowerInvariant()}:::{profile.ProfileDirectory}";
                    if (id == browserId)
                    {
                        target = new BrowserCardViewModel
                        {
                            Id = id, ExePath = browser.ExePath,
                            Args = $"--profile-directory=\"{profile.ProfileDirectory}\"",
                            CurrentUrl = url,
                        };
                        break;
                    }
                }
                if (target is not null) break;
            }
            else
            {
                var exeName = Path.GetFileName(browser.ExePath ?? "").ToLowerInvariant();
                if (exeName == browserId)
                {
                    target = new BrowserCardViewModel
                    {
                        Id = exeName, ExePath = browser.ExePath,
                        Args = browser.Args, CurrentUrl = url,
                    };
                    break;
                }
            }
        }

        if (target is null)
        {
            AppLogger.Warn($"[PickerVM] Auto-launch: browser '{browserId}' not found, showing picker.");
            return false;
        }

        AppLogger.Info($"[PickerVM] Auto-launch: {url} → {browserId}");
        LaunchBrowserDirect(target.ExePath, target.Args, url);
        return true;
    }

    // ── Browser launching ───────────────────────────────────────────────────

    public void LaunchCard(BrowserCardViewModel vm, bool incognito)
    {
        var args = vm.Args ?? "";

        if (incognito)
        {
            var exeName = Path.GetFileNameWithoutExtension(vm.ExePath ?? "").ToLowerInvariant();
            var flag = exeName is "firefox" or "librewolf" ? "-private-window" : "--incognito";
            args = $"{flag} {args}".Trim();
        }

        var url = vm.CurrentUrl ?? CurrentUrl;
        if (!string.IsNullOrEmpty(url))
            args = $"{args} \"{url}\"".Trim();

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = vm.ExePath,
                Arguments = args,
                UseShellExecute = false,
            });
            RequestHide?.Invoke();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[PickerVM] Launch failed: {ex.Message}");
        }
    }

    private static void LaunchBrowserDirect(string? exePath, string? args, string url)
    {
        var finalArgs = args ?? "";
        if (!string.IsNullOrEmpty(url))
            finalArgs = $"{finalArgs} \"{url}\"".Trim();

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = finalArgs,
                UseShellExecute = false,
            });
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[PickerVM] Auto-launch failed: {ex.Message}");
        }
    }

    // ── Modifier key handling ────────────────────────────────────────────────

    public void SetAltHeld(bool held)
    {
        IsAltHeld = held;
        if (held && IsAlwaysOpenMode)
            SetAlwaysOpenMode(false);
        UpdateHintText();
    }

    public void SetAlwaysOpenMode(bool active)
    {
        if (IsAlwaysOpenMode == active) return;
        if (active && IsLauncherMode) return;

        IsAlwaysOpenMode = active;
        foreach (var card in Browsers)
            card.IsAlwaysOpenMode = active;
        UpdateHintText();
    }

    private void UpdateHintText()
    {
        if (IsAlwaysOpenMode && !IsLauncherMode && Uri.TryCreate(CurrentUrl, UriKind.Absolute, out var uri))
        {
            var domain = uri.Host;
            if (domain.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                domain = domain[4..];
            HintOpacity = 1.0;
            HintText = $"Shift — always open \"{domain}\" with…";
        }
        else if (IsAltHeld)
        {
            HintOpacity = 1.0;
            HintText = "Alt — open in incognito";
        }
        else
        {
            HintOpacity = 0.45;
            HintText = IsLauncherMode ? LauncherHint : DefaultHint;
        }
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private void CopyUrl()
    {
        if (IsLauncherMode || string.IsNullOrEmpty(CurrentUrl)) return;
        var dp = new DataPackage();
        dp.SetText(CurrentUrl);
        Clipboard.SetContent(dp);
    }

    [RelayCommand]
    private void Cancel() => RequestHide?.Invoke();

    [RelayCommand]
    private void OpenSettings() => RequestShowSettings?.Invoke();

    [RelayCommand]
    private static void SetDefaultBrowser()
    {
        Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
    }

    // ── Registry status ─────────────────────────────────────────────────────

    private void CheckRegistryStatus()
    {
        var status = RegistrySetup.Check();
        if (!status.IsDefaultBrowser)
        {
            SetDefaultLinkVisibility = Visibility.Visible;
            ShiftHintVisibility = Visibility.Collapsed;
        }
        else
        {
            SetDefaultLinkVisibility = Visibility.Collapsed;
            ShiftHintVisibility = Visibility.Visible;
        }

        if (!status.IsFullyRegistered)
            AppLogger.Warn("[PickerVM] Browser not fully registered. Run the installer or register.ps1.");
    }
}
