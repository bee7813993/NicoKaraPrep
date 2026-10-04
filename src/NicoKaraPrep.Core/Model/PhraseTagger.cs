namespace NicoKaraPrep.Core.Model;

/// <summary>定型文を入れた結果。</summary>
/// <param name="Text">入れた文字列（<see cref="PhraseTagger.SecondsToken"/> を秒数に置き換えたもの）。</param>
/// <param name="StartCs">開始のタイムタグ（付けられなければ null）。</param>
/// <param name="EndCs">終わりのタイムタグ（付けられなければ null）。</param>
public sealed record PhraseInsertResult(string Text, int? StartCs, int? EndCs);

/// <summary>
/// 定型文（「（後奏）」「（間奏 約{秒}秒）」など、歌詞に入れる決まった文字列）の挿入とタイムタグ。
///
/// 前後の歌に合わせる:
///   開始 = 直前の歌の終わり（同じ行の前に文字があればその文字の終わりのタグ、行頭なら前の行の歌い終わり）
///   終わり = 直後の歌の始まり（同じ行の後ろの最初のタグ、無ければ次の行の歌い出し）
///   直後に歌が無い（後奏）ときは 開始 ＋ <c>tailCs</c>、直前に歌が無い（曲の頭）ときは 終わり − <c>leadCs</c>
/// 文字列の中の <see cref="SecondsToken"/> は、開始から終わりまでの秒数（四捨五入）に置き換える（「（間奏 約14秒）」）。
/// 終わりのタグは、後ろの文字の開始と同じなら置かず、違えば 2連タグ（スペーサー）で、行末なら行の終わりのタグとして置く。
/// </summary>
public static class PhraseTagger
{
    /// <summary>秒数に置き換える印。</summary>
    public const string SecondsToken = "{秒}";

    /// <summary>直後に歌が無いときの長さの既定（10ms 単位）。</summary>
    public const int DefaultTailCs = 300;

    /// <summary>
    /// <paramref name="lineIndex"/> 行の <paramref name="unitIndex"/>（CharUnit 単位）に定型文を入れ、前後の歌に合わせてタイムタグを付ける。
    /// </summary>
    public static PhraseInsertResult Insert(LyricsDocument doc, int lineIndex, int unitIndex, string template, int tailCs = DefaultTailCs, int leadCs = 200)
    {
        var line = doc.Lines[lineIndex];
        int u = Math.Clamp(unitIndex, 0, line.Chars.Count);

        // 直前の文字の終わりのタグ（2連タグのスペーサー）の後ろに入れる
        int? spacerEnd = null;
        while (u < line.Chars.Count && line.Chars[u].IsSpacer)
        {
            spacerEnd = line.Chars[u].TimeCs ?? spacerEnd;
            u++;
        }
        bool textBefore = line.Chars.Take(u).Any(c => !c.IsSpacer);

        int? next = FirstTimeFrom(line, u) ?? NextLinesStart(doc, lineIndex);
        int? prev = textBefore
            ? spacerEnd ?? FirstTimeFrom(line, u) ?? line.EndTimeCs
            : PreviousLinesEnd(doc, lineIndex);

        int? start = prev ?? (next is int n ? Math.Max(0, n - leadCs) : null);
        int? end = next ?? (prev is int p ? p + tailCs : null);
        if (start is int s0 && end is int e0 && e0 < s0) end = s0;

        string seconds = start is int s1 && end is int e1
            ? Math.Round((e1 - s1) / 100.0, MidpointRounding.AwayFromZero).ToString("0")
            : "?";
        string text = template.Replace(SecondsToken, seconds);

        // 1 コードポイント = 1 CharUnit で入れ、先頭に開始のタグを付ける
        var units = new List<CharUnit>();
        for (int pos = 0; pos < text.Length;)
        {
            int len = char.IsHighSurrogate(text[pos]) && pos + 1 < text.Length && char.IsLowSurrogate(text[pos + 1]) ? 2 : 1;
            units.Add(new CharUnit { Text = text.Substring(pos, len) });
            pos += len;
        }
        if (units.Count == 0) return new PhraseInsertResult(text, null, null);
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

        // 終わりのタグ
        int after = u + units.Count;
        if (end is int en)
        {
            if (after >= line.Chars.Count) line.EndTimeCs = en;
            else if (line.Chars[after].TimeCs != en) line.Chars.Insert(after, new CharUnit { Text = CharUnit.Spacer, TimeCs = en, CheckCount = 1 });
        }
        return new PhraseInsertResult(text, start, end);
    }

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

    /// <summary>次の行からの最初の歌い出し（タグのある行の最初のタグ）。</summary>
    private static int? NextLinesStart(LyricsDocument doc, int lineIndex)
    {
        for (int i = lineIndex + 1; i < doc.Lines.Count; i++)
        {
            if (doc.Lines[i].GetFirstTimeCs() is int t) return t;
        }
        return null;
    }

    /// <summary>前の行からの最後の歌い終わり（行の終わりのタグ、無ければ最後のタグ）。</summary>
    private static int? PreviousLinesEnd(LyricsDocument doc, int lineIndex)
    {
        for (int i = lineIndex - 1; i >= 0; i--)
        {
            var l = doc.Lines[i];
            if (l.EndTimeCs is int e) return e;
            if (l.GetLastTimeCs() is int t) return t;
        }
        return null;
    }
}
