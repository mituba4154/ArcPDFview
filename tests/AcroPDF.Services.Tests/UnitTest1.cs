using AcroPDF.Services;

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
}
