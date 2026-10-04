using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NicoKaraPrep.App.ViewModels;

namespace NicoKaraPrep.App.Views.FontSettings;

/// <summary>
/// 「まとめて消す...」で消す種類を選ぶ画面（アプリ共通・この曲専用・自作の配色パターン・最近使った色）。
/// 種類ごとに今の数を出し、何も無い種類は選べない。どれかを選ぶまで「消す」は押せない。選んだら <see cref="Choice"/> に入る。
/// </summary>
public sealed partial class FontClearDialog : ContentDialog
{
    public FontClearDialog(FontSettingsViewModel vm)
    {
        InitializeComponent();

        int folders = vm.CommonFolderCount;
        Setup(CommonBox, "アプリ共通のフォント設定とフォルダ分け", vm.CommonFontCount > 0 || folders > 0,
            folders > 0 ? $"{vm.CommonFontCount} 件・フォルダ {folders} 個" : $"{vm.CommonFontCount} 件");
        Setup(SongBox, "この曲専用のフォント設定", vm.SongFontCount > 0, $"{vm.SongFontCount} 件");
        Setup(PatternsBox, "自作の配色パターン", vm.UserPatternCount > 0, $"{vm.UserPatternCount} 件。新しいフォント設定に使うパターンは標準に戻します");
        Setup(RecentBox, "最近使った色", vm.RecentColorCount > 0, $"{vm.RecentColorCount} 色");

        PrimaryButtonClick += (_, _) => Choice = Current();
    }

    /// <summary>選んだ種類（キャンセルなら null）。</summary>
    public FontClearChoice? Choice { get; private set; }

    private static void Setup(CheckBox box, string label, bool any, string count)
    {
        box.Content = any ? $"{label}（{count}）" : $"{label}（ありません）";
        box.IsEnabled = any;
        box.IsChecked = false;
    }

    private FontClearChoice Current() => new(
        CommonBox.IsChecked == true,
        SongBox.IsChecked == true,
        PatternsBox.IsChecked == true,
        RecentBox.IsChecked == true);

    private void OnChoiceClick(object sender, RoutedEventArgs e) => IsPrimaryButtonEnabled = Current().Any;
}
