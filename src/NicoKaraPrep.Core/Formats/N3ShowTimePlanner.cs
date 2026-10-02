using NicoKaraPrep.Core.Model;
using NicoKaraPrep.Core.Validation;

namespace NicoKaraPrep.Core.Formats;

/// <summary>ニコカラメーカー3 向けの行表示時刻計算の設定（時刻はすべて ms）。</summary>
public sealed class N3ShowTimeSettings
{
    public PageSplitMode PageMode { get; set; } = PageSplitMode.EmptyLine;

    public int FixedLineCount { get; set; } = 2;

    /// <summary>表示開始 = ページ先頭タグの何 ms 前か（ニコカラメーカーの「ワイプ前の表示時間」）。</summary>
    public int LeadMs { get; set; } = 1500;

    /// <summary>表示終了 = 最終タグの何 ms 後か（「ワイプ後の表示時間」）。</summary>
    public int TailMs { get; set; } = 800;

    /// <summary>ページ区切りと段落区切りを分ける表示間隔（「歌詞の表示間隔」）。</summary>
    public int IntervalMs { get; set; } = 300;

    /// <summary>
    /// 隣接ページの行と詰めるとき、ワイプ前・ワイプ後の表示として最低限残す時間
    /// （ニコカラメーカーの ProtectTime。null = 自動: min(Lead, Tail) / 2）。
    /// </summary>
    public int? ProtectMs { get; set; }

    /// <summary>上段の行をページの最終行が消えるまで表示する（TopLong）。false は各行が自分の最終タグ後に消える（TopShort）。</summary>
    public bool TopLong { get; set; }

    /// <summary>ページ間の行対応付けを上からにする（false = 下から。ニコカラメーカーの既定）。</summary>
    public bool AlignFromTop { get; set; }

    /// <summary>1 行だけのページが上段へ昇格するのに必要な余裕（ms）。</summary>
    public int SingleLinePromoteGapMs { get; set; }

    /// <summary>
    /// 前のページの同じ段の行と重なるときは、次の行のワイプ前を絵文字・＿の先行タグではなく本当の歌い出しから測り、
    /// 必要な分だけ次の行の表示を遅らせる（ニコカラメーカー3 には無い NicoKaraPrep の機能。既定はオフ = ニコカラメーカー3 と同じ計算）。
    /// 遅らせた行の絵文字の開始は、書き出しで表示開始まで寄せる（<see cref="N3EmojiLead"/>）。
    /// </summary>
    public bool EmojiLeadYield { get; set; }

    /// <summary><see cref="EmojiLeadYield"/> で先行タグを除く絵文字（＿などのプレースホルダを含む）。null・空なら規則は働かない。</summary>
    public EmojiMatcher? LeadMatcher { get; set; }

    /// <summary><see cref="EmojiLeadYield"/> の規則が働くか（オンで、対象の絵文字がある）。</summary>
    public bool YieldsEmojiLead => EmojiLeadYield && LeadMatcher is { IsEmpty: false };

    public int EffectiveProtectMs => ProtectMs is int p && p >= 0 ? p : Math.Min(LeadMs, TailMs) / 2;

    /// <summary>前後の歌詞ブロックの間隔が段落区切り（間奏など）とみなせるか。</summary>
    public bool IsParagraphGap(int prevLastMs, int nextFirstMs) =>
        (nextFirstMs - LeadMs) - (prevLastMs + TailMs) >= IntervalMs;
}

/// <summary>行 1 つ分の表示時刻の計算結果（ms）。</summary>
/// <param name="LineIndex">行インデックス。</param>
/// <param name="BeginMs">表示開始。</param>
/// <param name="EndMs">表示終了。</param>
/// <param name="BeginIsManual">表示開始が手動指定か。</param>
/// <param name="EndIsManual">表示終了が手動指定か。</param>
/// <param name="PageIndex">ページ番号（0 始まり）。</param>
/// <param name="Row">画面位置（下から／上から k 行目、1 始まり）。</param>
/// <param name="Adjusted">前後ページとの衝突回避で自動値から動かされたか。</param>
/// <param name="EmojiYieldMs">
/// 絵文字の先行を譲る規則（<see cref="N3ShowTimeSettings.EmojiLeadYield"/>）で表示開始が遅れた量
/// （規則なしで計算したときの開始との差。0 = 遅れていない）。
/// </param>
public sealed record N3LinePlan(int LineIndex, int BeginMs, int EndMs, bool BeginIsManual, bool EndIsManual, int PageIndex, int Row, bool Adjusted, int EmojiYieldMs = 0);

/// <summary>
/// ニコカラメーカー3 の「自動で表示開始時刻・表示終了時刻を設定（上段歌詞を短めに／長めに表示）」に
/// 相当する計算。ページ内の行は同時に表示を開始し（ページ先頭タグ − ワイプ前）、各行は最終タグ＋ワイプ後で消える
/// （上段を長めに表示する場合はページの最終タグ＋ワイプ後）。隣接ページの同じ画面位置の行とは
/// <see cref="ResolvePair"/> の規則で間隔を空ける。手動指定の値は動かさない。
/// <see cref="N3ShowTimeSettings.EmojiLeadYield"/> がオンなら、同じ段の次の行のワイプ前だけを絵文字の先行を除いた歌い出しから測る
/// （<see cref="ResolvePairYieldingLead"/>。ページの表示開始・段の対応付けはニコカラメーカー3 と同じく絵文字の先行を含めて決める）。
/// </summary>
public static class N3ShowTimePlanner
{
    public static Dictionary<int, N3LinePlan> Plan(LyricsDocument doc, N3ShowTimeSettings s)
    {
        var pages = doc.GetPages(s.PageMode, s.FixedLineCount);
        var begins = new Dictionary<int, int>();
        var ends = new Dictionary<int, int>();
        var shortEnds = new Dictionary<int, int>();   // 上段を長めに表示しない場合の表示終了（自分の歌唱終了＋ワイプ後）
        var firstMs = new Dictionary<int, int>();     // 行の歌唱開始（行内のタグの最小。絵文字の先行タグを含む）
        var lastMs = new Dictionary<int, int>();      // 行の歌唱終了（行内のタグの最大）
        var manualBegin = new HashSet<int>();
        var manualEnd = new HashSet<int>();
        var adjusted = new HashSet<int>();
        var pageOf = new Dictionary<int, int>();
        var emojiYields = new Dictionary<int, int>(); // 絵文字の先行を譲る規則で表示開始が遅れた量

        // 1) 希望表示区間
        for (int pi = 0; pi < pages.Count; pi++)
        {
            var page = pages[pi];
            int? pageFirst = null, pageLast = null;
            foreach (int i in page)
            {
                var line = doc.Lines[i];
                if (SingStartMs(line) is int f)
                {
                    firstMs[i] = f;
                    pageFirst = pageFirst is int pf ? Math.Min(pf, f) : f;
                }
                if (SingEndMs(line) is int l)
                {
                    lastMs[i] = l;
                    pageLast = pageLast is int pl ? Math.Max(pl, l) : l;
                }
            }

            foreach (int i in page)
            {
                var line = doc.Lines[i];
                pageOf[i] = pi;
                if (!firstMs.ContainsKey(i) && !lastMs.ContainsKey(i) && line.ShowBeginCs is null && line.ShowEndCs is null)
                {
                    continue; // タグの無い行は表示時刻を決められない
                }

                int begin = (pageFirst ?? firstMs.GetValueOrDefault(i, lastMs.GetValueOrDefault(i))) - s.LeadMs;
                int ownLast = lastMs.GetValueOrDefault(i, firstMs.GetValueOrDefault(i));
                int end = (s.TopLong ? (pageLast ?? ownLast) : ownLast) + s.TailMs;
                int shortEnd = ownLast + s.TailMs;

                if (line.ShowBeginCs is int mb) { begin = mb * 10; manualBegin.Add(i); }
                if (line.ShowEndCs is int me) { end = me * 10; shortEnd = end; manualEnd.Add(i); }
                if (begin < 0) begin = 0;
                if (end < begin) end = begin;
                begins[i] = begin;
                ends[i] = end;
                shortEnds[i] = Math.Min(shortEnd, end);
            }
        }

        // 2) 画面位置の対応付けと、隣接ページ間の衝突回避
        int? DisplayEnd(int i) => ends.TryGetValue(i, out int e) ? e : null;
        int? DisplayStart(int i) => begins.TryGetValue(i, out int b) ? b : null;

        var rowMaps = new Dictionary<int, int>[pages.Count];
        for (int pi = 0; pi < pages.Count; pi++)
        {
            rowMaps[pi] = PageRowMap.Build(pages, pi, s.AlignFromTop, DisplayEnd, DisplayStart, s.SingleLinePromoteGapMs);
        }

        for (int p = 0; p + 1 < pages.Count; p++)
        {
            foreach (var (row, prev) in rowMaps[p])
            {
                if (!rowMaps[p + 1].TryGetValue(row, out int next)) continue;
                if (!ends.TryGetValue(prev, out int prevEnd) || !begins.TryGetValue(next, out int nextBegin)) continue;
                if (!lastMs.TryGetValue(prev, out int prevLast) || !firstMs.TryGetValue(next, out int nextFirst)) continue;

                int prevEndShort = shortEnds.GetValueOrDefault(prev, prevEnd);
                bool prevEndManual = manualEnd.Contains(prev);
                bool nextBeginManual = manualBegin.Contains(next);
                var (newEnd, newBegin) = ResolvePair(
                    prevEnd, prevEndShort, prevLast, prevEndManual,
                    nextBegin, nextFirst, nextBeginManual, s);
                if (YieldingFirstMs(doc.Lines[next], nextFirst, nextBeginManual, s) is int realFirst)
                {
                    // 絵文字の先行を譲る（規則なしの開始との差を、遅れた量として残す）
                    int plainBegin = newBegin;
                    (newEnd, newBegin) = ResolvePairYieldingLead(prevEnd, prevEndShort, prevLast, prevEndManual, nextBegin, realFirst, s);
                    if (newBegin > plainBegin) emojiYields[next] = newBegin - plainBegin;
                }
                if (newEnd != prevEnd)
                {
                    ends[prev] = newEnd;
                    adjusted.Add(prev);
                }
                if (newBegin != nextBegin)
                {
                    begins[next] = newBegin;
                    adjusted.Add(next);
                }
            }
        }

        // 3) 結果
        return BuildPlans(begins, ends, manualBegin, manualEnd, adjusted, pageOf, rowMaps, emojiYields);
    }

    /// <summary>
    /// 絵文字の先行を譲る規則が次の行に働くか（規則の判定はここだけで行う）。働くなら、次の行のワイプ前を測る本当の歌い出し（ms）を返す。
    /// 規則がオンで対象の絵文字があり、次の行の表示開始が手動指定でなく、絵文字・＿の先行タグを除いた歌い出しが行内の最小のタグより遅い行だけ。
    /// </summary>
    /// <param name="next">次のページの同じ段の行。</param>
    /// <param name="nextFirst">次の行の行内の最小のタグ（ms。絵文字の先行タグを含む）。</param>
    private static int? YieldingFirstMs(LyricsLine next, int nextFirst, bool nextBeginManual, N3ShowTimeSettings s)
    {
        if (!s.YieldsEmojiLead || nextBeginManual) return null;
        return N3EmojiLead.RealStartMs(next, s.LeadMatcher) is int real && real > nextFirst ? real : null;
    }

    /// <summary>
    /// 絵文字の先行を譲る規則（<see cref="N3ShowTimeSettings.EmojiLeadYield"/>。ニコカラメーカー3 には無い）で、
    /// 同じ段の前後の行の表示終了・開始を決める。次の行のワイプ前を絵文字・＿の先行タグではなく本当の歌い出しから測り、
    /// 必要な分だけ次の行を遅らせる（足りなければ <see cref="ResolvePair"/> の手順どおりに詰める）。
    /// 上段を長めに表示する場合の延長分も、絵文字の先行を譲り切る（表示開始 = 本当の歌い出し − ワイプ前）までは削らない。
    /// 次の行の表示開始が手動指定の組には使わない（<see cref="YieldingFirstMs"/>）。
    /// </summary>
    /// <param name="realFirst">次の行の本当の歌い出し（絵文字・＿の先行タグを除いた行内の最小。<see cref="N3EmojiLead.RealStartMs"/>）。</param>
    internal static (int PrevEnd, int NextBegin) ResolvePairYieldingLead(
        int prevEnd, int prevEndShort, int prevLast, bool prevEndManual,
        int nextBegin, int realFirst,
        N3ShowTimeSettings s)
    {
        int interval = Math.Max(0, s.IntervalMs);
        int yieldBegin = Math.Max(nextBegin, realFirst - s.LeadMs); // 絵文字の先行を譲り切ったときの表示開始
        if (s.TopLong && !prevEndManual && prevEnd > prevEndShort)
        {
            prevEnd = Math.Max(prevEndShort, Math.Min(prevEnd, yieldBegin - interval));
            prevEndShort = prevEnd; // 延長分はここで決めた（ResolvePair では削らない）
        }
        return ResolvePair(prevEnd, prevEndShort, prevLast, prevEndManual, nextBegin, realFirst, false, s);
    }

    /// <summary>
    /// 同じ画面位置に続けて表示される 2 行（前ページの行 → 次ページの行）の表示終了・開始を決める。
    /// ニコカラメーカー3 が自動設定した実プロジェクトの値の解析で確認した規則:
    /// 次の行は「前の行の表示終了＋表示間隔」より前には出さない。それでは次の行のワイプ前表示
    /// （歌い出し − 表示開始）が足りない場合は、次の順に詰める。
    ///   (1) 歌詞の表示間隔（1/4 は残す）　(2) 前の行のワイプ後表示（保護時間は残す）
    ///   (3) 次の行のワイプ前表示（保護時間は残す）　(3') 残りの表示間隔
    ///   (4) 前の行のワイプ後表示（保護時間も残さない）　(5) 次の行のワイプ前表示（保護時間も残さない）
    ///   (6) 次の行の歌い出しで前の行の表示を終える
    /// （ニコカラメーカー3 のヘルプ「自動で表示開始時刻・表示終了時刻を設定」に記載の順序。
    /// 　表示間隔の 1/4 を手順 3 まで残すのは実プロジェクトの値から確認した挙動）
    /// 手動指定された側は動かさず、もう一方だけで調整する。
    /// </summary>
    /// <param name="prevEnd">前の行の表示終了（希望値。上段を長めに表示する場合はページの最後まで延長した値）。</param>
    /// <param name="prevEndShort">前の行の延長しない表示終了（自分の歌唱終了＋ワイプ後）。延長分は表示間隔を保ったまま最初に削る。</param>
    /// <param name="prevLast">前の行の歌唱終了（ワイプ終了）。</param>
    /// <param name="nextBegin">次の行の表示開始（希望値 = ページ先頭 − ワイプ前）。</param>
    /// <param name="nextFirst">次の行の先頭タグ（ワイプ開始。絵文字の先行タグを含む）。</param>
    internal static (int PrevEnd, int NextBegin) ResolvePair(
        int prevEnd, int prevEndShort, int prevLast, bool prevEndManual,
        int nextBegin, int nextFirst, bool nextBeginManual,
        N3ShowTimeSettings s)
    {
        int interval = Math.Max(0, s.IntervalMs);
        int minGap = interval / 4;
        int protect = s.EffectiveProtectMs;
        int lead = s.LeadMs;

        if (prevEnd + interval <= nextBegin) return (prevEnd, nextBegin); // 間隔が十分
        if (prevEndManual && nextBeginManual) return (prevEnd, nextBegin);

        // 上段を長めに表示する場合の延長分は、表示間隔を保ったまま最初に削る
        if (!prevEndManual && prevEnd > prevEndShort)
        {
            prevEnd = Math.Max(prevEndShort, Math.Min(prevEnd, nextBegin - interval));
            if (prevEnd + interval <= nextBegin) return (prevEnd, nextBegin);
        }

        if (nextBeginManual)
        {
            // 次の行の開始は動かせない: 前の行の終了だけで間隔を空ける（ワイプ後表示は保護時間まで）
            int pe = prevEnd;
            if (pe + minGap > nextBegin) pe = Math.Max(nextBegin - minGap, prevLast + protect);
            if (pe > nextBegin) pe = Math.Max(nextBegin, prevLast);
            return (Math.Min(pe, prevEnd), nextBegin);
        }

        if (prevEndManual)
        {
            // 前の行の終了は動かせない: 次の行の開始だけで調整する（ワイプ前表示は保護時間まで）
            int nb = prevEnd + interval;
            if (nb > nextFirst - lead) nb = Math.Max(nextFirst - lead, prevEnd + minGap);
            if (nb > nextFirst - protect) nb = Math.Max(nextFirst - protect, prevEnd);
            return (prevEnd, Math.Max(nb, nextBegin));
        }

        // (0) 次の行を「前の行の終了＋表示間隔」まで遅らせるだけで足りる
        int desired = prevEnd + interval;
        if (desired <= nextFirst - lead) return (prevEnd, desired);

        // (1) 表示間隔を詰める（1/4 まで）。次の行はワイプ前表示を確保した位置から
        int begin = nextFirst - lead;
        if (prevEnd + minGap <= begin) return (prevEnd, Math.Max(begin, nextBegin));

        // (2) 前の行のワイプ後表示を保護時間まで縮める
        int end = begin - minGap;
        if (end >= prevLast + protect) return (Math.Min(end, prevEnd), Math.Max(begin, nextBegin));

        // (3) 次の行のワイプ前表示を保護時間まで縮める
        end = prevLast + protect;
        begin = end + minGap;
        if (nextFirst - begin >= protect) return (Math.Min(end, prevEnd), Math.Max(begin, nextBegin));

        int window = nextFirst - prevLast;

        // (3') 残りの表示間隔を 0 まで詰める（ワイプ前後とも保護時間は残す）
        if (window >= 2 * protect)
        {
            return (Math.Min(prevLast + protect, prevEnd), Math.Max(nextFirst - protect, nextBegin));
        }

        // (4) 前の行のワイプ後表示を 0 まで（次の行のワイプ前表示は保護時間を残す）
        if (window >= protect)
        {
            int meet = nextFirst - protect;
            return (Math.Min(meet, prevEnd), Math.Max(meet, nextBegin));
        }

        // (5) 次の行のワイプ前表示も 0 まで（前の行のワイプ終了と同時に切り替える）
        if (window >= 0)
        {
            return (Math.Min(prevLast, prevEnd), Math.Max(prevLast, nextBegin));
        }

        // (6) 次の行の歌い出しで前の行の表示を終える（前の行はワイプ途中で消える。ニコカラメーカーでも警告になる）
        return (Math.Min(nextFirst, prevEnd), Math.Max(nextFirst, nextBegin));
    }

    /// <summary>
    /// 行の歌唱開始（ms）。行内のタイムタグの最小値（同時歌唱などでタグが巻き戻る行にも対応）。
    /// ニコカラメーカーの自動表示時刻設定は行内で最も早い／遅い時刻を基準にする。
    /// </summary>
    public static int? SingStartMs(LyricsLine line)
    {
        int? min = null;
        foreach (var c in line.Chars)
        {
            if (c.TimeCs is int t) min = min is int m ? Math.Min(m, t) : t;
        }
        if (line.EndTimeCs is int e) min = min is int m2 ? Math.Min(m2, e) : e;
        return min * 10;
    }

    /// <summary>行の歌唱終了（ms）。行内のタイムタグの最大値（行末より行途中の文字の方が遅い場合はそちら）。</summary>
    public static int? SingEndMs(LyricsLine line)
    {
        int? max = null;
        foreach (var c in line.Chars)
        {
            if (c.TimeCs is int t) max = max is int m ? Math.Max(m, t) : t;
        }
        if (line.EndTimeCs is int e) max = max is int m2 ? Math.Max(m2, e) : e;
        return max * 10;
    }

    /// <summary>解析用（実プロジェクトとの照合）に <see cref="ResolvePair"/> を公開する。</summary>
    public static (int PrevEnd, int NextBegin) ResolvePairForAnalysis(
        int prevEnd, int prevEndShort, int prevLast, bool prevEndManual, int nextBegin, int nextFirst, bool nextBeginManual, N3ShowTimeSettings s) =>
        ResolvePair(prevEnd, prevEndShort, prevLast, prevEndManual, nextBegin, nextFirst, nextBeginManual, s);

    private static Dictionary<int, N3LinePlan> BuildPlans(
        Dictionary<int, int> begins,
        Dictionary<int, int> ends,
        HashSet<int> manualBegin,
        HashSet<int> manualEnd,
        HashSet<int> adjusted,
        Dictionary<int, int> pageOf,
        Dictionary<int, int>[] rowMaps,
        Dictionary<int, int> emojiYields)
    {
        var rowOf = new Dictionary<int, int>();
        foreach (var map in rowMaps)
        {
            foreach (var (row, line) in map) rowOf[line] = row;
        }

        var plans = new Dictionary<int, N3LinePlan>();
        foreach (var (i, begin) in begins)
        {
            plans[i] = new N3LinePlan(i, begin, ends[i], manualBegin.Contains(i), manualEnd.Contains(i),
                pageOf.GetValueOrDefault(i), rowOf.GetValueOrDefault(i), adjusted.Contains(i), emojiYields.GetValueOrDefault(i));
        }
        return plans;
    }
}
