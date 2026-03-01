#nullable enable

namespace AcroPDF.Core.Models;

/// <summary>
/// PDF 上の矩形領域（ポイント座標）を表します。
/// </summary>
/// <param name="Left">左端 X。</param>
/// <param name="Top">上端 Y。</param>
/// <param name="Right">右端 X。</param>
/// <param name="Bottom">下端 Y。</param>
public readonly record struct PdfTextBounds(double Left, double Top, double Right, double Bottom)
{
    /// <summary>
    /// 矩形同士が交差しているかを判定します。
    /// </summary>
    /// <param name="other">比較対象。</param>
    /// <returns>交差している場合は <see langword="true"/>。</returns>
    public bool Intersects(PdfTextBounds other)
    {
        var left = Math.Min(Left, Right);
        var right = Math.Max(Left, Right);
        var top = Math.Max(Top, Bottom);
        var bottom = Math.Min(Top, Bottom);
        var otherLeft = Math.Min(other.Left, other.Right);
        var otherRight = Math.Max(other.Left, other.Right);
        var otherTop = Math.Max(other.Top, other.Bottom);
        var otherBottom = Math.Min(other.Top, other.Bottom);

        return !(right < otherLeft || otherRight < left || top < otherBottom || otherTop < bottom);
    }
}

/// <summary>
/// テキスト検索の 1 件分結果を表します。
/// </summary>
/// <param name="PageNumber">1 始まりのページ番号。</param>
/// <param name="Bounds">一致範囲の PDF 座標矩形。</param>
/// <param name="MatchIndex">一致結果の通し番号（0 始まり）。</param>
public readonly record struct SearchResult(int PageNumber, PdfTextBounds Bounds, int MatchIndex);

/// <summary>
/// テキスト検索オプションを表します。
/// </summary>
/// <param name="CaseSensitive">大文字小文字を区別する場合は <see langword="true"/>。</param>
/// <param name="UseRegex">正規表現検索を利用する場合は <see langword="true"/>。</param>
public readonly record struct SearchOptions(bool CaseSensitive, bool UseRegex);

/// <summary>
/// PDF ブックマーク 1 ノードを表します。
/// </summary>
/// <param name="Title">表示タイトル。</param>
/// <param name="PageNumber">遷移先ページ番号（1 始まり）。遷移先がない場合は 0。</param>
/// <param name="Children">子ノード。</param>
public sealed record PdfBookmarkItem(string Title, int PageNumber, IReadOnlyList<PdfBookmarkItem> Children);

/// <summary>
/// テキスト選択結果を表します。
/// </summary>
/// <param name="Text">選択テキスト。</param>
/// <param name="Bounds">選択範囲ハイライト矩形群。</param>
public sealed record TextSelectionResult(string Text, IReadOnlyList<PdfTextBounds> Bounds);
