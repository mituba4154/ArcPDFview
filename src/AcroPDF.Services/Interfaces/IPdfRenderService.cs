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
    /// 指定ページのフォームフィールドを取得します。
    /// </summary>
    /// <param name="page">対象ページ。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>フォームフィールド一覧。</returns>
    Task<IReadOnlyList<PdfFormField>> GetFormFieldsAsync(PdfPage page, CancellationToken ct = default);

    /// <summary>
    /// フォームフィールド入力を PDFium FormFill API に送信します。
    /// </summary>
    /// <param name="page">対象ページ。</param>
    /// <param name="field">対象フィールド。</param>
    /// <param name="textValue">テキスト値。</param>
    /// <param name="isChecked">チェック状態。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>入力処理を実行した場合は <see langword="true"/>。</returns>
    Task<bool> ApplyFormFieldInputAsync(
        PdfPage page,
        PdfFormField field,
        string? textValue,
        bool? isChecked = null,
        CancellationToken ct = default);

    /// <summary>
    /// 印刷/プレビュー用に指定 DPI でページをレンダリングします。
    /// </summary>
    /// <param name="page">対象ページ。</param>
    /// <param name="dpi">描画 DPI。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>レンダリング済みビットマップ。</returns>
    Task<SKBitmap> RenderPageForPrintAsync(PdfPage page, int dpi, CancellationToken ct = default);

    /// <summary>
    /// 指定ページを新規 PDF へ抽出して保存します。
    /// </summary>
    /// <param name="document">対象ドキュメント。</param>
    /// <param name="pageNumbers">抽出するページ番号（1 始まり）。</param>
    /// <param name="outputPath">出力先 PDF パス。</param>
    /// <param name="ct">キャンセルトークン。</param>
    Task ExtractPagesAsync(PdfDocument document, IReadOnlyList<int> pageNumbers, string outputPath, CancellationToken ct = default);

    /// <summary>
    /// 指定ページを削除して PDF を保存します。
    /// </summary>
    /// <param name="document">対象ドキュメント。</param>
    /// <param name="pageNumbers">削除するページ番号（1 始まり）。</param>
    /// <param name="outputPath">出力先 PDF パス。省略時は元ファイルへ保存します。</param>
    /// <param name="ct">キャンセルトークン。</param>
    Task DeletePagesAsync(PdfDocument document, IReadOnlyList<int> pageNumbers, string? outputPath = null, CancellationToken ct = default);

    /// <summary>
    /// 指定ページを回転して PDF を保存します。
    /// </summary>
    /// <param name="document">対象ドキュメント。</param>
    /// <param name="pageNumbers">回転対象ページ番号（1 始まり）。</param>
    /// <param name="rotationDegrees">回転角度（90/180/270）。</param>
    /// <param name="outputPath">出力先 PDF パス。省略時は元ファイルへ保存します。</param>
    /// <param name="ct">キャンセルトークン。</param>
    Task RotatePagesAsync(PdfDocument document, IReadOnlyList<int> pageNumbers, int rotationDegrees, string? outputPath = null, CancellationToken ct = default);

    /// <summary>
    /// 複数 PDF を結合して保存します。
    /// </summary>
    /// <param name="inputFilePaths">入力 PDF パス一覧。</param>
    /// <param name="outputPath">出力先 PDF パス。</param>
    /// <param name="ct">キャンセルトークン。</param>
    Task MergeAsync(IReadOnlyList<string> inputFilePaths, string outputPath, CancellationToken ct = default);

    /// <summary>
    /// ページ順を並び替えて保存します。
    /// </summary>
    /// <param name="document">対象ドキュメント。</param>
    /// <param name="pageOrder">新しいページ順（1 始まり）。</param>
    /// <param name="outputPath">出力先 PDF パス。省略時は元ファイルへ保存します。</param>
    /// <param name="ct">キャンセルトークン。</param>
    Task ReorderPagesAsync(PdfDocument document, IReadOnlyList<int> pageOrder, string? outputPath = null, CancellationToken ct = default);

    /// <summary>
    /// 埋め込みファイル一覧を取得します。
    /// </summary>
    /// <param name="document">対象ドキュメント。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>埋め込みファイル一覧。</returns>
    Task<IReadOnlyList<PdfEmbeddedFile>> GetEmbeddedFilesAsync(PdfDocument document, CancellationToken ct = default);

    /// <summary>
    /// 埋め込みファイルを抽出して保存します。
    /// </summary>
    /// <param name="document">対象ドキュメント。</param>
    /// <param name="file">抽出対象ファイル。</param>
    /// <param name="outputPath">出力先ファイルパス。</param>
    /// <param name="ct">キャンセルトークン。</param>
    Task ExtractEmbeddedFileAsync(PdfDocument document, PdfEmbeddedFile file, string outputPath, CancellationToken ct = default);

    /// <summary>
    /// 暗号化方式と権限情報を取得します。
    /// </summary>
    /// <param name="document">対象ドキュメント。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>セキュリティ情報。</returns>
    Task<PdfSecurityInfo> GetSecurityInfoAsync(PdfDocument document, CancellationToken ct = default);

    /// <summary>
    /// ドキュメントを閉じます。
    /// </summary>
    /// <param name="document">対象ドキュメント。</param>
    void Close(PdfDocument document);
}
