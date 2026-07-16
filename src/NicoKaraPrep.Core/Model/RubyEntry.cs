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
}
