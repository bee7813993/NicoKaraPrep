namespace TimeTagTool.Core.Model;

/// <summary>
/// 歌詞中の絵文字（@Emoji の置き換え文字列）の出現箇所を検索する。
/// 置き換え文字列は「（花帆）」のような複数文字でもよい。長い文字列を優先して照合する。
/// </summary>
public sealed class EmojiMatcher
{
    private readonly List<string> _strings;

    public EmojiMatcher(IEnumerable<string> replaceStrings)
    {
        _strings = replaceStrings
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct()
            .OrderByDescending(s => s.Length)
            .ToList();
    }

    public bool IsEmpty => _strings.Count == 0;

    public IReadOnlyList<string> Strings => _strings;

    /// <summary>絵文字 1 出現分。範囲は CharUnit インデックス（連続した非スペーサー単位）。</summary>
    public readonly record struct Occurrence(int Start, int Length, string Value)
    {
        public int EndExclusive => Start + Length;
    }

    /// <summary>行内の絵文字出現箇所を左から順に列挙する（重複なし、長い文字列優先）。</summary>
    public List<Occurrence> FindOccurrences(IReadOnlyList<CharUnit> chars)
    {
        var result = new List<Occurrence>();
        if (IsEmpty) return result;

        int i = 0;
        while (i < chars.Count)
        {
            if (chars[i].IsSpacer)
            {
                i++;
                continue;
            }

            bool matched = false;
            foreach (string s in _strings)
            {
                if (MatchesAt(chars, i, s, out int len))
                {
                    result.Add(new Occurrence(i, len, s));
                    i += len;
                    matched = true;
                    break;
                }
            }
            if (!matched) i++;
        }
        return result;
    }

    /// <summary>chars[start] から連続する非スペーサー単位の連結が s と一致するか。</summary>
    private static bool MatchesAt(IReadOnlyList<CharUnit> chars, int start, string s, out int length)
    {
        length = 0;
        int pos = 0;
        int i = start;
        while (pos < s.Length)
        {
            if (i >= chars.Count) return false;
            var c = chars[i];
            if (c.IsSpacer) return false; // スペーサーをまたぐ出現は認めない
            string t = c.Text;
            if (pos + t.Length > s.Length) return false;
            if (string.CompareOrdinal(s, pos, t, 0, t.Length) != 0) return false;
            pos += t.Length;
            i++;
        }
        length = i - start;
        return true;
    }

    /// <summary>行内で絵文字を構成している CharUnit の集合。</summary>
    public HashSet<CharUnit> CollectUnits(LyricsLine line)
    {
        var set = new HashSet<CharUnit>();
        foreach (var occ in FindOccurrences(line.Chars))
        {
            for (int k = occ.Start; k < occ.EndExclusive; k++)
            {
                set.Add(line.Chars[k]);
            }
        }
        return set;
    }

    /// <summary>ドキュメント全体で絵文字を構成している CharUnit の集合（検証の除外判定用）。</summary>
    public HashSet<CharUnit> CollectUnits(LyricsDocument doc)
    {
        var set = new HashSet<CharUnit>();
        foreach (var line in doc.Lines)
        {
            foreach (var occ in FindOccurrences(line.Chars))
            {
                for (int k = occ.Start; k < occ.EndExclusive; k++)
                {
                    set.Add(line.Chars[k]);
                }
            }
        }
        return set;
    }
}
