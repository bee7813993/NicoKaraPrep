using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NicoKaraPrep.App.Services.Subtitles;
using NicoKaraPrep.Core.Formats;
using Windows.Foundation;
using Windows.System;
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
    internal static readonly Color NoVideoColor = Color.FromArgb(255, 0x18, 0x18, 0x24);

    private readonly CanvasControl _canvas;
    private SubtitlePreviewModel? _model;
    private double _timeMs = double.NegativeInfinity;
    private bool _hasVideo;
    private bool _showSubtitles = true;

    public SubtitlePreviewView()
    {
        IsHitTestVisible = false;
        _canvas = new CanvasControl { ClearColor = Microsoft.UI.Colors.Transparent, IsHitTestVisible = false };
        _canvas.Draw += OnDraw;
        Children.Add(_canvas);
        Loaded += (_, _) =>
        {
            SubtitleBitmapCache.Loaded += OnBitmapLoaded;
            _canvas.Invalidate();
        };
        Unloaded += (_, _) => SubtitleBitmapCache.Loaded -= OnBitmapLoaded;
        PointerPressed += OnDragPointerPressed;
        PointerMoved += OnDragPointerMoved;
        PointerReleased += OnDragPointerReleased;
        PointerCaptureLost += (_, _) => CancelLayoutDrag();
        PointerExited += (_, _) =>
        {
            if (_drag is not null) return;
            _hoverOnLine = false;
            ProtectedCursor = null;
        };
    }

    /// <summary>描く材料（null なら何も描かない）。</summary>
    public SubtitlePreviewModel? Model
    {
        get => _model;
        set
        {
            if (ReferenceEquals(_model, value)) return;
            _model = value;
            // ドラッグを離したあと、新しい値で作り直した材料が来るまでは、ドラッグ中の見た目のまま描く
            if (_drag is null) _dragModel = null;
            _canvas.Invalidate();
        }
    }

    // ------------------------------------------------------------ 行のドラッグでレイアウトを変える

    /// <summary>ドラッグ中の様子（始めたときの行・レイアウト設定・指の位置と、今の値）。</summary>
    private sealed class DragState
    {
        public required PreviewLine Line { get; init; }
        public required N3LayoutSettings Start { get; init; }
        public required Point StartPos { get; init; }
        public required float Scale { get; init; }
        public N3LayoutSettings Current { get; set; } = null!;
    }

    private DragState? _drag;

    /// <summary>ドラッグ中の見た目（ドラッグしている行のレイアウト設定を今の値に置き換えた材料。null ならふだんの材料）。</summary>
    private SubtitlePreviewModel? _dragModel;

    private bool _layoutDragEnabled;

    /// <summary>行の上にカーソルがあるか（カーソルの形を変えたか）。</summary>
    private bool _hoverOnLine;

    private InputSystemCursor? _moveCursor;

    private static readonly SolidColorBrush HitBackground = new(Microsoft.UI.Colors.Transparent);

    /// <summary>
    /// 字幕の行をマウスでドラッグして、その行のページのレイアウト設定（上下余白・左右余白・行間）を変えられるようにするか
    /// （レイアウト設定ビューの見本と、メディア再生の字幕のプレビューで使う。true のあいだは、字幕の行の無いところの操作も下へは通さない）。
    /// </summary>
    public bool LayoutDragEnabled
    {
        get => _layoutDragEnabled;
        set
        {
            if (_layoutDragEnabled == value) return;
            _layoutDragEnabled = value;
            IsHitTestVisible = value;
            Background = value ? HitBackground : null;
            if (!value)
            {
                CancelLayoutDrag();
                _hoverOnLine = false;
                ProtectedCursor = null;
            }
        }
    }

    /// <summary>行を押してドラッグを始めようとした（Cancel で止められる。止めたときの知らせは受け手が出す）。</summary>
    public event EventHandler<LayoutDragStartingEventArgs>? LayoutDragStarting;

    /// <summary>ドラッグを離して、レイアウト設定の値が変わった（受け手がレイアウト設定に書き込み、材料を作り直す）。</summary>
    public event EventHandler<LayoutDragCompletedEventArgs>? LayoutDragCompleted;

    /// <summary>今ドラッグ中か。</summary>
    public bool IsDraggingLayout => _drag is not null;

    /// <summary>字幕の画面を当てはめる倍率と左上（コントロールの座標）。</summary>
    private (float Scale, float Ox, float Oy) Fit(SubtitlePreviewModel model)
    {
        float aw = (float)ActualWidth, ah = (float)ActualHeight;
        float scale = Math.Min(aw / model.ScreenWidth, ah / model.ScreenHeight);
        return (scale, (aw - model.ScreenWidth * scale) / 2, (ah - model.ScreenHeight * scale) / 2);
    }

    /// <summary>コントロールの位置にある字幕の行（描いている範囲の少し外まで。重なっていれば小さい方。無ければ null）。</summary>
    private PreviewLine? LineAt(Point p)
    {
        var model = _model;
        if (model is null || !_showSubtitles || double.IsNegativeInfinity(_timeMs) || ActualWidth < 2 || ActualHeight < 2) return null;
        var (scale, ox, oy) = Fit(model);
        if (scale <= 0) return null;
        double sx = (p.X - ox) / scale, sy = (p.Y - oy) / scale;
        double pad = 6 / scale;
        PreviewLine? best = null;
        double bestArea = double.MaxValue;
        foreach (var line in VisibleLines(model, _timeMs))
        {
            var layout = line.Source.GetLayout();
            if (layout.Tokens.Count == 0) continue;
            var (ink, _) = Bounds(line, layout, model.ScreenWidth, model.ScreenHeight, model.Pages.GetValueOrDefault((line.Tab, line.Page)));
            if (sx < ink.Left - pad || sx > ink.Right + pad || sy < ink.Top - pad || sy > ink.Bottom + pad) continue;
            double area = (ink.Right - ink.Left) * (ink.Bottom - ink.Top);
            if (area < bestArea)
            {
                best = line;
                bestArea = area;
            }
        }
        return best;
    }

    private void OnDragPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_layoutDragEnabled || _model is not { } model) return;
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed) return;
        if (LineAt(point.Position) is not { } line) return;
        e.Handled = true;
        var starting = new LayoutDragStartingEventArgs(line);
        LayoutDragStarting?.Invoke(this, starting);
        if (starting.Cancel) return;
        _drag = new DragState
        {
            Line = line,
            Start = line.Layout,
            Current = line.Layout,
            StartPos = point.Position,
            Scale = Fit(model).Scale,
        };
        CapturePointer(e.Pointer);
        _canvas.Invalidate();
    }

    private void OnDragPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_layoutDragEnabled) return;
        var point = e.GetCurrentPoint(this);
        if (_drag is not { } drag)
        {
            // 行の上では、動かせることが分かるようにカーソルを変える
            bool onLine = LineAt(point.Position) is not null;
            if (onLine != _hoverOnLine)
            {
                _hoverOnLine = onLine;
                ProtectedCursor = onLine ? _moveCursor ??= InputSystemCursor.Create(InputSystemCursorShape.SizeAll) : null;
            }
            return;
        }
        e.Handled = true;
        double dx = (point.Position.X - drag.StartPos.X) / drag.Scale;
        double dy = (point.Position.Y - drag.StartPos.Y) / drag.Scale;
        if (e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift))
        {
            // Shift を押しながらなら、大きく動かした向きだけ
            if (Math.Abs(dx) >= Math.Abs(dy)) dy = 0;
            else dx = 0;
        }
        var next = N3LayoutDrag.Apply(drag.Start, drag.Line.Row, drag.Line.RowsInPage, dx, dy);
        if (next == drag.Current && _dragModel is not null) return;
        drag.Current = next;
        _dragModel = WithLayout(_model!, drag.Start.Name, next);
        _canvas.Invalidate();
    }

    private void OnDragPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_drag is not { } drag) return;
        e.Handled = true;
        _drag = null;
        ReleasePointerCapture(e.Pointer);
        if (drag.Current == drag.Start)
        {
            _dragModel = null;
        }
        else
        {
            LayoutDragCompleted?.Invoke(this, new LayoutDragCompletedEventArgs(drag.Line, drag.Start, drag.Current));
        }
        _canvas.Invalidate();
    }

    /// <summary>ドラッグをやめて（離したあとの、新しい材料を待つあいだの見た目も）、ふだんの見た目に戻す（値は変えない）。</summary>
    public void CancelLayoutDrag()
    {
        if (_drag is null && _dragModel is null) return;
        _drag = null;
        _dragModel = null;
        _canvas.Invalidate();
    }

    /// <summary>名前が <paramref name="name"/> のレイアウト設定で出す行を、<paramref name="layout"/> の上下余白・左右余白・行間に置き換えた材料。</summary>
    private static SubtitlePreviewModel WithLayout(SubtitlePreviewModel model, string name, N3LayoutSettings layout) =>
        new(model.ScreenWidth, model.ScreenHeight, model.Lines.Select(l => l.Layout.Name == name
            ? l with
            {
                Layout = l.Layout with
                {
                    VerticalMarginPx = layout.VerticalMarginPx,
                    HorizontalMarginPx = layout.HorizontalMarginPx,
                    LineSpacePx = layout.LineSpacePx,
                },
            }
            : l).ToList());

    /// <summary>ドラッグ中の目安（余白の位置の点線と、今の値の説明）を描く。</summary>
    private void DrawDragGuide(CanvasDrawingSession ds, SubtitlePreviewModel model, DragState drag)
    {
        var (scale, ox, oy) = Fit(model);
        float W = model.ScreenWidth * scale, H = model.ScreenHeight * scale;
        var c = drag.Current;
        var guide = Color.FromArgb(220, 0x33, 0x99, 0xFF);
        using var dash = new CanvasStrokeStyle { DashStyle = CanvasDashStyle.Dash };
        if (N3LayoutDrag.IsAnchorRow(c, drag.Line.Row, drag.Line.RowsInPage))
        {
            float y = c.VerticalAlignment == 0 ? oy + (float)c.VerticalMarginPx * scale : oy + H - (float)c.VerticalMarginPx * scale;
            ds.DrawLine(ox, y, ox + W, y, guide, 1.5f, dash);
        }
        if (c.AlignmentForRow(drag.Line.Row, Math.Max(drag.Line.RowsInPage, drag.Line.Row)) is 0 or 2)
        {
            float m = (float)c.HorizontalMarginPx * scale;
            ds.DrawLine(ox + m, oy, ox + m, oy + H, guide, 1.5f, dash);
            ds.DrawLine(ox + W - m, oy, ox + W - m, oy + H, guide, 1.5f, dash);
        }

        string targets = N3LayoutDrag.Targets(drag.Start, drag.Line.Row, drag.Line.RowsInPage);
        string text = $"{c.Name}: {N3LayoutDrag.Describe(c)}" +
            (targets.Length > 0 ? $"\nこの段のドラッグで変わるもの: {targets}（Shift で縦か横だけ）" : "\nこの段は動かせません（中央寄せの真ん中の段・中央の段）");
        using var format = new CanvasTextFormat { FontFamily = "Yu Gothic UI", FontSize = 12, WordWrapping = CanvasWordWrapping.Wrap };
        using var textLayout = new CanvasTextLayout(ds, text, format, Math.Max(80, W - 12), H);
        var box = textLayout.LayoutBounds;
        ds.FillRoundedRectangle(ox + 4, oy + 4, (float)box.Width + 12, (float)box.Height + 8, 4, 4, Color.FromArgb(190, 0, 0, 0));
        ds.DrawTextLayout(textLayout, ox + 10, oy + 8, Microsoft.UI.Colors.White);
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

    /// <summary>UI オートメーション・読み上げで見つけられるようにする（Grid には既定で無い。名前は AutomationProperties.Name）。</summary>
    protected override Microsoft.UI.Xaml.Automation.Peers.AutomationPeer OnCreateAutomationPeer() =>
        new Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer(this);

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var model = _dragModel ?? _model;
        if (model is null) return;
        float aw = (float)sender.ActualWidth, ah = (float)sender.ActualHeight;
        if (aw < 2 || ah < 2) return;
        try
        {
            DrawFrame(args.DrawingSession, model, _timeMs, aw, ah, _hasVideo, _showSubtitles);
            if (_drag is { } drag) DrawDragGuide(args.DrawingSession, model, drag);
        }
        catch (Exception ex) when (!sender.Device.IsDeviceLost(ex.HResult))
        {
            // 描けない値があっても再生は止めない
        }
    }

    /// <summary>
    /// 字幕の画面の 1 コマを描く（幅 <paramref name="aw"/> × 高さ <paramref name="ah"/> の範囲に、字幕の画面を縦横比を保って当てはめる）。
    /// 動画の上のプレビューと、画面に出さずに画像にするとき（MCP の render_preview。<see cref="SubtitleFrameRenderer"/>）で共通。
    /// 絵文字の画像などを読み込み中なら true を返す（読み終わったら描き直す）。
    /// </summary>
    internal static bool DrawFrame(CanvasDrawingSession ds, SubtitlePreviewModel model, double timeMs, float aw, float ah, bool hasVideo, bool showSubtitles)
    {
        float W = model.ScreenWidth, H = model.ScreenHeight;
        float scale = Math.Min(aw / W, ah / H);
        float ox = (aw - W * scale) / 2, oy = (ah - H * scale) / 2;
        var screen = new Rect(ox, oy, W * scale, H * scale);
        if (!hasVideo) ds.FillRectangle(screen, NoVideoColor);
        if (!showSubtitles || double.IsNegativeInfinity(timeMs)) return false;

        bool loading = false;
        using var clip = ds.CreateLayer(1f, screen);
        double t = timeMs;
        var visible = VisibleLines(model, t);
        // 行の画面上の場所（重なりの薄め方で、同じ場所に出る前後のページの行を見る。表示時刻の計算・チェックの組と同じ判定）
        var areas = new Dictionary<PreviewLine, N3LineArea>(ReferenceEqualityComparer.Instance);
        var layoutIds = new Dictionary<N3LayoutSettings, int>(ReferenceEqualityComparer.Instance);
        foreach (var line in visible)
        {
            if (!layoutIds.TryGetValue(line.Layout, out int id)) layoutIds[line.Layout] = id = layoutIds.Count;
            areas[line] = AreaOf(line, line.Source.GetLayout(), W, H, model.Pages.GetValueOrDefault((line.Tab, line.Page)), id);
        }
        bool SamePlace(PreviewLine a, PreviewLine b) =>
            areas.TryGetValue(a, out var x) && areas.TryGetValue(b, out var y) &&
            (a.Page < b.Page ? N3RowPlacement.Paired(x, a.Row, y, b.Row) : N3RowPlacement.Paired(y, b.Row, x, a.Row));
        foreach (var line in visible)
        {
            var layout = line.Source.GetLayout();
            if (layout.Tokens.Count == 0) continue;
            float alpha = CrossFadeAlpha(line, visible, t, SamePlace);
            if (alpha <= 0f) continue;
            var (x, baseline) = Position(line, layout, W, H, model.Pages.GetValueOrDefault((line.Tab, line.Page)));
            var transform = Matrix3x2.CreateTranslation(x, baseline) * Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation(ox, oy);
            if (alpha >= 1f)
            {
                loading |= SubtitleRenderer.Draw(ds, layout, transform, scale, Wiped(line, layout, t / 10));
            }
            else
            {
                using var fade = ds.CreateLayer(alpha);
                loading |= SubtitleRenderer.Draw(ds, layout, transform, scale, Wiped(line, layout, t / 10));
            }
        }
        return loading;
    }

    /// <summary>時刻 <paramref name="timeMs"/>（タグの時刻の基準の ms）に出ている行（表示開始 ≦ 時刻 &lt; 表示終了）。</summary>
    internal static List<PreviewLine> VisibleLines(SubtitlePreviewModel model, double timeMs)
    {
        var visible = new List<PreviewLine>();
        foreach (var line in model.Lines)
        {
            if (timeMs >= line.BeginMs && timeMs < line.EndMs) visible.Add(line);
        }
        return visible;
    }

    /// <summary>
    /// 同じタブの同じ場所（<paramref name="samePlace"/>。無ければ同じ段）で、前後のページの行が同時に出ている（表示時刻が重なっている）ときの濃さ（0〜1。重ならなければ 1）。
    /// 重なりのあいだ、前の行は消えるまでにだんだん薄く、次の行は前の行が消えるまでにだんだん濃くする
    /// （同じ段の行を重ねてよい設定や手動指定で重ねたとき、2 行が同じ位置に重なって読めなくならないように。
    /// 　ニコカラメーカー3 の字幕アクション「文字単位フェード」の見え方の近似で、文字ごとのフェードまでは再現しない）。
    /// </summary>
    /// <param name="samePlace">2 つの行が画面の同じ場所に出るか（表示時刻の計算・チェックと同じ組の判定。<see cref="N3RowPlacement.Paired"/>）。null なら同じ段か。</param>
    internal static float CrossFadeAlpha(PreviewLine line, IReadOnlyList<PreviewLine> visible, double t, Func<PreviewLine, PreviewLine, bool>? samePlace = null)
    {
        float alpha = 1f;
        foreach (var other in visible)
        {
            if (ReferenceEquals(other, line) || other.Tab != line.Tab || other.Page == line.Page) continue;
            if (samePlace is not null ? !samePlace(line, other) : other.Row != line.Row) continue;
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
    /// 行の画面上の描画範囲（字幕の画面の px。ルビ・縁・飾り・アイコンを含む、描く範囲）と、文字の枠（左右は行の送りの範囲、上下は行を並べる枠）。
    /// 前後のページの行が画面の同じ場所に出るか（表示時刻の計算・チェック・重なりの薄め方。<see cref="N3RowPlacement.Paired"/>）を決めるのに使う。
    /// <paramref name="page"/> はそのページの行（スマート水平配置で左右の位置を決めるため）。
    /// </summary>
    internal static ((float Left, float Top, float Right, float Bottom) Ink, (float Left, float Top, float Right, float Bottom) Box) Bounds(
        PreviewLine line, SubtitleLineLayout layout, float W, float H, IReadOnlyList<PreviewLine>? page)
    {
        float baseline = Baseline(line, layout, H);
        float x = SmartX(line, layout, W, page);
        return ((x + layout.Left, baseline + layout.Top, x + layout.Right, baseline + layout.Bottom),
                (x, baseline + layout.BoxTop, x + layout.Width, baseline + layout.BoxBottom));
    }

    /// <summary>描画範囲・文字の枠を、組の判定に使う行の場所にする（<paramref name="layoutId"/> はページのレイアウト）。</summary>
    internal static N3LineArea AreaOf(PreviewLine line, SubtitleLineLayout layout, float W, float H, IReadOnlyList<PreviewLine>? page, int layoutId)
    {
        static N3LineBounds Px((float Left, float Top, float Right, float Bottom) r) =>
            new((int)Math.Floor(r.Left), (int)Math.Floor(r.Top), (int)Math.Ceiling(r.Right), (int)Math.Ceiling(r.Bottom));
        var (ink, box) = Bounds(line, layout, W, H, page);
        return new N3LineArea(Px(ink), Px(box), layoutId);
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

/// <summary>字幕のプレビューで行のドラッグを始めようとしたとき（<see cref="Cancel"/> で止める）。</summary>
public sealed class LayoutDragStartingEventArgs : EventArgs
{
    public LayoutDragStartingEventArgs(PreviewLine line)
    {
        Line = line;
    }

    /// <summary>押した行（<see cref="PreviewLine.Layout"/> がその行のページのレイアウト設定）。</summary>
    public PreviewLine Line { get; }

    /// <summary>ドラッグさせない。</summary>
    public bool Cancel { get; set; }
}

/// <summary>字幕のプレビューで行をドラッグして離したとき（レイアウト設定の前の値と新しい値）。</summary>
public sealed class LayoutDragCompletedEventArgs : EventArgs
{
    public LayoutDragCompletedEventArgs(PreviewLine line, N3LayoutSettings before, N3LayoutSettings after)
    {
        Line = line;
        Before = before;
        After = after;
    }

    public PreviewLine Line { get; }

    public N3LayoutSettings Before { get; }

    /// <summary>新しい値（変わるのは上下余白・左右余白・行間だけ）。</summary>
    public N3LayoutSettings After { get; }
}
