#nullable enable

namespace AcroPDF.Core.Models;

/// <summary>
/// PDF ドキュメント内の 1 ページを表します。
/// </summary>
public sealed class PdfPage
{
    /// <summary>
    /// <see cref="PdfPage"/> の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="documentHandle">ネイティブのドキュメントハンドル。</param>
    /// <param name="pageIndex">0 始まりのページインデックス。</param>
    /// <param name="widthPt">ページ幅（ポイント）。</param>
    /// <param name="heightPt">ページ高さ（ポイント）。</param>
    public PdfPage(IntPtr documentHandle, int pageIndex, double widthPt, double heightPt)
    {
        DocumentHandle = documentHandle;
        PageIndex = pageIndex;
        WidthPt = widthPt;
        HeightPt = heightPt;
    }

    /// <summary>
    /// ネイティブのドキュメントハンドルを取得します。
    /// </summary>
    public IntPtr DocumentHandle { get; }

    /// <summary>
    /// 0 始まりのページインデックスを取得します。
    /// </summary>
    public int PageIndex { get; }

    /// <summary>
    /// 1 始まりのページ番号を取得します。
    /// </summary>
    public int PageNumber => PageIndex + 1;

    /// <summary>
    /// ページ幅（ポイント）を取得します。
    /// </summary>
    public double WidthPt { get; }

    /// <summary>
    /// ページ高さ（ポイント）を取得します。
    /// </summary>
    public double HeightPt { get; }

    /// <summary>
    /// ページの縦横比（高さ/幅）を取得します。
    /// </summary>
    public double AspectRatio => WidthPt <= 0 ? 0 : HeightPt / WidthPt;
}
