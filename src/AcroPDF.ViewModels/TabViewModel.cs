#nullable enable

using System.Collections.ObjectModel;
using AcroPDF.Core.Models;
using AcroPDF.Services;
using AcroPDF.Services.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;

namespace AcroPDF.ViewModels;

/// <summary>
/// PDF タブ 1 件分の状態を表します。
/// </summary>
public sealed partial class TabViewModel : ObservableObject, IDisposable
{
    private const double PdfDpi = 72d;
    private const double ScreenDpi = 96d;
    private const double FitViewportPaddingPx = 32d;
    private const int ThumbnailTargetWidthPx = 140;
    private readonly CancellationTokenSource _thumbnailCts = new();
    private int _isDisposed;
    private IReadOnlyList<SearchResult> _searchResults = [];
    private int _currentSearchResultIndex = -1;

    /// <summary>
    /// <see cref="TabViewModel"/> の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="document">対象ドキュメント。</param>
    public TabViewModel(PdfDocument document)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        PageInputText = CurrentPage.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Thumbnails = new ObservableCollection<ThumbnailViewModel>(
            Document.Pages.Select(static page => new ThumbnailViewModel(page.PageNumber)));
        UpdateSelectedThumbnail();
    }

    /// <summary>
    /// サムネイル更新時に発火します。
    /// </summary>
    public event Action<int>? ThumbnailUpdated;

    /// <summary>
    /// 対象 PDF ドキュメントを取得します。
    /// </summary>
    public PdfDocument Document { get; }

    /// <summary>
    /// タブに表示するタイトルを取得します。
    /// </summary>
    public string Title => Document.FileName;

    /// <summary>
    /// 総ページ数を取得します。
    /// </summary>
    public int PageCount => Document.PageCount;

    /// <summary>
    /// サムネイル一覧を取得します。
    /// </summary>
    public ObservableCollection<ThumbnailViewModel> Thumbnails { get; }

    /// <summary>
    /// 目次ブックマーク一覧を取得します。
    /// </summary>
    public ObservableCollection<PdfBookmarkItem> Bookmarks { get; } = [];

    /// <summary>
    /// 現在ページ番号（1 始まり）を取得または設定します。
    /// </summary>
    [ObservableProperty]
    private int _currentPage = 1;

    /// <summary>
    /// 表示ズーム倍率（1.0 = 100%）を取得または設定します。
    /// </summary>
    [ObservableProperty]
    private double _zoomLevel = 1.0d;

    /// <summary>
    /// 連続スクロールモードかどうかを取得または設定します。
    /// </summary>
    [ObservableProperty]
    private bool _isContinuousMode;

    /// <summary>
    /// 2ページ見開きモードかどうかを取得または設定します。
    /// </summary>
    [ObservableProperty]
    private bool _isTwoPageMode;

    /// <summary>
    /// 表示回転角度（0/90/180/270）を取得または設定します。
    /// </summary>
    [ObservableProperty]
    private int _rotationDegrees;

    /// <summary>
    /// 印刷オプションを取得または設定します。
    /// </summary>
    [ObservableProperty]
    private PrintOptions _printOptions = new();

    /// <summary>
    /// 検索バーの表示状態を取得または設定します。
    /// </summary>
    [ObservableProperty]
    private bool _isSearchVisible;

    /// <summary>
    /// 検索文字列を取得または設定します。
    /// </summary>
    [ObservableProperty]
    private string _searchQuery = string.Empty;

    /// <summary>
    /// 大文字小文字を区別するかどうかを取得または設定します。
    /// </summary>
    [ObservableProperty]
    private bool _isSearchCaseSensitive;

    /// <summary>
    /// 正規表現検索を使用するかどうかを取得または設定します。
    /// </summary>
    [ObservableProperty]
    private bool _isSearchRegex;

    /// <summary>
    /// ページ入力欄のテキストを取得または設定します。
    /// </summary>
    [ObservableProperty]
    private string _pageInputText = "1";

    /// <summary>
    /// マウス座標 X（pt）を取得または設定します。
    /// </summary>
    [ObservableProperty]
    private double _mouseX;

    /// <summary>
    /// マウス座標 Y（pt）を取得または設定します。
    /// </summary>
    [ObservableProperty]
    private double _mouseY;

    /// <summary>
    /// 現在の検索結果一覧を取得します。
    /// </summary>
    public IReadOnlyList<SearchResult> SearchResults => _searchResults;

    /// <summary>
    /// 現在選択中の検索結果インデックス（0 始まり）を取得します。
    /// </summary>
    public int CurrentSearchResultIndex => _currentSearchResultIndex;

    /// <summary>
    /// 現在選択中の検索結果を取得します。
    /// </summary>
    public SearchResult? CurrentSearchResult =>
        _currentSearchResultIndex >= 0 && _currentSearchResultIndex < _searchResults.Count
            ? _searchResults[_currentSearchResultIndex]
            : null;

    /// <summary>
    /// サムネイルをバックグラウンドで生成します。
    /// </summary>
    /// <param name="renderService">レンダリングサービス。</param>
    /// <param name="ct">キャンセルトークン。</param>
    public async Task GenerateThumbnailsAsync(IPdfRenderService renderService, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(renderService);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _thumbnailCts.Token);
        var token = linkedCts.Token;
        using var semaphore = new SemaphoreSlim(3, 3);
        var tasks = Document.Pages.Select(async page =>
        {
            await semaphore.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var thumbnailZoom = (ThumbnailTargetWidthPx / Math.Max(1d, page.WidthPt)) * (PdfDpi / ScreenDpi);
                using var raw = await renderService.RenderPageAsync(page, thumbnailZoom, token).ConfigureAwait(false);
                var scaled = ResizeToWidth(raw, ThumbnailTargetWidthPx);
                var thumbnail = Thumbnails[page.PageIndex];
                thumbnail.SetBitmap(scaled);
                ThumbnailUpdated?.Invoke(page.PageNumber);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    /// ページ入力テキストからページ移動を試行します。
    /// </summary>
    public void JumpFromInput()
    {
        if (int.TryParse(PageInputText, out var page))
        {
            JumpToPage(page);
        }
        else
        {
            PageInputText = CurrentPage.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// 指定ページへ移動します。
    /// </summary>
    /// <param name="page">移動先ページ（1 始まり）。</param>
    public void JumpToPage(int page)
    {
        CurrentPage = Math.Clamp(page, 1, Math.Max(1, PageCount));
    }

    /// <summary>
    /// 表示領域の幅に合わせてズームします。
    /// </summary>
    /// <param name="viewportWidthPx">表示領域幅（px）。</param>
    public void FitToWidth(double viewportWidthPx)
    {
        var page = GetCurrentPageModel();
        if (page is null)
        {
            return;
        }

        var widthPx = Math.Max(1d, viewportWidthPx - FitViewportPaddingPx);
        var zoom = (widthPx * PdfDpi) / (Math.Max(1d, page.WidthPt) * ScreenDpi);
        ZoomLevel = PdfiumRenderService.ClampZoomLevel(zoom);
    }

    /// <summary>
    /// 表示領域全体に合わせてズームします。
    /// </summary>
    /// <param name="viewportWidthPx">表示領域幅（px）。</param>
    /// <param name="viewportHeightPx">表示領域高さ（px）。</param>
    public void FitToPage(double viewportWidthPx, double viewportHeightPx)
    {
        var page = GetCurrentPageModel();
        if (page is null)
        {
            return;
        }

        var widthZoom = ((Math.Max(1d, viewportWidthPx) - FitViewportPaddingPx) * PdfDpi) / (Math.Max(1d, page.WidthPt) * ScreenDpi);
        var heightZoom = ((Math.Max(1d, viewportHeightPx) - FitViewportPaddingPx) * PdfDpi) / (Math.Max(1d, page.HeightPt) * ScreenDpi);
        ZoomLevel = PdfiumRenderService.ClampZoomLevel(Math.Min(widthZoom, heightZoom));
    }

    /// <summary>
    /// サムネイル生成のキャンセルを要求します。
    /// </summary>
    public void CancelThumbnailGeneration()
    {
        _thumbnailCts.Cancel();
    }

    /// <summary>
    /// 現在の印刷設定に基づいて印刷対象ページ番号を取得します。
    /// </summary>
    /// <returns>印刷対象ページ番号一覧（1 始まり）。</returns>
    public IReadOnlyList<int> ResolvePrintPages()
    {
        PrintOptions.CurrentPage = CurrentPage;
        return PrintOptions.ResolvePageNumbers(PageCount);
    }

    /// <summary>
    /// 検索結果一覧を設定します。
    /// </summary>
    /// <param name="results">検索結果一覧。</param>
    public void SetSearchResults(IReadOnlyList<SearchResult> results)
    {
        _searchResults = results ?? [];
        _currentSearchResultIndex = _searchResults.Count > 0 ? 0 : -1;
        OnPropertyChanged(nameof(SearchResults));
        SyncCurrentPageFromSearchResult();
    }

    /// <summary>
    /// 次の検索結果へ移動します。
    /// </summary>
    public void MoveToNextSearchResult()
    {
        if (_searchResults.Count == 0)
        {
            _currentSearchResultIndex = -1;
        }
        else
        {
            _currentSearchResultIndex = (_currentSearchResultIndex + 1 + _searchResults.Count) % _searchResults.Count;
        }

        SyncCurrentPageFromSearchResult();
    }

    /// <summary>
    /// 前の検索結果へ移動します。
    /// </summary>
    public void MoveToPreviousSearchResult()
    {
        if (_searchResults.Count == 0)
        {
            _currentSearchResultIndex = -1;
        }
        else
        {
            _currentSearchResultIndex = (_currentSearchResultIndex - 1 + _searchResults.Count) % _searchResults.Count;
        }

        SyncCurrentPageFromSearchResult();
    }

    /// <summary>
    /// 先頭ページへ移動します。
    /// </summary>
    [RelayCommand]
    private void MoveFirstPage()
    {
        JumpToPage(1);
    }

    /// <summary>
    /// 最終ページへ移動します。
    /// </summary>
    [RelayCommand]
    private void MoveLastPage()
    {
        JumpToPage(PageCount);
    }

    /// <summary>
    /// 次ページへ移動します。
    /// </summary>
    [RelayCommand]
    private void MoveNextPage()
    {
        JumpToPage(CurrentPage + 1);
    }

    /// <summary>
    /// 前ページへ移動します。
    /// </summary>
    [RelayCommand]
    private void MovePreviousPage()
    {
        JumpToPage(CurrentPage - 1);
    }

    /// <summary>
    /// ズームインします。
    /// </summary>
    [RelayCommand]
    private void ZoomIn()
    {
        ZoomLevel = PdfiumRenderService.ClampZoomLevel(ZoomLevel + 0.1d);
    }

    /// <summary>
    /// ズームアウトします。
    /// </summary>
    [RelayCommand]
    private void ZoomOut()
    {
        ZoomLevel = PdfiumRenderService.ClampZoomLevel(ZoomLevel - 0.1d);
    }

    /// <summary>
    /// 実寸（100%）表示にします。
    /// </summary>
    [RelayCommand]
    private void ZoomActualSize()
    {
        ZoomLevel = 1.0d;
    }

    /// <summary>
    /// 単ページモードへ切り替えます。
    /// </summary>
    [RelayCommand]
    private void SetSinglePageMode()
    {
        IsContinuousMode = false;
        IsTwoPageMode = false;
    }

    /// <summary>
    /// 連続スクロールモードへ切り替えます。
    /// </summary>
    [RelayCommand]
    private void SetContinuousMode()
    {
        IsContinuousMode = true;
        IsTwoPageMode = false;
    }

    /// <summary>
    /// 2ページ見開きモードへ切り替えます。
    /// </summary>
    [RelayCommand]
    private void SetTwoPageMode()
    {
        IsContinuousMode = false;
        IsTwoPageMode = true;
    }

    /// <summary>
    /// 表示を 90 度回転します。
    /// </summary>
    [RelayCommand]
    private void RotateClockwise()
    {
        RotationDegrees = NormalizeRotation(RotationDegrees + 90);
    }

    /// <summary>
    /// リソースを解放します。
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
        {
            return;
        }

        _thumbnailCts.Cancel();
        _thumbnailCts.Dispose();

        foreach (var thumbnail in Thumbnails)
        {
            thumbnail.Dispose();
        }
    }

    partial void OnCurrentPageChanged(int value)
    {
        PageInputText = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        UpdateSelectedThumbnail();
    }

    partial void OnZoomLevelChanged(double value)
    {
        ZoomLevel = PdfiumRenderService.ClampZoomLevel(value);
    }

    partial void OnRotationDegreesChanged(int value)
    {
        RotationDegrees = NormalizeRotation(value);
    }

    private PdfPage? GetCurrentPageModel()
    {
        var pageIndex = CurrentPage - 1;
        if (pageIndex < 0 || pageIndex >= Document.Pages.Count)
        {
            return null;
        }

        return Document.Pages[pageIndex];
    }

    private void UpdateSelectedThumbnail()
    {
        for (var index = 0; index < Thumbnails.Count; index++)
        {
            Thumbnails[index].IsSelected = index == CurrentPage - 1;
        }
    }

    private void SyncCurrentPageFromSearchResult()
    {
        if (_currentSearchResultIndex < 0 || _currentSearchResultIndex >= _searchResults.Count)
        {
            OnPropertyChanged(nameof(CurrentSearchResultIndex));
            OnPropertyChanged(nameof(CurrentSearchResult));
            return;
        }

        JumpToPage(_searchResults[_currentSearchResultIndex].PageNumber);
        OnPropertyChanged(nameof(CurrentSearchResultIndex));
        OnPropertyChanged(nameof(CurrentSearchResult));
    }

    private static SKBitmap ResizeToWidth(SKBitmap source, int targetWidth)
    {
        if (source.Width == targetWidth)
        {
            return source.Copy();
        }

        var ratio = source.Height / (double)Math.Max(1, source.Width);
        var targetHeight = Math.Max(1, (int)Math.Round(targetWidth * ratio, MidpointRounding.AwayFromZero));
        var resized = new SKBitmap(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        source.ScalePixels(resized, SKFilterQuality.Medium);
        return resized;
    }

    private static int NormalizeRotation(int value)
    {
        var normalized = value % 360;
        if (normalized < 0)
        {
            normalized += 360;
        }

        return (normalized / 90) * 90;
    }
}

/// <summary>
/// サムネイル表示情報を表します。
/// </summary>
public sealed class ThumbnailViewModel : ObservableObject, IDisposable
{
    private SKBitmap? _bitmap;

    /// <summary>
    /// <see cref="ThumbnailViewModel"/> の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="pageNumber">ページ番号（1 始まり）。</param>
    public ThumbnailViewModel(int pageNumber)
    {
        PageNumber = pageNumber;
    }

    /// <summary>
    /// ページ番号（1 始まり）を取得します。
    /// </summary>
    public int PageNumber { get; }

    /// <summary>
    /// このサムネイルが現在ページかどうかを取得または設定します。
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>
    /// サムネイルビットマップを取得します。
    /// </summary>
    public SKBitmap? Bitmap => _bitmap;

    private bool _isSelected;

    /// <summary>
    /// サムネイルビットマップを更新します。
    /// </summary>
    /// <param name="bitmap">設定するビットマップ。</param>
    public void SetBitmap(SKBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        _bitmap?.Dispose();
        _bitmap = bitmap;
        OnPropertyChanged(nameof(Bitmap));
    }

    /// <summary>
    /// リソースを解放します。
    /// </summary>
    public void Dispose()
    {
        _bitmap?.Dispose();
        _bitmap = null;
    }
}
