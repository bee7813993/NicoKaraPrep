using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NicoKaraPrep.App.ViewModels;

namespace NicoKaraPrep.App.Views.FontSettings;

/// <summary>
/// フォント設定ビューのフォントフェースの表（文字種別フォント 6 行）。
/// フォント名・フェイス名は、一覧から選ぶことも一覧に無い名前（ほかの PC のフォントなど）を入れることもできるよう、
/// 候補の一覧つきの入力欄（AutoSuggestBox）にする。Enter・候補の選択・欄を離れたときに確定する。
/// </summary>
public sealed partial class FaceTable : UserControl
{
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

    private static FaceRowViewModel? RowOf(object sender) => (sender as FrameworkElement)?.DataContext as FaceRowViewModel;

    private static bool IsFontBox(AutoSuggestBox box) => (box.Tag as string) == "font";

    /// <summary>入力した文字を含む候補（先頭は「継承に戻す」）。</summary>
    private static List<string> Suggestions(FaceRowViewModel row, AutoSuggestBox box)
    {
        var choices = IsFontBox(box) ? row.FontChoices : row.FaceChoices;
        string q = box.Text.Trim();
        if (q.Length == 0) return choices.ToList();
        return choices.Where(c => c == FaceRowViewModel.InheritChoice || c.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>欄の文字を確定し、欄をフォント設定の値に合わせる（「継承に戻す」や空は継承＝空欄）。</summary>
    private static void Commit(AutoSuggestBox box, string? text)
    {
        if (RowOf(box) is not { } row) return;
        if (IsFontBox(box))
        {
            row.CommitFontName(text);
            if (box.Text != row.FontNameText) box.Text = row.FontNameText;
        }
        else
        {
            row.CommitFaceName(text);
            if (box.Text != row.FaceNameText) box.Text = row.FaceNameText;
        }
    }

    /// <summary>欄に入ったら候補をすべて出す。</summary>
    private void OnNameGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not AutoSuggestBox box || RowOf(box) is not { } row) return;
        box.ItemsSource = Suggestions(row, box);
        box.IsSuggestionListOpen = true;
    }

    private void OnNameLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is AutoSuggestBox box) Commit(box, box.Text);
    }

    /// <summary>入力に合わせて候補を絞る（候補を選んだときの文字の変化では絞らない）。</summary>
    private void OnNameTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput || RowOf(sender) is not { } row) return;
        sender.ItemsSource = Suggestions(row, sender);
    }

    /// <summary>Enter か候補の選択で確定する。</summary>
    private void OnNameQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) =>
        Commit(sender, args.ChosenSuggestion as string ?? args.QueryText);
}
