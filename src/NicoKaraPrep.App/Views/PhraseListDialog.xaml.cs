using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
/// 定型文（絵文字挿入ビューで入れる決まった文字列）の編集画面。OK で設定の一覧を入れ替えて保存する（空の行は捨てる）。
/// </summary>
public sealed partial class PhraseListDialog : ContentDialog
{
    private readonly AppSettings _settings;

    public ObservableCollection<PhraseRow> Rows { get; } = new();

    public PhraseListDialog(AppSettings settings)
    {
        _settings = settings;
        foreach (string p in settings.InsertPhrases ?? new List<string>()) Rows.Add(new PhraseRow { Text = p });
        if (Rows.Count == 0) Rows.Add(new PhraseRow());
        InitializeComponent();
        UpdateKeyLabels();
        PrimaryButtonClick += (_, _) => Apply();
    }

    private void UpdateKeyLabels()
    {
        for (int i = 0; i < Rows.Count; i++) Rows[i].KeyLabel = i < 9 ? (i + 1).ToString() : i == 9 ? "0" : "";
    }

    private void Apply()
    {
        _settings.InsertPhrases = Rows.Select(r => r.Text.Trim()).Where(t => t.Length > 0).ToList();
        _settings.Save();
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        var row = new PhraseRow();
        Rows.Add(row);
        UpdateKeyLabels();
        RowList.ScrollIntoView(row);
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
