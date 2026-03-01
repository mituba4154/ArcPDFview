#nullable enable

using System.Globalization;
using System.Resources;

namespace AcroPDF.App.Assets.Localization;

/// <summary>
/// UI 文字列のローカライズを提供します。
/// </summary>
public static class AppStrings
{
    private static readonly ResourceManager ResourceManager = new("AcroPDF.App.Assets.Localization.Strings", typeof(AppStrings).Assembly);

    /// <summary>
    /// 現在の UI カルチャを取得または設定します。
    /// </summary>
    public static CultureInfo CurrentCulture { get; set; } = CultureInfo.CurrentUICulture;

    /// <summary>
    /// キーに対応する文字列を取得します。
    /// </summary>
    /// <param name="key">リソースキー。</param>
    /// <returns>ローカライズ文字列。</returns>
    public static string Get(string key)
    {
        return ResourceManager.GetString(key, CurrentCulture) ?? key;
    }
}
