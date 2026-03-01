#nullable enable

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace AcroPDF.App.Controls;

/// <summary>
/// SkiaSharp キャンバス描画を提供するベースコントロールです。
/// </summary>
public abstract class SKCanvasView : Control
{
    /// <summary>
    /// 描画時に呼び出されます。
    /// </summary>
    /// <param name="canvas">描画先キャンバス。</param>
    /// <param name="info">描画先情報。</param>
    protected abstract void OnPaintSurface(SKCanvas canvas, SKImageInfo info);

    /// <inheritdoc />
    public sealed override void Render(DrawingContext context)
    {
        base.Render(context);

        var width = Math.Max(1, (int)Bounds.Width);
        var height = Math.Max(1, (int)Bounds.Height);

        using var surfaceBitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(surfaceBitmap);
        canvas.Clear(SKColors.Transparent);

        OnPaintSurface(canvas, new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));

        using var image = SKImage.FromBitmap(surfaceBitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());
        using var bitmap = new Bitmap(stream);

        context.DrawImage(bitmap, new Rect(0, 0, bitmap.Size.Width, bitmap.Size.Height), Bounds);
    }
}

/// <summary>
/// PDF ページを表示するカスタムコントロールです。
/// </summary>
public sealed class PdfPageControl : SKCanvasView
{
    static PdfPageControl()
    {
        SearchHighlightsProperty.Changed.AddClassHandler<PdfPageControl>((control, _) => control.InvalidateVisual());
        CurrentSearchHighlightProperty.Changed.AddClassHandler<PdfPageControl>((control, _) => control.InvalidateVisual());
        SelectionHighlightsProperty.Changed.AddClassHandler<PdfPageControl>((control, _) => control.InvalidateVisual());
    }

    /// <summary>
    /// 検索ハイライト一覧プロパティです。
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<Rect>> SearchHighlightsProperty =
        AvaloniaProperty.Register<PdfPageControl, IReadOnlyList<Rect>>(nameof(SearchHighlights), []);

    /// <summary>
    /// 現在検索結果ハイライトプロパティです。
    /// </summary>
    public static readonly StyledProperty<Rect?> CurrentSearchHighlightProperty =
        AvaloniaProperty.Register<PdfPageControl, Rect?>(nameof(CurrentSearchHighlight));

    /// <summary>
    /// テキスト選択ハイライト一覧プロパティです。
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<Rect>> SelectionHighlightsProperty =
        AvaloniaProperty.Register<PdfPageControl, IReadOnlyList<Rect>>(nameof(SelectionHighlights), []);

    /// <summary>
    /// ズーム倍率プロパティです。
    /// </summary>
    public static readonly StyledProperty<double> ZoomLevelProperty =
        AvaloniaProperty.Register<PdfPageControl, double>(nameof(ZoomLevel), 1.0d);

    /// <summary>
    /// 現在ページ番号プロパティです。
    /// </summary>
    public static readonly StyledProperty<int> CurrentPageProperty =
        AvaloniaProperty.Register<PdfPageControl, int>(nameof(CurrentPage), 1);

    /// <summary>
    /// 表示中のズーム倍率（1.0 = 100%）を取得または設定します。
    /// </summary>
    public double ZoomLevel
    {
        get => GetValue(ZoomLevelProperty);
        set => SetValue(ZoomLevelProperty, Math.Clamp(value, 0.25d, 4.0d));
    }

    /// <summary>
    /// 表示中のページ番号（1 始まり）を取得または設定します。
    /// </summary>
    public int CurrentPage
    {
        get => GetValue(CurrentPageProperty);
        set => SetValue(CurrentPageProperty, Math.Max(1, value));
    }

    /// <summary>
    /// 検索結果ハイライト矩形一覧を取得または設定します。
    /// </summary>
    public IReadOnlyList<Rect> SearchHighlights
    {
        get => GetValue(SearchHighlightsProperty);
        set => SetValue(SearchHighlightsProperty, value);
    }

    /// <summary>
    /// 現在の検索結果ハイライト矩形を取得または設定します。
    /// </summary>
    public Rect? CurrentSearchHighlight
    {
        get => GetValue(CurrentSearchHighlightProperty);
        set => SetValue(CurrentSearchHighlightProperty, value);
    }

    /// <summary>
    /// 選択中テキストのハイライト矩形一覧を取得または設定します。
    /// </summary>
    public IReadOnlyList<Rect> SelectionHighlights
    {
        get => GetValue(SelectionHighlightsProperty);
        set => SetValue(SelectionHighlightsProperty, value);
    }

    /// <summary>
    /// 表示用ビットマップを設定します。
    /// </summary>
    /// <param name="bitmap">表示するビットマップ。</param>
    public void SetBitmap(SKBitmap? bitmap)
    {
        _bitmap?.Dispose();
        _bitmap = bitmap?.Copy();
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPaintSurface(SKCanvas canvas, SKImageInfo info)
    {
        canvas.Clear(new SKColor(0x3A, 0x3A, 0x3A));

        if (_bitmap is null)
        {
            return;
        }

        var sourceRect = new SKRect(0, 0, _bitmap.Width, _bitmap.Height);
        var scaledWidth = _bitmap.Width * (float)ZoomLevel;
        var scaledHeight = _bitmap.Height * (float)ZoomLevel;
        var left = (info.Width - scaledWidth) / 2f;
        var top = (info.Height - scaledHeight) / 2f;
        var destinationRect = new SKRect(left, top, left + scaledWidth, top + scaledHeight);

        canvas.DrawBitmap(_bitmap, sourceRect, destinationRect);
        DrawHighlights(canvas, destinationRect, SearchHighlights, new SKColor(255, 180, 0, 89));
        if (CurrentSearchHighlight is Rect current)
        {
            DrawHighlights(canvas, destinationRect, [current], new SKColor(100, 160, 255, 89));
        }

        DrawHighlights(canvas, destinationRect, SelectionHighlights, new SKColor(120, 200, 255, 96));
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _bitmap?.Dispose();
        _bitmap = null;
    }

    private SKBitmap? _bitmap;

    private void DrawHighlights(SKCanvas canvas, SKRect destinationRect, IReadOnlyList<Rect>? highlights, SKColor color)
    {
        if (_bitmap is null || highlights is null || highlights.Count == 0)
        {
            return;
        }

        var scaleX = destinationRect.Width / Math.Max(1, _bitmap.Width);
        var scaleY = destinationRect.Height / Math.Max(1, _bitmap.Height);
        using var paint = new SKPaint
        {
            Color = color,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        foreach (var highlight in highlights)
        {
            if (highlight.Width <= 0 || highlight.Height <= 0)
            {
                continue;
            }

            var rect = new SKRect(
                destinationRect.Left + (float)(highlight.X * scaleX),
                destinationRect.Top + (float)(highlight.Y * scaleY),
                destinationRect.Left + (float)((highlight.X + highlight.Width) * scaleX),
                destinationRect.Top + (float)((highlight.Y + highlight.Height) * scaleY));
            canvas.DrawRect(rect, paint);
        }
    }
}
