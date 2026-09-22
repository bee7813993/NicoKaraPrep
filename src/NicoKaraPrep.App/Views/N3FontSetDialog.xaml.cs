using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NicoKaraPrep.App.Services;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;
using NicoKaraPrep.Core.Project;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace NicoKaraPrep.App.Views;

/// <summary>フォント設定編集ダイアログの 1 行分。</summary>
public partial class N3FontRow : ObservableObject
{
    public IReadOnlyList<string> FontChoices { get; set; } = Array.Empty<string>();

    [ObservableProperty] private string name = "";
    [ObservableProperty] private string fontFamily = "メイリオ";
    [ObservableProperty] private string fontFace = "Bold";
    [ObservableProperty] private string sizeText = "80";
    [ObservableProperty] private string edgeText = "8";
    [ObservableProperty] private string textColorAfter = "FFFFFF";
    [ObservableProperty] private string edgeColorAfter = "000000";
    [ObservableProperty] private string textColorBefore = "4DA3FF";
    [ObservableProperty] private string edgeColorBefore = "FFFFFF";

    // 詳細（下のパネルで編集）
    public bool UseEdge2 { get; set; }
    public double Edge2Px { get; set; } = 4;
    public string Edge2ColorAfter { get; set; } = "";
    public string Edge2ColorBefore { get; set; } = "";
    public string DecorColorAfter { get; set; } = "";
    public string DecorColorBefore { get; set; } = "";
    public int DecorKind { get; set; }
    public double DecorSizePx { get; set; } = 10;
    public int BlurLevel { get; set; } = 2;
    public double RubySizePx { get; set; }
    public double RubyEdgePx { get; set; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Name);

    public static N3FontRow From(N3FontSet f, IReadOnlyList<string> fonts) => new()
    {
        FontChoices = fonts,
        Name = f.Name,
        FontFamily = f.FontFamily,
        FontFace = f.FontFace,
        SizeText = Num(f.SizePx),
        EdgeText = Num(f.EdgePx),
        TextColorAfter = f.TextColorAfter,
        EdgeColorAfter = f.EdgeColorAfter,
        TextColorBefore = f.TextColorBefore,
        EdgeColorBefore = f.EdgeColorBefore,
        UseEdge2 = f.UseEdge2,
        Edge2Px = f.Edge2Px,
        Edge2ColorAfter = f.Edge2ColorAfter,
        Edge2ColorBefore = f.Edge2ColorBefore,
        DecorColorAfter = f.DecorColorAfter,
        DecorColorBefore = f.DecorColorBefore,
        DecorKind = f.DecorKind,
        DecorSizePx = f.DecorSizePx,
        BlurLevel = f.BlurLevel,
        RubySizePx = f.RubySizePx,
        RubyEdgePx = f.RubyEdgePx,
    };

    public N3FontSet ToModel() => new()
    {
        Name = Name.Trim(),
        FontFamily = FontFamily.Trim(),
        FontFace = FontFace.Trim(),
        SizePx = Parse(SizeText, 80),
        EdgePx = Parse(EdgeText, 0),
        TextColorAfter = Color(TextColorAfter),
        EdgeColorAfter = Color(EdgeColorAfter),
        TextColorBefore = Color(TextColorBefore),
        EdgeColorBefore = Color(EdgeColorBefore),
        UseEdge2 = UseEdge2,
        Edge2Px = Edge2Px,
        Edge2ColorAfter = Color(Edge2ColorAfter),
        Edge2ColorBefore = Color(Edge2ColorBefore),
        DecorColorAfter = Color(DecorColorAfter),
        DecorColorBefore = Color(DecorColorBefore),
        DecorKind = DecorKind,
        DecorSizePx = DecorSizePx,
        BlurLevel = BlurLevel,
        RubySizePx = RubySizePx,
        RubyEdgePx = RubyEdgePx,
    };

    public N3FontRow Clone() => From(ToModel(), FontChoices);

    private static string Num(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    private static double Parse(string s, double fallback) =>
        double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v >= 0 ? v : fallback;

    /// <summary>16 進 6 桁として妥当なら大文字に正規化、それ以外は空（ベースの値を維持）。</summary>
    private static string Color(string s) =>
        N3FontSet.IsValidWeb16(s) ? s.Trim().TrimStart('#').ToUpperInvariant() : "";
}

public sealed partial class N3FontSetDialog : ContentDialog
{
    private readonly AppSettings _settings;
    private readonly List<string> _fonts;
    private N3FontRow? _detailRow;
    private bool _detailLoading;

    public ObservableCollection<N3FontRow> Rows { get; } = new();

    public N3FontSetDialog(AppSettings settings)
    {
        _settings = settings;
        _fonts = DirectWriteTextMeasurer.GetSystemFontFamilies().OrderBy(f => f).ToList();
        foreach (var f in settings.N3FontSets) Rows.Add(N3FontRow.From(f, _fonts));

        InitializeComponent();
        Resources["ContentDialogMaxWidth"] = 1200d;
        Resources["ContentDialogMaxHeight"] = 950d;
        PrimaryButtonClick += (_, _) => Apply();
    }

    private void Apply()
    {
        _settings.N3FontSets = Rows.Where(r => !r.IsEmpty).Select(r => r.ToModel()).ToList();
        _settings.Save();
    }

    // ------------------------------------------------------------ 行操作

    private void OnAddRowClick(object sender, RoutedEventArgs e)
    {
        var row = new N3FontRow { FontChoices = _fonts, FontFamily = _settings.FontFamily };
        Rows.Add(row);
        RowList.SelectedItem = row;
    }

    private void OnAddFromSettingsClick(object sender, RoutedEventArgs e)
    {
        var row = N3FontRow.From(new N3FontSet
        {
            Name = Rows.Any(r => r.Name == "標準") ? $"標準{Rows.Count + 1}" : "標準",
            FontFamily = _settings.FontFamily,
            FontFace = _settings.FontBold ? "Bold" : "",
            SizePx = _settings.FontSizePx,
            EdgePx = _settings.EdgeSizePx,
        }, _fonts);
        Rows.Add(row);
        RowList.SelectedItem = row;
    }

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        picker.FileTypeFilter.Add(".n3proj");
        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null) return;
        try
        {
            int added = 0;
            foreach (var f in N3ProjFormat.ReadFontSets(file.Path))
            {
                if (f.Name.Length == 0) continue;
                var existing = Rows.FirstOrDefault(r => r.Name == f.Name);
                if (existing is not null) Rows.Remove(existing);
                Rows.Add(N3FontRow.From(f, _fonts));
                added++;
            }
            DetailCaption.Text = $"{Path.GetFileName(file.Path)} から {added} 件を取り込みました";
        }
        catch (Exception ex)
        {
            DetailCaption.Text = $"⚠ 取り込めませんでした: {ex.Message}";
        }
    }

    private void OnDuplicateRowClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not N3FontRow row) return;
        int i = Rows.IndexOf(row);
        var clone = row.Clone();
        clone.Name = row.Name + "2";
        Rows.Insert(i + 1, clone);
    }

    private void OnMoveUpClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not N3FontRow row) return;
        int i = Rows.IndexOf(row);
        if (i > 0) Rows.Move(i, i - 1);
    }

    private void OnMoveDownClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not N3FontRow row) return;
        int i = Rows.IndexOf(row);
        if (i >= 0 && i < Rows.Count - 1) Rows.Move(i, i + 1);
    }

    private void OnDeleteRowClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not N3FontRow row) return;
        Rows.Remove(row);
    }

    // ------------------------------------------------------------ 詳細パネル

    private void OnRowSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _detailRow = RowList.SelectedItem as N3FontRow;
        _detailLoading = true;
        try
        {
            bool has = _detailRow is not null;
            DetailPanel.IsHitTestVisible = has;
            DetailPanel.Opacity = has ? 1 : 0.5;
            if (_detailRow is not { } r)
            {
                DetailCaption.Text = "行を選択すると詳細（縁 2・文字飾り・ルビ）を編集できます";
                return;
            }
            DetailCaption.Text = $"「{r.Name}」の詳細";
            UseEdge2Check.IsChecked = r.UseEdge2;
            Edge2Box.Value = r.Edge2Px;
            Edge2AfterBox.Text = r.Edge2ColorAfter;
            Edge2BeforeBox.Text = r.Edge2ColorBefore;
            RubySizeBox.Value = r.RubySizePx;
            RubyEdgeBox.Value = r.RubyEdgePx;
            DecorKindBox.SelectedIndex = Math.Clamp(r.DecorKind, 0, 2);
            DecorSizeBox.Value = r.DecorSizePx;
            DecorAfterBox.Text = r.DecorColorAfter;
            DecorBeforeBox.Text = r.DecorColorBefore;
            BlurLevelBox.SelectedIndex = Math.Clamp(r.BlurLevel, 0, 2);
        }
        finally
        {
            _detailLoading = false;
        }
    }

    private void StoreDetail()
    {
        if (_detailLoading || _detailRow is not { } r) return;
        r.UseEdge2 = UseEdge2Check.IsChecked == true;
        r.Edge2Px = double.IsNaN(Edge2Box.Value) ? r.Edge2Px : Edge2Box.Value;
        r.Edge2ColorAfter = Edge2AfterBox.Text;
        r.Edge2ColorBefore = Edge2BeforeBox.Text;
        r.RubySizePx = double.IsNaN(RubySizeBox.Value) ? 0 : RubySizeBox.Value;
        r.RubyEdgePx = double.IsNaN(RubyEdgeBox.Value) ? 0 : RubyEdgeBox.Value;
        r.DecorKind = Math.Max(0, DecorKindBox.SelectedIndex);
        r.DecorSizePx = double.IsNaN(DecorSizeBox.Value) ? r.DecorSizePx : DecorSizeBox.Value;
        r.DecorColorAfter = DecorAfterBox.Text;
        r.DecorColorBefore = DecorBeforeBox.Text;
        r.BlurLevel = Math.Max(0, BlurLevelBox.SelectedIndex);
    }

    private void OnDetailChanged(object sender, RoutedEventArgs e) => StoreDetail();

    private void OnDetailValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => StoreDetail();

    private void OnDetailTextChanged(object sender, TextChangedEventArgs e) => StoreDetail();

    private void OnDetailSelectionChanged(object sender, SelectionChangedEventArgs e) => StoreDetail();
}
