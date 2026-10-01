using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NicoKaraPrep.App.ViewModels;

namespace NicoKaraPrep.App.Views.FontSettings;

/// <summary>
/// 配色の編集欄の横に出す「使っている色」の一覧（役割ごとの色の見本と、使っている画像）。押すと、編集中の箇所をその色・画像にする。
/// 一覧の中身は <see cref="BrushEditorViewModel.SetPalette"/> で入れる。
/// </summary>
public sealed partial class UsedColorsPanel : UserControl
{
    public UsedColorsPanel()
    {
        InitializeComponent();
    }

    /// <summary>配色 1 箇所の編集欄の ViewModel（配色の編集欄が 1 回だけ設定する）。</summary>
    public BrushEditorViewModel? ViewModel { get; private set; }

    /// <summary>ViewModel を設定して表示を結び付ける。</summary>
    public void SetViewModel(BrushEditorViewModel viewModel)
    {
        ViewModel = viewModel;
        Bindings.Update();
    }

    private void OnColorClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is PaletteColorItem item) ViewModel?.ApplyPaletteColor(item.Color);
    }

    private void OnImageClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is PaletteImageItem item) ViewModel?.ApplyPaletteImage(item.Image);
    }
}
