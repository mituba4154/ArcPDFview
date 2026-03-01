#nullable enable

namespace AcroPDF.Core.Models;

/// <summary>
/// 印刷ページ範囲の指定方法を表します。
/// </summary>
public enum PrintPageRangeMode
{
    /// <summary>
    /// 全ページを印刷します。
    /// </summary>
    AllPages,

    /// <summary>
    /// 現在ページのみを印刷します。
    /// </summary>
    CurrentPage,

    /// <summary>
    /// ページ範囲を指定して印刷します。
    /// </summary>
    PageRange
}

/// <summary>
/// 印刷オプションを表します。
/// </summary>
public sealed class PrintOptions
{
    /// <summary>
    /// ページ範囲指定方法を取得または設定します。
    /// </summary>
    public PrintPageRangeMode RangeMode { get; set; } = PrintPageRangeMode.AllPages;

    /// <summary>
    /// 1 始まりの現在ページ番号を取得または設定します。
    /// </summary>
    public int CurrentPage { get; set; } = 1;

    /// <summary>
    /// ページ範囲開始（1 始まり）を取得または設定します。
    /// </summary>
    public int RangeStartPage { get; set; } = 1;

    /// <summary>
    /// ページ範囲終了（1 始まり）を取得または設定します。
    /// </summary>
    public int RangeEndPage { get; set; } = 1;

    /// <summary>
    /// 印刷部数を取得または設定します。
    /// </summary>
    public int Copies { get; set; } = 1;

    /// <summary>
    /// 用紙名を取得または設定します。
    /// </summary>
    public string PaperSizeName { get; set; } = "A4";

    /// <summary>
    /// 横向き印刷かどうかを取得または設定します。
    /// </summary>
    public bool IsLandscape { get; set; }

    /// <summary>
    /// 指定オプションで印刷対象のページ番号一覧を返します。
    /// </summary>
    /// <param name="pageCount">総ページ数。</param>
    /// <returns>印刷対象ページ番号一覧（1 始まり）。</returns>
    public IReadOnlyList<int> ResolvePageNumbers(int pageCount)
    {
        var safePageCount = Math.Max(0, pageCount);
        if (safePageCount == 0)
        {
            return [];
        }

        return RangeMode switch
        {
            PrintPageRangeMode.CurrentPage => [Math.Clamp(CurrentPage, 1, safePageCount)],
            PrintPageRangeMode.PageRange => BuildRange(safePageCount),
            _ => Enumerable.Range(1, safePageCount).ToArray()
        };
    }

    private IReadOnlyList<int> BuildRange(int pageCount)
    {
        var start = Math.Clamp(RangeStartPage, 1, pageCount);
        var end = Math.Clamp(RangeEndPage, 1, pageCount);
        if (end < start)
        {
            (start, end) = (end, start);
        }

        return Enumerable.Range(start, end - start + 1).ToArray();
    }
}
