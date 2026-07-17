namespace NicoKaraPrep.Core.Model;

/// <summary>
/// ルビ拡張規格の @RubyX タグ 1 件。
/// 書式: @RubyX=親文字,ルビ[,適用開始時刻[,適用終了時刻]]
/// </summary>
/// <param name="Parent">親文字（ルビが振られる文字列）。</param>
/// <param name="Ruby">ルビ（読み）。</param>
/// <param name="StartCs">適用開始時刻（10ms 単位）。null で制限なし。</param>
/// <param name="EndCs">適用終了時刻（10ms 単位）。null で制限なし。</param>
public sealed record RubyEntry(string Parent, string Ruby, int? StartCs = null, int? EndCs = null)
{
    /// <summary>@RubyX= の右辺（値部分）を生成する。</summary>
    public string ToTagValue()
    {
        string v = $"{Parent},{Ruby}";
        if (StartCs is int s)
        {
            v += "," + TimeTag.Format(s);
            if (EndCs is int e) v += "," + TimeTag.Format(e);
        }
        else if (EndCs is int e2)
        {
            v += ",," + TimeTag.Format(e2);
        }
        return v;
    }

    /// <summary>@RubyX= の右辺（値部分）を解析する。</summary>
    public static RubyEntry ParseTagValue(string value)
    {
        var parts = value.Split(',');
        string parent = parts.Length > 0 ? parts[0] : "";
        string ruby = parts.Length > 1 ? parts[1] : "";
        int? start = null, end = null;
        if (parts.Length > 2 && TimeTag.TryParse(parts[2], out int s)) start = s;
        if (parts.Length > 3 && TimeTag.TryParse(parts[3], out int e)) end = e;
        return new RubyEntry(parent, ruby, start, end);
    }

    /// <summary>
    /// ルビ文字列に埋め込まれたワイプタグ（例: "カ[00:00:15]ル[00:00:35]テッ[00:02:12]ト"）を分解する。
    /// タグの時刻は親文字グループ先頭からの相対値（10ms 単位）。
    /// 戻り値はタグを除いた読みと、ワイプ単位ごとの (文字列, 相対時刻)。先頭単位の相対時刻は null。
    /// </summary>
    public static (string Plain, List<(string Text, int? RelCs)> Segments) ParseWipeSegments(string ruby)
    {
        var segments = new List<(string Text, int? RelCs)>();
        var plain = new System.Text.StringBuilder();
        var current = new System.Text.StringBuilder();
        int? currentRel = null;

        int pos = 0;
        while (pos < ruby.Length)
        {
            if (ruby[pos] == '[' && TimeTag.TryParseAt(ruby, pos, out int cs, out int len))
            {
                if (current.Length > 0 || segments.Count == 0)
                {
                    segments.Add((current.ToString(), currentRel));
                }
                current.Clear();
                currentRel = cs;
                pos += len;
                continue;
            }
            current.Append(ruby[pos]);
            plain.Append(ruby[pos]);
            pos++;
        }
        if (current.Length > 0 || segments.Count == 0)
        {
            segments.Add((current.ToString(), currentRel));
        }
        return (plain.ToString(), segments);
    }

    /// <summary>
    /// 読みを指定数のワイプ単位に分割する。拗音などの小書き文字・長音は前の文字にまとめ、
    /// 「っ」は独立した単位にする（RhythmicaLyrics の分割と同じ）。
    /// 単位数が合わない場合は末尾へまとめる／余った単位を捨てる。
    /// </summary>
    public static List<string> SplitIntoWipeUnits(string ruby, int count)
    {
        var morae = new List<string>();
        foreach (char ch in ruby)
        {
            bool mergeWithPrev = morae.Count > 0 &&
                ch is 'ゃ' or 'ゅ' or 'ょ' or 'ぁ' or 'ぃ' or 'ぅ' or 'ぇ' or 'ぉ'
                   or 'ャ' or 'ュ' or 'ョ' or 'ァ' or 'ィ' or 'ゥ' or 'ェ' or 'ォ' or 'ー';
            if (mergeWithPrev)
            {
                morae[^1] += ch;
            }
            else
            {
                morae.Add(ch.ToString());
            }
        }

        if (count <= 1 || morae.Count <= 1)
        {
            return morae.Count == 0 ? new List<string>() : new List<string> { ruby };
        }

        // 単位数が多すぎる場合はまず「っ」「ッ」を前の単位へ吸収し（例: カ|ル|テ|ッ|ト → カ|ル|テッ|ト）、
        // それでも多ければ末尾からまとめる
        while (morae.Count > count)
        {
            int sokuon = morae.FindIndex(1, m => m is "っ" or "ッ");
            if (sokuon > 0)
            {
                morae[sokuon - 1] += morae[sokuon];
                morae.RemoveAt(sokuon);
            }
            else
            {
                morae[^2] += morae[^1];
                morae.RemoveAt(morae.Count - 1);
            }
        }
        return morae; // ちょうど、または単位数が少ない（余った境界は呼び出し側で捨てる）
    }
}
