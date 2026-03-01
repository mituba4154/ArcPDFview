#nullable enable

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AcroPDF.ViewModels;

/// <summary>
/// メインウィンドウ全体の表示状態を表します。
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    /// <summary>
    /// 開いているタブ一覧を取得または設定します。
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<TabViewModel> _tabs = [];

    /// <summary>
    /// 現在アクティブなタブを取得または設定します。
    /// </summary>
    [ObservableProperty]
    private TabViewModel? _activeTab;

    /// <summary>
    /// 分割ビューの有効状態を取得または設定します。
    /// </summary>
    [ObservableProperty]
    private bool _isSplitView;

    /// <summary>
    /// 分割ビュー右ペインのタブを取得または設定します。
    /// </summary>
    [ObservableProperty]
    private TabViewModel? _splitSecondaryTab;
}
