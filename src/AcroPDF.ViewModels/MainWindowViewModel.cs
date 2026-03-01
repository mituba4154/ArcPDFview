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

    /// <summary>
    /// タブを追加します。
    /// </summary>
    /// <param name="tab">追加対象。</param>
    public void AddTab(TabViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        Tabs.Add(tab);
    }

    /// <summary>
    /// タブを削除します。
    /// </summary>
    /// <param name="tab">削除対象。</param>
    /// <returns>削除結果。</returns>
    public bool RemoveTab(TabViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        return Tabs.Remove(tab);
    }

    /// <summary>
    /// タブ一覧を初期化します。
    /// </summary>
    /// <param name="tabs">タブ一覧。</param>
    public void ReplaceTabs(IEnumerable<TabViewModel> tabs)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        Tabs.Clear();
        foreach (var tab in tabs)
        {
            Tabs.Add(tab);
        }
    }
}
