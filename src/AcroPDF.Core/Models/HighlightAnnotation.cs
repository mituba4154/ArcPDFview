#nullable enable

namespace AcroPDF.Core.Models;

/// <summary>
/// テキスト系注釈の種別を表します。
/// </summary>
public enum HighlightType
{
    /// <summary>
    /// ハイライト。
    /// </summary>
    Highlight,

    /// <summary>
    /// 下線。
    /// </summary>
    Underline,

    /// <summary>
    /// 取り消し線。
    /// </summary>
    Strikethrough
}

/// <summary>
/// ハイライト色を表します。
/// </summary>
public enum HighlightColor
{
    /// <summary>
    /// 黄。
    /// </summary>
    Yellow,

    /// <summary>
    /// 緑。
    /// </summary>
    Green,

    /// <summary>
    /// 青。
    /// </summary>
    Blue,

    /// <summary>
    /// ピンク。
    /// </summary>
    Pink
}

/// <summary>
/// ハイライト系注釈を表します。
/// </summary>
public sealed class HighlightAnnotation : Annotation
{
    /// <summary>
    /// 注釈種別を取得または設定します。
    /// </summary>
    public HighlightType Type { get; set; } = HighlightType.Highlight;

    /// <summary>
    /// 表示色を取得または設定します。
    /// </summary>
    public HighlightColor Color { get; set; } = HighlightColor.Yellow;
}

