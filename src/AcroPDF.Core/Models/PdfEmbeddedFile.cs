#nullable enable

namespace AcroPDF.Core.Models;

/// <summary>
/// PDF に埋め込まれた添付ファイル情報を表します。
/// </summary>
public sealed record PdfEmbeddedFile
{
    /// <summary>
    /// 添付ファイルの一意 ID を取得または設定します。
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// 添付ファイル名を取得または設定します。
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 添付ファイルサイズ（バイト）を取得または設定します。
    /// </summary>
    public long Size { get; init; }
}
