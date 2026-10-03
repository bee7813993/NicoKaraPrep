namespace NicoKaraPrep.Core.Model;

/// <summary>絵文字挿入時のタイムタグ自動付与の設定。</summary>
public sealed class EmojiTagSettings
{
    /// <summary>先行秒数（10ms 単位）。絵文字の開始時刻 = 直後の実文字の時刻 − この値。</summary>
    public int LeadCs { get; set; } = 200;

    /// <summary>true: 連続絵文字それぞれにタグ付与（デフォルト）/ false: ブロックの先頭のみ。</summary>
    public bool PerEmoji { get; set; } = true;
}

/// <summary>
/// 絵文字（@Emoji の置き換え文字列。複数文字可）へのタイムタグ自動付与。
///
/// 仕様:
///   基準時刻 T = 絵文字を除く直後の実文字のタイムタグ開始時間
///   絵文字の終了時刻 = T、開始時刻 = T − 先行秒数
///   連続絵文字（デフォルト）: 各絵文字（出現）ごとに先頭へ T−n を付与し、間に終了時刻 T の 2連タグ（スペーサー）を挟む
///   連続絵文字（ブロックモード）: ブロック先頭の絵文字にのみ T−n を付与（終了は直後の実文字のタグ）
///   挿入した絵文字の直前の文字に終わりのタグが無ければ、T を終わりのタグとして足す（<see cref="CloseBeforeEmoji"/>）
/// </summary>
public static class EmojiTagger
{
    /// <summary>
    /// 行の charIndex 位置（CharUnit 単位）に絵文字（置き換え文字列全体）を挿入し、
    /// 行全体の絵文字タグを付け直す。挿入された CharUnit 数（直前の文字の終わりのタグを載せる空白を足したらその分も）を返す。
    /// </summary>
    public static int InsertEmoji(LyricsLine line, int charIndex, string replaceString, EmojiMatcher matcher, EmojiTagSettings settings) =>
        InsertEmoji(line, charIndex, replaceString, matcher, settings, out _);

    /// <summary>
    /// <see cref="InsertEmoji(LyricsLine, int, string, EmojiMatcher, EmojiTagSettings)"/> と同じ。closedEndCs は、直前の文字の
    /// 終わりのタグとして足した時刻（足さなければ null）。
    /// </summary>
    public static int InsertEmoji(LyricsLine line, int charIndex, string replaceString, EmojiMatcher matcher, EmojiTagSettings settings, out int? closedEndCs)
    {
        charIndex = Math.Clamp(charIndex, 0, line.Chars.Count);

        // 置き換え文字列を 1 コードポイント = 1 CharUnit で挿入
        int count = 0;
        int pos = 0;
        while (pos < replaceString.Length)
        {
            int len = char.IsHighSurrogate(replaceString[pos]) && pos + 1 < replaceString.Length &&
                      char.IsLowSurrogate(replaceString[pos + 1]) ? 2 : 1;
            line.Chars.Insert(charIndex + count, new CharUnit { Text = replaceString.Substring(pos, len) });
            pos += len;
            count++;
        }

        RetagLine(line, matcher, settings);
        return count + CloseBeforeEmoji(line, charIndex, matcher, out closedEndCs);
    }

    /// <summary>
    /// 挿入した絵文字（charIndex から）の直前の文字に終わりのタイムタグが無ければ、絵文字の基準時刻 T（次の最初のタイムタグ）を足す。
    /// 足さないと、絵文字の開始タグ（T − 表示秒数）が直前の文字の終わりになり、時刻が巻き戻る（縮む）。
    /// 例: <c>[02:22:94]d！[02:23:08] </c> の「d！」の後ろに入れると <c>[02:22:94]d！[02:21:08]（コーラス）[02:23:08] </c> になる。
    /// 直前が歌う文字で、絵文字の後ろが空白なら、T のタグを付けた空白を絵文字の前にも入れる
    /// （<c>[02:22:94]d！[02:23:08] [02:21:08]（コーラス）[02:23:08] </c>）。空白の無い所では空白を足さず、T のタグだけを置く
    /// （2連タグ: <c>[00:10:00]あ[00:20:00][00:18:00]（さやか）[00:20:00]い</c>）。
    /// 直前がタグの無い空白なら、その空白に T を付ける。行頭、直前が絵文字・スペーサー・タグの付いた空白、
    /// 直前の文字より前にタグが無い（どのタグの区間にも入っていない）、絵文字にタグが付かない・開始が T と同じ（表示秒数 0）ときは足さない。
    /// 足した CharUnit の数（0 か 1）を返す。
    /// </summary>
    private static int CloseBeforeEmoji(LyricsLine line, int charIndex, EmojiMatcher matcher, out int? closedEndCs)
    {
        closedEndCs = null;
        if (charIndex <= 0 || charIndex >= line.Chars.Count) return 0;
        var occurrences = matcher.FindOccurrences(line.Chars);
        var emojiUnits = new HashSet<int>();
        foreach (var occ in occurrences)
        {
            for (int i = occ.Start; i < occ.EndExclusive; i++) emojiUnits.Add(i);
        }
        var prev = line.Chars[charIndex - 1];
        if (prev.IsSpacer || emojiUnits.Contains(charIndex - 1)) return 0;

        // 挿入した絵文字の開始タグ（T − 表示秒数）と基準時刻 T（RetagLine と同じ決め方）
        int k = occurrences.FindIndex(o => o.Start == charIndex);
        if (k < 0 || line.Chars[charIndex].TimeCs is not int lead) return 0;
        if (BaseTime(line, occurrences[k].EndExclusive, emojiUnits) is not int baseT || lead >= baseT) return 0;

        bool blank = string.IsNullOrWhiteSpace(prev.Text);
        if (blank && prev.TimeCs is not null) return 0; // 空白に付いた終わりのタグがある
        bool timed = false;
        for (int i = charIndex - 1; i >= 0 && !timed; i--) timed = line.Chars[i].TimeCs is not null;
        if (!timed) return 0;

        closedEndCs = baseT;
        if (blank)
        {
            prev.TimeCs = baseT;
            prev.CheckCount = 1;
            return 0;
        }
        int next = occurrences[k].EndExclusive;
        bool spaceAfter = next < line.Chars.Count && string.IsNullOrWhiteSpace(line.Chars[next].Text);
        line.Chars.Insert(charIndex, new CharUnit { Text = spaceAfter ? " " : CharUnit.Spacer, TimeCs = baseT, CheckCount = 1 });
        return 1;
    }

    /// <summary>絵文字ブロックの基準時刻 T（blockEnd 以降の、絵文字・スペーサー以外の最初のタグ。無ければ行末のタグ）。</summary>
    private static int? BaseTime(LyricsLine line, int blockEnd, HashSet<int> emojiUnitIndexes)
    {
        for (int i = blockEnd; i < line.Chars.Count; i++)
        {
            var c = line.Chars[i];
            if (c.IsSpacer || emojiUnitIndexes.Contains(i)) continue;
            if (c.TimeCs is int time) return time;
        }
        return line.EndTimeCs;
    }

    /// <summary>ドキュメント全体の絵文字タグを現在の実文字の時刻から付け直す。</summary>
    public static void RetagAll(LyricsDocument doc, EmojiMatcher matcher, EmojiTagSettings settings)
    {
        foreach (var line in doc.Lines)
        {
            RetagLine(line, matcher, settings);
        }
    }

    /// <summary>
    /// 1 行の絵文字タグを付け直す。
    /// 既存の絵文字間スペーサー（過去の自動付与の産物）は一度取り除いてから再構築するため冪等。
    /// </summary>
    public static void RetagLine(LyricsLine line, EmojiMatcher matcher, EmojiTagSettings settings)
    {
        if (matcher.IsEmpty) return;

        // 1) 絵文字出現の間に挟まれたスペーサーを除去
        var occurrences = matcher.FindOccurrences(line.Chars);
        if (occurrences.Count == 0) return;

        var spacersToRemove = new List<int>();
        for (int k = 0; k + 1 < occurrences.Count; k++)
        {
            int gapStart = occurrences[k].EndExclusive;
            int gapEnd = occurrences[k + 1].Start;
            if (gapEnd > gapStart &&
                Enumerable.Range(gapStart, gapEnd - gapStart).All(i => line.Chars[i].IsSpacer))
            {
                spacersToRemove.AddRange(Enumerable.Range(gapStart, gapEnd - gapStart));
            }
        }
        for (int k = spacersToRemove.Count - 1; k >= 0; k--)
        {
            line.Chars.RemoveAt(spacersToRemove[k]);
        }

        // 2) 出現を取り直し、絵文字ユニットの索引を作る
        occurrences = matcher.FindOccurrences(line.Chars);
        var emojiUnitIndexes = new HashSet<int>();
        foreach (var occ in occurrences)
        {
            for (int i = occ.Start; i < occ.EndExclusive; i++) emojiUnitIndexes.Add(i);
        }

        // 3) 隣接する出現をブロックにまとめてタグ付け（後ろのブロックから処理して挿入によるずれを回避）
        var blocks = new List<List<EmojiMatcher.Occurrence>>();
        foreach (var occ in occurrences)
        {
            if (blocks.Count > 0 && blocks[^1][^1].EndExclusive == occ.Start)
            {
                blocks[^1].Add(occ);
            }
            else
            {
                blocks.Add(new List<EmojiMatcher.Occurrence> { occ });
            }
        }

        // 基準時刻 T をブロックごとに先に計算する
        // （タグ付けやスペーサー挿入でインデックスや時刻が変化する前の状態で判定するため）
        var blockTimes = new int?[blocks.Count];
        for (int b = 0; b < blocks.Count; b++)
        {
            int blockEnd = blocks[b][^1].EndExclusive;

            // 基準時刻 T = ブロックの直後にある実文字（絵文字・スペーサー以外）の最初のタグ（無ければ行末のタグ）
            blockTimes[b] = BaseTime(line, blockEnd, emojiUnitIndexes);
        }

        for (int b = blocks.Count - 1; b >= 0; b--)
        {
            var block = blocks[b];

            // 絵文字ユニットのタグをいったんクリア
            foreach (var occ in block)
            {
                for (int i = occ.Start; i < occ.EndExclusive; i++)
                {
                    line.Chars[i].TimeCs = null;
                    line.Chars[i].CheckCount = 0;
                }
            }

            if (blockTimes[b] is not int baseT) continue; // 基準にできるタグがない → タグは付けない

            int lead = Math.Max(0, baseT - settings.LeadCs);

            if (settings.PerEmoji)
            {
                // 各出現の先頭に [T−n]、出現の間に [T] のスペーサーを挟む
                for (int k = block.Count - 1; k >= 0; k--)
                {
                    var occ = block[k];
                    line.Chars[occ.Start].TimeCs = lead;
                    line.Chars[occ.Start].CheckCount = 1;
                    if (k > 0)
                    {
                        line.Chars.Insert(occ.Start, new CharUnit { Text = CharUnit.Spacer, TimeCs = baseT });
                    }
                }
            }
            else
            {
                // ブロックの先頭のみ [T−n]
                line.Chars[block[0].Start].TimeCs = lead;
                line.Chars[block[0].Start].CheckCount = 1;
            }
        }
    }

    /// <summary>タグの基準にできる実文字が無い絵文字ブロックを含む行かどうか（警告表示用）。</summary>
    public static bool HasUntaggableEmoji(LyricsLine line, EmojiMatcher matcher)
    {
        if (matcher.IsEmpty) return false;
        var occurrences = matcher.FindOccurrences(line.Chars);
        if (occurrences.Count == 0) return false;

        var emojiUnitIndexes = new HashSet<int>();
        foreach (var occ in occurrences)
        {
            for (int i = occ.Start; i < occ.EndExclusive; i++) emojiUnitIndexes.Add(i);
        }

        foreach (var occ in occurrences)
        {
            bool found = false;
            for (int i = occ.EndExclusive; i < line.Chars.Count; i++)
            {
                var c = line.Chars[i];
                if (c.IsSpacer || emojiUnitIndexes.Contains(i)) continue;
                if (c.TimeCs is not null)
                {
                    found = true;
                    break;
                }
            }
            if (!found && line.EndTimeCs is null) return true;
        }
        return false;
    }
}
