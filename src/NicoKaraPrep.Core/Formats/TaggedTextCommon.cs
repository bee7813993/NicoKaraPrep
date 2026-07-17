using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Formats;

/// <summary>
/// lrc / テキスト編集モード形式で共通のドキュメントレベル処理
/// （@タグ行の振り分け・@Ruby の歌詞への投影）。
/// </summary>
internal static class TaggedTextCommon
{
    /// <summary>テキスト全体を解析する。行の解析は parseLine に委譲する。</summary>
    public static LyricsDocument ParseDocument(string text, Func<string, LyricsLine> parseLine)
    {
        var doc = new LyricsDocument();
        var rubyEntries = new List<RubyEntry>();

        foreach (string line in SplitLines(text))
        {
            if (line.Length >= 2 && line[0] == '@')
            {
                int eq = line.IndexOf('=');
                if (eq > 1)
                {
                    string name = line[1..eq].Trim();
                    string value = line[(eq + 1)..];

                    if (IsRubyTagName(name))
                    {
                        rubyEntries.Add(RubyEntry.ParseTagValue(value));
                        continue;
                    }
                    if (name.Equals("Emoji", StringComparison.OrdinalIgnoreCase))
                    {
                        doc.EmojiEntries.Add(EmojiEntry.ParseTagValue(value));
                        continue;
                    }
                    doc.Metadata.Add(new MetadataTag(name, value));
                    continue;
                }
            }

            doc.Lines.Add(parseLine(line));
        }

        ApplyRubyEntries(doc, rubyEntries);
        return doc;
    }

    public static bool IsRubyTagName(string name)
    {
        if (!name.StartsWith("Ruby", StringComparison.OrdinalIgnoreCase)) return false;
        string rest = name[4..];
        return rest.Length > 0 && rest.All(char.IsAsciiDigit);
    }

    public static IEnumerable<string> SplitLines(string text)
    {
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            yield return line;
        }
    }

    /// <summary>
    /// @Ruby エントリを歌詞行の文字へ投影する。
    /// 同じ親文字に複数の読みがある場合は適用開始/終了時刻で選択する。
    /// 一度も使われなかったエントリは <see cref="LyricsDocument.UnappliedRubyEntries"/> に残す。
    /// 既にルビが付いている文字（インラインルビ由来）はスキップする。
    /// </summary>
    public static void ApplyRubyEntries(LyricsDocument doc, List<RubyEntry> entries)
    {
        if (entries.Count == 0) return;

        var used = new HashSet<RubyEntry>();
        var byParent = entries
            .Where(e => e.Parent.Length > 0)
            .GroupBy(e => e.Parent)
            .ToDictionary(g => g.Key, g => g.ToList());
        var parentsByLength = byParent.Keys.OrderByDescending(p => p.Length).ToList();

        int prevailingCs = 0;

        foreach (var line in doc.Lines)
        {
            var realChars = line.Chars.Where(c => !c.IsSpacer).ToList();
            int i = 0;
            while (i < realChars.Count)
            {
                if (realChars[i].TimeCs is int t0) prevailingCs = t0;

                bool applied = false;
                foreach (string parent in parentsByLength)
                {
                    if (!MatchesAt(realChars, i, parent, out int matchLen)) continue;

                    int matchCs = FirstTimeInRange(realChars, i, matchLen) ?? prevailingCs;
                    var entry = SelectEntry(byParent[parent], matchCs);
                    if (entry is null) continue;

                    if (Enumerable.Range(i, matchLen).Any(k => realChars[k].HasRubyInfo)) continue;

                    // ルビ内のワイプタグ（グループ先頭からの相対時刻）を分離する
                    var (plainRuby, segments) = RubyEntry.ParseWipeSegments(entry.Ruby);

                    for (int k = 0; k < matchLen; k++)
                    {
                        var c = realChars[i + k];
                        c.Ruby = k == 0 ? plainRuby : "";
                        c.RubyJoinsNext = k < matchLen - 1;
                    }

                    // ワイプタグ付きなら先頭文字にチェック数・口パク補助タグ（絶対時刻）として復元する
                    if (segments.Count > 1)
                    {
                        var head = realChars[i];
                        head.CheckCount = Math.Max(1, segments.Count);
                        head.AuxTimeTagsCs.Clear();
                        foreach (var (_, rel) in segments.Skip(1))
                        {
                            if (rel is int r) head.AuxTimeTagsCs.Add(matchCs + r);
                        }
                    }
                    used.Add(entry);
                    i += matchLen;
                    applied = true;
                    break;
                }

                if (!applied) i++;
            }
        }

        foreach (var e in entries)
        {
            if (!used.Contains(e)) doc.UnappliedRubyEntries.Add(e);
        }
    }

    private static bool MatchesAt(List<CharUnit> chars, int start, string parent, out int matchLen)
    {
        matchLen = 0;
        int pos = 0;
        int i = start;
        while (pos < parent.Length)
        {
            if (i >= chars.Count) return false;
            string t = chars[i].Text;
            if (pos + t.Length > parent.Length) return false;
            if (string.CompareOrdinal(parent, pos, t, 0, t.Length) != 0) return false;
            pos += t.Length;
            i++;
        }
        matchLen = i - start;
        return true;
    }

    private static int? FirstTimeInRange(List<CharUnit> chars, int start, int len)
    {
        for (int k = 0; k < len; k++)
        {
            if (chars[start + k].TimeCs is int t) return t;
        }
        return null;
    }

    private static RubyEntry? SelectEntry(List<RubyEntry> candidates, int timeCs)
    {
        RubyEntry? best = null;
        int bestStart = int.MinValue;
        foreach (var e in candidates)
        {
            if (e.StartCs is int s && timeCs < s) continue;
            if (e.EndCs is int en && timeCs >= en) continue;
            int start = e.StartCs ?? int.MinValue;
            if (best is null || start > bestStart)
            {
                best = e;
                bestStart = start;
            }
        }
        return best;
    }
}
