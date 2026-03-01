#nullable enable

using System.Diagnostics;
using System.Globalization;
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
    private readonly List<TabViewModel> _tabs = [];
    private readonly Dictionary<PdfPageControl, (TabViewModel Tab, PdfPage Page)> _continuousPageMap = [];
    private CancellationTokenSource? _renderCts;
    private TabViewModel? _activeTab;

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
        InitializeComponent();
        InitializeStaticStatusText();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        Closed += OnClosed;
        SetEmptyStateVisible(true);
    }

    /// <summary>
    /// 起動引数から渡されたファイルを開きます。
    /// </summary>
    /// <param name="filePath">開くファイルパス。</param>
    public void OpenFromStartupArgument(string filePath)
    {
        OpenFileWithoutAwait(filePath);
    }

    private async Task OpenFileAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
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
                    return;
                }
            }
        }

        var tab = new TabViewModel(document);
        tab.ThumbnailUpdated += OnThumbnailUpdated;
        _tabs.Add(tab);
        ActivateTab(tab);
        _ = GenerateThumbnailsAsync(tab);
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
        RebuildTabBar();
        UpdateToolbarState();
        RebuildThumbnailPanel();
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
            if (tab.IsContinuousMode)
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
        SinglePageScrollViewer.IsVisible = true;
        ContinuousScrollViewer.IsVisible = false;
        ContinuousPagePanel.Children.Clear();
        _continuousPageMap.Clear();

        var page = tab.Document.Pages[tab.CurrentPage - 1];
        using var bitmap = await _pdfRenderService.RenderPageAsync(page, tab.ZoomLevel, ct).ConfigureAwait(true);
        PageControl.CurrentPage = page.PageNumber;
        PageControl.ZoomLevel = 1.0d;
        PageControl.Width = bitmap.Width;
        PageControl.Height = bitmap.Height;
        PageControl.SetBitmap(bitmap);
        UpdateStatusBar();
    }

    private async Task RenderContinuousModeAsync(TabViewModel tab, CancellationToken ct)
    {
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

            _continuousPageMap[pageControl] = (tab, page);
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

    private void SetEmptyStateVisible(bool visible)
    {
        EmptyStateTextBlock.IsVisible = visible;
        SinglePageScrollViewer.IsVisible = !visible && !(_activeTab?.IsContinuousMode ?? false);
        ContinuousScrollViewer.IsVisible = !visible && (_activeTab?.IsContinuousMode ?? false);
        ThumbnailScrollViewer.IsVisible = !visible;
    }

    private void ClearPageViews()
    {
        PageControl.SetBitmap(null);
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
            await OpenFileAsync(filePath).ConfigureAwait(true);
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
        _pdfRenderService.Close(tab.Document);
        tab.Dispose();

        if (_tabs.Count == 0)
        {
            _activeTab = null;
            RebuildTabBar();
            RebuildThumbnailPanel();
            CancelRender();
            ClearPageViews();
            SetEmptyStateVisible(true);
            UpdateToolbarState();
            UpdateStatusBar();
            return;
        }

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

    private void UpdateMouseCoordinates(TabViewModel tab, PdfPage page, Point position, double zoom)
    {
        var x = (position.X * PdfDpi) / (ScreenDpi * Math.Max(zoom, 0.01d));
        var y = page.HeightPt - ((position.Y * PdfDpi) / (ScreenDpi * Math.Max(zoom, 0.01d)));
        tab.MouseX = Math.Clamp(x, 0, page.WidthPt);
        tab.MouseY = Math.Clamp(y, 0, page.HeightPt);
        UpdateStatusBar();
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
        CancelRender();
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
    }

    private void OnContinuousPagePointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not PdfPageControl control || !_continuousPageMap.TryGetValue(control, out var info))
        {
            return;
        }

        var point = e.GetPosition(control);
        UpdateMouseCoordinates(info.Tab, info.Page, point, info.Tab.ZoomLevel);
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
    }

    private async void OnOpenFileClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await OpenFileFromPickerAsync().ConfigureAwait(true);
    }

    private async void OnAddTabClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await OpenFileFromPickerAsync().ConfigureAwait(true);
    }

    private void OnFirstPageClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_activeTab is null)
        {
            return;
        }

        _activeTab.MoveFirstPageCommand.Execute(null);
        RebuildThumbnailPanel();
        _ = RenderActiveTabAsync();
    }

    private void OnPreviousPageClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_activeTab is null)
        {
            return;
        }

        _activeTab.MovePreviousPageCommand.Execute(null);
        RebuildThumbnailPanel();
        _ = RenderActiveTabAsync();
    }

    private void OnNextPageClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_activeTab is null)
        {
            return;
        }

        _activeTab.MoveNextPageCommand.Execute(null);
        RebuildThumbnailPanel();
        _ = RenderActiveTabAsync();
    }

    private void OnLastPageClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_activeTab is null)
        {
            return;
        }

        _activeTab.MoveLastPageCommand.Execute(null);
        RebuildThumbnailPanel();
        _ = RenderActiveTabAsync();
    }

    private void OnPageNumberTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _activeTab is null)
        {
            return;
        }

        _activeTab.PageInputText = PageNumberTextBox.Text ?? string.Empty;
        _activeTab.JumpFromInput();
        RebuildThumbnailPanel();
        _ = RenderActiveTabAsync();
        e.Handled = true;
    }

    private void OnZoomOutClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_activeTab is null)
        {
            return;
        }

        _activeTab.ZoomOutCommand.Execute(null);
        _ = RenderActiveTabAsync();
    }

    private void OnZoomInClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_activeTab is null)
        {
            return;
        }

        _activeTab.ZoomInCommand.Execute(null);
        _ = RenderActiveTabAsync();
    }

    private void OnZoomComboBoxSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_activeTab is null || ZoomComboBox.SelectedItem is not ComboBoxItem selected)
        {
            return;
        }

        var option = selected.Content?.ToString() ?? string.Empty;
        if (option.EndsWith('%') && double.TryParse(option.TrimEnd('%'), out var percent))
        {
            _activeTab.ZoomLevel = PdfiumRenderService.ClampZoomLevel(percent / 100d);
        }
        else if (string.Equals(option, "幅に合わせる", StringComparison.Ordinal))
        {
            _activeTab.FitToWidth(SinglePageScrollViewer.Viewport.Width);
        }
        else if (string.Equals(option, "ページに合わせる", StringComparison.Ordinal))
        {
            _activeTab.FitToPage(SinglePageScrollViewer.Viewport.Width, SinglePageScrollViewer.Viewport.Height);
        }
        else if (string.Equals(option, "実寸", StringComparison.Ordinal))
        {
            _activeTab.ZoomActualSizeCommand.Execute(null);
        }
        else
        {
            return;
        }

        _ = RenderActiveTabAsync();
    }

    private void OnSinglePageModeClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_activeTab is null)
        {
            return;
        }

        _activeTab.SetSinglePageModeCommand.Execute(null);
        _ = RenderActiveTabAsync();
    }

    private void OnContinuousModeClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_activeTab is null)
        {
            return;
        }

        _activeTab.SetContinuousModeCommand.Execute(null);
        _ = RenderActiveTabAsync();
    }

    private void OnWindowPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_activeTab is null)
        {
            return;
        }

        if ((e.KeyModifiers & KeyModifiers.Control) == 0)
        {
            return;
        }

        _activeTab.ZoomLevel = PdfiumRenderService.ClampZoomLevel(_activeTab.ZoomLevel + (e.Delta.Y > 0 ? 0.1d : -0.1d));
        _ = RenderActiveTabAsync();
        e.Handled = true;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (_activeTab is null)
        {
            return;
        }

        if ((e.KeyModifiers & KeyModifiers.Control) != 0 && e.Key == Key.W)
        {
            CloseTab(_activeTab);
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
                _activeTab.MovePreviousPageCommand.Execute(null);
                break;
            case Key.Right:
                _activeTab.MoveNextPageCommand.Execute(null);
                break;
            case Key.Home:
                _activeTab.MoveFirstPageCommand.Execute(null);
                break;
            case Key.End:
                _activeTab.MoveLastPageCommand.Execute(null);
                break;
            default:
                return;
        }

        RebuildThumbnailPanel();
        _ = RenderActiveTabAsync();
        e.Handled = true;
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
