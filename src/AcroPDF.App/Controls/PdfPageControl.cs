#nullable enable

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace AcroPDF.App.Controls;

/// <summary>
/// GPU キャッシュ描画を行う Skia ベースビューです。
/// </summary>
public abstract class SKGLControlView : Control
{
    private bool _isSurfaceDirty = true;
    private WriteableBitmap? _cachedFrame;

    /// <summary>
    /// 描画時に呼び出されます。
    /// </summary>
    /// <param name="canvas">描画先キャンバス。</param>
    /// <param name="info">描画先情報。</param>
    protected abstract void OnPaintSurface(SKCanvas canvas, SKImageInfo info);

    /// <summary>
    /// 描画キャッシュを無効化します。
    /// </summary>
    protected void InvalidateSurface()
    {
        _isSurfaceDirty = true;
        InvalidateVisual();
    }

    /// <inheritdoc />
    public sealed override void Render(DrawingContext context)
    {
        base.Render(context);

        var width = Math.Max(1, (int)Bounds.Width);
        var height = Math.Max(1, (int)Bounds.Height);

        if (_cachedFrame is null || _cachedFrame.PixelSize.Width != width || _cachedFrame.PixelSize.Height != height)
        {
            _cachedFrame?.Dispose();
            _cachedFrame = null;
            _isSurfaceDirty = true;
        }

        if (_isSurfaceDirty)
        {
            using var surfaceBitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(surfaceBitmap);
            canvas.Clear(SKColors.Transparent);
            OnPaintSurface(canvas, new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));

            _cachedFrame ??= new WriteableBitmap(
                new PixelSize(surfaceBitmap.Width, surfaceBitmap.Height),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);
            using var frameBuffer = _cachedFrame.Lock();
            Marshal.Copy(surfaceBitmap.Bytes, 0, frameBuffer.Address, surfaceBitmap.ByteCount);
            _isSurfaceDirty = false;
        }

        if (_cachedFrame is not null)
        {
            context.DrawImage(_cachedFrame, new Rect(0, 0, _cachedFrame.Size.Width, _cachedFrame.Size.Height), Bounds);
        }
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _cachedFrame?.Dispose();
        _cachedFrame = null;
    }
}

/// <summary>
/// PDF ページを表示するカスタムコントロールです。
/// </summary>
public sealed class PdfPageControl : SKGLControlView
{
    static PdfPageControl()
    {
        SearchHighlightsProperty.Changed.AddClassHandler<PdfPageControl>((control, _) => control.InvalidateSurface());
        CurrentSearchHighlightProperty.Changed.AddClassHandler<PdfPageControl>((control, _) => control.InvalidateSurface());
        SelectionHighlightsProperty.Changed.AddClassHandler<PdfPageControl>((control, _) => control.InvalidateSurface());
        AnnotationVisualsProperty.Changed.AddClassHandler<PdfPageControl>((control, _) => control.InvalidateSurface());
        ZoomLevelProperty.Changed.AddClassHandler<PdfPageControl>((control, _) => control.InvalidateSurface());
        CurrentPageProperty.Changed.AddClassHandler<PdfPageControl>((control, _) => control.InvalidateSurface());
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
    /// 注釈描画情報一覧プロパティです。
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<AnnotationVisual>> AnnotationVisualsProperty =
        AvaloniaProperty.Register<PdfPageControl, IReadOnlyList<AnnotationVisual>>(nameof(AnnotationVisuals), []);

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
    /// 注釈描画情報一覧を取得または設定します。
    /// </summary>
    public IReadOnlyList<AnnotationVisual> AnnotationVisuals
    {
        get => GetValue(AnnotationVisualsProperty);
        set => SetValue(AnnotationVisualsProperty, value);
    }

    /// <summary>
    /// 表示用ビットマップを設定します。
    /// </summary>
    /// <param name="bitmap">表示するビットマップ。</param>
    public void SetBitmap(SKBitmap? bitmap)
    {
        _bitmap?.Dispose();
        _bitmap = bitmap?.Copy();
        _gpuCachedImage?.Dispose();
        _gpuCachedImage = _bitmap is null ? null : SKImage.FromBitmap(_bitmap);
        InvalidateSurface();
    }

    /// <inheritdoc />
    protected override void OnPaintSurface(SKCanvas canvas, SKImageInfo info)
    {
        canvas.Clear(new SKColor(0x3A, 0x3A, 0x3A));

        if (_bitmap is null || _gpuCachedImage is null)
        {
            return;
        }

        var sourceRect = new SKRect(0, 0, _bitmap.Width, _bitmap.Height);
        var scaledWidth = _bitmap.Width * (float)ZoomLevel;
        var scaledHeight = _bitmap.Height * (float)ZoomLevel;
        var left = (info.Width - scaledWidth) / 2f;
        var top = (info.Height - scaledHeight) / 2f;
        var destinationRect = new SKRect(left, top, left + scaledWidth, top + scaledHeight);

        canvas.DrawImage(_gpuCachedImage, sourceRect, destinationRect);
        DrawHighlights(canvas, destinationRect, SearchHighlights, new SKColor(255, 180, 0, 89));
        if (CurrentSearchHighlight is Rect current)
        {
            DrawHighlights(canvas, destinationRect, [current], new SKColor(100, 160, 255, 89));
        }

        DrawHighlights(canvas, destinationRect, SelectionHighlights, new SKColor(120, 200, 255, 96));
        DrawAnnotationVisuals(canvas, destinationRect, AnnotationVisuals);
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _gpuCachedImage?.Dispose();
        _gpuCachedImage = null;
        _bitmap?.Dispose();
        _bitmap = null;
    }

    private SKBitmap? _bitmap;
    private SKImage? _gpuCachedImage;

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

    private void DrawAnnotationVisuals(SKCanvas canvas, SKRect destinationRect, IReadOnlyList<AnnotationVisual>? annotations)
    {
        if (_bitmap is null || annotations is null || annotations.Count == 0)
        {
            return;
        }

        var scaleX = destinationRect.Width / Math.Max(1, _bitmap.Width);
        var scaleY = destinationRect.Height / Math.Max(1, _bitmap.Height);
        foreach (var annotation in annotations)
        {
            var bounds = annotation.Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                continue;
            }

            var rect = new SKRect(
                destinationRect.Left + (float)(bounds.X * scaleX),
                destinationRect.Top + (float)(bounds.Y * scaleY),
                destinationRect.Left + (float)((bounds.X + bounds.Width) * scaleX),
                destinationRect.Top + (float)((bounds.Y + bounds.Height) * scaleY));
            var color = new SKColor(annotation.Color.R, annotation.Color.G, annotation.Color.B, annotation.Color.A);

            if (annotation.Kind == AnnotationVisualKind.Highlight)
            {
                using var fillPaint = new SKPaint
                {
                    Color = color.WithAlpha(89),
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill
                };
                canvas.DrawRect(rect, fillPaint);
                continue;
            }

            using var linePaint = new SKPaint
            {
                Color = color,
                IsAntialias = true,
                StrokeWidth = 2f,
                Style = SKPaintStyle.Stroke
            };

            if (annotation.Kind == AnnotationVisualKind.Underline)
            {
                canvas.DrawLine(rect.Left, rect.Bottom - 1f, rect.Right, rect.Bottom - 1f, linePaint);
            }
            else
            {
                var y = rect.MidY;
                canvas.DrawLine(rect.Left, y, rect.Right, y, linePaint);
            }
        }
    }
}

/// <summary>
/// 注釈描画情報を表します。
/// </summary>
/// <param name="Bounds">表示矩形。</param>
/// <param name="Kind">描画種別。</param>
/// <param name="Color">表示色。</param>
public readonly record struct AnnotationVisual(Rect Bounds, AnnotationVisualKind Kind, Color Color);

/// <summary>
/// 注釈描画種別を表します。
/// </summary>
public enum AnnotationVisualKind
{
    /// <summary>
    /// ハイライト表示。
    /// </summary>
    Highlight,

    /// <summary>
    /// 下線表示。
    /// </summary>
    Underline,

    /// <summary>
    /// 取り消し線表示。
    /// </summary>
    Strikethrough
}
