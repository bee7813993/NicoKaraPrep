using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NicoKaraPrep.App.ViewModels;

namespace NicoKaraPrep.App.Views.FontSettings;

/// <summary>
/// フォント設定ビューのフォントフェースの表（文字種別フォント 6 行）。
/// フォント名とフェイス名は 1 つのボタンに出し、押すとフォントを選ぶ画面（見本つきの一覧）を開く。
/// </summary>
public sealed partial class FaceTable : UserControl
{
    /// <summary>表の最小の幅（種別 60 + フォント 120 + 数値 60 × 4 + 縁 2 32 + 間隔 4 × 6）。これより狭いと横にスクロールする。</summary>
    private const double TableMinWidth = 60 + 120 + 60 * 4 + 32 + 4 * 6;

    public FaceTable()
    {
        InitializeComponent();
    }

    /// <summary>編集欄の ViewModel（フォント設定ビューが 1 回だけ設定する）。</summary>
    public FontEditorViewModel? ViewModel { get; private set; }

    /// <summary>ViewModel を設定して表示を結び付ける。</summary>
    public void SetViewModel(FontEditorViewModel viewModel)
    {
        ViewModel = viewModel;
        Bindings.Update();
    }

    /// <summary>
    /// 横にスクロールできる表は中身の幅が決まらないため、表示できる幅（狭いときは最小の幅）に合わせる。
    /// これで 6 行ともフォントの列が同じ幅になる。
    /// </summary>
    private void OnTableScrollSizeChanged(object sender, SizeChangedEventArgs e) =>
        TableRoot.Width = Math.Max(TableMinWidth, e.NewSize.Width);

    /// <summary>
    /// 数値の欄（幅 60px）では、入力中に出る消去ボタン（×）が数字を隠すので、幅を 0 にして出さない
    /// （表示・非表示は入力欄の状態で切り替わるため、幅で消す）。
    /// </summary>
    private void OnNumberBoxLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is DependencyObject box && FindDescendant(box, "DeleteButton") is Button delete)
        {
            delete.Width = 0;
            delete.MinWidth = 0;
            delete.Padding = new Thickness(0);
            delete.Margin = new Thickness(0);
        }
    }

    private static FrameworkElement? FindDescendant(DependencyObject root, string name)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe && fe.Name == name) return fe;
            if (FindDescendant(child, name) is { } found) return found;
        }
        return null;
    }

    /// <summary>フォントのボタン: フォントを選ぶ画面を開き、選んだフォント（または継承）をその行に設定する。</summary>
    private async void OnFontButtonClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not FaceRowViewModel row || ViewModel?.Font is not { } font) return;

        var own = font.Detail.Faces[row.Index];
        var (inheritedFont, inheritedFace) = row.InheritedFont(font);
        var dialog = new FontPickerDialog(
            row.Label, own.FontName, own.FaceName, inheritedFont, inheritedFace,
            ViewModel.UsedFontNames(), japaneseOnly: !row.IsAlphanumeric)
        {
            XamlRoot = XamlRoot,
        };
        try
        {
            await dialog.ShowAsync();
        }
        catch (Exception)
        {
            // ほかの画面（ダイアログ）が開いているなど。何も変えない
            return;
        }

        // 画面を開いている間に別のフォント設定を選ぶことはできないが、念のため同じフォント設定のときだけ書き込む
        if (!ReferenceEquals(ViewModel.Font, font)) return;
        switch (dialog.Result.Kind)
        {
            case FontPickerResultKind.Select:
                row.CommitFont(dialog.Result.FontName, dialog.Result.FaceName);
                break;
            case FontPickerResultKind.Inherit:
                row.CommitFont("", "");
                break;
        }
    }
}
