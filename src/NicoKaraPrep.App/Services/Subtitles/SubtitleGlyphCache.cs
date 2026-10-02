using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using NicoKaraPrep.Core.Model;
using Windows.Foundation;

namespace NicoKaraPrep.App.Services.Subtitles;

/// <summary>
/// 字幕の 1 文字の形。座標は基準の画面の高さの px で、送りの左端・ベースラインを原点にする（上が負）。
/// 横倍率（XScale）は輪郭と送り幅に掛けてある。
/// </summary>
internal sealed class SubtitleGlyph
{
    public SubtitleGlyph(CanvasGeometry? geometry, float advance, float ascent, float descent, Rect? ink, bool fontFound)
    {
        Geometry = geometry;
        Advance = advance;
        Ascent = ascent;
        Descent = descent;
        Ink = ink;
        FontFound = fontFound;
    }

    /// <summary>輪郭（重なりをまとめたもの）。字形の無い文字（空白）は null。</summary>
    public CanvasGeometry? Geometry { get; }

    /// <summary>送り幅。</summary>
    public float Advance { get; }

    /// <summary>ベースラインから文字の枠の上端まで（正の値）。</summary>
    public float Ascent { get; }

    /// <summary>ベースラインから文字の枠の下端まで（正の値）。</summary>
    public float Descent { get; }

    /// <summary>字面（輪郭の範囲）。字形が無ければ null。</summary>
    public Rect? Ink { get; }

    /// <summary>指定のフォントが見つかったか（見つからなければ代わりのフォントで作った形）。</summary>
    public bool FontFound { get; }
}

/// <summary>
/// 文字の形の使い回し（文字・フォント・サイズ・横倍率ごと）。輪郭を作る処理（CreateText と Outline）は重いため、
/// 行リストの行やプレビューの毎回の描画では作り直さない。共有のデバイスで作り、デバイスを失ったら捨てる。UI スレッドから使う。
/// </summary>
internal static class SubtitleGlyphCache
{
    private readonly record struct Key(string Text, string Family, ushort Weight, int Style, int Stretch, float Size, float XScale);

    private static readonly Dictionary<Key, SubtitleGlyph> Glyphs = new();
    private static CanvasDevice? _device;

    /// <summary>形を作るデバイス（共有のデバイス。失ったら作り直す）。</summary>
    public static CanvasDevice Device
    {
        get
        {
            if (_device is null)
            {
                _device = CanvasDevice.GetSharedDevice();
                _device.DeviceLost += OnDeviceLost;
            }
            return _device;
        }
    }

    /// <summary>デバイスを失ったときに、形・画像・行の配置の使い回しを捨てたことを知らせる。</summary>
    public static event EventHandler? Reset;

    private static void OnDeviceLost(CanvasDevice sender, object args)
    {
        sender.DeviceLost -= OnDeviceLost;
        _device = null;
        Clear();
        Reset?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>使い回している形をすべて捨てる。</summary>
    public static void Clear()
    {
        foreach (var g in Glyphs.Values) g.Geometry?.Dispose();
        Glyphs.Clear();
    }

    /// <summary>1 文字の形。</summary>
    public static SubtitleGlyph Get(string text, N3FontFace face)
    {
        var font = DirectWriteFontResolver.Resolve(face.FontName, face.FaceName);
        float size = (float)(double.IsFinite(face.SizePx) ? Math.Clamp(face.SizePx, 1, 2000) : 100);
        float xScale = (face.XScale > 0 ? Math.Clamp(face.XScale, 10, 1000) : 100) / 100f;
        var key = new Key(text, font.Family, font.Weight.Weight, (int)font.Style, (int)font.Stretch, size, xScale);
        if (Glyphs.TryGetValue(key, out var cached)) return cached;

        var device = Device;
        using var format = new CanvasTextFormat
        {
            FontFamily = font.Family,
            FontSize = size,
            FontWeight = font.Weight,
            FontStyle = font.Style,
            FontStretch = font.Stretch,
            WordWrapping = CanvasWordWrapping.NoWrap,
        };
        using var layout = new CanvasTextLayout(device, text, format, 100000f, 100000f);
        var metrics = layout.LineMetrics;
        float baseline = metrics.Length > 0 ? metrics[0].Baseline : size * 0.88f;
        // 空白だけの文字は LayoutBounds の幅が 0 になる（末尾の空白を含めない）ので、末尾の空白を含めた幅を送り幅にする
        var box = layout.LayoutBoundsIncludingTrailingWhitespace;
        float ascent = (float)(baseline - box.Top);
        float descent = (float)(box.Bottom - baseline);
        float advance = (float)box.Width * xScale;

        CanvasGeometry? geometry = null;
        Rect? ink = null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            using var raw = CanvasGeometry.CreateText(layout);
            using var moved = raw.Transform(Matrix3x2.CreateTranslation(0, -baseline) * Matrix3x2.CreateScale(xScale, 1f));
            // 重なった輪郭（太字のフォントに多い）をまとめてから縁を付ける（重なったまま線にすると縁に小さな穴が出ることがある）
            geometry = moved.Outline();
            try
            {
                var r = geometry.ComputeBounds();
                if (r.Width > 0 && r.Height > 0 && double.IsFinite(r.X) && double.IsFinite(r.Y)) ink = r;
            }
            catch (Exception)
            {
                ink = null; // 輪郭の無い文字
            }
            if (ink is null)
            {
                geometry.Dispose();
                geometry = null;
            }
        }

        var glyph = new SubtitleGlyph(geometry, advance, ascent, descent, ink, font.Found);
        Glyphs[key] = glyph;
        return glyph;
    }
}
