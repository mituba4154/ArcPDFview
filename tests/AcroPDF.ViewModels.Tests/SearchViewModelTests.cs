using AcroPDF.Core.Models;
using AcroPDF.Services.Interfaces;
using AcroPDF.ViewModels;

namespace AcroPDF.ViewModels.Tests;

public sealed class SearchViewModelTests
{
    [Fact]
    public async Task SearchAsync_DelegatesToService_WithQueryAndOptions()
    {
        using var document = new PdfDocument("/tmp/sample.pdf", IntPtr.Zero, [], _ => { });
        var expected = new[] { new SearchResult(2, new PdfTextBounds(1, 2, 3, 1), 0) };
        var fakeService = new FakeSearchService(expected);
        var vm = new SearchViewModel(fakeService);

        var results = await vm.SearchAsync(document, "abc", new SearchOptions(CaseSensitive: true, UseRegex: true));

        Assert.Same(expected, results);
        Assert.Equal("abc", fakeService.LastQuery);
        Assert.Equal(new SearchOptions(true, true), fakeService.LastOptions);
    }

    private sealed class FakeSearchService : ISearchService
    {
        private readonly IReadOnlyList<SearchResult> _results;

        public FakeSearchService(IReadOnlyList<SearchResult> results)
        {
            _results = results;
        }

        public string LastQuery { get; private set; } = string.Empty;

        public SearchOptions LastOptions { get; private set; }

        public Task<IReadOnlyList<SearchResult>> SearchAsync(PdfDocument document, string query, SearchOptions options, CancellationToken ct = default)
        {
            LastQuery = query;
            LastOptions = options;
            return Task.FromResult(_results);
        }

        public IReadOnlyList<PdfBookmarkItem> GetBookmarks(PdfDocument document)
        {
            return [];
        }

        public Task<TextSelectionResult> SelectTextAsync(PdfPage page, PdfTextBounds selectionBounds, CancellationToken ct = default)
        {
            return Task.FromResult(new TextSelectionResult(string.Empty, []));
        }
    }
}
