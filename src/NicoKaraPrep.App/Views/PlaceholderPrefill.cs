using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace NicoKaraPrep.App.Views;

/// <summary>
/// 空欄に薄字で出している今の値（自動・継承の値）を、欄に入ったときに文字として入れて選ぶ（TextBox・NumberBox）。
/// 薄字は入力を始めると消え、選んだりコピーしたりもできないので、今の値から直せるようにする（ユーザーの指摘）。
/// 変えずに欄を離れたら空欄に戻す（手で指定したことにしない）。値を確定する処理は <see cref="EffectiveText"/> で、
/// 入れた今の値のままなら空欄として扱う。NumberBox は Enter で確定する前に空へ戻し、値（NaN = 継承）を変えない。
/// </summary>
public static class PlaceholderPrefill
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(PlaceholderPrefill), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject o) => (bool)o.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject o, bool value) => o.SetValue(IsEnabledProperty, value);

    /// <summary>欄に入れた今の値（入れていなければ null）。</summary>
    private static readonly DependencyProperty PrefillProperty = DependencyProperty.RegisterAttached(
        "Prefill", typeof(string), typeof(PlaceholderPrefill), new PropertyMetadata(null));

    private static string? GetPrefill(DependencyObject o) => (string?)o.GetValue(PrefillProperty);

    private static void SetPrefill(DependencyObject o, string? value) => o.SetValue(PrefillProperty, value);

    /// <summary>
    /// 薄字から入れる値を取り出す（「自動: 」は外す）。「（自動）」のような説明・「--:--:--」・
    /// 複数の値をまとめたもの（「→」を含む）は値ではないので null。
    /// </summary>
    public static string? ValueOf(string? placeholder)
    {
        string t = (placeholder ?? "").Trim();
        const string auto = "自動: ";
        if (t.StartsWith(auto, StringComparison.Ordinal)) t = t[auto.Length..].Trim();
        if (t.Length == 0 || t.StartsWith('（') || t.Contains("--") || t.Contains('→') || t.Contains('…')) return null;
        return t;
    }

    /// <summary>確定に使う文字（入れた今の値のままなら空欄）。</summary>
    public static string EffectiveText(TextBox box) =>
        GetPrefill(box) is string p && box.Text.Trim() == p ? "" : box.Text;

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Control control || e.NewValue is not true) return;
        control.GotFocus += OnGotFocus;
        control.LosingFocus += OnLosingFocus;
        if (control is NumberBox number) number.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(OnNumberPreviewKeyDown), handledEventsToo: true);
    }

    private static void OnGotFocus(object sender, RoutedEventArgs e)
    {
        switch (sender)
        {
            case TextBox box when box.Text.Length == 0 && ValueOf(box.PlaceholderText) is string v:
                box.Text = v;
                SetPrefill(box, v);
                SelectAllLater(box);
                break;
            case NumberBox number when double.IsNaN(number.Value) && InnerBox(number) is { Text.Length: 0 } inner && ValueOf(number.PlaceholderText) is string v:
                inner.Text = v;
                SetPrefill(number, v);
                SelectAllLater(inner);
                break;
        }
    }

    private static void OnLosingFocus(UIElement sender, LosingFocusEventArgs args)
    {
        // 欄の中の部品へ移るだけなら（NumberBox の中の入力欄など）そのまま
        if (args.NewFocusedElement is DependencyObject next && IsInside(next, sender)) return;
        if (GetPrefill(sender) is not string p) return;
        SetPrefill(sender, null);
        if (sender is TextBox box && box.Text.Trim() == p) box.Text = "";
        else if (sender is NumberBox number && InnerBox(number) is { } inner && inner.Text.Trim() == p) inner.Text = "";
    }

    /// <summary>NumberBox の Enter: 入れた今の値のままなら、確定する前に空へ戻す（継承のまま）。</summary>
    private static void OnNumberPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter || sender is not NumberBox number || GetPrefill(number) is not string p) return;
        if (InnerBox(number) is { } inner && inner.Text.Trim() == p) inner.Text = "";
        SetPrefill(number, null);
    }

    private static void SelectAllLater(TextBox box) =>
        // 欄を押して入ったときは、押したあとにカーソルが置かれて選択が外れるので、そのあとで選ぶ。
        // 部品のメソッド（box.SelectAll）をそのまま渡すと、WinRT へ渡すときに InvalidCastException で落ちるのでラムダで包む
        box.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => box.SelectAll());

    private static TextBox? InnerBox(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBox t) return t;
            if (InnerBox(child) is { } found) return found;
        }
        return null;
    }

    private static bool IsInside(DependencyObject element, DependencyObject ancestor)
    {
        for (var e = element; e is not null; e = VisualTreeHelper.GetParent(e))
        {
            if (ReferenceEquals(e, ancestor)) return true;
        }
        return false;
    }
}
