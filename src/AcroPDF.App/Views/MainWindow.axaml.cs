#nullable enable

using System.Diagnostics;
using System.Globalization;
using System.Text;
using AcroPDF.App.Controls;
using AcroPDF.Core.Models;
using AcroPDF.Services;
using AcroPDF.Services.Interfaces;
using AcroPDF.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
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
    private readonly IPdfRenderService _pdfRenderService;
    private readonly SearchService _searchService;
    private readonly ISettingsService _settingsService;
    private readonly MainWindowViewModel _mainWindowViewModel = new();
    private readonly List<TabViewModel> _tabs = [];
    private readonly Dictionary<PdfPageControl, (TabViewModel Tab, PdfPage Page)> _continuousPageMap = [];
    private readonly Dictionary<TabViewModel, IReadOnlyList<PdfBookmarkItem>> _bookmarkMap = [];
    private readonly Dictionary<(TabViewModel Tab, int PageNumber), IReadOnlyList<Avalonia.Rect>> _selectionHighlightMap = [];
    private readonly Dictionary<TabViewModel, FileSystemWatcher> _watchers = [];
    private readonly List<TabViewModel> _splitDetachedTabs = [];
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _renderCts;
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

    /// <summary>
    /// <see cref="MainWindow"/> の新しいインスタンスを初期化します。
    /// </summary>
    public MainWindow()
        : this(new PdfiumRenderService())
    {
    }

    /// <summary>
    /// <see cref="MainWindow"/> の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="pdfRenderService">PDF レンダリングサービス。</param>
    public MainWindow(IPdfRenderService pdfRenderService)
    {
        _pdfRenderService = pdfRenderService ?? throw new ArgumentNullException(nameof(pdfRenderService));
        _searchService = new SearchService();
        _settingsService = new SettingsService();
        _settings = _settingsService.Load();
        InitializeComponent();
        InitializeStaticStatusText();
        InitializeTextSelectionContextMenu();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        Closed += OnClosed;
        Opened += OnOpened;
        SetEmptyStateVisible(true);
        DataContext = _mainWindowViewModel;
        SplitDivider.Cursor = new Cursor(StandardCursorType.SizeWestEast);
        ApplySettingsToToolbar();
        RebuildRecentFilesMenu();
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
        }

        if (document is null)
        {
            return null;
        }

        var tab = new TabViewModel(document);
        tab.ZoomLevel = _settings.DefaultZoom;
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
        _mainWindowViewModel.Tabs = new System.Collections.ObjectModel.ObservableCollection<TabViewModel>(_tabs);
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
        RebuildTabBar();
        RebuildSplitTabSelectors();
        UpdateToolbarState();
        RebuildThumbnailPanel();
        RebuildBookmarkPanel();
        UpdateSearchOverlayState();
        _ = RenderActiveTabAsync();
    }

    private async Task RenderActiveTabAsync()
    {
        CancelRender();
        _renderCts = new CancellationTokenSource();
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
    }

    private async Task RenderSinglePageAsync(TabViewModel tab, CancellationToken ct)
    {
        SplitViewGrid.IsVisible = false;
        SinglePageScrollViewer.IsVisible = true;
        ContinuousScrollViewer.IsVisible = false;
        ContinuousPagePanel.Children.Clear();
        _continuousPageMap.Clear();

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
        PageControl.SetBitmap(bitmap);
        ApplyHighlights(PageControl, tab, page);
        UpdateStatusBar();
    }

    private async Task RenderSplitViewAsync(TabViewModel primaryTab, CancellationToken ct)
    {
        SinglePageScrollViewer.IsVisible = false;
        ContinuousScrollViewer.IsVisible = false;
        SplitViewGrid.IsVisible = true;
        ContinuousPagePanel.Children.Clear();
        _continuousPageMap.Clear();

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
        if (tab.PageCount <= 0 || tab.CurrentPage <= 0 || tab.CurrentPage > tab.PageCount)
        {
            return null;
        }

        var page = tab.Document.Pages[tab.CurrentPage - 1];
        using var first = await _pdfRenderService.RenderPageAsync(page, tab.ZoomLevel, ct).ConfigureAwait(true);
        var bitmap = first.Copy();
        if (tab.IsTwoPageMode && tab.CurrentPage < tab.PageCount)
        {
            var secondPage = tab.Document.Pages[tab.CurrentPage];
            using var second = await _pdfRenderService.RenderPageAsync(secondPage, tab.ZoomLevel, ct).ConfigureAwait(true);
            using var secondCopy = second.Copy();
            var merged = new SKBitmap(bitmap.Width + secondCopy.Width + 16, Math.Max(bitmap.Height, secondCopy.Height), SKColorType.Bgra8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(merged);
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(bitmap, 0, 0);
            canvas.DrawBitmap(secondCopy, bitmap.Width + 16, 0);
            bitmap.Dispose();
            bitmap = merged;
        }

        if (tab.RotationDegrees == 0)
        {
            return bitmap;
        }

        var rotated = RotateBitmap(bitmap, tab.RotationDegrees);
        bitmap.Dispose();
        return rotated;
    }

    private async Task RenderContinuousModeAsync(TabViewModel tab, CancellationToken ct)
    {
        SplitViewGrid.IsVisible = false;
        SinglePageScrollViewer.IsVisible = false;
        ContinuousScrollViewer.IsVisible = true;
        ContinuousPagePanel.Children.Clear();
        _continuousPageMap.Clear();

        foreach (var page in tab.Document.Pages)
        {
            ct.ThrowIfCancellationRequested();
            using var bitmap = await _pdfRenderService.RenderPageAsync(page, tab.ZoomLevel, ct).ConfigureAwait(true);
            var pageControl = new PdfPageControl
            {
                Width = bitmap.Width,
                Height = bitmap.Height,
                ZoomLevel = 1.0d,
                CurrentPage = page.PageNumber,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            pageControl.SetBitmap(bitmap);
            pageControl.PointerMoved += OnContinuousPagePointerMoved;
            pageControl.PointerPressed += OnContinuousPagePointerPressed;
            pageControl.PointerReleased += OnContinuousPagePointerReleased;
            pageControl.ContextMenu = BuildTextSelectionContextMenu(pageControl);

            _continuousPageMap[pageControl] = (tab, page);
            ApplyHighlights(pageControl, tab, page);
            ContinuousPagePanel.Children.Add(pageControl);
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

        var target = ContinuousPagePanel.Children
            .OfType<PdfPageControl>()
            .FirstOrDefault(control => control.CurrentPage == tab.CurrentPage);
        target?.BringIntoView();
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

            closeButton.Click += (_, _) => CloseTab(tab);
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
        closeOthers.Click += (_, _) => CloseOtherTabs(tab);

        var closeRight = new MenuItem { Header = "右を閉じる" };
        closeRight.Click += (_, _) => CloseTabsToRight(tab);

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
                Content = border
            };
            border.Child = stack;
            button.Click += (_, _) =>
            {
                tab.JumpToPage(thumbnail.PageNumber);
                RebuildThumbnailPanel();
                _ = RenderActiveTabAsync();
            };

            ThumbnailPanel.Children.Add(button);

            if (thumbnail.IsSelected)
            {
                button.BringIntoView();
            }
        }
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
            var results = await _searchService.SearchAsync(
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
        var underline = new MenuItem { Header = "下線を引く" };
        var strike = new MenuItem { Header = "取り消し線" };
        var comment = new MenuItem { Header = "コメントを追加" };
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
        PrimarySplitPageControl.SetBitmap(null);
        SecondarySplitPageControl.SetBitmap(null);
        ContinuousPagePanel.Children.Clear();
        _continuousPageMap.Clear();
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

    private void CloseTab(TabViewModel tab)
    {
        if (!_tabs.Remove(tab))
        {
            return;
        }

        tab.ThumbnailUpdated -= OnThumbnailUpdated;
        tab.CancelThumbnailGeneration();
        _bookmarkMap.Remove(tab);
        StopTrackingFileChanges(tab);
        foreach (var key in _selectionHighlightMap.Keys.Where(key => ReferenceEquals(key.Tab, tab)).ToArray())
        {
            _selectionHighlightMap.Remove(key);
        }
        if (ReferenceEquals(_splitSecondaryTab, tab))
        {
            _splitSecondaryTab = null;
            _mainWindowViewModel.SplitSecondaryTab = null;
        }
        _pdfRenderService.Close(tab.Document);
        tab.Dispose();
        _mainWindowViewModel.Tabs = new System.Collections.ObjectModel.ObservableCollection<TabViewModel>(_tabs);

        if (_tabs.Count == 0)
        {
            _activeTab = null;
            _mainWindowViewModel.ActiveTab = null;
            _mainWindowViewModel.IsSplitView = false;
            CleanupSplitDetachedTabs();
            RebuildTabBar();
            RebuildSplitTabSelectors();
            RebuildThumbnailPanel();
            CancelRender();
            ClearPageViews();
            SetEmptyStateVisible(true);
            UpdateToolbarState();
            UpdateStatusBar();
            return;
        }

        RebuildSplitTabSelectors();
        ActivateTab(_tabs[Math.Max(0, _tabs.Count - 1)]);
    }

    private void CloseOtherTabs(TabViewModel baseTab)
    {
        foreach (var tab in _tabs.Where(tab => !ReferenceEquals(tab, baseTab)).ToArray())
        {
            CloseTab(tab);
        }

        ActivateTab(baseTab);
    }

    private void CloseTabsToRight(TabViewModel baseTab)
    {
        var index = _tabs.IndexOf(baseTab);
        if (index < 0)
        {
            return;
        }

        foreach (var tab in _tabs.Skip(index + 1).ToArray())
        {
            CloseTab(tab);
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
        _selectionHighlightMap[(tab, page.PageNumber)] = selection.Bounds
            .Select(item => ConvertBoundsToPixelRect(item, page, tab.ZoomLevel))
            .ToArray();
        _ = RenderActiveTabAsync();
    }

    private async Task CopySelectedTextAsync()
    {
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
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
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
            CloseTab(tab);
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
            var point = e.GetPosition(control);
            UpdateMouseCoordinates(info.Tab, info.Page, point, info.Tab.ZoomLevel);
            _isSelectingText = true;
            _selectionTab = info.Tab;
            _selectionPage = info.Page;
            _selectionStartPdfPoint = new Point(info.Tab.MouseX, info.Tab.MouseY);
            _selectedText = string.Empty;
            _selectionHighlightMap.Remove((info.Tab, info.Page.PageNumber));
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
        _isSelectingText = true;
        _selectionTab = tab;
        _selectionPage = page;
        _selectionStartPdfPoint = new Point(tab.MouseX, tab.MouseY);
        _selectedText = string.Empty;
        _selectionHighlightMap.Remove((tab, page.PageNumber));
    }

    private void OnSinglePagePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isSelectingText)
        {
            return;
        }

        _isSelectingText = false;
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
        if (PrimarySplitTabComboBox.SelectedIndex < 0 || PrimarySplitTabComboBox.SelectedIndex >= _tabs.Count)
        {
            return;
        }

        ActivateTab(_tabs[PrimarySplitTabComboBox.SelectedIndex]);
    }

    private void OnSecondarySplitTabSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
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
        var leftWidth = Math.Clamp(point.X, 120d, totalWidth - 120d);
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
        _ = RenderActiveTabAsync();
        e.Handled = true;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
            e.Handled = true;
            return;
        }

        var targetTab = GetCommandTargetTab();
        if (targetTab is null)
        {
            return;
        }

        if ((e.KeyModifiers & KeyModifiers.Control) != 0 && e.Key == Key.W)
        {
            CloseTab(targetTab);
            e.Handled = true;
            return;
        }

        if ((e.KeyModifiers & KeyModifiers.Control) != 0 && e.Key == Key.F)
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

        if ((e.KeyModifiers & KeyModifiers.Control) != 0 && e.Key == Key.C)
        {
            _ = CopySelectedTextAsync();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F3)
        {
            MoveToNextSearchResult((e.KeyModifiers & KeyModifiers.Shift) != 0);
            e.Handled = true;
            return;
        }

        if ((e.KeyModifiers & KeyModifiers.Control) != 0)
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

    private void OnOpened(object? sender, EventArgs e)
    {
        if (_restoreAttempted)
        {
            return;
        }

        _restoreAttempted = true;
        if (_skipSessionRestore || !_settings.RestoreSessionOnStartup || _settings.LastSession.Count == 0)
        {
            return;
        }

        _ = RestoreLastSessionAsync();
    }

    private async Task RestoreLastSessionAsync()
    {
        foreach (var entry in _settings.LastSession.Where(entry => File.Exists(entry.FilePath)))
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
        _watchers[tab] = watcher;
    }

    private void StopTrackingFileChanges(TabViewModel tab)
    {
        if (_watchers.Remove(tab, out var watcher))
        {
            watcher.Dispose();
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
        CloseTab(tab);
        _ = await OpenFileAsync(filePath, currentPage).ConfigureAwait(true);
    }

    private async Task<bool> ShowReloadConfirmDialogAsync()
    {
        var dialog = new Window
        {
            Width = 320,
            Height = 150,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Title = "ファイル変更検知",
            Background = new SolidColorBrush((Color)Application.Current!.FindResource("BgDark")!)
        };

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

    private void SaveSessionSettings()
    {
        var session = _tabs
            .Select(tab => new SessionEntry(tab.Document.FilePath, tab.CurrentPage))
            .ToArray();
        var recent = _settingsService.GetRecentFiles();
        _settings = _settings with { LastSession = session, RecentFiles = recent };
        _settingsService.Save(_settings);
    }

    private static SKBitmap RotateBitmap(SKBitmap source, int degrees)
    {
        var normalized = ((degrees % 360) + 360) % 360;
        if (normalized == 0)
        {
            return source.Copy();
        }

        var swapSize = normalized is 90 or 270;
        var width = swapSize ? source.Height : source.Width;
        var height = swapSize ? source.Width : source.Height;
        var rotated = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(rotated);
        canvas.Clear(SKColors.Transparent);
        canvas.Translate(width / 2f, height / 2f);
        canvas.RotateDegrees(normalized);
        canvas.Translate(-source.Width / 2f, -source.Height / 2f);
        canvas.DrawBitmap(source, 0, 0);
        return rotated;
    }

    private void OpenFileWithoutAwait(string filePath)
    {
        _ = OpenFileAsync(filePath).ContinueWith(
            static task => Trace.TraceError(task.Exception?.GetBaseException().Message),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }
}
