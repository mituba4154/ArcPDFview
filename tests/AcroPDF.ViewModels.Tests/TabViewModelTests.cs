using AcroPDF.Core.Models;
using AcroPDF.ViewModels;

namespace AcroPDF.ViewModels.Tests;

public class TabViewModelTests
{
    [Fact]
    public void NavigationCommands_ClampWithinPageRange()
    {
        using var tab = new TabViewModel(CreateDocument(3));

        tab.MovePreviousPageCommand.Execute(null);
        Assert.Equal(1, tab.CurrentPage);

        tab.MoveLastPageCommand.Execute(null);
        Assert.Equal(3, tab.CurrentPage);

        tab.MoveNextPageCommand.Execute(null);
        Assert.Equal(3, tab.CurrentPage);
    }

    [Fact]
    public void JumpFromInput_UsesCurrentPageOnInvalidInput()
    {
        using var tab = new TabViewModel(CreateDocument(10));
        tab.CurrentPage = 4;
        tab.PageInputText = "abc";

        tab.JumpFromInput();

        Assert.Equal(4, tab.CurrentPage);
        Assert.Equal("4", tab.PageInputText);
    }

    [Fact]
    public void FitCommands_ChangeZoomLevelWithinAllowedRange()
    {
        using var tab = new TabViewModel(CreateDocument(1));

        tab.FitToWidth(1280);
        var widthFit = tab.ZoomLevel;
        tab.FitToPage(400, 500);
        var pageFit = tab.ZoomLevel;

        Assert.InRange(widthFit, 0.25, 4.0);
        Assert.InRange(pageFit, 0.25, 4.0);
    }

    [Fact]
    public void CurrentPageChange_UpdatesThumbnailSelection()
    {
        using var tab = new TabViewModel(CreateDocument(3));

        tab.JumpToPage(2);

        Assert.False(tab.Thumbnails[0].IsSelected);
        Assert.True(tab.Thumbnails[1].IsSelected);
        Assert.False(tab.Thumbnails[2].IsSelected);
    }

    [Fact]
    public void SetSearchResults_SetsCurrentResultAndPage()
    {
        using var tab = new TabViewModel(CreateDocument(5));
        var results = new[]
        {
            new SearchResult(3, new PdfTextBounds(10, 20, 30, 10), 0),
            new SearchResult(5, new PdfTextBounds(10, 20, 30, 10), 1)
        };

        tab.SetSearchResults(results);

        Assert.Equal(0, tab.CurrentSearchResultIndex);
        Assert.Equal(3, tab.CurrentPage);
    }

    [Fact]
    public void MoveToNextSearchResult_WrapsAround()
    {
        using var tab = new TabViewModel(CreateDocument(5));
        tab.SetSearchResults(
        [
            new SearchResult(2, new PdfTextBounds(1, 2, 3, 1), 0),
            new SearchResult(4, new PdfTextBounds(1, 2, 3, 1), 1)
        ]);

        tab.MoveToNextSearchResult();
        Assert.Equal(1, tab.CurrentSearchResultIndex);
        Assert.Equal(4, tab.CurrentPage);

        tab.MoveToNextSearchResult();
        Assert.Equal(0, tab.CurrentSearchResultIndex);
        Assert.Equal(2, tab.CurrentPage);
    }

    [Fact]
    public void TwoPageModeCommand_SetsExpectedFlags()
    {
        using var tab = new TabViewModel(CreateDocument(5));

        tab.SetTwoPageModeCommand.Execute(null);

        Assert.True(tab.IsTwoPageMode);
        Assert.False(tab.IsContinuousMode);
    }

    [Fact]
    public void RotationDegrees_Setter_DoesNotNormalize()
    {
        using var tab = new TabViewModel(CreateDocument(1));

        tab.RotationDegrees = 450;

        Assert.Equal(450, tab.RotationDegrees);
    }

    [Fact]
    public void RotateClockwiseCommand_NormalizesToRightAngle()
    {
        using var tab = new TabViewModel(CreateDocument(1));
        tab.RotationDegrees = 270;

        tab.RotateClockwiseCommand.Execute(null);

        Assert.Equal(0, tab.RotationDegrees);
    }

    [Fact]
    public void ResolvePrintPages_UsesPrintOptionsRange()
    {
        using var tab = new TabViewModel(CreateDocument(8));
        tab.CurrentPage = 4;
        tab.PrintOptions.RangeMode = PrintPageRangeMode.CurrentPage;
        Assert.Equal(new[] { 4 }, tab.ResolvePrintPages());

        tab.PrintOptions.RangeMode = PrintPageRangeMode.PageRange;
        tab.PrintOptions.RangeStartPage = 6;
        tab.PrintOptions.RangeEndPage = 7;
        Assert.Equal(new[] { 6, 7 }, tab.ResolvePrintPages());
    }

    private static PdfDocument CreateDocument(int pageCount)
    {
        var pages = Enumerable.Range(0, pageCount)
            .Select(index => new PdfPage(IntPtr.Zero, index, 595, 842))
            .ToArray();
        return new PdfDocument("/tmp/sample.pdf", IntPtr.Zero, pages, _ => { });
    }
}
