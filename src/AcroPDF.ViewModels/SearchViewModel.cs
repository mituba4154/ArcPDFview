#nullable enable

using AcroPDF.Core.Models;
using AcroPDF.Services.Interfaces;

namespace AcroPDF.ViewModels;

/// <summary>
/// 検索実行ロジックを提供する ViewModel です。
/// </summary>
public sealed class SearchViewModel
{
    private readonly ISearchService _searchService;

    /// <summary>
    /// <see cref="SearchViewModel"/> の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="searchService">検索サービス。</param>
    public SearchViewModel(ISearchService searchService)
    {
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
    }

    /// <summary>
    /// 検索を実行します。
    /// </summary>
    /// <param name="document">対象ドキュメント。</param>
    /// <param name="query">検索文字列。</param>
    /// <param name="options">検索オプション。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>検索結果一覧。</returns>
    public Task<IReadOnlyList<SearchResult>> SearchAsync(
        PdfDocument document,
        string query,
        SearchOptions options,
        CancellationToken ct = default)
    {
        return _searchService.SearchAsync(document, query, options, ct);
    }
}
