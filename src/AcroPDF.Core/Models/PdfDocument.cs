#nullable enable

using System.IO;

namespace AcroPDF.Core.Models;

/// <summary>
/// 開かれている PDF ドキュメントを表します。
/// </summary>
public sealed class PdfDocument : IDisposable
{
    private readonly Action<IntPtr>? _releaseHandle;
    private readonly List<Annotation> _annotations = [];
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
    /// 注釈一覧を取得します。
    /// </summary>
    public IReadOnlyList<Annotation> Annotations => _annotations;

    /// <summary>
    /// 未保存の変更があるかどうかを取得します。
    /// </summary>
    public bool IsModified { get; private set; }

    /// <summary>
    /// 注釈一覧を置き換えます。
    /// </summary>
    /// <param name="annotations">設定する注釈一覧。</param>
    public void SetAnnotations(IEnumerable<Annotation> annotations)
    {
        _annotations.Clear();
        _annotations.AddRange(annotations);
        IsModified = false;
    }

    /// <summary>
    /// 注釈を追加します。
    /// </summary>
    /// <param name="annotation">追加する注釈。</param>
    public void AddAnnotation(Annotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        _annotations.Add(annotation);
        MarkModified();
    }

    /// <summary>
    /// 注釈を削除します。
    /// </summary>
    /// <param name="annotationId">削除対象の注釈 ID。</param>
    /// <returns>削除できた場合は <see langword="true"/>。</returns>
    public bool RemoveAnnotation(Guid annotationId)
    {
        var index = _annotations.FindIndex(annotation => annotation.Id == annotationId);
        if (index < 0)
        {
            return false;
        }

        _annotations.RemoveAt(index);
        MarkModified();
        return true;
    }

    /// <summary>
    /// 未保存状態にします。
    /// </summary>
    public void MarkModified()
    {
        IsModified = true;
    }

    /// <summary>
    /// 保存済み状態にします。
    /// </summary>
    public void ClearModified()
    {
        IsModified = false;
    }

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
