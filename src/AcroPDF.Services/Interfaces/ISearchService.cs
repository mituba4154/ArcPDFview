#nullable enable

using AcroPDF.Core.Models;

namespace AcroPDF.Services.Interfaces;

/// <summary>
/// PDF テキスト検索機能を提供します。
/// </summary>
public interface ISearchService
{
    /// <summary>
    /// ドキュメント全体からテキスト検索を実行します。
    /// </summary>
    /// <param name="document">対象ドキュメント。</param>
    /// <param name="query">検索クエリ。</param>
    /// <param name="options">検索オプション。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>検索結果一覧。</returns>
    Task<IReadOnlyList<SearchResult>> SearchAsync(
        PdfDocument document,
        string query,
        SearchOptions options,
        CancellationToken ct = default);

    /// <summary>
    /// PDF の目次（ブックマーク）を取得します。
    /// </summary>
    /// <param name="document">対象ドキュメント。</param>
    /// <returns>ブックマーク一覧。</returns>
    IReadOnlyList<PdfBookmarkItem> GetBookmarks(PdfDocument document);

    /// <summary>
    /// 指定矩形内の文字列を抽出します。
    /// </summary>
    /// <param name="page">対象ページ。</param>
    /// <param name="selectionBounds">選択矩形。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>抽出結果。</returns>
    Task<TextSelectionResult> SelectTextAsync(PdfPage page, PdfTextBounds selectionBounds, CancellationToken ct = default);
}
