namespace NicoKaraPrep.Core.Model;

/// <summary>
/// 0 秒より前（マイナス）の時刻の扱い。アイコン・先行タグ・定型文を曲の頭近くに入れたときや、字幕全体の時刻を早めたときに、
/// 時刻を 0 で切らずにマイナスのまま持つ（あとで遅らせたとき、元の間隔のまま戻るように）。
/// 歌詞ファイル（lrc・rlf・txt）・行のエクスポート・n3proj の書き出しには 0 秒にした写し（<see cref="ForOutput"/>）を書き、
/// 本当の値は作業状態（.tttproj の行の設定。<see cref="Collect"/>・<see cref="Restore"/>）に残す。
/// </summary>
public static class NegativeTimes
{
    /// <summary>行のタイムタグを、文字のタグ → その文字の補助タグ → 行末のタグ の順に並べたもの（作業状態でタグを番号で指す順）。</summary>
    public static List<int> TagValues(LyricsLine line)
    {
        var list = new List<int>();
        foreach (var c in line.Chars)
        {
            if (c.TimeCs is int t) list.Add(t);
            list.AddRange(c.AuxTimeTagsCs);
        }
        if (line.EndTimeCs is int e) list.Add(e);
        return list;
    }

    /// <summary>行の <paramref name="ordinal"/> 番目のタイムタグ（<see cref="TagValues"/> の順）を <paramref name="cs"/> にする。</summary>
    private static bool SetTag(LyricsLine line, int ordinal, int cs)
    {
        int k = 0;
        foreach (var c in line.Chars)
        {
            if (c.TimeCs is not null)
            {
                if (k++ == ordinal)
                {
                    c.TimeCs = cs;
                    return true;
                }
            }
            for (int a = 0; a < c.AuxTimeTagsCs.Count; a++)
            {
                if (k++ == ordinal)
                {
                    c.AuxTimeTagsCs[a] = cs;
                    return true;
                }
            }
        }
        if (line.EndTimeCs is not null && k == ordinal)
        {
            line.EndTimeCs = cs;
            return true;
        }
        return false;
    }

    /// <summary>行の 0 秒より前のタイムタグ（番号と値）。無ければ空。</summary>
    public static List<NegativeTimeTag> Collect(LyricsLine line)
    {
        var result = new List<NegativeTimeTag>();
        var values = TagValues(line);
        for (int k = 0; k < values.Count; k++)
        {
            if (values[k] < 0) result.Add(new NegativeTimeTag { Ordinal = k, Cs = values[k] });
        }
        return result;
    }

    /// <summary>行に 0 秒より前のタイムタグがあるか。</summary>
    public static bool HasNegative(LyricsLine line) => TagValues(line).Any(v => v < 0);

    /// <summary>文書の 0 秒より前のタイムタグの数。</summary>
    public static int Count(LyricsDocument doc) => doc.Lines.Sum(l => TagValues(l).Count(v => v < 0));

    /// <summary>文書の 0 秒より前のタイムタグのうち、いちばん早い時刻（無ければ null）。</summary>
    public static int? Earliest(LyricsDocument doc)
    {
        int? min = null;
        foreach (var line in doc.Lines)
        {
            foreach (int v in TagValues(line))
            {
                if (v < 0 && (min is null || v < min)) min = v;
            }
        }
        return min;
    }

    /// <summary>
    /// 0 秒より前の時刻（タイムタグ・行の表示時刻・歌詞に当てはめられなかった @Ruby の区間）を 0 にする。0 にしたタイムタグの数を返す
    /// （表示時刻・ルビの区間は数えない）。
    /// </summary>
    public static int ClampToZero(LyricsDocument doc)
    {
        int count = 0;
        foreach (var line in doc.Lines)
        {
            foreach (var c in line.Chars)
            {
                if (c.TimeCs is < 0)
                {
                    c.TimeCs = 0;
                    count++;
                }
                for (int a = 0; a < c.AuxTimeTagsCs.Count; a++)
                {
                    if (c.AuxTimeTagsCs[a] < 0)
                    {
                        c.AuxTimeTagsCs[a] = 0;
                        count++;
                    }
                }
            }
            if (line.EndTimeCs is < 0)
            {
                line.EndTimeCs = 0;
                count++;
            }
            if (line.ShowBeginCs is < 0) line.ShowBeginCs = 0;
            if (line.ShowEndCs is < 0) line.ShowEndCs = 0;
        }
        for (int i = 0; i < doc.UnappliedRubyEntries.Count; i++)
        {
            var r = doc.UnappliedRubyEntries[i];
            if (r.StartCs is < 0 || r.EndCs is < 0)
            {
                doc.UnappliedRubyEntries[i] = r with
                {
                    StartCs = r.StartCs is int s ? Math.Max(0, s) : null,
                    EndCs = r.EndCs is int e ? Math.Max(0, e) : null,
                };
            }
        }
        return count;
    }

    /// <summary>
    /// ファイル・書き出しへ出す文書。0 秒より前の時刻が無ければ <paramref name="doc"/> そのもの、あれば 0 にした写し
    /// （<paramref name="clamped"/> は 0 にしたタイムタグの数）。<paramref name="doc"/> は変えない。
    /// </summary>
    public static LyricsDocument ForOutput(LyricsDocument doc, out int clamped)
    {
        clamped = 0;
        bool any = doc.Lines.Any(l => HasNegative(l) || l.ShowBeginCs is < 0 || l.ShowEndCs is < 0) ||
                   doc.UnappliedRubyEntries.Any(r => r.StartCs is < 0 || r.EndCs is < 0);
        if (!any) return doc;
        var copy = doc.Clone();
        clamped = ClampToZero(copy);
        return copy;
    }

    /// <summary>
    /// 作業状態に残した 0 秒より前のタイムタグを、開き直した行へ戻す。行のタイムタグの数が保存したときと同じで、
    /// 戻す番号のタグが 0 にして書いた値（lrc・txt は 0、rlf は 0.01 秒）のときだけ戻す（ほかのアプリで直したタグは戻さない）。
    /// 戻したタグの数を返す。
    /// </summary>
    public static int Restore(LyricsLine line, IEnumerable<NegativeTimeTag> tags, int? tagCount)
    {
        var values = TagValues(line);
        if (tagCount is int n && n != values.Count) return 0;
        int restored = 0;
        foreach (var t in tags)
        {
            if (t.Cs >= 0 || t.Ordinal < 0 || t.Ordinal >= values.Count) continue;
            if (values[t.Ordinal] > 1) continue;
            if (SetTag(line, t.Ordinal, t.Cs)) restored++;
        }
        return restored;
    }

    /// <summary>時刻を「−1.00 秒」の形にする（チェック結果・知らせ用）。</summary>
    public static string FormatSeconds(int cs)
    {
        string sign = cs < 0 ? "−" : "";
        int abs = Math.Abs(cs);
        return $"{sign}{abs / 100}.{abs % 100:00} 秒";
    }
}

/// <summary>作業状態に残す 0 秒より前のタイムタグ 1 つ（行のタグの番号（<see cref="NegativeTimes.TagValues"/> の順）と本当の値）。</summary>
public sealed class NegativeTimeTag
{
    public int Ordinal { get; set; }

    /// <summary>本当の時刻（10ms 単位。0 より小さい）。</summary>
    public int Cs { get; set; }
}
