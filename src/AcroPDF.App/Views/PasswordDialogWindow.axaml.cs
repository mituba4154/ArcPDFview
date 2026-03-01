using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AcroPDF.App.Views;

/// <summary>
/// パスワード入力ダイアログです。
/// </summary>
public partial class PasswordDialogWindow : Window
{
    /// <summary>
    /// <see cref="PasswordDialogWindow"/> の新しいインスタンスを初期化します。
    /// </summary>
    public PasswordDialogWindow()
    {
        InitializeComponent();
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        var passwordBox = this.FindControl<TextBox>("PasswordTextBox");
        var password = passwordBox?.Text;
        Close(password);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
