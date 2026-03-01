#nullable enable

namespace AcroPDF.Core.Models;

/// <summary>
/// PDF の暗号化と権限情報を表します。
/// </summary>
public sealed record PdfSecurityInfo
{
    /// <summary>
    /// 全機能許可の既定値です。
    /// </summary>
    public static PdfSecurityInfo FullAccess { get; } = new()
    {
        IsEncrypted = false,
        EncryptionDescription = "なし",
        CanPrint = true,
        CanCopy = true,
        CanAnnotate = true
    };

    /// <summary>
    /// 暗号化されているかどうかを取得または設定します。
    /// </summary>
    public bool IsEncrypted { get; init; }

    /// <summary>
    /// 暗号化方式の説明を取得または設定します。
    /// </summary>
    public string EncryptionDescription { get; init; } = "なし";

    /// <summary>
    /// 印刷が許可されているかどうかを取得または設定します。
    /// </summary>
    public bool CanPrint { get; init; } = true;

    /// <summary>
    /// コピーが許可されているかどうかを取得または設定します。
    /// </summary>
    public bool CanCopy { get; init; } = true;

    /// <summary>
    /// 注釈追加・編集が許可されているかどうかを取得または設定します。
    /// </summary>
    public bool CanAnnotate { get; init; } = true;
}
