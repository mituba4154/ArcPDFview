#nullable enable

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using AcroPDF.Core.Models;
using AcroPDF.Services.Interfaces;
using SkiaSharp;

namespace AcroPDF.Services;

/// <summary>
/// PDFium を使用した PDF レンダリングサービスです。
/// </summary>
public sealed class PdfiumRenderService : IPdfRenderService
{
    private const uint PdfErrorPassword = 4;
    private const int RenderFlags = 0x01;
    private const int TwoPageSpacingPx = 16;

    private static readonly object InitLock = new();
    private static bool _libraryInitialized;

    private readonly ConcurrentDictionary<string, SKBitmap> _pageCache = new();
    private readonly ConcurrentDictionary<IntPtr, byte> _openDocuments = new();
    private int _disposed;

    /// <summary>
    /// <see cref="PdfiumRenderService"/> の新しいインスタンスを初期化します。
    /// </summary>
    public PdfiumRenderService()
    {
        EnsureLibraryInitialized();
    }

    /// <summary>
    /// ズーム倍率を 25%〜400% の範囲に正規化します。
    /// </summary>
    /// <param name="zoomLevel">元のズーム倍率。</param>
    /// <returns>正規化したズーム倍率。</returns>
    public static double ClampZoomLevel(double zoomLevel)
    {
        return Math.Clamp(zoomLevel, 0.25d, 4.0d);
    }

    /// <inheritdoc />
    public Task<PdfDocument> OpenAsync(string filePath, string? password = null, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);

        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("PDF file not found.", filePath);
            }

            var documentHandle = NativeMethods.FPDF_LoadDocument(filePath, password);
            if (documentHandle == IntPtr.Zero)
            {
                var error = NativeMethods.FPDF_GetLastError();
                if (error == PdfErrorPassword)
                {
                    throw new PdfPasswordRequiredException(filePath);
                }

                throw new InvalidOperationException($"Failed to open PDF. PDFium error code: {error}.");
            }

            _openDocuments.TryAdd(documentHandle, 0);

            var pageCount = NativeMethods.FPDF_GetPageCount(documentHandle);
            if (pageCount < 0)
            {
                CloseNativeDocument(documentHandle);
                throw new InvalidOperationException($"Failed to get page count. PDFium returned {pageCount}.");
            }

            var pages = new List<PdfPage>(pageCount);
            for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                ct.ThrowIfCancellationRequested();

                var widthPt = 0d;
                var heightPt = 0d;
                if (NativeMethods.FPDF_GetPageSizeByIndexF(documentHandle, pageIndex, out var pageSize) != 0)
                {
                    widthPt = pageSize.Width;
                    heightPt = pageSize.Height;
                }

                if (widthPt <= 0 || heightPt <= 0)
                {
                    var pageHandle = NativeMethods.FPDF_LoadPage(documentHandle, pageIndex);
                    if (pageHandle != IntPtr.Zero)
                    {
                        widthPt = NativeMethods.FPDF_GetPageWidthF(pageHandle);
                        heightPt = NativeMethods.FPDF_GetPageHeightF(pageHandle);
                        NativeMethods.FPDF_ClosePage(pageHandle);
                    }
                }

                pages.Add(new PdfPage(documentHandle, pageIndex, widthPt, heightPt));
            }

            return new PdfDocument(filePath, documentHandle, pages, CloseNativeDocument);
        }, ct);
    }

    /// <inheritdoc />
    public Task<SKBitmap> RenderPageAsync(PdfPage page, double zoomLevel, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);

        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            var normalizedZoom = ClampZoomLevel(zoomLevel);
            var renderDpi = 96d * normalizedZoom;
            var widthPx = Math.Max(1, (int)Math.Ceiling(page.WidthPt * renderDpi / 72d));
            var heightPx = Math.Max(1, (int)Math.Ceiling(page.HeightPt * renderDpi / 72d));
            var cacheKey = $"{page.DocumentHandle}:{page.PageIndex}:{renderDpi:F2}";

            if (_pageCache.TryGetValue(cacheKey, out var cachedBitmap))
            {
                return cachedBitmap.Copy();
            }

            var pageHandle = NativeMethods.FPDF_LoadPage(page.DocumentHandle, page.PageIndex);
            if (pageHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Failed to load page index {page.PageIndex}.");
            }

            IntPtr bitmapHandle = IntPtr.Zero;
            try
            {
                bitmapHandle = NativeMethods.FPDFBitmap_Create(widthPx, heightPx, alpha: 1);
                if (bitmapHandle == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Failed to create PDFium bitmap.");
                }

                NativeMethods.FPDFBitmap_FillRect(bitmapHandle, 0, 0, widthPx, heightPx, 0xFFFFFFFFu);
                NativeMethods.FPDF_RenderPageBitmap(bitmapHandle, pageHandle, 0, 0, widthPx, heightPx, 0, RenderFlags);

                var sourcePtr = NativeMethods.FPDFBitmap_GetBuffer(bitmapHandle);
                var sourceStride = NativeMethods.FPDFBitmap_GetStride(bitmapHandle);

                var renderedBitmap = new SKBitmap(widthPx, heightPx, SKColorType.Bgra8888, SKAlphaType.Premul);
                var destinationPtr = renderedBitmap.GetPixels();
                var destinationStride = renderedBitmap.RowBytes;
                var rowLength = Math.Min(sourceStride, destinationStride);

                var rowBuffer = new byte[rowLength];
                for (var y = 0; y < heightPx; y++)
                {
                    Marshal.Copy(IntPtr.Add(sourcePtr, y * sourceStride), rowBuffer, 0, rowLength);
                    Marshal.Copy(rowBuffer, 0, IntPtr.Add(destinationPtr, y * destinationStride), rowLength);
                }

                if (_pageCache.TryAdd(cacheKey, renderedBitmap))
                {
                    return renderedBitmap.Copy();
                }

                renderedBitmap.Dispose();
                return _pageCache[cacheKey].Copy();
            }
            finally
            {
                if (bitmapHandle != IntPtr.Zero)
                {
                    NativeMethods.FPDFBitmap_Destroy(bitmapHandle);
                }

                NativeMethods.FPDF_ClosePage(pageHandle);
            }
        }, ct);
    }

    /// <inheritdoc />
    public async Task<SKBitmap?> RenderCompositePageAsync(
        PdfDocument document,
        int currentPage,
        double zoomLevel,
        bool twoPageMode,
        int rotationDegrees,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (currentPage <= 0 || currentPage > document.PageCount)
        {
            return null;
        }

        var page = document.Pages[currentPage - 1];
        var bitmap = await RenderPageAsync(page, zoomLevel, ct).ConfigureAwait(false);
        try
        {
            if (twoPageMode && currentPage < document.PageCount)
            {
                var secondPage = document.Pages[currentPage];
                using var second = await RenderPageAsync(secondPage, zoomLevel, ct).ConfigureAwait(false);
                var mergedWidth = checked(bitmap.Width + second.Width + TwoPageSpacingPx);
                var merged = new SKBitmap(mergedWidth, Math.Max(bitmap.Height, second.Height), SKColorType.Bgra8888, SKAlphaType.Premul);
                using var canvas = new SKCanvas(merged);
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(bitmap, 0, 0);
                canvas.DrawBitmap(second, bitmap.Width + TwoPageSpacingPx, 0);
                bitmap.Dispose();
                bitmap = merged;
            }

            if (rotationDegrees == 0)
            {
                return bitmap;
            }

            var rotated = RotateBitmap(bitmap, rotationDegrees);
            bitmap.Dispose();
            return rotated;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public void Close(PdfDocument document)
    {
        document.Dispose();
        RemoveCacheForDocument(document.NativeHandle);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        foreach (var documentHandle in _openDocuments.Keys)
        {
            CloseNativeDocument(documentHandle);
        }

        foreach (var cacheEntry in _pageCache)
        {
            cacheEntry.Value.Dispose();
        }

        _pageCache.Clear();
    }

    private static void EnsureLibraryInitialized()
    {
        if (_libraryInitialized)
        {
            return;
        }

        lock (InitLock)
        {
            if (_libraryInitialized)
            {
                return;
            }

            NativeMethods.FPDF_InitLibrary();
            _libraryInitialized = true;
        }
    }

    private void CloseNativeDocument(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        if (_openDocuments.TryRemove(handle, out _))
        {
            NativeMethods.FPDF_CloseDocument(handle);
            RemoveCacheForDocument(handle);
        }
    }

    private void RemoveCacheForDocument(IntPtr handle)
    {
        var cacheKeyPrefix = $"{handle}:";
        var matchedKeys = _pageCache.Keys
            .Where(key => key.StartsWith(cacheKeyPrefix, StringComparison.Ordinal))
            .ToArray();

        foreach (var key in matchedKeys)
        {
            if (_pageCache.TryRemove(key, out var bitmap))
            {
                bitmap.Dispose();
            }
        }
    }

    private static SKBitmap RotateBitmap(SKBitmap source, int degrees)
    {
        var normalized = ((degrees % 360) + 360) % 360;
        if (normalized == 0)
        {
            return source.Copy();
        }

        var swapSize = normalized is 90 or 270;
        var width = swapSize ? source.Height : source.Width;
        var height = swapSize ? source.Width : source.Height;
        var rotated = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(rotated);
        canvas.Clear(SKColors.Transparent);
        canvas.Translate(width / 2f, height / 2f);
        canvas.RotateDegrees(normalized);
        canvas.Translate(-source.Width / 2f, -source.Height / 2f);
        canvas.DrawBitmap(source, 0, 0);
        return rotated;
    }

    private static class NativeMethods
    {
        private const string LibraryName = "pdfium";

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDF_InitLibrary();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDF_LoadDocument([MarshalAs(UnmanagedType.LPUTF8Str)] string file_path, [MarshalAs(UnmanagedType.LPUTF8Str)] string? password);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint FPDF_GetLastError();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDF_GetPageCount(IntPtr document);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDF_GetPageSizeByIndexF(IntPtr document, int page_index, out FS_SIZEF size);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDF_LoadPage(IntPtr document, int page_index);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDF_ClosePage(IntPtr page);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern float FPDF_GetPageWidthF(IntPtr page);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern float FPDF_GetPageHeightF(IntPtr page);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDF_CloseDocument(IntPtr document);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDFBitmap_Create(int width, int height, int alpha);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDFBitmap_Destroy(IntPtr bitmap);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFBitmap_GetStride(IntPtr bitmap);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int start_x, int start_y, int size_x, int size_y, int rotate, int flags);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct FS_SIZEF
    {
        public readonly float Width;
        public readonly float Height;
    }
}

/// <summary>
/// PDF を開く際にパスワード入力が必要なことを表す例外です。
/// </summary>
public sealed class PdfPasswordRequiredException : Exception
{
    /// <summary>
    /// <see cref="PdfPasswordRequiredException"/> の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="filePath">対象ファイルパス。</param>
    public PdfPasswordRequiredException(string filePath)
        : base($"Password is required to open '{filePath}'.")
    {
        FilePath = filePath;
    }

    /// <summary>
    /// パスワードが要求された対象ファイルパスを取得します。
    /// </summary>
    public string FilePath { get; }
}
