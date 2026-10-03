namespace NicoKaraPrep.Core.Formats;

/// <summary>行の画面上の四角（字幕の画面の px。左端・上端・右端・下端。左 &lt; 右、上 &lt; 下）。</summary>
public readonly record struct N3LineBounds(int Left, int Top, int Right, int Bottom)
{
    /// <summary>ほかの行の四角と、左右にも上下にも少しでも重なるか（接するだけなら重ならない）。</summary>
    public bool Overlaps(N3LineBounds other) =>
        Left < other.Right && other.Left < Right && Top < other.Bottom && other.Top < Bottom;
}

/// <summary>
/// 前後のページの同じ段の行が、画面の同じ場所に出るか（NicoKaraPrep の機能）。
/// ニコカラメーカー3 は段（下から／上から何行目）だけで組にするので、レイアウトで位置が違う行
/// （上寄せの 5 行のページの次の、下寄せの 2 行のページ・左寄せの 5 行と右寄せの 5 行のページなど）も重なるものとして扱う
/// （ニコカラメーカー3 の自動の表示時刻とチェックにもある動き）。
/// 行の画面上の四角（字幕の画面の px）が分かっている組だけ、四角が重ならなければ別の場所とみなす。
/// </summary>
public static class N3RowPlacement
{
    /// <summary>2 つの行が同じ場所に出るか。どちらかの四角が分からなければ true（段だけで決める）。</summary>
    public static bool SamePlace(IReadOnlyDictionary<int, N3LineBounds>? bounds, int a, int b)
    {
        if (bounds is null || !bounds.TryGetValue(a, out var x) || !bounds.TryGetValue(b, out var y)) return true;
        return x.Overlaps(y);
    }
}
