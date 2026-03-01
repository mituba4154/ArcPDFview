#nullable enable

using System.Runtime.InteropServices;
using System.Text;
using AcroPDF.Core.Models;
using AcroPDF.Services.Interfaces;

namespace AcroPDF.Services;

/// <summary>
/// PDFium を利用した注釈サービスです。
/// </summary>
public sealed class AnnotationService : IAnnotationService
{
    private const int AnnotSubtypeText = 1;
    private const int AnnotSubtypeHighlight = 9;
    private const int AnnotSubtypeUnderline = 10;
    private const int AnnotSubtypeStrikeOut = 13;
    private const int AnnotColorTypeNormal = 0;
    private const int SaveFlagNoIncremental = 1;
    private const uint CommentColorR = 48;
    private const uint CommentColorG = 112;
    private const uint CommentColorB = 255;
    private const uint CommentColorA = 255;

    /// <inheritdoc />
    public Task<IReadOnlyList<Annotation>> LoadAnnotationsAsync(PdfDocument document, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        return Task.Run<IReadOnlyList<Annotation>>(() =>
        {
            var annotations = new List<Annotation>();
            foreach (var page in document.Pages)
            {
                ct.ThrowIfCancellationRequested();
                var pageHandle = NativeMethods.FPDF_LoadPage(document.NativeHandle, page.PageIndex);
                if (pageHandle == IntPtr.Zero)
                {
                    continue;
                }

                try
                {
                    var annotCount = NativeMethods.FPDFPage_GetAnnotCount(pageHandle);
                    for (var index = 0; index < annotCount; index++)
                    {
                        ct.ThrowIfCancellationRequested();
                        var annotHandle = NativeMethods.FPDFPage_GetAnnot(pageHandle, index);
                        if (annotHandle == IntPtr.Zero)
                        {
                            continue;
                        }

                        try
                        {
                            if (!TryReadAnnotation(annotHandle, page.PageNumber, out var annotation))
                            {
                                continue;
                            }

                            annotations.Add(annotation);
                        }
                        finally
                        {
                            NativeMethods.FPDFPage_CloseAnnot(annotHandle);
                        }
                    }
                }
                finally
                {
                    NativeMethods.FPDF_ClosePage(pageHandle);
                }
            }

            return (IReadOnlyList<Annotation>)annotations;
        }, ct);
    }

    /// <inheritdoc />
    public Task SaveAnnotationsAsync(PdfDocument document, string? outputPath = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            foreach (var page in document.Pages)
            {
                ct.ThrowIfCancellationRequested();
                var pageHandle = NativeMethods.FPDF_LoadPage(document.NativeHandle, page.PageIndex);
                if (pageHandle == IntPtr.Zero)
                {
                    continue;
                }

                try
                {
                    RemoveAllAnnotations(pageHandle);
                    var pageAnnotations = document.Annotations
                        .Where(annotation => annotation.PageNumber == page.PageNumber)
                        .ToArray();
                    foreach (var annotation in pageAnnotations)
                    {
                        ct.ThrowIfCancellationRequested();
                        AddAnnotationToPage(pageHandle, annotation);
                    }
                }
                finally
                {
                    NativeMethods.FPDF_ClosePage(pageHandle);
                }
            }

            var destination = string.IsNullOrWhiteSpace(outputPath) ? document.FilePath : outputPath;
            SaveDocumentCopy(document.NativeHandle, destination!);
            document.ClearModified();
        }, ct);
    }

    /// <inheritdoc />
    public AnnotationPoint ConvertScreenToPdf(double screenX, double screenY, double dpiScale, double pageHeightPt)
    {
        var scale = Math.Max(0.0001d, dpiScale);
        var pdfX = screenX / scale;
        var pdfY = pageHeightPt - (screenY / scale);
        return new AnnotationPoint(pdfX, pdfY);
    }

    /// <inheritdoc />
    public AnnotationPoint ConvertPdfToScreen(double pdfX, double pdfY, double dpiScale, double pageHeightPt)
    {
        var scale = Math.Max(0.0001d, dpiScale);
        var screenX = pdfX * scale;
        var screenY = (pageHeightPt - pdfY) * scale;
        return new AnnotationPoint(screenX, screenY);
    }

    private static void RemoveAllAnnotations(IntPtr pageHandle)
    {
        var count = NativeMethods.FPDFPage_GetAnnotCount(pageHandle);
        for (var index = count - 1; index >= 0; index--)
        {
            NativeMethods.FPDFPage_RemoveAnnot(pageHandle, index);
        }
    }

    private static bool TryReadAnnotation(IntPtr annotHandle, int pageNumber, out Annotation annotation)
    {
        annotation = default!;
        var subtype = NativeMethods.FPDFAnnot_GetSubtype(annotHandle);
        if (!TryGetRect(annotHandle, out var rect))
        {
            return false;
        }

        var bounds = new PdfTextBounds(rect.Left, rect.Top, rect.Right, rect.Bottom);
        switch (subtype)
        {
            case AnnotSubtypeText:
                annotation = new CommentAnnotation
                {
                    PageNumber = pageNumber,
                    Bounds = bounds,
                    Text = GetAnnotationString(annotHandle, "Contents"),
                    Author = GetAnnotationString(annotHandle, "T"),
                    IsOpen = false
                };
                return true;
            case AnnotSubtypeHighlight:
            case AnnotSubtypeUnderline:
            case AnnotSubtypeStrikeOut:
                annotation = new HighlightAnnotation
                {
                    PageNumber = pageNumber,
                    Bounds = bounds,
                    Type = MapSubtypeToHighlightType(subtype),
                    Color = GetHighlightColor(annotHandle)
                };
                return true;
            default:
                return false;
        }
    }

    private static void AddAnnotationToPage(IntPtr pageHandle, Annotation annotation)
    {
        var subtype = annotation switch
        {
            CommentAnnotation => AnnotSubtypeText,
            HighlightAnnotation highlight => MapHighlightTypeToSubtype(highlight.Type),
            _ => -1
        };
        if (subtype < 0)
        {
            return;
        }

        var annotHandle = NativeMethods.FPDFPage_CreateAnnot(pageHandle, subtype);
        if (annotHandle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var bounds = annotation.Bounds;
            var rect = new FS_RECTF
            {
                Left = (float)Math.Min(bounds.Left, bounds.Right),
                Right = (float)Math.Max(bounds.Left, bounds.Right),
                Bottom = (float)Math.Min(bounds.Bottom, bounds.Top),
                Top = (float)Math.Max(bounds.Bottom, bounds.Top)
            };
            NativeMethods.FPDFAnnot_SetRect(annotHandle, ref rect);

            if (annotation is CommentAnnotation comment)
            {
                SetAnnotationString(annotHandle, "Contents", comment.Text);
                SetAnnotationString(annotHandle, "T", comment.Author);
                NativeMethods.FPDFAnnot_SetColor(annotHandle, AnnotColorTypeNormal, CommentColorR, CommentColorG, CommentColorB, CommentColorA);
            }
            else if (annotation is HighlightAnnotation highlight)
            {
                var color = MapHighlightColor(highlight.Color);
                NativeMethods.FPDFAnnot_SetColor(annotHandle, AnnotColorTypeNormal, color.R, color.G, color.B, 140);
            }
        }
        finally
        {
            NativeMethods.FPDFPage_CloseAnnot(annotHandle);
        }
    }

    private static void SaveDocumentCopy(IntPtr documentHandle, string outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"acropdf-annot-{Guid.NewGuid():N}.pdf");
        try
        {
            using (var writer = new PdfiumFileWriter(tempPath))
            {
                if (NativeMethods.FPDF_SaveAsCopy(documentHandle, ref writer.FileWrite, SaveFlagNoIncremental) == 0)
                {
                    throw new InvalidOperationException("Failed to save annotations using PDFium.");
                }
            }

            File.Copy(tempPath, outputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static bool TryGetRect(IntPtr annotHandle, out FS_RECTF rect)
    {
        rect = default;
        return NativeMethods.FPDFAnnot_GetRect(annotHandle, out rect) != 0;
    }

    private static string GetAnnotationString(IntPtr annotHandle, string key)
    {
        var keyPtr = Marshal.StringToHGlobalAnsi(key);
        try
        {
            var requiredBytes = NativeMethods.FPDFAnnot_GetStringValue(annotHandle, keyPtr, IntPtr.Zero, 0);
            if (requiredBytes <= 0)
            {
                return string.Empty;
            }

            var buffer = Marshal.AllocHGlobal((int)requiredBytes);
            try
            {
                var copied = NativeMethods.FPDFAnnot_GetStringValue(annotHandle, keyPtr, buffer, requiredBytes);
                if (copied <= 0)
                {
                    return string.Empty;
                }

                var managedBuffer = new byte[(int)copied];
                Marshal.Copy(buffer, managedBuffer, 0, (int)copied);
                return Encoding.Unicode.GetString(managedBuffer).TrimEnd('\0');
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(keyPtr);
        }
    }

    private static void SetAnnotationString(IntPtr annotHandle, string key, string value)
    {
        var keyPtr = Marshal.StringToHGlobalAnsi(key);
        try
        {
            NativeMethods.FPDFAnnot_SetStringValue(annotHandle, keyPtr, value);
        }
        finally
        {
            Marshal.FreeHGlobal(keyPtr);
        }
    }

    private static HighlightType MapSubtypeToHighlightType(int subtype)
    {
        return subtype switch
        {
            AnnotSubtypeUnderline => HighlightType.Underline,
            AnnotSubtypeStrikeOut => HighlightType.Strikethrough,
            _ => HighlightType.Highlight
        };
    }

    private static int MapHighlightTypeToSubtype(HighlightType type)
    {
        return type switch
        {
            HighlightType.Underline => AnnotSubtypeUnderline,
            HighlightType.Strikethrough => AnnotSubtypeStrikeOut,
            _ => AnnotSubtypeHighlight
        };
    }

    private static HighlightColor GetHighlightColor(IntPtr annotHandle)
    {
        if (NativeMethods.FPDFAnnot_GetColor(annotHandle, AnnotColorTypeNormal, out var r, out var g, out var b, out _) == 0)
        {
            return HighlightColor.Yellow;
        }

        return MapRgbToHighlightColor(r, g, b);
    }

    private static (uint R, uint G, uint B) MapHighlightColor(HighlightColor color)
    {
        return color switch
        {
            HighlightColor.Green => (0, 200, 120),
            HighlightColor.Blue => (80, 160, 255),
            HighlightColor.Pink => (255, 120, 180),
            _ => (255, 220, 0)
        };
    }

    private static HighlightColor MapRgbToHighlightColor(uint r, uint g, uint b)
    {
        var candidate = new[]
        {
            (Color: HighlightColor.Yellow, R: 255u, G: 220u, B: 0u),
            (Color: HighlightColor.Green, R: 0u, G: 200u, B: 120u),
            (Color: HighlightColor.Blue, R: 80u, G: 160u, B: 255u),
            (Color: HighlightColor.Pink, R: 255u, G: 120u, B: 180u)
        };

        return candidate
            .OrderBy(item => Math.Abs((int)item.R - (int)r) + Math.Abs((int)item.G - (int)g) + Math.Abs((int)item.B - (int)b))
            .First()
            .Color;
    }

    private sealed class PdfiumFileWriter : IDisposable
    {
        private readonly FileStream _stream;
        private readonly WriteBlockCallback _callback;

        public PdfiumFileWriter(string outputPath)
        {
            _stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
            _callback = WriteBlock;
            FileWrite = new FPDF_FILEWRITE
            {
                version = 1,
                WriteBlock = Marshal.GetFunctionPointerForDelegate(_callback)
            };
        }

        public FPDF_FILEWRITE FileWrite;

        public void Dispose()
        {
            _stream.Dispose();
        }

        private int WriteBlock(IntPtr _, IntPtr data, uint size)
        {
            if (size == 0)
            {
                return 1;
            }

            if (data == IntPtr.Zero)
            {
                return 0;
            }

            var buffer = new byte[size];
            Marshal.Copy(data, buffer, 0, (int)size);
            _stream.Write(buffer, 0, (int)size);
            return 1;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FS_RECTF
    {
        public float Left;
        public float Bottom;
        public float Right;
        public float Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FPDF_FILEWRITE
    {
        public int version;
        public IntPtr WriteBlock;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int WriteBlockCallback(IntPtr fileWrite, IntPtr data, uint size);

    private static class NativeMethods
    {
        private const string LibraryName = "pdfium";

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDF_LoadPage(IntPtr document, int page_index);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDF_ClosePage(IntPtr page);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFPage_GetAnnotCount(IntPtr page);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDFPage_GetAnnot(IntPtr page, int index);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDFPage_CreateAnnot(IntPtr page, int subtype);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFPage_RemoveAnnot(IntPtr page, int index);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDFPage_CloseAnnot(IntPtr annot);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFAnnot_GetSubtype(IntPtr annot);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFAnnot_GetRect(IntPtr annot, out FS_RECTF rect);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFAnnot_SetRect(IntPtr annot, ref FS_RECTF rect);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFAnnot_SetColor(IntPtr annot, int colorType, uint r, uint g, uint b, uint a);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFAnnot_GetColor(IntPtr annot, int colorType, out uint r, out uint g, out uint b, out uint a);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint FPDFAnnot_GetStringValue(IntPtr annot, IntPtr key, IntPtr value, uint buflen);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int FPDFAnnot_SetStringValue(IntPtr annot, IntPtr key, [MarshalAs(UnmanagedType.LPWStr)] string value);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDF_SaveAsCopy(IntPtr document, ref FPDF_FILEWRITE fileWrite, int flags);
    }
}
