using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Validation;

/// <summary>
/// 表示時刻のチェック（種類「表示時刻」）。書き出しと同じ表示時刻（行に持たせた値と自動計算。<see cref="N3ShowTimePlanner"/>）で調べる。
///   エラー: 前のページの同じ段の行が、次の行に場所を譲ってワイプの途中で消える（表示終了 &lt; 歌唱終了）。
///           絵文字の分だけ遅らせる規則のオン・オフにかかわらず出す。
///           行に持たせた表示終了が歌い終わりより前の行も出す（組の無い行・自動調整の後にタイムタグを直した行など）。
///   警告: 行に持たせた表示開始が、歌い出し（絵文字の分だけ遅らせる規則がオンなら、絵文字・＿の先行タグを除く）より後の行。
///   警告: 規則（オンのとき）で表示開始へ寄せる絵文字・＿の表示秒数が、下限（ワイプ前の表示時間と元の表示秒数の短い方）より短くなった。
///         規則が決めた値（自動で計算した値と、自動調整の値でいま実行し直しても同じもの）では、前の行をワイプの最後まで見せるときだけ起きる。
///         手で指定した表示時刻（次の行の表示開始・前の行の表示終了）でも起きる。読み込んだ値と、自動調整の後にタイムタグを直して
///         古くなった値では、絵文字は書き出しで自動で縮めるだけで知らせない。
///         前のページに同じ段の行が無い行も、表示開始の手動指定で下限より短くなれば警告する。
/// 規則で解消できた組は出さない。<see cref="PageRowCollisionValidator"/>（計算で詰める前の、希望の表示区間どうしの重なり）とは別の種類。
/// </summary>
public static class N3ShowTimeValidator
{
    /// <summary>チェックの種類の名前。</summary>
    public const string Category = "表示時刻";

    /// <summary>
    /// 前後のページの同じ段の行の組と、表示時刻を持たせた行を調べる。組は <paramref name="plans"/> のページと段（Planner が詰めた組と同じ）で決める。
    /// 組の結果の行（LineIndex）は次の行、関連する行（RelatedLineIndex）は前の行（ページ衝突チェックと同じ。
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

        // 自動調整の値が、いま実行し直しても同じか（規則が決めた値か）を見るための計算（自動調整の値・読み込んだ値があるときだけ）
        var fresh = N3ShowTimeAdjuster.HasRecomputable(doc) ? N3ShowTimeAdjuster.PlanFresh(doc, settings) : null;

        var issues = new List<ValidationIssue>();

        // 行に持たせた表示開始が歌い出しより後の行（その行の絵文字の下限の警告は重ねて出さない）
        var lateBegin = new HashSet<int>();
        foreach (var plan in plans.Values.OrderBy(p => p.LineIndex))
        {
            int i = plan.LineIndex;
            if (i < 0 || i >= doc.Lines.Count || doc.Lines[i].ShowBeginCs is null) continue;
            if (SingingStartMs(doc.Lines[i], settings) is not int start || plan.BeginMs <= start) continue;
            lateBegin.Add(i);
            issues.Add(new ValidationIssue(IssueSeverity.Warning, Category, i,
                $"{i + 1}行目（ページ{plan.PageIndex + 1}）: 表示開始 {Format(plan.BeginMs)} が歌い出し {Format(start)} より後です（ワイプが始まってから行が出ます）"));
        }

        var paired = new HashSet<int>();       // 組の次の行として調べた行
        var prevCovered = new HashSet<int>();  // 組の前の行としてワイプの途中で消えるエラーを出した行・出さない組の前の行
        foreach (var (page, prevRows) in pages.OrderBy(p => p.Key))
        {
            if (!pages.TryGetValue(page + 1, out var nextRows)) continue;
            foreach (var (row, prev) in prevRows.OrderBy(r => r.Key))
            {
                if (!nextRows.TryGetValue(row, out int next)) continue;
                paired.Add(next);
                if (skipPairs?.Contains((prev, next)) == true)
                {
                    prevCovered.Add(prev);
                    continue;
                }
                if (CheckPair(doc, plans[prev], plans[next], page, row, settings, fresh, lateBegin.Contains(next)) is not { } issue) continue;
                issues.Add(issue);
                if (issue.Severity == IssueSeverity.Error) prevCovered.Add(prev);
            }
        }

        foreach (var plan in plans.Values.OrderBy(p => p.LineIndex))
        {
            int i = plan.LineIndex;
            if (i < 0 || i >= doc.Lines.Count) continue;
            var line = doc.Lines[i];

            // 行に持たせた表示終了が歌い終わりより前の行（組のエラーで知らせた前の行は除く）
            if (line.ShowEndCs is not null && !prevCovered.Contains(i) &&
                N3ShowTimePlanner.SingEndMs(line) is int last && plan.EndMs < last)
            {
                issues.Add(new ValidationIssue(IssueSeverity.Error, Category, i,
                    $"{i + 1}行目（ページ{plan.PageIndex + 1}）: 表示終了 {Format(plan.EndMs)} が歌い終わり {Format(last)} より前です（ワイプの途中で消えます）"));
            }

            // 組の無い行（前のページに同じ段の行が無い行）: 表示開始の手動指定で絵文字が下限より短くなる行
            if (settings.YieldsEmojiLead && line.HasManualShowBegin && !paired.Contains(i) && !lateBegin.Contains(i))
            {
                var below = BelowFloor(N3EmojiLead.Describe(line, plan.BeginMs, settings.LeadMatcher), settings.LeadMs);
                if (below.Count > 0)
                {
                    issues.Add(new ValidationIssue(IssueSeverity.Warning, Category, i,
                        $"{i + 1}行目（ページ{plan.PageIndex + 1}）: 表示開始の手動指定のため、絵文字が {FormatShrinks(below)} 秒に縮みます"));
                }
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
    /// 前の行をワイプの最後まで見せるときと、表示時刻を行に持たせたとき（手動指定・読み込み・古くなった自動調整の値）だけ起きる。
    /// </summary>
    /// <param name="leadMs">ワイプ前の表示時間（ms）。</param>
    public static List<(double FromSec, double ToSec)> BelowFloor(IEnumerable<(double FromSec, double ToSec)> shrinks, int leadMs) =>
        shrinks.Where(x => Ms(x.ToSec) < Math.Min(leadMs, Ms(x.FromSec)) - 9).ToList();

    /// <summary>
    /// 行の歌い出し（ms）。絵文字の分だけ遅らせる規則がオンなら絵文字・＿の先行タグを除く（書き出しでは表示開始まで寄せるため）。
    /// オフなら行内のタグの最小（ニコカラメーカー3 と同じ）。
    /// </summary>
    private static int? SingingStartMs(LyricsLine line, N3ShowTimeSettings s) =>
        s.YieldsEmojiLead ? N3EmojiLead.RealStartMs(line, s.LeadMatcher) : N3ShowTimePlanner.SingStartMs(line);

    /// <summary>
    /// 規則が決めた値か: 行に持たせていない（自動で計算した）値か、自動調整の値で、いま実行し直しても同じ値（5ms 以内）。
    /// 手で指定した値・読み込んだ値・自動調整の後に歌詞や設定が変わって古くなった値は false。
    /// </summary>
    private static bool ByRule(int? storedCs, ShowTimeOrigin origin, int planMs, int? freshMs) =>
        storedCs is null || (origin == ShowTimeOrigin.Auto && freshMs is int f && Math.Abs(f - planMs) <= 5);

    private static int Ms(double sec) => (int)Math.Round(sec * 1000);

    private static string Format(int ms) => TimeTag.Format((int)Math.Round(ms / 10.0, MidpointRounding.AwayFromZero));

    private static ValidationIssue? CheckPair(
        LyricsDocument doc, N3LinePlan prev, N3LinePlan next, int page, int row, N3ShowTimeSettings s,
        IReadOnlyDictionary<int, N3LinePlan>? fresh, bool skipEmojiWarning)
    {
        var prevLine = doc.Lines[prev.LineIndex];
        var nextLine = doc.Lines[next.LineIndex];
        // 書き出しで表示開始へ寄せて縮む次の行の絵文字（規則がオンのときだけ寄せる）
        var shrinks = s.YieldsEmojiLead
            ? N3EmojiLead.Describe(nextLine, next.BeginMs, s.LeadMatcher)
            : Array.Empty<(double FromSec, double ToSec)>();
        string where = $"{next.LineIndex + 1}行目（ページ{page + 1}→{page + 2}・{(s.AlignFromTop ? "上" : "下")}から{row}行目）";
        N3LinePlan? freshNext = null, freshPrev = null;
        fresh?.TryGetValue(next.LineIndex, out freshNext);
        fresh?.TryGetValue(prev.LineIndex, out freshPrev);
        bool beginByRule = ByRule(nextLine.ShowBeginCs, nextLine.ShowBeginOrigin, next.BeginMs, freshNext?.BeginMs);
        bool endByRule = ByRule(prevLine.ShowEndCs, prevLine.ShowEndOrigin, prev.EndMs, freshPrev?.EndMs);

        // エラー: 前の行がワイプの途中で消える
        if (N3ShowTimePlanner.SingEndMs(prevLine) is int prevLast && prev.EndMs < prevLast)
        {
            // 規則で絵文字を縮めても足りなかった組は、縮めた秒数を添える（手動指定・読み込んだ値などで決まった組には添えない）
            string emoji = beginByRule && endByRule ? FormatShrinks(shrinks) : "";
            string tried = emoji.Length > 0 ? $"絵文字を {emoji} 秒に縮めても、" : "";
            return new ValidationIssue(IssueSeverity.Error, Category, next.LineIndex,
                $"{where}: {tried}前の行（{prev.LineIndex + 1}行目）がワイプの途中（残り {(prevLast - prev.EndMs) / 1000.0:0.0#} 秒）で消えます",
                RelatedLineIndex: prev.LineIndex);
        }

        // 警告: 絵文字が下限より短くなった（前の行をワイプの最後まで見せるときと、表示時刻の手動指定のときだけ知らせる）
        if (skipEmojiWarning) return null;
        var below = BelowFloor(shrinks, s.LeadMs);
        if (below.Count == 0) return null;
        string? cause = nextLine.HasManualShowBegin ? "表示開始の手動指定のため、"
            : prevLine.HasManualShowEnd ? $"前の行（{prev.LineIndex + 1}行目）の表示終了の手動指定のため、"
            : beginByRule && endByRule ? $"前の行（{prev.LineIndex + 1}行目）をワイプの最後まで見せるため、"
            : null; // 読み込んだ値・古くなった自動調整の値: 絵文字は書き出しで自動で縮めるだけ
        if (cause is null) return null;
        return new ValidationIssue(IssueSeverity.Warning, Category, next.LineIndex,
            $"{where}: {cause}絵文字が {FormatShrinks(below)} 秒に縮みます",
            RelatedLineIndex: nextLine.HasManualShowBegin ? null : prev.LineIndex);
    }
}
