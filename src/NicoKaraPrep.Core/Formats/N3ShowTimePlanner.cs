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

    /// <summary>前ページの行を短縮するとき最終タグ後に最低限残す時間（null = 自動: min(Lead, Tail) / 2）。</summary>
    public int? ProtectMs { get; set; }

    /// <summary>上段の行をページの最終行が消えるまで表示する（TopLong）。false は各行が自分の最終タグ後に消える（TopShort）。</summary>
    public bool TopLong { get; set; }

    /// <summary>ページ間の行対応付けを上からにする（false = 下から。ニコカラメーカーの既定）。</summary>
    public bool AlignFromTop { get; set; }

    /// <summary>1 行だけのページが上段へ昇格するのに必要な余裕（ms）。</summary>
    public int SingleLinePromoteGapMs { get; set; }

    /// <summary>絵文字など「歌唱開始とみなさない」文字の判定（衝突時に表示開始を遅らせる上限の計算に使う）。</summary>
    public Func<CharUnit, bool>? ExcludeChar { get; set; }

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
public sealed record N3LinePlan(int LineIndex, int BeginMs, int EndMs, bool BeginIsManual, bool EndIsManual, int PageIndex, int Row, bool Adjusted);

/// <summary>
/// ニコカラメーカー3 の「自動で表示開始時刻・表示終了時刻を設定（上段歌詞を短めに／長めに表示）」に
/// 相当する計算。ページ内の行は同時に表示を開始し、各行は最終タグの後に消える。
/// 隣接ページの同じ画面位置の行と重なる場合は、前の行の表示終了を短縮し（最終タグ＋保護時間まで）、
/// それでも重なる場合は次の行の表示開始を遅らせる（歌唱開始まで）。手動指定の値は動かさない。
/// </summary>
public static class N3ShowTimePlanner
{
    public static Dictionary<int, N3LinePlan> Plan(LyricsDocument doc, N3ShowTimeSettings s)
    {
        var pages = doc.GetPages(s.PageMode, s.FixedLineCount);
        var begins = new Dictionary<int, int>();
        var ends = new Dictionary<int, int>();
        var firstMs = new Dictionary<int, int>();     // 行の先頭タグ（絵文字含む）
        var lastMs = new Dictionary<int, int>();      // 行の最終タグ
        var singFirstMs = new Dictionary<int, int>(); // 歌唱開始（絵文字除く）
        var manualBegin = new HashSet<int>();
        var manualEnd = new HashSet<int>();
        var adjusted = new HashSet<int>();
        var pageOf = new Dictionary<int, int>();

        // 1) 希望表示区間
        for (int pi = 0; pi < pages.Count; pi++)
        {
            var page = pages[pi];
            int? pageFirst = null, pageLast = null;
            foreach (int i in page)
            {
                var line = doc.Lines[i];
                if (line.GetFirstTimeCs() is int f)
                {
                    firstMs[i] = f * 10;
                    pageFirst = pageFirst is int pf ? Math.Min(pf, f * 10) : f * 10;
                }
                if (line.GetLastTimeCs() is int l)
                {
                    lastMs[i] = l * 10;
                    pageLast = pageLast is int pl ? Math.Max(pl, l * 10) : l * 10;
                }
                if (line.GetFirstTimeCs(s.ExcludeChar) is int sf) singFirstMs[i] = sf * 10;
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

                if (line.ShowBeginCs is int mb) { begin = mb * 10; manualBegin.Add(i); }
                if (line.ShowEndCs is int me) { end = me * 10; manualEnd.Add(i); }
                if (begin < 0) begin = 0;
                if (end < begin) end = begin;
                begins[i] = begin;
                ends[i] = end;
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

        int protect = s.EffectiveProtectMs;
        for (int p = 0; p + 1 < pages.Count; p++)
        {
            foreach (var (row, prev) in rowMaps[p])
            {
                if (!rowMaps[p + 1].TryGetValue(row, out int next)) continue;
                if (!ends.TryGetValue(prev, out int prevEnd) || !begins.TryGetValue(next, out int nextBegin)) continue;
                if (prevEnd <= nextBegin) continue;

                // (a) 前の行の表示終了を短縮（最終タグ＋保護時間より前には縮めない）
                if (!manualEnd.Contains(prev) && lastMs.TryGetValue(prev, out int prevLast))
                {
                    int floor = prevLast + protect;
                    int newEnd = Math.Max(nextBegin, floor);
                    if (newEnd < prevEnd)
                    {
                        ends[prev] = newEnd;
                        prevEnd = newEnd;
                        adjusted.Add(prev);
                    }
                }

                // (b) それでも重なるなら次の行の表示開始を遅らせる（歌唱開始まで）
                if (prevEnd > nextBegin && !manualBegin.Contains(next))
                {
                    int cap = singFirstMs.TryGetValue(next, out int sf) ? sf : firstMs.GetValueOrDefault(next, prevEnd);
                    int newBegin = Math.Min(prevEnd, cap);
                    if (newBegin > nextBegin)
                    {
                        begins[next] = newBegin;
                        adjusted.Add(next);
                    }
                }
            }
        }

        // 3) 結果
        var rowOf = new Dictionary<int, int>();
        foreach (var map in rowMaps)
        {
            foreach (var (row, line) in map) rowOf[line] = row;
        }

        var plans = new Dictionary<int, N3LinePlan>();
        foreach (var (i, begin) in begins)
        {
            plans[i] = new N3LinePlan(i, begin, ends[i], manualBegin.Contains(i), manualEnd.Contains(i),
                pageOf.GetValueOrDefault(i), rowOf.GetValueOrDefault(i), adjusted.Contains(i));
        }
        return plans;
    }
}
