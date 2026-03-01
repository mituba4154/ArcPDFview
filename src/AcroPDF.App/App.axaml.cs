using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace AcroPDF.App;

/// <summary>
/// AcroPDF の Avalonia アプリケーション定義です。
/// </summary>
public partial class App : Application
{
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
            var mainWindow = new Views.MainWindow();
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
