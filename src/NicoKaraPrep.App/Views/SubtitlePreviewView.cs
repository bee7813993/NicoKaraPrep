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
            var widths = new Dictionary<(int, int), float>();
            foreach (var line in model.Lines)
            {
                if (t < line.BeginMs || t >= line.EndMs) continue;
                var layout = line.Source.GetLayout();
                if (layout.Tokens.Count == 0) continue;
                var (x, baseline) = Position(model, line, layout, widths, W, H);
                var transform = Matrix3x2.CreateTranslation(x, baseline) * Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation(ox, oy);
                SubtitleRenderer.Draw(ds, layout, transform, scale, Wiped(line, layout, t / 10));
            }
        }
        catch (Exception ex) when (!sender.Device.IsDeviceLost(ex.HResult))
        {
            // 描けない値があっても再生は止めない
        }
    }

    /// <summary>行の左端の x と本文のベースラインの y（字幕の画面の px）。</summary>
    private static (float X, float Baseline) Position(
        SubtitlePreviewModel model, PreviewLine line, SubtitleLineLayout layout, Dictionary<(int, int), float> widths, float W, float H)
    {
        var L = line.Layout;
        float textHeight = layout.TextBottom - layout.TextTop;
        float space = (float)L.LineSpacePx;
        float step = textHeight + space;
        float vm = (float)L.VerticalMarginPx;
        int rows = Math.Max(line.RowsInPage, line.Row);
        float baseline;
        switch (L.VerticalAlignment)
        {
            case 0: // 上寄せ: 上の行から
            {
                float top = vm + (rows - line.Row) * step;
                baseline = top - layout.TextTop;
                break;
            }
            case 1: // 中央: ページの行をまとめて上下の中央に
            {
                float total = rows * textHeight + (rows - 1) * space;
                float top = (H - total) / 2 + (rows - line.Row) * step;
                baseline = top - layout.TextTop;
                break;
            }
            default: // 下寄せ: 下の行から
            {
                float bottom = H - vm - (line.Row - 1) * step;
                baseline = bottom - layout.TextBottom;
                break;
            }
        }

        float w = layout.Width;
        float hm = (float)L.HorizontalMarginPx;
        int align = L.AlignmentForRow(line.Row, rows);
        float x = align switch
        {
            0 => hm,
            2 => W - hm - w,
            _ => (W - w) / 2,
        };
        if (L.SmartHorizon == 1)
        {
            // 中心位置揃え: 短い行は、左寄せなら中央で終わり、右寄せなら中央から始まる
            if (align == 0) x = Math.Max(hm, W / 2 - w);
            else if (align == 2) x = Math.Min(W - hm - w, W / 2);
        }
        else if (L.SmartHorizon == 2)
        {
            // 左右余白揃え: ページのいちばん長い行に合わせて、左右の余白を等しくしながら中央へ寄せる
            if (!widths.TryGetValue((line.Tab, line.Page), out float longest))
            {
                longest = model.Pages.TryGetValue((line.Tab, line.Page), out var page)
                    ? page.Max(p => p.Source.GetLayout().Width)
                    : w;
                widths[(line.Tab, line.Page)] = longest;
            }
            float m = Math.Max(hm, (W - longest) / 2);
            if (align == 0) x = m;
            else if (align == 2) x = W - m - w;
        }
        return (x, baseline);
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
