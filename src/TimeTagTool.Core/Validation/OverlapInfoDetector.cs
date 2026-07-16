using TimeTagTool.Core.Model;

namespace TimeTagTool.Core.Validation;

/// <summary>
/// パート分け同時歌唱などによるタイムタグの重なりを「情報」として検出する。
/// （正常なケースなのでエラー扱いにはしない）
/// </summary>
public static class OverlapInfoDetector
{
    /// <summary>重なり情報を持つ行インデックスの集合を返す。</summary>
    public static HashSet<int> Detect(LyricsDocument doc, Func<CharUnit, bool>? excludeChar = null)
    {
        var marked = new HashSet<int>();

        // 行内の時刻逆行
        for (int i = 0; i < doc.Lines.Count; i++)
        {
            int? last = null;
            foreach (var c in doc.Lines[i].Chars)
            {
                if (excludeChar is not null && excludeChar(c)) continue;
                if (c.TimeCs is not int t) continue;
                if (last is int prev && t < prev)
                {
                    marked.Add(i);
                    break;
                }
                last = t;
            }
        }

        // 前後の行との重なり（空行を挟まない連続行のみ）
        for (int i = 0; i + 1 < doc.Lines.Count; i++)
        {
            var a = doc.Lines[i];
            var b = doc.Lines[i + 1];
            if (a.IsEmpty || b.IsEmpty) continue;
            if (a.GetLastTimeCs(excludeChar) is int aEnd &&
                b.GetFirstTimeCs(excludeChar) is int bStart &&
                aEnd > bStart)
            {
                marked.Add(i);
                marked.Add(i + 1);
            }
        }

        return marked;
    }
}
