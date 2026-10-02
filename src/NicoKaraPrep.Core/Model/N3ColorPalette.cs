namespace NicoKaraPrep.Core.Model;

/// <summary>使っている色の 1 つ（同じ色は 1 つにまとめる）。</summary>
/// <param name="Color">色（16 進 6 桁の大文字）。</param>
/// <param name="AlphaPercent">不透明度 %。</param>
/// <param name="FontNames">その色を使っているフォント設定の名前（一覧の順）。</param>
public sealed record N3PaletteColor(string Color, int AlphaPercent, IReadOnlyList<string> FontNames);

/// <summary>役割（メイン色・ベース色など）ごとの使っている色。</summary>
public sealed record N3PaletteGroup(string RoleName, IReadOnlyList<N3PaletteColor> Colors);

/// <summary>使っている画像の 1 つ（同じパスは 1 つにまとめる）。</summary>
/// <param name="Path">画像ファイルのパス。</param>
/// <param name="Scale">拡大率 %（最初に見つけたフォント設定のもの）。</param>
/// <param name="FontNames">その画像を使っているフォント設定の名前（一覧の順）。</param>
public sealed record N3PaletteImage(string Path, int Scale, IReadOnlyList<string> FontNames);

/// <summary>
/// フォント設定で使っている色と画像の一覧（配色の編集欄の横に出して、押すとその色にする）。
/// 色は、配色パターンの役割（メイン色・ベース色など）ごとに、各フォント設定のその役割の色（役割の箇所でいちばん多く使われている色）を集める。
/// 単色だけ（ミルフィーユ・グラデーション・画像・未指定は除く）で、同じ色（色と不透明度が同じ）は 1 つにする。
/// パターンを当てはめていないフォント設定（個別）の色は入れない。画像は、どの箇所のものも集める。
/// </summary>
public static class N3ColorPalette
{
    /// <summary>
    /// 役割の名前ごとに使っている色を集める（<paramref name="roleNames"/> の順。同じ名前の役割なら、別のパターンの役割の色もまとめる）。
    /// 色の並びはフォント設定の一覧の順（最初に見つけた順）。
    /// </summary>
    public static List<N3PaletteGroup> Collect(IEnumerable<N3FontSet> fonts, IReadOnlyList<N3ColorPattern> patterns, IEnumerable<string> roleNames)
    {
        var names = roleNames.Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.Ordinal).ToList();
        var groups = names.ToDictionary(n => n, _ => new List<(string Color, int Alpha, List<string> Fonts)>(), StringComparer.Ordinal);
        foreach (var font in fonts)
        {
            if (N3ColorPatterns.Effective(font, patterns) is not { } match) continue;
            var pattern = match.Pattern;
            for (int role = 0; role < pattern.Roles.Count; role++)
            {
                if (!groups.TryGetValue(pattern.RoleName(role), out var colors)) continue;
                int slot = N3ColorPatterns.RepresentativeSlot(font.Detail, pattern, role);
                if (slot < 0) continue;
                var brush = font.Detail.Brushes[slot];
                if (brush.Type != N3Brush.TypeSolid || !N3FontSet.IsValidWeb16(brush.Color)) continue;
                string color = N3FontSet.NormalizeWeb16(brush.Color);
                int i = colors.FindIndex(c => c.Color == color && c.Alpha == brush.AlphaPercent);
                if (i < 0)
                {
                    colors.Add((color, brush.AlphaPercent, new List<string> { font.Name }));
                }
                else if (!colors[i].Fonts.Contains(font.Name))
                {
                    colors[i].Fonts.Add(font.Name);
                }
            }
        }
        return names
            .Select(n => new N3PaletteGroup(n, groups[n].Select(c => new N3PaletteColor(c.Color, c.Alpha, c.Fonts)).ToList()))
            .ToList();
    }

    /// <summary>使っている画像を集める（8 箇所のどこでも。パスが空のものは除く。同じパスは大文字小文字を区別せず 1 つにする）。</summary>
    public static List<N3PaletteImage> CollectImages(IEnumerable<N3FontSet> fonts)
    {
        var images = new List<(string Path, int Scale, List<string> Fonts)>();
        foreach (var font in fonts)
        {
            foreach (var brush in font.Detail.Brushes)
            {
                if (brush.Type != N3Brush.TypeBitmap || string.IsNullOrWhiteSpace(brush.BitmapPath)) continue;
                string path = brush.BitmapPath.Trim();
                int i = images.FindIndex(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase));
                if (i < 0)
                {
                    images.Add((path, brush.BitmapScale, new List<string> { font.Name }));
                }
                else if (!images[i].Fonts.Contains(font.Name))
                {
                    images[i].Fonts.Add(font.Name);
                }
            }
        }
        return images.Select(x => new N3PaletteImage(x.Path, x.Scale, x.Fonts)).ToList();
    }
}
