namespace NicoKaraPrep.Core.Validation;

/// <summary>行ごとの幅の判定結果。</summary>
/// <param name="LineIndex">行インデックス。</param>
/// <param name="WidthPx">行の幅 px（字幕のプレビューと同じ並べ方で測った、行の両端の文字のすき間を含む幅。字幕の画面の px）。</param>
/// <param name="UsagePercent">左右余白を除いた幅に対する使用率 %。</param>
/// <param name="Severity">null = 問題なし / Warning = 左右余白を確保できない / Error = 画面からはみ出す。</param>
/// <param name="ScreenWidthPx">字幕の画面の横幅 px。</param>
/// <param name="SideMarginPx">片側の左右余白 px（行のページのレイアウト設定の左右余白）。</param>
public sealed record LineWidthResult(int LineIndex, double WidthPx, double UsagePercent, IssueSeverity? Severity, int ScreenWidthPx, double SideMarginPx)
{
    /// <summary>左右余白を除いた幅 px。</summary>
    public double UsableWidthPx => ScreenWidthPx - 2 * SideMarginPx;
}

/// <summary>
/// 行の幅の判定。幅は字幕のプレビューと同じ並べ方で測ったもの（App 側で測る）を受け取る。
/// エラー: 画面からはみ出す / 警告: 収まるが、レイアウト設定の左右余白を確保できない（ニコカラメーカー3 の「字幕の左右余白が確保できない」）。
/// </summary>
public static class LineWidthValidator
{
    /// <summary>測った幅を、画面の横幅と左右余白で判定する。</summary>
    public static LineWidthResult Evaluate(int lineIndex, double widthPx, int screenWidthPx, double sideMarginPx)
    {
        double margin = double.IsFinite(sideMarginPx) ? Math.Max(0, sideMarginPx) : 0;
        double usable = screenWidthPx - 2 * margin;
        double usage = usable <= 0 ? 0 : widthPx / usable * 100.0;
        IssueSeverity? severity = widthPx > screenWidthPx ? IssueSeverity.Error
            : widthPx > usable ? IssueSeverity.Warning
            : null;
        return new LineWidthResult(lineIndex, widthPx, usage, severity, screenWidthPx, margin);
    }

    public static List<ValidationIssue> ToIssues(IEnumerable<LineWidthResult> results)
    {
        var issues = new List<ValidationIssue>();
        foreach (var r in results)
        {
            if (r.Severity is not IssueSeverity s) continue;
            string message = s == IssueSeverity.Error
                ? $"{r.LineIndex + 1}行目: 画面からはみ出します（{r.WidthPx:F0}px > 画面 {r.ScreenWidthPx}px）"
                : $"{r.LineIndex + 1}行目: 左右余白 {r.SideMarginPx:F0}px を確保できません（{r.WidthPx:F0}px > 余白を除いた幅 {r.UsableWidthPx:F0}px）";
            issues.Add(new ValidationIssue(s, "横幅", r.LineIndex, message));
        }
        return issues;
    }
}
