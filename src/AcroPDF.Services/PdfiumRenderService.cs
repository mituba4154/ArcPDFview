#nullable enable

using System.Collections.Concurrent;
using System.Buffers;
using System.Diagnostics;
using System.Threading;
using System.Runtime.InteropServices;
using System.Text;
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
    private const uint PdfSaveNoIncremental = 1;
    private const int RenderFlags = 0x01;
    private const int TwoPageSpacingPx = 16;
    private const int AnnotSubtypeWidget = 20;
    private const uint PermissionPrint = 0x00000004;
    private const uint PermissionModify = 0x00000008;
    private const uint PermissionCopy = 0x00000010;
    private const uint PermissionAnnotate = 0x00000020;
    private const int FormFieldTypeUnknown = 0;
    private const int FormFieldTypePushButton = 1;
    private const int FormFieldTypeCheckBox = 2;
    private const int FormFieldTypeRadioButton = 3;
    private const int FormFieldTypeComboBox = 4;
    private const int FormFieldTypeListBox = 5;
    private const int FormFieldTypeTextField = 6;
    private const int FormFieldTypeSignature = 7;
    private const int PdfiumWorkerStackSizeBytes = 256 * 1024 * 1024;

    private static readonly object InitLock = new();
    private static readonly BlockingCollection<Action> PdfiumWorkQueue = new();
    private static readonly AsyncLocal<FileStream?> SaveStreamContext = new();
    private static bool _libraryInitialized;
    private static int _pdfiumWorkerThreadId;

    // Phase 5 要件: ページキャッシュは最大 10 エントリの LRU とする。
    private const int MaxPageCacheEntries = 10;
    private readonly object _cacheLock = new();
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _pageCache = new(StringComparer.Ordinal);
    private readonly LinkedList<CacheEntry> _pageCacheLru = new();
    private readonly ConcurrentDictionary<IntPtr, byte> _openDocuments = new();
    private readonly ConcurrentDictionary<IntPtr, IntPtr> _formHandles = new();
    private readonly ConcurrentDictionary<int, TimerRegistration> _formTimers = new();
    private readonly ConcurrentDictionary<IntPtr, DocumentLoadContext> _documentLoadContexts = new();
    private readonly ConcurrentDictionary<IntPtr, byte> _memoryMeasuredDocuments = new();
    // Unmanaged FPDF_FORMFILLINFO.Release で使用するため、GC 回収されないようインスタンスで保持する。
    private readonly ReleaseCallback _releaseCallback;
    private readonly WriteBlockCallback _saveCallback;
    private readonly FormFillSetTimerCallback _setTimerCallback;
    private readonly FormFillKillTimerCallback _killTimerCallback;
    private readonly FormFillGetLocalTimeCallback _getLocalTimeCallback;
    private int _nextFormTimerId;
    private int _disposed;

    static PdfiumRenderService()
    {
        var workerThread = new Thread(ProcessPdfiumQueue, PdfiumWorkerStackSizeBytes)
        {
            IsBackground = true,
            Name = "pdfium-worker"
        };
        workerThread.Start();
    }

    /// <summary>
    /// <see cref="PdfiumRenderService"/> の新しいインスタンスを初期化します。
    /// </summary>
    public PdfiumRenderService()
    {
        _releaseCallback = static _ => { };
        _saveCallback = SaveDocumentWriteBlock;
        _setTimerCallback = OnSetFormTimer;
        _killTimerCallback = OnKillFormTimer;
        _getLocalTimeCallback = OnGetFormLocalTime;
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

        return RunOnPdfiumThread(() =>
        {
            EnsureLibraryInitialized();
            ct.ThrowIfCancellationRequested();
            var stopwatch = Stopwatch.StartNew();

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("PDF file not found.", filePath);
            }

            var loadContext = new DocumentLoadContext(filePath);
            var documentHandle = NativeMethods.FPDF_LoadCustomDocument(loadContext.AccessInfoPtr, password);
            if (documentHandle == IntPtr.Zero)
            {
                loadContext.Dispose();
                var error = NativeMethods.FPDF_GetLastError();
                if (error == PdfErrorPassword)
                {
                    throw new PdfPasswordRequiredException(filePath);
                }

                throw new InvalidOperationException($"Failed to open PDF. PDFium error code: {error}.");
            }

            _openDocuments.TryAdd(documentHandle, 0);
            _documentLoadContexts.TryAdd(documentHandle, loadContext);

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

            var document = new PdfDocument(filePath, documentHandle, pages, CloseNativeDocument);
            stopwatch.Stop();
            Trace.TraceInformation($"PDF open: {Path.GetFileName(filePath)} pages={pageCount} elapsed={stopwatch.ElapsedMilliseconds}ms");
            if (pageCount >= 100)
            {
                var memoryMb = GC.GetTotalMemory(false) / (1024d * 1024d);
                Trace.TraceInformation($"Memory baseline after open (>=100 pages): {memoryMb:F1} MB");
            }

            return document;
        });
    }

    /// <inheritdoc />
    public Task<SKBitmap> RenderPageAsync(PdfPage page, double zoomLevel, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);

        return RunOnPdfiumThread(() =>
        {
            EnsureLibraryInitialized();
            ct.ThrowIfCancellationRequested();

            var normalizedZoom = ClampZoomLevel(zoomLevel);
            var renderDpi = 96d * normalizedZoom;
            var rendered = RenderPageInternal(page, renderDpi, useCache: true);
            if (page.PageIndex >= 99 && _memoryMeasuredDocuments.TryAdd(page.DocumentHandle, 0))
            {
                var memoryMb = GC.GetTotalMemory(false) / (1024d * 1024d);
                Trace.TraceInformation($"Memory after rendering page {page.PageNumber}: {memoryMb:F1} MB");
            }

            QueueAdjacentPrefetch(page, normalizedZoom);
            return rendered;
        });
    }

    /// <inheritdoc />
    public Task<SKBitmap> RenderPageForPrintAsync(PdfPage page, int dpi, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
        var printDpi = Math.Clamp(dpi, 72, 600);
        return RunOnPdfiumThread(() => RenderPageInternal(page, printDpi, useCache: false));
    }

    /// <inheritdoc />
    public Task ExtractPagesAsync(PdfDocument document, IReadOnlyList<int> pageNumbers, string outputPath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(pageNumbers);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        return RunOnPdfiumThread(() =>
        {
            ct.ThrowIfCancellationRequested();
            var sourcePages = NormalizePageNumbers(pageNumbers, document.PageCount);
            if (sourcePages.Count == 0)
            {
                throw new ArgumentException("At least one page must be selected.", nameof(pageNumbers));
            }

            var newDocument = NativeMethods.FPDF_CreateNewDocument();
            if (newDocument == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create destination PDF document.");
            }

            try
            {
                ImportPages(newDocument, document.NativeHandle, sourcePages);
                NativeMethods.FPDF_CopyViewerPreferences(newDocument, document.NativeHandle);
                SaveDocumentCopy(newDocument, outputPath);
            }
            finally
            {
                NativeMethods.FPDF_CloseDocument(newDocument);
            }
        });
    }

    /// <inheritdoc />
    public Task DeletePagesAsync(PdfDocument document, IReadOnlyList<int> pageNumbers, string? outputPath = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(pageNumbers);

        return RunOnPdfiumThread(() =>
        {
            ct.ThrowIfCancellationRequested();
            var deletePages = NormalizePageNumbers(pageNumbers, document.PageCount);
            if (deletePages.Count >= document.PageCount)
            {
                throw new InvalidOperationException("Cannot delete all pages from a document.");
            }

            var destinationPath = outputPath ?? document.FilePath;
            var newDocument = NativeMethods.FPDF_CreateNewDocument();
            if (newDocument == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create destination PDF document.");
            }

            try
            {
                ImportPages(newDocument, document.NativeHandle, Enumerable.Range(1, document.PageCount).ToArray());
                foreach (var page in deletePages.OrderByDescending(static page => page))
                {
                    ct.ThrowIfCancellationRequested();
                    NativeMethods.FPDFPage_Delete(newDocument, page - 1);
                }

                NativeMethods.FPDF_CopyViewerPreferences(newDocument, document.NativeHandle);
                SaveDocumentCopy(newDocument, destinationPath);
            }
            finally
            {
                NativeMethods.FPDF_CloseDocument(newDocument);
            }
        });
    }

    /// <inheritdoc />
    public Task RotatePagesAsync(PdfDocument document, IReadOnlyList<int> pageNumbers, int rotationDegrees, string? outputPath = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(pageNumbers);

        return RunOnPdfiumThread(() =>
        {
            ct.ThrowIfCancellationRequested();
            var pages = NormalizePageNumbers(pageNumbers, document.PageCount);
            if (pages.Count == 0)
            {
                return;
            }

            var rotation = NormalizeRotationIndex(rotationDegrees);
            var newDocument = NativeMethods.FPDF_CreateNewDocument();
            if (newDocument == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create destination PDF document.");
            }

            try
            {
                ImportPages(newDocument, document.NativeHandle, Enumerable.Range(1, document.PageCount).ToArray());
                NativeMethods.FPDF_CopyViewerPreferences(newDocument, document.NativeHandle);
                foreach (var pageNumber in pages)
                {
                    ct.ThrowIfCancellationRequested();
                    var page = NativeMethods.FPDF_LoadPage(newDocument, pageNumber - 1);
                    if (page == IntPtr.Zero)
                    {
                        continue;
                    }

                    try
                    {
                        NativeMethods.FPDFPage_SetRotation(page, rotation);
                    }
                    finally
                    {
                        NativeMethods.FPDF_ClosePage(page);
                    }
                }

                SaveDocumentCopy(newDocument, outputPath ?? document.FilePath);
            }
            finally
            {
                NativeMethods.FPDF_CloseDocument(newDocument);
            }
        });
    }

    /// <inheritdoc />
    public Task MergeAsync(IReadOnlyList<string> inputFilePaths, string outputPath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(inputFilePaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        return RunOnPdfiumThread(() =>
        {
            ct.ThrowIfCancellationRequested();
            var existingInputs = inputFilePaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (existingInputs.Length == 0)
            {
                throw new ArgumentException("At least one input file is required.", nameof(inputFilePaths));
            }

            var destination = NativeMethods.FPDF_CreateNewDocument();
            if (destination == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create destination PDF document.");
            }

            try
            {
                foreach (var input in existingInputs)
                {
                    ct.ThrowIfCancellationRequested();
                    var source = NativeMethods.FPDF_LoadDocument(input, null);
                    if (source == IntPtr.Zero)
                    {
                        throw new InvalidOperationException($"Failed to open source PDF '{input}'.");
                    }

                    try
                    {
                        if (NativeMethods.FPDF_ImportPages(destination, source, null, NativeMethods.FPDF_GetPageCount(destination)) == 0)
                        {
                            throw new InvalidOperationException($"Failed to import pages from '{input}'.");
                        }
                    }
                    finally
                    {
                        NativeMethods.FPDF_CloseDocument(source);
                    }
                }

                var firstSource = NativeMethods.FPDF_LoadDocument(existingInputs[0], null);
                if (firstSource != IntPtr.Zero)
                {
                    try
                    {
                        NativeMethods.FPDF_CopyViewerPreferences(destination, firstSource);
                    }
                    finally
                    {
                        NativeMethods.FPDF_CloseDocument(firstSource);
                    }
                }

                SaveDocumentCopy(destination, outputPath);
            }
            finally
            {
                NativeMethods.FPDF_CloseDocument(destination);
            }
        });
    }

    /// <inheritdoc />
    public Task ReorderPagesAsync(PdfDocument document, IReadOnlyList<int> pageOrder, string? outputPath = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(pageOrder);

        return RunOnPdfiumThread(() =>
        {
            ct.ThrowIfCancellationRequested();
            var normalizedOrder = pageOrder
                .Select(page => Math.Clamp(page, 1, document.PageCount))
                .ToArray();
            if (normalizedOrder.Length != document.PageCount || normalizedOrder.Distinct().Count() != document.PageCount)
            {
                throw new ArgumentException("pageOrder must contain all pages exactly once.", nameof(pageOrder));
            }

            var newDocument = NativeMethods.FPDF_CreateNewDocument();
            if (newDocument == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create destination PDF document.");
            }

            try
            {
                foreach (var page in normalizedOrder)
                {
                    ct.ThrowIfCancellationRequested();
                    ImportPages(newDocument, document.NativeHandle, [page]);
                }

                NativeMethods.FPDF_CopyViewerPreferences(newDocument, document.NativeHandle);
                SaveDocumentCopy(newDocument, outputPath ?? document.FilePath);
            }
            finally
            {
                NativeMethods.FPDF_CloseDocument(newDocument);
            }
        });
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PdfEmbeddedFile>> GetEmbeddedFilesAsync(PdfDocument document, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        return RunOnPdfiumThread<IReadOnlyList<PdfEmbeddedFile>>(() =>
        {
            ct.ThrowIfCancellationRequested();
            var count = NativeMethods.FPDFDoc_GetAttachmentCount(document.NativeHandle);
            if (count <= 0)
            {
                return [];
            }

            var files = new List<PdfEmbeddedFile>(count);
            for (var index = 0; index < count; index++)
            {
                ct.ThrowIfCancellationRequested();
                var attachment = NativeMethods.FPDFDoc_GetAttachment(document.NativeHandle, index);
                if (attachment == IntPtr.Zero)
                {
                    continue;
                }

                var name = GetAttachmentName(attachment);
                var size = GetAttachmentSize(attachment);
                files.Add(new PdfEmbeddedFile
                {
                    Id = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Name = string.IsNullOrWhiteSpace(name) ? $"Attachment-{index + 1}" : name,
                    Size = size
                });
            }

            return files;
        });
    }

    /// <inheritdoc />
    public Task ExtractEmbeddedFileAsync(PdfDocument document, PdfEmbeddedFile file, string outputPath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        return RunOnPdfiumThread(() =>
        {
            ct.ThrowIfCancellationRequested();
            if (!int.TryParse(file.Id, out var index))
            {
                throw new InvalidOperationException($"Invalid attachment id '{file.Id}'.");
            }

            var attachment = NativeMethods.FPDFDoc_GetAttachment(document.NativeHandle, index);
            if (attachment == IntPtr.Zero)
            {
                throw new InvalidOperationException("Attachment not found.");
            }

            var size = GetAttachmentSize(attachment);
            if (size <= 0)
            {
                File.WriteAllBytes(outputPath, []);
                return;
            }

            var buffer = new byte[size];
            if (NativeMethods.FPDFAttachment_GetFile(attachment, buffer, (uint)buffer.Length, out var outLength) == 0 ||
                outLength == 0)
            {
                throw new InvalidOperationException("Failed to extract attachment.");
            }

            File.WriteAllBytes(outputPath, buffer.AsSpan(0, (int)outLength).ToArray());
        });
    }

    /// <inheritdoc />
    public Task<PdfSecurityInfo> GetSecurityInfoAsync(PdfDocument document, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        return RunOnPdfiumThread(() =>
        {
            ct.ThrowIfCancellationRequested();
            var permissions = NativeMethods.FPDF_GetDocPermissions(document.NativeHandle);
            var revision = NativeMethods.FPDF_GetSecurityHandlerRevision(document.NativeHandle);
            var isEncrypted = revision > 0;
            var unrestricted = permissions == uint.MaxValue || permissions == 0;

            return new PdfSecurityInfo
            {
                IsEncrypted = isEncrypted,
                EncryptionDescription = ResolveEncryptionDescription(revision),
                CanPrint = unrestricted || (permissions & PermissionPrint) != 0,
                CanCopy = unrestricted || (permissions & PermissionCopy) != 0,
                CanAnnotate = unrestricted || (permissions & PermissionAnnotate) != 0 || (permissions & PermissionModify) != 0
            };
        });
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
    public Task<IReadOnlyList<PdfFormField>> GetFormFieldsAsync(PdfPage page, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
        return RunOnPdfiumThread<IReadOnlyList<PdfFormField>>(() =>
        {
            ct.ThrowIfCancellationRequested();
            var pageHandle = NativeMethods.FPDF_LoadPage(page.DocumentHandle, page.PageIndex);
            if (pageHandle == IntPtr.Zero)
            {
                return [];
            }

            var formHandle = GetOrCreateFormHandle(page.DocumentHandle);
            var fields = new List<PdfFormField>();
            try
            {
                var count = NativeMethods.FPDFPage_GetAnnotCount(pageHandle);
                for (var index = 0; index < count; index++)
                {
                    ct.ThrowIfCancellationRequested();
                    var annot = NativeMethods.FPDFPage_GetAnnot(pageHandle, index);
                    if (annot == IntPtr.Zero)
                    {
                        continue;
                    }

                    try
                    {
                        if (NativeMethods.FPDFAnnot_GetSubtype(annot) != AnnotSubtypeWidget ||
                            NativeMethods.FPDFAnnot_GetRect(annot, out var rect) == 0)
                        {
                            continue;
                        }

                        var formFieldType = formHandle != IntPtr.Zero
                            ? NativeMethods.FPDFAnnot_GetFormFieldType(formHandle, annot)
                            : FormFieldTypeUnknown;
                        var type = MapFormFieldType(formFieldType);
                        var appearanceState = GetAnnotationString(annot, "AS");
                        fields.Add(new PdfFormField
                        {
                            Id = $"{page.PageNumber}:{index}",
                            PageNumber = page.PageNumber,
                            Name = GetAnnotationString(annot, "T"),
                            FieldType = type,
                            Bounds = new PdfTextBounds(rect.Left, rect.Top, rect.Right, rect.Bottom),
                            Value = GetAnnotationString(annot, "V"),
                            IsChecked = string.Equals(appearanceState, "Yes", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(appearanceState, "On", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(appearanceState, "1", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(appearanceState, "True", StringComparison.OrdinalIgnoreCase)
                        });
                    }
                    finally
                    {
                        NativeMethods.FPDFPage_CloseAnnot(annot);
                    }
                }
            }
            finally
            {
                NativeMethods.FPDF_ClosePage(pageHandle);
            }

            return fields;
        });
    }

    /// <inheritdoc />
    public Task<bool> ApplyFormFieldInputAsync(
        PdfPage page,
        PdfFormField field,
        string? textValue,
        bool? isChecked = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(field);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);

        return RunOnPdfiumThread(() =>
        {
            ct.ThrowIfCancellationRequested();
            var formHandle = GetOrCreateFormHandle(page.DocumentHandle);
            if (formHandle == IntPtr.Zero)
            {
                return false;
            }

            var pageHandle = NativeMethods.FPDF_LoadPage(page.DocumentHandle, page.PageIndex);
            if (pageHandle == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                var centerX = (field.Bounds.Left + field.Bounds.Right) * 0.5d;
                var centerY = (field.Bounds.Top + field.Bounds.Bottom) * 0.5d;
                NativeMethods.FORM_OnLButtonDown(formHandle, pageHandle, 0, centerX, centerY);
                NativeMethods.FORM_OnLButtonUp(formHandle, pageHandle, 0, centerX, centerY);

                if (field.FieldType == PdfFormFieldType.Text || field.FieldType == PdfFormFieldType.Signature)
                {
                    foreach (var ch in ExpandToPdfiumCharEvents(textValue ?? string.Empty))
                    {
                        ct.ThrowIfCancellationRequested();
                        NativeMethods.FORM_OnChar(formHandle, pageHandle, ch, 0);
                    }
                }
                else if ((field.FieldType == PdfFormFieldType.CheckBox || field.FieldType == PdfFormFieldType.RadioButton) &&
                         isChecked == true)
                {
                    NativeMethods.FORM_OnLButtonDown(formHandle, pageHandle, 0, centerX, centerY);
                    NativeMethods.FORM_OnLButtonUp(formHandle, pageHandle, 0, centerX, centerY);
                }

                NativeMethods.FORM_ForceToKillFocus(formHandle);
                return true;
            }
            finally
            {
                NativeMethods.FPDF_ClosePage(pageHandle);
            }
        });
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

        foreach (var documentHandle in _formHandles.Keys.ToArray())
        {
            DestroyFormHandle(documentHandle);
        }

        foreach (var timerId in _formTimers.Keys.ToArray())
        {
            StopFormTimer(timerId);
        }

        ClearCache();

        foreach (var context in _documentLoadContexts.Values)
        {
            context.Dispose();
        }

        _documentLoadContexts.Clear();
    }

    /// <summary>
    /// PDFium の呼び出しを専用スレッド（256MB スタック）で実行します。
    /// スレッドプールの既定スタックサイズ (1MB) では PDFium の深い再帰で
    /// スタックオーバーフローが発生する可能性があるため、大きいスタックを確保します。
    /// 一部の複雑な PDF では 64MB でも不足するため、余裕を持たせています。
    /// </summary>
    private static Task<T> RunOnPdfiumThread<T>(Func<T> func)
    {
        if (Environment.CurrentManagedThreadId == Volatile.Read(ref _pdfiumWorkerThreadId))
        {
            try
            {
                return Task.FromResult(func());
            }
            catch (Exception ex)
            {
                return Task.FromException<T>(ex);
            }
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        PdfiumWorkQueue.Add(() =>
        {
            try
            {
                tcs.SetResult(func());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    /// <summary>
    /// PDFium の呼び出しを専用スレッド（256MB スタック）で実行します（戻り値なし版）。
    /// </summary>
    private static Task RunOnPdfiumThread(Action action)
    {
        if (Environment.CurrentManagedThreadId == Volatile.Read(ref _pdfiumWorkerThreadId))
        {
            try
            {
                action();
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                return Task.FromException(ex);
            }
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PdfiumWorkQueue.Add(() =>
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    private static void ProcessPdfiumQueue()
    {
        Volatile.Write(ref _pdfiumWorkerThreadId, Environment.CurrentManagedThreadId);
        foreach (var action in PdfiumWorkQueue.GetConsumingEnumerable())
        {
            action();
        }
    }

    private SKBitmap RenderPageInternal(PdfPage page, double dpi, bool useCache)
    {
        var widthPx = Math.Max(1, (int)Math.Ceiling(page.WidthPt * dpi / 72d));
        var heightPx = Math.Max(1, (int)Math.Ceiling(page.HeightPt * dpi / 72d));
        var roundedDpi = Math.Round(dpi, 1);
        var cacheKey = $"{page.DocumentHandle}:{page.PageIndex}:{roundedDpi}";

        if (useCache && TryGetCachedBitmapCopy(cacheKey, out var cachedBitmap))
        {
            return cachedBitmap;
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
            if (sourceStride == destinationStride)
            {
                unsafe
                {
                    Buffer.MemoryCopy(
                        (void*)sourcePtr,
                        (void*)destinationPtr,
                        heightPx * destinationStride,
                        heightPx * sourceStride);
                }
            }
            else
            {
                var rowLength = Math.Min(sourceStride, destinationStride);
                var rowBuffer = new byte[rowLength];
                for (var y = 0; y < heightPx; y++)
                {
                    Marshal.Copy(IntPtr.Add(sourcePtr, y * sourceStride), rowBuffer, 0, rowLength);
                    Marshal.Copy(rowBuffer, 0, IntPtr.Add(destinationPtr, y * destinationStride), rowLength);
                }
            }

            if (useCache)
            {
                return AddToCacheAndCopy(cacheKey, page.DocumentHandle, renderedBitmap);
            }

            return renderedBitmap;
        }
        finally
        {
            if (bitmapHandle != IntPtr.Zero)
            {
                NativeMethods.FPDFBitmap_Destroy(bitmapHandle);
            }

            NativeMethods.FPDF_ClosePage(pageHandle);
        }
    }

    private void QueueAdjacentPrefetch(PdfPage centerPage, double zoomLevel)
    {
        var documentHandle = centerPage.DocumentHandle;
        var pageCount = NativeMethods.FPDF_GetPageCount(documentHandle);
        if (pageCount <= 1)
        {
            return;
        }

        _ = RunOnPdfiumThread(() =>
        {
            var renderDpi = 96d * zoomLevel;
            foreach (var pageIndex in new[] { centerPage.PageIndex - 1, centerPage.PageIndex + 1 })
            {
                if (pageIndex < 0 || pageIndex >= pageCount)
                {
                    continue;
                }

                var cacheKey = $"{documentHandle}:{pageIndex}:{Math.Round(renderDpi, 1)}";
                if (IsCached(cacheKey))
                {
                    continue;
                }

                if (!TryCreatePage(documentHandle, pageIndex, out var page))
                {
                    continue;
                }

                using var _ = RenderPageInternal(page, renderDpi, useCache: true);
            }
        });
    }

    private static bool TryCreatePage(IntPtr documentHandle, int pageIndex, out PdfPage page)
    {
        page = default!;
        var widthPt = 0d;
        var heightPt = 0d;
        if (NativeMethods.FPDF_GetPageSizeByIndexF(documentHandle, pageIndex, out var pageSize) != 0)
        {
            widthPt = pageSize.Width;
            heightPt = pageSize.Height;
        }

        if (widthPt <= 0d || heightPt <= 0d)
        {
            var pageHandle = NativeMethods.FPDF_LoadPage(documentHandle, pageIndex);
            if (pageHandle == IntPtr.Zero)
            {
                return false;
            }

            widthPt = NativeMethods.FPDF_GetPageWidthF(pageHandle);
            heightPt = NativeMethods.FPDF_GetPageHeightF(pageHandle);
            NativeMethods.FPDF_ClosePage(pageHandle);
        }

        page = new PdfPage(documentHandle, pageIndex, widthPt, heightPt);
        return true;
    }

    private IntPtr GetOrCreateFormHandle(IntPtr documentHandle)
    {
        if (_formHandles.TryGetValue(documentHandle, out var existing))
        {
            return existing;
        }

        var formFillInfo = new FPDF_FORMFILLINFO
        {
            version = 2,
            xfa_disabled = new IntPtr(1)
        };
        formFillInfo.Release = Marshal.GetFunctionPointerForDelegate(_releaseCallback);
        formFillInfo.FFI_SetTimer = Marshal.GetFunctionPointerForDelegate(_setTimerCallback);
        formFillInfo.FFI_KillTimer = Marshal.GetFunctionPointerForDelegate(_killTimerCallback);
        formFillInfo.FFI_GetLocalTime = Marshal.GetFunctionPointerForDelegate(_getLocalTimeCallback);
        var created = NativeMethods.FPDFDOC_InitFormFillEnvironment(documentHandle, ref formFillInfo);
        if (created == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        NativeMethods.FPDF_LoadXFA(documentHandle);
        _formHandles.TryAdd(documentHandle, created);
        return created;
    }

    private void DestroyFormHandle(IntPtr documentHandle)
    {
        if (_formHandles.TryRemove(documentHandle, out var handle) && handle != IntPtr.Zero)
        {
            NativeMethods.FPDFDOC_ExitFormFillEnvironment(handle);
        }
    }

    private static PdfFormFieldType MapFormFieldType(int pdfiumType)
    {
        return pdfiumType switch
        {
            FormFieldTypeTextField => PdfFormFieldType.Text,
            FormFieldTypeCheckBox => PdfFormFieldType.CheckBox,
            FormFieldTypeRadioButton => PdfFormFieldType.RadioButton,
            FormFieldTypeComboBox => PdfFormFieldType.ComboBox,
            FormFieldTypeSignature => PdfFormFieldType.Signature,
            FormFieldTypeListBox => PdfFormFieldType.ComboBox,
            FormFieldTypePushButton => PdfFormFieldType.Unknown,
            _ => PdfFormFieldType.Unknown
        };
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

    private static IReadOnlyList<int> NormalizePageNumbers(IReadOnlyList<int> pageNumbers, int pageCount)
    {
        return pageNumbers
            .Select(page => Math.Clamp(page, 1, pageCount))
            .Distinct()
            .OrderBy(page => page)
            .ToArray();
    }

    private static int NormalizeRotationIndex(int rotationDegrees)
    {
        var normalized = ((rotationDegrees % 360) + 360) % 360;
        return normalized switch
        {
            90 => 1,
            180 => 2,
            270 => 3,
            _ => 0
        };
    }

    private static string ResolveEncryptionDescription(int revision)
    {
        return revision switch
        {
            6 => "AES-256",
            5 => "AES-256",
            4 => "AES-128",
            3 => "RC4-128",
            2 => "RC4-40",
            _ => "なし"
        };
    }

    private static void ImportPages(IntPtr destinationDocument, IntPtr sourceDocument, IReadOnlyList<int> pages)
    {
        var pageRange = string.Join(',', pages);
        if (NativeMethods.FPDF_ImportPages(destinationDocument, sourceDocument, pageRange, NativeMethods.FPDF_GetPageCount(destinationDocument)) == 0)
        {
            throw new InvalidOperationException($"Failed to import pages '{pageRange}'.");
        }
    }

    private void SaveDocumentCopy(IntPtr documentHandle, string outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        var fileWrite = new FPDF_FILEWRITE
        {
            version = 1,
            WriteBlock = Marshal.GetFunctionPointerForDelegate(_saveCallback)
        };
        var previousStream = SaveStreamContext.Value;
        SaveStreamContext.Value = stream;
        try
        {
            if (NativeMethods.FPDF_SaveAsCopy(documentHandle, ref fileWrite, PdfSaveNoIncremental) == 0)
            {
                throw new InvalidOperationException("Failed to save PDF document.");
            }
        }
        finally
        {
            SaveStreamContext.Value = previousStream;
        }
    }

    private static IEnumerable<int> ExpandToPdfiumCharEvents(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            var utf16 = rune.ToString();
            for (var index = 0; index < utf16.Length; index++)
            {
                yield return utf16[index];
            }
        }
    }

    private int OnSetFormTimer(int elapseMs, IntPtr timerProc)
    {
        if (elapseMs <= 0 || timerProc == IntPtr.Zero)
        {
            return 0;
        }

        var timerId = Interlocked.Increment(ref _nextFormTimerId);
        var interval = TimeSpan.FromMilliseconds(elapseMs);
        var timer = new System.Threading.Timer(
            static state => InvokeFormTimerCallback(state),
            (Service: this, TimerId: timerId, TimerProc: timerProc),
            interval,
            interval);
        _formTimers[timerId] = new TimerRegistration(timer);
        return timerId;
    }

    private void OnKillFormTimer(int timerId)
    {
        StopFormTimer(timerId);
    }

    private static void InvokeFormTimerCallback(object? state)
    {
        if (state is not ValueTuple<PdfiumRenderService, int, IntPtr> info)
        {
            return;
        }

        var service = info.Item1;
        var timerId = info.Item2;
        var timerProc = info.Item3;
        if (!service._formTimers.ContainsKey(timerId))
        {
            return;
        }

        var callback = Marshal.GetDelegateForFunctionPointer<FormFillTimerProcCallback>(timerProc);
        callback(timerId);
    }

    private void StopFormTimer(int timerId)
    {
        if (_formTimers.TryRemove(timerId, out var registration))
        {
            registration.Timer.Dispose();
        }
    }

    private static void OnGetFormLocalTime(IntPtr systemTimePtr)
    {
        if (systemTimePtr == IntPtr.Zero)
        {
            return;
        }

        var now = DateTime.Now;
        var systemTime = new FPDF_SYSTEMTIME
        {
            wYear = (ushort)now.Year,
            wMonth = (ushort)now.Month,
            wDayOfWeek = (ushort)now.DayOfWeek,
            wDay = (ushort)now.Day,
            wHour = (ushort)now.Hour,
            wMinute = (ushort)now.Minute,
            wSecond = (ushort)now.Second,
            wMilliseconds = (ushort)now.Millisecond
        };
        Marshal.StructureToPtr(systemTime, systemTimePtr, fDeleteOld: false);
    }

    private static int SaveDocumentWriteBlock(IntPtr pThis, IntPtr data, uint size)
    {
        var stream = SaveStreamContext.Value;
        if (stream is null)
        {
            return 0;
        }

        var buffer = new byte[size];
        Marshal.Copy(data, buffer, 0, checked((int)size));
        stream.Write(buffer, 0, checked((int)size));
        return 1;
    }

    private static string GetAttachmentName(IntPtr attachment)
    {
        var required = NativeMethods.FPDFAttachment_GetName(attachment, IntPtr.Zero, 0);
        if (required <= 0)
        {
            return string.Empty;
        }

        var buffer = Marshal.AllocHGlobal((int)required);
        try
        {
            NativeMethods.FPDFAttachment_GetName(attachment, buffer, required);
            return Marshal.PtrToStringUni(buffer) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static int GetAttachmentSize(IntPtr attachment)
    {
        NativeMethods.FPDFAttachment_GetFile(attachment, null, 0, out var length);
        return checked((int)Math.Min(length, int.MaxValue));
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
            DestroyFormHandle(handle);
            NativeMethods.FPDF_CloseDocument(handle);
            RemoveCacheForDocument(handle);
            _memoryMeasuredDocuments.TryRemove(handle, out _);
            if (_documentLoadContexts.TryRemove(handle, out var context))
            {
                context.Dispose();
            }
        }
    }

    private void RemoveCacheForDocument(IntPtr handle)
    {
        lock (_cacheLock)
        {
            var node = _pageCacheLru.First;
            while (node is not null)
            {
                var next = node.Next;
                if (node.Value.DocumentHandle == handle)
                {
                    _pageCacheLru.Remove(node);
                    _pageCache.Remove(node.Value.Key);
                    node.Value.Bitmap.Dispose();
                }

                node = next;
            }
        }
    }

    private bool IsCached(string cacheKey)
    {
        lock (_cacheLock)
        {
            return _pageCache.ContainsKey(cacheKey);
        }
    }

    private bool TryGetCachedBitmapCopy(string cacheKey, out SKBitmap bitmap)
    {
        lock (_cacheLock)
        {
            if (_pageCache.TryGetValue(cacheKey, out var node))
            {
                _pageCacheLru.Remove(node);
                _pageCacheLru.AddFirst(node);
                bitmap = node.Value.Bitmap.Copy();
                return true;
            }
        }

        bitmap = null!;
        return false;
    }

    private SKBitmap AddToCacheAndCopy(string cacheKey, IntPtr documentHandle, SKBitmap bitmap)
    {
        lock (_cacheLock)
        {
            if (_pageCache.TryGetValue(cacheKey, out var existing))
            {
                existing.Value.Bitmap.Dispose();
                existing.Value = existing.Value with { Bitmap = bitmap };
                _pageCacheLru.Remove(existing);
                _pageCacheLru.AddFirst(existing);
                return existing.Value.Bitmap.Copy();
            }

            var node = new LinkedListNode<CacheEntry>(new CacheEntry(cacheKey, bitmap, documentHandle));
            _pageCacheLru.AddFirst(node);
            _pageCache[cacheKey] = node;

            while (_pageCache.Count > MaxPageCacheEntries && _pageCacheLru.Last is { } last)
            {
                _pageCacheLru.RemoveLast();
                _pageCache.Remove(last.Value.Key);
                last.Value.Bitmap.Dispose();
            }

            return bitmap.Copy();
        }
    }

    private void ClearCache()
    {
        lock (_cacheLock)
        {
            var node = _pageCacheLru.First;
            while (node is not null)
            {
                node.Value.Bitmap.Dispose();
                node = node.Next;
            }

            _pageCacheLru.Clear();
            _pageCache.Clear();
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
        public static extern IntPtr FPDF_LoadCustomDocument(IntPtr fileAccess, [MarshalAs(UnmanagedType.LPUTF8Str)] string? password);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDF_CreateNewDocument();

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
        public static extern int FPDF_ImportPages(IntPtr destination_doc, IntPtr source_doc, [MarshalAs(UnmanagedType.LPUTF8Str)] string? pagerange, int index);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDF_CopyViewerPreferences(IntPtr destination_doc, IntPtr source_doc);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDF_SaveAsCopy(IntPtr document, ref FPDF_FILEWRITE fileWrite, uint flags);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDFPage_Delete(IntPtr document, int page_index);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDFPage_SetRotation(IntPtr page, int rotate);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDFDOC_InitFormFillEnvironment(IntPtr document, ref FPDF_FORMFILLINFO formInfo);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDFDOC_ExitFormFillEnvironment(IntPtr formHandle);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDF_LoadXFA(IntPtr document);

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

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFPage_GetAnnotCount(IntPtr page);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDFPage_GetAnnot(IntPtr page, int index);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDFPage_CloseAnnot(IntPtr annot);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFAnnot_GetSubtype(IntPtr annot);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFAnnot_GetRect(IntPtr annot, out FS_RECTF rect);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint FPDFAnnot_GetStringValue(IntPtr annot, IntPtr key, IntPtr value, uint buflen);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFAnnot_GetFormFieldType(IntPtr formHandle, IntPtr annot);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FORM_OnLButtonDown(IntPtr formHandle, IntPtr page, int modifier, double pageX, double pageY);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FORM_OnLButtonUp(IntPtr formHandle, IntPtr page, int modifier, double pageX, double pageY);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FORM_OnChar(IntPtr formHandle, IntPtr page, int nChar, int modifier);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FORM_ForceToKillFocus(IntPtr formHandle);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFDoc_GetAttachmentCount(IntPtr document);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDFDoc_GetAttachment(IntPtr document, int index);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint FPDFAttachment_GetName(IntPtr attachment, IntPtr buffer, uint buflen);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFAttachment_GetFile(IntPtr attachment, [Out] byte[]? buffer, uint buflen, out uint out_buflen);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint FPDF_GetDocPermissions(IntPtr document);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDF_GetSecurityHandlerRevision(IntPtr document);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FPDF_FILEACCESS
    {
        public uint m_FileLen;
        public IntPtr m_GetBlock;
        public IntPtr m_Param;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct FS_SIZEF
    {
        public readonly float Width;
        public readonly float Height;
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
    private struct FPDF_FORMFILLINFO
    {
        public int version;
        public IntPtr Release;
        public IntPtr FFI_Invalidate;
        public IntPtr FFI_OutputSelectedRect;
        public IntPtr FFI_SetCursor;
        public IntPtr FFI_SetTimer;
        public IntPtr FFI_KillTimer;
        public IntPtr FFI_GetLocalTime;
        public IntPtr FFI_OnChange;
        public IntPtr FFI_GetPage;
        public IntPtr FFI_GetCurrentPage;
        public IntPtr FFI_GetRotation;
        public IntPtr FFI_ExecuteNamedAction;
        public IntPtr FFI_SetTextFieldFocus;
        public IntPtr FFI_DoURIAction;
        public IntPtr FFI_DoGoToAction;
        public IntPtr m_pJsPlatform;
        public IntPtr xfa_disabled;
        public IntPtr FFI_EmailTo;
        public IntPtr FFI_GetPlatform;
        public IntPtr FFI_GetLanguage;
        public IntPtr FFI_DownloadFromURL;
        public IntPtr FFI_PostRequestURL;
        public IntPtr FFI_PutRequestURL;
        public IntPtr FFI_UploadTo;
        public IntPtr FFI_GetStringFromFile;
        public IntPtr FFI_DeleteFileParam;
        public IntPtr FFI_SetStringToFile;
        public IntPtr FFI_GotoURL;
        public IntPtr FFI_GetFilePath;
        public IntPtr FFI_Alert;
        public IntPtr FFI_Print;
        public IntPtr FFI_SubmitForm;
        public IntPtr FFI_GotoPage;
        public IntPtr FFI_Browse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FPDF_FILEWRITE
    {
        public int version;
        public IntPtr WriteBlock;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FPDF_SYSTEMTIME
    {
        public ushort wYear;
        public ushort wMonth;
        public ushort wDayOfWeek;
        public ushort wDay;
        public ushort wHour;
        public ushort wMinute;
        public ushort wSecond;
        public ushort wMilliseconds;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ReleaseCallback(IntPtr formFillInfo);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int WriteBlockCallback(IntPtr pThis, IntPtr data, uint size);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int FormFillSetTimerCallback(int elapse, IntPtr timerProc);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FormFillKillTimerCallback(int timerId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FormFillGetLocalTimeCallback(IntPtr systemTime);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FormFillTimerProcCallback(int timerId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetBlockCallback(IntPtr param, uint position, IntPtr buffer, uint size);

    private readonly record struct TimerRegistration(System.Threading.Timer Timer);

    private readonly record struct CacheEntry(string Key, SKBitmap Bitmap, IntPtr DocumentHandle);

    private sealed class DocumentLoadContext : IDisposable
    {
        private readonly FileStream _stream;
        private readonly object _sync = new();
        private readonly GCHandle _selfHandle;
        private readonly IntPtr _accessInfoPtr;
        private bool _disposed;

        public DocumentLoadContext(string filePath)
        {
            _stream = new FileStream(filePath, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read);
            if (_stream.Length > uint.MaxValue)
            {
                throw new InvalidOperationException("PDF larger than 4GB is not supported by the current PDFium custom stream bridge.");
            }

            GetBlock = ReadBlock;
            _selfHandle = GCHandle.Alloc(this, GCHandleType.Normal);
            var accessInfo = new FPDF_FILEACCESS
            {
                m_FileLen = checked((uint)_stream.Length),
                m_GetBlock = Marshal.GetFunctionPointerForDelegate(GetBlock),
                m_Param = GCHandle.ToIntPtr(_selfHandle)
            };

            _accessInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<FPDF_FILEACCESS>());
            Marshal.StructureToPtr(accessInfo, _accessInfoPtr, false);
        }

        /// <summary>アンマネージドメモリ上に固定された FPDF_FILEACCESS へのポインタ。</summary>
        public IntPtr AccessInfoPtr => _accessInfoPtr;

        public GetBlockCallback GetBlock { get; }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stream.Dispose();

            // アンマネージド FPDF_FILEACCESS を先に解放し、次に GCHandle を解放する。
            // FPDF_FILEACCESS 内の m_Param が GCHandle を指しているため、この順序を守る。
            if (_accessInfoPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_accessInfoPtr);
            }

            if (_selfHandle.IsAllocated)
            {
                _selfHandle.Free();
            }
        }

        private int ReadBlock(IntPtr param, uint position, IntPtr buffer, uint size)
        {
            if (_disposed || size == 0)
            {
                return 0;
            }

            var endPosition = checked((long)position + size);
            if (endPosition > _stream.Length)
            {
                return 0;
            }

            var sizeInt = checked((int)size);
            var bytes = ArrayPool<byte>.Shared.Rent(sizeInt);
            try
            {
                lock (_sync)
                {
                    _stream.Position = position;
                    var read = _stream.Read(bytes, 0, sizeInt);
                    if (read != sizeInt)
                    {
                        return 0;
                    }
                }

                Marshal.Copy(bytes, 0, buffer, sizeInt);
                return 1;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(bytes);
            }
        }
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
