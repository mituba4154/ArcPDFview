#nullable enable

namespace AcroPDF.Core.Models;

/// <summary>
/// PDF フォームフィールドを表します。
/// </summary>
public sealed class PdfFormField
{
    /// <summary>
    /// フィールド識別子を取得または設定します。
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// ページ番号（1 始まり）を取得または設定します。
    /// </summary>
    public int PageNumber { get; set; }

    /// <summary>
    /// フィールド名を取得または設定します。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// フィールド種別を取得または設定します。
    /// </summary>
    public PdfFormFieldType FieldType { get; set; }

    /// <summary>
    /// PDF 座標（pt）での境界矩形を取得または設定します。
    /// </summary>
    public PdfTextBounds Bounds { get; set; }

    /// <summary>
    /// 値を取得または設定します。
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// チェック状態を取得または設定します。
    /// </summary>
    public bool IsChecked { get; set; }

    /// <summary>
    /// 読み取り専用かどうかを取得または設定します。
    /// </summary>
    public bool IsReadOnly { get; set; }

    /// <summary>
    /// ドロップダウン候補一覧を取得または設定します。
    /// </summary>
    public IReadOnlyList<string> Options { get; set; } = [];
}

/// <summary>
/// PDF フォームフィールド種別を表します。
/// </summary>
public enum PdfFormFieldType
{
    /// <summary>
    /// テキスト入力。
    /// </summary>
    Text,

    /// <summary>
    /// チェックボックス。
    /// </summary>
    CheckBox,

    /// <summary>
    /// ラジオボタン。
    /// </summary>
    RadioButton,

    /// <summary>
    /// ドロップダウン。
    /// </summary>
    ComboBox,

    /// <summary>
    /// 署名フィールド。
    /// </summary>
    Signature,

    /// <summary>
    /// 不明。
    /// </summary>
    Unknown
}
