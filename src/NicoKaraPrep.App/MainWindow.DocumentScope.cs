using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NicoKaraPrep.App.ViewModels;

namespace NicoKaraPrep.App;

/// <summary>
/// 新規作成・ほかの曲を開くときの確認: 保存していない変更を破棄するかと一緒に、新しい文書に出なくなるもの（この曲だけのもの）と
/// そのまま使えるもの（アプリ共通）を並べて見せる（どの設定が曲ごとで、どれがアプリ共通なのか分かるように）。
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>確認の画面の中身（問いかけ・この曲だけのもの・アプリ共通のもの）。</summary>
    private static UIElement BuildScopeContent(string question, DocumentScopeSummary scope)
    {
        var secondary = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        var panel = new StackPanel { Spacing = 6, MaxWidth = 520 };
        panel.Children.Add(new TextBlock { Text = question, TextWrapping = TextWrapping.Wrap });

        void Section(string title, IEnumerable<string> items, string note)
        {
            panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock
            {
                Text = string.Join("\n", items.Select(i => "・" + i)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8, 0, 0, 0),
            });
            panel.Children.Add(new TextBlock { Text = note, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = secondary, Margin = new Thickness(8, 0, 0, 0) });
        }

        Section("新しい文書には出なくなるもの（この曲だけのもの）", scope.SongItems,
            scope.HasFile
                ? "保存した分は、歌詞ファイルと .tttproj（歌詞ファイルの隣）に残っていて、この曲を開き直すと戻ります。保存していない変更は消えます。"
                : "この歌詞はまだファイルに保存していないので、破棄すると戻せません（名前を付けて保存すると、歌詞ファイルと .tttproj に保存されます）。");
        Section("そのまま使えるもの（アプリ共通）", scope.CommonItems,
            "設定（settings.json）に保存されていて、どの曲でも使えます。曲ごとに変えたいフォント設定は、フォント設定ビュー（F3）で「この曲専用」に写せます。");
        return new ScrollViewer { Content = panel, MaxHeight = 460, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    /// <summary>保存していない変更があれば、破棄して新規作成するか尋ねる（変更が無ければ尋ねずに true）。</summary>
    private async Task<bool> ConfirmNewAsync(DocumentScopeSummary scope)
    {
        if (!ViewModel.HasUnsavedChanges) return true;
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "新規作成",
            Content = BuildScopeContent("保存されていない変更があります。破棄して新規作成（ファイルを閉じる）しますか？", scope),
            PrimaryButtonText = "破棄して新規作成",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>
    /// 歌詞ファイルを開く（メニューの「開く...」・最近使用したファイル・ドロップ）。保存していない変更があれば、破棄して開くか尋ねる。
    /// 開いたら true。
    /// </summary>
    private async Task<bool> OpenLyricsFileAsync(string path)
    {
        if (ViewModel.HasUnsavedChanges && !await ConfirmDiscardAsync(Path.GetFileName(path))) return false;
        TryRun(() => ViewModel.OpenFile(path));
        AfterDocumentLoaded();
        if (InsertViewActive) RefreshInsertView(0);
        return true;
    }
}
