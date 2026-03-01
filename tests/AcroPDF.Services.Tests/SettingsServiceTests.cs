using AcroPDF.Core.Models;
using AcroPDF.Services;

namespace AcroPDF.Services.Tests;

public sealed class SettingsServiceTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsSettings()
    {
        using var scope = new SettingsEnvironmentScope();
        var service = new SettingsService();
        var settings = new AppSettings
        {
            DefaultZoom = 1.25d,
            Theme = ThemePreference.Dark,
            RecentFiles = ["/tmp/a.pdf"],
            LastSession = [new SessionEntry("/tmp/a.pdf", 3)]
        };

        service.Save(settings);
        var loaded = service.Load();

        Assert.Equal(1.25d, loaded.DefaultZoom);
        Assert.Equal(ThemePreference.Dark, loaded.Theme);
        Assert.Single(loaded.RecentFiles);
        Assert.Single(loaded.LastSession);
        Assert.Equal(3, loaded.LastSession[0].PageNumber);
    }

    [Fact]
    public void AddRecentFile_KeepsMostRecentAndLimitedCount()
    {
        using var scope = new SettingsEnvironmentScope();
        var service = new SettingsService();
        for (var index = 0; index < 25; index++)
        {
            service.AddRecentFile($"/tmp/sample-{index}.pdf");
        }

        var recent = service.Load().RecentFiles;
        Assert.Equal(20, recent.Count);
        Assert.Contains("sample-24.pdf", recent[0], StringComparison.Ordinal);
    }

    [Fact]
    public void SaveSession_AndLoadSession_RoundTripsEntries()
    {
        using var scope = new SettingsEnvironmentScope();
        var service = new SettingsService();
        var session = new[]
        {
            new SessionEntry("/tmp/a.pdf", 2),
            new SessionEntry("/tmp/b.pdf", 5)
        };

        service.SaveSession(session);
        var loaded = service.LoadSession();

        Assert.Equal(2, loaded.Count);
        Assert.Equal("/tmp/a.pdf", loaded[0].FilePath);
        Assert.Equal(5, loaded[1].PageNumber);
    }

    private sealed class SettingsEnvironmentScope : IDisposable
    {
        private readonly string? _oldAppData = Environment.GetEnvironmentVariable("APPDATA");
        private readonly string? _oldHome = Environment.GetEnvironmentVariable("HOME");
        private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"arcpdf-settings-{Guid.NewGuid():N}");

        public SettingsEnvironmentScope()
        {
            Directory.CreateDirectory(_tempDirectory);
            Environment.SetEnvironmentVariable("APPDATA", _tempDirectory);
            Environment.SetEnvironmentVariable("HOME", _tempDirectory);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("APPDATA", _oldAppData);
            Environment.SetEnvironmentVariable("HOME", _oldHome);
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
    }
}
