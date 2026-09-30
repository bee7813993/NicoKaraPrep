namespace NicoKaraPrep.Core.Model;

/// <summary>見づらい配色の組（文字と縁の明るさが近い）。</summary>
/// <param name="TextSlot">文字の箇所（<see cref="N3FontDetail.Brushes"/> の添字）。</param>
/// <param name="EdgeSlot">縁の箇所。</param>
/// <param name="TextColor">文字の色（多色なら見づらいマーカーの色）。</param>
/// <param name="EdgeColor">縁の色（多色なら見づらいマーカーの色）。</param>
/// <param name="Ratio">明るさの比（コントラスト比。1 = 同じ明るさ、21 = 黒と白）。</param>
/// <param name="BothSolid">文字と縁がどちらも単色か（色の候補は単色どうしのときだけ出す）。</param>
public sealed record N3ContrastIssue(int TextSlot, int EdgeSlot, string TextColor, string EdgeColor, double Ratio, bool BothSolid);

/// <summary>見づらい配色を直す色の候補の種類。</summary>
public enum N3ContrastSuggestionKind
{
    /// <summary>文字の色の明るさを変える（色合いはそのまま）。</summary>
    TextLightness,

    /// <summary>縁を文字の色合いの濃い色（または淡い色）にする。</summary>
    EdgeShade,

    /// <summary>縁を黒か白にする。</summary>
    EdgeBlackOrWhite,
}

/// <summary>見づらい配色を直す色の候補 1 つ。</summary>
/// <param name="Slot">色を変える箇所。</param>
/// <param name="Color">変えたあとの色（16 進 6 桁）。</param>
/// <param name="Ratio">変えたあとの明るさの比。</param>
/// <param name="Kind">候補の種類。</param>
public sealed record N3ContrastSuggestion(int Slot, string Color, double Ratio, N3ContrastSuggestionKind Kind);

/// <summary>
/// 見づらい配色の判定。文字と縁（ワイプ後・ワイプ前のそれぞれ）の明るさの比（ウェブの読みやすさの基準と同じ相対輝度による
/// コントラスト比）が小さい組を見つけ、明るさの比が十分になる色の候補を作る。黄色の文字に白の縁（比 1.1 ほど）などを見つけるため。
/// </summary>
public static class N3Contrast
{
    /// <summary>これより比が小さい組を見づらいとする（黄色と白は 1.1、水色 66C5EC と白は 1.9、紫 AE62FF と白は 3.5）。</summary>
    public const double WarnRatio = 1.5;

    /// <summary>色の候補が目指す比。</summary>
    public const double TargetRatio = 2.5;

    /// <summary>
    /// 調べる文字と縁の組（ワイプ前、ワイプ後の順。キャラ色の反転では、ワイプ前がキャラ色の文字と白の縁になり、
    /// その組の候補（キャラ色を直す）がいちばん分かりやすいので先にする）。
    /// </summary>
    public static IReadOnlyList<(int Text, int Edge)> Pairs { get; } = new[] { (4, 5), (0, 1) };

    // ------------------------------------------------------------ 明るさ

    /// <summary>相対輝度（0 = 黒、1 = 白）。色が読めなければ NaN。</summary>
    public static double Luminance(string? web16) =>
        N3FontSet.TryParseWeb16(web16, out byte r, out byte g, out byte b) ? Luminance(r, g, b) : double.NaN;

    public static double Luminance(byte r, byte g, byte b) =>
        0.2126 * Linear(r) + 0.7152 * Linear(g) + 0.0722 * Linear(b);

    private static double Linear(byte v)
    {
        double c = v / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    /// <summary>2 色の明るさの比（1〜21。どちらかが読めなければ NaN）。</summary>
    public static double Ratio(string? a, string? b)
    {
        double la = Luminance(a);
        double lb = Luminance(b);
        if (double.IsNaN(la) || double.IsNaN(lb)) return double.NaN;
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    // ------------------------------------------------------------ 判定

    /// <summary>
    /// 見づらい組を集める（ワイプ後・ワイプ前の文字と縁）。縁の幅が 0（縁なし）のフォント設定と、不透明度が 50% 未満の色・画像の箇所・
    /// 未指定の色は見ない。多色（グラデーション・ミルフィーユ）の箇所はマーカーの色ごとに比べ、いちばん比の小さい色で知らせる
    /// （ミルフィーユの最後のマーカーは塗りに使われないので除く）。
    /// </summary>
    public static List<N3ContrastIssue> Check(N3FontSet font, double warnRatio = WarnRatio)
    {
        var result = new List<N3ContrastIssue>();
        if (!(N3FontLibrary.EffectiveFace(font, 0).EdgePx > 0)) return result;
        var d = font.Detail;
        foreach (var (text, edge) in Pairs)
        {
            var textColors = Colors(d.Brushes[text]);
            var edgeColors = Colors(d.Brushes[edge]);
            N3ContrastIssue? worst = null;
            foreach (string t in textColors)
            {
                foreach (string e in edgeColors)
                {
                    double ratio = Ratio(t, e);
                    if (double.IsNaN(ratio) || ratio >= warnRatio) continue;
                    if (worst is null || ratio < worst.Ratio)
                    {
                        bool solid = d.Brushes[text].Type == N3Brush.TypeSolid && d.Brushes[edge].Type == N3Brush.TypeSolid;
                        worst = new N3ContrastIssue(text, edge, N3FontSet.NormalizeWeb16(t), N3FontSet.NormalizeWeb16(e), ratio, solid);
                    }
                }
            }
            if (worst is not null) result.Add(worst);
        }
        return result;
    }

    /// <summary>箇所の塗りで見える色（見ない箇所は空）。</summary>
    private static List<string> Colors(N3Brush b)
    {
        switch (b.Type)
        {
            case N3Brush.TypeSolid:
                return N3FontSet.IsValidWeb16(b.Color) && b.AlphaPercent >= 50 ? new List<string> { b.Color } : new List<string>();
            case N3Brush.TypeGradient:
            case N3Brush.TypeMilleFeuille:
                var stops = b.Stops.ToList();
                if (b.Type == N3Brush.TypeMilleFeuille && stops.Count > 1) stops.RemoveAt(stops.Count - 1);
                return stops.Where(s => N3FontSet.IsValidWeb16(s.Color) && s.AlphaPercent >= 50).Select(s => s.Color).ToList();
            default:
                return new List<string>();
        }
    }

    // ------------------------------------------------------------ 色の候補

    /// <summary>
    /// 見づらい組を直す色の候補（文字と縁がどちらも単色のときだけ。多色のときは空）。
    /// ① 文字の色合いのまま明るさを変える ② 縁を文字の色合いの濃い色（文字が暗ければ淡い色）にする ③ 縁を黒か白にする。
    /// どれも比が <paramref name="targetRatio"/> 以上になる色で、作れないものは出さない。同じ色の候補は 1 つにする。
    /// </summary>
    public static List<N3ContrastSuggestion> Suggest(N3ContrastIssue issue, double targetRatio = TargetRatio)
    {
        var result = new List<N3ContrastSuggestion>();
        if (!issue.BothSolid) return result;
        string text = issue.TextColor;
        string edge = issue.EdgeColor;

        void Add(int slot, string? color, N3ContrastSuggestionKind kind, string other)
        {
            if (color is null || result.Any(r => r.Slot == slot && r.Color == color)) return;
            result.Add(new N3ContrastSuggestion(slot, color, Ratio(color, other), kind));
        }

        // ① 文字の明るさを、縁と反対の向きへ
        bool edgeBrighter = Luminance(edge) >= Luminance(text);
        Add(issue.TextSlot, AdjustLightness(text, edge, darker: edgeBrighter, targetRatio), N3ContrastSuggestionKind.TextLightness, edge);

        // ② 縁を文字の色合いの濃い色（文字が暗ければ淡い色）に
        bool textBright = Luminance(text) >= 0.2;
        var (h, s, _) = ToHsl(text);
        double seedS = s < 0.1 ? 0 : Math.Max(s, 0.6); // 灰色の文字には灰色の縁（色合いの無い色に色味を付けない）
        string seed = FromHsl(h, seedS, textBright ? 0.3 : 0.8);
        Add(issue.EdgeSlot, AdjustLightness(seed, text, darker: textBright, targetRatio), N3ContrastSuggestionKind.EdgeShade, text);

        // ③ 縁を黒か白に（比の大きいほう）
        string bw = Ratio("000000", text) >= Ratio("FFFFFF", text) ? "000000" : "FFFFFF";
        if (Ratio(bw, text) >= targetRatio) Add(issue.EdgeSlot, bw, N3ContrastSuggestionKind.EdgeBlackOrWhite, text);
        return result;
    }

    /// <summary>
    /// 色合いと鮮やかさはそのままで明るさだけを変え、<paramref name="other"/> との比が目標以上になる色のうち、元の色にいちばん近いものを返す。
    /// darker なら暗くする向き、そうでなければ明るくする向き。目標に届かなければ null。
    /// </summary>
    public static string? AdjustLightness(string color, string other, bool darker, double targetRatio = TargetRatio)
    {
        var (h, s, l) = ToHsl(color);
        if (Ratio(color, other) >= targetRatio) return N3FontSet.NormalizeWeb16(color);
        double lo = darker ? 0 : l;
        double hi = darker ? l : 1;
        string end = FromHsl(h, s, darker ? 0 : 1);
        if (!(Ratio(end, other) >= targetRatio)) return null;

        // 目標を満たす側の端から、元の明るさへ向かって二分探索する
        for (int i = 0; i < 30; i++)
        {
            double mid = (lo + hi) / 2;
            bool ok = Ratio(FromHsl(h, s, mid), other) >= targetRatio;
            if (darker)
            {
                if (ok) lo = mid;
                else hi = mid;
            }
            else
            {
                if (ok) hi = mid;
                else lo = mid;
            }
        }
        string result = FromHsl(h, s, darker ? lo : hi);
        return Ratio(result, other) >= targetRatio ? result : end;
    }

    // ------------------------------------------------------------ HSL

    private static (double H, double S, double L) ToHsl(string web16)
    {
        N3FontSet.TryParseWeb16(web16, out byte rb, out byte gb, out byte bb);
        double r = rb / 255.0, g = gb / 255.0, b = bb / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2;
        if (max - min < 1e-9) return (0, 0, l);
        double d = max - min;
        double s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h = max == r ? (g - b) / d + (g < b ? 6 : 0)
            : max == g ? (b - r) / d + 2
            : (r - g) / d + 4;
        return (h / 6, s, l);
    }

    private static string FromHsl(double h, double s, double l)
    {
        l = Math.Clamp(l, 0, 1);
        s = Math.Clamp(s, 0, 1);
        double r, g, b;
        if (s < 1e-9)
        {
            r = g = b = l;
        }
        else
        {
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            r = Hue(p, q, h + 1.0 / 3);
            g = Hue(p, q, h);
            b = Hue(p, q, h - 1.0 / 3);
        }
        return $"{ToByte(r):X2}{ToByte(g):X2}{ToByte(b):X2}";
    }

    private static double Hue(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 0.5) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }

    private static byte ToByte(double v) => (byte)Math.Clamp((int)Math.Round(v * 255), 0, 255);
}
