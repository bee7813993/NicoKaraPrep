using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NicoKaraPrep.App.Services;
using NicoKaraPrep.Core.Model;
using Windows.Foundation;
using Windows.UI;

namespace NicoKaraPrep.App.Views;

/// <summary>
/// フォント設定（<see cref="N3FontSet"/>）の見本。サンプル文字列の左半分をワイプ後、右半分をワイプ前の配色で描く（ニコカラメーカー3 のサンプル「永」と同じ）。
/// 文字は輪郭（<see cref="CanvasGeometry.CreateText"/>）にして「飾り → 縁 2 → 縁 → 本体」の順に描く。
/// ニコカラメーカー3 の描画処理は読めないため、次のように近似する。
/// 縁は輪郭を幅 EdgePx の線で、縁 2 は幅 EdgePx + Edge2Px の線で描く（線は輪郭の両側に半分ずつ広がる）。
/// 影は本体の形を DecorSizePx だけ右下へずらし、ブラーは本体＋縁の形を標準偏差 DecorSizePx / 2 でぼかす（濃さ 0/1/2 = 不透明度 50/75/100%）。
/// グラデーション・ミルフィーユの位置 0〜1 は文字列の字面の上端〜下端に当て、画像は字面の左上から拡大率で敷き詰める。
/// FontSet の参照先の中身を書き換えたときは <see cref="Invalidate"/> を呼ぶ（同じ参照を設定し直しても描き直さないため）。
/// </summary>
public sealed partial class FontPreviewControl : UserControl
{
    /// <summary>再描画をまとめる間隔。</summary>
    private static readonly TimeSpan RedrawDelay = TimeSpan.FromMilliseconds(100);

    /// <summary>ニコカラメーカー3 が新規に書き出すときの既定色（添字 = <see cref="N3FontDetail.Brushes"/>。Color が空の箇所に使う）。</summary>
    private static readonly string[] DefaultColors =
    {
        "FFFFFF", "000000", "FFFFFF", "000000",
        "4DA3FF", "FFFFFF", "000000", "000000",
    };

    /// <summary>色が読めない・塗りの種類が範囲外のときの代わりの灰色。</summary>
    private static readonly Color FallbackGray = Color.FromArgb(255, 0x80, 0x80, 0x80);

    /// <summary>ミルフィーユの帯の境目をくっきりさせるための、帯の終わりの位置の手前へのずらし幅。</summary>
    private const float MilleFeuilleEdge = 0.0005f;

    /// <summary>ブラーの濃さ（0–2）ごとの不透明度。</summary>
    private static readonly float[] BlurOpacities = { 0.5f, 0.75f, 1f };

    private const double DefaultReferenceHeight = 1080;
    private const float SideMargin = 16;
    private const float VerticalMargin = 6;
    private const float GuideTop = 22;
    private const float GuideLine = 17;

    private readonly DispatcherQueueTimer _redrawTimer;
    private CanvasControl? _canvas;

    // 画像ブラシの画像（キー = BitmapPath。値 null = 読めなかった）。デバイスごとに作り直す
    private readonly Dictionary<string, CanvasBitmap?> _bitmaps = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _loadingBitmaps = new(StringComparer.OrdinalIgnoreCase);
    private CanvasRenderTarget? _hatch;
    private int _resourceGeneration;

    public FontPreviewControl()
    {
        InitializeComponent();
        _redrawTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _redrawTimer.Interval = RedrawDelay;
        _redrawTimer.IsRepeating = false;
        _redrawTimer.Tick += (_, _) => _canvas?.Invalidate();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    // ------------------------------------------------------------ 公開プロパティ

    public static readonly DependencyProperty FontSetProperty = DependencyProperty.Register(
        nameof(FontSet), typeof(N3FontSet), typeof(FontPreviewControl), new PropertyMetadata(null, OnAppearanceChanged));

    public static readonly DependencyProperty SampleTextProperty = DependencyProperty.Register(
        nameof(SampleText), typeof(string), typeof(FontPreviewControl), new PropertyMetadata("永", OnAppearanceChanged));

    public static readonly DependencyProperty RubyTextProperty = DependencyProperty.Register(
        nameof(RubyText), typeof(string), typeof(FontPreviewControl), new PropertyMetadata("", OnAppearanceChanged));

    public static readonly DependencyProperty ReferenceHeightProperty = DependencyProperty.Register(
        nameof(ReferenceHeight), typeof(double), typeof(FontPreviewControl), new PropertyMetadata(DefaultReferenceHeight, OnAppearanceChanged));

    public static readonly DependencyProperty BackgroundColorProperty = DependencyProperty.Register(
        nameof(BackgroundColor), typeof(Color), typeof(FontPreviewControl),
        new PropertyMetadata(Color.FromArgb(255, 0x38, 0x38, 0x38), OnAppearanceChanged));

    public static readonly DependencyProperty ShowGuideProperty = DependencyProperty.Register(
        nameof(ShowGuide), typeof(bool), typeof(FontPreviewControl), new PropertyMetadata(true, OnAppearanceChanged));

    /// <summary>描くフォント設定（null なら何も描かない）。</summary>
    public N3FontSet? FontSet
    {
        get => GetValue(FontSetProperty) as N3FontSet;
        set => SetValue(FontSetProperty, value);
    }

    /// <summary>本文の見本の文字列（既定「永」。複数文字も可）。</summary>
    public string SampleText
    {
        get => GetValue(SampleTextProperty) as string ?? "";
        set => SetValue(SampleTextProperty, value ?? "");
    }

    /// <summary>ルビの見本の文字列（空ならルビ行を描かない）。ルビ／漢字（Faces[3]）の実効値で本文の上に描く。</summary>
    public string RubyText
    {
        get => GetValue(RubyTextProperty) as string ?? "";
        set => SetValue(RubyTextProperty, value ?? "");
    }

    /// <summary>フォントのサイズ px の基準にする画面の高さ（既定 1080）。コントロールの高さをこの高さとみなして等比で縮めて描く。</summary>
    public double ReferenceHeight
    {
        get => (double)GetValue(ReferenceHeightProperty);
        set => SetValue(ReferenceHeightProperty, value);
    }

    /// <summary>背景色（既定は濃い灰色）。</summary>
    public Color BackgroundColor
    {
        get => (Color)GetValue(BackgroundColorProperty);
        set => SetValue(BackgroundColorProperty, value);
    }

    /// <summary>「ワイプ後」「ワイプ前」の見出しと「近似表示」などの注記を描くか（既定 true）。</summary>
    public bool ShowGuide
    {
        get => (bool)GetValue(ShowGuideProperty);
        set => SetValue(ShowGuideProperty, value);
    }

    /// <summary>描き直しを予約する（100ms の間の呼び出しはまとめて 1 回描く）。FontSet の中身を変えたときに呼ぶ。</summary>
    public void Invalidate()
    {
        if (_canvas is null) return; // 表示されたときに描く
        if (!_redrawTimer.IsRunning) _redrawTimer.Start();
    }

    private static void OnAppearanceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((FontPreviewControl)d).Invalidate();

    // ------------------------------------------------------------ CanvasControl の作成と解放

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_canvas is not null) return;
        var canvas = new CanvasControl();
        canvas.CreateResources += OnCreateResources;
        canvas.Draw += OnDraw;
        CanvasHost.Children.Add(canvas);
        _canvas = canvas;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) return; // 付け替えで Loaded が先に来たときは使い続ける
        _redrawTimer.Stop();
        if (_canvas is CanvasControl canvas)
        {
            canvas.CreateResources -= OnCreateResources;
            canvas.Draw -= OnDraw;
            canvas.RemoveFromVisualTree(); // Win2D の作法: 親から外してデバイスのリソースを解放する
            CanvasHost.Children.Remove(canvas);
            _canvas = null;
        }
        ReleaseResources();
    }

    private void OnCreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
    {
        if (args.Reason == CanvasCreateResourcesReason.DpiChanged) return; // 画像は DPI に依らない
        ReleaseResources();
        _hatch = CreateHatch(sender);
        args.TrackAsyncAction(LoadBitmapsAsync(sender, BitmapPathsOf(FontSet)).AsAsyncAction());
    }

    private void ReleaseResources()
    {
        _resourceGeneration++;
        foreach (var bitmap in _bitmaps.Values) bitmap?.Dispose();
        _bitmaps.Clear();
        _loadingBitmaps.Clear();
        _hatch?.Dispose();
        _hatch = null;
    }

    /// <summary>画像が無いときに使う灰色の斜線（12px 四方を敷き詰める）。</summary>
    private static CanvasRenderTarget CreateHatch(ICanvasResourceCreator creator)
    {
        const float size = 12;
        var target = new CanvasRenderTarget(creator, size, size, 96);
        using var ds = target.CreateDrawingSession();
        ds.Clear(FallbackGray);
        var line = Color.FromArgb(255, 0x58, 0x58, 0x58);
        ds.DrawLine(-1, size + 1, size + 1, -1, line, 2.5f);
        ds.DrawLine(-1, 1, 1, -1, line, 2.5f);
        ds.DrawLine(size - 1, size + 1, size + 1, size - 1, line, 2.5f);
        return target;
    }

    private static IEnumerable<string> BitmapPathsOf(N3FontSet? fontSet)
    {
        if (fontSet is null) return Array.Empty<string>();
        return fontSet.Detail.Brushes
            .Where(b => b is not null && b.Type == N3Brush.TypeBitmap && !string.IsNullOrWhiteSpace(b.BitmapPath))
            .Select(b => b.BitmapPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task LoadBitmapsAsync(ICanvasResourceCreator creator, IEnumerable<string> paths)
    {
        int generation = _resourceGeneration;
        foreach (string path in paths)
        {
            var bitmap = await TryLoadBitmapAsync(creator, path);
            if (generation != _resourceGeneration)
            {
                bitmap?.Dispose();
                return;
            }
            _bitmaps[path] = bitmap;
        }
    }

    private static async Task<CanvasBitmap?> TryLoadBitmapAsync(ICanvasResourceCreator creator, string path)
    {
        try
        {
            if (!Path.IsPathFullyQualified(path) || !File.Exists(path)) return null;
            return await CanvasBitmap.LoadAsync(creator, path);
        }
        catch (Exception)
        {
            return null; // 壊れた画像・対応していない形式は斜線で代わりに描く
        }
    }

    /// <summary>画像を返す。まだ読んでいなければ読み始めて null を返し、読み終わったら描き直す。</summary>
    private CanvasBitmap? GetBitmap(CanvasControl canvas, string path, out bool loading)
    {
        loading = false;
        if (_bitmaps.TryGetValue(path, out var bitmap)) return bitmap;
        loading = true;
        if (_loadingBitmaps.Add(path)) _ = LoadBitmapLaterAsync(canvas, path);
        return null;
    }

    private async Task LoadBitmapLaterAsync(CanvasControl canvas, string path)
    {
        int generation = _resourceGeneration;
        var bitmap = await TryLoadBitmapAsync(canvas, path);
        if (generation != _resourceGeneration || !ReferenceEquals(canvas, _canvas))
        {
            bitmap?.Dispose();
            return;
        }
        _loadingBitmaps.Remove(path);
        _bitmaps[path] = bitmap;
        canvas.Invalidate();
    }

    // ------------------------------------------------------------ 描画

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        ds.Clear(BackgroundColor);
        var size = sender.Size;
        if (size.Width < 1 || size.Height < 1) return;
        try
        {
            DrawPreview(sender, ds, size);
        }
        catch (Exception ex) when (!sender.Device.IsDeviceLost(ex.HResult))
        {
            // 描けない値があっても落とさず、理由を出す
            DrawCenteredMessage(ds, size, "プレビューを描けません: " + ex.Message);
        }
    }

    private void DrawPreview(CanvasControl canvas, CanvasDrawingSession ds, Size size)
    {
        float width = (float)size.Width;
        float height = (float)size.Height;
        bool guide = ShowGuide;
        var fontSet = FontSet;
        if (fontSet is null)
        {
            if (guide) DrawCenteredMessage(ds, size, "フォント設定を選ぶと、ここに見本を表示します");
            return;
        }

        var warnings = new List<string>();
        var trash = new List<IDisposable>();
        try
        {
            using var strokeStyle = new CanvasStrokeStyle
            {
                LineJoin = CanvasLineJoin.Round,
                StartCap = CanvasCapStyle.Round,
                EndCap = CanvasCapStyle.Round,
            };

            // 1. ReferenceHeight 基準の px 座標で本文とルビの輪郭を作る（本文の横の中心を x = 0 にする）
            var lines = new List<PreviewLine>();
            var body = CreateLine(canvas, SampleText, SafeEffectiveFace(fontSet, 0), warnings);
            if (body is not null) trash.Add(body);
            var ruby = CreateLine(canvas, RubyText, SafeEffectiveFace(fontSet, 3), warnings);
            if (ruby is not null) trash.Add(ruby);
            if (body is not null)
            {
                body.Move(-body.Box.CenterX, -body.Box.Top);
                lines.Add(body);
            }
            if (ruby is not null)
            {
                // ルビの字面の下端を本文の字面の上端の少し上に置く（行の間隔はレイアウト設定側にあるため目安）
                double baseTop = body?.Face.Top ?? 0;
                double gap = (body?.OuterEdge ?? 0) / 2 + ruby.OuterEdge / 2 + (body?.SizePx ?? ruby.SizePx) * 0.04;
                ruby.Move(-ruby.Box.CenterX, baseTop - gap - ruby.Face.Bottom);
                lines.Add(ruby);
            }
            if (lines.Count == 0)
            {
                if (guide) DrawGuide(ds, width, height, width / 2, warnings);
                return;
            }

            var detail = fontSet.Detail;
            int decorKind = detail.DecorKind;
            double decorSize = Finite(detail.DecorSizePx, 0, 0, 500);
            foreach (int slot in UsedSlots(lines, decorKind)) CheckImage(canvas, detail, slot, warnings);

            // 2. 縁・飾りまで含めた範囲を測り、コントロールに収まる等比の倍率を決める
            var extent = Bounds.Empty;
            foreach (var line in lines)
            {
                var ink = line.Face;
                var outer = ink.Inflate(line.OuterEdge / 2);
                extent = extent.Union(line.Box).Union(outer);
                if (decorKind == 1) extent = extent.Union(ink.Offset(decorSize, decorSize));
                if (decorKind == 2) extent = extent.Union(outer.Inflate(decorSize * 1.5));
            }

            float top = guide ? GuideTop : 0;
            float bottom = guide ? GuideLine * (warnings.Count + 1) + 4 : 0;
            double availWidth = Math.Max(1, width - SideMargin * 2);
            double availHeight = Math.Max(1, height - top - bottom - VerticalMargin * 2);
            double reference = Finite(ReferenceHeight, DefaultReferenceHeight, 1, 100000);
            double scale = height / reference;
            double halfExtent = Math.Max(-extent.Left, extent.Right);
            if (halfExtent > 0) scale = Math.Min(scale, availWidth / 2 / halfExtent);
            if (extent.Height > 0) scale = Math.Min(scale, availHeight / extent.Height);
            if (!(scale > 0) || !double.IsFinite(scale)) return;

            float s = (float)scale;
            float centerX = width / 2; // 本文の横の中心 = ワイプ前後の境目
            float originY = (float)(top + VerticalMargin + (availHeight - extent.Height * scale) / 2 - extent.Top * scale);
            var toScreen = Matrix3x2.CreateScale(s) * Matrix3x2.CreateTranslation(centerX, originY);
            foreach (var line in lines) line.ToScreen(toScreen, s);

            // 3. 左半分にワイプ後（Brushes[0..3]）、右半分にワイプ前（Brushes[4..7]）を描く
            for (int half = 0; half < 2; half++)
            {
                int offset = half == 0 ? 0 : N3FontDetail.BeforeOffset;
                var clip = half == 0
                    ? new Rect(0, 0, centerX, height)
                    : new Rect(centerX, 0, Math.Max(0, width - centerX), height);
                // 境目は縁をぼかさずに切る（ぼかすと左右の半透明な画素が重なって背景の色の線が出る）
                ds.Antialiasing = CanvasAntialiasing.Aliased;
                using var layer = ds.CreateLayer(1f, clip);
                ds.Antialiasing = CanvasAntialiasing.Antialiased;

                DrawDecor(canvas, ds, lines, detail, offset + 3, decorKind, (float)(decorSize * s), strokeStyle, trash);
                foreach (var line in lines)
                {
                    if (line.Edge2Width > 0)
                    {
                        ds.DrawGeometry(line.Screen, CreateBrush(canvas, detail, offset + 2, line.ScreenBox, s, trash), line.Edge2Width, strokeStyle);
                    }
                }
                foreach (var line in lines)
                {
                    if (line.EdgeWidth > 0)
                    {
                        ds.DrawGeometry(line.Screen, CreateBrush(canvas, detail, offset + 1, line.ScreenBox, s, trash), line.EdgeWidth, strokeStyle);
                    }
                }
                foreach (var line in lines)
                {
                    ds.FillGeometry(line.Screen, CreateBrush(canvas, detail, offset, line.ScreenBox, s, trash));
                }
            }

            if (guide) DrawGuide(ds, width, height, centerX, warnings);
        }
        finally
        {
            foreach (var d in trash) d.Dispose();
        }
    }

    /// <summary>文字飾り。影は本体の形を右下へずらして塗り、ブラーは本体＋縁の形をぼかして下に敷く。</summary>
    private void DrawDecor(
        CanvasControl canvas, CanvasDrawingSession ds, List<PreviewLine> lines, N3FontDetail detail, int slot,
        int decorKind, float decorSize, CanvasStrokeStyle strokeStyle, List<IDisposable> trash)
    {
        if (decorKind == 1)
        {
            if (decorSize <= 0) return; // ずらさない影は本体に隠れる
            var move = Matrix3x2.CreateTranslation(decorSize, decorSize);
            foreach (var line in lines)
            {
                var shadow = line.Screen.Transform(move);
                trash.Add(shadow);
                var box = new Rect(line.ScreenBox.X + decorSize, line.ScreenBox.Y + decorSize, line.ScreenBox.Width, line.ScreenBox.Height);
                ds.FillGeometry(shadow, CreateBrush(canvas, detail, slot, box, line.Scale, trash));
            }
        }
        else if (decorKind == 2)
        {
            var source = new CanvasCommandList(canvas);
            trash.Add(source);
            using (var cds = source.CreateDrawingSession())
            {
                foreach (var line in lines)
                {
                    cds.FillGeometry(line.Silhouette(strokeStyle), CreateBrush(canvas, detail, slot, line.ScreenBox, line.Scale, trash));
                }
            }
            var blur = new GaussianBlurEffect
            {
                Source = source,
                BlurAmount = Math.Clamp(decorSize / 2, 0, 250), // 標準偏差（Direct2D の上限 250）。広がり（DecorSizePx）のおよそ半分
                BorderMode = EffectBorderMode.Soft,
            };
            trash.Add(blur);
            float opacity = BlurOpacities[Math.Clamp(detail.BlurLevel, 0, BlurOpacities.Length - 1)];
            using var layer = ds.CreateLayer(opacity);
            ds.DrawImage(blur);
        }
    }

    /// <summary>配色 1 箇所のブラシ。グラデーション・ミルフィーユは <paramref name="box"/> の上端を 0、下端を 1 にする。</summary>
    private ICanvasBrush CreateBrush(CanvasControl canvas, N3FontDetail detail, int slot, Rect box, float scale, List<IDisposable> trash)
    {
        var b = slot >= 0 && slot < detail.Brushes.Length ? detail.Brushes[slot] : null;
        ICanvasBrush brush;
        if (b is null)
        {
            brush = new CanvasSolidColorBrush(canvas, ParseColor(DefaultColors[slot], 100));
        }
        else
        {
            switch (b.Type)
            {
                case N3Brush.TypeSolid:
                    string color = b.Color.Length == 0 ? DefaultColors[slot] : b.Color;
                    brush = new CanvasSolidColorBrush(canvas, ParseColor(color, b.AlphaPercent));
                    break;
                case N3Brush.TypeGradient:
                case N3Brush.TypeMilleFeuille:
                    brush = new CanvasLinearGradientBrush(canvas, GradientStops(b), CanvasEdgeBehavior.Clamp, CanvasAlphaMode.Premultiplied)
                    {
                        StartPoint = new Vector2((float)box.X, (float)box.Top),
                        EndPoint = new Vector2((float)box.X, (float)Math.Max(box.Bottom, box.Top + 0.001)),
                    };
                    break;
                case N3Brush.TypeBitmap:
                    brush = CreateImageBrush(canvas, b, box, scale);
                    break;
                default:
                    brush = new CanvasSolidColorBrush(canvas, FallbackGray);
                    break;
            }
        }
        trash.Add(brush);
        return brush;
    }

    /// <summary>画像ブラシ。文字の範囲の左上を起点に、拡大率（BitmapScale %）で敷き詰める。画像が無い・読み込み中なら灰色の斜線。</summary>
    private ICanvasBrush CreateImageBrush(CanvasControl canvas, N3Brush b, Rect box, float scale)
    {
        var bitmap = string.IsNullOrWhiteSpace(b.BitmapPath) ? null : GetBitmap(canvas, b.BitmapPath, out _);
        if (bitmap is null)
        {
            if (_hatch is null) return new CanvasSolidColorBrush(canvas, FallbackGray);
            return new CanvasImageBrush(canvas, _hatch)
            {
                ExtendX = CanvasEdgeBehavior.Wrap,
                ExtendY = CanvasEdgeBehavior.Wrap,
            };
        }
        int percent = b.BitmapScale == 0 ? 100 : Math.Clamp(b.BitmapScale, 1, 1000); // ニコカラメーカーと同じ丸め
        float k = percent / 100f * scale;
        return new CanvasImageBrush(canvas, bitmap)
        {
            ExtendX = CanvasEdgeBehavior.Wrap,
            ExtendY = CanvasEdgeBehavior.Wrap,
            Transform = Matrix3x2.CreateScale(k) * Matrix3x2.CreateTranslation((float)box.X, (float)box.Top),
        };
    }

    /// <summary>描く配色の箇所（縁・縁 2・飾りは使うときだけ）。</summary>
    private static IEnumerable<int> UsedSlots(List<PreviewLine> lines, int decorKind)
    {
        bool edge = lines.Any(l => l.EdgePx > 0);
        bool edge2 = lines.Any(l => l.Edge2Px > 0);
        bool decor = decorKind is 1 or 2;
        foreach (int offset in new[] { 0, N3FontDetail.BeforeOffset })
        {
            yield return offset;
            if (edge) yield return offset + 1;
            if (edge2) yield return offset + 2;
            if (decor) yield return offset + 3;
        }
    }

    /// <summary>画像ブラシの画像を読み始め、読めないものを注記に足す（注記の行数で配置が変わるため描く前に調べる）。</summary>
    private void CheckImage(CanvasControl canvas, N3FontDetail detail, int slot, List<string> warnings)
    {
        var b = detail.Brushes[slot];
        if (b is null || b.Type != N3Brush.TypeBitmap) return;
        if (string.IsNullOrWhiteSpace(b.BitmapPath))
        {
            AddWarning(warnings, "画像が指定されていない箇所は斜線で表示");
        }
        else if (GetBitmap(canvas, b.BitmapPath, out bool loading) is null && !loading)
        {
            AddWarning(warnings, $"画像「{Path.GetFileName(b.BitmapPath)}」を読めないため斜線で表示");
        }
    }

    /// <summary>
    /// グラデーション・ミルフィーユのマーカーを Win2D の stop にする。マーカーが無ければニコカラメーカーの既定 3 点。
    /// ミルフィーユは各マーカーの色を次のマーカーの位置まで塗る（最後のマーカーの色は使わない）。境目は位置をわずかにずらした 2 つの stop でくっきりさせる。
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

    /// <summary>"RRGGBB" と不透明度 % から色を作る。読めない色は灰色。</summary>
    private static Color ParseColor(string? web16, int alphaPercent)
    {
        byte a = (byte)Math.Round(Math.Clamp(alphaPercent, 0, 100) * 255 / 100.0);
        return N3FontSet.TryParseWeb16(web16, out byte r, out byte g, out byte b)
            ? Color.FromArgb(a, r, g, b)
            : Color.FromArgb(a, FallbackGray.R, FallbackGray.G, FallbackGray.B);
    }

    // ------------------------------------------------------------ 文字の輪郭

    private static N3FontFace SafeEffectiveFace(N3FontSet fontSet, int index)
    {
        try
        {
            return N3FontLibrary.EffectiveFace(fontSet, index);
        }
        catch (Exception)
        {
            return N3FontLibrary.NkmDefaultFace(); // 手で壊した設定（フェイスが null など）でも落とさない
        }
    }

    /// <summary>文字列 1 行の輪郭を ReferenceHeight 基準の px で作る。空・空白だけなら null。</summary>
    private static PreviewLine? CreateLine(ICanvasResourceCreator creator, string? text, N3FontFace face, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var font = DirectWriteFontResolver.Resolve(face.FontName, face.FaceName);
        if (!font.Found) AddWarning(warnings, $"フォント「{face.FontName}」が無いため代わりのフォントで表示");

        float sizePx = (float)Finite(face.SizePx, 100, 1, 2000);
        float xScale = (face.XScale > 0 ? Math.Clamp(face.XScale, 10, 1000) : 100) / 100f;
        using var format = new CanvasTextFormat
        {
            FontFamily = font.Family,
            FontSize = sizePx,
            FontWeight = font.Weight,
            FontStyle = font.Style,
            FontStretch = font.Stretch,
            WordWrapping = CanvasWordWrapping.NoWrap,
        };
        using var layout = new CanvasTextLayout(creator, text, format, 100000f, 100000f);
        using var raw = CanvasGeometry.CreateText(layout);
        using var scaled = raw.Transform(Matrix3x2.CreateScale(xScale, 1f));
        // 重なった輪郭（太字のフォントに多い）をまとめてから縁を付ける（重なったまま線にすると縁に小さな穴が出ることがある）
        var geometry = scaled.Outline();

        var lb = layout.LayoutBounds;
        var box = new Bounds(lb.Left * xScale, lb.Top, lb.Right * xScale, lb.Bottom);
        var ink = Bounds.FromRect(TryComputeBounds(geometry));

        double edge = Finite(face.EdgePx, 0, 0, 500);
        double edge2 = face.UseEdge2 == true ? Finite(face.Edge2Px, 0, 0, 500) : 0;
        return new PreviewLine(geometry, box, ink, sizePx, edge, edge2 > 0 ? edge + edge2 : 0);
    }

    private static Rect? TryComputeBounds(CanvasGeometry geometry)
    {
        try
        {
            return geometry.ComputeBounds();
        }
        catch (Exception)
        {
            return null; // 輪郭の無い文字（字形の無い空白など）
        }
    }

    private static double Finite(double value, double fallback, double min, double max) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    private static void AddWarning(List<string> warnings, string message)
    {
        if (!warnings.Contains(message)) warnings.Add(message);
    }

    // ------------------------------------------------------------ 見出しと注記

    private void DrawGuide(CanvasDrawingSession ds, float width, float height, float centerX, List<string> warnings)
    {
        var color = GuideColor(BackgroundColor);
        using var heading = new CanvasTextFormat
        {
            FontSize = 12,
            HorizontalAlignment = CanvasHorizontalAlignment.Center,
            WordWrapping = CanvasWordWrapping.NoWrap,
        };
        ds.DrawText("ワイプ後", new Rect(0, 3, centerX, GuideLine), color, heading);
        ds.DrawText("ワイプ前", new Rect(centerX, 3, Math.Max(0, width - centerX), GuideLine), color, heading);
        ds.DrawLine(centerX, 4, centerX, GuideTop - 4, Color.FromArgb((byte)(color.A / 2), color.R, color.G, color.B), 1);

        using var note = new CanvasTextFormat
        {
            FontSize = 11,
            WordWrapping = CanvasWordWrapping.NoWrap,
            Options = CanvasDrawTextOptions.Clip,
        };
        var notes = warnings.Append("近似表示（ニコカラメーカー3 の実際の描画とは異なることがあります）").ToList();
        float y = height - 4 - GuideLine * notes.Count;
        foreach (string text in notes)
        {
            ds.DrawText(text, new Rect(8, y, Math.Max(1, width - 16), GuideLine), color, note);
            y += GuideLine;
        }
    }

    private void DrawCenteredMessage(CanvasDrawingSession ds, Size size, string message)
    {
        using var format = new CanvasTextFormat
        {
            FontSize = 13,
            HorizontalAlignment = CanvasHorizontalAlignment.Center,
            VerticalAlignment = CanvasVerticalAlignment.Center,
            WordWrapping = CanvasWordWrapping.Wrap,
        };
        ds.DrawText(message, new Rect(8, 0, Math.Max(1, size.Width - 16), size.Height), GuideColor(BackgroundColor), format);
    }

    /// <summary>背景の明るさに合わせた見出しの色（暗い背景なら白、明るい背景なら黒。やや透明）。</summary>
    private static Color GuideColor(Color background)
    {
        double luma = 0.299 * background.R + 0.587 * background.G + 0.114 * background.B;
        return luma < 140 ? Color.FromArgb(190, 255, 255, 255) : Color.FromArgb(190, 0, 0, 0);
    }

    // ------------------------------------------------------------ 補助の型

    /// <summary>左・上・右・下で持つ矩形（空を扱えるように Rect の代わりに使う）。</summary>
    private readonly record struct Bounds(double Left, double Top, double Right, double Bottom)
    {
        public static readonly Bounds Empty = new(double.PositiveInfinity, double.PositiveInfinity, double.NegativeInfinity, double.NegativeInfinity);

        public bool IsEmpty => !(Right >= Left && Bottom >= Top);

        public double Width => IsEmpty ? 0 : Right - Left;

        public double Height => IsEmpty ? 0 : Bottom - Top;

        public double CenterX => (Left + Right) / 2;

        public static Bounds FromRect(Rect? rect)
        {
            if (rect is not Rect r || !double.IsFinite(r.X) || !double.IsFinite(r.Y) ||
                !double.IsFinite(r.Width) || !double.IsFinite(r.Height) || r.Width <= 0 || r.Height <= 0)
            {
                return Empty;
            }
            return new Bounds(r.Left, r.Top, r.Right, r.Bottom);
        }

        public Bounds Union(Bounds other)
        {
            if (other.IsEmpty) return this;
            if (IsEmpty) return other;
            return new Bounds(Math.Min(Left, other.Left), Math.Min(Top, other.Top), Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom));
        }

        public Bounds Inflate(double amount) => IsEmpty ? this : new Bounds(Left - amount, Top - amount, Right + amount, Bottom + amount);

        public Bounds Offset(double dx, double dy) => IsEmpty ? this : new Bounds(Left + dx, Top + dy, Right + dx, Bottom + dy);
    }

    /// <summary>描く文字列 1 行（本文かルビ）。最初は ReferenceHeight 基準の px、<see cref="ToScreen"/> のあとは画面の座標で持つ。</summary>
    private sealed class PreviewLine : IDisposable
    {
        private CanvasGeometry _geometry;
        private CanvasGeometry? _silhouette;

        public PreviewLine(CanvasGeometry geometry, Bounds box, Bounds ink, double sizePx, double edgePx, double outerEdge2Px)
        {
            _geometry = geometry;
            Box = box;
            Ink = ink;
            SizePx = sizePx;
            EdgePx = edgePx;
            Edge2Px = outerEdge2Px;
        }

        /// <summary>文字の枠（レイアウトの範囲）。</summary>
        public Bounds Box { get; private set; }

        /// <summary>字面（輪郭の範囲）。字形の無い文字だけなら空。</summary>
        public Bounds Ink { get; private set; }

        /// <summary>字面（空なら文字の枠）。</summary>
        public Bounds Face => Ink.IsEmpty ? Box : Ink;

        public double SizePx { get; }

        /// <summary>縁の線の幅 px。</summary>
        public double EdgePx { get; }

        /// <summary>縁 2 の線の幅 px（縁 + 縁 2。縁 2 を付けないなら 0）。</summary>
        public double Edge2Px { get; }

        /// <summary>いちばん外側の線の幅 px。</summary>
        public double OuterEdge => Math.Max(EdgePx, Edge2Px);

        /// <summary>画面の座標の輪郭。</summary>
        public CanvasGeometry Screen => _geometry;

        /// <summary>画面の座標で、グラデーション・画像を当てる範囲（字面。字面が無ければ文字の枠）。</summary>
        public Rect ScreenBox { get; private set; }

        public float Scale { get; private set; } = 1;

        public float EdgeWidth { get; private set; }

        public float Edge2Width { get; private set; }

        /// <summary>px 座標のまま平行移動する。</summary>
        public void Move(double dx, double dy)
        {
            Replace(Matrix3x2.CreateTranslation((float)dx, (float)dy));
            Box = Box.Offset(dx, dy);
            Ink = Ink.Offset(dx, dy);
        }

        /// <summary>画面の座標へ移す（線の幅も倍率に合わせる）。</summary>
        public void ToScreen(Matrix3x2 transform, float scale)
        {
            Replace(transform);
            Scale = scale;
            EdgeWidth = (float)(EdgePx * scale);
            Edge2Width = (float)(Edge2Px * scale);
            var range = Face;
            var tl = Vector2.Transform(new Vector2((float)range.Left, (float)range.Top), transform);
            var br = Vector2.Transform(new Vector2((float)range.Right, (float)range.Bottom), transform);
            ScreenBox = new Rect(tl.X, tl.Y, Math.Max(0, br.X - tl.X), Math.Max(0, br.Y - tl.Y));
        }

        /// <summary>本体と、いちばん外側の縁を合わせた形（ブラーの元）。</summary>
        public CanvasGeometry Silhouette(CanvasStrokeStyle strokeStyle)
        {
            if (_silhouette is not null) return _silhouette;
            float outer = Math.Max(EdgeWidth, Edge2Width);
            if (outer <= 0) return _geometry;
            using var stroke = _geometry.Stroke(outer, strokeStyle);
            _silhouette = _geometry.CombineWith(stroke, Matrix3x2.Identity, CanvasGeometryCombine.Union);
            return _silhouette;
        }

        private void Replace(Matrix3x2 transform)
        {
            var moved = _geometry.Transform(transform);
            _geometry.Dispose();
            _geometry = moved;
            _silhouette?.Dispose();
            _silhouette = null;
        }

        public void Dispose()
        {
            _silhouette?.Dispose();
            _geometry.Dispose();
        }
    }
}
