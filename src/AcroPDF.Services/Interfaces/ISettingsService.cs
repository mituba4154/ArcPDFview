#nullable enable

using AcroPDF.Core.Models;

namespace AcroPDF.Services.Interfaces;

/// <summary>
/// アプリケーション設定の永続化機能を提供します。
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// 設定を読み込みます。
    /// </summary>
    /// <returns>読み込まれた設定。</returns>
    AppSettings Load();

    /// <summary>
    /// 設定を保存します。
    /// </summary>
    /// <param name="settings">保存する設定。</param>
    void Save(AppSettings settings);

    /// <summary>
    /// 最近のファイル一覧へ追加します。
    /// </summary>
    /// <param name="filePath">追加するファイルパス。</param>
    void AddRecentFile(string filePath);

    /// <summary>
    /// 最近のファイル一覧を取得します。
    /// </summary>
    /// <returns>ファイル一覧。</returns>
    IReadOnlyList<string> GetRecentFiles();

    /// <summary>
    /// 最近のファイル一覧から削除します。
    /// </summary>
    /// <param name="filePath">削除するファイルパス。</param>
    void RemoveRecentFile(string filePath);
}
