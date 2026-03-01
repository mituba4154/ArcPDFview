#nullable enable

using System.Runtime.InteropServices;
using System.Text.Json;
using AcroPDF.Core.Models;
using AcroPDF.Services.Interfaces;

namespace AcroPDF.Services;

/// <summary>
/// JSON ファイルによる設定永続化サービスです。
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>
    /// 設定ファイルの絶対パスを取得します。
    /// </summary>
    public string SettingsFilePath => GetSettingsFilePath();

    /// <inheritdoc />
    public AppSettings Load()
    {
        var path = SettingsFilePath;
        if (!File.Exists(path))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    /// <inheritdoc />
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var path = SettingsFilePath;
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(path, json);
    }

    /// <inheritdoc />
    public void AddRecentFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        var fullPath = Path.GetFullPath(filePath);
        var settings = Load();
        var list = settings.RecentFiles
            .Where(path => !string.Equals(path, fullPath, StringComparison.OrdinalIgnoreCase))
            .Prepend(fullPath)
            .Take(Math.Max(1, settings.MaxRecentFiles))
            .ToArray();
        Save(settings with { RecentFiles = list });
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetRecentFiles()
    {
        var settings = Load();
        return settings.RecentFiles
            .Where(File.Exists)
            .Take(Math.Max(1, settings.MaxRecentFiles))
            .ToArray();
    }

    /// <inheritdoc />
    public void RemoveRecentFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        var settings = Load();
        var list = settings.RecentFiles
            .Where(path => !string.Equals(path, filePath, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Save(settings with { RecentFiles = list });
    }

    /// <inheritdoc />
    public void SaveSession(IReadOnlyList<SessionEntry> sessionEntries)
    {
        ArgumentNullException.ThrowIfNull(sessionEntries);
        var settings = Load();
        Save(settings with { LastSession = sessionEntries });
    }

    /// <inheritdoc />
    public IReadOnlyList<SessionEntry> LoadSession()
    {
        return Load().LastSession;
    }

    private static string GetSettingsFilePath()
    {
        string baseDirectory;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            baseDirectory = Path.Combine(home, ".config");
        }

        return Path.Combine(baseDirectory, "ArcPDFview", "settings.json");
    }
}
