using System.Text;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Formats;

/// <summary>lrc 書き出しオプション。</summary>
public sealed class LrcWriteOptions
{
    /// <summary>改行コード。デフォルトは CRLF。</summary>
    public string NewLine { get; set; } = "\r\n";

    /// <summary>@Emoji タグを出力するか。</summary>
    public bool EmitEmojiTags { get; set; } = true;

    /// <summary>true なら歌詞中で使われている置き換え文字列の @Emoji だけを出力する。デフォルトは全件出力。</summary>
    public bool EmitOnlyUsedEmoji { get; set; }

    /// <summary>
    /// @Emoji の出力元リスト。null ならドキュメント内の定義（doc.EmojiEntries）を使う。
    /// アプリからはグローバル分も含む実効リストを渡す。
    /// </summary>
    public IReadOnlyList<EmojiEntry>? EmojiEntriesOverride { get; set; }

    /// <summary>
    /// lrc の保存先フォルダ。指定すると、このフォルダと同じかその配下にある画像は
    /// 相対パスで出力する（フォルダごと移動・共有できるように）。配下にない画像は絶対パスのまま。
    /// </summary>
    public string? BaseFolder { get; set; }

    /// <summary>@Ruby タグを出力するか。</summary>
    public bool EmitRubyTags { get; set; } = true;
}

/// <summary>
/// タイムタグ付き lrc（ルビ拡張規格・@Emoji 対応）のパーサ / ライタ。
/// </summary>
public static class LrcFormat
{
    // ---------------------------------------------------------------- Parse

    /// <summary>lrc テキストを解析して <see cref="LyricsDocument"/> を返す。</summary>
    public static LyricsDocument Parse(string text) =>
        TaggedTextCommon.ParseDocument(text, ParseLyricLine);

    /// <summary>歌詞 1 行（タイムタグ＋文字）を解析する。</summary>
    public static LyricsLine ParseLyricLine(string line)
    {
        var result = new LyricsLine();
        int? pendingTag = null;
        int pos = 0;

        while (pos < line.Length)
        {
            if (line[pos] == '[' && TimeTag.TryParseAt(line, pos, out int cs, out int tagLen))
            {
                if (pendingTag is int prev)
                {
                    // 連続タイムタグ → 前のタグはスペーサー文字として保持（2連タグ）
                    result.Chars.Add(new CharUnit { Text = CharUnit.Spacer, TimeCs = prev });
                }
                pendingTag = cs;
                pos += tagLen;
                continue;
            }

            // 1 コードポイント消費
            int len = char.IsHighSurrogate(line[pos]) && pos + 1 < line.Length && char.IsLowSurrogate(line[pos + 1]) ? 2 : 1;
            result.Chars.Add(new CharUnit
            {
                Text = line.Substring(pos, len),
                TimeCs = pendingTag,
                CheckCount = pendingTag is null ? 0 : 1, // lrc にはチェック数情報がないためタグ有り=1で補完
            });
            pendingTag = null;
            pos += len;
        }

        if (pendingTag is int end) result.EndTimeCs = end;
        return result;
    }

    // ---------------------------------------------------------------- Write

    /// <summary>ドキュメントを lrc テキストへ書き出す。</summary>
    public static string Write(LyricsDocument doc, LrcWriteOptions? options = null)
    {
        options ??= new LrcWriteOptions();
        var sb = new StringBuilder();
        string nl = options.NewLine;

        foreach (var m in doc.Metadata)
        {
            sb.Append(m.ToLine()).Append(nl);
        }

        if (options.EmitEmojiTags)
        {
            foreach (var e in SelectEmojiEntries(doc, options))
            {
                sb.Append("@Emoji=").Append(WithRelativeImagePaths(e, options.BaseFolder).ToTagValue()).Append(nl);
            }
        }

        if (options.EmitRubyTags)
        {
            int no = 1;
            foreach (var r in BuildRubyEntries(doc))
            {
                sb.Append("@Ruby").Append(no++).Append('=').Append(r.ToTagValue()).Append(nl);
            }
        }

        foreach (var line in doc.Lines)
        {
            sb.Append(WriteLyricLine(line)).Append(nl);
        }

        return sb.ToString();
    }

    private static IEnumerable<EmojiEntry> SelectEmojiEntries(LyricsDocument doc, LrcWriteOptions options)
    {
        IEnumerable<EmojiEntry> source =
            (options.EmojiEntriesOverride ?? (IReadOnlyList<EmojiEntry>)doc.EmojiEntries)
            .Where(e => e.ReplaceChar.Length > 0);
        if (!options.EmitOnlyUsedEmoji) return source;

        // 置き換え文字列は複数文字のことがあるため、1 文字単位ではなく出現検索で判定する
        var matcher = new EmojiMatcher(source.Select(e => e.ReplaceChar));
        var used = new HashSet<string>();
        foreach (var line in doc.Lines)
        {
            foreach (var occ in matcher.FindOccurrences(line.Chars))
            {
                used.Add(occ.Value);
            }
        }
        return source.Where(e => used.Contains(e.ReplaceChar));
    }

    /// <summary>保存先フォルダ配下にある画像パスを相対パスへ変換したエントリを返す（対象外はそのまま）。</summary>
    private static EmojiEntry WithRelativeImagePaths(EmojiEntry e, string? baseFolder)
    {
        if (string.IsNullOrEmpty(baseFolder)) return e;

        string before = TryMakeRelative(e.ImageBefore, baseFolder);
        string? after = string.IsNullOrEmpty(e.ImageAfter) ? e.ImageAfter : TryMakeRelative(e.ImageAfter!, baseFolder);
        if (before == e.ImageBefore && after == e.ImageAfter) return e;

        var clone = e.Clone();
        clone.ImageBefore = before;
        clone.ImageAfter = after;
        return clone;
    }

    private static string TryMakeRelative(string path, string baseFolder)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path)) return path;
            string rel = Path.GetRelativePath(baseFolder, path);
            // 「..」で上へ出る・別ドライブになる場合は配下ではないので絶対パスのまま
            if (rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel)) return path;
            return rel;
        }
        catch (Exception)
        {
            return path;
        }
    }

    /// <summary>歌詞 1 行をタイムタグ付きテキストにする。</summary>
    public static string WriteLyricLine(LyricsLine line)
    {
        var sb = new StringBuilder();
        foreach (var c in line.Chars)
        {
            if (c.TimeCs is int t) sb.Append(TimeTag.Format(t));
            if (!c.IsSpacer) sb.Append(c.Text);
        }
        if (line.EndTimeCs is int end) sb.Append(TimeTag.Format(end));
        return sb.ToString();
    }

    /// <summary>
    /// 文字単位のルビ情報から @Ruby エントリを生成する。
    /// 同じ親文字がすべて同じ読みなら時刻なしの 1 件に集約し、
    /// 読みが異なる場合は適用開始・終了時刻で区間を区切ったエントリを出力する。
    /// </summary>
    public static List<RubyEntry> BuildRubyEntries(LyricsDocument doc)
    {
        // グループ（親文字, ルビ, 時刻）を文書順に収集
        var groups = new List<(string Parent, string Ruby, int TimeCs)>();
        int prevailingCs = 0;

        foreach (var line in doc.Lines)
        {
            var realChars = line.Chars.Where(c => !c.IsSpacer).ToList();
            int i = 0;
            while (i < realChars.Count)
            {
                if (realChars[i].TimeCs is int t0) prevailingCs = t0;

                if (!realChars[i].HasRubyInfo)
                {
                    i++;
                    continue;
                }

                var parent = new StringBuilder();
                var ruby = new StringBuilder();
                int? groupTime = null;
                int j = i;
                while (j < realChars.Count)
                {
                    var c = realChars[j];
                    parent.Append(c.Text);
                    ruby.Append(c.Ruby);
                    groupTime ??= c.TimeCs;
                    j++;
                    if (!realChars[j - 1].RubyJoinsNext) break;
                }
                if (ruby.Length > 0)
                {
                    groups.Add((parent.ToString(), ruby.ToString(), groupTime ?? prevailingCs));
                }
                i = j;
            }
        }

        var result = new List<RubyEntry>();
        foreach (var byParent in groups.GroupBy(g => g.Parent))
        {
            var list = byParent.ToList();
            if (list.All(g => g.Ruby == list[0].Ruby))
            {
                result.Add(new RubyEntry(byParent.Key, list[0].Ruby));
                continue;
            }

            // 読みが変わる位置ごとにエントリ化し、適用区間で完全に区切る:
            // 各エントリの適用終了 = 次のエントリの適用開始（最初は開始なし、最後は終了なし）。
            // 区間を閉じずに複数エントリを出すと、ニコカラメーカーが
            // 「ルビ情報が複数存在するため、適切なルビを選択できていない場合があります」と警告する。
            var changes = new List<(string Ruby, int TimeCs)>();
            string? lastRuby = null;
            foreach (var g in list)
            {
                if (g.Ruby == lastRuby) continue;
                changes.Add((g.Ruby, g.TimeCs));
                lastRuby = g.Ruby;
            }
            for (int k = 0; k < changes.Count; k++)
            {
                int? start = k == 0 ? null : changes[k].TimeCs;
                int? end = k + 1 < changes.Count ? changes[k + 1].TimeCs : null;
                result.Add(new RubyEntry(byParent.Key, changes[k].Ruby, start, end));
            }
        }

        result.AddRange(doc.UnappliedRubyEntries);
        return result;
    }
}
