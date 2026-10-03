namespace NicoKaraPrep.Core.Formats;

/// <summary>
/// 前後のページの同じ段の行が、画面の同じ場所に出るか（NicoKaraPrep の機能）。
/// ニコカラメーカー3 は段（下から／上から何行目）だけで組にするので、レイアウトで位置が違う行
/// （上寄せの 5 行のページの次の、下寄せの 2 行のページなど）も重なるものとして扱う（ニコカラメーカー3 の自動の表示時刻とチェックにもある動き）。
/// 行の画面上の上下の範囲（字幕の画面の px）が分かっている組だけ、範囲が重ならなければ別の場所とみなす。
/// </summary>
public static class N3RowPlacement
{
    /// <summary>
    /// 2 つの行が同じ場所に出るか。どちらかの範囲が分からなければ true（段だけで決める）。
    /// 範囲は上端・下端（px、上端 &lt; 下端）で、上下に少しでも重なれば同じ場所。
    /// </summary>
    public static bool SamePlace(IReadOnlyDictionary<int, (int Top, int Bottom)>? spans, int a, int b)
    {
        if (spans is null || !spans.TryGetValue(a, out var x) || !spans.TryGetValue(b, out var y)) return true;
        return x.Top < y.Bottom && y.Top < x.Bottom;
    }
}
