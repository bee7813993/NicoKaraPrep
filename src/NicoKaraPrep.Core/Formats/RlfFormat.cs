using System.Text;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Formats;

/// <summary>
/// RhythmicaLyrics 編集ファイル（.rlf）の読み書き。
/// rlf は hspda の vsave 形式で、以下の変数を含む（RhythmicaLyrics v64 の *hensu_Hozon より）:
///   mojitan[行,列](str)  mojisu[行](int)  t_jikan[列+1,行+1](int)  t_kazu[列+1,行+1](int)
///   mojitan_width[行,列](int)  sakura_timetag/sakura_yomi/sakura_surface/sakura_script[列+1,行+1](str)
///   st_max rentag kaz_retu noin(int)  at_ti2..at_ss2(int)  at_ti_inp2..at_ss_inp2 at_sonota_inp2(str)
///   SaveMojiCode YomiMojiCode(int)
///
/// 文字セルのエンコーディング: Shift-JIS。
///   0x0A("\n") = 行終端セル / 0x1A = 2連タグ用スペーサー /
///   0x11 + "$XXXX"[+"XXXX"] = 非SJIS文字（UTF-16 コード単位、サロゲートペアは 8 桁）
/// </summary>
public static class RlfFormat
{
    static RlfFormat()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private const byte UniMarker = 0x11;
    private const byte SpacerByte = 0x1A;
    private const int DefaultStrBuf = 64;

    private static readonly string[] KnownTagNames = ["Title", "Artist", "Album", "TaggingBy", "SilencemSec"];
    private static readonly string[] FlagVarNames = ["at_ti2", "at_ar2", "at_al2", "at_tb2", "at_ss2"];
    private static readonly string[] InpVarNames = ["at_ti_inp2", "at_ar_inp2", "at_al_inp2", "at_tb_inp2", "at_ss_inp2"];

    // ---------------------------------------------------------------- Read

    public static LyricsDocument Read(byte[] bytes)
    {
        var vars = HspVsaveFile.Read(bytes).ToDictionary(v => v.Name);

        var mojitan = Require(vars, "mojitan");
        var mojisu = Require(vars, "mojisu").IntValues!;
        var tJikan = Require(vars, "t_jikan");
        var tKazu = Require(vars, "t_kazu");
        vars.TryGetValue("mojitan_width", out var mojitanWidth);
        vars.TryGetValue("sakura_timetag", out var sakuraTimetag);
        vars.TryGetValue("sakura_yomi", out var sakuraYomi);
        vars.TryGetValue("sakura_surface", out var sakuraSurface);
        vars.TryGetValue("sakura_script", out var sakuraScript);

        int lines = mojitan.Dims[0];
        var doc = new LyricsDocument();

        for (int cn = 0; cn < lines; cn++)
        {
            var line = new LyricsLine();
            int count = cn < mojisu.Length ? mojisu[cn] : 0;

            for (int st = 0; st < count; st++)
            {
                string text = DecodeCell(GetStr(mojitan, cn, st));
                var unit = new CharUnit
                {
                    Text = text.Length == 0 ? " " : text,
                    TimeCs = TimeFromRaw(GetInt(tJikan, st, cn)),
                    CheckCount = GetInt(tKazu, st, cn),
                    WidthCache = mojitanWidth is null ? 0 : GetIntByLineCol(mojitanWidth, cn, st),
                };

                if (sakuraYomi is not null)
                {
                    string ruby = DecodeCell(GetStr(sakuraYomi, st, cn));
                    if (ruby.Length > 0)
                    {
                        if (ruby.EndsWith('＋'))
                        {
                            unit.Ruby = ruby[..^1];
                            unit.RubyJoinsNext = true;
                        }
                        else
                        {
                            unit.Ruby = ruby;
                        }
                    }
                }

                if (sakuraTimetag is not null)
                {
                    string aux = DecodeCell(GetStr(sakuraTimetag, st, cn));
                    int p = 0;
                    while (p < aux.Length)
                    {
                        if (aux[p] == '[' && TimeTag.TryParseAt(aux, p, out int t, out int len))
                        {
                            unit.AuxTimeTagsCs.Add(t);
                            p += len;
                        }
                        else
                        {
                            p++;
                        }
                    }
                }

                if (sakuraSurface is not null && !(st == 0 && cn == 0))
                {
                    string s = DecodeCell(GetStr(sakuraSurface, st, cn));
                    if (s.Length > 0) unit.SakuraSurface = s;
                }
                if (sakuraScript is not null)
                {
                    string s = DecodeCell(GetStr(sakuraScript, st, cn));
                    if (s.Length > 0) unit.SakuraScript = s;
                }

                line.Chars.Add(unit);
            }

            // 行末タイムタグ（終端セル位置）
            line.EndTimeCs = TimeFromRaw(GetInt(tJikan, count, cn));
            doc.Lines.Add(line);
        }

        // メタデータ
        var rubyEntries = new List<RubyEntry>();
        for (int i = 0; i < KnownTagNames.Length; i++)
        {
            bool enabled = vars.TryGetValue(FlagVarNames[i], out var f) && f.IntValues is [> 0, ..];
            if (!enabled) continue;
            string value = vars.TryGetValue(InpVarNames[i], out var inp) ? DecodeCell(inp.StrElements![0]) : "";
            doc.Metadata.Add(new MetadataTag(KnownTagNames[i], value));
        }
        if (vars.TryGetValue("at_sonota_inp2", out var sonota))
        {
            string text = DecodeCell(sonota.StrElements![0]);
            foreach (string rawLine in text.Split('\n'))
            {
                string l = rawLine.TrimEnd('\r');
                if (l.Length < 2 || l[0] != '@') continue;
                int eq = l.IndexOf('=');
                if (eq <= 1) continue;
                string name = l[1..eq].Trim();
                string value = l[(eq + 1)..];
                if (TaggedTextCommon.IsRubyTagName(name))
                {
                    rubyEntries.Add(RubyEntry.ParseTagValue(value));
                }
                else if (name.Equals("Emoji", StringComparison.OrdinalIgnoreCase))
                {
                    doc.EmojiEntries.Add(EmojiEntry.ParseTagValue(value));
                }
                else
                {
                    doc.Metadata.Add(new MetadataTag(name, value));
                }
            }
        }
        TaggedTextCommon.ApplyRubyEntries(doc, rubyEntries);

        doc.RlfExtras = new RlfExtras
        {
            SaveMojiCode = vars.TryGetValue("SaveMojiCode", out var smc) && smc.IntValues is [var v1, ..] ? v1 : 0,
            YomiMojiCode = vars.TryGetValue("YomiMojiCode", out var ymc) && ymc.IntValues is [var v2, ..] ? v2 : 0,
            Surface00 = sakuraSurface is not null ? DecodeCell(sakuraSurface.StrElements![0]) : "",
        };

        return doc;
    }

    public static LyricsDocument ReadFile(string path) => Read(File.ReadAllBytes(path));

    // ---------------------------------------------------------------- Write

    public static byte[] Write(LyricsDocument doc)
    {
        int lines = Math.Max(1, doc.Lines.Count);
        var mojisu = new int[lines];
        for (int cn = 0; cn < lines; cn++)
        {
            mojisu[cn] = cn < doc.Lines.Count ? doc.Lines[cn].Chars.Count : 0;
        }

        int maxChars = mojisu.Length == 0 ? 0 : mojisu.Max();
        int kaz = maxChars + 3;      // kaz_retu（RhythmicaLyrics の kaz_hoz 相当。余裕を持たせる）
        int kx = kaz + 1;            // t_jikan 等の第1次元
        int ky = lines + 1;          // t_jikan 等の第2次元

        var mojitan = NewStrVar("mojitan", lines, kaz);
        var mojitanWidth = NewIntVar("mojitan_width", lines, kaz);
        var tJikan = NewIntVar("t_jikan", kx, ky);
        var tKazu = NewIntVar("t_kazu", kx, ky);
        var sakuraTimetag = NewStrVar("sakura_timetag", kx, ky);
        var sakuraYomi = NewStrVar("sakura_yomi", kx, ky);
        var sakuraSurface = NewStrVar("sakura_surface", kx, ky);
        var sakuraScript = NewStrVar("sakura_script", kx, ky);

        int rentag = 0;

        for (int cn = 0; cn < doc.Lines.Count; cn++)
        {
            var line = doc.Lines[cn];
            for (int st = 0; st < line.Chars.Count; st++)
            {
                var c = line.Chars[st];
                if (c.IsSpacer) rentag++;

                SetStr(mojitan, cn, st, EncodeCell(c.Text));
                mojitanWidth.IntValues![cn + st * lines] = c.WidthCache;
                tJikan.IntValues![st + cn * kx] = TimeToRaw(c.TimeCs);
                tKazu.IntValues![st + cn * kx] = c.CheckCount;

                if (c.Ruby is not null || c.RubyJoinsNext)
                {
                    string ruby = (c.Ruby ?? "") + (c.RubyJoinsNext ? "＋" : "");
                    SetStr(sakuraYomi, st, cn, EncodeCell(ruby));
                }
                if (c.AuxTimeTagsCs.Count > 0)
                {
                    var sb = new StringBuilder();
                    foreach (int t in c.AuxTimeTagsCs) sb.Append(TimeTag.Format(t));
                    SetStr(sakuraTimetag, st, cn, EncodeCell(sb.ToString()));
                }
                if (c.SakuraSurface is not null)
                {
                    SetStr(sakuraSurface, st, cn, EncodeCell(c.SakuraSurface));
                }
                if (c.SakuraScript is not null)
                {
                    SetStr(sakuraScript, st, cn, EncodeCell(c.SakuraScript));
                }
            }

            // 行終端セル "\n" と行末タイムタグ
            SetStr(mojitan, cn, line.Chars.Count, new byte[] { 0x0A });
            tJikan.IntValues![line.Chars.Count + cn * kx] = TimeToRaw(line.EndTimeCs);
        }

        var extras = doc.RlfExtras ?? new RlfExtras();
        SetStr(sakuraSurface, 0, 0, EncodeCell(extras.Surface00));

        // @タグメタデータ
        var flagValues = new int[KnownTagNames.Length];
        var inpValues = new string[KnownTagNames.Length];
        var sonotaLines = new List<string>();
        for (int i = 0; i < inpValues.Length; i++) inpValues[i] = "";

        foreach (var m in doc.Metadata)
        {
            int idx = Array.FindIndex(KnownTagNames, n => n.Equals(m.Name, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0 && flagValues[idx] == 0)
            {
                flagValues[idx] = 1;
                inpValues[idx] = m.Value;
            }
            else
            {
                sonotaLines.Add(m.ToLine());
            }
        }
        foreach (var e in doc.EmojiEntries)
        {
            sonotaLines.Add("@Emoji=" + e.ToTagValue());
        }
        for (int i = 0; i < doc.UnappliedRubyEntries.Count; i++)
        {
            // rlf ではルビは sakura_yomi に保持されるため、未適用エントリのみ持ち越す
            sonotaLines.Add($"@Ruby{i + 1}=" + doc.UnappliedRubyEntries[i].ToTagValue());
        }

        var vars = new List<HspVariable>
        {
            mojitan,
            NewIntVar1D("mojisu", mojisu),
            tJikan,
            tKazu,
            mojitanWidth,
            sakuraTimetag,
            sakuraYomi,
            sakuraSurface,
            sakuraScript,
            NewIntScalar("st_max", maxChars),
            NewIntScalar("rentag", rentag),
            NewIntScalar("kaz_retu", kaz),
            NewIntScalar("noin", lines - 1),
        };
        for (int i = 0; i < FlagVarNames.Length; i++)
        {
            vars.Add(NewIntScalar(FlagVarNames[i], flagValues[i]));
        }
        for (int i = 0; i < InpVarNames.Length; i++)
        {
            vars.Add(NewStrScalar(InpVarNames[i], inpValues[i]));
        }
        vars.Add(NewStrScalar("at_sonota_inp2", string.Join('\n', sonotaLines)));
        vars.Add(NewIntScalar("SaveMojiCode", extras.SaveMojiCode));
        vars.Add(NewIntScalar("YomiMojiCode", extras.YomiMojiCode));

        return HspVsaveFile.Write(vars);
    }

    public static void WriteFile(string path, LyricsDocument doc) => File.WriteAllBytes(path, Write(doc));

    // ------------------------------------------------------------ セル変換

    /// <summary>SJIS セルのバイト列（NUL 終端まで）を .NET 文字列へ変換する。</summary>
    internal static string DecodeCell(byte[] buf)
    {
        int end = Array.IndexOf(buf, (byte)0);
        if (end < 0) end = buf.Length;

        var sb = new StringBuilder();
        int pos = 0;
        while (pos < end)
        {
            byte b = buf[pos];
            if (b == UniMarker && pos + 5 < end && buf[pos + 1] == '$' &&
                TryHex4(buf, pos + 2, end, out int hi))
            {
                pos += 6;
                if (hi is >= 0xD800 and <= 0xDBFF && TryHex4(buf, pos, end, out int lo) &&
                    lo is >= 0xDC00 and <= 0xDFFF)
                {
                    sb.Append((char)hi).Append((char)lo);
                    pos += 4;
                }
                else
                {
                    sb.Append((char)hi);
                }
                continue;
            }
            if (b == SpacerByte)
            {
                sb.Append(CharUnit.Spacer);
                pos++;
                continue;
            }
            // Shift-JIS の 1 文字（1 or 2 バイト）
            int len = IsSjisLead(b) && pos + 1 < end ? 2 : 1;
            sb.Append(EncodingDetector.ShiftJis.GetString(buf, pos, len));
            pos += len;
        }
        return sb.ToString();
    }

    /// <summary>.NET 文字列を SJIS セルのバイト列（NUL 終端、最低 64B バッファ）へ変換する。</summary>
    internal static byte[] EncodeCell(string text)
    {
        var bytes = new List<byte>();
        int i = 0;
        while (i < text.Length)
        {
            char ch = text[i];
            if (ch == '\u001A')
            {
                bytes.Add(SpacerByte);
                i++;
                continue;
            }

            bool surrogatePair = char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]);
            string one = surrogatePair ? text.Substring(i, 2) : ch.ToString();

            byte[]? sjis = TryEncodeSjis(one);
            if (sjis is not null)
            {
                bytes.AddRange(sjis);
            }
            else
            {
                // 0x11 + "$XXXX"（サロゲートペアは "$XXXXYYYY" 形式）
                bytes.Add(UniMarker);
                bytes.Add((byte)'$');
                AppendHex4(bytes, one[0]);
                if (one.Length == 2) AppendHex4(bytes, one[1]);
            }
            i += one.Length;
        }
        bytes.Add(0);

        // RhythmicaLyrics の確保サイズに合わせて最低 64B
        if (bytes.Count < DefaultStrBuf)
        {
            bytes.AddRange(Enumerable.Repeat((byte)0, DefaultStrBuf - bytes.Count));
        }
        return bytes.ToArray();
    }

    private static byte[]? TryEncodeSjis(string s)
    {
        try
        {
            var enc = Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            byte[] b = enc.GetBytes(s);
            // ラウンドトリップ確認（SJIS に無い字が別字に化けるのを防ぐ）
            return enc.GetString(b) == s ? b : null;
        }
        catch (EncoderFallbackException)
        {
            return null;
        }
    }

    private static void AppendHex4(List<byte> bytes, char c)
    {
        string hex = ((int)c).ToString("X4");
        foreach (char h in hex) bytes.Add((byte)h);
    }

    private static bool TryHex4(byte[] buf, int pos, int end, out int value)
    {
        value = 0;
        if (pos + 4 > end) return false;
        for (int i = 0; i < 4; i++)
        {
            int d = buf[pos + i] switch
            {
                >= (byte)'0' and <= (byte)'9' => buf[pos + i] - '0',
                >= (byte)'A' and <= (byte)'F' => buf[pos + i] - 'A' + 10,
                _ => -1,
            };
            if (d < 0) return false;
            value = value * 16 + d;
        }
        return true;
    }

    private static bool IsSjisLead(byte b) => (b >= 0x81 && b <= 0x9F) || (b >= 0xE0 && b <= 0xFC);

    // ------------------------------------------------------------ 変数操作

    private static HspVariable Require(Dictionary<string, HspVariable> vars, string name) =>
        vars.TryGetValue(name, out var v) ? v : throw new InvalidDataException($"rlf に変数 {name} がありません");

    /// <summary>int 変数から (x, y) の値を取得（x が第1次元）。範囲外は 0。</summary>
    private static int GetInt(HspVariable v, int x, int y)
    {
        int d1 = v.Dims[0];
        int d2 = v.Dims.Length > 1 ? v.Dims[1] : 1;
        if (x < 0 || x >= d1 || y < 0 || y >= d2) return 0;
        return v.IntValues![x + y * d1];
    }

    /// <summary>mojitan_width 用: (行, 列) の値を取得（行が第1次元）。</summary>
    private static int GetIntByLineCol(HspVariable v, int line, int col) => GetInt(v, line, col);

    /// <summary>
    /// str 変数からセルを取得。
    /// transposed=false: mojitan 系 (行, 列) — 行が第1次元。
    /// transposed=true:  sakura 系 (列, 行) — 列が第1次元。
    /// </summary>
    private static byte[] GetStr(HspVariable v, int a, int b)
    {
        int d1 = v.Dims[0];
        int d2 = v.Dims.Length > 1 ? v.Dims[1] : 1;
        int x = a, y = b;
        if (x < 0 || x >= d1 || y < 0 || y >= d2) return [];
        return v.StrElements![x + y * d1];
    }

    private static void SetStr(HspVariable v, int x, int y, byte[] value)
    {
        int d1 = v.Dims[0];
        v.StrElements![x + y * d1] = value;
    }

    private static HspVariable NewStrVar(string name, int d1, int d2)
    {
        var elements = new byte[d1 * d2][];
        var empty = new byte[DefaultStrBuf];
        for (int i = 0; i < elements.Length; i++) elements[i] = empty;
        return new HspVariable
        {
            Name = name,
            Type = HspVarType.Str,
            Dims = [d1, d2],
            StrElements = elements,
        };
    }

    private static HspVariable NewIntVar(string name, int d1, int d2) => new()
    {
        Name = name,
        Type = HspVarType.Int,
        Dims = [d1, d2],
        IntValues = new int[d1 * d2],
    };

    private static HspVariable NewIntVar1D(string name, int[] values) => new()
    {
        Name = name,
        Type = HspVarType.Int,
        Dims = [values.Length],
        IntValues = values,
    };

    private static HspVariable NewIntScalar(string name, int value) => new()
    {
        Name = name,
        Type = HspVarType.Int,
        Dims = [1],
        IntValues = [value],
    };

    private static HspVariable NewStrScalar(string name, string value) => new()
    {
        Name = name,
        Type = HspVarType.Str,
        Dims = [1],
        StrElements = [EncodeCell(value)],
    };

    private static int? TimeFromRaw(int raw) => raw == 0 ? null : raw;

    private static int TimeToRaw(int? cs) => cs switch
    {
        null => 0,
        0 => 1,   // RhythmicaLyrics は時刻 0 を 1 に丸める（0 は「タグなし」の意味のため）
        int v => v,
    };
}
