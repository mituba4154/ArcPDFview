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

    private static PdfDocument CreateDocument(int pageCount)
    {
        var pages = Enumerable.Range(0, pageCount)
            .Select(index => new PdfPage(IntPtr.Zero, index, 595, 842))
            .ToArray();
        return new PdfDocument("/tmp/sample.pdf", IntPtr.Zero, pages, _ => { });
    }
}
