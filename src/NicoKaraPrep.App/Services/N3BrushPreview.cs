using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using NicoKaraPrep.Core.Model;
using Windows.Foundation;
using Windows.UI;

namespace NicoKaraPrep.App.Services;

/// <summary>
/// ニコカラメーカー3 の配色 1 箇所（<see cref="N3Brush"/>）を、一覧や配色ボタンの色見本として描くブラシにする。
/// グラデーション・ミルフィーユは縦方向（上端 = 位置 0）。ミルフィーユはマーカー k の色で次のマーカーまでを塗り、境目をくっきりさせる
/// （最下段のマーカーの色は使わない。ニコカラメーカー3 の実データからの推測）。
/// </summary>
public static class N3BrushPreview
{
    /// <summary>未指定の箇所・読めない画像の代わりに見せる色。</summary>
    public static readonly Color UnsetColor = Color.FromArgb(0, 0, 0, 0);

    /// <summary>画像が見つからないときの色（灰色）。</summary>
    public static readonly Color MissingImageColor = Color.FromArgb(255, 0x80, 0x80, 0x80);

    /// <summary>"RRGGBB" と不透明度 % から色を作る。色が読めなければ <paramref name="fallback"/>。</summary>
    public static Color ToColor(string? web16, int alphaPercent, Color fallback)
    {
        if (!N3FontSet.TryParseWeb16(web16, out byte r, out byte g, out byte b)) return fallback;
        return Color.FromArgb(AlphaByte(alphaPercent), r, g, b);
    }

    /// <summary>不透明度 %（0–100）を 0–255 にする。</summary>
    public static byte AlphaByte(int alphaPercent) => (byte)Math.Round(Math.Clamp(alphaPercent, 0, 100) * 255 / 100.0);

    /// <summary>0–255 の不透明度を %（0–100）にする。</summary>
    public static int AlphaPercent(byte alpha) => (int)Math.Round(alpha * 100 / 255.0);

    /// <summary>色を "RRGGBB" にする。</summary>
    public static string ToWeb16(Color c) => $"{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>配色 1 箇所の色見本のブラシ。</summary>
    public static Brush Create(N3Brush brush)
    {
        switch (brush.Type)
        {
            case N3Brush.TypeGradient:
                return CreateGradient(EffectiveStops(brush), sharp: false);
            case N3Brush.TypeMilleFeuille:
                return CreateGradient(EffectiveStops(brush), sharp: true);
            case N3Brush.TypeBitmap:
                return CreateImage(brush.BitmapPath);
            default:
                return new SolidColorBrush(ToColor(brush.Color, brush.AlphaPercent, UnsetColor));
        }
    }

    /// <summary>
    /// マーカーの一覧から縦方向のグラデーションを作る。sharp が true なら、マーカー k の色で次のマーカーの位置までを塗る
    /// （同じ位置に 2 つの stop を置いて境目をくっきりさせる）。
    /// </summary>
    public static LinearGradientBrush CreateGradient(IReadOnlyList<N3GradientStop> stops, bool sharp)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1),
        };
        var sorted = stops.OrderBy(s => s.Position).ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            var s = sorted[i];
            var color = ToColor(s.Color, s.AlphaPercent, UnsetColor);
            double pos = Math.Clamp(s.Position, 0, 1);
            if (!sharp)
            {
                brush.GradientStops.Add(new GradientStop { Color = color, Offset = pos });
                continue;
            }
            if (i == sorted.Count - 1)
            {
                // 最下段のマーカーは層の終わりを示すだけ（色は使わない）。マーカーが 1 つだけなら全体をその色にする
                if (sorted.Count == 1) brush.GradientStops.Add(new GradientStop { Color = color, Offset = 0 });
                break;
            }
            double next = Math.Clamp(sorted[i + 1].Position, 0, 1);
            brush.GradientStops.Add(new GradientStop { Color = color, Offset = pos });
            brush.GradientStops.Add(new GradientStop { Color = color, Offset = next });
        }
        return brush;
    }

    /// <summary>描くときに使うマーカー（未指定ならニコカラメーカー3 の既定 3 点）。</summary>
    public static IReadOnlyList<N3GradientStop> EffectiveStops(N3Brush brush) =>
        brush.Stops.Count > 0 ? brush.Stops : N3Brush.DefaultStops();

    private static Brush CreateImage(string path)
    {
        if (path.Length == 0 || !File.Exists(path)) return new SolidColorBrush(MissingImageColor);
        try
        {
            return new ImageBrush
            {
                ImageSource = new BitmapImage(new Uri(Path.GetFullPath(path))) { DecodePixelHeight = 64 },
                Stretch = Stretch.UniformToFill,
            };
        }
        catch (Exception)
        {
            return new SolidColorBrush(MissingImageColor);
        }
    }

    /// <summary>配色 1 箇所の説明（ツールチップ用）。</summary>
    public static string Describe(string label, N3Brush brush) => brush.Type switch
    {
        N3Brush.TypeGradient => $"{label}: グラデーション（マーカー {EffectiveStops(brush).Count} 個）",
        N3Brush.TypeMilleFeuille => $"{label}: ミルフィーユ（マーカー {EffectiveStops(brush).Count} 個）",
        N3Brush.TypeBitmap => brush.BitmapPath.Length > 0
            ? $"{label}: 画像 {Path.GetFileName(brush.BitmapPath)}{(File.Exists(brush.BitmapPath) ? "" : "（見つかりません）")}"
            : $"{label}: 画像（未指定）",
        _ => N3FontSet.IsValidWeb16(brush.Color)
            ? $"{label}: #{N3FontSet.NormalizeWeb16(brush.Color)}{(brush.AlphaPercent != 100 ? $"・不透明度 {brush.AlphaPercent}%" : "")}"
            : $"{label}: 未指定（書き出し時はベースの色、ベースが無ければ既定の色）",
    };

    /// <summary>フォントの書体の表示（一覧用）。</summary>
    public static string DescribeFace(N3FontSet f)
    {
        var face = N3FontLibrary.EffectiveFace(f, 0);
        string name = face.FontName.Length > 0 ? face.FontName : "（フォント指定なし）";
        string faceName = face.FaceName.Length > 0 ? $"（{face.FaceName}）" : "";
        return $"{name}{faceName} {face.SizePx:0.#}px";
    }

    /// <summary>未指定などで透明なブラシか（見本に「未指定」の印を出すため）。</summary>
    public static bool IsBlank(N3Brush brush) =>
        brush.Type == N3Brush.TypeSolid && !N3FontSet.IsValidWeb16(brush.Color);

    /// <summary>ブラシを使わない場所の透明色。</summary>
    public static SolidColorBrush Transparent() => new(Colors.Transparent);
}
