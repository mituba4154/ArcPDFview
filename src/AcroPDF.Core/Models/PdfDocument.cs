#nullable enable

using System.IO;

namespace AcroPDF.Core.Models;

/// <summary>
/// 開かれている PDF ドキュメントを表します。
/// </summary>
public sealed class PdfDocument : IDisposable
{
    private readonly Action<IntPtr>? _releaseHandle;
    private int _isDisposed;

    /// <summary>
    /// <see cref="PdfDocument"/> の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="filePath">ファイルパス。</param>
    /// <param name="nativeHandle">ネイティブのドキュメントハンドル。</param>
    /// <param name="pages">ページ一覧。</param>
    /// <param name="releaseHandle">破棄時に呼び出すハンドル解放処理。</param>
    public PdfDocument(string filePath, IntPtr nativeHandle, IReadOnlyList<PdfPage> pages, Action<IntPtr>? releaseHandle)
    {
        FilePath = filePath;
        NativeHandle = nativeHandle;
        Pages = pages;
        _releaseHandle = releaseHandle;
    }

    /// <summary>
    /// ファイルパスを取得します。
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// ファイル名を取得します。
    /// </summary>
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>
    /// ネイティブのドキュメントハンドルを取得します。
    /// </summary>
    public IntPtr NativeHandle { get; }

    /// <summary>
    /// ページ一覧を取得します。
    /// </summary>
    public IReadOnlyList<PdfPage> Pages { get; }

    /// <summary>
    /// 総ページ数を取得します。
    /// </summary>
    public int PageCount => Pages.Count;

    /// <summary>
    /// ネイティブリソースを解放します。
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
        {
            return;
        }

        if (NativeHandle != IntPtr.Zero)
        {
            _releaseHandle?.Invoke(NativeHandle);
        }

        GC.SuppressFinalize(this);
    }
}
