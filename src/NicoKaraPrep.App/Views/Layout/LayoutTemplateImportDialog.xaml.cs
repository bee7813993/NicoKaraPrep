using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NicoKaraPrep.App.ViewModels;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.Views.Layout;

/// <summary>レイアウト設定テンプレートの取り込み画面の 1 行。</summary>
public sealed partial class LayoutTemplateRow : ObservableObject
{
    public LayoutTemplateRow(N3Layout layout, bool nameExists)
    {
        Layout = layout;
        var s = layout.ToSettings(0);
        Name = layout.Name.Length > 0 ? layout.Name : "（名前なし）";
        Summary = $"{LayoutTexts.Summary(s)}（{string.Join("・", s.HorizontalAlignments.Select(LayoutTexts.Horizontal))}）";
        Note = nameExists ? "同名あり（名前を変えて追加）" : "追加";
        ToolTip = $"{Name}\n" +
                  $"上下配置: {LayoutTexts.Vertical(s.VerticalAlignment)}（{LayoutTexts.VerticalMarginLabel(s.VerticalAlignment).Replace(" px", "")} {s.VerticalMarginPx:0.#} px）・左右余白 {s.HorizontalMarginPx:0.#} px・行間 {s.LineSpacePx:0.#} px\n" +
                  $"行ごとの左右配置（上の行から）: {string.Join("・", s.HorizontalAlignments.Select(LayoutTexts.Horizontal))}\n" +
                  $"スマート水平配置: {LayoutTexts.SmartHorizonNames[Math.Clamp(s.SmartHorizon, 0, 2)]}・ルビ配置: {LayoutTexts.RubyAlignmentNames[Math.Clamp(s.RubyAlignment, 0, 2)]}";
    }

    /// <summary>テンプレートから読んだレイアウト設定。</summary>
    public N3Layout Layout { get; }

    public string Name { get; }

    /// <summary>配置の短い説明（「下寄せ・2 行（左寄せ・右寄せ）」）。</summary>
    public string Summary { get; }

    public string Note { get; }

    public string ToolTip { get; }

    [ObservableProperty]
    private bool isSelected;

    /// <summary>読み上げ・UI オートメーションでの名前（一覧の行の名前になる）。</summary>
    public override string ToString() => $"{Name}（{Note}）";
}

/// <summary>
/// ニコカラメーカー3 のレイアウト設定テンプレートから取り込むものを選ぶ画面。選んだものは NicoKaraPrep のレイアウト設定（アプリ共通）に足す
/// （同じ名前があれば名前の末尾に 2, 3… を付ける。テンプレートとの連動は引き継がない）。フォント設定ビューのテンプレートの取り込みと同じ形。
/// </summary>
public sealed partial class LayoutTemplateImportDialog : ContentDialog
{
    private readonly List<LayoutTemplateRow> _rows;

    public LayoutTemplateImportDialog(IReadOnlyList<N3Layout> templates, IReadOnlyCollection<string> existingNames, string folderText)
    {
        var names = new HashSet<string>(existingNames, StringComparer.Ordinal);
        _rows = templates.Select(t => new LayoutTemplateRow(t, names.Contains(t.Name))).ToList();
        foreach (var row in _rows) row.PropertyChanged += (_, _) => UpdateCount();

        InitializeComponent();
        Resources["ContentDialogMaxWidth"] = 900d;
        Resources["ContentDialogMaxHeight"] = 900d;
        FolderText.Text = folderText;
        ApplySearch();
    }

    /// <summary>検索に合う行。</summary>
    public ObservableCollection<LayoutTemplateRow> VisibleRows { get; } = new();

    /// <summary>選んだテンプレート（名前の順）。</summary>
    public IReadOnlyList<N3Layout> Selected => _rows.Where(r => r.IsSelected).Select(r => r.Layout).ToList();

    private void ApplySearch()
    {
        string q = SearchBox.Text.Trim();
        VisibleRows.Clear();
        foreach (var row in _rows)
        {
            if (q.Length == 0 || row.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || row.Summary.Contains(q, StringComparison.OrdinalIgnoreCase))
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
