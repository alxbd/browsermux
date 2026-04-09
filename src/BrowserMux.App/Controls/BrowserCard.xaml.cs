using BrowserMux.App.Services;
using BrowserMux.App.ViewModels;
using BrowserMux.Core.Models;
using BrowserMux.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BrowserMux.App.Controls;

public sealed partial class BrowserCard : UserControl
{
    public BrowserCardViewModel? ViewModel { get; private set; }

    private bool _isHovered;

    public BrowserCard()
    {
        InitializeComponent();

        PointerEntered += (_, _) => { _isHovered = true; UpdateOverlayIcons(); };
        PointerExited  += (_, _) => { _isHovered = false; UpdateOverlayIcons(); };

        DataContextChanged += (_, _) =>
        {
            if (DataContext is BrowserCardViewModel vm)
            {
                ViewModel = vm;
                BrowserName.Text = vm.DisplayName;

                if (!string.IsNullOrEmpty(vm.SubText))
                {
                    SubText.Text = vm.SubText;
                    SubText.Visibility = Visibility.Visible;
                }
                else
                {
                    SubText.Visibility = Visibility.Collapsed;
                }

                if (vm.ShortcutKey is not null)
                {
                    ShortcutText.Text = vm.ShortcutKey;
                    ShortcutBadge.Visibility = Visibility.Visible;
                }
                else
                {
                    ShortcutBadge.Visibility = Visibility.Collapsed;
                }

                ApplyProfileColor(vm.ProfileColor);
                _ = LoadIconAsync();
            }
        };
    }

    public void RefreshPinIcon() => UpdateOverlayIcons();

    private void UpdateOverlayIcons()
    {
        PinIcon.Visibility = (_isHovered && ViewModel?.IsAlwaysOpenMode == true)
            ? Visibility.Visible : Visibility.Collapsed;

        IncognitoIcon.Visibility = (_isHovered && Windows.PickerWindow.ViewModel.IsAltHeld)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyProfileColor(int? color)
    {
        if (color is null)
        {
            ProfileColorDot.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                global::Windows.UI.Color.FromArgb(0, 0, 0, 0));
            return;
        }

        var argb = unchecked((uint)color.Value) | 0xFF000000;
        var c = global::Windows.UI.Color.FromArgb(
            (byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

        ProfileColorDot.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(c);
    }

    private async Task LoadIconAsync()
    {
        if (ViewModel?.IconPath is null) return;
        try
        {
            var iconSize = ViewModel.IconSize > 0 ? ViewModel.IconSize : 32;
            BrowserIcon.Width = iconSize;
            BrowserIcon.Height = iconSize;
            var bitmap = await IconExtractor.GetBitmapFromExeAsync(ViewModel.IconPath, size: iconSize);
            if (bitmap is not null)
                BrowserIcon.Source = bitmap;
        }
        catch { }
    }

    private void CardButton_Click(object sender, RoutedEventArgs e)
        => LaunchBrowser(incognito: Windows.PickerWindow.ViewModel.IsAltHeld);

    private void CardButton_RightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        => LaunchBrowser(incognito: true);

    private void Open_Click(object sender, RoutedEventArgs e)
        => LaunchBrowser(incognito: false);

    private void OpenPrivate_Click(object sender, RoutedEventArgs e)
        => LaunchBrowser(incognito: true);

    private void LaunchBrowser(bool incognito)
    {
        if (ViewModel is null) return;

        // "Always open with" mode: create a domain rule before launching
        if (ViewModel.IsAlwaysOpenMode && !string.IsNullOrEmpty(ViewModel.CurrentUrl))
        {
            try
            {
                if (Uri.TryCreate(ViewModel.CurrentUrl, UriKind.Absolute, out var uri))
                {
                    var domain = uri.Host;
                    if (domain.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                        domain = domain[4..];

                    PreferencesService.Instance.AddDomainRule(new DomainRule
                    {
                        Pattern   = domain,
                        MatchType = RuleMatchType.Domain,
                        BrowserId = ViewModel.Id,
                    });
                    AppLogger.Info($"[BrowserCard] 'Always open' rule: {domain} → {ViewModel.Id}");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("[BrowserCard] Error creating domain rule", ex);
            }
        }

        var args = ViewModel.Args ?? string.Empty;

        if (incognito)
        {
            var exeName = Path.GetFileNameWithoutExtension(ViewModel.ExePath ?? "").ToLowerInvariant();
            var flag = exeName switch
            {
                "firefox" or "librewolf" => "-private-window",
                _ => "--incognito",
            };
            args = $"{flag} {args}".Trim();
        }

        var url = ViewModel.CurrentUrl ?? string.Empty;
        if (!string.IsNullOrEmpty(url))
            args = $"{args} \"{url}\"".Trim();

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = ViewModel.ExePath,
                Arguments = args,
                UseShellExecute = false,
            });
            ViewModel.OnLaunched?.Invoke();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Launch error: {ex.Message}");
        }
    }
}
