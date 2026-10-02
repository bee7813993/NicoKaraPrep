using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Formats;

/// <summary>
/// 絵文字（または ＿ などのプレースホルダ）の先行タグ 1 つ分。
/// </summary>
/// <param name="UnitIndex">先行タグを持つ単位の添字（出現の中で最初にタグを持つ単位）。</param>
/// <param name="Ecs">先行タグ E（絵文字の開始。10ms 単位）。</param>
/// <param name="Tcs">
/// 基準時刻 T（10ms 単位。<see cref="EmojiTagger.RetagLine"/> と同じく、ブロックの後ろで最初にタグの付いた実文字の時刻。
/// 無ければ行末タグ。どちらも無ければ null）。
/// </param>
public readonly record struct LeadOccurrence(int UnitIndex, int Ecs, int? Tcs);

/// <summary>
/// 絵文字の先行を譲る規則（<see cref="N3ShowTimeSettings.EmojiLeadYield"/>。ニコカラメーカー3 には無い NicoKaraPrep の機能）の、
/// 歌詞の側の計算。
/// 前のページの同じ段と重なって表示が遅れた行は、絵文字の開始タグを表示開始まで寄せる（絵文字の表示秒数を縮める）。
/// 縮めるのはワイプ前の表示時間（表示秒数がそれより短い絵文字はその秒数）まで（<see cref="LatestBeginMs"/>。
/// 表示開始をそれより遅らせないのは <see cref="N3ShowTimePlanner"/> の側。表示時刻の手動指定のときだけ下限を割りうる）。
/// 寄せるのは書き出し用の写し（lrc・Raw・文字の時刻）と字幕のプレビューだけで、ユーザーの歌詞のタグは変えない。
/// 寄せる出現は「開始タグ E が表示開始（10ms 単位に切り上げ）より前、かつ基準時刻 T より前」のものだけで、
/// 新しい開始は min(T, 表示開始) にする（同時歌唱で E が T より後の出現や、T の無い出現は変えない）。
/// </summary>
public static class N3EmojiLead
{
    /// <summary>
    /// 行の本当の歌い出し（ms）: 寄せられる先行タグ（<see cref="CanClamp"/>。T があって E が T より前の出現の、最初のタグ）を除いた
    /// 行内のタグの最小（スペーサーと行末タグは含める）。
    /// T の無い出現のタグ、出現の中の 2 つ目以降のタグ、E ≧ T のタグは寄せないので、実のタグとして数える
    /// （E ≧ T のタグは T 以上なので最小は変わらない）。除くタグと寄せるタグをそろえて、寄せた後のタグが表示開始より前に残らないようにする。
    /// matcher が null・空なら <see cref="N3ShowTimePlanner.SingStartMs"/> と同じ。タグが無ければ null。
    /// </summary>
    public static int? RealStartMs(LyricsLine line, EmojiMatcher? matcher)
    {
        if (matcher is null || matcher.IsEmpty) return N3ShowTimePlanner.SingStartMs(line);
        var units = new HashSet<int>(FindLeads(line, matcher).Where(CanClamp).Select(l => l.UnitIndex));
        if (units.Count == 0) return N3ShowTimePlanner.SingStartMs(line);

        int? min = null;
        for (int i = 0; i < line.Chars.Count; i++)
        {
            if (units.Contains(i)) continue;
            if (line.Chars[i].TimeCs is int t) min = min is int m ? Math.Min(m, t) : t;
        }
        if (line.EndTimeCs is int e) min = min is int m2 ? Math.Min(m2, e) : e;
        return min * 10;
    }

    /// <summary>
    /// 行の絵文字・＿の先行タグを、出現ごとに左から返す（タグの無い出現は返さない）。
    /// 出現・ブロック（間がスペーサーだけの出現のまとまり）・基準時刻 T の求め方は <see cref="EmojiTagger.RetagLine"/> と同じ。
    /// 連続絵文字はそれぞれの出現の先頭（PerEmoji）、ブロックモードはブロックの先頭の出現だけがタグを持つので、
    /// どちらも「出現の中で最初にタグを持つ単位」で扱える。
    /// </summary>
    public static IReadOnlyList<LeadOccurrence> FindLeads(LyricsLine line, EmojiMatcher matcher)
    {
        var result = new List<LeadOccurrence>();
        if (matcher.IsEmpty) return result;
        var occurrences = matcher.FindOccurrences(line.Chars);
        if (occurrences.Count == 0) return result;
        var units = UnitIndexes(occurrences);

        int first = 0;
        while (first < occurrences.Count)
        {
            // 間がスペーサーだけの出現は 1 つのブロック（RetagLine はスペーサーを除いてから隣り合う出現をまとめる）
            int last = first;
            while (last + 1 < occurrences.Count && OnlySpacers(line, occurrences[last].EndExclusive, occurrences[last + 1].Start)) last++;

            int? baseCs = BaseTimeCs(line, occurrences[last].EndExclusive, units);
            for (int o = first; o <= last; o++)
            {
                var occ = occurrences[o];
                for (int k = occ.Start; k < occ.EndExclusive; k++)
                {
                    if (line.Chars[k].TimeCs is int e)
                    {
                        result.Add(new LeadOccurrence(k, e, baseCs));
                        break;
                    }
                }
            }
            first = last + 1;
        }
        return result;
    }

    /// <summary>
    /// 先行タグ 1 つの寄せ先（10ms 単位）。寄せないなら null（E' の計算はここだけで行う）。
    /// E が下限（表示開始を 10ms 単位に切り上げた値）より前で、寄せられる先行タグ（<see cref="CanClamp"/>）のときだけ、E' = min(T, 下限)。
    /// </summary>
    /// <param name="floorCs">寄せる下限（表示開始を 10ms 単位に切り上げた値）。</param>
    public static int? ClampedCs(LeadOccurrence lead, int floorCs)
    {
        if (lead.Ecs >= floorCs || !CanClamp(lead)) return null;
        return Math.Min(lead.Tcs!.Value, floorCs);
    }

    /// <summary>
    /// 寄せられる先行タグか（判定はここだけで行う）: T があって E が T より前のとき。
    /// E が T より前でない出現（同時歌唱など）や、T の無い出現は寄せない。<see cref="RealStartMs"/> が除くタグと
    /// <see cref="LatestBeginMs"/> が下限を守る出現もこれにそろえる。
    /// </summary>
    internal static bool CanClamp(LeadOccurrence lead) => lead.Tcs is int t && lead.Ecs < t;

    /// <summary>
    /// どの絵文字・＿も表示秒数の下限を割らない、最も遅い表示開始（ms）。寄せられる先行タグ（<see cref="CanClamp"/>）が無ければ null。
    /// 下限は出現ごとに、ワイプ前の表示時間と元の表示秒数（T − E）の短い方（F = min(ワイプ前, T − E)。表示秒数 1 秒の絵文字は縮めない）。
    /// 表示開始が T − F = max(T − ワイプ前, E) 以下なら、寄せても F を割らない（ワイプ前の表示時間が 10ms 単位なら、
    /// T − F も 10ms 単位なので、表示開始を 10ms 単位に切り上げて寄せても割らない）。
    /// </summary>
    /// <param name="leadMs">ワイプ前の表示時間（ms）。</param>
    public static int? LatestBeginMs(LyricsLine line, EmojiMatcher? matcher, int leadMs)
    {
        if (matcher is null || matcher.IsEmpty) return null;
        int? latest = null;
        foreach (var lead in FindLeads(line, matcher))
        {
            if (!CanClamp(lead)) continue;
            int begin = Math.Max(lead.Tcs!.Value * 10 - leadMs, lead.Ecs * 10);
            latest = latest is int l ? Math.Min(l, begin) : begin;
        }
        return latest;
    }

    /// <summary>
    /// 表示開始（ms）より前の絵文字の開始タグを表示開始へ寄せた行の写しを返す（寄せる所が無ければ元の行そのもの）。元の行は変えない。
    /// 表示開始は 10ms 単位に切り上げる（lrc のタグは 10ms 単位のため）。
    /// </summary>
    public static LyricsLine ClampLine(LyricsLine line, int floorMs, EmojiMatcher? matcher)
    {
        if (matcher is null || matcher.IsEmpty) return line;
        int floorCs = CeilCs(floorMs);
        LyricsLine? copy = null;
        foreach (var lead in FindLeads(line, matcher))
        {
            if (ClampedCs(lead, floorCs) is not int clamped) continue;
            copy ??= line.Clone();
            copy.Chars[lead.UnitIndex].TimeCs = clamped;
        }
        return copy ?? line;
    }

    /// <summary>
    /// 各行の絵文字の開始タグを、その行の表示開始（<paramref name="plans"/>）へ寄せたドキュメントの写しを返す
    /// （寄せる所が無ければ元のドキュメントそのもの）。元のドキュメントは変えない（写しは <see cref="LyricsDocument.Clone"/> の深い写し）。
    /// </summary>
    public static LyricsDocument ClampDocument(LyricsDocument doc, IReadOnlyDictionary<int, N3LinePlan> plans, EmojiMatcher? matcher)
    {
        if (matcher is null || matcher.IsEmpty) return doc;
        LyricsDocument? copy = null;
        for (int i = 0; i < doc.Lines.Count; i++)
        {
            if (!plans.TryGetValue(i, out var plan)) continue;
            var clamped = ClampLine(doc.Lines[i], plan.BeginMs, matcher);
            if (ReferenceEquals(clamped, doc.Lines[i])) continue;
            copy ??= doc.Clone();
            copy.Lines[i] = clamped;
        }
        return copy ?? doc;
    }

    /// <summary>
    /// 書き出すタブ 1 つ分の表示時刻と、絵文字の開始を寄せた歌詞を求める（書き出しの lrc と n3proj の行はどちらもこの結果から作る）。
    /// 表示時刻は元の歌詞で計算する（寄せた歌詞で計算し直すと、ページの表示開始が寄せた絵文字の分だけ変わるため）。
    /// 規則がオフ・対象の絵文字が無ければ、歌詞は元のドキュメントそのもの。同じ入力なら何度呼んでも同じ結果になる。
    /// </summary>
    public static (IReadOnlyDictionary<int, N3LinePlan> Plans, LyricsDocument Clamped) PrepareTab(LyricsDocument doc, N3ShowTimeSettings show)
    {
        var plans = N3ShowTimePlanner.Plan(doc, show);
        var clamped = show.YieldsEmojiLead ? ClampDocument(doc, plans, show.LeadMatcher) : doc;
        return (plans, clamped);
    }

    /// <summary>
    /// 画面の説明用: 表示開始（ms）へ寄せて縮む絵文字ごとの、元の表示秒数（T − E）と新しい表示秒数（T − E'）。縮まない絵文字は含めない。
    /// </summary>
    public static IReadOnlyList<(double FromSec, double ToSec)> Describe(LyricsLine line, int floorMs, EmojiMatcher? matcher)
    {
        var result = new List<(double, double)>();
        if (matcher is null || matcher.IsEmpty) return result;
        int floorCs = CeilCs(floorMs);
        foreach (var lead in FindLeads(line, matcher))
        {
            if (ClampedCs(lead, floorCs) is not int clamped || lead.Tcs is not int t) continue;
            result.Add(((t - lead.Ecs) / 100.0, (t - clamped) / 100.0));
        }
        return result;
    }

    /// <summary>ms を 10ms 単位へ切り上げる。</summary>
    private static int CeilCs(int ms) => ms > 0 ? (ms + 9) / 10 : ms / 10;

    /// <summary>行内で絵文字・＿の出現を構成している単位の添字。</summary>
    internal static HashSet<int> UnitIndexes(LyricsLine line, EmojiMatcher matcher) => UnitIndexes(matcher.FindOccurrences(line.Chars));

    private static HashSet<int> UnitIndexes(IEnumerable<EmojiMatcher.Occurrence> occurrences)
    {
        var set = new HashSet<int>();
        foreach (var occ in occurrences)
        {
            for (int k = occ.Start; k < occ.EndExclusive; k++) set.Add(k);
        }
        return set;
    }

    /// <summary>添字 from 〜 to（含まない）の単位がすべてスペーサーか（空の範囲も true）。</summary>
    private static bool OnlySpacers(LyricsLine line, int from, int to)
    {
        for (int k = from; k < to; k++)
        {
            if (!line.Chars[k].IsSpacer) return false;
        }
        return true;
    }

    /// <summary>基準時刻 T（10ms 単位）: blockEnd 以降で最初にタグの付いた実文字（絵文字・スペーサー以外）の時刻。無ければ行末タグ。</summary>
    private static int? BaseTimeCs(LyricsLine line, int blockEnd, HashSet<int> emojiUnits)
    {
        for (int k = blockEnd; k < line.Chars.Count; k++)
        {
            var c = line.Chars[k];
            if (c.IsSpacer || emojiUnits.Contains(k)) continue;
            if (c.TimeCs is int t) return t;
        }
        return line.EndTimeCs;
    }
}
