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
    /// ビュー表示用に現在ページ（必要に応じて見開き/回転）をレンダリングします。
    /// </summary>
    /// <param name="document">対象ドキュメント。</param>
    /// <param name="currentPage">現在ページ番号（1 始まり）。</param>
    /// <param name="zoomLevel">ズーム倍率（1.0 = 100%）。</param>
    /// <param name="twoPageMode">見開きモード。</param>
    /// <param name="rotationDegrees">回転角度。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>レンダリング結果。ページ不正時は <see langword="null"/>。</returns>
    Task<SKBitmap?> RenderCompositePageAsync(
        PdfDocument document,
        int currentPage,
        double zoomLevel,
        bool twoPageMode,
        int rotationDegrees,
        CancellationToken ct = default);

    /// <summary>
    /// ドキュメントを閉じます。
    /// </summary>
    /// <param name="document">対象ドキュメント。</param>
    void Close(PdfDocument document);
}
