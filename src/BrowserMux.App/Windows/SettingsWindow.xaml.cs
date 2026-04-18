using System.Diagnostics;
using BrowserMux.App.Services;
using BrowserMux.App.ViewModels;
using BrowserMux.Core;
using BrowserMux.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VirtualKey = global::Windows.System.VirtualKey;

namespace BrowserMux.App.Windows;

public sealed partial class SettingsWindow : Window
{
    public SettingsViewModel ViewModel { get; } = new();

    public SettingsWindow()
    {
        InitializeComponent();
        Title = $"{AppInfo.AppName} Settings";
        ExtendsContentIntoTitleBar = true;

        AppWindow.Resize(new global::Windows.Graphics.SizeInt32(1100, 820));
        AppWindow.SetIcon(App.AppIconPath);

        SystemBackdropHelper.Apply(this);

        ViewModel.Load();
        BindToViewModel();
        ApplyTheme();
        _appliedIsDark = ComputeIsDark();

        NavView.SelectedItem = NavView.MenuItems[0];

        PreferencesService.Instance.SettingsChanged += OnSettingsChanged;
        Closed += (_, _) => PreferencesService.Instance.SettingsChanged -= OnSettingsChanged;

#if DEBUG
        UpdateCard.Visibility = Visibility.Collapsed;
#else
        // Kick off an update check as soon as the window is shown. The check itself
        // honors a 6h cooldown, so opening Settings repeatedly won't spam GitHub.
        Activated += OnFirstActivated;
#endif
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs e)
    {
        Activated -= OnFirstActivated;
        CheckForUpdatesAsync(force: false);
    }

    private bool _appliedIsDark;

    private void OnSettingsChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            // RequestedTheme can't be mutated on a live WinUI 3 window without freezing —
            // if the theme actually changed, recreate the window instead.
            var nowDark = ComputeIsDark();
            if (nowDark != _appliedIsDark)
            {
                RecreateRequested?.Invoke();
                Close();
            }
        });
    }

    /// <summary>Raised when the window needs to be recreated (e.g. theme change). Owner reopens.</summary>
    public event Action? RecreateRequested;

    private static bool ComputeIsDark()
    {
        var s = PreferencesService.Instance.Current.Settings;
        return s.Theme == Core.Models.AppTheme.Dark
            || (s.Theme == Core.Models.AppTheme.System && IsSystemDarkTheme());
    }

    // ── Manual bindings (VM → controls) ─────────────────────────────────────

    private bool _syncing;

    private void BindToViewModel()
    {
        _syncing = true;

        // General settings
        ThemeCombo.SelectedIndex = ViewModel.ThemeIndex;
        AlwaysOnTopToggle.IsOn = ViewModel.AlwaysOnTop;
        CloseOnFocusLossToggle.IsOn = ViewModel.CloseOnFocusLoss;
        DetectProfilesToggle.IsOn = ViewModel.DetectChromiumProfiles;
        HotkeyBox.Text = ViewModel.LauncherHotkey;

        // About
        VersionText.Text = ViewModel.VersionText;
        RegistryStatusText.Text = ViewModel.RegistryStatusText;
        SetDefaultButton.IsEnabled = !RegistrySetup.Check().IsDefaultBrowser;

        // Lists
        BrowsersListView.ItemsSource = ViewModel.BrowserItems;
        RulesListView.ItemsSource = ViewModel.RuleItems;

        _syncing = false;
    }

    // ── Theme (Window chrome — stays in code-behind) ────────────────────────

    private void ApplyTheme()
    {
        var settings = PreferencesService.Instance.Current.Settings;
        var isDark = settings.Theme == Core.Models.AppTheme.Dark
            || (settings.Theme == Core.Models.AppTheme.System && IsSystemDarkTheme());

        if (Content is FrameworkElement root)
            root.RequestedTheme = isDark ? ElementTheme.Dark : ElementTheme.Light;

        var titleBar = AppWindow.TitleBar;
        var fg = isDark ? global::Windows.UI.Color.FromArgb(255, 255, 255, 255)
                        : global::Windows.UI.Color.FromArgb(255, 0, 0, 0);
        var bg = global::Windows.UI.Color.FromArgb(0, 0, 0, 0);
        titleBar.ForegroundColor = fg;
        titleBar.ButtonForegroundColor = fg;
        titleBar.ButtonBackgroundColor = bg;
        titleBar.ButtonInactiveBackgroundColor = bg;
        titleBar.ButtonHoverBackgroundColor = isDark
            ? global::Windows.UI.Color.FromArgb(30, 255, 255, 255)
            : global::Windows.UI.Color.FromArgb(30, 0, 0, 0);
        titleBar.ButtonHoverForegroundColor = fg;
    }

    private static bool IsSystemDarkTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int i && i == 0;
        }
        catch { return false; }
    }

    // ── Navigation ──────────────────────────────────────────────────────────

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag as string;
        GeneralPanel.Visibility  = tag == "General"  ? Visibility.Visible : Visibility.Collapsed;
        BrowsersPanel.Visibility = tag == "Browsers" ? Visibility.Visible : Visibility.Collapsed;
        RulesPanel.Visibility    = tag == "Rules"    ? Visibility.Visible : Visibility.Collapsed;
        AboutPanel.Visibility    = tag == "About"    ? Visibility.Visible : Visibility.Collapsed;

        if (tag == "About")
            CheckForUpdatesAsync();
    }

    // ── General settings event handlers → VM ────────────────────────────────

    private void ThemeCombo_Changed(object s, SelectionChangedEventArgs e)
    { if (!_syncing) ViewModel.ThemeIndex = ThemeCombo.SelectedIndex; }

    private void AlwaysOnTopToggle_Toggled(object s, RoutedEventArgs e)
    { if (!_syncing) ViewModel.AlwaysOnTop = AlwaysOnTopToggle.IsOn; }

    private void CloseOnFocusLossToggle_Toggled(object s, RoutedEventArgs e)
    { if (!_syncing) ViewModel.CloseOnFocusLoss = CloseOnFocusLossToggle.IsOn; }

    private void DetectProfilesToggle_Toggled(object s, RoutedEventArgs e)
    { if (!_syncing) ViewModel.DetectChromiumProfiles = DetectProfilesToggle.IsOn; }

    // ── Hotkey capture (UI-specific — stays in code-behind) ─────────────────

    private bool _hotkeyCapturing;

    private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        _hotkeyCapturing = true;
        HotkeyBox.PlaceholderText = "Press key combination...";
    }

    private void HotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        _hotkeyCapturing = false;
        HotkeyBox.PlaceholderText = "Click, then press keys";
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_hotkeyCapturing) return;
        e.Handled = true;

        if (e.Key is VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl
            or VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift
            or VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu
            or VirtualKey.LeftWindows or VirtualKey.RightWindows)
            return;

        uint modifiers = 0;
        var kbState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread;
        if (kbState(VirtualKey.Control).HasFlag(global::Windows.UI.Core.CoreVirtualKeyStates.Down))
            modifiers |= 0x0002;
        if (kbState(VirtualKey.Menu).HasFlag(global::Windows.UI.Core.CoreVirtualKeyStates.Down))
            modifiers |= 0x0001;
        if (kbState(VirtualKey.Shift).HasFlag(global::Windows.UI.Core.CoreVirtualKeyStates.Down))
            modifiers |= 0x0004;

        if (modifiers == 0) return;

        var vk = (uint)e.Key;
        var formatted = GlobalHotkeyService.FormatHotkey(modifiers, vk);
        if (!GlobalHotkeyService.TryParseHotkey(formatted, out _, out _)) return;

        HotkeyBox.Text = formatted;
        ViewModel.SetHotkey(formatted);

        _hotkeyCapturing = false;
        HotkeyClearButton.Focus(FocusState.Programmatic);
    }

    private void HotkeyClearButton_Click(object sender, RoutedEventArgs e)
    {
        HotkeyBox.Text = "";
        ViewModel.ClearHotkeyCommand.Execute(null);
    }

    private void SetDefaultBrowser_Click(object s, RoutedEventArgs e)
        => ViewModel.SetDefaultBrowserCommand.Execute(null);

    // ── Browsers tab event handlers → VM ────────────────────────────────────

    private void RemoveCustomBrowser_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BrowserItemViewModel vm })
        {
            ViewModel.RemoveCustomBrowser(vm);
            BrowsersListView.ItemsSource = ViewModel.BrowserItems;
        }
    }

    private async void AddCustomBrowser_Click(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox { PlaceholderText = "e.g. Firefox Nightly", Header = "Name" };
        var pathBox = new TextBox { PlaceholderText = "C:\\path\\to\\browser.exe", Header = "Executable path", IsReadOnly = true };
        var argsBox = new TextBox { PlaceholderText = "Optional", Header = "Arguments (optional)" };
        var browseBtn = new Button { Content = "Browse...", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 0) };

        browseBtn.Click += async (_, _) =>
        {
            var picker = new global::Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add(".exe");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var file = await picker.PickSingleFileAsync();
            if (file is not null)
            {
                pathBox.Text = file.Path;
                if (string.IsNullOrWhiteSpace(nameBox.Text))
                    nameBox.Text = Path.GetFileNameWithoutExtension(file.Path);
            }
        };

        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(nameBox);
        stack.Children.Add(pathBox);
        stack.Children.Add(browseBtn);
        stack.Children.Add(argsBox);

        var dialog = new ContentDialog
        {
            Title = "Add custom browser",
            Content = stack,
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.Content.XamlRoot,
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            ViewModel.AddCustomBrowser(nameBox.Text, pathBox.Text, argsBox.Text);
            BrowsersListView.ItemsSource = ViewModel.BrowserItems;
        }
    }

    private void ReloadBrowsers_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ReloadBrowsersCommand.Execute(null);
        // Rebind lists after reload
        BrowsersListView.ItemsSource = ViewModel.BrowserItems;
        RulesListView.ItemsSource = ViewModel.RuleItems;
    }

    // ── Browser drag-reorder ──────────────────────────────────────────────

    private void BrowsersListView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        // WinUI 3 ListView reorders its internal items but does not update the
        // source ObservableCollection. Sync the collection to match the visual order.
        var reordered = sender.Items.Cast<BrowserItemViewModel>().ToList();

        ViewModel.BrowserItems.CollectionChanged -= ViewModel.OnBrowserItemsChanged;
        ViewModel.BrowserItems.Clear();
        foreach (var item in reordered)
            ViewModel.BrowserItems.Add(item);
        ViewModel.BrowserItems.CollectionChanged += ViewModel.OnBrowserItemsChanged;

        ViewModel.SaveBrowserOrder();
    }

    // ── Rules tab event handlers → VM ───────────────────────────────────────

    private void RuleDomain_LostFocus(object sender, RoutedEventArgs e) => ViewModel.SaveRules();
    private void RuleBrowser_Changed(object sender, SelectionChangedEventArgs e) => ViewModel.SaveRules();

    private void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RuleItemViewModel vm })
            ViewModel.DeleteRule(vm);
    }

    private void AddRule_Click(object sender, RoutedEventArgs e)
        => ViewModel.AddRuleCommand.Execute(null);

    private void TestUrl_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.TestUrlText = TestUrlBox.Text;
        ViewModel.TestUrlCommand.Execute(null);
        TestUrlResult.Text = ViewModel.TestUrlResult;
    }

    // ── Update ──────────────────────────────────────────────────────────────

    private Core.Models.UpdateInfo? _pendingUpdate;

    public async void CheckForUpdatesAsync(bool force = false)
    {
        UpdateStatusText.Text = "Checking for updates...";
        UpdateButton.Visibility = Visibility.Collapsed;
        ReleaseNotesLink.Visibility = Visibility.Collapsed;

        var info = await UpdateChecker.CheckAsync(force);
        if (info is null)
        {
            UpdateStatusText.Text = "You're up to date";
            return;
        }

        if (info.IsAvailable)
        {
            _pendingUpdate = info;
            UpdateStatusText.Text = $"Version {info.LatestVersion} available";
            UpdateButton.Visibility = Visibility.Visible;
            if (info.ReleaseNotesUrl is not null)
            {
                ReleaseNotesLink.NavigateUri = new Uri(info.ReleaseNotesUrl);
                ReleaseNotesLink.Visibility = Visibility.Visible;
            }
        }
        else
        {
            UpdateStatusText.Text = "You're up to date";
        }
    }

    private void CheckUpdate_Click(object sender, RoutedEventArgs e)
        => CheckForUpdatesAsync(force: true);

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate?.DownloadUrl is null) return;

        UpdateButton.IsEnabled = false;
        UpdateButton.Content = "Downloading...";
        UpdateProgress.Visibility = Visibility.Visible;
        UpdateProgress.Value = 0;

        var progress = new Progress<double>(p =>
        {
            DispatcherQueue.TryEnqueue(() => UpdateProgress.Value = p);
        });

        var path = await UpdateChecker.DownloadInstallerAsync(_pendingUpdate.DownloadUrl, progress);

        if (path is not null)
        {
            UpdateStatusText.Text = "Launching installer...";
            // /SILENT shows a small progress window with no prompts; /SUPPRESSMSGBOXES
            // accepts default answers; /NORESTART avoids surprise reboots; /RELAUNCH is
            // a custom switch our setup.iss watches for to relaunch BrowserMux after
            // install. We exit ourselves before launching because the PickerWindow's
            // Closing handler cancels WM_CLOSE (to hide instead of close), which
            // prevents Inno Setup's CloseApplications from terminating the process.
            Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                Arguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART /RELAUNCH",
            });
            Application.Current.Exit();
        }
        else
        {
            UpdateStatusText.Text = "Download failed — try again";
            UpdateButton.Content = "Download & install";
            UpdateButton.IsEnabled = true;
            UpdateProgress.Visibility = Visibility.Collapsed;
        }
    }
}
