namespace NicoKaraPrep.Core.Model;

/// <summary>見づらい配色の組（文字と縁の明るさが近い）。</summary>
/// <param name="TextSlot">文字の箇所（<see cref="N3FontDetail.Brushes"/> の添字）。</param>
/// <param name="EdgeSlot">縁の箇所。</param>
/// <param name="TextColor">文字の色（多色なら見づらいマーカーの色）。</param>
/// <param name="EdgeColor">縁の色（多色なら見づらいマーカーの色）。</param>
/// <param name="Ratio">明るさの比（コントラスト比。1 = 同じ明るさ、21 = 黒と白）。</param>
/// <param name="BothSolid">文字と縁がどちらも単色か。</param>
public sealed record N3ContrastIssue(int TextSlot, int EdgeSlot, string TextColor, string EdgeColor, double Ratio, bool BothSolid)
{
    /// <summary>文字が多色のとき、見づらい色のマーカーの番号（<see cref="N3Brush.Stops"/> の添字。単色は -1）。</summary>
    public int TextStop { get; init; } = -1;

    /// <summary>縁が多色のとき、見づらい色のマーカーの番号（単色は -1）。</summary>
    public int EdgeStop { get; init; } = -1;
}

/// <summary>見づらい配色を直す色の候補の種類。</summary>
public enum N3ContrastSuggestionKind
{
    /// <summary>文字の色の明るさを変える（色合いはそのまま）。</summary>
    TextLightness,

    /// <summary>縁を文字の色合いの濃い色（または淡い色）にする。</summary>
    EdgeShade,

    /// <summary>縁を黒か白にする。</summary>
    EdgeBlackOrWhite,

    /// <summary>文字を黒か白にする（縁が多色のとき）。</summary>
    TextBlackOrWhite,

    /// <summary>多色の箇所の、見づらいマーカーの色の明るさを変える（色合いはそのまま）。</summary>
    StopLightness,
}

/// <summary>見づらい配色を直す色の候補 1 つ。</summary>
/// <param name="Slot">色を変える箇所。</param>
/// <param name="Color">変えたあとの色（16 進 6 桁）。</param>
/// <param name="Ratio">変えたあとの明るさの比。</param>
/// <param name="Kind">候補の種類。</param>
public sealed record N3ContrastSuggestion(int Slot, string Color, double Ratio, N3ContrastSuggestionKind Kind)
{
    /// <summary>多色の箇所のマーカーの色を変える候補のとき、そのマーカーの番号（<see cref="N3Brush.Stops"/> の添字。それ以外は -1）。</summary>
    public int Stop { get; init; } = -1;

    /// <summary>マーカーの色を変える候補のとき、変える前の色。</summary>
    public string OriginalColor { get; init; } = "";
}

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
    /// 調べる文字と縁の組（ワイプ前、ワイプ後の順。メイン色の反転では、ワイプ前がメイン色の文字と白の縁になり、
    /// その組の候補（メイン色を直す）がいちばん分かりやすいので先にする）。
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
            foreach (var (t, ts) in textColors)
            {
                foreach (var (e, es) in edgeColors)
                {
                    double ratio = Ratio(t, e);
                    if (double.IsNaN(ratio) || ratio >= warnRatio) continue;
                    if (worst is null || ratio < worst.Ratio)
                    {
                        bool solid = d.Brushes[text].Type == N3Brush.TypeSolid && d.Brushes[edge].Type == N3Brush.TypeSolid;
                        worst = new N3ContrastIssue(text, edge, t, e, ratio, solid) { TextStop = ts, EdgeStop = es };
                    }
                }
            }
            if (worst is not null) result.Add(worst);
        }
        return result;
    }

    /// <summary>
    /// 箇所の塗りで見える色と、そのマーカーの番号（単色は -1。見ない箇所は空）。不透明度が 50% 未満・未指定の色は除き、
    /// ミルフィーユの最後のマーカー（塗りに使われない）も除く。
    /// </summary>
    private static List<(string Color, int Stop)> Colors(N3Brush b)
    {
        var result = new List<(string Color, int Stop)>();
        switch (b.Type)
        {
            case N3Brush.TypeSolid:
                if (N3FontSet.IsValidWeb16(b.Color) && b.AlphaPercent >= 50) result.Add((N3FontSet.NormalizeWeb16(b.Color), -1));
                break;
            case N3Brush.TypeGradient:
            case N3Brush.TypeMilleFeuille:
                int count = b.Type == N3Brush.TypeMilleFeuille && b.Stops.Count > 1 ? b.Stops.Count - 1 : b.Stops.Count;
                for (int i = 0; i < count; i++)
                {
                    var stop = b.Stops[i];
                    if (N3FontSet.IsValidWeb16(stop.Color) && stop.AlphaPercent >= 50) result.Add((N3FontSet.NormalizeWeb16(stop.Color), i));
                }
                break;
        }
        return result;
    }

    // ------------------------------------------------------------ 色の候補

    /// <summary>
    /// 見づらい組を直す色の候補。文字と縁がどちらも単色なら
    /// ① 文字の色合いのまま明るさを変える ② 縁を文字の色合いの濃い色（文字が暗ければ淡い色）にする ③ 縁を黒か白にする。
    /// 多色の箇所があるとき（<paramref name="detail"/> が要る。無ければ空）は、多色の側は見づらいマーカーの色の明るさを変え、
    /// 単色の側は多色のどの色とも比が足りる色（色合いを保った明るさ・黒か白）にする。
    /// どれも比が <paramref name="targetRatio"/> 以上になる色で、作れないものは出さない。同じ色の候補は 1 つにする。
    /// </summary>
    public static List<N3ContrastSuggestion> Suggest(N3ContrastIssue issue, N3FontDetail? detail = null, double targetRatio = TargetRatio)
    {
        if (!issue.BothSolid) return detail is null ? new List<N3ContrastSuggestion>() : SuggestMulti(issue, detail, targetRatio);
        var result = new List<N3ContrastSuggestion>();
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

    /// <summary>多色の箇所がある組の候補（<see cref="Suggest"/>）。</summary>
    private static List<N3ContrastSuggestion> SuggestMulti(N3ContrastIssue issue, N3FontDetail detail, double targetRatio)
    {
        var result = new List<N3ContrastSuggestion>();
        var textBrush = detail.Brushes[issue.TextSlot];
        var edgeBrush = detail.Brushes[issue.EdgeSlot];
        var text = Colors(textBrush);
        var edge = Colors(edgeBrush);
        if (text.Count == 0 || edge.Count == 0) return result;
        var textColors = text.Select(t => t.Color).ToList();
        var edgeColors = edge.Select(e => e.Color).ToList();

        void Add(int slot, string? color, N3ContrastSuggestionKind kind, IReadOnlyList<string> others, int stop = -1, string original = "")
        {
            if (color is null) return;
            color = N3FontSet.NormalizeWeb16(color);
            if (result.Any(r => r.Slot == slot && r.Stop == stop && r.Color == color)) return;
            result.Add(new N3ContrastSuggestion(slot, color, MinRatio(color, others), kind) { Stop = stop, OriginalColor = original });
        }

        // 多色の側: 見づらいマーカーの色を、相手のどの色とも比が足りる明るさへ
        void FixStops(int slot, List<(string Color, int Stop)> colors, List<string> others)
        {
            foreach (var (c, stop) in colors.Where(x => MinRatio(x.Color, others) < WarnRatio))
            {
                string worst = others.MinBy(o => Ratio(c, o))!;
                bool darker = Luminance(worst) >= Luminance(c);
                Add(slot, AdjustLightness(c, others, darker, targetRatio), N3ContrastSuggestionKind.StopLightness, others, stop, c);
            }
        }

        // 文字の側
        if (textBrush.Type == N3Brush.TypeSolid)
        {
            string t = textColors[0];
            bool edgeBrighter = Luminance(issue.EdgeColor) >= Luminance(t);
            Add(issue.TextSlot, AdjustLightness(t, edgeColors, darker: edgeBrighter, targetRatio), N3ContrastSuggestionKind.TextLightness, edgeColors);
            Add(issue.TextSlot, BlackOrWhite(edgeColors, targetRatio), N3ContrastSuggestionKind.TextBlackOrWhite, edgeColors);
        }
        else
        {
            FixStops(issue.TextSlot, text, edgeColors);
        }

        // 縁の側
        if (edgeBrush.Type == N3Brush.TypeSolid)
        {
            bool textBright = Luminance(issue.TextColor) >= 0.2;
            var (h, s, _) = ToHsl(issue.TextColor);
            double seedS = s < 0.1 ? 0 : Math.Max(s, 0.6);
            string seed = FromHsl(h, seedS, textBright ? 0.3 : 0.8);
            Add(issue.EdgeSlot, AdjustLightness(seed, textColors, darker: textBright, targetRatio), N3ContrastSuggestionKind.EdgeShade, textColors);
            Add(issue.EdgeSlot, BlackOrWhite(textColors, targetRatio), N3ContrastSuggestionKind.EdgeBlackOrWhite, textColors);
        }
        else
        {
            FixStops(issue.EdgeSlot, edge, textColors);
        }
        return result;
    }

    /// <summary>いくつかの色とのいちばん小さい比。</summary>
    private static double MinRatio(string color, IEnumerable<string> others) => others.Select(o => Ratio(color, o)).DefaultIfEmpty(double.NaN).Min();

    /// <summary>いくつかの色のどれとも比が目標以上になる黒か白（いちばん小さい比が大きいほう。どちらも届かなければ null）。</summary>
    private static string? BlackOrWhite(IReadOnlyList<string> others, double targetRatio)
    {
        double black = MinRatio("000000", others);
        double white = MinRatio("FFFFFF", others);
        string best = black >= white ? "000000" : "FFFFFF";
        return Math.Max(black, white) >= targetRatio ? best : null;
    }

    /// <summary>
    /// 色合いと鮮やかさはそのままで明るさだけを変え、<paramref name="others"/> のどの色とも比が目標以上になる色のうち、元の色にいちばん近いものを返す
    /// （darker なら暗くする向き、そうでなければ明るくする向きへ少しずつ動かして探す。相手に明るい色と暗い色の両方があると、
    /// 答えが途中にしか無いため）。見つからなければ null。
    /// </summary>
    public static string? AdjustLightness(string color, IReadOnlyList<string> others, bool darker, double targetRatio = TargetRatio)
    {
        if (others.Count == 0) return null;
        if (MinRatio(color, others) >= targetRatio) return N3FontSet.NormalizeWeb16(color);
        var (h, s, l) = ToHsl(color);
        for (int i = 1; i <= 200; i++)
        {
            double next = darker ? l - i * 0.005 : l + i * 0.005;
            if (next < 0 || next > 1) break;
            string c = FromHsl(h, s, next);
            if (MinRatio(c, others) >= targetRatio) return c;
        }
        string end = FromHsl(h, s, darker ? 0 : 1);
        return MinRatio(end, others) >= targetRatio ? end : null;
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
