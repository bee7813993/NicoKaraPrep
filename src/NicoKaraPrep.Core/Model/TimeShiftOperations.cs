namespace NicoKaraPrep.Core.Model;

/// <summary>
/// 歌詞の時刻をまとめてずらす（「字幕全体の時刻をずらす」）。
/// タイムタグ（文字のタグ・行末タグ・口パク用の補助タグ）、行の表示時刻（手で指定した値・読み込んだ値・自動調整の値のどれも）、
/// 歌詞に当てはめられなかった @Ruby の適用区間を、同じだけ動かす。
/// ルビの中のワイプのタグは読み込みのときに絶対時刻の補助タグにしているので、補助タグを動かせば足りる。@Offset は変えない。
/// 早めて 0 秒より前になった時刻はマイナスのまま持つ（<see cref="NegativeTimes"/>。保存・書き出しでは 0 秒。あとで遅らせると、その値から計算する）。
/// </summary>
public static class TimeShiftOperations
{
    /// <summary>タイムタグの書式（[mm:ss:cc]、分は 2 桁）で書ける最も遅い時刻（10ms 単位）。</summary>
    public const int MaxTimeCs = 99 * 6000 + 59 * 100 + 99;

    /// <summary>持てる最も早い時刻（[-99:59:99]）。</summary>
    public const int MinTimeCs = -MaxTimeCs;

    /// <summary>文書のタイムタグ（文字・行末・補助タグ）の最も早い時刻と最も遅い時刻。タグが 1 つも無ければ null。</summary>
    public static (int Earliest, int Latest)? TagRange(LyricsDocument doc)
    {
        int min = int.MaxValue, max = int.MinValue;
        void Note(int cs)
        {
            if (cs < min) min = cs;
            if (cs > max) max = cs;
        }
        foreach (var line in doc.Lines)
        {
            foreach (var c in line.Chars)
            {
                if (c.TimeCs is int t) Note(t);
                foreach (int aux in c.AuxTimeTagsCs) Note(aux);
            }
            if (line.EndTimeCs is int e) Note(e);
        }
        return min <= max ? (min, max) : null;
    }

    /// <summary>
    /// 文書（全タブ）をまとめてずらせる量の範囲（10ms 単位）。早めるのは最も早いタイムタグが <see cref="MinTimeCs"/> になるまで
    /// （0 秒より前はマイナスのまま持つ）、遅らせるのは最も遅いタイムタグが <see cref="MaxTimeCs"/> になるまで。タグが 1 つも無ければ (0, 0)。
    /// </summary>
    public static (int Min, int Max) ShiftLimits(IEnumerable<LyricsDocument> docs)
    {
        int? earliest = null, latest = null;
        foreach (var doc in docs)
        {
            if (TagRange(doc) is not var (lo, hi)) continue;
            earliest = earliest is int a ? Math.Min(a, lo) : lo;
            latest = latest is int b ? Math.Max(b, hi) : hi;
        }
        if (earliest is not int first || latest is not int last) return (0, 0);
        return (Math.Min(0, MinTimeCs - first), Math.Max(0, MaxTimeCs - last));
    }

    /// <summary>
    /// 文書の時刻を <paramref name="deltaCs"/>（10ms 単位。負なら早める）だけずらす。動かしたタイムタグの数を返す。
    /// 時刻は <see cref="MinTimeCs"/>〜<see cref="MaxTimeCs"/> に収める（タイムタグは <see cref="ShiftLimits"/> の範囲でずらせば、
    /// 収める必要は起きない）。0 秒より前になってもマイナスのまま持つ。
    /// </summary>
    public static int Shift(LyricsDocument doc, int deltaCs)
    {
        if (deltaCs == 0) return 0;
        int Move(int cs) => Math.Clamp(cs + deltaCs, MinTimeCs, MaxTimeCs);
        int count = 0;
        foreach (var line in doc.Lines)
        {
            foreach (var c in line.Chars)
            {
                if (c.TimeCs is int t)
                {
                    c.TimeCs = Move(t);
                    count++;
                }
                for (int k = 0; k < c.AuxTimeTagsCs.Count; k++)
                {
                    c.AuxTimeTagsCs[k] = Move(c.AuxTimeTagsCs[k]);
                    count++;
                }
            }
            if (line.EndTimeCs is int e)
            {
                line.EndTimeCs = Move(e);
                count++;
            }
            if (line.ShowBeginCs is int b) line.ShowBeginCs = Move(b);
            if (line.ShowEndCs is int se) line.ShowEndCs = Move(se);
        }
        for (int i = 0; i < doc.UnappliedRubyEntries.Count; i++)
        {
            var r = doc.UnappliedRubyEntries[i];
            if (r.StartCs is null && r.EndCs is null) continue;
            doc.UnappliedRubyEntries[i] = r with
            {
                StartCs = r.StartCs is int s ? Move(s) : null,
                EndCs = r.EndCs is int en ? Move(en) : null,
            };
        }
        return count;
    }
}
