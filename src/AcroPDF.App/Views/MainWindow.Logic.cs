#nullable enable

using System.Diagnostics;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using AcroPDF.App.Controls;
using AcroPDF.App.Assets.Localization;
using AcroPDF.Core.Models;
using AcroPDF.Services;
using AcroPDF.Services.Interfaces;
using AcroPDF.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace AcroPDF.App.Views;

/// <summary>
/// AcroPDF のメインウィンドウです。
/// </summary>
public partial class MainWindow : Window
{
    private const double PdfDpi = 72d;
    private const double ScreenDpi = 96d;
    private const int ThumbnailWidthPx = 140;
    private const double A4AspectRatio = 0.707d;
    private const double SplitPaneMinWidthPx = 120d;
    private const double DefaultCommentHalfHeightPt = 18d;
    private const double DefaultCommentWidthPt = 120d;
    private const string StampPrefix = "[STAMP]";
    private const string DefaultStampText = "承認済み";
    private const int ContinuousPreloadMarginPx = 300;
    private static readonly TimeSpan SplashDisplayDuration = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SplashFadeDuration = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan ZoomDebounceDelay = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan ContinuousScrollDebounceDelay = TimeSpan.FromMilliseconds(50);
    private readonly IPdfRenderService _pdfRenderService;
    private readonly IAnnotationService _annotationService;
    private readonly ISearchService _searchService;
    private readonly SearchViewModel _searchViewModel;
    private readonly ISettingsService _settingsService;
    private readonly MainWindowViewModel _mainWindowViewModel = new();
    private readonly List<TabViewModel> _tabs = [];
    private readonly Dictionary<PdfPageControl, (TabViewModel Tab, PdfPage Page)> _continuousPageMap = [];
    private readonly Dictionary<TabViewModel, IReadOnlyList<PdfBookmarkItem>> _bookmarkMap = [];
    private readonly Dictionary<(TabViewModel Tab, int PageNumber), IReadOnlyList<Avalonia.Rect>> _selectionHighlightMap = [];
    private readonly Dictionary<(TabViewModel Tab, int PageNumber), IReadOnlyList<PdfTextBounds>> _selectionPdfBoundsMap = [];
    private readonly Dictionary<(TabViewModel Tab, int PageNumber), IReadOnlyList<PdfFormField>> _formFieldMap = [];
    private readonly Dictionary<TabViewModel, IReadOnlyList<PdfEmbeddedFile>> _attachmentMap = [];
    private readonly Dictionary<TabViewModel, PdfSecurityInfo> _securityMap = [];
    private readonly Dictionary<TabViewModel, WatchedFile> _watchers = [];
    private readonly List<TabViewModel> _splitDetachedTabs = [];
    private readonly ObservableCollection<Control> _continuousPageItems = [];
    private readonly List<ContinuousPageItemState> _continuousPageStates = [];
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _renderCts;
    private CancellationTokenSource? _zoomDebounceCts;
    private CancellationTokenSource? _continuousScrollDebounceCts;
    private AppSettings _settings = new();
    private TabViewModel? _activeTab;
    private TabViewModel? _splitSecondaryTab;
    private bool _isSelectingText;
    private bool _isSplitRightPaneActive;
    private bool _isSplitDividerDragging;
    private bool _restoreAttempted;
    private bool _skipSessionRestore;
    private TabViewModel? _selectionTab;
    private PdfPage? _selectionPage;
    private Point _selectionStartPdfPoint;
    private string _selectedText = string.Empty;
    private PdfSecurityInfo _activeSecurityInfo = PdfSecurityInfo.FullAccess;
    private AnnotationTool _activeAnnotationTool = AnnotationTool.TextSelect;
    private ShapeType _activeShapeType = ShapeType.Rectangle;
    private string _activeStrokeColorHex = "#ff0000";
    private string? _activeFillColorHex;
    private double _activeStrokeWidth = 2d;
    private readonly List<AnnotationPoint> _currentFreehandStroke = [];
    private Avalonia.Controls.Shapes.Polyline? _activeFreehandPreview;
    private Point? _shapeStartPdfPoint;
    private int? _thumbnailDragPageNumber;
    private bool _isRebuildingSplitSelectors;
    private bool _isInitialized;

    /// <summary>
    /// <see cref="MainWindow"/> の新しいインスタンスを初期化します。
    /// </summary>
    public MainWindow()
        : this(
            ResolveRequiredService<IPdfRenderService>(),
            ResolveRequiredService<IAnnotationService>(),
            ResolveRequiredService<ISearchService>(),
            ResolveRequiredService<ISettingsService>())
    {
    }

    /// <summary>
    /// <see cref="MainWindow"/> の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="pdfRenderService">PDF レンダリングサービス。</param>
    /// <param name="annotationService">注釈サービス。</param>
    /// <param name="searchService">検索サービス。</param>
    /// <param name="settingsService">設定サービス。</param>
    public MainWindow(
        IPdfRenderService pdfRenderService,
        IAnnotationService annotationService,
        ISearchService searchService,
        ISettingsService settingsService)
    {
        _pdfRenderService = pdfRenderService ?? throw new ArgumentNullException(nameof(pdfRenderService));
        _annotationService = annotationService ?? throw new ArgumentNullException(nameof(annotationService));
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        _searchViewModel = new SearchViewModel(_searchService);
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _settings = _settingsService.Load();
        AppStrings.CurrentCulture = string.Equals(_settings.Language, "en", StringComparison.OrdinalIgnoreCase)
            ? new CultureInfo("en")
            : new CultureInfo("ja");
        InitializeComponent();
        ApplyLocalizedText();
        ContinuousPageItemsControl.ItemsSource = _continuousPageItems;
        ContinuousScrollViewer.EffectiveViewportChanged += OnContinuousViewportChanged;
        InitializeStaticStatusText();
        InitializeTextSelectionContextMenu();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
        Closed += OnClosed;
        Opened += OnOpened;
        SetEmptyStateVisible(true);
        DataContext = _mainWindowViewModel;
        SplitDivider.Cursor = new Cursor(StandardCursorType.SizeWestEast);
        ApplySettingsToToolbar();
        InitializeAnnotationTooling();
        RebuildRecentFilesMenu();
        _isInitialized = true;
    }

    private static T ResolveRequiredService<T>()
        where T : notnull
    {
        var provider = App.Services ?? throw new InvalidOperationException("Application services are not initialized.");
        return provider.GetRequiredService<T>();
    }

    /// <summary>
    /// 起動引数から渡されたファイルを開きます。
    /// </summary>
    /// <param name="filePath">開くファイルパス。</param>
    public void OpenFromStartupArgument(string filePath)
    {
        _skipSessionRestore = true;
        OpenFileWithoutAwait(filePath);
    }

    private async Task<TabViewModel?> OpenFileAsync(string filePath, int initialPage = 1, bool activate = true)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        string? password = null;
        PdfDocument? document = null;

        while (document is null)
        {
            try
            {
                document = await _pdfRenderService.OpenAsync(filePath, password).ConfigureAwait(true);
            }
            catch (PdfPasswordRequiredException)
            {
                var dialog = new PasswordDialogWindow();
                password = await dialog.ShowDialog<string?>(this).ConfigureAwait(true);
                if (string.IsNullOrWhiteSpace(password))
                {
                    return null;
                }
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Failed to open PDF '{filePath}': {ex}");
                await ShowOpenFileErrorDialogAsync(filePath, ex).ConfigureAwait(true);
                return null;
            }
        }

        if (document is null)
        {
            return null;
        }

        var tab = new TabViewModel(document);
        var annotations = await _annotationService.LoadAnnotationsAsync(document).ConfigureAwait(true);
        document.SetAnnotations(annotations);
        _securityMap[tab] = await _pdfRenderService.GetSecurityInfoAsync(document).ConfigureAwait(true);
        _attachmentMap[tab] = await _pdfRenderService.GetEmbeddedFilesAsync(document).ConfigureAwait(true);
        tab.ZoomLevel = PdfiumRenderService.ClampZoomLevel(_settings.DefaultZoom);
        tab.CurrentPage = Math.Clamp(initialPage, 1, tab.PageCount);
        switch (_settings.DefaultViewMode)
        {
            case ViewMode.Continuous:
                tab.IsContinuousMode = true;
                break;
            case ViewMode.TwoPage:
                tab.IsTwoPageMode = true;
                break;
            default:
                tab.IsContinuousMode = false;
                tab.IsTwoPageMode = false;
                break;
        }

        tab.ThumbnailUpdated += OnThumbnailUpdated;
        var bookmarks = _searchService.GetBookmarks(document);
        tab.Bookmarks.Clear();
        foreach (var bookmark in bookmarks)
        {
            tab.Bookmarks.Add(bookmark);
        }

        _bookmarkMap[tab] = bookmarks;
        _tabs.Add(tab);
        _mainWindowViewModel.AddTab(tab);
        TrackFileChanges(tab);
        _settingsService.AddRecentFile(filePath);
        RebuildRecentFilesMenu();
        if (activate)
        {
            ActivateTab(tab);
        }
        _ = GenerateThumbnailsAsync(tab);
        return tab;
    }

    private async Task GenerateThumbnailsAsync(TabViewModel tab)
    {
        try
        {
            await tab.GenerateThumbnailsAsync(_pdfRenderService).ConfigureAwait(true);
            if (ReferenceEquals(_activeTab, tab))
            {
                RebuildThumbnailPanel();
            }
        }
        catch (OperationCanceledException)
        {
            // タブを閉じた時のキャンセルは正常系。
        }
        catch (Exception ex)
        {
            Trace.TraceError(ex.ToString());
        }
    }

    private void ActivateTab(TabViewModel tab)
    {
        _activeTab = tab;
        _mainWindowViewModel.ActiveTab = tab;
        _activeSecurityInfo = _securityMap.TryGetValue(tab, out var security) ? security : PdfSecurityInfo.FullAccess;
        RebuildTabBar();
        RebuildSplitTabSelectors();
        UpdateToolbarState();
        RebuildThumbnailPanel();
        RebuildBookmarkPanel();
        RebuildAttachmentPanel();
        RebuildAnnotationPanel();
        UpdateFileInfoPanel();
        UpdateSearchOverlayState();
        _ = RenderActiveTabAsync();
    }

    private async Task RenderActiveTabAsync(CancellationToken externalCt = default)
    {
        CancelRender();
        _renderCts = externalCt.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(externalCt)
            : new CancellationTokenSource();
        var token = _renderCts.Token;

        var tab = _activeTab;
        if (tab is null)
        {
            ClearPageViews();
            SetEmptyStateVisible(true);
            UpdateStatusBar();
            return;
        }

        SetEmptyStateVisible(false);
        UpdateToolbarState();
        UpdateStatusBar();
        SetLoadingOverlayVisible(true);

        try
        {
            if (_mainWindowViewModel.IsSplitView)
            {
                await RenderSplitViewAsync(tab, token).ConfigureAwait(true);
            }
            else if (tab.IsContinuousMode)
            {
                await RenderContinuousModeAsync(tab, token).ConfigureAwait(true);
            }
            else
            {
                await RenderSinglePageAsync(tab, token).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            // 表示更新途中のキャンセルは正常系。
        }
        finally
        {
            SetLoadingOverlayVisible(false);
        }
    }

    private async Task RenderSinglePageAsync(TabViewModel tab, CancellationToken ct)
    {
        SplitViewGrid.IsVisible = false;
        SinglePageScrollViewer.IsVisible = true;
        ContinuousScrollViewer.IsVisible = false;
        ClearContinuousPageItems();

        using var bitmap = await RenderTabBitmapAsync(tab, ct).ConfigureAwait(true);
        if (bitmap is null)
        {
            return;
        }

        var page = tab.Document.Pages[tab.CurrentPage - 1];
        PageControl.CurrentPage = page.PageNumber;
        PageControl.ZoomLevel = 1.0d;
        PageControl.Width = bitmap.Width;
        PageControl.Height = bitmap.Height;
        AnnotationOverlayCanvas.Width = bitmap.Width;
        AnnotationOverlayCanvas.Height = bitmap.Height;
        PageControl.SetBitmap(bitmap);
        ApplyHighlights(PageControl, tab, page);
        RebuildCommentOverlay(tab, page);
        await RebuildFormOverlayAsync(tab, page, ct).ConfigureAwait(true);
        UpdateStatusBar();
    }

    private async Task RenderSplitViewAsync(TabViewModel primaryTab, CancellationToken ct)
    {
        SinglePageScrollViewer.IsVisible = false;
        ContinuousScrollViewer.IsVisible = false;
        SplitViewGrid.IsVisible = true;
        AnnotationOverlayCanvas.Children.Clear();
        ClearContinuousPageItems();
        _formFieldMap.Clear();

        var secondaryTab = _splitSecondaryTab ?? primaryTab;
        await RenderSplitPaneAsync(primaryTab, PrimarySplitPageControl, ct).ConfigureAwait(true);
        await RenderSplitPaneAsync(secondaryTab, SecondarySplitPageControl, ct).ConfigureAwait(true);
    }

    private async Task RenderSplitPaneAsync(TabViewModel tab, PdfPageControl control, CancellationToken ct)
    {
        using var bitmap = await RenderTabBitmapAsync(tab, ct).ConfigureAwait(true);
        if (bitmap is null)
        {
            control.SetBitmap(null);
            return;
        }

        control.CurrentPage = tab.CurrentPage;
        control.ZoomLevel = 1.0d;
        control.Width = bitmap.Width;
        control.Height = bitmap.Height;
        control.SetBitmap(bitmap);
    }

    private async Task<SKBitmap?> RenderTabBitmapAsync(TabViewModel tab, CancellationToken ct)
    {
        return await _pdfRenderService.RenderCompositePageAsync(
            tab.Document,
            tab.CurrentPage,
            tab.ZoomLevel,
            tab.IsTwoPageMode,
            tab.RotationDegrees,
            ct).ConfigureAwait(true);
    }

    private async Task RenderContinuousModeAsync(TabViewModel tab, CancellationToken ct)
    {
        SplitViewGrid.IsVisible = false;
        SinglePageScrollViewer.IsVisible = false;
        ContinuousScrollViewer.IsVisible = true;
        ClearContinuousPageItems();
        _formFieldMap.Clear();

        var scale = (ScreenDpi * Math.Max(0.01d, tab.ZoomLevel)) / PdfDpi;
        foreach (var page in tab.Document.Pages)
        {
            ct.ThrowIfCancellationRequested();
            var width = Math.Max(1d, page.WidthPt * scale);
            var height = Math.Max(1d, page.HeightPt * scale);
            var host = new Border
            {
                Width = width,
                Height = height,
                Margin = new Thickness(0, 0, 0, 16),
                Background = new SolidColorBrush((Color)Application.Current!.FindResource("BgPanel2")!),
                BorderBrush = new SolidColorBrush((Color)Application.Current!.FindResource("Border")!),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            host.Child = new Border
            {
                Background = new SolidColorBrush((Color)Application.Current!.FindResource("BgPanel2")!)
            };

            _continuousPageStates.Add(new ContinuousPageItemState(tab, page, host));
            _continuousPageItems.Add(host);
        }

        await RenderVisibleContinuousPagesAsync(tab, ct).ConfigureAwait(true);
        foreach (var state in _continuousPageStates)
        {
            if (state.OverlayCanvas is not null && state.PageControl is not null)
            {
                RebuildCommentOverlay(tab, state.Page, state.OverlayCanvas);
            }
        }

        ScrollContinuousToCurrentPage();
        UpdateStatusBar();
    }

    private void ScrollContinuousToCurrentPage()
    {
        var tab = _activeTab;
        if (tab is null || !tab.IsContinuousMode)
        {
            return;
        }

        var target = _continuousPageStates
            .FirstOrDefault(state => state.Page.PageNumber == tab.CurrentPage)?.Host;
        target?.BringIntoView();
    }

    private async Task RenderVisibleContinuousPagesAsync(TabViewModel tab, CancellationToken ct)
    {
        var viewportTop = ContinuousScrollViewer.Offset.Y - ContinuousPreloadMarginPx;
        var viewportBottom = ContinuousScrollViewer.Offset.Y + ContinuousScrollViewer.Viewport.Height + ContinuousPreloadMarginPx;
        foreach (var state in _continuousPageStates)
        {
            ct.ThrowIfCancellationRequested();
            var origin = state.Host.TranslatePoint(default, ContinuousPageItemsControl);
            if (origin is null)
            {
                continue;
            }

            var top = origin.Value.Y;
            var bottom = top + state.Host.Bounds.Height;
            var isVisibleRange = bottom >= viewportTop && top <= viewportBottom;
            if (isVisibleRange)
            {
                await EnsureContinuousPageRenderedAsync(state, tab, ct).ConfigureAwait(true);
            }
            else
            {
                UnloadContinuousPage(state);
            }
        }

        UpdateCurrentPageFromContinuousViewport(tab);
    }

    private async Task EnsureContinuousPageRenderedAsync(ContinuousPageItemState state, TabViewModel tab, CancellationToken ct)
    {
        if (state.IsRendering)
        {
            return;
        }

        if (state.PageControl is not null && Math.Abs(state.RenderedZoomLevel - tab.ZoomLevel) < 0.0001d)
        {
            return;
        }

        state.IsRendering = true;
        try
        {
            using var bitmap = await _pdfRenderService.RenderPageAsync(state.Page, tab.ZoomLevel, ct).ConfigureAwait(true);
            var pageControl = state.PageControl ?? new PdfPageControl
            {
                ZoomLevel = 1.0d,
                CurrentPage = state.Page.PageNumber,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            pageControl.Width = bitmap.Width;
            pageControl.Height = bitmap.Height;
            pageControl.SetBitmap(bitmap);
            if (state.PageControl is null)
            {
                pageControl.PointerMoved += OnContinuousPagePointerMoved;
                pageControl.PointerPressed += OnContinuousPagePointerPressed;
                pageControl.PointerReleased += OnContinuousPagePointerReleased;
                pageControl.ContextMenu = BuildTextSelectionContextMenu(pageControl);
            }

            state.PageControl = pageControl;
            state.RenderedZoomLevel = tab.ZoomLevel;

            var overlayCanvas = state.OverlayCanvas ?? new Canvas { IsHitTestVisible = true };
            overlayCanvas.Width = bitmap.Width;
            overlayCanvas.Height = bitmap.Height;
            state.OverlayCanvas = overlayCanvas;

            var layer = new Grid
            {
                Width = bitmap.Width,
                Height = bitmap.Height
            };
            layer.Children.Add(pageControl);
            layer.Children.Add(overlayCanvas);
            state.Host.Width = bitmap.Width;
            state.Host.Height = bitmap.Height;
            state.Host.Child = layer;

            _continuousPageMap[pageControl] = (tab, state.Page);
            ApplyHighlights(pageControl, tab, state.Page);
            RebuildCommentOverlay(tab, state.Page, overlayCanvas);
        }
        finally
        {
            state.IsRendering = false;
        }
    }

    private void UnloadContinuousPage(ContinuousPageItemState state)
    {
        if (state.PageControl is not null)
        {
            state.PageControl.SetBitmap(null);
            _continuousPageMap.Remove(state.PageControl);
        }

        state.OverlayCanvas?.Children.Clear();
        state.Host.Child = new Border
        {
            Background = new SolidColorBrush((Color)Application.Current!.FindResource("BgPanel2")!)
        };
    }

    private void ClearContinuousPageItems()
    {
        foreach (var state in _continuousPageStates)
        {
            if (state.PageControl is not null)
            {
                state.PageControl.PointerMoved -= OnContinuousPagePointerMoved;
                state.PageControl.PointerPressed -= OnContinuousPagePointerPressed;
                state.PageControl.PointerReleased -= OnContinuousPagePointerReleased;
                state.PageControl.SetBitmap(null);
            }
            state.OverlayCanvas?.Children.Clear();
        }

        _continuousPageStates.Clear();
        _continuousPageItems.Clear();
        _continuousPageMap.Clear();
    }

    private void UpdateCurrentPageFromContinuousViewport(TabViewModel tab)
    {
        if (!tab.IsContinuousMode || _continuousPageStates.Count == 0)
        {
            return;
        }

        var centerY = ContinuousScrollViewer.Viewport.Height / 2d;
        var best = _continuousPageStates
            .Select(state =>
            {
                var origin = state.Host.TranslatePoint(default, ContinuousScrollViewer);
                if (origin is null)
                {
                    return (State: state, Distance: double.MaxValue);
                }

                var itemCenter = origin.Value.Y + (state.Host.Bounds.Height / 2d);
                return (State: state, Distance: Math.Abs(itemCenter - centerY));
            })
            .OrderBy(pair => pair.Distance)
            .FirstOrDefault();

        if (best.State is null || best.State.Page.PageNumber == tab.CurrentPage)
        {
            return;
        }

        tab.JumpToPage(best.State.Page.PageNumber);
        RebuildThumbnailPanel();
        UpdateToolbarState();
        UpdateStatusBar();
    }

    private void RebuildTabBar()
    {
        TabHost.Children.Clear();

        foreach (var tab in _tabs)
        {
            var tabButton = new Button
            {
                Width = 180,
                MinWidth = 120,
                MaxWidth = 200,
                Margin = new Thickness(0, 0, 4, 0),
                Padding = new Thickness(8, 4),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(ReferenceEquals(tab, _activeTab)
                    ? (Color)Application.Current!.FindResource("BgTabActive")!
                    : (Color)Application.Current!.FindResource("BgTabInactive")!),
                Foreground = new SolidColorBrush(ReferenceEquals(tab, _activeTab)
                    ? (Color)Application.Current!.FindResource("TextPrimary")!
                    : (Color)Application.Current!.FindResource("TextSecondary")!)
            };

            var panel = new DockPanel { LastChildFill = true };
            var closeButton = new Button
            {
                Content = "×",
                Width = 18,
                Height = 18,
                Padding = new Thickness(0),
                Margin = new Thickness(4, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right
            };

            closeButton.Click += async (_, _) => await CloseTabAsync(tab).ConfigureAwait(true);
            DockPanel.SetDock(closeButton, Dock.Right);
            panel.Children.Add(closeButton);
            panel.Children.Add(new TextBlock
            {
                Text = tab.Title,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            tabButton.Content = panel;
            tabButton.Click += (_, _) => ActivateTab(tab);
            tabButton.ContextMenu = BuildTabContextMenu(tab);
            tabButton.PointerEntered += (_, _) => closeButton.IsVisible = true;
            tabButton.PointerExited += (_, _) => closeButton.IsVisible = false;
            closeButton.IsVisible = false;
            TabHost.Children.Add(tabButton);
        }
    }

    private ContextMenu BuildTabContextMenu(TabViewModel tab)
    {
        var duplicate = new MenuItem { Header = "複製" };
        duplicate.Click += (_, _) => OpenFileWithoutAwait(tab.Document.FilePath);

        var closeOthers = new MenuItem { Header = "他を閉じる" };
        closeOthers.Click += async (_, _) => await CloseOtherTabsAsync(tab).ConfigureAwait(true);

        var closeRight = new MenuItem { Header = "右を閉じる" };
        closeRight.Click += async (_, _) => await CloseTabsToRightAsync(tab).ConfigureAwait(true);

        var openExplorer = new MenuItem { Header = "エクスプローラーで開く" };
        openExplorer.Click += (_, _) => OpenInExplorer(tab.Document.FilePath);

        return new ContextMenu
        {
            ItemsSource = new object[]
            {
                duplicate,
                closeOthers,
                closeRight,
                new Separator(),
                openExplorer
            }
        };
    }

    private void RebuildThumbnailPanel()
    {
        ThumbnailPanel.Children.Clear();
        var tab = _activeTab;
        if (tab is null)
        {
            return;
        }

        foreach (var thumbnail in tab.Thumbnails)
        {
            var border = new Border
            {
                BorderThickness = new Thickness(2),
                BorderBrush = new SolidColorBrush((Color)Application.Current!.FindResource(
                    thumbnail.IsSelected ? "Accent" : "BorderLight")!),
                Background = new SolidColorBrush((Color)Application.Current!.FindResource("BgPanel2")!),
                Padding = new Thickness(4)
            };

            var stack = new StackPanel { Spacing = 4 };
            var thumbControl = new PdfPageControl
            {
                Width = ThumbnailWidthPx,
                Height = Math.Round(ThumbnailWidthPx / A4AspectRatio),
                ZoomLevel = 1.0d,
                CurrentPage = thumbnail.PageNumber
            };

            if (thumbnail.Bitmap is not null)
            {
                thumbControl.SetBitmap(thumbnail.Bitmap);
            }

            stack.Children.Add(thumbControl);
            stack.Children.Add(new TextBlock
            {
                Text = thumbnail.PageNumber.ToString(CultureInfo.InvariantCulture),
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush((Color)Application.Current!.FindResource("TextPrimary")!)
            });

            var button = new Button
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Content = border,
                Tag = thumbnail.PageNumber
            };
            border.Child = stack;
            button.Click += (_, _) =>
            {
                tab.JumpToPage(thumbnail.PageNumber);
                RebuildThumbnailPanel();
                _ = RenderActiveTabAsync();
            };
            button.PointerPressed += OnThumbnailPointerPressed;
            button.PointerMoved += OnThumbnailPointerMoved;
            DragDrop.SetAllowDrop(button, true);
            button.AddHandler(DragDrop.DragOverEvent, OnThumbnailDragOver);
            button.AddHandler(DragDrop.DragLeaveEvent, OnThumbnailDragLeave);
            button.AddHandler(DragDrop.DropEvent, OnThumbnailDrop);

            ThumbnailPanel.Children.Add(button);

            if (thumbnail.IsSelected)
            {
                button.BringIntoView();
            }
        }
    }

    private void OnThumbnailPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control control && control.Tag is int pageNumber)
        {
            _thumbnailDragPageNumber = pageNumber;
        }
    }

    private async void OnThumbnailPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_thumbnailDragPageNumber is null ||
            sender is not Control control ||
            e.GetCurrentPoint(control).Properties.IsLeftButtonPressed is false)
        {
            return;
        }

        var dragNumber = _thumbnailDragPageNumber.Value;
        _thumbnailDragPageNumber = null;
        var dataObject = new DataObject();
        dataObject.Set("acro.thumbnail.page", dragNumber.ToString(CultureInfo.InvariantCulture));
        await DragDrop.DoDragDrop(e, dataObject, DragDropEffects.Move).ConfigureAwait(true);
    }

    private void OnThumbnailDragOver(object? sender, DragEventArgs e)
    {
        Border? border = null;
        if (sender is Button button && button.Content is Border contentBorder)
        {
            border = contentBorder;
        }
        if (e.Data.Contains("acro.thumbnail.page"))
        {
            e.DragEffects = DragDropEffects.Move;
            if (border is not null)
            {
                border.BorderBrush = new SolidColorBrush((Color)Application.Current!.FindResource("Accent")!);
            }
        }
    }

    private void OnThumbnailDragLeave(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Content is not Border border ||
            button.Tag is not int pageNumber ||
            _activeTab is null)
        {
            return;
        }

        var selected = _activeTab.CurrentPage == pageNumber;
        border.BorderBrush = new SolidColorBrush((Color)Application.Current!.FindResource(selected ? "Accent" : "BorderLight")!);
    }

    private async void OnThumbnailDrop(object? sender, DragEventArgs e)
    {
        if (_activeTab is null ||
            sender is not Button button ||
            button.Tag is not int targetPage ||
            !e.Data.Contains("acro.thumbnail.page"))
        {
            return;
        }

        var sourceRaw = e.Data.Get("acro.thumbnail.page")?.ToString();
        if (!int.TryParse(sourceRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sourcePage) ||
            sourcePage == targetPage)
        {
            return;
        }

        var order = Enumerable.Range(1, _activeTab.PageCount).ToList();
        order.Remove(sourcePage);
        var targetIndex = Math.Max(0, order.IndexOf(targetPage));
        order.Insert(targetIndex, sourcePage);
        await _pdfRenderService.ReorderPagesAsync(_activeTab.Document, order).ConfigureAwait(true);

        var filePath = _activeTab.Document.FilePath;
        var currentPage = Math.Clamp(targetPage, 1, _activeTab.PageCount);
        CloseTabCore(_activeTab);
        _ = await OpenFileAsync(filePath, currentPage).ConfigureAwait(true);
    }

    private void RebuildBookmarkPanel()
    {
        BookmarkPanel.Children.Clear();
        if (_activeTab is null || !_bookmarkMap.TryGetValue(_activeTab, out var bookmarks))
        {
            return;
        }

        foreach (var bookmark in bookmarks)
        {
            AddBookmarkNode(BookmarkPanel, bookmark, 0);
        }
    }

    private void RebuildAttachmentPanel()
    {
        if (_activeTab is null || !_attachmentMap.TryGetValue(_activeTab, out var attachments) || attachments.Count == 0)
        {
            AttachmentListBox.ItemsSource = Array.Empty<object>();
            ExtractAttachmentButton.IsEnabled = false;
            return;
        }

        AttachmentListBox.ItemsSource = attachments
            .Select(file => new AttachmentPanelItem(file, $"{file.Name} ({FormatSize(file.Size)})"))
            .ToArray();
        ExtractAttachmentButton.IsEnabled = AttachmentListBox.SelectedItem is AttachmentPanelItem;
    }

    private void AddBookmarkNode(Panel host, PdfBookmarkItem bookmark, int depth)
    {
        var expander = new Expander
        {
            IsExpanded = true,
            Header = CreateBookmarkButton(bookmark, depth),
            Margin = new Thickness(depth * 8, 2, 0, 2)
        };

        if (bookmark.Children.Count == 0)
        {
            host.Children.Add(CreateBookmarkButton(bookmark, depth));
            return;
        }

        var childPanel = new StackPanel { Spacing = 2 };
        foreach (var child in bookmark.Children)
        {
            AddBookmarkNode(childPanel, child, depth + 1);
        }

        expander.Content = childPanel;
        host.Children.Add(expander);
    }

    private Button CreateBookmarkButton(PdfBookmarkItem bookmark, int depth)
    {
        var button = new Button
        {
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 2),
            Margin = new Thickness(depth * 8, 0, 0, 0),
            Foreground = new SolidColorBrush((Color)Application.Current!.FindResource("TextPrimary")!),
            Content = string.IsNullOrWhiteSpace(bookmark.Title) ? "(無題)" : bookmark.Title
        };
        button.Click += (_, _) =>
        {
            if (_activeTab is null || bookmark.PageNumber <= 0)
            {
                return;
            }

            _activeTab.JumpToPage(bookmark.PageNumber);
            RebuildThumbnailPanel();
            _ = RenderActiveTabAsync();
        };
        return button;
    }

    private void ApplyHighlights(PdfPageControl control, TabViewModel tab, PdfPage page)
    {
        var pageResults = tab.SearchResults.Where(result => result.PageNumber == page.PageNumber).ToArray();
        control.SearchHighlights = pageResults
            .Select(result => ConvertBoundsToPixelRect(result.Bounds, page, tab.ZoomLevel))
            .ToArray();

        var currentResult = tab.CurrentSearchResult;
        if (currentResult is SearchResult searchResult && searchResult.PageNumber == page.PageNumber)
        {
            control.CurrentSearchHighlight = ConvertBoundsToPixelRect(searchResult.Bounds, page, tab.ZoomLevel);
        }
        else
        {
            control.CurrentSearchHighlight = null;
        }

        if (_selectionHighlightMap.TryGetValue((tab, page.PageNumber), out var selectionRects))
        {
            control.SelectionHighlights = selectionRects;
        }
        else
        {
            control.SelectionHighlights = [];
        }

        control.AnnotationVisuals = tab.Document.Annotations
            .Where(annotation => annotation.PageNumber == page.PageNumber)
            .SelectMany(annotation => MapAnnotationVisuals(annotation, page, tab.ZoomLevel))
            .ToArray();
    }

    private static IReadOnlyList<AnnotationVisual> MapAnnotationVisuals(Annotation annotation, PdfPage page, double zoomLevel)
    {
        switch (annotation)
        {
            case HighlightAnnotation highlight:
                return
                [
                    new AnnotationVisual(
                        ConvertBoundsToPixelRect(highlight.Bounds, page, zoomLevel),
                        highlight.Type switch
                        {
                            HighlightType.Underline => AnnotationVisualKind.Underline,
                            HighlightType.Strikethrough => AnnotationVisualKind.Strikethrough,
                            _ => AnnotationVisualKind.Highlight
                        },
                        ToAvaloniaColor(highlight.Color))
                ];
            case FreehandAnnotation freehand:
                return freehand.Strokes
                    .Where(stroke => stroke.Count > 1)
                    .Select(stroke => new AnnotationVisual(
                        ConvertBoundsToPixelRect(freehand.Bounds, page, zoomLevel),
                        AnnotationVisualKind.Freehand,
                        ParseColorHex(freehand.StrokeColorHex, Color.FromRgb(255, 0, 0)),
                        stroke.Select(point => ConvertPdfPointToPixelPoint(point, page, zoomLevel)).ToArray(),
                        null,
                        freehand.StrokeWidth))
                    .ToArray();
            case ShapeAnnotation shape:
                return
                [
                    new AnnotationVisual(
                        ConvertBoundsToPixelRect(shape.Bounds, page, zoomLevel),
                        shape.Type switch
                        {
                            ShapeType.Ellipse => AnnotationVisualKind.Ellipse,
                            ShapeType.Arrow => AnnotationVisualKind.Arrow,
                            ShapeType.Line => AnnotationVisualKind.Line,
                            _ => AnnotationVisualKind.Rectangle
                        },
                        ParseColorHex(shape.StrokeColorHex, Color.FromRgb(255, 0, 0)),
                        null,
                        string.IsNullOrWhiteSpace(shape.FillColorHex)
                            ? null
                            : ParseColorHex(shape.FillColorHex, Color.FromArgb(96, 255, 0, 0)),
                        shape.StrokeWidth)
                ];
            case CommentAnnotation comment when IsStampComment(comment):
                return
                [
                    new AnnotationVisual(
                        ConvertBoundsToPixelRect(comment.Bounds, page, zoomLevel),
                        AnnotationVisualKind.Stamp,
                        Color.FromRgb(224, 88, 32),
                        null,
                        null,
                        2d,
                        comment.Text)
                ];
            default:
                return [];
        }
    }

    private static Avalonia.Rect ConvertBoundsToPixelRect(PdfTextBounds bounds, PdfPage page, double zoomLevel)
    {
        var scale = (ScreenDpi * Math.Max(0.01d, zoomLevel)) / PdfDpi;
        var left = Math.Min(bounds.Left, bounds.Right) * scale;
        var right = Math.Max(bounds.Left, bounds.Right) * scale;
        var topPdf = Math.Max(bounds.Top, bounds.Bottom);
        var bottomPdf = Math.Min(bounds.Top, bounds.Bottom);
        var y = (page.HeightPt - topPdf) * scale;
        var height = Math.Max(1d, (topPdf - bottomPdf) * scale);
        return new Avalonia.Rect(left, y, Math.Max(1d, right - left), height);
    }

    private static Color ToAvaloniaColor(HighlightColor color)
    {
        return color switch
        {
            HighlightColor.Green => Color.FromRgb(0, 200, 120),
            HighlightColor.Blue => Color.FromRgb(80, 160, 255),
            HighlightColor.Pink => Color.FromRgb(255, 120, 180),
            _ => Color.FromRgb(255, 220, 0)
        };
    }

    private void RebuildCommentOverlay(TabViewModel tab, PdfPage page, Canvas? overlayCanvas = null)
    {
        var targetCanvas = overlayCanvas ?? AnnotationOverlayCanvas;
        targetCanvas.Children.Clear();
        var dpiScale = (ScreenDpi * Math.Max(0.01d, tab.ZoomLevel)) / PdfDpi;
        var comments = tab.Document.Annotations
            .Where(annotation => annotation.PageNumber == page.PageNumber)
            .OfType<CommentAnnotation>()
            .Where(comment => !IsStampComment(comment))
            .ToArray();
        foreach (var comment in comments)
        {
            var anchor = _annotationService.ConvertPdfToScreen(
                Math.Min(comment.Bounds.Left, comment.Bounds.Right),
                Math.Max(comment.Bounds.Top, comment.Bounds.Bottom),
                dpiScale,
                page.HeightPt);

            if (!comment.IsOpen)
            {
                var pinButton = new Button
                {
                    Content = "📌",
                    Width = 26,
                    Height = 26,
                    Padding = new Thickness(0),
                    Background = new SolidColorBrush(Color.Parse("#fffde7")),
                    BorderBrush = new SolidColorBrush(Color.Parse("#f0c040")),
                    BorderThickness = new Thickness(1)
                };
                pinButton.Click += (_, _) =>
                {
                    comment.IsOpen = true;
                    comment.Touch();
                    tab.Document.MarkModified();
                    RebuildCommentOverlay(tab, page, targetCanvas);
                };
                Canvas.SetLeft(pinButton, Math.Max(0, anchor.X));
                Canvas.SetTop(pinButton, Math.Max(0, anchor.Y));
                targetCanvas.Children.Add(pinButton);
                continue;
            }

            var panel = new StackPanel { Spacing = 4 };
            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var pin = new Button
            {
                Content = "📌",
                Width = 24,
                Height = 24,
                Padding = new Thickness(0)
            };
            pin.Click += (_, _) =>
            {
                comment.IsOpen = false;
                comment.Touch();
                tab.Document.MarkModified();
                RebuildCommentOverlay(tab, page, targetCanvas);
            };
            header.Children.Add(pin);
            header.Children.Add(new TextBlock
            {
                Text = $"{comment.Author}  {comment.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}",
                Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                VerticalAlignment = VerticalAlignment.Center
            });
            panel.Children.Add(header);

            var textBox = new TextBox
            {
                Text = comment.Text,
                AcceptsReturn = true,
                Width = 220,
                MinHeight = 70,
                TextWrapping = TextWrapping.Wrap
            };
            textBox.TextChanged += (_, _) =>
            {
                comment.Text = textBox.Text ?? string.Empty;
                comment.Touch();
                tab.Document.MarkModified();
            };
            panel.Children.Add(textBox);

            var noteBorder = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#fffde7")),
                BorderBrush = new SolidColorBrush(Color.Parse("#f0c040")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8),
                Child = panel
            };
            Canvas.SetLeft(noteBorder, Math.Max(0, anchor.X));
            Canvas.SetTop(noteBorder, Math.Max(0, anchor.Y));
            targetCanvas.Children.Add(noteBorder);
        }
    }

    private async Task RebuildFormOverlayAsync(TabViewModel tab, PdfPage page, CancellationToken ct)
    {
        IReadOnlyList<PdfFormField> fields;
        if (_formFieldMap.TryGetValue((tab, page.PageNumber), out var cached))
        {
            fields = cached;
        }
        else
        {
            fields = await _pdfRenderService.GetFormFieldsAsync(page, ct).ConfigureAwait(true);
            _formFieldMap[(tab, page.PageNumber)] = fields;
        }

        foreach (var field in fields)
        {
            var bounds = ConvertBoundsToPixelRect(field.Bounds, page, tab.ZoomLevel);
            if (bounds.Width <= 1 || bounds.Height <= 1)
            {
                continue;
            }

            Control? control = field.FieldType switch
            {
                PdfFormFieldType.Text => CreateTextFormControl(tab, page, field, bounds),
                PdfFormFieldType.CheckBox => CreateBooleanFormControl(tab, page, field, bounds, isRadio: false),
                PdfFormFieldType.RadioButton => CreateBooleanFormControl(tab, page, field, bounds, isRadio: true),
                PdfFormFieldType.ComboBox => CreateComboFormControl(tab, page, field, bounds),
                PdfFormFieldType.Signature => CreateSignatureFormControl(tab, page, field, bounds),
                _ => null
            };

            if (control is null)
            {
                continue;
            }

            Canvas.SetLeft(control, bounds.X);
            Canvas.SetTop(control, bounds.Y);
            AnnotationOverlayCanvas.Children.Add(control);
        }
    }

    private Control CreateTextFormControl(TabViewModel tab, PdfPage page, PdfFormField field, Avalonia.Rect bounds)
    {
        var textBox = new TextBox
        {
            Text = field.Value,
            Width = Math.Max(32, bounds.Width),
            Height = Math.Max(22, bounds.Height),
            IsReadOnly = field.IsReadOnly,
            Background = new SolidColorBrush(Color.Parse("#f8f8f8")),
            BorderBrush = new SolidColorBrush(Color.Parse("#4a90e2")),
            BorderThickness = new Thickness(1)
        };
        textBox.LostFocus += async (_, _) =>
        {
            field.Value = textBox.Text ?? string.Empty;
            await _pdfRenderService.ApplyFormFieldInputAsync(page, field, field.Value).ConfigureAwait(true);
            tab.Document.MarkModified();
        };
        return textBox;
    }

    private Control CreateBooleanFormControl(TabViewModel tab, PdfPage page, PdfFormField field, Avalonia.Rect bounds, bool isRadio)
    {
        var checkBox = new CheckBox
        {
            IsChecked = field.IsChecked,
            Width = Math.Max(18, bounds.Width),
            Height = Math.Max(18, bounds.Height),
            IsEnabled = !field.IsReadOnly,
            Content = isRadio ? "○" : string.Empty
        };
        checkBox.Click += async (_, _) =>
        {
            field.IsChecked = checkBox.IsChecked == true;
            await _pdfRenderService.ApplyFormFieldInputAsync(page, field, null, field.IsChecked).ConfigureAwait(true);
            tab.Document.MarkModified();
        };
        return checkBox;
    }

    private Control CreateComboFormControl(TabViewModel tab, PdfPage page, PdfFormField field, Avalonia.Rect bounds)
    {
        var options = field.Options.Count > 0 ? field.Options : [field.Value];
        var comboBox = new ComboBox
        {
            ItemsSource = options,
            SelectedItem = string.IsNullOrWhiteSpace(field.Value) ? options.FirstOrDefault() : field.Value,
            Width = Math.Max(56, bounds.Width),
            Height = Math.Max(24, bounds.Height),
            IsEnabled = !field.IsReadOnly
        };
        comboBox.SelectionChanged += async (_, _) =>
        {
            field.Value = comboBox.SelectedItem?.ToString() ?? string.Empty;
            await _pdfRenderService.ApplyFormFieldInputAsync(page, field, field.Value).ConfigureAwait(true);
            tab.Document.MarkModified();
        };
        return comboBox;
    }

    private Control CreateSignatureFormControl(TabViewModel tab, PdfPage page, PdfFormField field, Avalonia.Rect bounds)
    {
        var button = new Button
        {
            Width = Math.Max(120, bounds.Width),
            Height = Math.Max(28, bounds.Height),
            Content = string.IsNullOrWhiteSpace(field.Value) ? "署名を入力..." : FormatSignatureDisplay(field.Value),
            IsEnabled = !field.IsReadOnly
        };
        button.Click += async (_, _) =>
        {
            var signature = await ShowSignatureInputDialogAsync().ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(signature))
            {
                return;
            }

            field.Value = signature;
            button.Content = FormatSignatureDisplay(signature);
            await _pdfRenderService.ApplyFormFieldInputAsync(page, field, signature).ConfigureAwait(true);
            tab.Document.MarkModified();
        };
        return button;
    }

    private static string FormatSignatureDisplay(string value)
    {
        return value.StartsWith("HAND:", StringComparison.Ordinal)
            ? "手書き署名"
            : value;
    }

    private async Task<string?> ShowSignatureInputDialogAsync()
    {
        var dialog = new Window
        {
            Width = 520,
            Height = 360,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Title = "署名入力",
            Background = new SolidColorBrush((Color)Application.Current!.FindResource("BgDark")!)
        };

        var strokes = new List<Point>();
        var drawingLine = new Avalonia.Controls.Shapes.Polyline
        {
            Stroke = new SolidColorBrush(Color.Parse("#202020")),
            StrokeThickness = 2
        };
        var drawingCanvas = new Canvas
        {
            Width = 460,
            Height = 180,
            Background = Brushes.White,
            Children = { drawingLine }
        };
        var isDrawing = false;
        drawingCanvas.PointerPressed += (_, e) =>
        {
            isDrawing = true;
            strokes.Clear();
            drawingLine.Points.Clear();
            var point = e.GetPosition(drawingCanvas);
            strokes.Add(point);
            drawingLine.Points.Add(point);
        };
        drawingCanvas.PointerMoved += (_, e) =>
        {
            if (!isDrawing)
            {
                return;
            }

            var point = e.GetPosition(drawingCanvas);
            strokes.Add(point);
            drawingLine.Points.Add(point);
        };
        drawingCanvas.PointerReleased += (_, _) => isDrawing = false;

        var fontComboBox = new ComboBox
        {
            ItemsSource = new[] { "Segoe UI", "Arial", "Times New Roman", "Courier New" },
            SelectedIndex = 0,
            Width = 180
        };
        var signatureTextBox = new TextBox
        {
            Width = 260,
            Watermark = "署名テキストを入力"
        };

        var tabs = new TabControl
        {
            ItemsSource = new object[]
            {
                new TabItem
                {
                    Header = "手書き",
                    Content = new StackPanel
                    {
                        Spacing = 8,
                        Children =
                        {
                            new TextBlock { Text = "ポインターで署名してください", Foreground = new SolidColorBrush((Color)Application.Current.FindResource("TextPrimary")!) },
                            drawingCanvas
                        }
                    }
                },
                new TabItem
                {
                    Header = "テキスト",
                    Content = new StackPanel
                    {
                        Spacing = 8,
                        Children =
                        {
                            new TextBlock { Text = "フォントを選択して入力", Foreground = new SolidColorBrush((Color)Application.Current.FindResource("TextPrimary")!) },
                            fontComboBox,
                            signatureTextBox
                        }
                    }
                }
            }
        };

        string? result = null;
        var okButton = new Button { Content = "適用", Width = 88 };
        var cancelButton = new Button { Content = "キャンセル", Width = 88 };
        okButton.Click += (_, _) =>
        {
            if (tabs.SelectedIndex == 0)
            {
                result = strokes.Count > 1
                    ? $"HAND:{string.Join(";", strokes.Select(point => $"{point.X:0.##},{point.Y:0.##}"))}"
                    : null;
            }
            else
            {
                var text = signatureTextBox.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var font = fontComboBox.SelectedItem?.ToString() ?? "Segoe UI";
                    result = $"{text} [{font}]";
                }
            }

            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { cancelButton, okButton }
        };
        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Margin = new Thickness(12)
        };
        Grid.SetRow(tabs, 0);
        Grid.SetRow(buttonRow, 1);
        layout.Children.Add(tabs);
        layout.Children.Add(buttonRow);
        dialog.Content = layout;
        await dialog.ShowDialog(this).ConfigureAwait(true);
        return result;
    }

    private void RebuildAnnotationPanel()
    {
        if (_activeTab is null)
        {
            AnnotationPanelListBox.ItemsSource = Array.Empty<AnnotationPanelItem>();
            AnnotationCountBadgeTextBlock.Text = "0";
            DeleteAnnotationButton.IsEnabled = false;
            return;
        }

        var items = _activeTab.Document.Annotations
            .OrderBy(annotation => annotation.PageNumber)
            .Select(annotation => new AnnotationPanelItem(
                annotation.Id,
                annotation.PageNumber,
                GetAnnotationBadge(annotation),
                GetAnnotationSummary(annotation)))
            .ToArray();
        AnnotationPanelListBox.ItemsSource = items;
        AnnotationCountBadgeTextBlock.Text = items.Length.ToString(CultureInfo.InvariantCulture);
        DeleteAnnotationButton.IsEnabled = AnnotationPanelListBox.SelectedItem is AnnotationPanelItem;
    }

    private void UpdateFileInfoPanel()
    {
        if (_activeTab is null)
        {
            FileInfoTextBlock.Text = "ファイル未選択";
            return;
        }

        var path = _activeTab.Document.FilePath;
        var fileInfo = new FileInfo(path);
        var size = fileInfo.Exists
            ? fileInfo.Length < (1024 * 1024)
                ? $"{Math.Max(1d, fileInfo.Length / 1024d):0} KB"
                : $"{fileInfo.Length / 1024d / 1024d:0.0} MB"
            : "不明";
        FileInfoTextBlock.Text =
            $"ページ: {_activeTab.PageCount} / サイズ: {size} / 暗号化: {(_activeSecurityInfo.IsEncrypted ? _activeSecurityInfo.EncryptionDescription : "なし")}";
    }

    private static string FormatSize(long size)
    {
        return size < 1024 * 1024
            ? $"{Math.Max(1d, size / 1024d):0} KB"
            : $"{size / 1024d / 1024d:0.0} MB";
    }

    private static string GetAnnotationBadge(Annotation annotation)
    {
        return annotation switch
        {
            HighlightAnnotation highlight => highlight.Type switch
            {
                HighlightType.Underline => "🔴",
                HighlightType.Strikethrough => "🔴",
                _ => "🟡"
            },
            CommentAnnotation comment when IsStampComment(comment) => "🟢",
            CommentAnnotation => "🔵",
            FreehandAnnotation => "🟢",
            ShapeAnnotation => "🟢",
            _ => "⚪"
        };
    }

    private static string GetAnnotationSummary(Annotation annotation)
    {
        return annotation switch
        {
            HighlightAnnotation highlight => highlight.Type switch
            {
                HighlightType.Underline => "下線",
                HighlightType.Strikethrough => "取り消し線",
                _ => "ハイライト"
            },
            CommentAnnotation comment when IsStampComment(comment) => $"スタンプ: {comment.Text}",
            CommentAnnotation comment => string.IsNullOrWhiteSpace(comment.Text) ? "コメント" : $"コメント: {comment.Text}",
            FreehandAnnotation => "フリーハンド",
            ShapeAnnotation shape => shape.Type switch
            {
                ShapeType.Ellipse => "楕円",
                ShapeType.Arrow => "矢印",
                ShapeType.Line => "線",
                _ => "矩形"
            },
            _ => "注釈"
        };
    }

    private static bool IsStampComment(CommentAnnotation comment)
        => comment.Text?.StartsWith(StampPrefix, StringComparison.Ordinal) == true;

    private static Color ParseColorHex(string? colorHex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(colorHex))
        {
            return fallback;
        }

        try
        {
            return Color.Parse(colorHex);
        }
        catch (FormatException)
        {
            return fallback;
        }
    }

    private static Point ConvertPdfPointToPixelPoint(AnnotationPoint point, PdfPage page, double zoomLevel)
    {
        var scale = (ScreenDpi * Math.Max(0.01d, zoomLevel)) / PdfDpi;
        return new Point(point.X * scale, (page.HeightPt - point.Y) * scale);
    }

    private void UpdateToolbarState()
    {
        var tab = _activeTab;
        var hasTab = tab is not null;
        var totalPages = tab?.PageCount ?? 0;
        PageNumberTextBox.IsEnabled = hasTab;
        TotalPageTextBlock.Text = $"/ {totalPages}";
        PageNumberTextBox.Text = tab?.CurrentPage.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        SinglePageModeButton.IsChecked = hasTab && !tab!.IsContinuousMode;
        ContinuousModeButton.IsChecked = hasTab && tab!.IsContinuousMode;
        TwoPageModeButton.IsChecked = hasTab && tab!.IsTwoPageMode;
        SplitViewModeButton.IsChecked = _mainWindowViewModel.IsSplitView;

        if (hasTab)
        {
            var zoomLabel = $"{Math.Round(tab!.ZoomLevel * 100d, MidpointRounding.AwayFromZero):0}%";
            ZoomComboBox.SelectedItem = ZoomComboBox.Items
                ?.Cast<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Content?.ToString(), zoomLabel, StringComparison.Ordinal));
        }
        else
        {
            ZoomComboBox.SelectedItem = null;
        }

        HighlightToolButton.IsChecked = _activeAnnotationTool == AnnotationTool.Highlight;
        CommentToolButton.IsChecked = _activeAnnotationTool == AnnotationTool.Comment;
        FreehandToolButton.IsChecked = _activeAnnotationTool == AnnotationTool.Freehand;
        ShapeToolButton.IsChecked = _activeAnnotationTool == AnnotationTool.Shape;
        StampToolButton.IsChecked = _activeAnnotationTool == AnnotationTool.Stamp;
        var canAnnotate = hasTab && _activeSecurityInfo.CanAnnotate;
        HighlightToolButton.IsEnabled = canAnnotate;
        CommentToolButton.IsEnabled = canAnnotate;
        FreehandToolButton.IsEnabled = canAnnotate;
        ShapeToolButton.IsEnabled = canAnnotate;
        StampToolButton.IsEnabled = canAnnotate;
        DeleteAnnotationButton.IsEnabled = canAnnotate && AnnotationPanelListBox.SelectedItem is AnnotationPanelItem;
        ExportFdfButton.IsEnabled = canAnnotate;
        ImportFdfButton.IsEnabled = canAnnotate;
        PrintButton.IsEnabled = hasTab && _activeSecurityInfo.CanPrint;
        ExtractAttachmentButton.IsEnabled = hasTab && AttachmentListBox.SelectedItem is AttachmentPanelItem;
    }

    private void UpdateStatusBar()
    {
        var tab = _activeTab;
        FileStatusTextBlock.Text = tab?.Title ?? "(未選択)";
        PageStatusTextBlock.Text = tab is null ? "0 / 0" : $"{tab.CurrentPage} / {tab.PageCount}";
        ZoomStatusTextBlock.Text = tab is null
            ? "0%"
            : $"{Math.Round(tab.ZoomLevel * 100d, MidpointRounding.AwayFromZero):0}%";
        MouseStatusTextBlock.Text = tab is null
            ? "(0, 0)"
            : $"({Math.Round(tab.MouseX):0}, {Math.Round(tab.MouseY):0})";
    }

    private void InitializeStaticStatusText()
    {
        PlatformStatusTextBlock.Text = $"{Environment.OSVersion.Platform} / .NET {Environment.Version}";
        ReadyStatusTextBlock.Text = "● 準備完了";
        UpdateStatusBar();
    }

    private void UpdateSearchOverlayState()
    {
        if (_activeTab is null)
        {
            SearchOverlay.IsVisible = false;
            SearchTextBox.Text = string.Empty;
            SearchCountTextBlock.Text = "0/0件";
            return;
        }

        SearchOverlay.IsVisible = _activeTab.IsSearchVisible;
        SearchTextBox.Text = _activeTab.SearchQuery;
        CaseSensitiveSearchCheckBox.IsChecked = _activeTab.IsSearchCaseSensitive;
        RegexSearchCheckBox.IsChecked = _activeTab.IsSearchRegex;
        UpdateSearchCountText();
    }

    private async Task ExecuteSearchAsync()
    {
        CancelSearch();
        if (_activeTab is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_activeTab.SearchQuery))
        {
            _activeTab.SetSearchResults([]);
            UpdateSearchCountText();
            _ = RenderActiveTabAsync();
            return;
        }

        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        try
        {
            var results = await _searchViewModel.SearchAsync(
                _activeTab.Document,
                _activeTab.SearchQuery,
                new SearchOptions(_activeTab.IsSearchCaseSensitive, _activeTab.IsSearchRegex),
                token).ConfigureAwait(true);
            _activeTab.SetSearchResults(results);
            UpdateSearchCountText();
            _ = RenderActiveTabAsync();
        }
        catch (OperationCanceledException)
        {
            // 検索途中キャンセルは正常系。
        }
    }

    private void UpdateSearchCountText()
    {
        var tab = _activeTab;
        if (tab is null || tab.SearchResults.Count == 0)
        {
            SearchCountTextBlock.Text = "0/0件";
            return;
        }

        SearchCountTextBlock.Text = $"{tab.CurrentSearchResultIndex + 1}/{tab.SearchResults.Count}件";
    }

    private void MoveToNextSearchResult(bool reverse)
    {
        if (_activeTab is null)
        {
            return;
        }

        if (reverse)
        {
            _activeTab.MoveToPreviousSearchResult();
        }
        else
        {
            _activeTab.MoveToNextSearchResult();
        }

        UpdateSearchCountText();
        RebuildThumbnailPanel();
        _ = RenderActiveTabAsync();
    }

    private TabViewModel? GetCommandTargetTab()
    {
        if (!_mainWindowViewModel.IsSplitView || !_isSplitRightPaneActive)
        {
            return _activeTab;
        }

        return _splitSecondaryTab ?? _activeTab;
    }

    private void InitializeTextSelectionContextMenu()
    {
        PageControl.ContextMenu = BuildTextSelectionContextMenu(PageControl);
    }

    private ContextMenu BuildTextSelectionContextMenu(PdfPageControl control)
    {
        var copy = new MenuItem { Header = "コピー" };
        copy.Click += async (_, _) => await CopySelectedTextAsync().ConfigureAwait(true);

        var highlight = new MenuItem { Header = "ハイライト" };
        var highlightYellow = new MenuItem { Header = "黄" };
        highlightYellow.Click += (_, _) => AddHighlightFromSelection(HighlightType.Highlight, HighlightColor.Yellow);
        var highlightGreen = new MenuItem { Header = "緑" };
        highlightGreen.Click += (_, _) => AddHighlightFromSelection(HighlightType.Highlight, HighlightColor.Green);
        var highlightBlue = new MenuItem { Header = "青" };
        highlightBlue.Click += (_, _) => AddHighlightFromSelection(HighlightType.Highlight, HighlightColor.Blue);
        var highlightPink = new MenuItem { Header = "ピンク" };
        highlightPink.Click += (_, _) => AddHighlightFromSelection(HighlightType.Highlight, HighlightColor.Pink);
        highlight.ItemsSource = new object[] { highlightYellow, highlightGreen, highlightBlue, highlightPink };

        var underline = new MenuItem { Header = "下線を引く" };
        underline.Click += (_, _) => AddHighlightFromSelection(HighlightType.Underline, HighlightColor.Yellow);
        var strike = new MenuItem { Header = "取り消し線" };
        strike.Click += (_, _) => AddHighlightFromSelection(HighlightType.Strikethrough, HighlightColor.Pink);
        var comment = new MenuItem { Header = "コメントを追加" };
        comment.Click += (_, _) => AddCommentFromSelection();
        var link = new MenuItem { Header = "リンクを追加" };
        var searchSelected = new MenuItem { Header = "選択テキストを検索" };
        searchSelected.Click += (_, _) =>
        {
            if (_activeTab is null || string.IsNullOrWhiteSpace(_selectedText))
            {
                return;
            }

            _activeTab.IsSearchVisible = true;
            _activeTab.SearchQuery = _selectedText.Trim();
            UpdateSearchOverlayState();
            _ = ExecuteSearchAsync();
        };

        return new ContextMenu
        {
            PlacementTarget = control,
            ItemsSource = new object[]
            {
                copy,
                highlight,
                underline,
                strike,
                new Separator(),
                comment,
                link,
                new Separator(),
                searchSelected
            }
        };
    }

    private void AddHighlightFromSelection(HighlightType type, HighlightColor color)
    {
        var tab = _activeTab;
        var page = _selectionPage;
        if (tab is null || page is null || !_selectionPdfBoundsMap.TryGetValue((tab, page.PageNumber), out var selectionBounds))
        {
            return;
        }

        foreach (var bounds in selectionBounds)
        {
            tab.Document.AddAnnotation(new HighlightAnnotation
            {
                PageNumber = page.PageNumber,
                Bounds = bounds,
                Type = type,
                Color = color
            });
        }

        RebuildAnnotationPanel();
        _ = RenderActiveTabAsync();
    }

    private void AddCommentFromSelection()
    {
        var tab = _activeTab;
        if (tab is null)
        {
            return;
        }

        var page = _selectionPage ?? tab.Document.Pages[tab.CurrentPage - 1];
        var selectedBounds = _selectionPdfBoundsMap.TryGetValue((tab, page.PageNumber), out var boundsList)
            ? boundsList
            : [];
        var hasSelectedText = !string.IsNullOrWhiteSpace(_selectedText);
        var bounds = selectedBounds.Count > 0
            ? selectedBounds[0]
            : new PdfTextBounds(
                tab.MouseX,
                tab.MouseY + DefaultCommentHalfHeightPt,
                tab.MouseX + DefaultCommentWidthPt,
                tab.MouseY - DefaultCommentHalfHeightPt);
        var comment = new CommentAnnotation
        {
            PageNumber = page.PageNumber,
            Bounds = bounds,
            Text = hasSelectedText ? _selectedText : string.Empty,
            Author = Environment.UserName,
            IsOpen = true
        };
        tab.Document.AddAnnotation(comment);
        RebuildAnnotationPanel();
        _ = RenderActiveTabAsync();
    }

    private void AddFreehandAnnotation(TabViewModel tab, PdfPage page, IReadOnlyList<AnnotationPoint> points)
    {
        if (points.Count < 2)
        {
            return;
        }

        var left = points.Min(point => point.X);
        var right = points.Max(point => point.X);
        var top = points.Max(point => point.Y);
        var bottom = points.Min(point => point.Y);
        var stroke = points.ToArray();
        tab.Document.AddAnnotation(new FreehandAnnotation
        {
            PageNumber = page.PageNumber,
            Bounds = new PdfTextBounds(left, top, right, bottom),
            StrokeColorHex = _activeStrokeColorHex,
            StrokeWidth = _activeStrokeWidth,
            Strokes =
            [
                stroke
            ]
        });
    }

    private void AddShapeAnnotation(TabViewModel tab, PdfPage page, Point startPdf, Point endPdf)
    {
        var bounds = CreateBoundsFromDrag(startPdf, endPdf);
        tab.Document.AddAnnotation(new ShapeAnnotation
        {
            PageNumber = page.PageNumber,
            Bounds = bounds,
            Type = _activeShapeType,
            StrokeColorHex = _activeStrokeColorHex,
            FillColorHex = _activeFillColorHex,
            StrokeWidth = _activeStrokeWidth
        });
    }

    private void AddPresetStamp(TabViewModel tab, PdfPage page, string stampText)
    {
        var stampWidth = Math.Min(90d, page.WidthPt);
        var stampHeight = Math.Min(28d, page.HeightPt);
        var left = Math.Clamp(tab.MouseX, 0, Math.Max(0d, page.WidthPt - stampWidth));
        var top = Math.Clamp(tab.MouseY + 20d, stampHeight, page.HeightPt);
        var right = left + stampWidth;
        var bottom = top - stampHeight;
        tab.Document.AddAnnotation(new CommentAnnotation
        {
            PageNumber = page.PageNumber,
            Bounds = new PdfTextBounds(left, top, right, bottom),
            Text = $"{StampPrefix}{stampText}",
            Author = Environment.UserName,
            IsOpen = false
        });
    }

    private void SetEmptyStateVisible(bool visible)
    {
        EmptyStateTextBlock.IsVisible = visible;
        SinglePageScrollViewer.IsVisible = !visible && !(_activeTab?.IsContinuousMode ?? false) && !_mainWindowViewModel.IsSplitView;
        ContinuousScrollViewer.IsVisible = !visible && (_activeTab?.IsContinuousMode ?? false);
        SplitViewGrid.IsVisible = !visible && _mainWindowViewModel.IsSplitView;
        ThumbnailScrollViewer.IsVisible = !visible;
    }

    private void ClearPageViews()
    {
        PageControl.SetBitmap(null);
        AnnotationOverlayCanvas.Children.Clear();
        _activeFreehandPreview = null;
        PrimarySplitPageControl.SetBitmap(null);
        SecondarySplitPageControl.SetBitmap(null);
        ClearContinuousPageItems();
        FloatingAnnotationToolbar.IsVisible = false;
    }

    private async Task ShowOpenFileErrorDialogAsync(string filePath, Exception ex)
    {
        var title = "PDFを開けませんでした";
        var reason = ex switch
        {
            DllNotFoundException => "PDFレンダリングに必要なネイティブライブラリが見つかりません。インストーラーに同梱漏れがないか確認してください。",
            BadImageFormatException => "PDFレンダリング用ライブラリのアーキテクチャが実行環境と一致していません。",
            EntryPointNotFoundException => "PDFレンダリング用ライブラリのバージョン不整合が検出されました。",
            FileNotFoundException => "指定されたPDFファイルが見つかりません。",
            InvalidOperationException => ex.Message,
            _ => "PDFの読み込み中に予期しないエラーが発生しました。"
        };

        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = reason,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"対象ファイル: {filePath}",
            Foreground = new SolidColorBrush(Colors.Gainsboro),
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"詳細: {ex.GetBaseException().Message}",
            Foreground = new SolidColorBrush(Colors.Gainsboro),
            TextWrapping = TextWrapping.Wrap
        });

        var ok = new Button { Content = "OK", Width = 88, HorizontalAlignment = HorizontalAlignment.Right };
        var dialog = CreateStyledDialog(title, DialogSeverity.Error, 640, 280);
        ok.Click += (_, _) => dialog.Close();
        panel.Children.Add(ok);
        dialog.Content = panel;
        await dialog.ShowDialog(this).ConfigureAwait(true);
    }

    private async Task OpenFileFromPickerAsync()
    {
        var pickerResult = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("PDF")
                    {
                        Patterns = ["*.pdf"]
                    }
                ]
            }).ConfigureAwait(true);

        var filePath = pickerResult.FirstOrDefault()?.Path.LocalPath;
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            _ = await OpenFileAsync(filePath).ConfigureAwait(true);
        }
    }

    private async Task<bool> CloseTabAsync(TabViewModel tab)
    {
        if (tab.Document.IsModified)
        {
            var saveDecision = await ShowUnsavedAnnotationDialogAsync().ConfigureAwait(true);
            if (saveDecision == AnnotationSaveDecision.Cancel)
            {
                return false;
            }

            if (saveDecision == AnnotationSaveDecision.Save)
            {
                await _annotationService.SaveAnnotationsAsync(tab.Document).ConfigureAwait(true);
            }
        }

        CloseTabCore(tab);
        return true;
    }

    private void CloseTabCore(TabViewModel tab)
    {
        if (!_tabs.Remove(tab))
        {
            return;
        }
        _mainWindowViewModel.RemoveTab(tab);

        tab.ThumbnailUpdated -= OnThumbnailUpdated;
        tab.CancelThumbnailGeneration();
        _bookmarkMap.Remove(tab);
        _attachmentMap.Remove(tab);
        _securityMap.Remove(tab);
        StopTrackingFileChanges(tab);
        foreach (var key in _selectionHighlightMap.Keys.Where(key => ReferenceEquals(key.Tab, tab)).ToArray())
        {
            _selectionHighlightMap.Remove(key);
        }
        foreach (var key in _selectionPdfBoundsMap.Keys.Where(key => ReferenceEquals(key.Tab, tab)).ToArray())
        {
            _selectionPdfBoundsMap.Remove(key);
        }
        foreach (var key in _formFieldMap.Keys.Where(key => ReferenceEquals(key.Tab, tab)).ToArray())
        {
            _formFieldMap.Remove(key);
        }
        if (ReferenceEquals(_splitSecondaryTab, tab))
        {
            _splitSecondaryTab = null;
            _mainWindowViewModel.SplitSecondaryTab = null;
        }
        _pdfRenderService.Close(tab.Document);
        tab.Dispose();

        if (_tabs.Count == 0)
        {
            _activeTab = null;
            _mainWindowViewModel.ActiveTab = null;
            _mainWindowViewModel.IsSplitView = false;
            CleanupSplitDetachedTabs();
            RebuildTabBar();
            RebuildSplitTabSelectors();
            RebuildThumbnailPanel();
            RebuildAttachmentPanel();
            RebuildAnnotationPanel();
            CancelRender();
            ClearPageViews();
            SetEmptyStateVisible(true);
            UpdateToolbarState();
            UpdateStatusBar();
            _activeSecurityInfo = PdfSecurityInfo.FullAccess;
            return;
        }

        RebuildSplitTabSelectors();
        ActivateTab(_tabs[Math.Max(0, _tabs.Count - 1)]);
    }

    private async Task CloseOtherTabsAsync(TabViewModel baseTab)
    {
        foreach (var tab in _tabs.Where(tab => !ReferenceEquals(tab, baseTab)).ToArray())
        {
            await CloseTabAsync(tab).ConfigureAwait(true);
        }

        ActivateTab(baseTab);
    }

    private async Task CloseTabsToRightAsync(TabViewModel baseTab)
    {
        var index = _tabs.IndexOf(baseTab);
        if (index < 0)
        {
            return;
        }

        foreach (var tab in _tabs.Skip(index + 1).ToArray())
        {
            await CloseTabAsync(tab).ConfigureAwait(true);
        }

        ActivateTab(baseTab);
    }

    private void OpenInExplorer(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = directory,
            UseShellExecute = true
        });
    }

    private void CancelRender()
    {
        _renderCts?.Cancel();
        _renderCts?.Dispose();
        _renderCts = null;
    }

    private void CancelSearch()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;
    }

    private void UpdateMouseCoordinates(TabViewModel tab, PdfPage page, Point position, double zoom)
    {
        var x = (position.X * PdfDpi) / (ScreenDpi * Math.Max(zoom, 0.01d));
        var y = page.HeightPt - ((position.Y * PdfDpi) / (ScreenDpi * Math.Max(zoom, 0.01d)));
        tab.MouseX = Math.Clamp(x, 0, page.WidthPt);
        tab.MouseY = Math.Clamp(y, 0, page.HeightPt);
        UpdateStatusBar();
    }

    private static PdfTextBounds CreateBoundsFromDrag(Point startPdfPoint, Point currentPdfPoint)
    {
        var left = Math.Min(startPdfPoint.X, currentPdfPoint.X);
        var right = Math.Max(startPdfPoint.X, currentPdfPoint.X);
        var top = Math.Max(startPdfPoint.Y, currentPdfPoint.Y);
        var bottom = Math.Min(startPdfPoint.Y, currentPdfPoint.Y);
        return new PdfTextBounds(left, top, right, bottom);
    }

    private async Task UpdateTextSelectionAsync(TabViewModel tab, PdfPage page, Point currentPdfPoint)
    {
        var bounds = CreateBoundsFromDrag(_selectionStartPdfPoint, currentPdfPoint);
        var selection = await _searchService.SelectTextAsync(page, bounds).ConfigureAwait(true);
        _selectedText = selection.Text.Trim();
        _selectionPdfBoundsMap[(tab, page.PageNumber)] = selection.Bounds;
        _selectionHighlightMap[(tab, page.PageNumber)] = selection.Bounds
            .Select(item => ConvertBoundsToPixelRect(item, page, tab.ZoomLevel))
            .ToArray();
        _ = RenderActiveTabAsync();
    }

    private async Task CopySelectedTextAsync()
    {
        if (!_activeSecurityInfo.CanCopy)
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null || string.IsNullOrWhiteSpace(_selectedText))
        {
            return;
        }

        await clipboard.SetTextAsync(_selectedText).ConfigureAwait(true);
    }

    private void OnThumbnailUpdated(int _)
    {
        if (_activeTab is not null)
        {
            Dispatcher.UIThread.Post(RebuildThumbnailPanel);
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files))
        {
            e.DragEffects = DragDropEffects.Copy;
            DragDropOverlay.IsVisible = true;
        }
    }

    private void OnDragLeave(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DragDropOverlay.IsVisible = false;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        DragDropOverlay.IsVisible = false;
        if (!e.Data.Contains(DataFormats.Files))
        {
            return;
        }

        var filePath = e.Data.GetFiles()
            ?.OfType<IStorageFile>()
            .FirstOrDefault()
            ?.Path.LocalPath;
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            OpenFileWithoutAwait(filePath);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        SaveSessionSettings();
        CancelRender();
        CancelSearch();
        _zoomDebounceCts?.Cancel();
        _zoomDebounceCts?.Dispose();
        _zoomDebounceCts = null;
        _continuousScrollDebounceCts?.Cancel();
        _continuousScrollDebounceCts?.Dispose();
        _continuousScrollDebounceCts = null;
        foreach (var watcher in _watchers.Values.ToArray())
        {
            watcher.Dispose();
        }
        _watchers.Clear();
        foreach (var detached in _splitDetachedTabs.ToArray())
        {
            detached.Dispose();
            _splitDetachedTabs.Remove(detached);
        }

        foreach (var tab in _tabs.ToArray())
        {
            if (tab.Document.IsModified)
            {
                _annotationService.SaveAnnotationsAsync(tab.Document).GetAwaiter().GetResult();
            }

            CloseTabCore(tab);
        }

        _pdfRenderService.Dispose();
    }

    private void OnSinglePagePointerMoved(object? sender, PointerEventArgs e)
    {
        var tab = _activeTab;
        if (tab is null || tab.IsContinuousMode)
        {
            return;
        }

        var page = tab.Document.Pages[tab.CurrentPage - 1];
        var point = e.GetPosition(PageControl);
        UpdateMouseCoordinates(tab, page, point, tab.ZoomLevel);

        if (_isSelectingText && ReferenceEquals(tab, _selectionTab) && ReferenceEquals(page, _selectionPage))
        {
            var currentPdfPoint = new Point(tab.MouseX, tab.MouseY);
            _ = UpdateTextSelectionAsync(tab, page, currentPdfPoint);
        }
        else if (_activeAnnotationTool == AnnotationTool.Freehand && _currentFreehandStroke.Count > 0)
        {
            _currentFreehandStroke.Add(new AnnotationPoint(tab.MouseX, tab.MouseY));
            UpdateFreehandPreview(tab, page);
        }
    }

    private void InitializeFreehandPreview(TabViewModel tab, PdfPage page)
    {
        RemoveFreehandPreview();
        _activeFreehandPreview = new Avalonia.Controls.Shapes.Polyline
        {
            Stroke = new SolidColorBrush(Color.Parse(_activeStrokeColorHex)),
            StrokeThickness = Math.Max(1d, _activeStrokeWidth),
            IsHitTestVisible = false
        };
        AnnotationOverlayCanvas.Children.Add(_activeFreehandPreview);
        UpdateFreehandPreview(tab, page);
    }

    private void UpdateFreehandPreview(TabViewModel tab, PdfPage page)
    {
        if (_activeFreehandPreview is null || _currentFreehandStroke.Count == 0)
        {
            return;
        }

        var scale = (ScreenDpi * Math.Max(0.01d, tab.ZoomLevel)) / PdfDpi;
        var points = _currentFreehandStroke
            .Select(point => _annotationService.ConvertPdfToScreen(point.X, point.Y, scale, page.HeightPt))
            .Select(point => new Avalonia.Point(point.X, point.Y))
            .ToArray();
        _activeFreehandPreview.Points = points;
        AnnotationOverlayCanvas.InvalidateVisual();
    }

    private void RemoveFreehandPreview()
    {
        if (_activeFreehandPreview is null)
        {
            return;
        }

        AnnotationOverlayCanvas.Children.Remove(_activeFreehandPreview);
        _activeFreehandPreview = null;
    }

    private void OnContinuousPagePointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not PdfPageControl control || !_continuousPageMap.TryGetValue(control, out var info))
        {
            return;
        }

        var point = e.GetPosition(control);
        UpdateMouseCoordinates(info.Tab, info.Page, point, info.Tab.ZoomLevel);

        if (_isSelectingText && ReferenceEquals(info.Tab, _selectionTab) && ReferenceEquals(info.Page, _selectionPage))
        {
            var currentPdfPoint = new Point(info.Tab.MouseX, info.Tab.MouseY);
            _ = UpdateTextSelectionAsync(info.Tab, info.Page, currentPdfPoint);
        }
    }

    private void OnContinuousPagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not PdfPageControl control || !_continuousPageMap.TryGetValue(control, out var info))
        {
            return;
        }

        info.Tab.JumpToPage(info.Page.PageNumber);
        RebuildThumbnailPanel();
        UpdateToolbarState();
        UpdateStatusBar();

        if (e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            if (_activeAnnotationTool != AnnotationTool.TextSelect)
            {
                return;
            }

            var point = e.GetPosition(control);
            UpdateMouseCoordinates(info.Tab, info.Page, point, info.Tab.ZoomLevel);
            _isSelectingText = true;
            _selectionTab = info.Tab;
            _selectionPage = info.Page;
            _selectionStartPdfPoint = new Point(info.Tab.MouseX, info.Tab.MouseY);
            _selectedText = string.Empty;
            FloatingAnnotationToolbar.IsVisible = false;
            _selectionHighlightMap.Remove((info.Tab, info.Page.PageNumber));
            _selectionPdfBoundsMap.Remove((info.Tab, info.Page.PageNumber));
        }
    }

    private void OnContinuousPagePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isSelectingText)
        {
            return;
        }

        _isSelectingText = false;
    }

    private void OnSinglePagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var tab = _activeTab;
        if (tab is null || tab.IsContinuousMode || !e.GetCurrentPoint(PageControl).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var page = tab.Document.Pages[tab.CurrentPage - 1];
        var point = e.GetPosition(PageControl);
        UpdateMouseCoordinates(tab, page, point, tab.ZoomLevel);
        if (_activeAnnotationTool == AnnotationTool.Comment)
        {
            _selectionPage = page;
            _selectedText = string.Empty;
            AddCommentFromSelection();
            RebuildAnnotationPanel();
            return;
        }

        if (_activeAnnotationTool == AnnotationTool.Stamp)
        {
            AddPresetStamp(tab, page, DefaultStampText);
            RebuildAnnotationPanel();
            _ = RenderActiveTabAsync();
            return;
        }

        if (_activeAnnotationTool == AnnotationTool.Freehand)
        {
            _selectionPage = page;
            _currentFreehandStroke.Clear();
            _currentFreehandStroke.Add(new AnnotationPoint(tab.MouseX, tab.MouseY));
            InitializeFreehandPreview(tab, page);
            return;
        }

        if (_activeAnnotationTool == AnnotationTool.Shape)
        {
            _selectionPage = page;
            _shapeStartPdfPoint = new Point(tab.MouseX, tab.MouseY);
            return;
        }

        _isSelectingText = true;
        _selectionTab = tab;
        _selectionPage = page;
        _selectionStartPdfPoint = new Point(tab.MouseX, tab.MouseY);
        _selectedText = string.Empty;
        FloatingAnnotationToolbar.IsVisible = false;
        _selectionHighlightMap.Remove((tab, page.PageNumber));
        _selectionPdfBoundsMap.Remove((tab, page.PageNumber));
    }

    private void OnSinglePagePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var tab = _activeTab;
        if (tab is not null && _selectionPage is not null)
        {
            if (_activeAnnotationTool == AnnotationTool.Freehand && _currentFreehandStroke.Count > 1)
            {
                AddFreehandAnnotation(tab, _selectionPage, _currentFreehandStroke);
                _currentFreehandStroke.Clear();
                RemoveFreehandPreview();
                RebuildAnnotationPanel();
                _ = RenderActiveTabAsync();
                return;
            }

            if (_activeAnnotationTool == AnnotationTool.Shape && _shapeStartPdfPoint is Point startPoint)
            {
                var endPoint = new Point(tab.MouseX, tab.MouseY);
                AddShapeAnnotation(tab, _selectionPage, startPoint, endPoint);
                _shapeStartPdfPoint = null;
                RebuildAnnotationPanel();
                _ = RenderActiveTabAsync();
                return;
            }
        }

        if (_activeAnnotationTool == AnnotationTool.Freehand)
        {
            _currentFreehandStroke.Clear();
            RemoveFreehandPreview();
        }

        if (!_isSelectingText)
        {
            return;
        }

        _isSelectingText = false;
        FloatingAnnotationToolbar.IsVisible = !string.IsNullOrWhiteSpace(_selectedText);
    }

    private async void OnOpenFileClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await OpenFileFromPickerAsync().ConfigureAwait(true);
    }

    private async void OnAddTabClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await OpenFileFromPickerAsync().ConfigureAwait(true);
    }

    private async void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_activeTab is null)
        {
            return;
        }

        _activeTab.SearchQuery = SearchTextBox.Text ?? string.Empty;
        await ExecuteSearchAsync().ConfigureAwait(true);
    }

    private async void OnSearchOptionChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_activeTab is null)
        {
            return;
        }

        _activeTab.IsSearchCaseSensitive = CaseSensitiveSearchCheckBox.IsChecked == true;
        _activeTab.IsSearchRegex = RegexSearchCheckBox.IsChecked == true;
        await ExecuteSearchAsync().ConfigureAwait(true);
    }

    private void OnSearchPreviousClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        MoveToNextSearchResult(reverse: true);
    }

    private void OnSearchNextClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        MoveToNextSearchResult(reverse: false);
    }

    private void OnSearchCloseClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_activeTab is null)
        {
            return;
        }

        _activeTab.IsSearchVisible = false;
        UpdateSearchOverlayState();
    }

    private void OnHighlightToolClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SetAnnotationTool(HighlightToolButton.IsChecked == true ? AnnotationTool.Highlight : AnnotationTool.TextSelect);
    }

    private void OnCommentToolClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SetAnnotationTool(CommentToolButton.IsChecked == true ? AnnotationTool.Comment : AnnotationTool.TextSelect);
    }

    private void OnFreehandToolClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SetAnnotationTool(FreehandToolButton.IsChecked == true ? AnnotationTool.Freehand : AnnotationTool.TextSelect);
    }

    private void OnShapeToolClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SetAnnotationTool(ShapeToolButton.IsChecked == true ? AnnotationTool.Shape : AnnotationTool.TextSelect);
    }

    private void OnStampToolClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SetAnnotationTool(StampToolButton.IsChecked == true ? AnnotationTool.Stamp : AnnotationTool.TextSelect);
    }

    private void SetAnnotationTool(AnnotationTool tool)
    {
        _activeAnnotationTool = tool;
        UpdateToolbarState();
    }

    private void OnShapeTypeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ShapeTypeComboBox is null)
        {
            return;
        }

        _activeShapeType = ShapeTypeComboBox.SelectedIndex switch
        {
            1 => ShapeType.Ellipse,
            2 => ShapeType.Arrow,
            3 => ShapeType.Line,
            _ => ShapeType.Rectangle
        };
    }

    private void OnStrokeStyleChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (StrokeColorComboBox is null || FillColorComboBox is null || StrokeWidthComboBox is null)
        {
            return;
        }

        _activeStrokeColorHex = StrokeColorComboBox.SelectedIndex switch
        {
            1 => "#00c878",
            2 => "#50a0ff",
            3 => "#ffd000",
            4 => "#111111",
            _ => "#ff0000"
        };
        _activeFillColorHex = FillColorComboBox.SelectedIndex switch
        {
            1 => "#60ff0000",
            2 => "#6000c878",
            3 => "#6050a0ff",
            4 => "#60ffd000",
            _ => null
        };
        _activeStrokeWidth = StrokeWidthComboBox.SelectedIndex switch
        {
            0 => 1d,
            2 => 3d,
            3 => 4d,
            4 => 6d,
            _ => 2d
        };
    }

    private void OnFloatingHighlightClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => AddHighlightFromSelection(HighlightType.Highlight, HighlightColor.Yellow);

    private void OnFloatingUnderlineClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => AddHighlightFromSelection(HighlightType.Underline, HighlightColor.Yellow);

    private void OnFloatingStrikeClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => AddHighlightFromSelection(HighlightType.Strikethrough, HighlightColor.Pink);

    private void OnFloatingCommentClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => AddCommentFromSelection();

    private void OnFloatingFreehandClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => SetAnnotationTool(AnnotationTool.Freehand);

    private void OnFloatingRectangleClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _activeShapeType = ShapeType.Rectangle;
        ShapeTypeComboBox.SelectedIndex = 0;
        SetAnnotationTool(AnnotationTool.Shape);
    }

    private void OnFloatingEllipseClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _activeShapeType = ShapeType.Ellipse;
        ShapeTypeComboBox.SelectedIndex = 1;
        SetAnnotationTool(AnnotationTool.Shape);
    }

    private void OnFloatingArrowClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _activeShapeType = ShapeType.Arrow;
        ShapeTypeComboBox.SelectedIndex = 2;
        SetAnnotationTool(AnnotationTool.Shape);
    }

    private void OnFloatingStampClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => SetAnnotationTool(AnnotationTool.Stamp);

    private void OnAnnotationPanelSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_activeTab is null || AnnotationPanelListBox.SelectedItem is not AnnotationPanelItem item)
        {
            UpdateToolbarState();
            return;
        }

        _activeTab.JumpToPage(item.PageNumber);
        RebuildThumbnailPanel();
        UpdateToolbarState();
        _ = RenderActiveTabAsync();
    }

    private void OnAttachmentSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        ExtractAttachmentButton.IsEnabled = AttachmentListBox.SelectedItem is AttachmentPanelItem;
    }

    private async void OnExtractAttachmentClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_activeTab is null || AttachmentListBox.SelectedItem is not AttachmentPanelItem item)
        {
            return;
        }

        var output = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = item.File.Name,
            FileTypeChoices =
            [
                new FilePickerFileType("All files")
                {
                    Patterns = ["*"]
                }
            ]
        }).ConfigureAwait(true);
        if (output is null)
        {
            return;
        }

        await _pdfRenderService.ExtractEmbeddedFileAsync(_activeTab.Document, item.File, output.Path.LocalPath).ConfigureAwait(true);
    }

    private void OnDeleteAnnotationClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_activeSecurityInfo.CanAnnotate)
        {
            return;
        }

        if (_activeTab is null || AnnotationPanelListBox.SelectedItem is not AnnotationPanelItem item)
        {
            return;
        }

        _activeTab.Document.RemoveAnnotation(item.AnnotationId);
        RebuildAnnotationPanel();
        _ = RenderActiveTabAsync();
    }

    private async void OnExportFdfClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_activeTab is null)
        {
            return;
        }

        var target = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = $"{Path.GetFileNameWithoutExtension(_activeTab.Document.FileName)}.fdf",
            FileTypeChoices =
            [
                new FilePickerFileType("FDF")
                {
                    Patterns = ["*.fdf"]
                }
            ]
        }).ConfigureAwait(true);
        if (target is null)
        {
            return;
        }

        await _annotationService.ExportAsFdfAsync(_activeTab.Document, target.Path.LocalPath).ConfigureAwait(true);
    }

    private async void OnImportFdfClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_activeTab is null)
        {
            return;
        }

        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("FDF")
                {
                    Patterns = ["*.fdf"]
                }
            ]
        }).ConfigureAwait(true);
        var path = picked.FirstOrDefault()?.Path.LocalPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await _annotationService.ImportFdfAsync(_activeTab.Document, path).ConfigureAwait(true);
        RebuildAnnotationPanel();
        _ = RenderActiveTabAsync();
    }

    private void OnFirstPageClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var targetTab = GetCommandTargetTab();
        if (targetTab is null)
        {
            return;
        }

        targetTab.MoveFirstPageCommand.Execute(null);
        RebuildThumbnailPanel();
        _ = RenderActiveTabAsync();
    }

    private void OnPreviousPageClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var targetTab = GetCommandTargetTab();
        if (targetTab is null)
        {
            return;
        }

        targetTab.MovePreviousPageCommand.Execute(null);
        RebuildThumbnailPanel();
        _ = RenderActiveTabAsync();
    }

    private void OnNextPageClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var targetTab = GetCommandTargetTab();
        if (targetTab is null)
        {
            return;
        }

        targetTab.MoveNextPageCommand.Execute(null);
        RebuildThumbnailPanel();
        _ = RenderActiveTabAsync();
    }

    private void OnLastPageClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var targetTab = GetCommandTargetTab();
        if (targetTab is null)
        {
            return;
        }

        targetTab.MoveLastPageCommand.Execute(null);
        RebuildThumbnailPanel();
        _ = RenderActiveTabAsync();
    }

    private void OnPageNumberTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        var targetTab = GetCommandTargetTab();
        if (e.Key != Key.Enter || targetTab is null)
        {
            return;
        }

        targetTab.PageInputText = PageNumberTextBox.Text ?? string.Empty;
        targetTab.JumpFromInput();
        RebuildThumbnailPanel();
        _ = RenderActiveTabAsync();
        e.Handled = true;
    }

    private void OnZoomOutClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var targetTab = GetCommandTargetTab();
        if (targetTab is null)
        {
            return;
        }

        targetTab.ZoomOutCommand.Execute(null);
        _ = RenderActiveTabAsync();
    }

    private void OnZoomInClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var targetTab = GetCommandTargetTab();
        if (targetTab is null)
        {
            return;
        }

        targetTab.ZoomInCommand.Execute(null);
        _ = RenderActiveTabAsync();
    }

    private void OnZoomComboBoxSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var targetTab = GetCommandTargetTab();
        if (targetTab is null || ZoomComboBox.SelectedItem is not ComboBoxItem selected)
        {
            return;
        }

        var option = selected.Content?.ToString() ?? string.Empty;
        if (option.EndsWith('%') && double.TryParse(option.TrimEnd('%'), out var percent))
        {
            targetTab.ZoomLevel = PdfiumRenderService.ClampZoomLevel(percent / 100d);
        }
        else if (string.Equals(option, "幅に合わせる", StringComparison.Ordinal))
        {
            targetTab.FitToWidth(SinglePageScrollViewer.Viewport.Width);
        }
        else if (string.Equals(option, "ページに合わせる", StringComparison.Ordinal))
        {
            targetTab.FitToPage(SinglePageScrollViewer.Viewport.Width, SinglePageScrollViewer.Viewport.Height);
        }
        else if (string.Equals(option, "実寸", StringComparison.Ordinal))
        {
            targetTab.ZoomActualSizeCommand.Execute(null);
        }
        else
        {
            return;
        }

        _ = RenderActiveTabAsync();
    }

    private void OnSinglePageModeClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var targetTab = GetCommandTargetTab();
        if (targetTab is null)
        {
            return;
        }

        targetTab.SetSinglePageModeCommand.Execute(null);
        _ = RenderActiveTabAsync();
    }

    private void OnContinuousModeClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var targetTab = GetCommandTargetTab();
        if (targetTab is null)
        {
            return;
        }

        targetTab.SetContinuousModeCommand.Execute(null);
        _ = RenderActiveTabAsync();
    }

    private void OnTwoPageModeClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var targetTab = GetCommandTargetTab();
        if (targetTab is null)
        {
            return;
        }

        targetTab.SetTwoPageModeCommand.Execute(null);
        _ = RenderActiveTabAsync();
    }

    private void OnRotateClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var targetTab = GetCommandTargetTab();
        if (targetTab is null)
        {
            return;
        }

        targetTab.RotateClockwiseCommand.Execute(null);
        _ = RenderActiveTabAsync();
    }

    private async void OnPrintClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_activeSecurityInfo.CanPrint)
        {
            return;
        }

        await ShowPrintDialogAsync().ConfigureAwait(true);
    }

    private void OnSplitViewModeClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_activeTab is null)
        {
            return;
        }

        _mainWindowViewModel.IsSplitView = !_mainWindowViewModel.IsSplitView;
        if (_mainWindowViewModel.IsSplitView)
        {
            EnsureSplitSecondaryTab();
        }
        else
        {
            CleanupSplitDetachedTabs();
        }

        RebuildSplitTabSelectors();
        UpdateToolbarState();
        _ = RenderActiveTabAsync();
    }

    private void OnRecentFilesClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        RebuildRecentFilesMenu();
        if (RecentFilesButton.ContextMenu is { } menu)
        {
            menu.PlacementTarget = RecentFilesButton;
            menu.Open();
        }
    }

    private void OnPrimarySplitTabSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isRebuildingSplitSelectors)
        {
            return;
        }

        if (PrimarySplitTabComboBox.SelectedIndex < 0 || PrimarySplitTabComboBox.SelectedIndex >= _tabs.Count)
        {
            return;
        }

        ActivateTab(_tabs[PrimarySplitTabComboBox.SelectedIndex]);
    }

    private void OnSecondarySplitTabSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isRebuildingSplitSelectors)
        {
            return;
        }

        if (SecondarySplitTabComboBox.SelectedIndex < 0 || SecondarySplitTabComboBox.SelectedIndex >= _tabs.Count)
        {
            return;
        }

        var selectedTab = _tabs[SecondarySplitTabComboBox.SelectedIndex];
        if (ReferenceEquals(selectedTab, _activeTab))
        {
            CleanupSplitDetachedTabs();
            EnsureSplitSecondaryTab();
        }
        else
        {
            CleanupSplitDetachedTabs();
            _splitSecondaryTab = selectedTab;
        }

        _mainWindowViewModel.SplitSecondaryTab = _splitSecondaryTab;
        _isSplitRightPaneActive = true;
        _ = RenderActiveTabAsync();
    }

    private void OnPrimarySplitPagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _isSplitRightPaneActive = false;
    }

    private void OnSecondarySplitPagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _isSplitRightPaneActive = true;
    }

    private void OnSplitDividerPointerEntered(object? sender, PointerEventArgs e)
    {
        SplitDivider.Background = new SolidColorBrush((Color)Application.Current!.FindResource("Accent")!);
    }

    private void OnSplitDividerPointerExited(object? sender, PointerEventArgs e)
    {
        if (!_isSplitDividerDragging)
        {
            SplitDivider.Background = new SolidColorBrush((Color)Application.Current!.FindResource("BorderLight")!);
        }
    }

    private void OnSplitDividerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_mainWindowViewModel.IsSplitView)
        {
            _isSplitDividerDragging = true;
            e.Pointer.Capture(SplitDivider);
        }
    }

    private void OnSplitDividerPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isSplitDividerDragging || !_mainWindowViewModel.IsSplitView)
        {
            return;
        }

        var point = e.GetPosition(SplitViewGrid);
        var totalWidth = Math.Max(1d, SplitViewGrid.Bounds.Width - 3d);
        var leftWidth = Math.Clamp(point.X, SplitPaneMinWidthPx, totalWidth - SplitPaneMinWidthPx);
        SplitViewGrid.ColumnDefinitions[0].Width = new GridLength(leftWidth, GridUnitType.Pixel);
        SplitViewGrid.ColumnDefinitions[2].Width = new GridLength(totalWidth - leftWidth, GridUnitType.Pixel);
    }

    private void OnSplitDividerPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isSplitDividerDragging = false;
        e.Pointer.Capture(null);
        SplitDivider.Background = new SolidColorBrush((Color)Application.Current!.FindResource("BorderLight")!);
    }

    private void OnWindowPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var targetTab = GetCommandTargetTab();
        if (targetTab is null)
        {
            return;
        }

        if ((e.KeyModifiers & KeyModifiers.Control) == 0)
        {
            return;
        }

        targetTab.ZoomLevel = PdfiumRenderService.ClampZoomLevel(targetTab.ZoomLevel + (e.Delta.Y > 0 ? 0.1d : -0.1d));
        _ = ScheduleZoomRenderAsync();
        e.Handled = true;
    }

    private async Task ScheduleZoomRenderAsync()
    {
        _zoomDebounceCts?.Cancel();
        _zoomDebounceCts?.Dispose();
        var debounceCts = new CancellationTokenSource();
        _zoomDebounceCts = debounceCts;
        var token = debounceCts.Token;
        try
        {
            await Task.Delay(ZoomDebounceDelay, token).ConfigureAwait(true);
            if (!token.IsCancellationRequested)
            {
                await RenderActiveTabAsync(token).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            // ホイール連続入力時のキャンセルは正常系。
        }
        finally
        {
            if (ReferenceEquals(_zoomDebounceCts, debounceCts))
            {
                _zoomDebounceCts = null;
            }

            debounceCts.Dispose();
        }
    }

    private async void OnContinuousScrollViewerScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        var tab = _activeTab;
        if (tab is null || !tab.IsContinuousMode)
        {
            return;
        }

        _continuousScrollDebounceCts?.Cancel();
        _continuousScrollDebounceCts?.Dispose();
        var debounceCts = new CancellationTokenSource();
        _continuousScrollDebounceCts = debounceCts;
        try
        {
            await Task.Delay(ContinuousScrollDebounceDelay, debounceCts.Token).ConfigureAwait(true);
            await RenderVisibleContinuousPagesAsync(tab, _renderCts?.Token ?? CancellationToken.None).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // 画面更新途中のキャンセルは正常系。
        }
        finally
        {
            if (ReferenceEquals(_continuousScrollDebounceCts, debounceCts))
            {
                _continuousScrollDebounceCts = null;
            }

            debounceCts.Dispose();
        }
    }

    private async void OnContinuousViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        var tab = _activeTab;
        if (tab is null || !tab.IsContinuousMode || _renderCts is null)
        {
            return;
        }

        try
        {
            await RenderVisibleContinuousPagesAsync(tab, _renderCts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // 表示更新途中のキャンセルは正常系。
        }
    }

    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        var ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        var shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;

        if (e.Key == Key.F11)
        {
            WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.O)
        {
            await OpenFileFromPickerAsync().ConfigureAwait(true);
            e.Handled = true;
            return;
        }

        var targetTab = GetCommandTargetTab();
        if (targetTab is null)
        {
            return;
        }

        if (ctrl && e.Key == Key.W)
        {
            await CloseTabAsync(targetTab).ConfigureAwait(true);
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.S)
        {
            if (_activeSecurityInfo.CanAnnotate)
            {
                await _annotationService.SaveAnnotationsAsync(targetTab.Document).ConfigureAwait(true);
            }

            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.G)
        {
            PageNumberTextBox.Focus();
            PageNumberTextBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (ctrl && (e.Key == Key.OemPlus || e.Key == Key.Add))
        {
            targetTab.ZoomInCommand.Execute(null);
            _ = RenderActiveTabAsync();
            e.Handled = true;
            return;
        }

        if (ctrl && (e.Key == Key.OemMinus || e.Key == Key.Subtract))
        {
            targetTab.ZoomOutCommand.Execute(null);
            _ = RenderActiveTabAsync();
            e.Handled = true;
            return;
        }

        if (ctrl && (e.Key == Key.D0 || e.Key == Key.NumPad0))
        {
            targetTab.ZoomActualSizeCommand.Execute(null);
            _ = RenderActiveTabAsync();
            e.Handled = true;
            return;
        }

        if (ctrl && shift && e.Key == Key.H)
        {
            targetTab.FitToWidth(SinglePageScrollViewer.Bounds.Width);
            _ = RenderActiveTabAsync();
            e.Handled = true;
            return;
        }

        if (ctrl && shift && e.Key == Key.F)
        {
            targetTab.FitToPage(SinglePageScrollViewer.Bounds.Width, SinglePageScrollViewer.Bounds.Height);
            _ = RenderActiveTabAsync();
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.Tab)
        {
            SwitchTab(shift ? -1 : 1);
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.F)
        {
            if (_activeTab is null)
            {
                return;
            }

            _activeTab.IsSearchVisible = true;
            UpdateSearchOverlayState();
            SearchTextBox.Focus();
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.C)
        {
            _ = CopySelectedTextAsync();
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.P)
        {
            if (_activeSecurityInfo.CanPrint)
            {
                await ShowPrintDialogAsync().ConfigureAwait(true);
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.F3)
        {
            MoveToNextSearchResult(shift);
            e.Handled = true;
            return;
        }

        if (ctrl)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Left:
                targetTab.MovePreviousPageCommand.Execute(null);
                break;
            case Key.Right:
                targetTab.MoveNextPageCommand.Execute(null);
                break;
            case Key.Home:
                targetTab.MoveFirstPageCommand.Execute(null);
                break;
            case Key.End:
                targetTab.MoveLastPageCommand.Execute(null);
                break;
            case Key.Escape:
                if (_activeTab is null)
                {
                    return;
                }

                _activeTab.IsSearchVisible = false;
                SetAnnotationTool(AnnotationTool.TextSelect);
                UpdateSearchOverlayState();
                e.Handled = true;
                return;
            default:
                return;
        }

        RebuildThumbnailPanel();
        _ = RenderActiveTabAsync();
        e.Handled = true;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        await Task.Delay(SplashDisplayDuration).ConfigureAwait(true);
        if (!IsVisible)
        {
            return;
        }

        SplashOverlay.Opacity = 0d;
        await Task.Delay(SplashFadeDuration).ConfigureAwait(true);
        if (!IsVisible)
        {
            return;
        }

        SplashOverlay.IsVisible = false;

        if (_restoreAttempted)
        {
            return;
        }

        _restoreAttempted = true;
        var sessionEntries = _settingsService.LoadSession();
        if (_skipSessionRestore || !_settings.RestoreSessionOnStartup || sessionEntries.Count == 0)
        {
            return;
        }

        _ = RestoreLastSessionAsync(sessionEntries);
    }

    private async Task RestoreLastSessionAsync(IReadOnlyList<SessionEntry> sessionEntries)
    {
        foreach (var entry in sessionEntries.Where(entry => File.Exists(entry.FilePath)))
        {
            var opened = await OpenFileAsync(entry.FilePath, entry.PageNumber, activate: false).ConfigureAwait(true);
            if (opened is not null)
            {
                opened.JumpToPage(entry.PageNumber);
            }
        }

        if (_tabs.Count > 0)
        {
            ActivateTab(_tabs[0]);
        }
    }

    private void ApplySettingsToToolbar()
    {
        ZoomComboBox.SelectedIndex = 3;
        SettingsThemeComboBox.SelectedIndex = _settings.Theme switch
        {
            ThemePreference.Light => 1,
            ThemePreference.Dark => 2,
            _ => 0
        };

        SettingsLanguageComboBox.SelectedIndex = string.Equals(_settings.Language, "en", StringComparison.OrdinalIgnoreCase)
            ? 1
            : 0;
    }

    private void ApplyLocalizedText()
    {
        OpenFileButton.Content = AppStrings.Get("Open");
        RecentFilesButton.Content = AppStrings.Get("Recent");
        SettingsButton.Content = AppStrings.Get("Settings");
        ThemeLabelTextBlock.Text = AppStrings.Get("Theme");
        LanguageLabelTextBlock.Text = AppStrings.Get("Language");
        EmptyStateTextBlock.Text = AppStrings.Get("EmptyState");
        DragDropHintTextBlock.Text = AppStrings.Get("DropPrompt");
        LoadingTextBlock.Text = AppStrings.Get("Loading");
        SplashTitleTextBlock.Text = AppStrings.Get("SplashTitle");
    }

    private void OnThemeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized)
        {
            return;
        }

        var preference = SettingsThemeComboBox.SelectedIndex switch
        {
            1 => ThemePreference.Light,
            2 => ThemePreference.Dark,
            _ => ThemePreference.System
        };

        if (Application.Current is App app)
        {
            app.SetThemePreference(preference);
        }

        _settings = _settings with { Theme = preference };
    }

    private void OnLanguageSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized)
        {
            return;
        }

        var language = SettingsLanguageComboBox.SelectedIndex == 1 ? "en" : "ja";
        AppStrings.CurrentCulture = new CultureInfo(language);
        _settings = _settings with { Language = language };
        ApplyLocalizedText();
    }

    private void InitializeAnnotationTooling()
    {
        ShapeTypeComboBox.SelectedIndex = 0;
        StrokeColorComboBox.SelectedIndex = 0;
        FillColorComboBox.SelectedIndex = 0;
        StrokeWidthComboBox.SelectedIndex = 1;
        UpdateToolbarState();
    }

    private void RebuildRecentFilesMenu()
    {
        var items = new List<object>();
        foreach (var file in _settingsService.GetRecentFiles())
        {
            var item = new MenuItem { Header = file };
            item.Click += (_, _) => OpenFileWithoutAwait(file);
            items.Add(item);
        }

        if (items.Count == 0)
        {
            items.Add(new MenuItem { Header = "(履歴なし)", IsEnabled = false });
        }

        RecentFilesButton.ContextMenu = new ContextMenu { ItemsSource = items };
    }

    private void RebuildSplitTabSelectors()
    {
        _isRebuildingSplitSelectors = true;
        try
        {
            var names = _tabs.Select(tab => tab.Title).Cast<object>().ToArray();
            PrimarySplitTabComboBox.ItemsSource = names;
            SecondarySplitTabComboBox.ItemsSource = names;
            PrimarySplitTabComboBox.SelectedIndex = _activeTab is null ? -1 : _tabs.IndexOf(_activeTab);
            SecondarySplitTabComboBox.SelectedIndex = _splitSecondaryTab is null
                ? -1
                : Math.Max(
                    _tabs.IndexOf(_splitSecondaryTab),
                    _activeTab is null || !string.Equals(_splitSecondaryTab.Document.FilePath, _activeTab.Document.FilePath, StringComparison.OrdinalIgnoreCase)
                        ? -1
                        : _tabs.IndexOf(_activeTab));
        }
        finally
        {
            _isRebuildingSplitSelectors = false;
        }
    }

    private void SwitchTab(int offset)
    {
        if (_tabs.Count == 0 || _activeTab is null)
        {
            return;
        }

        var current = _tabs.IndexOf(_activeTab);
        if (current < 0)
        {
            return;
        }

        var next = (current + offset) % _tabs.Count;
        if (next < 0)
        {
            next += _tabs.Count;
        }

        ActivateTab(_tabs[next]);
    }

    private void EnsureSplitSecondaryTab()
    {
        if (_activeTab is null)
        {
            return;
        }

        if (_splitSecondaryTab is null)
        {
            var detached = new TabViewModel(_activeTab.Document)
            {
                CurrentPage = _activeTab.CurrentPage,
                ZoomLevel = _activeTab.ZoomLevel,
                IsContinuousMode = false,
                IsTwoPageMode = _activeTab.IsTwoPageMode,
                RotationDegrees = _activeTab.RotationDegrees
            };
            _splitDetachedTabs.Add(detached);
            _splitSecondaryTab = detached;
        }

        _mainWindowViewModel.SplitSecondaryTab = _splitSecondaryTab;
    }

    private void CleanupSplitDetachedTabs()
    {
        foreach (var detached in _splitDetachedTabs.ToArray())
        {
            detached.Dispose();
            _splitDetachedTabs.Remove(detached);
        }

        _splitSecondaryTab = null;
        _mainWindowViewModel.SplitSecondaryTab = null;
        _isSplitRightPaneActive = false;
    }

    private void TrackFileChanges(TabViewModel tab)
    {
        var filePath = tab.Document.FilePath;
        var directory = Path.GetDirectoryName(filePath);
        var fileName = Path.GetFileName(filePath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName) || !Directory.Exists(directory))
        {
            return;
        }

        StopTrackingFileChanges(tab);
        var watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };
        FileSystemEventHandler changed = (_, _) => Dispatcher.UIThread.Post(() => PromptReload(tab));
        RenamedEventHandler renamed = (_, _) => Dispatcher.UIThread.Post(() => PromptReload(tab));
        watcher.Changed += changed;
        watcher.Created += changed;
        watcher.Renamed += renamed;
        _watchers[tab] = new WatchedFile(watcher, changed, renamed);
    }

    private void StopTrackingFileChanges(TabViewModel tab)
    {
        if (_watchers.Remove(tab, out var watchedFile))
        {
            watchedFile.Dispose();
        }
    }

    private void PromptReload(TabViewModel tab)
    {
        if (!ReferenceEquals(tab, _activeTab) || !IsVisible)
        {
            return;
        }

        _ = PromptReloadAsync(tab);
    }

    private async Task PromptReloadAsync(TabViewModel tab)
    {
        var reload = await ShowReloadConfirmDialogAsync().ConfigureAwait(true);
        if (!reload || !File.Exists(tab.Document.FilePath))
        {
            return;
        }

        var currentPage = tab.CurrentPage;
        var filePath = tab.Document.FilePath;
        CloseTabCore(tab);
        _ = await OpenFileAsync(filePath, currentPage).ConfigureAwait(true);
    }

    private async Task<AnnotationSaveDecision> ShowUnsavedAnnotationDialogAsync()
    {
        var dialog = CreateStyledDialog("未保存の注釈", DialogSeverity.Warning, 360, 170);

        var result = AnnotationSaveDecision.Cancel;
        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = "未保存の注釈があります。保存しますか？",
            Foreground = new SolidColorBrush((Color)Application.Current!.FindResource("TextPrimary")!)
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        var cancelButton = new Button { Content = "キャンセル" };
        var discardButton = new Button { Content = "破棄" };
        var saveButton = new Button { Content = "保存" };
        cancelButton.Click += (_, _) => dialog.Close();
        discardButton.Click += (_, _) =>
        {
            result = AnnotationSaveDecision.Discard;
            dialog.Close();
        };
        saveButton.Click += (_, _) =>
        {
            result = AnnotationSaveDecision.Save;
            dialog.Close();
        };
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(discardButton);
        buttons.Children.Add(saveButton);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        await dialog.ShowDialog(this).ConfigureAwait(true);
        return result;
    }

    private async Task<bool> ShowReloadConfirmDialogAsync()
    {
        var dialog = CreateStyledDialog("ファイル変更検知", DialogSeverity.Information, 320, 150);

        var result = false;
        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = "外部変更を検知しました。再読み込みしますか？",
            Foreground = new SolidColorBrush((Color)Application.Current!.FindResource("TextPrimary")!)
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        var noButton = new Button { Content = "いいえ" };
        var yesButton = new Button { Content = "はい" };
        noButton.Click += (_, _) => dialog.Close();
        yesButton.Click += (_, _) =>
        {
            result = true;
            dialog.Close();
        };
        buttons.Children.Add(noButton);
        buttons.Children.Add(yesButton);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        await dialog.ShowDialog(this).ConfigureAwait(true);
        return result;
    }

    private async Task ShowPrintDialogAsync()
    {
        var tab = _activeTab;
        if (tab is null || tab.PageCount <= 0)
        {
            return;
        }

        var options = tab.PrintOptions;
        options.CurrentPage = tab.CurrentPage;
        options.RangeStartPage = Math.Clamp(options.RangeStartPage, 1, tab.PageCount);
        options.RangeEndPage = Math.Clamp(options.RangeEndPage, 1, tab.PageCount);

        var dialog = CreateStyledDialog("印刷", DialogSeverity.Information, 780, 540, canResize: true);

        var allPagesRadio = new RadioButton { GroupName = "PrintRange", Content = "全ページ", IsChecked = options.RangeMode == PrintPageRangeMode.AllPages };
        var currentPageRadio = new RadioButton { GroupName = "PrintRange", Content = $"現在ページ ({tab.CurrentPage})", IsChecked = options.RangeMode == PrintPageRangeMode.CurrentPage };
        var rangeRadio = new RadioButton { GroupName = "PrintRange", Content = "範囲" };
        var rangeTextBox = new TextBox
        {
            Width = 120,
            Text = $"{options.RangeStartPage}-{options.RangeEndPage}",
            IsEnabled = options.RangeMode == PrintPageRangeMode.PageRange
        };
        if (options.RangeMode == PrintPageRangeMode.PageRange)
        {
            rangeRadio.IsChecked = true;
        }

        var paperComboBox = new ComboBox
        {
            Width = 140,
            ItemsSource = new[] { "A4", "A3", "Letter" },
            SelectedItem = options.PaperSizeName
        };
        var orientationComboBox = new ComboBox
        {
            Width = 120,
            ItemsSource = new[] { "縦", "横" },
            SelectedIndex = options.IsLandscape ? 1 : 0
        };
        var copiesTextBox = new TextBox { Width = 64, Text = Math.Max(1, options.Copies).ToString(CultureInfo.InvariantCulture) };
        var previewImage = new Image { Stretch = Stretch.Uniform, Width = 360, Height = 460 };

        async Task UpdatePreviewAsync()
        {
            var mode = ResolvePrintRangeMode();
            var pages = ResolvePageNumbers(tab, rangeTextBox.Text, mode);
            if (pages.Count == 0)
            {
                previewImage.Source = null;
                return;
            }

            using var previewBitmap = await _pdfRenderService.RenderPageForPrintAsync(tab.Document.Pages[pages[0] - 1], 150).ConfigureAwait(true);
            previewImage.Source = ToBitmap(previewBitmap);
        }

        rangeRadio.Checked += (_, _) => rangeTextBox.IsEnabled = true;
        allPagesRadio.Checked += async (_, _) =>
        {
            rangeTextBox.IsEnabled = false;
            await UpdatePreviewAsync().ConfigureAwait(true);
        };
        currentPageRadio.Checked += async (_, _) =>
        {
            rangeTextBox.IsEnabled = false;
            await UpdatePreviewAsync().ConfigureAwait(true);
        };
        rangeRadio.Checked += async (_, _) => await UpdatePreviewAsync().ConfigureAwait(true);
        rangeTextBox.LostFocus += async (_, _) => await UpdatePreviewAsync().ConfigureAwait(true);

        await UpdatePreviewAsync().ConfigureAwait(true);

        var printRequested = false;
        var printButton = new Button { Content = "印刷", Width = 88 };
        var cancelButton = new Button { Content = "キャンセル", Width = 88 };
        printButton.Click += (_, _) =>
        {
            printRequested = true;
            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();

        var rightPanel = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "ページ範囲", Foreground = new SolidColorBrush((Color)Application.Current.FindResource("TextPrimary")!) },
                allPagesRadio,
                currentPageRadio,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children = { rangeRadio, rangeTextBox }
                },
                new Separator(),
                new TextBlock { Text = "用紙", Foreground = new SolidColorBrush((Color)Application.Current.FindResource("TextPrimary")!) },
                paperComboBox,
                new TextBlock { Text = "向き", Foreground = new SolidColorBrush((Color)Application.Current.FindResource("TextPrimary")!) },
                orientationComboBox,
                new TextBlock { Text = "部数", Foreground = new SolidColorBrush((Color)Application.Current.FindResource("TextPrimary")!) },
                copiesTextBox
            }
        };

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancelButton, printButton }
        };

        var root = new DockPanel { LastChildFill = true, Margin = new Thickness(12) };
        DockPanel.SetDock(buttonPanel, Dock.Bottom);
        buttonPanel.Margin = new Thickness(0, 12, 0, 0);
        root.Children.Add(buttonPanel);

        var contentGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("2*,*"),
            ColumnSpacing = 12
        };
        var previewBorder = new Border
        {
            BorderBrush = new SolidColorBrush((Color)Application.Current!.FindResource("BorderLight")!),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Child = previewImage
        };
        Grid.SetColumn(previewBorder, 0);
        Grid.SetColumn(rightPanel, 1);
        contentGrid.Children.Add(previewBorder);
        contentGrid.Children.Add(rightPanel);
        root.Children.Add(contentGrid);
        dialog.Content = root;

        await dialog.ShowDialog(this).ConfigureAwait(true);
        if (!printRequested)
        {
            return;
        }

        var mode = ResolvePrintRangeMode();
        var pages = ResolvePageNumbers(tab, rangeTextBox.Text, mode);
        options.RangeMode = mode;
        options.Copies = Math.Max(1, int.TryParse(copiesTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCopies) ? parsedCopies : 1);
        options.PaperSizeName = paperComboBox.SelectedItem?.ToString() ?? "A4";
        options.IsLandscape = orientationComboBox.SelectedIndex == 1;
        options.RangeStartPage = pages.Count > 0 ? pages.Min() : options.RangeStartPage;
        options.RangeEndPage = pages.Count > 0 ? pages.Max() : options.RangeEndPage;

        await PrintPagesAsync(tab, options, pages).ConfigureAwait(true);

        PrintPageRangeMode ResolvePrintRangeMode()
        {
            if (currentPageRadio.IsChecked == true)
            {
                return PrintPageRangeMode.CurrentPage;
            }

            if (rangeRadio.IsChecked == true)
            {
                return PrintPageRangeMode.PageRange;
            }

            return PrintPageRangeMode.AllPages;
        }
    }

    private static IReadOnlyList<int> ResolvePageNumbers(TabViewModel tab, string? rangeText, PrintPageRangeMode mode)
    {
        if (mode == PrintPageRangeMode.CurrentPage)
        {
            return [Math.Clamp(tab.CurrentPage, 1, tab.PageCount)];
        }

        if (mode != PrintPageRangeMode.PageRange)
        {
            return tab.PrintOptions.ResolvePageNumbers(tab.PageCount);
        }

        var text = rangeText?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return [Math.Clamp(tab.CurrentPage, 1, tab.PageCount)];
        }

        var parts = text.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var start) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var end))
        {
            return [Math.Clamp(tab.CurrentPage, 1, tab.PageCount)];
        }

        tab.PrintOptions.RangeStartPage = start;
        tab.PrintOptions.RangeEndPage = end;
        tab.PrintOptions.RangeMode = PrintPageRangeMode.PageRange;
        return tab.ResolvePrintPages();
    }

    private async Task PrintPagesAsync(TabViewModel tab, PrintOptions options, IReadOnlyList<int> pageNumbers)
    {
        if (pageNumbers.Count == 0)
        {
            return;
        }

        try
        {
            if (OperatingSystem.IsLinux())
            {
                var tempFiles = new List<string>();
                try
                {
                    foreach (var pageNumber in pageNumbers)
                    {
                        using var bitmap = await _pdfRenderService.RenderPageForPrintAsync(tab.Document.Pages[pageNumber - 1], 300).ConfigureAwait(true);
                        var tempPath = Path.Combine(Path.GetTempPath(), $"acropdf-print-{Guid.NewGuid():N}-{pageNumber}.png");
                        using var image = SKImage.FromBitmap(bitmap);
                        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                        await using var output = File.Create(tempPath);
                        data.SaveTo(output);
                        tempFiles.Add(tempPath);
                    }

                    var psi = new ProcessStartInfo("lp")
                    {
                        UseShellExecute = false
                    };
                    psi.ArgumentList.Add("-n");
                    psi.ArgumentList.Add(Math.Max(1, options.Copies).ToString(CultureInfo.InvariantCulture));
                    psi.ArgumentList.Add("-o");
                    psi.ArgumentList.Add($"media={SanitizePaperSize(options.PaperSizeName)}");
                    if (options.IsLandscape)
                    {
                        psi.ArgumentList.Add("-o");
                        psi.ArgumentList.Add("landscape");
                    }

                    psi.ArgumentList.Add("-o");
                    psi.ArgumentList.Add($"page-ranges={string.Join(",", pageNumbers)}");
                    foreach (var tempFile in tempFiles)
                    {
                        psi.ArgumentList.Add(tempFile);
                    }

                    using var process = Process.Start(psi);
                    process?.WaitForExit(5000);
                }
                finally
                {
                    foreach (var tempFile in tempFiles)
                    {
                        if (File.Exists(tempFile))
                        {
                            File.Delete(tempFile);
                        }
                    }
                }
            }
            else if (OperatingSystem.IsWindows())
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = tab.Document.FilePath,
                    Verb = "print",
                    UseShellExecute = true
                });
                process?.WaitForExit(3000);
            }
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Print request failed: {ex}");
        }
    }

    private static string SanitizePaperSize(string? value)
    {
        var safe = new string((value ?? string.Empty)
            .Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_')
            .ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "A4" : safe;
    }

    private static Bitmap ToBitmap(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var stream = new MemoryStream(data.ToArray(), writable: false);
        return new Bitmap(stream);
    }

    private static Window CreateStyledDialog(string title, DialogSeverity severity, double width, double height, bool canResize = false)
    {
        var icon = severity switch
        {
            DialogSeverity.Warning => "⚠ ",
            DialogSeverity.Error => "⛔ ",
            _ => "ℹ "
        };

        return new Window
        {
            Width = width,
            Height = height,
            CanResize = canResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Title = $"{icon}{title}",
            Background = new SolidColorBrush((Color)Application.Current!.FindResource("BgDark")!)
        };
    }

    private void SaveSessionSettings()
    {
        var session = _tabs
            .Select(tab => new SessionEntry(tab.Document.FilePath, tab.CurrentPage))
            .ToArray();
        var recent = _settingsService.GetRecentFiles();
        _settingsService.SaveSession(session);
        _settings = _settings with { RecentFiles = recent };
        _settingsService.Save(_settings);
    }

    private void SetLoadingOverlayVisible(bool visible)
    {
        if (visible)
        {
            LoadingOverlay.IsVisible = true;
            LoadingOverlay.Opacity = 1d;
        }
        else
        {
            LoadingOverlay.Opacity = 0d;
            LoadingOverlay.IsVisible = false;
        }
    }

    private sealed class ContinuousPageItemState
    {
        public ContinuousPageItemState(TabViewModel tab, PdfPage page, Border host)
        {
            Tab = tab;
            Page = page;
            Host = host;
        }

        public TabViewModel Tab { get; }

        public PdfPage Page { get; }

        public Border Host { get; }

        public PdfPageControl? PageControl { get; set; }

        public Canvas? OverlayCanvas { get; set; }

        public double RenderedZoomLevel { get; set; }

        public bool IsRendering { get; set; }
    }

    private enum DialogSeverity
    {
        Information,
        Warning,
        Error
    }

    private sealed class WatchedFile : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private readonly FileSystemEventHandler _changedHandler;
        private readonly RenamedEventHandler _renamedHandler;

        public WatchedFile(FileSystemWatcher watcher, FileSystemEventHandler changedHandler, RenamedEventHandler renamedHandler)
        {
            _watcher = watcher;
            _changedHandler = changedHandler;
            _renamedHandler = renamedHandler;
        }

        public void Dispose()
        {
            _watcher.Changed -= _changedHandler;
            _watcher.Created -= _changedHandler;
            _watcher.Renamed -= _renamedHandler;
            _watcher.Dispose();
        }
    }

    private void OpenFileWithoutAwait(string filePath)
    {
        _ = OpenFileAsync(filePath).ContinueWith(
            static task => Trace.TraceError(task.Exception?.GetBaseException().Message),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    private enum AnnotationSaveDecision
    {
        Save,
        Discard,
        Cancel
    }

    private enum AnnotationTool
    {
        TextSelect,
        Highlight,
        Comment,
        Freehand,
        Shape,
        Stamp
    }

    private sealed record AnnotationPanelItem(Guid AnnotationId, int PageNumber, string Badge, string Summary)
    {
        public string Display => $"{Badge} P{PageNumber}: {Summary}";

        public override string ToString() => Display;
    }

    private sealed record AttachmentPanelItem(PdfEmbeddedFile File, string Display)
    {
        public override string ToString() => Display;
    }
}
