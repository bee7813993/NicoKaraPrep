namespace NicoKaraPrep.Core.Model;

/// <summary>
/// ページごとの文字の大きさの増減（ニコカラメーカー3 には無い NicoKaraPrep の機能）。
/// ページの行の <see cref="LyricsLine.FontSizeDelta"/>（ページの中で最初に 0 以外を持つ行の値）で、ページの文字に当たるフォント設定を、
/// 文字の大きさだけを変えたもの（<see cref="Derive"/>）に置き換える。n3proj の書き出しでは、置き換えたフォント設定を
/// 「（麻衣）+4」のような名前（<see cref="DerivedName"/>）でフォント設定に足す。
/// 大きさの変え方（<see cref="OffsetFaceSizes"/>）: 歌詞（漢字・かな・英数）は増減の px をそのまま足す。ルビは歌詞／漢字と同じ割合で変えて
/// 整数に丸める。継承（0）の欄は継承のまま（歌詞／かな・英数は新しい歌詞／漢字を、ルビ／漢字の 0 は歌詞の半分を引き継ぐ）。縁の幅などは変えない。
/// </summary>
public static class N3PageFontSize
{
    /// <summary>増減の px の範囲（画面の高さ 1080 基準）。</summary>
    public const int MinDelta = -100;

    public const int MaxDelta = 200;

    /// <summary>変えたあとの文字の大きさの下限 px。</summary>
    public const double MinSizePx = 4;

    /// <summary>大きさを変えたフォント設定の名前（「（麻衣）+4」「標準-2」）。</summary>
    public static string DerivedName(string baseName, int delta) =>
        $"{baseName}{(delta > 0 ? "+" : "-")}{Math.Abs(delta)}";

    /// <summary>増減の表示（「+4」「-2」「0」）。</summary>
    public static string Signed(int delta) => delta > 0 ? $"+{delta}" : delta.ToString();

    /// <summary>
    /// 名前が、同じプロジェクトのほかのフォント設定（<paramref name="names"/>）の大きさを変えたもの（<see cref="DerivedName"/>。書き出しで作ったもの）か。
    /// n3proj の読み込みで、書き出しで作ったフォント設定を最初から選ばないために使う。
    /// </summary>
    public static bool IsDerivedName(string name, IReadOnlyCollection<string> names)
    {
        var m = System.Text.RegularExpressions.Regex.Match(name, @"^(.+)([+-])(\d{1,3})$");
        if (!m.Success || !int.TryParse(m.Groups[3].Value, out int amount) || amount == 0) return false;
        int delta = m.Groups[2].Value == "+" ? amount : -amount;
        return delta >= MinDelta && delta <= MaxDelta && names.Contains(m.Groups[1].Value);
    }

    /// <summary>
    /// 行ごとの、そのページの文字の大きさの増減 px（0 の行は入れない）。ページは書き出しと同じ分け方（<see cref="LyricsDocument.GetPages"/>）で、
    /// ページの中で最初に 0 以外を持つ行の値をページのすべての行に当てる。
    /// </summary>
    public static Dictionary<int, int> LineDeltas(LyricsDocument doc, PageSplitMode mode, int fixedLineCount)
    {
        var result = new Dictionary<int, int>();
        foreach (var page in doc.GetPages(mode, fixedLineCount))
        {
            int delta = 0;
            foreach (int i in page)
            {
                if (doc.Lines[i].FontSizeDelta != 0)
                {
                    delta = Math.Clamp(doc.Lines[i].FontSizeDelta, MinDelta, MaxDelta);
                    break;
                }
            }
            if (delta == 0) continue;
            foreach (int i in page) result[i] = delta;
        }
        return result;
    }

    /// <summary>
    /// 文字の大きさを <paramref name="delta"/> px 変えたときの、文字種別フォント 6 種の文字サイズ px（添字 = <see cref="N3FontDetail.Faces"/>。
    /// 0 は継承のまま）。歌詞／漢字は継承でも実効値に足して書く。
    /// </summary>
    public static double[] OffsetFaceSizes(N3FontSet font, int delta)
    {
        var faces = font.Detail.Faces;
        var sizes = new double[N3FontDetail.FaceCount];
        double main = N3FontLibrary.EffectiveFace(font, 0).SizePx;
        if (main <= 0)
        {
            return faces.Select(f => f.SizePx).ToArray();
        }
        double newMain = Math.Max(MinSizePx, main + delta);
        double ratio = newMain / main;
        for (int i = 0; i < N3FontDetail.FaceCount; i++)
        {
            double size = faces[i].SizePx;
            sizes[i] = i switch
            {
                0 => newMain,
                1 or 2 => size > 0 ? Math.Max(MinSizePx, size + delta) : 0,
                _ => size > 0 ? Math.Max(MinSizePx, Math.Round(size * ratio, MidpointRounding.AwayFromZero)) : 0,
            };
        }
        return sizes;
    }

    /// <summary>文字の大きさを <paramref name="delta"/> px 変えたフォント設定（複製。名前は <see cref="DerivedName"/>）。</summary>
    public static N3FontSet Derive(N3FontSet font, int delta)
    {
        var result = font.Clone();
        if (delta == 0) return result;
        result.Name = DerivedName(font.Name, delta);
        var sizes = OffsetFaceSizes(font, delta);
        for (int i = 0; i < N3FontDetail.FaceCount; i++) result.Detail.Faces[i].SizePx = sizes[i];
        return result;
    }
}
