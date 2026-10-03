using System.Numerics;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NicoKaraPrep.App.Services.Subtitles;
using NicoKaraPrep.Core.Formats;
using Windows.Foundation;
using Windows.UI;

namespace NicoKaraPrep.App.Views;

/// <summary>
/// メディア再生パネルの動画の上に、字幕のプレビュー（ニコカラメーカー3 での見え方の近似）を重ねる。
/// 字幕の画面（書き出すプロジェクトの画面の大きさ）を、縦横比を保って表示の範囲いっぱいに当てはめる。
/// 表示時刻のあいだ、ページの下から何段目かとレイアウト設定（上下・左右の配置、余白、行間、スマート水平配置）で位置を決め、ワイプしながら描く。
/// フェードなどの字幕アクションは描かない。
/// </summary>
public sealed class SubtitlePreviewView : Grid
{
    /// <summary>動画が無い（音声だけ）ときの字幕の画面の背景。</summary>
    private static readonly Color NoVideoColor = Color.FromArgb(255, 0x18, 0x18, 0x24);

    private readonly CanvasControl _canvas;
    private SubtitlePreviewModel? _model;
    private double _timeMs = double.NegativeInfinity;
    private bool _hasVideo;
    private bool _showSubtitles = true;

    public SubtitlePreviewView()
    {
        IsHitTestVisible = false;
        _canvas = new CanvasControl { ClearColor = Microsoft.UI.Colors.Transparent };
        _canvas.Draw += OnDraw;
        Children.Add(_canvas);
        Loaded += (_, _) =>
        {
            SubtitleBitmapCache.Loaded += OnBitmapLoaded;
            _canvas.Invalidate();
        };
        Unloaded += (_, _) => SubtitleBitmapCache.Loaded -= OnBitmapLoaded;
    }

    /// <summary>描く材料（null なら何も描かない）。</summary>
    public SubtitlePreviewModel? Model
    {
        get => _model;
        set
        {
            if (ReferenceEquals(_model, value)) return;
            _model = value;
            _canvas.Invalidate();
        }
    }

    /// <summary>今の時刻（タグの時刻の基準の ms）。</summary>
    public double TimeMs
    {
        get => _timeMs;
        set
        {
            if (_timeMs == value) return;
            _timeMs = value;
            _canvas.Invalidate();
        }
    }

    /// <summary>動画があるか（無ければ字幕の画面を暗い色で塗る）。</summary>
    public bool HasVideo
    {
        get => _hasVideo;
        set
        {
            if (_hasVideo == value) return;
            _hasVideo = value;
            _canvas.Invalidate();
        }
    }

    /// <summary>字幕を描くか。</summary>
    public bool ShowSubtitles
    {
        get => _showSubtitles;
        set
        {
            if (_showSubtitles == value) return;
            _showSubtitles = value;
            _canvas.Invalidate();
        }
    }

    private void OnBitmapLoaded(object? sender, EventArgs e) => _canvas.Invalidate();

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        var model = _model;
        if (model is null) return;
        float aw = (float)sender.ActualWidth, ah = (float)sender.ActualHeight;
        if (aw < 2 || ah < 2) return;
        float W = model.ScreenWidth, H = model.ScreenHeight;
        float scale = Math.Min(aw / W, ah / H);
        float ox = (aw - W * scale) / 2, oy = (ah - H * scale) / 2;
        var screen = new Rect(ox, oy, W * scale, H * scale);
        if (!_hasVideo) ds.FillRectangle(screen, NoVideoColor);
        if (!_showSubtitles || double.IsNegativeInfinity(_timeMs)) return;

        try
        {
            using var clip = ds.CreateLayer(1f, screen);
            double t = _timeMs;
            var visible = new List<PreviewLine>();
            foreach (var line in model.Lines)
            {
                if (t >= line.BeginMs && t < line.EndMs) visible.Add(line);
            }
            foreach (var line in visible)
            {
                var layout = line.Source.GetLayout();
                if (layout.Tokens.Count == 0) continue;
                float alpha = CrossFadeAlpha(line, visible, t);
                if (alpha <= 0f) continue;
                var (x, baseline) = Position(line, layout, W, H, model.Pages.GetValueOrDefault((line.Tab, line.Page)));
                var transform = Matrix3x2.CreateTranslation(x, baseline) * Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation(ox, oy);
                if (alpha >= 1f)
                {
                    SubtitleRenderer.Draw(ds, layout, transform, scale, Wiped(line, layout, t / 10));
                }
                else
                {
                    using var fade = ds.CreateLayer(alpha);
                    SubtitleRenderer.Draw(ds, layout, transform, scale, Wiped(line, layout, t / 10));
                }
            }
        }
        catch (Exception ex) when (!sender.Device.IsDeviceLost(ex.HResult))
        {
            // 描けない値があっても再生は止めない
        }
    }

    /// <summary>
    /// 同じタブの同じ段で、前後のページの行が同時に出ている（表示時刻が重なっている）ときの濃さ（0〜1。重ならなければ 1）。
    /// 重なりのあいだ、前の行は消えるまでにだんだん薄く、次の行は前の行が消えるまでにだんだん濃くする
    /// （同じ段の行を重ねてよい設定や手動指定で重ねたとき、2 行が同じ位置に重なって読めなくならないように。
    /// 　ニコカラメーカー3 の字幕アクション「文字単位フェード」の見え方の近似で、文字ごとのフェードまでは再現しない）。
    /// </summary>
    internal static float CrossFadeAlpha(PreviewLine line, IReadOnlyList<PreviewLine> visible, double t)
    {
        float alpha = 1f;
        foreach (var other in visible)
        {
            if (ReferenceEquals(other, line) || other.Tab != line.Tab || other.Row != line.Row || other.Page == line.Page) continue;
            if (other.Page > line.Page)
            {
                // この行が前の行: 次の行が出てから、この行が消えるまでに薄くなる
                double span = line.EndMs - other.BeginMs;
                if (span > 0) alpha = Math.Min(alpha, (float)Math.Clamp((line.EndMs - t) / span, 0, 1));
            }
            else
            {
                // この行が次の行: この行が出てから、前の行が消えるまでに濃くなる
                double span = other.EndMs - line.BeginMs;
                if (span > 0) alpha = Math.Min(alpha, (float)Math.Clamp((t - line.BeginMs) / span, 0, 1));
            }
        }
        return alpha;
    }

    /// <summary>
    /// 行の左端の x と本文のベースラインの y（字幕の画面の px）。左右は行ごとの左右レイアウトの位置に置き、
    /// スマート水平配置は、ページの行を組にして寄せる（<see cref="SmartX"/>。<paramref name="page"/> はそのページの行）。
    /// </summary>
    internal static (float X, float Baseline) Position(PreviewLine line, SubtitleLineLayout layout, float W, float H, IReadOnlyList<PreviewLine>? page = null)
    {
        float baseline = Baseline(line, layout, H);
        return (SmartX(line, layout, W, page), baseline);
    }

    /// <summary>
    /// 行の枠の上端・下端（字幕の画面の px）。前後のページの同じ段の行が画面の同じ場所に出るか
    /// （表示時刻の計算・チェック。レイアウトで位置が違う行は組にしない）を決めるのに使う。
    /// </summary>
    internal static (float Top, float Bottom) VerticalSpan(PreviewLine line, SubtitleLineLayout layout, float H)
    {
        float baseline = Baseline(line, layout, H);
        return (baseline + layout.BoxTop, baseline + layout.BoxBottom);
    }

    /// <summary>本文のベースラインの y（字幕の画面の px）。</summary>
    private static float Baseline(PreviewLine line, SubtitleLineLayout layout, float H)
    {
        var L = line.Layout;
        // 上下は、文字の枠に縁の幅の半分ずつを足した枠で並べる（ニコカラメーカー3 の出力画像の実測: 行の間隔 = 枠の高さ + 行間、
        // 下寄せは枠の下端が下余白の位置）
        float boxHeight = layout.BoxBottom - layout.BoxTop;
        float space = (float)L.LineSpacePx;
        float step = boxHeight + space;
        float vm = (float)L.VerticalMarginPx;
        int rows = Math.Max(line.RowsInPage, line.Row);
        float baseline;
        switch (L.VerticalAlignment)
        {
            case 0: // 上寄せ: 上の行から
            {
                float top = vm + (rows - line.Row) * step;
                baseline = top - layout.BoxTop;
                break;
            }
            case 1: // 中央: ページの行をまとめて上下の中央に
            {
                float total = rows * boxHeight + (rows - 1) * space;
                float top = (H - total) / 2 + (rows - line.Row) * step;
                baseline = top - layout.BoxTop;
                break;
            }
            default: // 下寄せ: 下の行から
            {
                float bottom = H - vm - (line.Row - 1) * step;
                baseline = bottom - layout.BoxBottom;
                break;
            }
        }
        return baseline;
    }

    /// <summary>
    /// 行の左端の x（字幕の画面の px）。左右は行の送りの範囲（文字の左右のすき間を含む）で余白にそろえる（ニコカラメーカー3 の出力画像の実測）。
    /// スマート水平配置は、ページに左寄せの行と右寄せの行の両方がある（左右に分かれる）ときだけ短い行を中央へ寄せる
    /// （ニコカラメーカー3 の出力画像の実測。Dou-Da？ DOING！・ジェットスターター、左右余白 50 と 100 で確認。全部の段が左寄せのページは寄せない）:
    /// 左右余白揃えは、左寄せの行の左の余白と右寄せの行の右の余白を同じ幅にして、2 つの行が中央で本文の文字 1 つ分重なるところまで寄せる
    /// （余白 = max(左右余白, (画面の幅 + 文字の大きさ − 左寄せの行の幅 − 右寄せの行の幅) / 2)。長い行があれば左右余白のまま）。
    /// 中心位置揃えは、左寄せの短い行が中央より文字の半分右で終わり、右寄せの短い行が中央より文字の半分左から始まる（ヘルプの説明から。実測は無い）。
    /// 1 行だけのページは、左右余白揃えなら中央に置く（Darling Wanted の最後のページ・アイドゥーミー！・Dou-Da？ DOING！の 1 行のページで確認）。
    /// </summary>
    private static float SmartX(PreviewLine line, SubtitleLineLayout layout, float W, IReadOnlyList<PreviewLine>? page)
    {
        var L = line.Layout;
        float w = layout.Width;
        float hm = (float)L.HorizontalMarginPx;
        int align = L.AlignmentForRow(line.Row, Math.Max(line.RowsInPage, line.Row));
        float x = align switch
        {
            0 => hm,
            2 => W - hm - w,
            _ => (W - w) / 2,
        };
        if (align == 1 || L.SmartHorizon is not (1 or 2)) return x;
        if (line.LinesInPage == 1) return L.SmartHorizon == 2 ? (W - w) / 2 : x;
        if (page is null) return x;

        // ページの左寄せの行・右寄せの行のうち、いちばん長い行（幅と本文の文字の大きさ）
        (float Width, float Size)? left = null, right = null;
        foreach (var p in page)
        {
            var pl = p.Source.GetLayout();
            if (pl.Tokens.Count == 0) continue;
            int a = p.Layout.AlignmentForRow(p.Row, Math.Max(p.RowsInPage, p.Row));
            if (a == 0 && (left is null || pl.Width > left.Value.Width)) left = (pl.Width, pl.MainSize);
            if (a == 2 && (right is null || pl.Width > right.Value.Width)) right = (pl.Width, pl.MainSize);
        }
        if (left is not { } l || right is not { } r) return x; // 左右に分かれていなければ寄せない
        float overlap = (l.Size + r.Size) / 2;
        if (L.SmartHorizon == 2)
        {
            float m = Math.Max(hm, (W + overlap - l.Width - r.Width) / 2);
            return align == 0 ? m : W - m - w;
        }
        float half = overlap / 2;
        return align == 0 ? Math.Max(hm, W / 2 + half - w) : Math.Min(W - hm - w, W / 2 - half);
    }

    /// <summary>時刻 <paramref name="cs"/>（10ms 単位）にワイプ済みの横の範囲（行の座標）。</summary>
    private static List<(float X0, float X1)> Wiped(PreviewLine line, SubtitleLineLayout layout, double cs)
    {
        var result = new List<(float X0, float X1)>();
        foreach (var g in line.Wipe)
        {
            double p = N3WipeTimeline.Progress(g, cs);
            if (p <= 0) continue;
            float x0 = float.MaxValue, x1 = float.MinValue;
            foreach (var t in layout.Tokens)
            {
                if (t.UnitEnd < g.FirstUnit || t.UnitStart > g.LastUnit) continue;
                x0 = Math.Min(x0, t.X);
                x1 = Math.Max(x1, t.X + t.Width);
            }
            if (x1 <= x0) continue;
            result.Add((x0, x0 + (float)p * (x1 - x0)));
        }
        return result;
    }
}
