using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NicoKaraPrep.App.ViewModels;

namespace NicoKaraPrep.App.Views.FontSettings;

/// <summary>フォント設定ビューの配色 1 箇所の編集欄（塗りの種類・色・マーカー・画像）。</summary>
public sealed partial class BrushEditor : UserControl
{
    public BrushEditor()
    {
        InitializeComponent();
    }

    /// <summary>編集欄の ViewModel（フォント設定ビューが 1 回だけ設定する）。</summary>
    public BrushEditorViewModel? ViewModel { get; private set; }

    /// <summary>ViewModel を設定して表示を結び付ける。</summary>
    public void SetViewModel(BrushEditorViewModel viewModel)
    {
        ViewModel = viewModel;
        Bindings.Update();
    }

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
