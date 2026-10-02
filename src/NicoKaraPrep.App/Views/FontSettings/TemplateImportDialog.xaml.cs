using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NicoKaraPrep.App.Services;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.Views.FontSettings;

/// <summary>テンプレートの取り込み画面の 1 行。</summary>
public sealed partial class TemplateRow : ObservableObject
{
    public TemplateRow(N3FontSet font, bool nameExists)
    {
        Font = font;
        Name = font.Name.Length > 0 ? font.Name : "（名前なし）";
        FaceText = N3BrushPreview.DescribeFace(font);
        AfterBrush = N3BrushPreview.Create(font.Detail.Brushes[0]);
        BeforeBrush = N3BrushPreview.Create(font.Detail.Brushes[N3FontDetail.BeforeOffset]);
        Note = nameExists ? "同名あり（名前を変えて追加）" : "追加";
        ToolTip = string.Join("\n", new[]
        {
            Name,
            FaceText,
            N3BrushPreview.Describe("ワイプ後の文字", font.Detail.Brushes[0]),
            N3BrushPreview.Describe("ワイプ前の文字", font.Detail.Brushes[N3FontDetail.BeforeOffset]),
            font.ImportedFrom ?? "",
        });
    }

    public N3FontSet Font { get; }

    public string Name { get; }

    public string FaceText { get; }

    public Brush AfterBrush { get; }

    public Brush BeforeBrush { get; }

    public string Note { get; }

    public string ToolTip { get; }

    [ObservableProperty]
    private bool isSelected;

    public override string ToString() => $"{Name}（{Note}）";
}

/// <summary>
/// ニコカラメーカー3 のフォント設定テンプレートから取り込むものを選ぶ画面。選んだものはアプリ共通に追加する
/// （同じ名前があれば名前の末尾に 2, 3… を付ける。テンプレートとの連動はそのまま）。
/// </summary>
public sealed partial class TemplateImportDialog : ContentDialog
{
    private readonly List<TemplateRow> _rows;

    public TemplateImportDialog(IReadOnlyList<N3FontSet> templates, IReadOnlyCollection<string> existingNames, string folderText)
    {
        var names = new HashSet<string>(existingNames, StringComparer.Ordinal);
        _rows = templates.Select(t => new TemplateRow(t, names.Contains(t.Name))).ToList();
        foreach (var row in _rows) row.PropertyChanged += (_, _) => UpdateCount();

        InitializeComponent();
        Resources["ContentDialogMaxWidth"] = 900d;
        Resources["ContentDialogMaxHeight"] = 900d;
        FolderText.Text = folderText;
        ApplySearch();
    }

    /// <summary>検索に合う行。</summary>
    public ObservableCollection<TemplateRow> VisibleRows { get; } = new();

    /// <summary>選んだテンプレート（名前の順）。</summary>
    public IReadOnlyList<N3FontSet> Selected => _rows.Where(r => r.IsSelected).Select(r => r.Font).ToList();

    private void ApplySearch()
    {
        string q = SearchBox.Text.Trim();
        VisibleRows.Clear();
        foreach (var row in _rows)
        {
            if (q.Length == 0 || row.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || row.FaceText.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                VisibleRows.Add(row);
            }
        }
        UpdateCount();
    }

    private void UpdateCount()
    {
        int selected = _rows.Count(r => r.IsSelected);
        CountText.Text = $"テンプレート {_rows.Count} 件（表示 {VisibleRows.Count} 件）・選択 {selected} 件";
        IsPrimaryButtonEnabled = selected > 0;
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => ApplySearch();

    private void OnSelectAllClick(object sender, RoutedEventArgs e)
    {
        foreach (var row in VisibleRows) row.IsSelected = true;
    }

    private void OnSelectNoneClick(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows) row.IsSelected = false;
    }
}
