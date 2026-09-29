using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Formats;

/// <summary>
/// ニコカラメーカー3 の「パート別にフォントを設定（歌詞の文字と同じ名称のフォント設定を適用する）」相当。
/// 行内にフォント設定名と同じ文字列（絵文字の置き換え文字列など）が現れると、そこから先の文字に
/// そのフォント設定を適用する。長い名前を優先し、2 連タグ用スペーサーを飛ばして照合する。行ごとの手動指定（FontSetName）があれば
/// その行はそれで統一する。行を順に渡すと、前の行のフォントを引き継ぐ（行が変わっても維持する場合）。
/// </summary>
public sealed class N3FontResolver
{
    private readonly Dictionary<string, int> _byName = new(StringComparer.Ordinal);
    private readonly EmojiMatcher _matcher;
    private readonly int _default;
    private readonly bool _continue;
    private int _current;

    /// <param name="fontNames">フォント設定名（添字がフォント設定の番号。同じ名前が複数あれば先のものを使う）。</param>
    /// <param name="defaultName">最初に使うフォント設定名（無い名前や null なら 0 番）。</param>
    /// <param name="continueAcrossLines">true: 行が変わってもフォントを維持する / false: 行ごとに既定へ戻す。</param>
    public N3FontResolver(IReadOnlyList<string> fontNames, string? defaultName, bool continueAcrossLines)
    {
        for (int i = 0; i < fontNames.Count; i++)
        {
            string n = fontNames[i];
            if (n.Length > 0 && !_byName.ContainsKey(n)) _byName[n] = i;
        }
        _matcher = new EmojiMatcher(_byName.Keys);
        _default = defaultName is not null && _byName.TryGetValue(defaultName, out int d) ? d : 0;
        _current = _default;
        _continue = continueAcrossLines;
    }

    /// <summary>行の各 CharUnit に適用するフォント設定の番号（fontNames の添字）。</summary>
    public int[] Resolve(LyricsLine line)
    {
        var result = new int[line.Chars.Count];
        if (!_continue) _current = _default;

        var changes = new Dictionary<int, int>();
        if (!_matcher.IsEmpty)
        {
            // 2 連タグ用スペーサーを除いた文字の並びで照合し、出現の先頭を元の位置へ戻す
            // （ニコカラメーカー3 と同じく、「（麻衣）<SP>（のりこ）」も「（麻衣）（のりこ）」に当たる。
            //   EmojiMatcher 自体はスペーサーをまたがないので、ここだけで飛ばす）
            var positions = new List<int>(line.Chars.Count);
            var compact = new List<CharUnit>(line.Chars.Count);
            for (int i = 0; i < line.Chars.Count; i++)
            {
                if (line.Chars[i].IsSpacer) continue;
                positions.Add(i);
                compact.Add(line.Chars[i]);
            }
            foreach (var occ in _matcher.FindOccurrences(compact))
            {
                changes[positions[occ.Start]] = _byName[occ.Value];
            }
        }
        for (int i = 0; i < result.Length; i++)
        {
            if (changes.TryGetValue(i, out int idx)) _current = idx;
            result[i] = _current;
        }

        if (line.FontSetName is string manual && _byName.TryGetValue(manual, out int m))
        {
            Array.Fill(result, m);
        }
        return result;
    }

    /// <summary>
    /// 文書全体で、フォント設定名ごとに適用される文字数（2 連タグ用スペーサーを除く CharUnit の数。
    /// 絵文字の置き換え文字列は文字数ぶん数える）。使われていない名前も 0 で含む。
    /// </summary>
    public static Dictionary<string, int> CountUsage(LyricsDocument doc, IReadOnlyList<string> fontNames, string? defaultName, bool continueAcrossLines) =>
        CountUsage(new[] { doc }, fontNames, defaultName, continueAcrossLines);

    /// <summary>
    /// 複数の文書（歌詞設定タブ）を順に通したときの、フォント設定名ごとの文字数。
    /// n3proj の書き出しと同じく、前の文書の最後のフォントを次の文書へ引き継ぐ。
    /// </summary>
    public static Dictionary<string, int> CountUsage(IEnumerable<LyricsDocument> documents, IReadOnlyList<string> fontNames, string? defaultName, bool continueAcrossLines)
    {
        var usage = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string n in fontNames) usage.TryAdd(n, 0);
        if (fontNames.Count == 0) return usage;

        var resolver = new N3FontResolver(fontNames, defaultName, continueAcrossLines);
        foreach (var doc in documents)
        {
            foreach (var line in doc.Lines)
            {
                if (line.IsEmpty) continue;
                int[] fonts = resolver.Resolve(line);
                for (int i = 0; i < fonts.Length; i++)
                {
                    if (!line.Chars[i].IsSpacer) usage[fontNames[fonts[i]]]++;
                }
            }
        }
        return usage;
    }

    /// <summary>指定したフォント設定が 1 文字以上に適用される行の番号（doc.Lines の添字、昇順）。</summary>
    public static IReadOnlyList<int> LinesUsing(LyricsDocument doc, IReadOnlyList<string> fontNames, string? defaultName, bool continueAcrossLines, string fontName) =>
        LinesUsing(new[] { doc }, fontNames, defaultName, continueAcrossLines, fontName).Select(p => p.Line).ToList();

    /// <summary>
    /// 複数の文書（歌詞設定タブ）を順に通したときに、指定したフォント設定が 1 文字以上に適用される行
    /// （文書の番号と doc.Lines の添字。文書順・行順）。n3proj の書き出しや複数の文書の <see cref="CountUsage(IEnumerable{LyricsDocument}, IReadOnlyList{string}, string?, bool)"/>
    /// と同じく、前の文書の最後のフォントを次の文書へ引き継ぐ。
    /// </summary>
    public static IReadOnlyList<(int Document, int Line)> LinesUsing(IEnumerable<LyricsDocument> documents, IReadOnlyList<string> fontNames, string? defaultName, bool continueAcrossLines, string fontName)
    {
        var result = new List<(int Document, int Line)>();
        int target = -1;
        for (int i = 0; i < fontNames.Count; i++)
        {
            if (string.Equals(fontNames[i], fontName, StringComparison.Ordinal))
            {
                target = i;
                break;
            }
        }
        if (target < 0) return result;

        var resolver = new N3FontResolver(fontNames, defaultName, continueAcrossLines);
        int di = 0;
        foreach (var doc in documents)
        {
            for (int li = 0; li < doc.Lines.Count; li++)
            {
                var line = doc.Lines[li];
                if (line.IsEmpty) continue;
                int[] fonts = resolver.Resolve(line);
                for (int i = 0; i < fonts.Length; i++)
                {
                    if (fonts[i] == target && !line.Chars[i].IsSpacer)
                    {
                        result.Add((di, li));
                        break;
                    }
                }
            }
            di++;
        }
        return result;
    }
}
