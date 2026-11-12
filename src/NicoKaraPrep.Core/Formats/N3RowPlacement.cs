namespace NicoKaraPrep.Core.Formats;

/// <summary>行の画面上の四角（字幕の画面の px。左端・上端・右端・下端。左 &lt; 右、上 &lt; 下）。</summary>
public readonly record struct N3LineBounds(int Left, int Top, int Right, int Bottom)
{
    /// <summary>ほかの行の四角と、左右にも上下にも少しでも重なるか（接するだけなら重ならない）。</summary>
    public bool Overlaps(N3LineBounds other) =>
        Left < other.Right && other.Left < Right && Top < other.Bottom && other.Top < Bottom;
}

/// <summary>
/// 行の画面上の場所（字幕のプレビューと同じ並べ方で求めたもの）。
/// </summary>
/// <param name="Ink">描画範囲（ルビ・縁・飾り・アイコンを含む、描く範囲）。</param>
/// <param name="Box">文字の枠（ルビ・縁の外側を除いた、行を並べる枠。ページの中の上下の行は行間の分だけ離れる）。</param>
/// <param name="Layout">ページのレイアウト（同じ値なら同じレイアウト）。</param>
public readonly record struct N3LineArea(N3LineBounds Ink, N3LineBounds Box, int Layout);

/// <summary>
/// 1 行だけのページの行の場所（下の段に出るとき・2 行分の上の段に上がるとき）。
/// 1 行だけのページは、前のページの下の段の行がまだ出ているあいだに出るときだけ上の段に上がるので（ニコカラメーカー3 の出力の実測）、
/// 段は表示時刻の計算で決める（<see cref="N3ShowTimePlanner"/>）。
/// </summary>
public readonly record struct N3SingleLineAreas(N3LineArea Lower, N3LineArea Raised);

/// <summary>
/// 前後のページの行の組（NicoKaraPrep の機能）。
/// ニコカラメーカー3 は段（下から／上から何行目）だけで組にするので、レイアウトで位置が違う行
/// （上寄せの 5 行のページの次の、下寄せの 2 行のページ・左寄せの 5 行と右寄せの 5 行のページなど）も重なるものとして扱い、
/// 段の違う行は、画面で重なっていても組にしない（ニコカラメーカー3 の自動の表示時刻とチェックにもある動き）。
/// 行の場所（<see cref="N3LineArea"/>）が分かっている行どうしは、場所で組にする（<see cref="Paired"/>）。
/// </summary>
public static class N3RowPlacement
{
    /// <summary>
    /// 場所の分かっている前の行（段 <paramref name="prevRow"/>）と次の行（段 <paramref name="nextRow"/>）が組か。
    /// 同じ段なら、描画範囲（ルビ・縁込み）が少しでも重なれば組。
    /// 段が違えば、前後のページのレイアウトが同じなら組にしない（上下に隣り合う段のルビ・縁が触れるのはレイアウトどおり）。
    /// レイアウトが違えば、文字の枠が重なるときだけ組（ほかの段の行が同じ場所に来るとき）。
    /// </summary>
    public static bool Paired(N3LineArea prev, int prevRow, N3LineArea next, int nextRow) =>
        prevRow == nextRow
            ? prev.Ink.Overlaps(next.Ink)
            : prev.Layout != next.Layout && prev.Box.Overlaps(next.Box);

    /// <summary>
    /// 前のページの行と次のページの行の組（前の行, 次の行）。
    /// 場所が分かる行どうしは <see cref="Paired"/> で決め、どちらかの場所が分からなければ同じ段どうし。
    /// <paramref name="areaAt"/> が null なら（場所を使わない）、ニコカラメーカー3 と同じく同じ段どうし。
    /// 並びは次の行の段の順、同じ次の行の中は前の行の段の順。
    /// </summary>
    /// <param name="prevPage">前のページの行と段。</param>
    /// <param name="nextPage">次のページの行と段。</param>
    /// <param name="areaAt">行とその段から場所を返す（分からなければ null）。</param>
    public static List<(int Prev, int Next)> Pairs(
        IEnumerable<(int Line, int Row)> prevPage,
        IEnumerable<(int Line, int Row)> nextPage,
        Func<int, int, N3LineArea?>? areaAt)
    {
        var prevs = prevPage.OrderBy(x => x.Row).ToList();
        var pairs = new List<(int, int)>();
        foreach (var (next, nextRow) in nextPage.OrderBy(x => x.Row))
        {
            var nb = areaAt?.Invoke(next, nextRow);
            foreach (var (prev, prevRow) in prevs)
            {
                bool paired = areaAt is not null && nb is { } b && areaAt(prev, prevRow) is { } a
                    ? Paired(a, prevRow, b, nextRow)
                    : prevRow == nextRow;
                if (paired) pairs.Add((prev, next));
            }
        }
        return pairs;
    }

    /// <summary>行の段の説明（例: 「下から2行目」。前の行と次の行の段が違えば「下から2行目→1行目」）。</summary>
    public static string RowLabel(bool alignFromTop, int prevRow, int nextRow)
    {
        string from = alignFromTop ? "上" : "下";
        return prevRow == nextRow ? $"{from}から{nextRow}行目" : $"{from}から{prevRow}行目→{nextRow}行目";
    }
}
