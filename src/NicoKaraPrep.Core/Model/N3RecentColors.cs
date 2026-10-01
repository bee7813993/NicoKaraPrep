namespace NicoKaraPrep.Core.Model;

/// <summary>
/// 最近使った色（フォント設定ビューで ColorPicker・16 進・不透明度で指定した色の履歴。新しい順）。
/// 設定には "RRGGBB"（不透明度 100%）か "RRGGBB@不透明度" の文字列で保存する。
/// </summary>
public static class N3RecentColors
{
    /// <summary>覚えておく数。</summary>
    public const int Max = 12;

    /// <summary>保存する文字列（"RRGGBB" か "RRGGBB@不透明度"）。</summary>
    public static string Format(string color, int alphaPercent)
    {
        string c = N3FontSet.NormalizeWeb16(color);
        return alphaPercent == 100 ? c : $"{c}@{alphaPercent}";
    }

    /// <summary>保存した文字列を読む（読めなければ false）。</summary>
    public static bool TryParse(string? text, out string color, out int alphaPercent)
    {
        color = "";
        alphaPercent = 100;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Trim().Split('@');
        if (parts.Length > 2 || !N3FontSet.IsValidWeb16(parts[0])) return false;
        if (parts.Length == 2 && !(int.TryParse(parts[1], out alphaPercent) && alphaPercent is >= 0 and <= 100))
        {
            alphaPercent = 100;
            return false;
        }
        color = N3FontSet.NormalizeWeb16(parts[0]);
        return true;
    }

    /// <summary>
    /// 色を先頭に入れる（同じ色（色と不透明度）があれば先頭へ動かす。読めない文字列は消す）。<paramref name="max"/> を超えたら古いものから消す。
    /// 一覧が変わったら true。
    /// </summary>
    public static bool Add(List<string> list, string color, int alphaPercent, int max = Max)
    {
        if (!N3FontSet.IsValidWeb16(color)) return false;
        string entry = Format(color, alphaPercent);
        if (list.Count > 0 && list[0] == entry) return false;
        list.RemoveAll(x => !TryParse(x, out var c, out int a) || Format(c, a) == entry);
        list.Insert(0, entry);
        if (list.Count > max) list.RemoveRange(max, list.Count - max);
        return true;
    }

    /// <summary>覚えている色（新しい順。読めないものは除く）。</summary>
    public static List<N3PaletteColor> Colors(IEnumerable<string>? list) =>
        (list ?? Enumerable.Empty<string>())
            .Select(x => TryParse(x, out var c, out int a) ? new N3PaletteColor(c, a, Array.Empty<string>()) : null)
            .OfType<N3PaletteColor>()
            .ToList();
}
