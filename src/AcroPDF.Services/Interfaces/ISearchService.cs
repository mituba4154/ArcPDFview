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
}
