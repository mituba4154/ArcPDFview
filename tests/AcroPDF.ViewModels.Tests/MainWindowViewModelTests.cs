using AcroPDF.Core.Models;
using AcroPDF.ViewModels;

namespace AcroPDF.ViewModels.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void SplitProperties_CanBeAssigned()
    {
        using var tab = CreateTab(1);
        var vm = new MainWindowViewModel
        {
            ActiveTab = tab,
            IsSplitView = true,
            SplitSecondaryTab = tab
        };

        Assert.True(vm.IsSplitView);
        Assert.Same(tab, vm.ActiveTab);
        Assert.Same(tab, vm.SplitSecondaryTab);
    }

    [Fact]
    public void AddAndRemoveTab_UpdatesCollection()
    {
        using var tab = CreateTab(1);
        var vm = new MainWindowViewModel();

        vm.AddTab(tab);
        var removed = vm.RemoveTab(tab);

        Assert.True(removed);
        Assert.Empty(vm.Tabs);
    }

    private static TabViewModel CreateTab(int pageCount)
    {
        var pages = Enumerable.Range(0, pageCount)
            .Select(index => new PdfPage(IntPtr.Zero, index, 595, 842))
            .ToArray();
        var document = new PdfDocument("/tmp/sample.pdf", IntPtr.Zero, pages, _ => { });
        return new TabViewModel(document);
    }
}
