using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NicoKaraPrep.App.ViewModels;
using NicoKaraPrep.Core.Project;

namespace NicoKaraPrep.App.Views;

/// <summary>定型文の編集画面の 1 行。</summary>
public partial class PhraseRow : ObservableObject
{
    /// <summary>番号のキー（上から 1–9・0。11 件目からは空）。</summary>
    [ObservableProperty]
    private string keyLabel = "";

    [ObservableProperty]
    private string text = "";
}

/// <summary>
/// 定型文（絵文字挿入ビューで入れる決まった文字列）と、定型文に使うフォント設定の編集画面。OK で設定の一覧を入れ替えて保存する（空の行は捨てる）。
/// </summary>
public sealed partial class PhraseListDialog : ContentDialog
{
    private readonly AppSettings _settings;
    private readonly Func<string?, List<FontPickGroup>>? _fontGroups;
    private readonly HashSet<string> _fontNames;

    public ObservableCollection<PhraseRow> Rows { get; } = new();

    /// <param name="fontGroups">フォント設定の ▼ で出す名前の一覧（引数は名前の絞り込み）。</param>
    public PhraseListDialog(AppSettings settings, Func<string?, List<FontPickGroup>>? fontGroups = null)
    {
        _settings = settings;
        _fontGroups = fontGroups;
        _fontNames = new HashSet<string>((fontGroups?.Invoke(null) ?? new List<FontPickGroup>()).SelectMany(g => g).Select(i => i.Name), StringComparer.Ordinal);
        foreach (string p in settings.InsertPhrases ?? new List<string>()) Rows.Add(new PhraseRow { Text = p });
        if (Rows.Count == 0) Rows.Add(new PhraseRow());
        InitializeComponent();
        FontBox.Text = settings.InsertPhraseFontSetName ?? "";
        UpdateKeyLabels();
        UpdateFontWarning();
        PrimaryButtonClick += (_, _) => Apply();
    }

    private void OnPickFontClick(object sender, RoutedEventArgs e) =>
        FontNameFlyout.Show(
            PickFontButton,
            (DataTemplate)RootPanel.Resources["FontNameItemTemplate"],
            (DataTemplate)RootPanel.Resources["FontNameGroupTemplate"],
            _fontGroups,
            name => FontBox.Text = name);

    private void OnFontBoxChanged(object sender, TextChangedEventArgs e) => UpdateFontWarning();

    /// <summary>入力したフォント設定が無ければ注意を出す（書き出しでは既定のフォント設定になる）。</summary>
    private void UpdateFontWarning()
    {
        string name = FontBox.Text.Trim();
        bool missing = name.Length > 0 && _fontGroups is not null && !_fontNames.Contains(name);
        FontWarningText.Text = missing ? $"「{name}」というフォント設定はまだありません（このままでも使えますが、作るまでは書き出しで既定のフォント設定になります）" : "";
        FontWarningText.Visibility = missing ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateKeyLabels()
    {
        for (int i = 0; i < Rows.Count; i++) Rows[i].KeyLabel = i < 9 ? (i + 1).ToString() : i == 9 ? "0" : "";
    }

    private void Apply()
    {
        _settings.InsertPhrases = Rows.Select(r => r.Text.Trim()).Where(t => t.Length > 0).ToList();
        _settings.InsertPhraseFontSetName = FontBox.Text.Trim();
        _settings.Save();
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        var row = new PhraseRow();
        Rows.Add(row);
        UpdateKeyLabels();
        RowList.ScrollIntoView(row);
    }

    /// <summary>既定の定型文（前奏・間奏・後奏）のうち、まだ無いものを末尾に足す（空の行があればそこへ入れる）。</summary>
    private void OnAddDefaultsClick(object sender, RoutedEventArgs e)
    {
        var have = new HashSet<string>(Rows.Select(r => r.Text.Trim()));
        PhraseRow? last = null;
        foreach (string p in AppSettings.DefaultInsertPhrases.Where(p => !have.Contains(p)))
        {
            var row = Rows.FirstOrDefault(r => r.Text.Trim().Length == 0);
            if (row is null)
            {
                row = new PhraseRow();
                Rows.Add(row);
            }
            row.Text = p;
            last = row;
        }
        UpdateKeyLabels();
        if (last is not null) RowList.ScrollIntoView(last);
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not PhraseRow row) return;
        Rows.Remove(row);
        if (Rows.Count == 0) Rows.Add(new PhraseRow());
        UpdateKeyLabels();
    }

    private void OnMoveUpClick(object sender, RoutedEventArgs e) => Move(sender, -1);

    private void OnMoveDownClick(object sender, RoutedEventArgs e) => Move(sender, +1);

    private void Move(object sender, int delta)
    {
        if ((sender as FrameworkElement)?.Tag is not PhraseRow row) return;
        int i = Rows.IndexOf(row);
        int to = i + delta;
        if (i < 0 || to < 0 || to >= Rows.Count) return;
        Rows.Move(i, to);
        UpdateKeyLabels();
    }
}
