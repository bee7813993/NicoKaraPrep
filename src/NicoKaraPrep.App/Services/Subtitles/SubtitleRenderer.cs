using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using NicoKaraPrep.Core.Model;
using Windows.Foundation;
using Windows.UI;

namespace NicoKaraPrep.App.Services.Subtitles;

/// <summary>
/// <see cref="SubtitleLineLayout"/> を描く（フォント設定ビューのプレビュー（FontPreviewControl）と同じ近似）。
/// 文字は「飾り → 縁 2 → 縁 → 本体」の順に行全体で重ね、ワイプの位置より左をワイプ後（Brushes[0..3]）、右をワイプ前（Brushes[4..7]）の配色で描く。
/// 縁は輪郭を幅 EdgePx の線で、縁 2 は幅 EdgePx + Edge2Px の線で描く。影は本体の形を DecorSizePx だけ右下へずらし、
/// ブラーは本体＋縁の形を標準偏差 DecorSizePx / 2 でぼかす（濃さ 0/1/2 = 不透明度 50/75/100%）。
/// グラデーション・ミルフィーユの位置 0〜1 は行の本文（ルビはルビ）の字面の上端〜下端に当て、画像は字面の左上から拡大率で敷き詰める。
/// 絵文字の画像は縁を付けずに描く（ワイプ後の画像があれば、ワイプの位置より左はその画像）。
/// </summary>
internal static class SubtitleRenderer
{
    /// <summary>ニコカラメーカー3 が新規に書き出すときの既定色（添字 = <see cref="N3FontDetail.Brushes"/>。Color が空の箇所に使う）。</summary>
    private static readonly string[] DefaultColors =
    {
        "FFFFFF", "000000", "FFFFFF", "000000",
        "4DA3FF", "FFFFFF", "000000", "000000",
    };

    private static readonly Color FallbackGray = Color.FromArgb(255, 0x80, 0x80, 0x80);
    private const float MilleFeuilleEdge = 0.0005f;
    private static readonly float[] BlurOpacities = { 0.5f, 0.75f, 1f };

    /// <summary>
    /// 行を描く。<paramref name="transform"/> は行の座標（基準 px）から画面の座標への変換、<paramref name="scale"/> はその倍率。
    /// <paramref name="wiped"/> はワイプ済みの横の範囲（行の座標。重なっていてよい）。null・空なら全部ワイプ前で描く。
    /// 行の左端から始まる範囲は左の縁・ルビのはみ出しまで、右端で終わる範囲は右のはみ出しまでワイプ済みにする。
    /// 画像を読み込み中なら true を返す（読み終わったら描き直す）。
    /// </summary>
    public static bool Draw(CanvasDrawingSession ds, SubtitleLineLayout layout, Matrix3x2 transform, float scale, IReadOnlyList<(float X0, float X1)>? wiped, float opacity = 1f)
    {
        if (layout.Tokens.Count == 0) return false;
        bool loading = false;
        var trash = new List<IDisposable>();
        var oldTransform = ds.Transform;
        try
        {
            using var style = new CanvasStrokeStyle
            {
                LineJoin = CanvasLineJoin.Round,
                StartCap = CanvasCapStyle.Round,
                EndCap = CanvasCapStyle.Round,
            };
            // ワイプ済みの範囲（重なりをまとめる）と、ワイプ前の範囲の切り抜き
            const float big = 100000;
            var spans = Merge(wiped, layout.Width, big);
            bool allBefore = spans.Count == 0;
            bool allAfter = spans.Count == 1 && spans[0].X0 <= -big / 2 && spans[0].X1 >= big / 2;
            CanvasGeometry? afterClip = null, beforeClip = null;
            if (!allBefore && !allAfter)
            {
                var rects = spans.Select(sp => CanvasGeometry.CreateRectangle(ds, sp.X0, -big, Math.Max(0, sp.X1 - sp.X0), big * 2)).ToArray();
                afterClip = CanvasGeometry.CreateGroup(ds, rects, CanvasFilledRegionDetermination.Winding);
                foreach (var r in rects) r.Dispose();
                using var everything = CanvasGeometry.CreateRectangle(ds, -big, -big, big * 2, big * 2);
                beforeClip = everything.CombineWith(afterClip, Matrix3x2.Identity, CanvasGeometryCombine.Exclude);
                trash.Add(afterClip);
                trash.Add(beforeClip);
            }

            for (int half = 0; half < 2; half++)
            {
                // half 0: ワイプ後、half 1: ワイプ前
                bool after = half == 0;
                if (after && allBefore) continue;
                if (!after && allAfter) continue;
                int offset = after ? 0 : N3FontDetail.BeforeOffset;

                ds.Transform = transform;
                ds.Antialiasing = CanvasAntialiasing.Aliased; // 境目は縁をぼかさずに切る（左右の半透明な画素が重なって背景の色の線が出ないように）
                var clip = after ? afterClip : beforeClip;
                using var layer = clip is null ? ds.CreateLayer(opacity) : ds.CreateLayer(opacity, clip);
                ds.Antialiasing = CanvasAntialiasing.Antialiased;

                DrawDecor(ds, layout, transform, scale, offset, style, trash);
                ds.Transform = transform;
                foreach (var g in layout.Groups)
                {
                    if (g.Edge2Outer > 0) ds.DrawGeometry(g.Geometry, Brush(ds, layout, g, offset + 2, trash, ref loading), g.Edge2Outer, style);
                }
                foreach (var g in layout.Groups)
                {
                    if (g.Edge > 0) ds.DrawGeometry(g.Geometry, Brush(ds, layout, g, offset + 1, trash, ref loading), g.Edge, style);
                }
                foreach (var g in layout.Groups)
                {
                    ds.FillGeometry(g.Geometry, Brush(ds, layout, g, offset, trash, ref loading));
                }
                foreach (var image in layout.Images)
                {
                    string path = after && image.After is { Length: > 0 } afterImage ? afterImage : image.Before;
                    var bitmap = SubtitleBitmapCache.Get(path, out bool l);
                    loading |= l;
                    if (bitmap is not null) ds.DrawImage(bitmap, image.Rect);
                }
            }
        }
        finally
        {
            ds.Transform = oldTransform;
            foreach (var d in trash) d.Dispose();
        }
        return loading;
    }

    /// <summary>
    /// ワイプ済みの範囲を、重なり・隣り合いをまとめて左から並べる。行の左端（0）から始まる範囲は左へ、
    /// 行の右端（<paramref name="width"/>）で終わる範囲は右へ、縁・ルビのはみ出しの分まで広げる（±<paramref name="big"/>）。
    /// </summary>
    private static List<(float X0, float X1)> Merge(IReadOnlyList<(float X0, float X1)>? spans, float width, float big)
    {
        var result = new List<(float X0, float X1)>();
        if (spans is null) return result;
        foreach (var sp in spans.Where(sp => sp.X1 > sp.X0).OrderBy(sp => sp.X0))
        {
            if (result.Count > 0 && sp.X0 <= result[^1].X1)
            {
                result[^1] = (result[^1].X0, Math.Max(result[^1].X1, sp.X1));
            }
            else
            {
                result.Add(sp);
            }
        }
        if (result.Count > 0)
        {
            if (result[0].X0 <= 0.5f) result[0] = (-big, result[0].X1);
            if (result[^1].X1 >= width - 0.5f) result[^1] = (result[^1].X0, big);
        }
        return result;
    }

    /// <summary>文字飾り。影は本体の形を右下へずらして塗り、ブラーは本体＋縁の形をぼかして下に敷く。</summary>
    private static void DrawDecor(
        CanvasDrawingSession ds, SubtitleLineLayout layout, Matrix3x2 transform, float scale, int offset, CanvasStrokeStyle style, List<IDisposable> trash)
    {
        bool dummy = false;
        // フォント設定ごとに飾りの種類・大きさが違うので、まとまりごとに描く（ブラーはフォント設定ごとに 1 つのぼかしにまとめる）
        var blurByFont = new Dictionary<int, CanvasCommandList>();
        foreach (var g in layout.Groups)
        {
            var detail = layout.Fonts[g.Font].Detail;
            double d = double.IsFinite(detail.DecorSizePx) ? Math.Clamp(detail.DecorSizePx, 0, 500) : 0;
            if (detail.DecorKind == 1 && d > 0)
            {
                ds.Transform = Matrix3x2.CreateTranslation((float)d, (float)d) * transform;
                ds.FillGeometry(g.Geometry, Brush(ds, layout, g, offset + 3, trash, ref dummy));
            }
            else if (detail.DecorKind == 2)
            {
                if (!blurByFont.TryGetValue(g.Font, out var list))
                {
                    list = new CanvasCommandList(ds);
                    trash.Add(list);
                    blurByFont[g.Font] = list;
                }
                using var cds = list.CreateDrawingSession();
                cds.Transform = transform;
                cds.FillGeometry(g.Silhouette(style), Brush(cds, layout, g, offset + 3, trash, ref dummy));
            }
        }
        foreach (var (font, list) in blurByFont)
        {
            var detail = layout.Fonts[font].Detail;
            double d = double.IsFinite(detail.DecorSizePx) ? Math.Clamp(detail.DecorSizePx, 0, 500) : 0;
            var blur = new GaussianBlurEffect
            {
                Source = list,
                BlurAmount = (float)Math.Clamp(d * scale / 2, 0, 250), // 標準偏差（画面の px。Direct2D の上限 250）
                BorderMode = EffectBorderMode.Soft,
            };
            trash.Add(blur);
            float opacity = BlurOpacities[Math.Clamp(detail.BlurLevel, 0, BlurOpacities.Length - 1)];
            ds.Transform = Matrix3x2.Identity;
            using var layer = ds.CreateLayer(opacity);
            ds.DrawImage(blur);
        }
        ds.Transform = transform;
    }

    /// <summary>配色 1 箇所のブラシ（行の座標）。グラデーション・ミルフィーユは字面の上端を 0、下端を 1 にする。</summary>
    private static ICanvasBrush Brush(ICanvasResourceCreator creator, SubtitleLineLayout layout, SubtitleLineLayout.Group group, int slot, List<IDisposable> trash, ref bool loading)
    {
        var detail = layout.Fonts[group.Font].Detail;
        var box = group.Ruby ? layout.RubyInk : layout.MainInk;
        var b = slot >= 0 && slot < detail.Brushes.Length ? detail.Brushes[slot] : null;
        ICanvasBrush brush;
        if (b is null)
        {
            brush = new CanvasSolidColorBrush(creator, ParseColor(DefaultColors[slot], 100));
        }
        else
        {
            switch (b.Type)
            {
                case N3Brush.TypeSolid:
                    string color = b.Color.Length == 0 ? DefaultColors[slot] : b.Color;
                    brush = new CanvasSolidColorBrush(creator, ParseColor(color, b.AlphaPercent));
                    break;
                case N3Brush.TypeGradient:
                case N3Brush.TypeMilleFeuille:
                    brush = new CanvasLinearGradientBrush(creator, GradientStops(b), CanvasEdgeBehavior.Clamp, CanvasAlphaMode.Premultiplied)
                    {
                        StartPoint = new Vector2((float)box.X, (float)box.Top),
                        EndPoint = new Vector2((float)box.X, (float)Math.Max(box.Bottom, box.Top + 0.001)),
                    };
                    break;
                case N3Brush.TypeBitmap:
                    CanvasBitmap? bitmap = null;
                    if (!string.IsNullOrWhiteSpace(b.BitmapPath))
                    {
                        bitmap = SubtitleBitmapCache.Get(b.BitmapPath, out bool l);
                        loading |= l;
                    }
                    if (bitmap is null)
                    {
                        brush = new CanvasImageBrush(creator, SubtitleBitmapCache.Hatch) { ExtendX = CanvasEdgeBehavior.Wrap, ExtendY = CanvasEdgeBehavior.Wrap };
                    }
                    else
                    {
                        int percent = b.BitmapScale == 0 ? 100 : Math.Clamp(b.BitmapScale, 1, 1000);
                        float k = percent / 100f;
                        brush = new CanvasImageBrush(creator, bitmap)
                        {
                            ExtendX = CanvasEdgeBehavior.Wrap,
                            ExtendY = CanvasEdgeBehavior.Wrap,
                            Transform = Matrix3x2.CreateScale(k) * Matrix3x2.CreateTranslation((float)box.X, (float)box.Top),
                        };
                    }
                    break;
                default:
                    brush = new CanvasSolidColorBrush(creator, FallbackGray);
                    break;
            }
        }
        trash.Add(brush);
        return brush;
    }

    /// <summary>
    /// グラデーション・ミルフィーユのマーカーを Win2D の stop にする。マーカーが無ければニコカラメーカーの既定 3 点。
    /// ミルフィーユは各マーカーの色を次のマーカーの位置まで塗る（最後のマーカーの色は使わない）。
    /// </summary>
    private static CanvasGradientStop[] GradientStops(N3Brush b)
    {
        var stops = b.Stops
            .Where(x => x is not null && double.IsFinite(x.Position))
            .OrderBy(x => x.Position)
            .Select(x => (Position: (float)Math.Clamp(x.Position, 0, 1), Color: ParseColor(x.Color, x.AlphaPercent)))
            .ToList();
        if (stops.Count == 0)
        {
            stops = N3Brush.DefaultStops()
                .Select(x => (Position: (float)x.Position, Color: ParseColor(x.Color, x.AlphaPercent)))
                .ToList();
        }
        if (b.Type != N3Brush.TypeMilleFeuille)
        {
            return stops.Select(x => new CanvasGradientStop { Position = x.Position, Color = x.Color }).ToArray();
        }
        if (stops.Count == 1)
        {
            return new[]
            {
                new CanvasGradientStop { Position = 0, Color = stops[0].Color },
                new CanvasGradientStop { Position = 1, Color = stops[0].Color },
            };
        }
        var result = new List<CanvasGradientStop>();
        for (int k = 0; k < stops.Count - 1; k++)
        {
            float start = stops[k].Position;
            float end = Math.Max(start, stops[k + 1].Position - MilleFeuilleEdge);
            result.Add(new CanvasGradientStop { Position = start, Color = stops[k].Color });
            result.Add(new CanvasGradientStop { Position = end, Color = stops[k].Color });
        }
        return result.ToArray();
    }

    private static Color ParseColor(string? web16, int alphaPercent)
    {
        byte a = (byte)Math.Round(Math.Clamp(alphaPercent, 0, 100) * 255 / 100.0);
        return N3FontSet.TryParseWeb16(web16, out byte r, out byte g, out byte b)
            ? Color.FromArgb(a, r, g, b)
            : Color.FromArgb(a, FallbackGray.R, FallbackGray.G, FallbackGray.B);
    }
}
