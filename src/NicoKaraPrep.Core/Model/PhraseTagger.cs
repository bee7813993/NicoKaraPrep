using System.Text;
using System.Text.RegularExpressions;

namespace NicoKaraPrep.Core.Model;

/// <summary>定型文の時刻の決め方の値（10ms 単位）。</summary>
public sealed class PhraseTiming
{
    /// <summary>ワイプ前（行を歌い出しの何秒前から表示するか）。</summary>
    public int LeadCs { get; init; } = 150;

    /// <summary>ワイプ後（行を歌い終わりの何秒後まで表示するか）。</summary>
    public int TailCs { get; init; } = 50;

    /// <summary>後ろ（前）に歌が無いときの定型文のワイプの長さ（後奏）。</summary>
    public int AloneCs { get; init; } = 300;

    /// <summary>定型文だけの行の前後に空行を置いて、定型文だけのページにするか（空行がページ区切りのとき）。</summary>
    public bool SeparatePages { get; init; } = true;
}

/// <summary>定型文を入れた結果。</summary>
/// <param name="Text">入れた文字列（<see cref="PhraseTagger.SecondsToken"/> を秒数に置き換えたもの）。</param>
/// <param name="StartCs">開始のタイムタグ（付けられなければ null）。</param>
/// <param name="EndCs">終わりのタイムタグ（付けられなければ null）。</param>
/// <param name="LineIndex">定型文が入った行。</param>
/// <param name="DisplayOffset">定型文の行の中の、定型文の先頭の表示位置。</param>
/// <param name="OwnLine">定型文だけの行として入れたか（false は行の途中に入れた）。</param>
/// <param name="Squeezed">前後の間が短く、表示が前後のページと重ならない位置に入らなかったので、前後の歌に合わせたか。</param>
public sealed record PhraseInsertResult(string Text, int? StartCs, int? EndCs, int LineIndex, int DisplayOffset, bool OwnLine, bool Squeezed);

/// <summary>行の中の定型文 1 つ（CharUnit の範囲と、行の表示文字列の中の位置）。</summary>
public sealed record PhraseOccurrence(int Start, int EndExclusive, int DisplayStart, string Value);

/// <summary>
/// 定型文（「（前奏）」「（間奏）」「（後奏）」「（間奏 約{秒}秒）」など、歌詞に入れる決まった文字列）の挿入・削除とタイムタグ。
///
/// 行頭・行末・空行に入れたときは、定型文だけの行にする（行頭なら前、行末なら後ろに行を足す。空行がページ区切りなら前後に空行を置いて、
/// 定型文だけのページにする）。時刻は、前後のページと表示が重ならないように置く:
///   画面に出す時間 = 前のページが消える時刻 ＋ ワイプ前 〜 次のページが出る時刻 − ワイプ後
///   （前のページが消える時刻 = 前の行の表示終了の指定、無ければ 歌い終わり ＋ ワイプ後。次のページが出る時刻も同じく 歌い出し − ワイプ前）
///   タグ = 画面に出す時間の 始まり ＋ ワイプ前 〜 終わり − ワイプ後（ニコカラメーカー3 がこのタグから同じ時間を表示する）
///   例: 歌い終わり 01:12.0・歌い出し 01:26.0・ワイプ前 1.5・ワイプ後 0.5 → 画面 01:14.0〜01:24.0、タグ [01:15.5]（間奏）[01:23.5]
///   後ろに歌が無い（後奏）ときは開始から <see cref="PhraseTiming.AloneCs"/>、前に歌が無い（前奏）ときは曲の頭（00:00）から表示する。
///   間が短くて入らないときは、前後の歌に合わせる（直前の歌の終わり 〜 直後の歌の始まり）。
/// 行の途中に入れたときは、前後の歌に合わせる（直前の文字の終わり 〜 直後の文字の始まり）。
/// 文字列の中の <see cref="SecondsToken"/> は、前後の歌のあいだの秒数（四捨五入）に置き換える（「（間奏 約14秒）」）。
/// </summary>
public static class PhraseTagger
{
    /// <summary>秒数に置き換える印。</summary>
    public const string SecondsToken = "{秒}";

    /// <summary><paramref name="lineIndex"/> 行の <paramref name="unitIndex"/>（CharUnit 単位）に定型文を入れ、タイムタグを付ける。</summary>
    public static PhraseInsertResult Insert(LyricsDocument doc, int lineIndex, int unitIndex, string template, PhraseTiming timing)
    {
        var line = doc.Lines[lineIndex];
        int u = Math.Clamp(unitIndex, 0, line.Chars.Count);
        bool textBefore = line.Chars.Take(u).Any(c => !c.IsSpacer);
        bool textAfter = line.Chars.Skip(u).Any(c => !c.IsSpacer);
        if (textBefore && textAfter) return InsertInline(doc, lineIndex, u, template, timing);

        // 定型文だけの行（空行ならその行、行頭なら前、行末なら後ろに行を足す）
        int at;
        if (!textBefore && !textAfter)
        {
            at = lineIndex;
            line.Chars.Clear();
            line.EndTimeCs = null;
        }
        else
        {
            at = textBefore ? lineIndex + 1 : lineIndex;
            doc.Lines.Insert(at, new LyricsLine());
        }
        if (timing.SeparatePages)
        {
            if (at + 1 < doc.Lines.Count && !doc.Lines[at + 1].IsEmpty) doc.Lines.Insert(at + 1, new LyricsLine());
            if (at > 0 && !doc.Lines[at - 1].IsEmpty)
            {
                doc.Lines.Insert(at, new LyricsLine());
                at++;
            }
        }

        var (prevEnd, prevLine) = PreviousLinesEnd(doc, at);
        var (nextStart, nextLine) = NextLinesStart(doc, at);
        int? prevShown = prevLine?.ShowEndCs ?? (prevEnd is int pe ? pe + timing.TailCs : null);
        int? nextShown = nextLine?.ShowBeginCs ?? (nextStart is int ns ? ns - timing.LeadCs : null);

        // 画面に出す時間 → タグ
        int? start = prevShown is int a ? a + timing.LeadCs + timing.LeadCs : nextShown is not null ? timing.LeadCs : null;
        int? end = nextShown is int b ? b - timing.TailCs - timing.TailCs : start is int s0 ? s0 + timing.AloneCs : null;
        bool squeezed = false;
        if (start is int s1 && end is int e1 && e1 <= s1)
        {
            // 間が短くて入らない: 前後の歌に合わせる
            squeezed = true;
            start = prevEnd ?? (nextStart is int n0 ? Math.Max(0, n0 - timing.AloneCs) : null);
            end = nextStart ?? (start is int s2 ? s2 + timing.AloneCs : null);
            if (start is int s3 && end is int e3 && e3 < s3) end = s3;
        }

        string text = template.Replace(SecondsToken, Seconds(prevEnd, nextStart, start, end));
        var phraseLine = doc.Lines[at];
        phraseLine.Chars.AddRange(Units(text));
        if (phraseLine.Chars.Count > 0 && start is int st)
        {
            phraseLine.Chars[0].TimeCs = st;
            phraseLine.Chars[0].CheckCount = 1;
        }
        if (phraseLine.Chars.Count > 0) phraseLine.EndTimeCs = end;
        return new PhraseInsertResult(text, start, end, at, 0, OwnLine: true, squeezed);
    }

    /// <summary>行の途中: 直前の文字の終わり 〜 直後の文字の始まり。</summary>
    private static PhraseInsertResult InsertInline(LyricsDocument doc, int lineIndex, int u, string template, PhraseTiming timing)
    {
        var line = doc.Lines[lineIndex];

        // 直前の文字の終わりのタグ（2連タグのスペーサー）の後ろに入れる
        int? spacerEnd = null;
        while (u < line.Chars.Count && line.Chars[u].IsSpacer)
        {
            spacerEnd = line.Chars[u].TimeCs ?? spacerEnd;
            u++;
        }
        int? next = FirstTimeFrom(line, u) ?? NextLinesStart(doc, lineIndex).Time;
        int? prev = spacerEnd ?? next ?? line.EndTimeCs;
        int? start = prev;
        int? end = next ?? (prev is int p ? p + timing.AloneCs : null);

        string text = template.Replace(SecondsToken, Seconds(prev, next, start, end));
        var units = Units(text);
        if (units.Count == 0) return new PhraseInsertResult(text, null, null, lineIndex, DisplayOffsetOf(line, u), false, false);
        if (start is int st)
        {
            units[0].TimeCs = st;
            units[0].CheckCount = 1;
            // 直前の文字の終わりのスペーサーと同じ時刻なら、定型文の開始のタグがその代わりになる（同じタグを 2 つ並べない）
            if (u > 0 && line.Chars[u - 1].IsSpacer && line.Chars[u - 1].TimeCs == st)
            {
                line.Chars.RemoveAt(u - 1);
                u--;
            }
        }
        line.Chars.InsertRange(u, units);
        int after = u + units.Count;
        if (end is int en && after < line.Chars.Count && line.Chars[after].TimeCs != en)
        {
            line.Chars.Insert(after, new CharUnit { Text = CharUnit.Spacer, TimeCs = en, CheckCount = 1 });
        }
        return new PhraseInsertResult(text, start, end, lineIndex, DisplayOffsetOf(line, u), false, false);
    }

    // ------------------------------------------------------------ 探す・消す

    /// <summary>
    /// 行の中の定型文を探す（<see cref="SecondsToken"/> は数字か「?」に当てはめる）。重なるときは前から、同じ位置なら長いほうを取る。
    /// </summary>
    public static List<PhraseOccurrence> FindOccurrences(LyricsLine line, IEnumerable<string> templates)
    {
        // 表示文字列と、表示位置 → CharUnit の対応
        var sb = new StringBuilder();
        var unitAt = new List<int>();
        for (int i = 0; i < line.Chars.Count; i++)
        {
            var c = line.Chars[i];
            if (c.IsSpacer) continue;
            foreach (char _ in c.Text) unitAt.Add(i);
            sb.Append(c.Text);
        }
        string display = sb.ToString();

        var found = new List<(int Start, int Length)>();
        foreach (string t in templates.Where(t => !string.IsNullOrEmpty(t)).Distinct())
        {
            string pattern = string.Join(@"(?:\d+|\?)", t.Split(SecondsToken).Select(Regex.Escape));
            foreach (Match m in Regex.Matches(display, pattern))
            {
                if (m.Length > 0) found.Add((m.Index, m.Length));
            }
        }
        var result = new List<PhraseOccurrence>();
        int taken = 0;
        foreach (var (s, len) in found.OrderBy(f => f.Start).ThenByDescending(f => f.Length))
        {
            if (s < taken) continue;
            int startUnit = unitAt[s];
            int endUnit = unitAt[s + len - 1] + 1;
            result.Add(new PhraseOccurrence(startUnit, endUnit, s, display.Substring(s, len)));
            taken = s + len;
        }
        return result;
    }

    /// <summary>
    /// 定型文を消す。定型文だけの行なら行ごと消し、入れたときに足した前後の空行（続いた空行）を 1 つにまとめる。
    /// 行の途中なら文字と、入れたときに足した終わりのタグ（直後のスペーサー）を消し、直前の文字の終わりのタグを残す。
    /// 消したあとのカーソルの位置（行, 行の中の表示位置）を返す。
    /// </summary>
    public static (int LineIndex, int DisplayOffset) Delete(LyricsDocument doc, int lineIndex, PhraseOccurrence occ)
    {
        var line = doc.Lines[lineIndex];
        bool only = line.Chars.Select((c, i) => (c, i)).All(x => x.c.IsSpacer || (x.i >= occ.Start && x.i < occ.EndExclusive));
        if (only)
        {
            int i = lineIndex;
            doc.Lines.RemoveAt(i);
            if (i > 0 && i < doc.Lines.Count && doc.Lines[i - 1].IsEmpty && doc.Lines[i].IsEmpty)
            {
                doc.Lines.RemoveAt(i);
                i--;
            }
            else if (i == 0 && doc.Lines.Count > 0 && doc.Lines[0].IsEmpty)
            {
                doc.Lines.RemoveAt(0);
            }
            else if (i == doc.Lines.Count && i > 0 && doc.Lines[i - 1].IsEmpty)
            {
                doc.Lines.RemoveAt(i - 1);
            }
            if (doc.Lines.Count == 0) doc.Lines.Add(new LyricsLine());
            if (i >= doc.Lines.Count) return (doc.Lines.Count - 1, doc.Lines[^1].GetDisplayText().Length);
            return (i, 0);
        }

        int s = occ.Start;
        int e = occ.EndExclusive;
        int? startTag = line.Chars[s].TimeCs;
        if (e < line.Chars.Count && line.Chars[e].IsSpacer) e++; // 入れたときに足した終わりのタグ
        line.Chars.RemoveRange(s, e - s);
        if (startTag is int t && s > 0 && !line.Chars[s - 1].IsSpacer)
        {
            // 定型文の開始のタグは直前の文字の終わりのタグでもあったので、後ろのタグと違えば 2連タグで残す
            int? nextTag = s < line.Chars.Count ? line.Chars[s].TimeCs : line.EndTimeCs;
            if (nextTag != t) line.Chars.Insert(s, new CharUnit { Text = CharUnit.Spacer, TimeCs = t, CheckCount = 1 });
        }
        return (lineIndex, occ.DisplayStart);
    }

    // ------------------------------------------------------------ 補助

    /// <summary>「{秒}」の値: 前後の歌のあいだ（前が無ければ曲の頭から）。分からなければ定型文の長さ、それも無ければ「?」。</summary>
    private static string Seconds(int? prevEnd, int? nextStart, int? start, int? end)
    {
        int? cs = nextStart is int n ? n - (prevEnd ?? 0) : start is int s && end is int e ? e - s : null;
        return cs is int v ? Math.Round(Math.Max(0, v) / 100.0, MidpointRounding.AwayFromZero).ToString("0") : "?";
    }

    /// <summary>1 コードポイント = 1 CharUnit。</summary>
    private static List<CharUnit> Units(string text)
    {
        var units = new List<CharUnit>();
        for (int pos = 0; pos < text.Length;)
        {
            int len = char.IsHighSurrogate(text[pos]) && pos + 1 < text.Length && char.IsLowSurrogate(text[pos + 1]) ? 2 : 1;
            units.Add(new CharUnit { Text = text.Substring(pos, len) });
            pos += len;
        }
        return units;
    }

    private static int DisplayOffsetOf(LyricsLine line, int unitIndex) =>
        line.Chars.Take(unitIndex).Where(c => !c.IsSpacer).Sum(c => c.Text.Length);

    /// <summary>行の index 以降の最初のタイムタグ（スペーサーを除く文字の開始。無ければ null）。</summary>
    private static int? FirstTimeFrom(LyricsLine line, int index)
    {
        for (int i = index; i < line.Chars.Count; i++)
        {
            var c = line.Chars[i];
            if (!c.IsSpacer && c.TimeCs is int t) return t;
        }
        return null;
    }

    /// <summary>次の行からの最初の歌い出し（タグのある行の最初のタグ）と、その行。</summary>
    private static (int? Time, LyricsLine? Line) NextLinesStart(LyricsDocument doc, int lineIndex)
    {
        for (int i = lineIndex + 1; i < doc.Lines.Count; i++)
        {
            if (doc.Lines[i].GetFirstTimeCs() is int t) return (t, doc.Lines[i]);
        }
        return (null, null);
    }

    /// <summary>前の行からの最後の歌い終わり（行の終わりのタグ、無ければ最後のタグ）と、その行。</summary>
    private static (int? Time, LyricsLine? Line) PreviousLinesEnd(LyricsDocument doc, int lineIndex)
    {
        for (int i = lineIndex - 1; i >= 0; i--)
        {
            if (doc.Lines[i].GetLastTimeCs() is int t) return (t, doc.Lines[i]);
        }
        return (null, null);
    }
}
