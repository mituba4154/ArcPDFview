#nullable enable

namespace AcroPDF.Core.Models;

/// <summary>
/// PDF 注釈の共通情報を表す基底クラスです。
/// </summary>
public abstract class Annotation
{
    /// <summary>
    /// 注釈 ID を取得します。
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// 対象ページ番号（1 始まり）を取得または設定します。
    /// </summary>
    public int PageNumber { get; set; }

    /// <summary>
    /// 注釈の PDF 座標矩形を取得または設定します。
    /// </summary>
    public PdfTextBounds Bounds { get; set; }

    /// <summary>
    /// 作成者名を取得または設定します。
    /// </summary>
    public string Author { get; set; } = Environment.UserName;

    /// <summary>
    /// 作成日時（UTC）を取得または設定します。
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 最終更新日時（UTC）を取得または設定します。
    /// </summary>
    public DateTime ModifiedAt { get; private set; } = DateTime.UtcNow;

    /// <summary>
    /// 任意コメントを取得または設定します。
    /// </summary>
    public string? Comment { get; set; }

    /// <summary>
    /// 更新時刻を現在時刻に更新します。
    /// </summary>
    public void Touch()
    {
        ModifiedAt = DateTime.UtcNow;
    }
}

