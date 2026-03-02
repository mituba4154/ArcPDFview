#nullable enable

using AcroPDF.App.Assets.Localization;
using AcroPDF.Core.Models;
using AcroPDF.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AcroPDF.App.Views;

/// <summary>
/// アプリケーション設定を編集するモーダルダイアログです。
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly ComboBox _themeComboBox;
    private readonly ComboBox _languageComboBox;
    private readonly ComboBox _defaultZoomComboBox;
    private readonly ComboBox _defaultViewModeComboBox;
    private readonly CheckBox _restoreSessionCheckBox;

    /// <summary>
    /// <see cref="SettingsWindow"/> の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="settings">初期表示する設定値。</param>
    public SettingsWindow(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Title = AppStrings.Get("Settings");
        Width = 380;
        Height = 340;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush((Color)Application.Current!.FindResource("BgDark")!);

        _themeComboBox = new ComboBox
        {
            ItemsSource = new[] { "OS", "Light", "Dark" },
            SelectedIndex = settings.Theme switch
            {
                ThemePreference.Light => 1,
                ThemePreference.Dark => 2,
                _ => 0
            }
        };
        _languageComboBox = new ComboBox
        {
            ItemsSource = new[] { "日本語", "English" },
            SelectedIndex = string.Equals(settings.Language, "en", StringComparison.OrdinalIgnoreCase) ? 1 : 0
        };
        _defaultZoomComboBox = new ComboBox
        {
            ItemsSource = new[] { "25%", "50%", "75%", "100%", "125%", "150%", "200%", "300%", "400%" },
            SelectedItem = $"{Math.Round(PdfiumRenderService.ClampZoomLevel(settings.DefaultZoom) * 100d):0}%"
        };
        _defaultViewModeComboBox = new ComboBox
        {
            ItemsSource = new[] { "単ページ", "連続", "見開き" },
            SelectedIndex = settings.DefaultViewMode switch
            {
                ViewMode.SinglePage => 0,
                ViewMode.TwoPage => 2,
                _ => 1
            }
        };
        _restoreSessionCheckBox = new CheckBox
        {
            Content = "起動時にセッションを復元",
            IsChecked = settings.RestoreSessionOnStartup
        };

        var applyButton = new Button
        {
            Content = "適用",
            Width = 90,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        applyButton.Click += (_, _) =>
        {
            ResultSettings = BuildSettings(settings);
            Close(ResultSettings);
        };

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            Children =
            {
                BuildLabel(AppStrings.Get("Theme")),
                _themeComboBox,
                BuildLabel(AppStrings.Get("Language")),
                _languageComboBox,
                BuildLabel("既定ズーム"),
                _defaultZoomComboBox,
                BuildLabel("既定表示モード"),
                _defaultViewModeComboBox,
                _restoreSessionCheckBox,
                applyButton
            }
        };
    }

    /// <summary>
    /// 適用済み設定を取得します。
    /// </summary>
    public AppSettings? ResultSettings { get; private set; }

    private static TextBlock BuildLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush((Color)Application.Current!.FindResource("TextPrimary")!)
        };
    }

    private AppSettings BuildSettings(AppSettings baseSettings)
    {
        var theme = _themeComboBox.SelectedIndex switch
        {
            1 => ThemePreference.Light,
            2 => ThemePreference.Dark,
            _ => ThemePreference.System
        };
        var language = _languageComboBox.SelectedIndex == 1 ? "en" : "ja";
        var defaultZoomText = _defaultZoomComboBox.SelectedItem?.ToString() ?? "100%";
        var defaultZoom = double.TryParse(defaultZoomText.TrimEnd('%'), out var percent)
            ? PdfiumRenderService.ClampZoomLevel(percent / 100d)
            : baseSettings.DefaultZoom;
        var defaultViewMode = _defaultViewModeComboBox.SelectedIndex switch
        {
            0 => ViewMode.SinglePage,
            2 => ViewMode.TwoPage,
            _ => ViewMode.Continuous
        };

        return baseSettings with
        {
            Theme = theme,
            Language = language,
            DefaultZoom = defaultZoom,
            DefaultViewMode = defaultViewMode,
            RestoreSessionOnStartup = _restoreSessionCheckBox.IsChecked == true
        };
    }
}
