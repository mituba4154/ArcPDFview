#nullable enable

using AcroPDF.Core.Models;
using SkiaSharp;

namespace AcroPDF.Services.Interfaces;

/// <summary>
/// PDF の読み込みとレンダリング機能を提供します。
/// </summary>
public interface IPdfRenderService : IDisposable
{
    /// <summary>
    /// PDF ファイルを開きます。
    /// </summary>
    /// <param name="filePath">ファイルパス。</param>
    /// <param name="password">パスワード。不要な場合は <see langword="null"/>。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>開かれた <see cref="PdfDocument"/>。</returns>
    Task<PdfDocument> OpenAsync(string filePath, string? password = null, CancellationToken ct = default);

    /// <summary>
    /// 指定ページをビットマップとしてレンダリングします。
    /// </summary>
    /// <param name="page">対象ページ。</param>
    /// <param name="zoomLevel">ズーム倍率（1.0 = 100%）。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>レンダリング済みビットマップ。</returns>
    Task<SKBitmap> RenderPageAsync(PdfPage page, double zoomLevel, CancellationToken ct = default);

    /// <summary>
    /// ドキュメントを閉じます。
    /// </summary>
    /// <param name="document">対象ドキュメント。</param>
    void Close(PdfDocument document);
}
