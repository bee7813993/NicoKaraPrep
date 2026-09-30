using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NicoKaraPrep.App.Services;
using NicoKaraPrep.App.ViewModels;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.Views.FontSettings;

/// <summary>
/// 組み合わせフォント（複数人で歌うパート用）を作る画面。元にするフォント設定を並べ、塗り方（ミルフィーユ・グラデーション）と
/// 端の帯の広さを選ぶ。名前は元の名前を並びどおりにつなげたもの。「作る」を押したら結果のプロパティに入る（呼び出し側で作る）。
/// </summary>
public sealed partial class ComposeDialog : ContentDialog
{
    private readonly IReadOnlyList<FontListItem> _candidates;
    private readonly IReadOnlyList<N3ColorPattern> _patterns;
    private readonly ObservableCollection<FontListItem> _sources = new();
    private bool _loading;

    /// <param name="candidates">元にできるフォント設定（アプリ共通・この曲専用）。</param>
    /// <param name="first">最初に並べておくフォント設定（無ければ null）。</param>
    public ComposeDialog(IReadOnlyList<FontListItem> candidates, FontListItem? first, int brushType, double endWidenPercent, bool autoCompose, IReadOnlyList<N3ColorPattern> patterns)
    {
        _candidates = candidates;
        _patterns = patterns;
        InitializeComponent();
        Resources["ContentDialogMaxWidth"] = 960d;

        _loading = true;
        try
        {
            StyleButtons.SelectedIndex = brushType == N3Brush.TypeGradient ? 1 : 0;
            WidenBox.Value = endWidenPercent;
            AutoComposeBox.IsChecked = autoCompose;
            SourceList.ItemsSource = _sources;
            if (first is not null) _sources.Add(first);
            ApplySearch();
        }
        finally
        {
            _loading = false;
        }
        UpdatePreview();
        PrimaryButtonClick += (_, _) => Commit();
        Opened += (_, _) => ComposeSearchBox.Focus(FocusState.Programmatic);
    }

    /// <summary>元にするフォント設定（並びどおり。「作る」のあと）。</summary>
    public List<N3FontSet> Sources { get; private set; } = new();

    /// <summary>塗りの種類（ミルフィーユかグラデーション）。</summary>
    public int BrushType { get; private set; } = N3Brush.TypeMilleFeuille;

    /// <summary>端の帯の広さ（%）。</summary>
    public double EndWidenPercent { get; private set; } = N3FontComposer.DefaultEndWidenPercent;

    /// <summary>塗り方と端の帯の広さを、自動で作るときの既定にするか。</summary>
    public bool SaveDefaults { get; private set; }

    /// <summary>絵文字を続けて入れたときに自動で作るか。</summary>
    public bool AutoCompose { get; private set; } = true;

    // ------------------------------------------------------------ 探す・足す

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => ApplySearch();

    private void ApplySearch()
    {
        string q = ComposeSearchBox.Text.Trim();
        CandidateList.ItemsSource = _candidates
            .Where(c => q.Length == 0 || c.Font.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || c.PathText.Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();
        AddSourceButton.IsEnabled = false;
    }

    private void OnCandidateSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        AddSourceButton.IsEnabled = CandidateList.SelectedItem is FontListItem item && !_sources.Contains(item);

    private void OnCandidateDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => AddSelected();

    private void OnAddSourceClick(object sender, RoutedEventArgs e) => AddSelected();

    private void AddSelected()
    {
        if (CandidateList.SelectedItem is not FontListItem item || _sources.Contains(item)) return;
        _sources.Add(item);
        AddSourceButton.IsEnabled = false;
        UpdatePreview();
    }

    // ------------------------------------------------------------ 並び

    private void OnSourceSelectionChanged(object sender, SelectionChangedEventArgs e) { }

    private void OnSourceUpClick(object sender, RoutedEventArgs e) => MoveSelected(-1);

    private void OnSourceDownClick(object sender, RoutedEventArgs e) => MoveSelected(+1);

    private void MoveSelected(int delta)
    {
        if (SourceList.SelectedItem is not FontListItem item) return;
        int i = _sources.IndexOf(item);
        int to = i + delta;
        if (i < 0 || to < 0 || to >= _sources.Count) return;
        _sources.Move(i, to);
        SourceList.SelectedItem = item;
        UpdatePreview();
    }

    private void OnRemoveSourceClick(object sender, RoutedEventArgs e)
    {
        if (SourceList.SelectedItem is not FontListItem item) return;
        _sources.Remove(item);
        UpdatePreview();
    }

    // ------------------------------------------------------------ 塗り方・見本

    private void OnStyleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading) UpdatePreview();
    }

    private void OnWidenChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_loading) UpdatePreview();
    }

    private int SelectedBrushType => StyleButtons.SelectedIndex == 1 ? N3Brush.TypeGradient : N3Brush.TypeMilleFeuille;

    private double SelectedWiden => double.IsNaN(WidenBox.Value) ? N3FontComposer.DefaultEndWidenPercent : Math.Clamp(WidenBox.Value, 0, 200);

    /// <summary>名前・見本の帯・帯の幅の説明を、今の並びと設定で作り直す。2 つ以上並べたら「作る」を押せる。</summary>
    private void UpdatePreview()
    {
        var fonts = _sources.Select(s => s.Font).ToList();
        IsPrimaryButtonEnabled = fonts.Count >= 2;
        if (fonts.Count == 0)
        {
            ComposeNameText.Text = "元にするフォント設定を 2 つ以上並べてください";
            PreviewStrip.Background = N3BrushPreview.Transparent();
            BandsText.Text = "";
            return;
        }
        string name = string.Concat(fonts.Select(f => f.Name));
        ComposeNameText.Text = fonts.Count >= 2 ? $"名前: {name}" : "もう 1 つ以上並べてください";

        var pattern = N3FontComposer.PatternFor(null, fonts, _patterns);
        var colors = fonts.SelectMany(f => N3FontComposer.RoleColors(f.Detail, pattern, N3FontComposer.MultiColorRole)).ToList();
        var brush = N3FontComposer.MultiColorBrush(colors, SelectedBrushType, SelectedWiden);
        PreviewStrip.Background = N3BrushPreview.CreateGradient(N3BrushPreview.EffectiveStops(brush), sharp: brush.Type == N3Brush.TypeMilleFeuille);
        var widths = N3FontComposer.BandWidths(colors.Count, SelectedWiden);
        BandsText.Text = $"{pattern.RoleName(N3FontComposer.MultiColorRole)}の帯の幅（上から）: {string.Join(" / ", widths.Select(w => $"{w * 100:0.#}%"))}\n" +
                         "ほかの箇所（ベース色など）と、色以外の項目は、いちばん上のフォント設定と同じにします";
    }

    private void Commit()
    {
        Sources = _sources.Select(s => s.Font).ToList();
        BrushType = SelectedBrushType;
        EndWidenPercent = SelectedWiden;
        SaveDefaults = SaveDefaultsBox.IsChecked == true;
        AutoCompose = AutoComposeBox.IsChecked == true;
    }
}
