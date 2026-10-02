namespace NicoKaraPrep.Core.Model;

/// <summary>文字単位のフォント設定名の手動指定の範囲。行の表示文字（2 連タグ用スペーサーを除く CharUnit）の位置で数える。</summary>
public sealed class CharFontRange
{
    /// <summary>最初の表示文字の位置（0 から）。</summary>
    public int Start { get; set; }

    /// <summary>表示文字の数。</summary>
    public int Length { get; set; }

    /// <summary>フォント設定名。</summary>
    public string Name { get; set; } = "";
}

/// <summary>
/// 文字単位のフォント設定名の手動指定（<see cref="CharUnit.FontSetName"/>）の操作。
/// 範囲は行の表示文字（2 連タグ用スペーサーを除く）の位置で数える（スペーサーは絵文字の挿入などで増減するため）。
/// </summary>
public static class CharFontOperations
{
    /// <summary>行の表示文字（2 連タグ用スペーサーを除く CharUnit）。</summary>
    public static List<CharUnit> DisplayUnits(LyricsLine line) => line.Chars.Where(c => !c.IsSpacer).ToList();

    /// <summary>行の文字の手動指定を、同じ名前が続く範囲ごとにまとめる（保存用）。</summary>
    public static List<CharFontRange> Ranges(LyricsLine line)
    {
        var result = new List<CharFontRange>();
        var units = DisplayUnits(line);
        for (int i = 0; i < units.Count; i++)
        {
            string? name = units[i].FontSetName;
            if (name is null) continue;
            if (result.Count > 0 && result[^1].Name == name && result[^1].Start + result[^1].Length == i)
            {
                result[^1].Length++;
            }
            else
            {
                result.Add(new CharFontRange { Start = i, Length = 1, Name = name });
            }
        }
        return result;
    }

    /// <summary>保存した範囲を行の文字へ当てる（行の外にはみ出す分は捨てる。名前が空の範囲は無視する）。</summary>
    public static void ApplyRanges(LyricsLine line, IEnumerable<CharFontRange>? ranges)
    {
        if (ranges is null) return;
        var units = DisplayUnits(line);
        foreach (var r in ranges)
        {
            if (r is null || string.IsNullOrEmpty(r.Name) || r.Length <= 0) continue;
            int end = Math.Min(units.Count, r.Start + r.Length);
            for (int i = Math.Max(0, r.Start); i < end; i++) units[i].FontSetName = r.Name;
        }
    }

    /// <summary>
    /// 行の CharUnit の範囲（<paramref name="startUnit"/> 〜 <paramref name="endUnit"/>。<see cref="LyricsLine.Chars"/> の添字、両端を含む）の文字に
    /// フォント設定名を指定する（null / 空 = 自動に戻す）。変わった文字の数を返す。範囲の中のスペーサーにも同じ名前を付ける。
    /// </summary>
    public static int SetRange(LyricsLine line, int startUnit, int endUnit, string? name)
    {
        string? value = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        if (line.Chars.Count == 0) return 0;
        int from = Math.Clamp(Math.Min(startUnit, endUnit), 0, line.Chars.Count - 1);
        int to = Math.Clamp(Math.Max(startUnit, endUnit), 0, line.Chars.Count - 1);
        int changed = 0;
        for (int i = from; i <= to; i++)
        {
            var c = line.Chars[i];
            if (c.FontSetName == value) continue;
            c.FontSetName = value;
            if (!c.IsSpacer) changed++;
        }
        return changed;
    }

    /// <summary>行の文字の手動指定をすべて消す。消した文字があれば true。</summary>
    public static bool Clear(LyricsLine line)
    {
        bool changed = false;
        foreach (var c in line.Chars)
        {
            if (c.FontSetName is null) continue;
            c.FontSetName = null;
            changed = true;
        }
        return changed;
    }

    /// <summary>行の文字の手動指定の名前 oldName を newName にする。変えた文字の数を返す（スペーサーは数えない）。</summary>
    public static int Rename(LyricsLine line, string oldName, string newName)
    {
        int count = 0;
        foreach (var c in line.Chars)
        {
            if (c.FontSetName != oldName) continue;
            c.FontSetName = newName;
            if (!c.IsSpacer) count++;
        }
        return count;
    }

    /// <summary>
    /// 歌詞を書き換えて作り直した行（<paramref name="to"/>）へ、元の行（<paramref name="from"/>）の文字の手動指定を写す。
    /// 表示文字の並びを最長共通部分列で対応させ、同じ文字に同じ名前を付ける。書き換えで増えた文字は、
    /// 前後の文字が同じ名前ならその名前にする（指定した範囲の途中に文字を足しても範囲が切れないように）。
    /// スペーサーは直後の表示文字（無ければ直前）と同じ名前にする。
    /// </summary>
    public static void CopyCharFonts(LyricsLine from, LyricsLine to)
    {
        var oldUnits = DisplayUnits(from);
        if (!oldUnits.Any(c => c.FontSetName is not null)) return;
        var newUnits = DisplayUnits(to);
        int n = oldUnits.Count, m = newUnits.Count;

        // 最長共通部分列（文字の並びの対応）
        var lcs = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
        {
            for (int j = m - 1; j >= 0; j--)
            {
                lcs[i, j] = oldUnits[i].Text == newUnits[j].Text
                    ? lcs[i + 1, j + 1] + 1
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }
        var names = new string?[m];
        var matched = new bool[m];
        for (int i = 0, j = 0; i < n && j < m;)
        {
            if (oldUnits[i].Text == newUnits[j].Text && lcs[i, j] == lcs[i + 1, j + 1] + 1)
            {
                names[j] = oldUnits[i].FontSetName;
                matched[j] = true;
                i++;
                j++;
            }
            else if (lcs[i + 1, j] >= lcs[i, j + 1])
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        // 増えた文字: 前後の対応した文字が同じ名前なら、その名前にする
        for (int j = 0; j < m; j++)
        {
            if (matched[j]) continue;
            string? before = null, after = null;
            for (int k = j - 1; k >= 0; k--)
            {
                if (matched[k]) { before = names[k]; break; }
            }
            for (int k = j + 1; k < m; k++)
            {
                if (matched[k]) { after = names[k]; break; }
            }
            if (before is not null && before == after) names[j] = before;
        }

        for (int j = 0; j < m; j++) newUnits[j].FontSetName = names[j];

        // スペーサー: 直後の表示文字（無ければ直前）と同じ名前
        for (int i = 0; i < to.Chars.Count; i++)
        {
            var c = to.Chars[i];
            if (!c.IsSpacer) continue;
            string? name = null;
            bool found = false;
            for (int k = i + 1; k < to.Chars.Count; k++)
            {
                if (to.Chars[k].IsSpacer) continue;
                name = to.Chars[k].FontSetName;
                found = true;
                break;
            }
            if (!found)
            {
                for (int k = i - 1; k >= 0; k--)
                {
                    if (to.Chars[k].IsSpacer) continue;
                    name = to.Chars[k].FontSetName;
                    break;
                }
            }
            c.FontSetName = name;
        }
    }
}
