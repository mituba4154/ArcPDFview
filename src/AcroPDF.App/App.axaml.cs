using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using AcroPDF.Core.Models;
using AcroPDF.Services;
using AcroPDF.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AcroPDF.App;

/// <summary>
/// AcroPDF の Avalonia アプリケーション定義です。
/// </summary>
public partial class App : Application
{
    private static readonly Stopwatch StartupStopwatch = Stopwatch.StartNew();
    private ServiceProvider? _serviceProvider;
    private ISettingsService? _settingsService;
    private ThemePreference _themePreference = ThemePreference.System;

    /// <summary>
    /// アプリケーションのサービスプロバイダーを取得します。
    /// </summary>
    public static IServiceProvider? Services { get; private set; }

    /// <summary>
    /// アプリケーションの XAML リソースを読み込みます。
    /// </summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// デスクトップライフタイムのメインウィンドウを初期化します。
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IPdfRenderService, PdfiumRenderService>();
            services.AddSingleton<IAnnotationService, AnnotationService>();
            services.AddSingleton<ISearchService, SearchService>();
            services.AddSingleton<ISettingsService, SettingsService>();
            _serviceProvider = services.BuildServiceProvider();
            Services = _serviceProvider;
            _settingsService = _serviceProvider.GetRequiredService<ISettingsService>();
            _themePreference = _settingsService.Load().Theme;
            ApplyThemePreference();
            if (PlatformSettings is not null)
            {
                PlatformSettings.ColorValuesChanged += OnPlatformColorValuesChanged;
            }

            var mainWindow = ActivatorUtilities.CreateInstance<Views.MainWindow>(_serviceProvider);
            desktop.MainWindow = mainWindow;
            desktop.Exit += (_, _) => _serviceProvider?.Dispose();

            var startupFile = desktop.Args?.FirstOrDefault(static path => !string.IsNullOrWhiteSpace(path));
            if (!string.IsNullOrWhiteSpace(startupFile))
            {
                mainWindow.OpenFromStartupArgument(startupFile);
            }

            StartupStopwatch.Stop();
            Trace.TraceInformation($"Startup elapsed: {StartupStopwatch.ElapsedMilliseconds}ms");
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// テーマ設定を更新します。
    /// </summary>
    /// <param name="preference">更新するテーマ設定。</param>
    public void SetThemePreference(ThemePreference preference)
    {
        _themePreference = preference;
        ApplyThemePreference();
    }

    private void OnPlatformColorValuesChanged(object? sender, PlatformColorValues e)
    {
        if (_themePreference == ThemePreference.System)
        {
            ApplyThemePreference();
        }
    }

    private void ApplyThemePreference()
    {
        var isHighContrast = PlatformSettings?.GetColorValues()?.ContrastPreference == ColorContrastPreference.High;
        RequestedThemeVariant = _themePreference switch
        {
            ThemePreference.Light => ThemeVariant.Light,
            ThemePreference.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

        if (isHighContrast)
        {
            Resources["BgDark"] = Color.Parse("#000000");
            Resources["BgPanel"] = Color.Parse("#000000");
            Resources["BgPanel2"] = Color.Parse("#000000");
            Resources["BgToolbar"] = Color.Parse("#000000");
            Resources["TextPrimary"] = Color.Parse("#ffffff");
            Resources["TextSecondary"] = Color.Parse("#ffffff");
            Resources["TextDim"] = Color.Parse("#ffffff");
            Resources["Border"] = Color.Parse("#ffffff");
            Resources["BorderLight"] = Color.Parse("#ffff00");
            Resources["Accent"] = Color.Parse("#ffff00");
            Resources["Accent2"] = Color.Parse("#00ffff");
            Resources["BgTabActive"] = Color.Parse("#000000");
            Resources["BgTabInactive"] = Color.Parse("#111111");
            return;
        }

        var useLightPalette = _themePreference == ThemePreference.Light ||
            (_themePreference == ThemePreference.System && ActualThemeVariant == ThemeVariant.Light);
        if (useLightPalette)
        {
            Resources["BgDark"] = Color.Parse("#f5f5f5");
            Resources["BgPanel"] = Color.Parse("#ffffff");
            Resources["BgPanel2"] = Color.Parse("#f0f0f0");
            Resources["BgToolbar"] = Color.Parse("#e8e8e8");
            Resources["TextPrimary"] = Color.Parse("#1a1a1a");
            Resources["TextSecondary"] = Color.Parse("#555555");
            Resources["TextDim"] = Color.Parse("#666666");
            Resources["Border"] = Color.Parse("#d0d0d0");
            Resources["BorderLight"] = Color.Parse("#e0e0e0");
            Resources["BgTabActive"] = Color.Parse("#ffffff");
            Resources["BgTabInactive"] = Color.Parse("#ececec");
        }
        else
        {
            Resources["BgDark"] = Color.Parse("#1a1a1a");
            Resources["BgPanel"] = Color.Parse("#252525");
            Resources["BgPanel2"] = Color.Parse("#2e2e2e");
            Resources["BgToolbar"] = Color.Parse("#323232");
            Resources["TextPrimary"] = Color.Parse("#e8e8e8");
            Resources["TextSecondary"] = Color.Parse("#999999");
            Resources["TextDim"] = Color.Parse("#666666");
            Resources["Border"] = Color.Parse("#3a3a3a");
            Resources["BorderLight"] = Color.Parse("#444444");
            Resources["BgTabActive"] = Color.Parse("#1a1a1a");
            Resources["BgTabInactive"] = Color.Parse("#2a2a2a");
        }
    }
}
