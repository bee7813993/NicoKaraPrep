using Microsoft.UI.Xaml.Media;

namespace NicoKaraPrep.App.ViewModels;

/// <summary>行に当たるフォント設定 1 つ（名前と、ワイプ後・前の文字の色見本）。</summary>
public sealed record LineFontRun(string Name, Brush? After, Brush? Before);

/// <summary>
/// 行リストの「フォント設定」欄の表示。行の中でフォント設定が切り替わるときは、最初と最後のものを「→」でつないで出す
/// （3 つ以上なら間を「…」にして、すべてはツールチップに出す）。
/// </summary>
public sealed record LineFontDisplay(bool IsVisible, LineFontRun First, LineFontRun Last, bool HasLast, string Arrow, bool IsManual, string Summary, string ToolTip)
{
    private static readonly LineFontRun EmptyRun = new("", null, null);

    /// <summary>空行など、出すものが無いとき。</summary>
    public static readonly LineFontDisplay None = new(false, EmptyRun, EmptyRun, false, "", false, "", "");

    /// <param name="runs">行に当たるフォント設定（文字の順。1 つ以上）。</param>
    /// <param name="manual">行の手動指定で行全体をそろえたか。</param>
    /// <param name="manualName">行の手動指定の名前（無ければ null）。manual が false なのに名前があるのは、書き出すフォント設定に無い名前。</param>
    /// <param name="charManual">文字ごとの手動指定が 1 文字以上に効いているか。</param>
    public static LineFontDisplay Create(IReadOnlyList<LineFontRun> runs, bool manual, string? manualName, bool charManual = false)
    {
        string summary = string.Join(" → ", runs.Select(r => r.Name));
        var tip = new List<string>
        {
            manual ? $"この行のフォント設定: {summary}（行ごとの手動指定）" : $"この行のフォント設定: {summary}",
        };
        if (!manual && manualName is { Length: > 0 } missing)
        {
            tip.Add($"⚠ 手動指定の「{missing}」は書き出すフォント設定に無いため使われません");
        }
        if (charManual) tip.Add("一部の文字は、文字ごとの手動指定のフォント設定です");
        tip.Add("ニコカラメーカー3 プロジェクトの書き出しと同じ決め方です（フォント設定と同じ名前の記号から先がそのフォント設定になり、次の行にも引き継ぎます）");
        return new LineFontDisplay(
            true,
            runs[0],
            runs[^1],
            runs.Count > 1,
            runs.Count > 2 ? "→ … →" : "→",
            manual,
            summary,
            string.Join("\n", tip));
    }
}
