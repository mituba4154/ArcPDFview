#nullable enable

namespace AcroPDF.Core.Models;

/// <summary>
/// コメント（付箋）注釈を表します。
/// </summary>
public sealed class CommentAnnotation : Annotation
{
    /// <summary>
    /// コメント本文を取得または設定します。
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// 付箋が展開状態かどうかを取得または設定します。
    /// </summary>
    public bool IsOpen { get; set; }
}

