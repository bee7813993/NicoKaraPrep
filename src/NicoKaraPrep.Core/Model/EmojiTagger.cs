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
/// </summary>
public static class EmojiTagger
{
    /// <summary>
    /// 行の charIndex 位置（CharUnit 単位）に絵文字（置き換え文字列全体）を挿入し、
    /// 行全体の絵文字タグを付け直す。挿入された CharUnit 数を返す。
    /// </summary>
    public static int InsertEmoji(LyricsLine line, int charIndex, string replaceString, EmojiMatcher matcher, EmojiTagSettings settings)
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
        return count;
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

            // 基準時刻 T = ブロックの直後にある実文字（絵文字・スペーサー以外）の最初のタグ
            int? t = null;
            for (int i = blockEnd; i < line.Chars.Count; i++)
            {
                var c = line.Chars[i];
                if (c.IsSpacer || emojiUnitIndexes.Contains(i)) continue;
                if (c.TimeCs is int time)
                {
                    t = time;
                    break;
                }
            }
            blockTimes[b] = t ?? line.EndTimeCs;
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
