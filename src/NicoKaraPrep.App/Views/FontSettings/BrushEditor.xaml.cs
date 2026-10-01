using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NicoKaraPrep.App.ViewModels;

namespace NicoKaraPrep.App.Views.FontSettings;

/// <summary>フォント設定ビューの配色 1 箇所の編集欄（塗りの種類・色・マーカー・画像）。</summary>
public sealed partial class BrushEditor : UserControl
{
    public BrushEditor()
    {
        InitializeComponent();

        // 色の四角・つまみのドラッグの始めと終わりを知らせる（ColorPicker の中の部品が押下を処理済みにするので、処理済みのものも受け取る）
        Picker.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => ColorDragChanged?.Invoke(this, true)), handledEventsToo: true);
        var end = new PointerEventHandler((_, _) => ColorDragChanged?.Invoke(this, false));
        Picker.AddHandler(PointerReleasedEvent, end, handledEventsToo: true);
        Picker.AddHandler(PointerCaptureLostEvent, end, handledEventsToo: true);
        Picker.AddHandler(PointerCanceledEvent, end, handledEventsToo: true);

        // ドラッグ中の途中の色は最近使った色に入れず、離したときの色を入れる
        ColorDragChanged += (_, dragging) => ViewModel?.SetDragging(dragging);
    }

    /// <summary>ColorPicker の上でドラッグを始めた（true）・終えた（false）。</summary>
    public event EventHandler<bool>? ColorDragChanged;

    /// <summary>編集欄の ViewModel（フォント設定ビューが 1 回だけ設定する）。</summary>
    public BrushEditorViewModel? ViewModel { get; private set; }

    /// <summary>ViewModel を設定して表示を結び付ける。</summary>
    public void SetViewModel(BrushEditorViewModel viewModel)
    {
        ViewModel = viewModel;
        Bindings.Update();
        PickerPalette.SetViewModel(viewModel);
        BitmapPalette.SetViewModel(viewModel);
    }

    /// <summary>
    /// 外側の ScrollViewer に位置を保たせる欄（スクロール アンカーの候補。色の四角・16 進の欄など、操作中に画面の上で動いてほしくないもの）。
    /// 塗りの種類のラジオボタンとマーカーの一覧の項目は、自動で候補になる。
    /// </summary>
    public IEnumerable<UIElement> AnchorCandidates => new UIElement[] { StopsPanel, BitmapPanel, Picker, HexRow };

    private void OnClearColorClick(object sender, RoutedEventArgs e) => ViewModel?.ClearColor();

    private void OnAddStopClick(object sender, RoutedEventArgs e) => ViewModel?.AddStop();

    private void OnDistributeStopsClick(object sender, RoutedEventArgs e) => ViewModel?.DistributeStops();

    private void OnDeleteStopClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is GradientStopViewModel stop) ViewModel?.DeleteStop(stop);
    }

    /// <summary>画像ファイルを選ぶ。</summary>
    private async void OnBrowseBitmapClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || App.MainWindow is not { } window) return;
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
            foreach (string ext in new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" }) picker.FileTypeFilter.Add(ext);
            var file = await picker.PickSingleFileAsync();
            if (file is not null && !string.IsNullOrEmpty(file.Path)) ViewModel.BitmapPath = file.Path;
        }
        catch (Exception ex)
        {
            // 一時フォルダの中など、選んでも受け取れないファイルがある（メイン画面のファイル選択と同じ）
            ViewModel.BitmapNote =$"選んだファイルを受け取れませんでした（{ex.Message}）。パスを直接入力してください";
        }
    }
}
