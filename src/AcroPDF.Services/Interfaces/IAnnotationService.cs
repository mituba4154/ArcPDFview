#nullable enable

using AcroPDF.Core.Models;

namespace AcroPDF.Services.Interfaces;

/// <summary>
/// PDF 注釈の読み書き機能を提供します。
/// </summary>
public interface IAnnotationService
{
    /// <summary>
    /// ドキュメントから注釈を読み込みます。
    /// </summary>
    /// <param name="document">対象ドキュメント。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>読み込んだ注釈一覧。</returns>
    Task<IReadOnlyList<Annotation>> LoadAnnotationsAsync(PdfDocument document, CancellationToken ct = default);

    /// <summary>
    /// ドキュメントへ注釈を保存します。
    /// </summary>
    /// <param name="document">対象ドキュメント。</param>
    /// <param name="outputPath">出力先ファイルパス。省略時は元ファイルに保存します。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>完了タスク。</returns>
    Task SaveAnnotationsAsync(PdfDocument document, string? outputPath = null, CancellationToken ct = default);

    /// <summary>
    /// 画面座標を PDF 座標へ変換します。
    /// </summary>
    /// <param name="screenX">画面 X 座標（px）。</param>
    /// <param name="screenY">画面 Y 座標（px）。</param>
    /// <param name="dpiScale">DPI スケール。</param>
    /// <param name="pageHeightPt">ページ高さ（pt）。</param>
    /// <returns>変換後の PDF 座標。</returns>
    AnnotationPoint ConvertScreenToPdf(double screenX, double screenY, double dpiScale, double pageHeightPt);

    /// <summary>
    /// PDF 座標を画面座標へ変換します。
    /// </summary>
    /// <param name="pdfX">PDF X 座標（pt）。</param>
    /// <param name="pdfY">PDF Y 座標（pt）。</param>
    /// <param name="dpiScale">DPI スケール。</param>
    /// <param name="pageHeightPt">ページ高さ（pt）。</param>
    /// <returns>変換後の画面座標。</returns>
    AnnotationPoint ConvertPdfToScreen(double pdfX, double pdfY, double dpiScale, double pageHeightPt);
}

