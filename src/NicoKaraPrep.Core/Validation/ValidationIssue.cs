namespace NicoKaraPrep.Core.Validation;

public enum IssueSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>検証結果 1 件。</summary>
/// <param name="Severity">重要度。</param>
/// <param name="Category">種別（例: "ページ衝突" "横幅"）。</param>
/// <param name="LineIndex">ジャンプ先の行インデックス（0 始まり）。</param>
/// <param name="Message">表示メッセージ。</param>
/// <param name="RelatedLineIndex">関連するもう一方の行（ページ衝突の前ページ側の行など）。</param>
public sealed record ValidationIssue(IssueSeverity Severity, string Category, int LineIndex, string Message, int? RelatedLineIndex = null);
