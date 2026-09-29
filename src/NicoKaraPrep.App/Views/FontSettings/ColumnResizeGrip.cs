using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace NicoKaraPrep.App.Views.FontSettings;

/// <summary>
/// 列のあいだのつまみ。左右にドラッグするか、フォーカスを置いて ← / → キーで、隣の列の幅を変える（カーソルは左右の矢印）。
/// ダブルクリックで元の幅に戻す。幅の計算と保存は持ち主が行う（<see cref="DragStarted"/>・<see cref="Dragging"/>・
/// <see cref="DragCompleted"/>・<see cref="Stepped"/>・<see cref="ResetRequested"/>）。
/// </summary>
public sealed partial class ColumnResizeGrip : ContentControl
{
    /// <summary>キーで 1 回に動かす幅（px）。</summary>
    public const double KeyStep = 20;

    private bool _dragging;
    private double _startX;

    public ColumnResizeGrip()
    {
        IsTabStop = true;
        UseSystemFocusVisuals = true;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
        DoubleTapped += (_, e) =>
        {
            ResetRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        };
    }

    /// <summary>ドラッグを始めたとき（持ち主は今の幅を覚えておく）。</summary>
    public event EventHandler? DragStarted;

    /// <summary>ドラッグ中（引数は、始めたときからの横の移動量 px。つまみ自身が動いても変わらないウィンドウ上の位置で測る）。</summary>
    public event EventHandler<double>? Dragging;

    /// <summary>ドラッグを終えたとき（持ち主は幅を保存する）。</summary>
    public event EventHandler? DragCompleted;

    /// <summary>← / → キー（引数は幅の増減 px）。</summary>
    public event EventHandler<double>? Stepped;

    /// <summary>ダブルクリック（元の幅に戻す）。</summary>
    public event EventHandler? ResetRequested;

    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragging = CapturePointer(e.Pointer);
        if (!_dragging) return;
        _startX = e.GetCurrentPoint(null).Position.X;
        DragStarted?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerRoutedEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging) return;
        Dragging?.Invoke(this, e.GetCurrentPoint(null).Position.X - _startX);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;
        ReleasePointerCapture(e.Pointer);
        EndDrag();
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerRoutedEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        EndDrag();
    }

    private void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        DragCompleted?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Left:
                Stepped?.Invoke(this, -KeyStep);
                e.Handled = true;
                break;
            case VirtualKey.Right:
                Stepped?.Invoke(this, KeyStep);
                e.Handled = true;
                break;
            default:
                base.OnKeyDown(e);
                break;
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new GripAutomationPeer(this);

    /// <summary>UI オートメーションではつまみ（Thumb）として見せる。</summary>
    private sealed partial class GripAutomationPeer : FrameworkElementAutomationPeer
    {
        public GripAutomationPeer(ColumnResizeGrip owner)
            : base(owner)
        {
        }

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Thumb;

        protected override string GetClassNameCore() => nameof(ColumnResizeGrip);
    }
}
