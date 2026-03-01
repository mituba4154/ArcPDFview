using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AcroPDF.Services;
using AcroPDF.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AcroPDF.App;

/// <summary>
/// AcroPDF の Avalonia アプリケーション定義です。
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

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

            var mainWindow = ActivatorUtilities.CreateInstance<Views.MainWindow>(_serviceProvider);
            desktop.MainWindow = mainWindow;

            var startupFile = desktop.Args?.FirstOrDefault(static path => !string.IsNullOrWhiteSpace(path));
            if (!string.IsNullOrWhiteSpace(startupFile))
            {
                mainWindow.OpenFromStartupArgument(startupFile);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
