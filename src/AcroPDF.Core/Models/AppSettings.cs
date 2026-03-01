#nullable enable

namespace AcroPDF.Core.Models;

/// <summary>
/// 既定のページ表示モードを表します。
/// </summary>
public enum ViewMode
{
    /// <summary>
    /// 単ページ表示です。
    /// </summary>
    SinglePage = 0,

    /// <summary>
    /// 連続スクロール表示です。
    /// </summary>
    Continuous = 1,

    /// <summary>
    /// 2ページ見開き表示です。
    /// </summary>
    TwoPage = 2
}

/// <summary>
/// セッション復元用のタブ情報を表します。
/// </summary>
/// <param name="FilePath">PDF ファイルパス。</param>
/// <param name="PageNumber">復元ページ番号（1 始まり）。</param>
public sealed record SessionEntry(string FilePath, int PageNumber);

/// <summary>
/// アプリケーション設定を表します。
/// </summary>
public sealed record AppSettings
{
    /// <summary>
    /// 既定ズーム倍率（1.0 = 100%）を取得します。
    /// </summary>
    public double DefaultZoom { get; init; } = 1.0d;

    /// <summary>
    /// 既定表示モードを取得します。
    /// </summary>
    public ViewMode DefaultViewMode { get; init; } = ViewMode.Continuous;

    /// <summary>
    /// 起動時にセッションを復元するかどうかを取得します。
    /// </summary>
    public bool RestoreSessionOnStartup { get; init; } = true;

    /// <summary>
    /// 最近のファイル保持上限を取得します。
    /// </summary>
    public int MaxRecentFiles { get; init; } = 20;

    /// <summary>
    /// 既定ハイライト色（HEX 文字列）を取得します。
    /// </summary>
    public string DefaultHighlightColor { get; init; } = "#FFFF00";

    /// <summary>
    /// 既定注釈作成者名を取得します。
    /// </summary>
    public string DefaultAnnotationAuthor { get; init; } = Environment.UserName;

    /// <summary>
    /// ページキャッシュ数を取得します。
    /// </summary>
    public int PageCacheSize { get; init; } = 10;

    /// <summary>
    /// レンダリング DPI 乗数を取得します。
    /// </summary>
    public double RenderDpiMultiplier { get; init; } = 1.0d;

    /// <summary>
    /// 左サイドバー幅を取得します。
    /// </summary>
    public double LeftSidebarWidth { get; init; } = 220d;

    /// <summary>
    /// 右サイドバー幅を取得します。
    /// </summary>
    public double RightSidebarWidth { get; init; } = 240d;

    /// <summary>
    /// ステータスバー表示有無を取得します。
    /// </summary>
    public bool ShowStatusBar { get; init; } = true;

    /// <summary>
    /// UI 言語コードを取得します。
    /// </summary>
    public string Language { get; init; } = "ja";

    /// <summary>
    /// 最近開いたファイル一覧を取得します。
    /// </summary>
    public IReadOnlyList<string> RecentFiles { get; init; } = [];

    /// <summary>
    /// 前回終了時セッション一覧を取得します。
    /// </summary>
    public IReadOnlyList<SessionEntry> LastSession { get; init; } = [];
}
