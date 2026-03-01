using AcroPDF.Services;
using AcroPDF.Core.Models;
using System.Reflection;

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

    [Fact]
    public async Task AnnotationService_ExportAndImportFdf_RoundTripsSupportedAnnotations()
    {
        var service = new AnnotationService();
        using var source = new PdfDocument("sample.pdf", IntPtr.Zero, [], _ => { });
        source.AddAnnotation(new HighlightAnnotation
        {
            PageNumber = 1,
            Bounds = new PdfTextBounds(10, 20, 30, 5),
            Type = HighlightType.Highlight,
            Color = HighlightColor.Blue
        });
        source.AddAnnotation(new FreehandAnnotation
        {
            PageNumber = 2,
            Bounds = new PdfTextBounds(5, 30, 40, 1),
            StrokeColorHex = "#00c878",
            StrokeWidth = 3d,
            Strokes = [[new AnnotationPoint(5, 5), new AnnotationPoint(10, 8)]]
        });
        source.ClearModified();

        var path = Path.Combine(Path.GetTempPath(), $"acropdf-{Guid.NewGuid():N}.fdf");
        try
        {
            await service.ExportAsFdfAsync(source, path);

            using var imported = new PdfDocument("target.pdf", IntPtr.Zero, [], _ => { });
            await service.ImportFdfAsync(imported, path);

            Assert.Equal(2, imported.Annotations.Count);
            Assert.True(imported.Annotations.OfType<HighlightAnnotation>().Any());
            Assert.True(imported.Annotations.OfType<FreehandAnnotation>().Any());
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void ExpandToPdfiumCharEvents_BmpCharacter_ReturnsSingleCodeUnit()
    {
        var actual = InvokeExpandToPdfiumCharEvents("A");

        Assert.Equal([(int)'A'], actual);
    }

    [Fact]
    public void ExpandToPdfiumCharEvents_SupplementaryCharacter_ReturnsSurrogatePair()
    {
        var actual = InvokeExpandToPdfiumCharEvents("😀");

        Assert.Equal([0xD83D, 0xDE00], actual);
    }

    private static int[] InvokeExpandToPdfiumCharEvents(string text)
    {
        var method = typeof(PdfiumRenderService).GetMethod(
            "ExpandToPdfiumCharEvents",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var values = method!.Invoke(null, [text]) as IEnumerable<int>;
        Assert.NotNull(values);
        return values!.ToArray();
    }
}
