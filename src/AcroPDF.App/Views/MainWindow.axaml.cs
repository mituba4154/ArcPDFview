using AcroPDF.App.Controls;
using AcroPDF.Core.Models;
using AcroPDF.Services;
using AcroPDF.Services.Interfaces;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace AcroPDF.App.Views;

/// <summary>
/// AcroPDF のメインウィンドウです。
/// </summary>
public partial class MainWindow : Window
{
    private readonly IPdfRenderService _pdfRenderService = new PdfiumRenderService();
    private PdfDocument? _currentDocument;

    /// <summary>
    /// <see cref="MainWindow"/> の新しいインスタンスを初期化します。
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        Closed += OnClosed;
    }

    /// <summary>
    /// 起動引数から渡されたファイルを開きます。
    /// </summary>
    /// <param name="filePath">開くファイルパス。</param>
    public void OpenFromStartupArgument(string filePath)
    {
        _ = OpenFileAsync(filePath);
    }

    private async Task OpenFileAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        string? password = null;
        while (true)
        {
            try
            {
                _currentDocument?.Dispose();
                _currentDocument = await _pdfRenderService.OpenAsync(filePath, password).ConfigureAwait(true);
                break;
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

        var firstPage = _currentDocument.Pages.FirstOrDefault();
        if (firstPage is null)
        {
            return;
        }

        var pageBitmap = await _pdfRenderService.RenderPageAsync(firstPage, 1.0d).ConfigureAwait(true);
        var pageControl = this.FindControl<PdfPageControl>("PageControl");
        if (pageControl is null)
        {
            pageBitmap.Dispose();
            return;
        }

        pageControl.ZoomLevel = 1.0d;
        pageControl.CurrentPage = firstPage.PageNumber;
        pageControl.SetBitmap(pageBitmap);
        pageBitmap.Dispose();
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

        var files = e.Data.GetFiles();
        var filePath = files?
            .OfType<IStorageFile>()
            .FirstOrDefault()?
            .Path.LocalPath;
        if (filePath is null)
        {
            return;
        }

        _ = OpenFileAsync(filePath);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _currentDocument?.Dispose();
        _pdfRenderService.Dispose();
    }
}
