using System.ComponentModel;
using System.Numerics;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NicoKaraPrep.App.Services.Subtitles;
using NicoKaraPrep.App.ViewModels;
using Windows.System;
using Windows.UI;

namespace NicoKaraPrep.App.Views;

/// <summary>文字を選んだ（押した・ドラッグした）ときの行と範囲（<see cref="Core.Model.LyricsLine.Chars"/> の添字、両端を含む）。</summary>
public sealed class LyricCharSelectEventArgs : EventArgs
{
    public LyricCharSelectEventArgs(LineViewModel line, int startUnit, int endUnit, bool started)
    {
        Line = line;
        StartUnit = startUnit;
        EndUnit = endUnit;
        Started = started;
    }

    public LineViewModel Line { get; }

    public int StartUnit { get; }

    public int EndUnit { get; }

    /// <summary>押したとき true（ドラッグで広げているあいだは false）。</summary>
    public bool Started { get; }
}

/// <summary>
/// 行リストの歌詞を字幕の見た目（ワイプ前の配色。ニコカラメーカー3 の歌詞設定パネルと同じく暗い背景）で描く。
/// 文字をクリックすると選び、Shift＋クリック・ドラッグで範囲を広げる（Ctrl＋クリックは行の選び方に任せる）。
/// </summary>
public sealed class LyricLineView : Grid
{
    /// <summary>背景（ニコカラメーカー3 の歌詞設定パネルに近い濃い紺）。</summary>
    private static readonly Color BackgroundColor = Color.FromArgb(255, 0x2E, 0x2E, 0x52);

    /// <summary>選んだ文字の背景。</summary>
    private static readonly Color SelectionColor = Color.FromArgb(150, 0x33, 0x99, 0xFF);

    /// <summary>左の余白 px。</summary>
    private const float LeftPadding = 6;

    /// <summary>縮める倍率の上限（80px の文字が 24px）。</summary>
    private const float MaxScale = 0.3f;

    public static readonly DependencyProperty LineProperty = DependencyProperty.Register(
        nameof(Line), typeof(LineViewModel), typeof(LyricLineView), new PropertyMetadata(null, OnLineChanged));

    private readonly CanvasControl _canvas;
    private LineViewModel? _observed;
    private int _anchorToken = -1;
    private bool _dragging;
    private float _scale = MaxScale;
    private float _originY;

    public LyricLineView()
    {
        Background = new SolidColorBrush(BackgroundColor);
        _canvas = new CanvasControl { IsHitTestVisible = false };
        _canvas.Draw += OnDraw;
        Children.Add(_canvas);
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += (_, _) => _dragging = false;
        Loaded += (_, _) =>
        {
            SubtitleBitmapCache.Loaded += OnBitmapLoaded;
            _canvas.Invalidate();
        };
        Unloaded += (_, _) => SubtitleBitmapCache.Loaded -= OnBitmapLoaded;
    }

    /// <summary>描く行。</summary>
    public LineViewModel? Line
    {
        get => GetValue(LineProperty) as LineViewModel;
        set => SetValue(LineProperty, value);
    }

    /// <summary>文字を選んだ（押した・ドラッグで広げた）。</summary>
    public event EventHandler<LyricCharSelectEventArgs>? CharSelecting;

    private static void OnLineChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (LyricLineView)d;
        if (view._observed is not null) view._observed.PropertyChanged -= view.OnLinePropertyChanged;
        view._observed = e.NewValue as LineViewModel;
        if (view._observed is not null) view._observed.PropertyChanged += view.OnLinePropertyChanged;
        view._anchorToken = -1;
        view._dragging = false;
        view._canvas.Invalidate();
    }

    private void OnLinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LineViewModel.RenderSource) or nameof(LineViewModel.CharSelection)) _canvas.Invalidate();
    }

    private void OnBitmapLoaded(object? sender, EventArgs e) => _canvas.Invalidate();

    // ------------------------------------------------------------ 描く

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        ds.Clear(BackgroundColor);
        if (Line?.RenderSource is not { } source) return;
        float height = (float)sender.ActualHeight;
        if (height < 4) return;
        try
        {
            var layout = source.GetLayout();
            // ルビ・縁を含めて行の高さに収める（同じ大きさの文字の行は同じ倍率になるよう、文字のサイズからも決める）
            float extent = Math.Max(layout.Bottom - layout.Top, layout.MainSize * 2f);
            _scale = extent > 0 ? Math.Min(MaxScale, (height - 3) / extent) : MaxScale;
            _originY = height - 2 - layout.Bottom * _scale;
            var transform = Matrix3x2.CreateScale(_scale) * Matrix3x2.CreateTranslation(LeftPadding, _originY);

            if (Line.CharSelection is (int start, int end))
            {
                float x0 = float.MaxValue, x1 = float.MinValue;
                foreach (var t in layout.Tokens)
                {
                    if (t.UnitEnd < start || t.UnitStart > end) continue;
                    x0 = Math.Min(x0, t.X);
                    x1 = Math.Max(x1, t.X + t.Width);
                }
                if (x1 > x0)
                {
                    ds.FillRectangle(LeftPadding + x0 * _scale, 1, (x1 - x0) * _scale, height - 2, SelectionColor);
                }
            }

            if (SubtitleRenderer.Draw(ds, layout, transform, _scale, null))
            {
                // 画像を読み込み中: 読み終わったら OnBitmapLoaded で描き直す
            }
        }
        catch (Exception ex) when (!sender.Device.IsDeviceLost(ex.HResult))
        {
            // 描けない値があっても行リストは落とさない
        }
    }

    // ------------------------------------------------------------ 文字を選ぶ

    /// <summary>コントロールの x 位置の表示の単位（文字か絵文字）。文字が無ければ -1。</summary>
    private int TokenAt(double x)
    {
        if (Line?.RenderSource is not { } source) return -1;
        var layout = source.GetLayout();
        float lx = ((float)x - LeftPadding) / (_scale > 0 ? _scale : MaxScale);
        if (lx > layout.Width + 4 / Math.Max(_scale, 0.01f)) return -1; // 行の右の何も無いところ
        return layout.TokenAt(lx);
    }

    private void Raise(int anchor, int active, bool started)
    {
        if (Line?.RenderSource is not { } source) return;
        var layout = source.GetLayout();
        if (anchor < 0 || active < 0 || anchor >= layout.Tokens.Count || active >= layout.Tokens.Count) return;
        var a = layout.Tokens[Math.Min(anchor, active)];
        var b = layout.Tokens[Math.Max(anchor, active)];
        CharSelecting?.Invoke(this, new LyricCharSelectEventArgs(Line, a.UnitStart, b.UnitEnd, started));
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed) return;
        if (e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control)) return; // Ctrl＋クリックは行の選び方（ListView）に任せる
        int token = TokenAt(point.Position.X);
        if (token < 0)
        {
            // 文字の無いところは文字の選択を外して、行を選ぶのは ListView に任せる
            if (Line is { } line) CharSelecting?.Invoke(this, new LyricCharSelectEventArgs(line, -1, -1, true));
            return;
        }
        bool extend = e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift) && _anchorToken >= 0 && Line?.CharSelection is not null;
        if (!extend) _anchorToken = token;
        _dragging = true;
        CapturePointer(e.Pointer);
        Raise(_anchorToken, token, started: !extend);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging || _anchorToken < 0) return;
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            _dragging = false;
            return;
        }
        int token = TokenAt(Math.Max(0, point.Position.X));
        if (token < 0 && Line?.RenderSource is { } source) token = source.GetLayout().Tokens.Count - 1;
        if (token >= 0) Raise(_anchorToken, token, started: false);
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }
}
