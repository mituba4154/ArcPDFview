using AcroPDF.Services;
using AcroPDF.Core.Models;

namespace AcroPDF.Services.Tests;

public class PdfiumRenderServiceTests
{
    [Theory]
    [InlineData(0.1, 0.25)]
    [InlineData(1.0, 1.0)]
    [InlineData(5.0, 4.0)]
    public void ClampZoomLevel_ClampsToExpectedRange(double input, double expected)
    {
        var actual = PdfiumRenderService.ClampZoomLevel(input);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsEmpty()
    {
        var service = new SearchService();
        using var document = new PdfDocument("sample.pdf", IntPtr.Zero, [], _ => { });

        var result = await service.SearchAsync(document, string.Empty, new SearchOptions(false, false));

        Assert.Empty(result);
    }

    [Fact]
    public void AnnotationService_CoordinateConversion_RoundTrips()
    {
        var service = new AnnotationService();
        var screen = service.ConvertPdfToScreen(100, 200, dpiScale: 2, pageHeightPt: 800);
        var pdf = service.ConvertScreenToPdf(screen.X, screen.Y, dpiScale: 2, pageHeightPt: 800);

        Assert.Equal(100, pdf.X, precision: 6);
        Assert.Equal(200, pdf.Y, precision: 6);
    }
}
