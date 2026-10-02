using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Validation;

/// <summary>
/// 表示時刻のチェック（種類「表示時刻」）。書き出しと同じ表示時刻の計算（<see cref="N3ShowTimePlanner"/>）の結果で、
/// 前後のページの同じ段の行の組を調べる。
///   エラー: 前の行が次の行に場所を譲ってワイプの途中で消える（表示終了 &lt; 歌唱終了）。絵文字の分だけ遅らせる規則のオン・オフにかかわらず出す。
///   警告: 規則で絵文字を縮めても足りず、絵文字の表示秒数がワイプ前の表示時間より短くなった。
/// 規則で解消できた組は出さない。<see cref="PageRowCollisionValidator"/>（計算で詰める前の、希望の表示区間どうしの重なり）とは別の種類。
/// </summary>
public static class N3ShowTimeValidator
{
    /// <summary>チェックの種類の名前。</summary>
    public const string Category = "表示時刻";

    /// <summary>
    /// 前後のページの同じ段の行の組を調べる。組は <paramref name="plans"/> のページと段（Planner が詰めた組と同じ）で決める。
    /// 結果の行（LineIndex）は次の行、関連する行（RelatedLineIndex）は前の行（ページ衝突チェックと同じ）。
    /// </summary>
    /// <param name="plans"><see cref="N3ShowTimePlanner.Plan"/> の結果（<paramref name="doc"/> と <paramref name="settings"/> で計算したもの）。</param>
    /// <param name="skipPairs">出さない組（前の行, 次の行）。ページ衝突チェックのエラーが出ている組など。</param>
    public static List<ValidationIssue> Validate(
        LyricsDocument doc,
        IReadOnlyDictionary<int, N3LinePlan> plans,
        N3ShowTimeSettings settings,
        IReadOnlySet<(int Prev, int Next)>? skipPairs = null)
    {
        // ページ → 段 → 行
        var pages = new Dictionary<int, Dictionary<int, int>>();
        foreach (var plan in plans.Values)
        {
            if (plan.Row <= 0 || plan.LineIndex < 0 || plan.LineIndex >= doc.Lines.Count) continue;
            if (!pages.TryGetValue(plan.PageIndex, out var rows)) pages[plan.PageIndex] = rows = new Dictionary<int, int>();
            rows[plan.Row] = plan.LineIndex;
        }

        var issues = new List<ValidationIssue>();
        foreach (var (page, prevRows) in pages.OrderBy(p => p.Key))
        {
            if (!pages.TryGetValue(page + 1, out var nextRows)) continue;
            foreach (var (row, prev) in prevRows.OrderBy(r => r.Key))
            {
                if (!nextRows.TryGetValue(row, out int next)) continue;
                if (skipPairs?.Contains((prev, next)) == true) continue;
                if (CheckPair(doc, plans[prev], plans[next], page, row, settings) is { } issue) issues.Add(issue);
            }
        }
        return issues;
    }

    /// <summary>縮めた絵文字の秒数の説明（例: 「2.0→1.5」。違う値が複数あれば「・」でつなぐ）。無ければ空。</summary>
    public static string FormatShrinks(IEnumerable<(double FromSec, double ToSec)> shrinks) =>
        string.Join("・", shrinks.Select(s => $"{s.FromSec:0.0#}→{s.ToSec:0.0#}").Distinct());

    private static ValidationIssue? CheckPair(LyricsDocument doc, N3LinePlan prev, N3LinePlan next, int page, int row, N3ShowTimeSettings s)
    {
        // 規則で縮める次の行の絵文字（表示開始の手動指定に合わせて縮めるものは、規則のせいではないので数えない）
        var shrinks = s.YieldsEmojiLead && !next.BeginIsManual
            ? N3EmojiLead.Describe(doc.Lines[next.LineIndex], next.BeginMs, s.LeadMatcher)
            : Array.Empty<(double FromSec, double ToSec)>();
        string where = $"{next.LineIndex + 1}行目（ページ{page + 1}→{page + 2}・{(s.AlignFromTop ? "上" : "下")}から{row}行目）";

        // エラー: 前の行がワイプの途中で消える
        if (N3ShowTimePlanner.SingEndMs(doc.Lines[prev.LineIndex]) is int prevLast && prev.EndMs < prevLast)
        {
            string emoji = FormatShrinks(shrinks);
            string tried = emoji.Length > 0 ? $"絵文字を {emoji} 秒に縮めても、" : "";
            return new ValidationIssue(IssueSeverity.Error, Category, next.LineIndex,
                $"{where}: {tried}前の行（{prev.LineIndex + 1}行目）がワイプの途中（残り {(prevLast - prev.EndMs) / 1000.0:0.0#} 秒）で消えます",
                RelatedLineIndex: prev.LineIndex);
        }

        // 警告: 絵文字を縮めても足りず、ワイプ前の表示時間より短くなった（表示開始を 10ms 単位に切り上げた分の差は数えない）
        var tooShort = shrinks.Where(x => (int)Math.Round(x.ToSec * 1000) < s.LeadMs - 9).ToList();
        if (tooShort.Count > 0)
        {
            return new ValidationIssue(IssueSeverity.Warning, Category, next.LineIndex,
                $"{where}: 絵文字を {FormatShrinks(tooShort)} 秒に縮めても前の行（{prev.LineIndex + 1}行目）と重なります",
                RelatedLineIndex: prev.LineIndex);
        }
        return null;
    }
}
