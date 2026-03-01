#nullable enable

using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using AcroPDF.Core.Models;
using AcroPDF.Services.Interfaces;

namespace AcroPDF.Services;

/// <summary>
/// PDFium を利用したテキスト検索サービスです。
/// </summary>
public sealed class SearchService : ISearchService
{
    private const int SearchFlagMatchCase = 0x01;

    /// <inheritdoc />
    public Task<IReadOnlyList<SearchResult>> SearchAsync(
        PdfDocument document,
        string query,
        SearchOptions options,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult<IReadOnlyList<SearchResult>>([]);
        }

        return Task.Run<IReadOnlyList<SearchResult>>(() =>
        {
            var results = new List<SearchResult>();
            var matchIndex = 0;

            foreach (var page in document.Pages)
            {
                ct.ThrowIfCancellationRequested();

                if (options.UseRegex)
                {
                    foreach (var regexResult in SearchRegexOnPage(page, query, options, matchIndex, ct))
                    {
                        results.Add(regexResult);
                        matchIndex++;
                    }

                    continue;
                }

                foreach (var pageResult in SearchPlainOnPage(page, query, options, matchIndex, ct))
                {
                    results.Add(pageResult);
                    matchIndex++;
                }
            }

            return (IReadOnlyList<SearchResult>)results;
        }, ct);
    }

    /// <summary>
    /// PDF の目次（ブックマーク）を取得します。
    /// </summary>
    /// <param name="document">対象ドキュメント。</param>
    /// <returns>ブックマークツリー。</returns>
    public IReadOnlyList<PdfBookmarkItem> GetBookmarks(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return ReadBookmarks(document, IntPtr.Zero);
    }

    /// <summary>
    /// 指定矩形内の文字列を抽出します。
    /// </summary>
    /// <param name="page">対象ページ。</param>
    /// <param name="selectionBounds">選択矩形。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>抽出結果。</returns>
    public Task<TextSelectionResult> SelectTextAsync(PdfPage page, PdfTextBounds selectionBounds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(page);

        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            var pageHandle = NativeMethods.FPDF_LoadPage(page.DocumentHandle, page.PageIndex);
            if (pageHandle == IntPtr.Zero)
            {
                return new TextSelectionResult(string.Empty, []);
            }

            try
            {
                var textPage = NativeMethods.FPDFText_LoadPage(pageHandle);
                if (textPage == IntPtr.Zero)
                {
                    return new TextSelectionResult(string.Empty, []);
                }

                try
                {
                    var charCount = NativeMethods.FPDFText_CountChars(textPage);
                    if (charCount <= 0)
                    {
                        return new TextSelectionResult(string.Empty, []);
                    }

                    var fullText = GetTextByRange(textPage, 0, charCount).Replace("\0", string.Empty);
                    var builder = new StringBuilder();
                    var bounds = new List<PdfTextBounds>();

                    for (var index = 0; index < charCount && index < fullText.Length; index++)
                    {
                        ct.ThrowIfCancellationRequested();

                        if (!TryGetCharBounds(textPage, index, out var charBounds))
                        {
                            continue;
                        }

                        if (!charBounds.Intersects(selectionBounds))
                        {
                            continue;
                        }

                        bounds.Add(charBounds);
                        builder.Append(fullText[index]);
                    }

                    return new TextSelectionResult(builder.ToString(), bounds);
                }
                finally
                {
                    NativeMethods.FPDFText_ClosePage(textPage);
                }
            }
            finally
            {
                NativeMethods.FPDF_ClosePage(pageHandle);
            }
        }, ct);
    }

    private static IEnumerable<SearchResult> SearchPlainOnPage(
        PdfPage page,
        string query,
        SearchOptions options,
        int firstMatchIndex,
        CancellationToken ct)
    {
        var pageHandle = NativeMethods.FPDF_LoadPage(page.DocumentHandle, page.PageIndex);
        if (pageHandle == IntPtr.Zero)
        {
            yield break;
        }

        try
        {
            var textPage = NativeMethods.FPDFText_LoadPage(pageHandle);
            if (textPage == IntPtr.Zero)
            {
                yield break;
            }

            try
            {
                var flags = options.CaseSensitive ? SearchFlagMatchCase : 0;
                var handle = NativeMethods.FPDFText_FindStart(textPage, query + "\0", flags, 0);
                if (handle == IntPtr.Zero)
                {
                    yield break;
                }

                try
                {
                    var localIndex = 0;
                    while (NativeMethods.FPDFText_FindNext(handle) != 0)
                    {
                        ct.ThrowIfCancellationRequested();

                        var startIndex = NativeMethods.FPDFText_GetSchResultIndex(handle);
                        var count = NativeMethods.FPDFText_GetSchCount(handle);

                        if (startIndex < 0 || count <= 0)
                        {
                            continue;
                        }

                        var bounds = CalculateBounds(textPage, startIndex, count);
                        if (bounds is null)
                        {
                            continue;
                        }

                        yield return new SearchResult(page.PageNumber, bounds.Value, firstMatchIndex + localIndex);
                        localIndex++;
                    }
                }
                finally
                {
                    NativeMethods.FPDFText_FindClose(handle);
                }
            }
            finally
            {
                NativeMethods.FPDFText_ClosePage(textPage);
            }
        }
        finally
        {
            NativeMethods.FPDF_ClosePage(pageHandle);
        }
    }

    private static IEnumerable<SearchResult> SearchRegexOnPage(
        PdfPage page,
        string query,
        SearchOptions options,
        int firstMatchIndex,
        CancellationToken ct)
    {
        var pageHandle = NativeMethods.FPDF_LoadPage(page.DocumentHandle, page.PageIndex);
        if (pageHandle == IntPtr.Zero)
        {
            yield break;
        }

        try
        {
            var textPage = NativeMethods.FPDFText_LoadPage(pageHandle);
            if (textPage == IntPtr.Zero)
            {
                yield break;
            }

            try
            {
                var charCount = NativeMethods.FPDFText_CountChars(textPage);
                if (charCount <= 0)
                {
                    yield break;
                }

                var fullText = GetTextByRange(textPage, 0, charCount);
                var regexOptions = options.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                var regex = new Regex(query, regexOptions, TimeSpan.FromSeconds(1));

                var localIndex = 0;
                foreach (Match match in regex.Matches(fullText))
                {
                    ct.ThrowIfCancellationRequested();

                    if (!match.Success || match.Length <= 0)
                    {
                        continue;
                    }

                    var bounds = CalculateBounds(textPage, match.Index, match.Length);
                    if (bounds is null)
                    {
                        continue;
                    }

                    yield return new SearchResult(page.PageNumber, bounds.Value, firstMatchIndex + localIndex);
                    localIndex++;
                }
            }
            finally
            {
                NativeMethods.FPDFText_ClosePage(textPage);
            }
        }
        finally
        {
            NativeMethods.FPDF_ClosePage(pageHandle);
        }
    }

    private static PdfTextBounds? CalculateBounds(IntPtr textPage, int startIndex, int count)
    {
        var hasBounds = false;
        var left = 0d;
        var right = 0d;
        var top = 0d;
        var bottom = 0d;

        for (var offset = 0; offset < count; offset++)
        {
            if (!TryGetCharBounds(textPage, startIndex + offset, out var charBounds))
            {
                continue;
            }

            if (!hasBounds)
            {
                hasBounds = true;
                left = Math.Min(charBounds.Left, charBounds.Right);
                right = Math.Max(charBounds.Left, charBounds.Right);
                top = Math.Max(charBounds.Top, charBounds.Bottom);
                bottom = Math.Min(charBounds.Top, charBounds.Bottom);
                continue;
            }

            left = Math.Min(left, Math.Min(charBounds.Left, charBounds.Right));
            right = Math.Max(right, Math.Max(charBounds.Left, charBounds.Right));
            top = Math.Max(top, Math.Max(charBounds.Top, charBounds.Bottom));
            bottom = Math.Min(bottom, Math.Min(charBounds.Top, charBounds.Bottom));
        }

        return hasBounds ? new PdfTextBounds(left, top, right, bottom) : null;
    }

    private static bool TryGetCharBounds(IntPtr textPage, int charIndex, out PdfTextBounds bounds)
    {
        if (NativeMethods.FPDFText_GetCharBox(textPage, charIndex, out var left, out var right, out var bottom, out var top) == 0)
        {
            bounds = default;
            return false;
        }

        bounds = new PdfTextBounds(left, top, right, bottom);
        return true;
    }

    private static string GetTextByRange(IntPtr textPage, int startIndex, int count)
    {
        if (count <= 0)
        {
            return string.Empty;
        }

        var buffer = new ushort[count + 1];
        var copied = NativeMethods.FPDFText_GetText(textPage, startIndex, count, buffer);
        if (copied <= 0)
        {
            return string.Empty;
        }

        var chars = MemoryMarshal.Cast<ushort, char>(buffer.AsSpan(0, Math.Min(copied, buffer.Length)));
        return new string(chars).TrimEnd('\0');
    }

    private static IReadOnlyList<PdfBookmarkItem> ReadBookmarks(PdfDocument document, IntPtr parentBookmark)
    {
        var items = new List<PdfBookmarkItem>();
        var bookmark = NativeMethods.FPDFBookmark_GetFirstChild(document.NativeHandle, parentBookmark);

        while (bookmark != IntPtr.Zero)
        {
            var title = GetBookmarkTitle(bookmark);
            var pageNumber = 0;
            var destination = NativeMethods.FPDFBookmark_GetDest(document.NativeHandle, bookmark);
            if (destination != IntPtr.Zero)
            {
                var pageIndex = NativeMethods.FPDFDest_GetDestPageIndex(document.NativeHandle, destination);
                pageNumber = pageIndex >= 0 ? pageIndex + 1 : 0;
            }

            var children = ReadBookmarks(document, bookmark);
            items.Add(new PdfBookmarkItem(title, pageNumber, children));
            bookmark = NativeMethods.FPDFBookmark_GetNextSibling(document.NativeHandle, bookmark);
        }

        return items;
    }

    private static string GetBookmarkTitle(IntPtr bookmark)
    {
        var bytesLength = NativeMethods.FPDFBookmark_GetTitle(bookmark, IntPtr.Zero, 0);
        if (bytesLength <= 0)
        {
            return string.Empty;
        }

        var buffer = Marshal.AllocHGlobal(checked((int)bytesLength));
        try
        {
            NativeMethods.FPDFBookmark_GetTitle(bookmark, buffer, bytesLength);
            return Marshal.PtrToStringUni(buffer) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static class NativeMethods
    {
        private const string LibraryName = "pdfium";

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDF_LoadPage(IntPtr document, int page_index);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDF_ClosePage(IntPtr page);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDFText_LoadPage(IntPtr page);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDFText_ClosePage(IntPtr text_page);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDFText_FindStart(IntPtr text_page, [MarshalAs(UnmanagedType.LPWStr)] string findwhat, int flags, int start_index);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFText_FindNext(IntPtr handle);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDFText_FindClose(IntPtr handle);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFText_GetSchResultIndex(IntPtr handle);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFText_GetSchCount(IntPtr handle);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFText_CountChars(IntPtr text_page);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFText_GetText(IntPtr text_page, int start_index, int count, [Out] ushort[] result);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFText_GetCharBox(IntPtr text_page, int index, out double left, out double right, out double bottom, out double top);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDFBookmark_GetFirstChild(IntPtr document, IntPtr bookmark);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDFBookmark_GetNextSibling(IntPtr document, IntPtr bookmark);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint FPDFBookmark_GetTitle(IntPtr bookmark, IntPtr buffer, uint buflen);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDFBookmark_GetDest(IntPtr document, IntPtr bookmark);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFDest_GetDestPageIndex(IntPtr document, IntPtr dest);
    }
}
