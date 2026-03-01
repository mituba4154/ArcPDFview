#nullable enable

namespace AcroPDF.Core.Models;

/// <summary>
/// 図形注釈の種別を表します。
/// </summary>
public enum ShapeType
{
    /// <summary>
    /// 矩形。
    /// </summary>
    Rectangle,

    /// <summary>
    /// 楕円。
    /// </summary>
    Ellipse,

    /// <summary>
    /// 矢印。
    /// </summary>
    Arrow,

    /// <summary>
    /// 線。
    /// </summary>
    Line
}

/// <summary>
/// 図形注釈を表します。
/// </summary>
public sealed class ShapeAnnotation : Annotation
{
    /// <summary>
    /// 図形種別を取得または設定します。
    /// </summary>
    public ShapeType Type { get; set; } = ShapeType.Rectangle;

    /// <summary>
    /// 枠線色（#RRGGBB）を取得または設定します。
    /// </summary>
    public string StrokeColorHex { get; set; } = "#ff0000";

    /// <summary>
    /// 塗り色（#RRGGBB）を取得または設定します。
    /// </summary>
    public string? FillColorHex { get; set; }

    /// <summary>
    /// 枠線太さを取得または設定します。
    /// </summary>
    public double StrokeWidth { get; set; } = 2d;
}

