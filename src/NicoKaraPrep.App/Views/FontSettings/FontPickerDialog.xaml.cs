using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NicoKaraPrep.App.Services;
using Windows.System;
using Windows.UI.Text;

namespace NicoKaraPrep.App.Views.FontSettings;

/// <summary>フォントを選ぶ画面の結果の種類。</summary>
public enum FontPickerResultKind
{
    /// <summary>キャンセル（何も変えない）。</summary>
    Cancel,

    /// <summary>継承に戻す（フォント名・フェイス名を空にする）。</summary>
    Inherit,

    /// <summary>フォントを選んだ。</summary>
    Select,
}

/// <summary>フォントを選ぶ画面の結果。</summary>
public sealed record FontPickerResult(FontPickerResultKind Kind, string FontName = "", string FaceName = "");

/// <summary>フォントを選ぶ画面の 1 フォント（ファミリー）。見本の文字をそのフォントで描く。</summary>
public sealed partial class FontFamilyItem : ObservableObject
{
    public FontFamilyItem(SystemFontFamily? family, string name, string badge, string sample, string previewFamily)
    {
        Family = family;
        Name = name;
        Badge = badge;
        this.sample = sample;
        PreviewFamily = new FontFamily(previewFamily);
        var face = family?.DefaultFace(null);
        PreviewWeight = face?.Weight ?? new FontWeight { Weight = 400 };
        PreviewStyle = face?.Style ?? FontStyle.Normal;
        PreviewStretch = face?.Stretch ?? FontStretch.Normal;
        ToolTip = family is null
            ? $"{name}（{badge}）"
            : $"{string.Join(" / ", family.AllNames)}\nフェイス {family.Faces.Count} 件: {string.Join("、", family.Faces.Select(f => f.Name))}";
    }

    /// <summary>システムのフォント（この PC に無い名前・入力した名前なら null）。</summary>
    public SystemFontFamily? Family { get; }

    /// <summary>フォント名（保存する名前）。</summary>
    public string Name { get; }

    /// <summary>「使用中」「この PC に無い」などの印。</summary>
    public string Badge { get; }

    public bool IsUsed => Badge == FontPickerDialog.UsedBadge;

    public FontFamily PreviewFamily { get; }

    public FontWeight PreviewWeight { get; }

    public FontStyle PreviewStyle { get; }

    public FontStretch PreviewStretch { get; }

    public string ToolTip { get; }

    [ObservableProperty]
    private string sample;

    /// <summary>名前のどれかが <paramref name="name"/> と同じか。</summary>
    public bool Matches(string name) =>
        Family?.Matches(name) ?? string.Equals(Name, name, StringComparison.OrdinalIgnoreCase);

    /// <summary>検索の文字を含むか。</summary>
    public bool Contains(string query) =>
        Family?.Contains(query) ?? Name.Contains(query, StringComparison.OrdinalIgnoreCase);

    public override string ToString() => Name;
}

/// <summary>フォントを選ぶ画面の 1 フェイス（太さ・斜体）。見本の文字をそのフェイスで描く。</summary>
public sealed partial class FontFaceItem : ObservableObject
{
    public FontFaceItem(string name, FontFamily previewFamily, FontWeight weight, FontStyle style, FontStretch stretch, string sample, string toolTip)
    {
        Name = name;
        PreviewFamily = previewFamily;
        Weight = weight;
        Style = style;
        Stretch = stretch;
        this.sample = sample;
        ToolTip = toolTip;
    }

    /// <summary>フェイス名（保存する名前。空は「指定しない」）。</summary>
    public string Name { get; }

    public FontFamily PreviewFamily { get; }

    public FontWeight Weight { get; }

    public FontStyle Style { get; }

    public FontStretch Stretch { get; }

    public string ToolTip { get; }

    [ObservableProperty]
    private string sample;

    public override string ToString() => Name;
}

/// <summary>
/// フォントを選ぶ画面。システムのフォントを、見本の文字をそのフォントで描いた一覧から選び、右の一覧でフェイス（太さ・斜体）を選ぶ。
/// 名前はどの言語でも検索でき、この PC に無いフォント名（ニコカラメーカー3 を使う別の PC のフォントなど）も入力して使える。
/// フォント設定で使っているフォントは一覧の先頭に出す。
/// </summary>
public sealed partial class FontPickerDialog : ContentDialog
{
    internal const string UsedBadge = "使用中";
    private const string MissingBadge = "この PC に無い";
    private const string TypedBadge = "入力した名前";

    /// <summary>この PC に無いフォントの見本に使うフォント。</summary>
    private const string FallbackPreviewFamily = "Yu Gothic UI";

    /// <summary>フェイスの一覧が引けないフォント（この PC に無い・入力した名前）で出すフェイスの候補。</summary>
    private static readonly string[] CommonFaceNames = { "Bold", "ﾍﾋﾞｰ", "ｴｸｽﾄﾗﾎﾞｰﾙﾄﾞ", "Regular" };

    /// <summary>見本の文字（アプリの実行中は前回の値を使う）。</summary>
    private static string s_sample = "永あア亜Aa1";

    private readonly string _currentFont;
    private readonly string _currentFace;
    private readonly string _inheritedFont;
    private readonly string _inheritedFace;
    private readonly HashSet<string> _usedFonts;
    private List<FontFamilyItem> _items = new();
    private FontFamilyItem? _selectedFamily;
    private FontFaceItem? _selectedFace;
    private string _lastFaceName;
    private bool _ready;
    private bool _updating;

    /// <param name="rowLabel">文字種別（歌詞／漢字 など。題名に出す）。</param>
    /// <param name="currentFont">いまのフォント名（空は継承）。</param>
    /// <param name="currentFace">いまのフェイス名（空は継承）。</param>
    /// <param name="inheritedFont">継承に戻したときのフォント名（空はニコカラメーカー3 の既定のフォント）。</param>
    /// <param name="inheritedFace">継承に戻したときのフェイス名。</param>
    /// <param name="usedFonts">フォント設定で使っているフォント名（一覧の先頭に出す）。</param>
    /// <param name="japaneseOnly">「日本語のあるフォントだけ」を最初から選んでおくか。</param>
    public FontPickerDialog(
        string rowLabel,
        string currentFont,
        string currentFace,
        string inheritedFont,
        string inheritedFace,
        IEnumerable<string> usedFonts,
        bool japaneseOnly)
    {
        _currentFont = currentFont.Trim();
        _currentFace = currentFace.Trim();
        _inheritedFont = inheritedFont.Trim();
        _inheritedFace = inheritedFace.Trim();
        _usedFonts = new HashSet<string>(usedFonts.Where(n => !string.IsNullOrWhiteSpace(n)), StringComparer.OrdinalIgnoreCase);
        _lastFaceName = _currentFace.Length > 0 ? _currentFace : _inheritedFace;

        InitializeComponent();
        Resources["ContentDialogMaxWidth"] = 1000d;
        Resources["ContentDialogMaxHeight"] = 900d;
        Title = $"フォントを選ぶ（{rowLabel}）";
        FontSampleBox.Text = s_sample;
        JapaneseOnlyBox.IsChecked = japaneseOnly;
        InheritText.Text = $"「継承に戻す」にすると: {Describe(_inheritedFont, _inheritedFace)}";
        IsPrimaryButtonEnabled = false;

        Opened += OnOpened;
        PrimaryButtonClick += (_, e) =>
        {
            if (!TryCommit()) e.Cancel = true;
        };
        SecondaryButtonClick += (_, _) => Result = new FontPickerResult(FontPickerResultKind.Inherit);
        FontSearchBox.PreviewKeyDown += OnSearchKeyDown; // 入力欄が ↓ を処理する前に受ける
    }

    /// <summary>選んだ結果（閉じたあとに読む）。</summary>
    public FontPickerResult Result { get; private set; } = new(FontPickerResultKind.Cancel);

    private static string Describe(string font, string face)
    {
        string f = font.Length > 0 ? font : "ニコカラメーカー3 の既定のフォント";
        return face.Length > 0 ? $"{f}（{face}）" : f;
    }

    private async void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        FontSearchBox.Focus(FocusState.Programmatic);
        IReadOnlyList<SystemFontFamily> catalog;
        try
        {
            catalog = await SystemFontCatalog.LoadAsync();
        }
        catch (Exception)
        {
            catalog = Array.Empty<SystemFontFamily>();
        }

        BuildItems(catalog);
        LoadingPanel.Visibility = Visibility.Collapsed;
        _ready = true;

        // いまのフォント（継承なら継承したフォント）を選んでおく
        string initial = _currentFont.Length > 0 ? _currentFont : _inheritedFont;
        var select = initial.Length > 0 ? _items.FirstOrDefault(i => i.Matches(initial)) : null;
        if (select is { Family: { SupportsJapanese: false } }) JapaneseOnlyBox.IsChecked = false;
        ApplyFilter(select);
        if (select is not null) FamilyGrid.ScrollIntoView(select, ScrollIntoViewAlignment.Leading);
    }

    /// <summary>一覧の項目を作る（使用中のフォントを先頭に。いまのフォントがこの PC に無ければ、さらにその前に出す）。</summary>
    private void BuildItems(IReadOnlyList<SystemFontFamily> catalog)
    {
        string sample = FontSampleBox.Text;
        var items = catalog
            .Select(f => new FontFamilyItem(f, f.Name, _usedFonts.Any(f.Matches) ? UsedBadge : "", sample, f.Name))
            .OrderBy(i => i.IsUsed ? 0 : 1) // 並びは名前の順のまま
            .ToList();

        foreach (string name in new[] { _currentFont, _inheritedFont })
        {
            if (name.Length > 0 && !items.Any(i => i.Matches(name)))
            {
                items.Insert(0, new FontFamilyItem(null, name, MissingBadge, sample, FallbackPreviewFamily));
            }
        }
        _items = items;
    }

    /// <summary>検索と絞り込みに合う項目を出す（選んでいた項目は、合っていればそのまま選んでおく）。</summary>
    private void ApplyFilter(FontFamilyItem? select = null)
    {
        if (!_ready) return;
        string q = FontSearchBox.Text.Trim();
        bool japaneseOnly = JapaneseOnlyBox.IsChecked == true;
        var keep = select ?? _selectedFamily;

        var list = new List<FontFamilyItem>();
        foreach (var item in _items)
        {
            bool isKept = ReferenceEquals(item, keep);
            if (japaneseOnly && item.Family is { SupportsJapanese: false } && !isKept) continue;
            if (q.Length > 0 && !item.Contains(q)) continue;
            list.Add(item);
        }
        if (q.Length > 0 && list.Count == 0 && !_items.Any(i => i.Matches(q)))
        {
            // 一致するフォントが無い名前は、入力した名前のまま使えるようにする（ニコカラメーカー3 を使う別の PC のフォントなど）。
            // 一部だけ入力したときに無いフォント名で決まってしまわないよう、一致するものがあるときは出さない
            list.Add(new FontFamilyItem(null, q, TypedBadge, FontSampleBox.Text, FallbackPreviewFamily));
        }

        _updating = true;
        try
        {
            FamilyGrid.ItemsSource = list;
            var target = keep is not null && list.Contains(keep) ? keep
                : q.Length > 0 ? list.FirstOrDefault()
                : null;
            FamilyGrid.SelectedItem = target;
        }
        finally
        {
            _updating = false;
        }
        OnFamilyChanged(FamilyGrid.SelectedItem as FontFamilyItem);
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void OnFilterChanged(object sender, RoutedEventArgs e) => ApplyFilter();

    /// <summary>検索欄で ↓ を押したら一覧へ移る。</summary>
    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Down) return;
        if (FamilyGrid.SelectedItem is null && FamilyGrid.Items.Count > 0) FamilyGrid.SelectedIndex = 0;
        if (FamilyGrid.SelectedItem is { } item && FamilyGrid.ContainerFromItem(item) is Control container)
        {
            container.Focus(FocusState.Keyboard);
        }
        else
        {
            FamilyGrid.Focus(FocusState.Keyboard);
        }
        e.Handled = true;
    }

    /// <summary>見本の文字を変えたら、一覧の見本をすべて描き直す。</summary>
    private void OnSampleChanged(object sender, TextChangedEventArgs e)
    {
        s_sample = FontSampleBox.Text;
        foreach (var item in _items) item.Sample = s_sample;
        if (FamilyGrid.ItemsSource is IEnumerable<FontFamilyItem> visible)
        {
            foreach (var item in visible) item.Sample = s_sample;
        }
        if (FaceList.ItemsSource is IEnumerable<FontFaceItem> faces)
        {
            foreach (var face in faces) face.Sample = s_sample;
        }
    }

    private void OnFamilySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        OnFamilyChanged(FamilyGrid.SelectedItem as FontFamilyItem);
    }

    /// <summary>選んだフォントのフェイスの一覧を作り、前に選んでいたフェイス名があればそれを選ぶ。</summary>
    private void OnFamilyChanged(FontFamilyItem? family)
    {
        _selectedFamily = family;
        var faces = new List<FontFaceItem>();
        FontFaceItem? select = null;
        if (family is not null)
        {
            string preferred = family.Matches(_currentFont) && _currentFace.Length > 0 ? _currentFace : _lastFaceName;
            if (family.Family is { } f && f.Faces.Count > 0)
            {
                var defaultFace = f.DefaultFace(preferred);
                foreach (var face in f.Faces)
                {
                    var item = new FontFaceItem(
                        face.Name, family.PreviewFamily, face.Weight, face.Style, face.Stretch, FontSampleBox.Text,
                        $"{string.Join(" / ", face.AllNames)}（太さ {face.Weight.Weight}）");
                    faces.Add(item);
                    if (ReferenceEquals(face, defaultFace)) select = item;
                }
            }
            else
            {
                // この PC に無いフォントは、いまのフェイス名とよく使う名前を候補にする（見本は名前から推し量った太さ）
                var names = new[] { preferred }.Concat(CommonFaceNames).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);
                foreach (string name in names)
                {
                    var guess = DirectWriteFontResolver.GuessFromFaceName(name);
                    var item = new FontFaceItem(name, family.PreviewFamily, guess.Weight, guess.Style, guess.Stretch, FontSampleBox.Text, name);
                    faces.Add(item);
                    select ??= item;
                }
            }
        }

        _updating = true;
        try
        {
            FaceList.ItemsSource = faces;
            FaceList.SelectedItem = select;
        }
        finally
        {
            _updating = false;
        }
        _selectedFace = select;
        UpdateSelectionText();
    }

    private void OnFaceSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        _selectedFace = FaceList.SelectedItem as FontFaceItem;
        if (_selectedFace is not null) _lastFaceName = _selectedFace.Name;
        UpdateSelectionText();
    }

    private void UpdateSelectionText()
    {
        IsPrimaryButtonEnabled = _selectedFamily is not null;
        if (_selectedFamily is not { } family)
        {
            SelectionText.Text = "一覧からフォントを選んでください";
            return;
        }
        string note = family.Family is null
            ? "　※ この PC に無いフォント名です。ニコカラメーカー3 を使う PC にあれば、そのフォントで表示されます"
            : "";
        SelectionText.Text = $"選択中: {Describe(family.Name, _selectedFace?.Name ?? "")}{note}";
    }

    private bool TryCommit()
    {
        if (_selectedFamily is not { } family) return false;
        Result = new FontPickerResult(FontPickerResultKind.Select, family.Name, _selectedFace?.Name ?? "");
        return true;
    }

    /// <summary>フォントをダブルクリックしたら、そのフォントで決める。</summary>
    private void OnFamilyDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is FontFamilyItem item)
        {
            if (!ReferenceEquals(item, _selectedFamily)) FamilyGrid.SelectedItem = item;
            if (TryCommit()) Hide();
        }
    }

    /// <summary>フェイスをダブルクリックしたら、そのフェイスで決める。</summary>
    private void OnFaceDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is FontFaceItem item)
        {
            if (!ReferenceEquals(item, _selectedFace)) FaceList.SelectedItem = item;
            if (TryCommit()) Hide();
        }
    }
}
