#nullable enable

namespace AcroPDF.Core.Models;

/// <summary>
/// 注釈描画用の 2D 座標を表します。
/// </summary>
/// <param name="X">X 座標。</param>
/// <param name="Y">Y 座標。</param>
public readonly record struct AnnotationPoint(double X, double Y);

/// <summary>
/// フリーハンド注釈を表します。
/// </summary>
public sealed class FreehandAnnotation : Annotation
{
    /// <summary>
    /// ストローク集合を取得または設定します。
    /// </summary>
    public IReadOnlyList<IReadOnlyList<AnnotationPoint>> Strokes { get; set; } = [];

    /// <summary>
    /// 線色（#RRGGBB）を取得または設定します。
    /// </summary>
    public string StrokeColorHex { get; set; } = "#ff0000";

    /// <summary>
    /// 線幅を取得または設定します。
    /// </summary>
    public double StrokeWidth { get; set; } = 2d;
}

