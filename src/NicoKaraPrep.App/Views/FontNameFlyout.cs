using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using NicoKaraPrep.App.ViewModels;

namespace NicoKaraPrep.App.Views;

/// <summary>
/// フォント設定の名前を選ぶ一覧（絵文字のリストの「文字」・定型文の編集画面の「フォント設定」の ▼）。
/// この曲専用 → アプリ共通のフォルダごとに並べ（<see cref="MainViewModel.BuildFontPickGroups"/>）、名前で絞り込める。
/// 項目とまとまりの見出しの見た目は、呼び出す画面の XAML の DataTemplate を渡す（x:Bind はその画面でコンパイルされるため）。
/// </summary>
internal static class FontNameFlyout
{
    /// <summary><paramref name="target"/> の下に一覧を出し、押した名前を <paramref name="picked"/> へ渡して閉じる。</summary>
    public static void Show(
        FrameworkElement target,
        DataTemplate itemTemplate,
        DataTemplate groupTemplate,
        Func<string?, List<FontPickGroup>>? groups,
        Action<string> picked)
    {
        var search = new TextBox { PlaceholderText = "名前で絞り込み" };
        var empty = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        };
        var source = new CollectionViewSource { IsSourceGrouped = true };
        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.None,
            IsItemClickEnabled = true,
            MaxHeight = 420,
            ItemTemplate = itemTemplate,
        };
        list.GroupStyle.Add(new GroupStyle { HeaderTemplate = groupTemplate });

        void Fill()
        {
            var result = groups?.Invoke(search.Text) ?? new List<FontPickGroup>();
            source.Source = result;
            list.ItemsSource = source.View;
            bool none = result.Count == 0;
            empty.Text = search.Text.Trim().Length > 0
                ? "当てはまるフォント設定がありません"
                : "フォント設定がありません（フォント設定ビュー（F3）で作れます）";
            empty.Visibility = none ? Visibility.Visible : Visibility.Collapsed;
            list.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
        }
        Fill();

        var panel = new StackPanel { Spacing = 8, Width = 280 };
        panel.Children.Add(search);
        panel.Children.Add(empty);
        panel.Children.Add(list);
        var flyout = new Flyout { Content = panel, Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };
        search.TextChanged += (_, _) => Fill();
        list.ItemClick += (_, args) =>
        {
            if (args.ClickedItem is not FontPickItem item) return;
            picked(item.Name);
            flyout.Hide();
        };
        flyout.Opened += (_, _) => search.Focus(FocusState.Programmatic);
        flyout.ShowAt(target);
    }
}
