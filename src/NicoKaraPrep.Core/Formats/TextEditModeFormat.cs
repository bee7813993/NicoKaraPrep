using System.Text;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Formats;

/// <summary>テキスト編集モード形式の書き出しオプション。</summary>
public sealed class TextEditWriteOptions
{
    /// <summary>改行コード。デフォルトは CRLF。</summary>
    public string NewLine { get; set; } = "\r\n";

    /// <summary>チェック数（[n|mm:ss:cc] / [n]）を出力するか。</summary>
    public bool IncludeChecks { get; set; } = true;

    /// <summary>ルビ（{親|ルビ}）を出力するか。</summary>
    public bool IncludeRuby { get; set; } = true;

    /// <summary>@Emoji タグ行を出力するか。</summary>
    public bool EmitEmojiTags { get; set; } = true;
}

/// <summary>
/// RhythmicaLyrics「テキスト編集モード」（チェック数・ルビ表示あり）のテキスト形式のパーサ / ライタ。
///
/// 書式（RhythmicaLyrics v64 ソース *YOMIKARA の解析結果）:
///   - タイムタグ:               [mm:ss:cc] / [mm:ss]
///   - チェック数付きタイムタグ: [n|mm:ss:cc]
///   - チェック数のみ:           [n]
///   - ルビ:                     {親文字|ルビ}（複数親文字は ＋ 区切り、各部の先頭にタグ可、
///                               部内の追加 [mm:ss:cc] は口パク用補助タグ）
///   - 2連タグ:                  連続するタグはスペーサー文字（0x1A）を挟んで表現
///   - エスケープ:               $2759=| $271A=＋ $2775=} $25A8=スペーサー、その他 $XXXX=UTF-16 コード単位
/// </summary>
public static class TextEditModeFormat
{
    private const string EscapePipe = "$2759";
    private const string EscapePlus = "$271A";
    private const string EscapeCloseBrace = "$2775";
    private const string EscapeSpacer = "$25A8";

    // ---------------------------------------------------------------- Parse

    /// <summary>テキスト編集モード形式の全文を解析する。</summary>
    public static LyricsDocument Parse(string text) =>
        TaggedTextCommon.ParseDocument(text, ParseLyricLine);

    /// <summary>歌詞 1 行を解析する。</summary>
    public static LyricsLine ParseLyricLine(string line)
    {
        var result = new LyricsLine();
        int? pendingTag = null;
        int? pendingCheck = null;
        int pos = 0;

        while (pos < line.Length)
        {
            char ch = line[pos];

            if (ch == '[')
            {
                if (TryParseCheckedTag(line, pos, out int chk, out int cs, out int len))
                {
                    FlushPendingAsSpacer(result, ref pendingTag, ref pendingCheck);
                    pendingTag = cs;
                    pendingCheck = chk;
                    pos += len;
                    continue;
                }
                if (TimeTag.TryParseAt(line, pos, out int cs2, out int len2))
                {
                    FlushPendingAsSpacer(result, ref pendingTag, ref pendingCheck);
                    pendingTag = cs2;
                    pos += len2;
                    continue;
                }
                if (TryParseCheckOnly(line, pos, out int chk2, out int len3))
                {
                    FlushPendingAsSpacer(result, ref pendingTag, ref pendingCheck);
                    pendingCheck = chk2;
                    pos += len3;
                    continue;
                }
            }

            if (ch == '{')
            {
                int close = line.IndexOf('}', pos + 1);
                int sep = line.IndexOf('|', pos + 1);
                if (close > 0 && sep > pos && sep < close)
                {
                    // ルビ塊の直前のタグは 2連タグ扱い（RhythmicaLyrics と同じ）
                    FlushPendingAsSpacer(result, ref pendingTag, ref pendingCheck);
                    ParseRubyBlock(result, line[(pos + 1)..sep], line[(sep + 1)..close]);
                    pos = close + 1;
                    continue;
                }
            }

            string text = ReadCharToken(line, ref pos);
            result.Chars.Add(new CharUnit
            {
                Text = text,
                TimeCs = pendingTag,
                CheckCount = pendingCheck ?? (pendingTag is null ? 0 : 1),
            });
            pendingTag = null;
            pendingCheck = null;
        }

        if (pendingTag is int end) result.EndTimeCs = end;
        return result;
    }

    private static void FlushPendingAsSpacer(LyricsLine line, ref int? pendingTag, ref int? pendingCheck)
    {
        if (pendingTag is null && pendingCheck is null) return;
        line.Chars.Add(new CharUnit
        {
            Text = CharUnit.Spacer,
            TimeCs = pendingTag,
            CheckCount = pendingCheck ?? 0,
        });
        pendingTag = null;
        pendingCheck = null;
    }

    /// <summary>[n|mm:ss:cc] を解析する。</summary>
    private static bool TryParseCheckedTag(string s, int pos, out int check, out int cs, out int length)
    {
        check = 0;
        cs = 0;
        length = 0;
        if (pos >= s.Length || s[pos] != '[') return false;
        int i = pos + 1;
        int n = 0, digits = 0;
        while (i < s.Length && char.IsAsciiDigit(s[i]) && digits < 4)
        {
            n = n * 10 + (s[i] - '0');
            i++;
            digits++;
        }
        if (digits == 0 || i >= s.Length || s[i] != '|') return false;
        i++;
        // "mm:ss:cc]" 部分
        string rest = "[" + s[i..Math.Min(s.Length, i + 9)];
        if (!TimeTag.TryParseAt(rest, 0, out cs, out int tagLen) || tagLen != 10) return false;
        check = n;
        length = i + 9 - pos;
        return true;
    }

    /// <summary>[n] を解析する。</summary>
    private static bool TryParseCheckOnly(string s, int pos, out int check, out int length)
    {
        check = 0;
        length = 0;
        if (pos >= s.Length || s[pos] != '[') return false;
        int i = pos + 1;
        int n = 0, digits = 0;
        while (i < s.Length && char.IsAsciiDigit(s[i]) && digits < 4)
        {
            n = n * 10 + (s[i] - '0');
            i++;
            digits++;
        }
        if (digits == 0 || i >= s.Length || s[i] != ']') return false;
        check = n;
        length = i + 1 - pos;
        return true;
    }

    /// <summary>{親文字|ルビ} ブロックを解析して行に文字を追加する。</summary>
    private static void ParseRubyBlock(LyricsLine line, string parentText, string rubyText)
    {
        // 親文字を 1 文字ずつに分解（$XXXX エスケープ対応）
        var parents = new List<string>();
        int p = 0;
        while (p < parentText.Length) parents.Add(ReadCharToken(parentText, ref p));
        if (parents.Count == 0) parents.Add(CharUnit.Spacer);

        // ルビを ＋ で分割し、親文字数に合わせて空文字を補う
        var parts = rubyText.Split('＋').ToList();
        while (parts.Count < parents.Count) parts.Add("");

        for (int i = 0; i < parents.Count; i++)
        {
            var unit = new CharUnit { Text = parents[i] };
            string part = parts[i];
            int pos = 0;

            // 先頭のタグ（[n|mm:ss:cc] / [mm:ss:cc] / [n]）
            if (TryParseCheckedTag(part, 0, out int chk, out int cs, out int len))
            {
                unit.TimeCs = cs;
                unit.CheckCount = chk;
                pos = len;
            }
            else if (TimeTag.TryParseAt(part, 0, out int cs2, out int len2))
            {
                unit.TimeCs = cs2;
                unit.CheckCount = 1;
                pos = len2;
            }
            else if (TryParseCheckOnly(part, 0, out int chk2, out int len3))
            {
                unit.CheckCount = chk2;
                pos = len3;
            }

            // 残りから補助タイムタグを抜き出し、ルビ本文を組み立てる
            var ruby = new StringBuilder();
            while (pos < part.Length)
            {
                if (part[pos] == '[' && TimeTag.TryParseAt(part, pos, out int aux, out int auxLen))
                {
                    unit.AuxTimeTagsCs.Add(aux);
                    pos += auxLen;
                    continue;
                }
                ruby.Append(ReadCharToken(part, ref pos));
            }

            unit.Ruby = ruby.ToString();
            unit.RubyJoinsNext = i < parents.Count - 1;
            line.Chars.Add(unit);
        }
    }

    /// <summary>1 文字トークン（$XXXX エスケープ・サロゲートペア対応）を読み取る。</summary>
    private static string ReadCharToken(string s, ref int pos)
    {
        if (s[pos] == '$' && TryParseHex4(s, pos + 1, out int code))
        {
            pos += 5;
            // サロゲートペア（$XXXX$YYYY）
            if (code is >= 0xD800 and <= 0xDBFF &&
                pos < s.Length && s[pos] == '$' && TryParseHex4(s, pos + 1, out int low) &&
                low is >= 0xDC00 and <= 0xDFFF)
            {
                pos += 5;
                return new string(new[] { (char)code, (char)low });
            }
            return code switch
            {
                0x2759 => "|",
                0x271A => "＋",
                0x2775 => "}",
                0x25A8 => CharUnit.Spacer,
                _ => ((char)code).ToString(),
            };
        }

        int len = char.IsHighSurrogate(s[pos]) && pos + 1 < s.Length && char.IsLowSurrogate(s[pos + 1]) ? 2 : 1;
        string t = s.Substring(pos, len);
        pos += len;
        return t;
    }

    private static bool TryParseHex4(string s, int pos, out int value)
    {
        value = 0;
        if (pos + 4 > s.Length) return false;
        for (int i = 0; i < 4; i++)
        {
            char c = s[pos + i];
            int d = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'A' and <= 'F' => c - 'A' + 10,
                _ => -1,
            };
            if (d < 0) return false;
            value = value * 16 + d;
        }
        return true;
    }

    // ---------------------------------------------------------------- Write

    /// <summary>ドキュメントをテキスト編集モード形式の全文へ書き出す。</summary>
    public static string Write(LyricsDocument doc, TextEditWriteOptions? options = null)
    {
        options ??= new TextEditWriteOptions();
        var sb = new StringBuilder();
        string nl = options.NewLine;

        foreach (var m in doc.Metadata)
        {
            sb.Append(m.ToLine()).Append(nl);
        }
        if (options.EmitEmojiTags)
        {
            foreach (var e in doc.EmojiEntries)
            {
                sb.Append("@Emoji=").Append(e.ToTagValue()).Append(nl);
            }
        }

        foreach (var line in doc.Lines)
        {
            sb.Append(WriteLyricLine(line, options)).Append(nl);
        }

        return sb.ToString();
    }

    /// <summary>歌詞 1 行をテキスト編集モード形式にする。</summary>
    public static string WriteLyricLine(LyricsLine line, TextEditWriteOptions? options = null)
    {
        options ??= new TextEditWriteOptions();
        var sb = new StringBuilder();
        int i = 0;

        while (i < line.Chars.Count)
        {
            var c = line.Chars[i];

            if (options.IncludeRuby && c.HasRubyInfo && !c.IsSpacer)
            {
                // ルビグループを {親|ルビ} ブロックにまとめる
                var group = new List<CharUnit>();
                int j = i;
                while (j < line.Chars.Count)
                {
                    var g = line.Chars[j];
                    if (g.IsSpacer) { j++; continue; } // グループ内のスペーサーは無視（通常存在しない）
                    group.Add(g);
                    j++;
                    if (!g.RubyJoinsNext) break;
                }

                sb.Append('{');
                foreach (var g in group) sb.Append(EscapeParent(g.Text));
                sb.Append('|');
                for (int k = 0; k < group.Count; k++)
                {
                    if (k > 0) sb.Append('＋');
                    var g = group[k];
                    sb.Append(TagPrefix(g, options));
                    sb.Append(EscapeRuby(g.Ruby ?? ""));
                    foreach (int aux in g.AuxTimeTagsCs) sb.Append(TimeTag.Format(aux));
                }
                sb.Append('}');
                i = j;
                continue;
            }

            if (c.IsSpacer)
            {
                string prefix = TagPrefix(c, options);
                if (prefix.Length > 0)
                {
                    sb.Append(prefix); // タグのみ出力 → 次のタグと連続して 2連タグになる
                }
                else
                {
                    sb.Append(EscapeSpacer);
                }
                i++;
                continue;
            }

            sb.Append(TagPrefix(c, options));
            sb.Append(EscapeText(c.Text));
            i++;
        }

        if (line.EndTimeCs is int end) sb.Append(TimeTag.Format(end));
        return sb.ToString();
    }

    /// <summary>文字の直前に付くタグ表現（[n|mm:ss:cc] / [mm:ss:cc] / [n]）。</summary>
    private static string TagPrefix(CharUnit c, TextEditWriteOptions options)
    {
        if (c.TimeCs is int t)
        {
            if (options.IncludeChecks && c.CheckCount > 0)
            {
                string tag = TimeTag.Format(t);
                return $"[{c.CheckCount}|{tag[1..]}";
            }
            return TimeTag.Format(t);
        }
        if (options.IncludeChecks && c.CheckCount > 0)
        {
            return $"[{c.CheckCount}]";
        }
        return "";
    }

    // ---------------------------------------------------------- エスケープ

    /// <summary>通常テキスト中の文字をエスケープする。</summary>
    private static string EscapeText(string ch) => ch switch
    {
        "|" => EscapePipe,
        "{" => "$007B",
        "}" => "$007D",
        "[" => "$005B",
        "$" => "$0024",
        CharUnit.Spacer => EscapeSpacer,
        _ => ch,
    };

    /// <summary>ルビ親文字中の文字をエスケープする。</summary>
    private static string EscapeParent(string ch) => ch switch
    {
        "|" => EscapePipe,
        "{" => "$007B",
        "}" => "$007D",
        "[" => "$005B",
        "$" => "$0024",
        CharUnit.Spacer => EscapeSpacer,
        _ => ch,
    };

    /// <summary>ルビ本文をエスケープする。</summary>
    private static string EscapeRuby(string ruby)
    {
        var sb = new StringBuilder(ruby.Length);
        foreach (char c in ruby)
        {
            switch (c)
            {
                case '|': sb.Append(EscapePipe); break;
                case '＋': sb.Append(EscapePlus); break;
                case '}': sb.Append(EscapeCloseBrace); break;
                case '{': sb.Append("$007B"); break;
                case '[': sb.Append("$005B"); break;
                case '$': sb.Append("$0024"); break;
                case '\u001A': sb.Append(EscapeSpacer); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}
