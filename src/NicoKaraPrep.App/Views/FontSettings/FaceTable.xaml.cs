using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NicoKaraPrep.App.ViewModels;

namespace NicoKaraPrep.App.Views.FontSettings;

/// <summary>フォント設定ビューのフォントフェースの表（文字種別フォント 6 行）。</summary>
public sealed partial class FaceTable : UserControl
{
    /// <summary>選択を外している最中か（そのときの SelectionChanged は確定として扱わない）。</summary>
    private bool _resetting;

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

    /// <summary>
    /// フォント名・フェイス名の欄（編集できる ComboBox）の文字を、行の値に合わせ続ける。
    /// 編集できる ComboBox の Text は、表示の準備ができる前に設定しても出ないため、x:Bind ではなくここで設定する。
    /// </summary>
    private void OnNameComboLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ComboBox combo || RowOf(sender) is not { } row) return;
        bool font = (string)combo.Tag == "font";
        string Current() => font ? row.FontNameText : row.FaceNameText;
        combo.Text = Current();
        if (_syncedCombos.Add(combo))
        {
            string property = font ? nameof(FaceRowViewModel.FontNameText) : nameof(FaceRowViewModel.FaceNameText);
            row.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == property && combo.Text != Current()) combo.Text = Current();
            };
        }
    }

    /// <summary>行の値の変化を受け取るようにした ComboBox（行も ComboBox もビューと同じだけ残るので、登録は 1 回だけ）。</summary>
    private readonly HashSet<ComboBox> _syncedCombos = new();

    private void OnFontSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_resetting || sender is not ComboBox combo || combo.SelectedItem is not string name || RowOf(sender) is not { } row) return;
        row.CommitFontName(name);
        ResetSelection(combo, () => row.FontNameText);
    }

    private void OnFontTextSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args)
    {
        if (RowOf(sender) is not { } row) return;
        args.Handled = true; // 一覧に無い名前（ほかの PC のフォントなど）もそのまま使う
        row.CommitFontName(args.Text);
        ResetSelection(sender, () => row.FontNameText);
    }

    private void OnFaceSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_resetting || sender is not ComboBox combo || combo.SelectedItem is not string name || RowOf(sender) is not { } row) return;
        row.CommitFaceName(name);
        ResetSelection(combo, () => row.FaceNameText);
    }

    private void OnFaceTextSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args)
    {
        if (RowOf(sender) is not { } row) return;
        args.Handled = true;
        row.CommitFaceName(args.Text);
        ResetSelection(sender, () => row.FaceNameText);
    }

    /// <summary>
    /// 選んだ項目の選択を外し、欄の文字をフォント設定の値に合わせる（継承なら空にして PlaceholderText を見せる）。
    /// 選択を残すと、元に戻す で値が変わったあとに同じ項目を選び直せないため。ComboBox の処理が終わってから行う。
    /// </summary>
    private void ResetSelection(ComboBox combo, Func<string> text)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _resetting = true;
            try
            {
                combo.SelectedIndex = -1;
                combo.Text = text();
            }
            finally
            {
                _resetting = false;
            }
        });
    }
}
