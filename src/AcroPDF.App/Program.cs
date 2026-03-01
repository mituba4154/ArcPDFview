using Avalonia;

namespace AcroPDF.App;

internal static class Program
{
    /// <summary>
    /// AcroPDF アプリケーションのエントリポイントです。
    /// </summary>
    /// <param name="args">起動引数。</param>
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Avalonia のアプリケーションビルダーを構成します。
    /// </summary>
    /// <returns>構成済みの <see cref="AppBuilder"/>。</returns>
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
