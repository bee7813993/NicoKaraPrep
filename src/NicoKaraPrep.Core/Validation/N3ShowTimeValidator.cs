using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Validation;

/// <summary>
/// 表示時刻のチェック（種類「表示時刻」）。書き出しと同じ表示時刻の計算（<see cref="N3ShowTimePlanner"/>）の結果で、
/// 前後のページの同じ段の行の組を調べる。
///   エラー: 前の行が次の行に場所を譲ってワイプの途中で消える（表示終了 &lt; 歌唱終了）。絵文字の分だけ遅らせる規則のオン・オフにかかわらず出す。
///   警告: 規則（オンのとき）で表示開始へ寄せる絵文字・＿の表示秒数が、下限（ワイプ前の表示時間と元の表示秒数の短い方）より短くなった。
///         規則は下限を守るので、表示時刻の手動指定（次の行の表示開始・前の行の表示終了）のときだけ起きる。
///         前のページに同じ段の行が無い行も、表示開始の手動指定で下限より短くなれば警告する。
/// 規則で解消できた組は出さない。<see cref="PageRowCollisionValidator"/>（計算で詰める前の、希望の表示区間どうしの重なり）とは別の種類。
/// </summary>
public static class N3ShowTimeValidator
{
    /// <summary>チェックの種類の名前。</summary>
    public const string Category = "表示時刻";

    /// <summary>
    /// 前後のページの同じ段の行の組を調べる。組は <paramref name="plans"/> のページと段（Planner が詰めた組と同じ）で決める。
    /// 結果の行（LineIndex）は次の行、関連する行（RelatedLineIndex）は前の行（ページ衝突チェックと同じ。
    /// 次の行の表示開始の手動指定だけが理由の警告では、前の行は関係しないので無し）。
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
        var paired = new HashSet<int>(); // 組の次の行として調べた行
        foreach (var (page, prevRows) in pages.OrderBy(p => p.Key))
        {
            if (!pages.TryGetValue(page + 1, out var nextRows)) continue;
            foreach (var (row, prev) in prevRows.OrderBy(r => r.Key))
            {
                if (!nextRows.TryGetValue(row, out int next)) continue;
                paired.Add(next);
                if (skipPairs?.Contains((prev, next)) == true) continue;
                if (CheckPair(doc, plans[prev], plans[next], page, row, settings) is { } issue) issues.Add(issue);
            }
        }

        // 組の無い行（前のページに同じ段の行が無い行）: 表示開始の手動指定で絵文字が下限より短くなる行
        if (settings.YieldsEmojiLead)
        {
            foreach (var plan in plans.Values.OrderBy(p => p.LineIndex))
            {
                if (!plan.BeginIsManual || paired.Contains(plan.LineIndex) || plan.LineIndex < 0 || plan.LineIndex >= doc.Lines.Count) continue;
                var below = BelowFloor(N3EmojiLead.Describe(doc.Lines[plan.LineIndex], plan.BeginMs, settings.LeadMatcher), settings.LeadMs);
                if (below.Count == 0) continue;
                issues.Add(new ValidationIssue(IssueSeverity.Warning, Category, plan.LineIndex,
                    $"{plan.LineIndex + 1}行目（ページ{plan.PageIndex + 1}）: 表示開始の手動指定のため、絵文字が {FormatShrinks(below)} 秒に縮みます"));
            }
        }
        return issues;
    }

    /// <summary>縮めた絵文字の秒数の説明（例: 「2.0→1.5」。違う値が複数あれば「・」でつなぐ）。無ければ空。</summary>
    public static string FormatShrinks(IEnumerable<(double FromSec, double ToSec)> shrinks) =>
        string.Join("・", shrinks.Select(s => $"{s.FromSec:0.0#}→{s.ToSec:0.0#}").Distinct());

    /// <summary>
    /// 縮めた絵文字（<see cref="N3EmojiLead.Describe"/>）のうち、表示秒数が下限（ワイプ前の表示時間と元の表示秒数の短い方）より
    /// 短くなったもの（表示開始を 10ms 単位に切り上げた分の差は数えない）。絵文字の分だけ遅らせる規則は下限を守るので、
    /// 表示時刻の手動指定のときだけ起きる。
    /// </summary>
    /// <param name="leadMs">ワイプ前の表示時間（ms）。</param>
    public static List<(double FromSec, double ToSec)> BelowFloor(IEnumerable<(double FromSec, double ToSec)> shrinks, int leadMs) =>
        shrinks.Where(x => Ms(x.ToSec) < Math.Min(leadMs, Ms(x.FromSec)) - 9).ToList();

    private static int Ms(double sec) => (int)Math.Round(sec * 1000);

    private static ValidationIssue? CheckPair(LyricsDocument doc, N3LinePlan prev, N3LinePlan next, int page, int row, N3ShowTimeSettings s)
    {
        // 書き出しで表示開始へ寄せて縮む次の行の絵文字（規則がオンのときだけ寄せる）
        var shrinks = s.YieldsEmojiLead
            ? N3EmojiLead.Describe(doc.Lines[next.LineIndex], next.BeginMs, s.LeadMatcher)
            : Array.Empty<(double FromSec, double ToSec)>();
        string where = $"{next.LineIndex + 1}行目（ページ{page + 1}→{page + 2}・{(s.AlignFromTop ? "上" : "下")}から{row}行目）";

        // エラー: 前の行がワイプの途中で消える
        if (N3ShowTimePlanner.SingEndMs(doc.Lines[prev.LineIndex]) is int prevLast && prev.EndMs < prevLast)
        {
            // 規則で絵文字を縮めても足りなかった組は、縮めた秒数を添える（手動指定で決まった組には添えない）
            string emoji = next.BeginIsManual || prev.EndIsManual ? "" : FormatShrinks(shrinks);
            string tried = emoji.Length > 0 ? $"絵文字を {emoji} 秒に縮めても、" : "";
            return new ValidationIssue(IssueSeverity.Error, Category, next.LineIndex,
                $"{where}: {tried}前の行（{prev.LineIndex + 1}行目）がワイプの途中（残り {(prevLast - prev.EndMs) / 1000.0:0.0#} 秒）で消えます",
                RelatedLineIndex: prev.LineIndex);
        }

        // 警告: 絵文字が下限より短くなった（表示時刻の手動指定のときだけ起きる）
        var below = BelowFloor(shrinks, s.LeadMs);
        if (below.Count > 0)
        {
            string cause = next.BeginIsManual ? "表示開始の手動指定のため、"
                : prev.EndIsManual ? $"前の行（{prev.LineIndex + 1}行目）の表示終了の手動指定のため、"
                : "";
            return new ValidationIssue(IssueSeverity.Warning, Category, next.LineIndex,
                $"{where}: {cause}絵文字が {FormatShrinks(below)} 秒に縮みます",
                RelatedLineIndex: next.BeginIsManual ? null : prev.LineIndex);
        }
        return null;
    }
}
