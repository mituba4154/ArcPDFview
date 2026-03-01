using AcroPDF.Core.Models;

namespace AcroPDF.Core.Tests;

public class PdfModelTests
{
    [Fact]
    public void PdfPage_AspectRatio_IsCalculated()
    {
        var page = new PdfPage(IntPtr.Zero, 0, 200, 300);

        Assert.Equal(1.5d, page.AspectRatio);
        Assert.Equal(1, page.PageNumber);
    }

    [Fact]
    public void PdfDocument_Dispose_ReleasesHandleOnlyOnce()
    {
        var releaseCount = 0;
        var document = new PdfDocument(
            filePath: "/tmp/sample.pdf",
            nativeHandle: new IntPtr(123),
            pages: Array.Empty<PdfPage>(),
            releaseHandle: _ => releaseCount++);

        document.Dispose();
        document.Dispose();

        Assert.Equal(1, releaseCount);
    }

    [Fact]
    public void CommentAnnotation_Defaults_AreInitialized()
    {
        var annotation = new CommentAnnotation();

        Assert.NotEqual(Guid.Empty, annotation.Id);
        Assert.Equal(Environment.UserName, annotation.Author);
        Assert.False(annotation.IsOpen);
    }

    [Fact]
    public void PdfDocument_AddAnnotation_MarksModified()
    {
        var document = new PdfDocument(
            filePath: "/tmp/sample.pdf",
            nativeHandle: IntPtr.Zero,
            pages: Array.Empty<PdfPage>(),
            releaseHandle: null);

        document.AddAnnotation(new HighlightAnnotation());

        Assert.True(document.IsModified);
        Assert.Single(document.Annotations);
    }
}
