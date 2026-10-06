using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using NicoKaraPrep.App.Services;
using NicoKaraPrep.App.ViewModels;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.Views;

/// <summary>読み込み確認画面の初期選択。</summary>
public enum N3ProjImportFocus
{
    /// <summary>チェック用の設定と書き出しのベース（従来の「設定を読み込み」と同じ）。</summary>
    Default,

    /// <summary>フォント設定だけ（フォント設定ビューの「取り込み > n3proj から...」から）。</summary>
    FontSets,
}

/// <summary>読み込み確認画面のフォント設定 1 行分。</summary>
public partial class N3ImportFontRow : ObservableObject
{
    public string Name { get; set; } = "";

    public string FontText { get; set; } = "";

    public string SizeText { get; set; } = "";

    public Brush AfterBrush { get; set; } = new SolidColorBrush(Colors.Transparent);

    public Brush BeforeBrush { get; set; } = new SolidColorBrush(Colors.Transparent);

    public string AfterTip { get; set; } = "";

    public string BeforeTip { get; set; } = "";

    /// <summary>取り込んだときの動作（追加 / 置き換え）。</summary>
    public string ActionText { get; set; } = "";

    [ObservableProperty]
    private bool isSelected = true;

    public static N3ImportFontRow From(N3FontSet f, bool exists) => new()
    {
        Name = f.Name,
        FontText = f.FontFamily.Length > 0 ? $"{f.FontFamily}{(f.FontFace.Length > 0 ? $"（{f.FontFace}）" : "")}" : "（フォント指定なし）",
        SizeText = f.SizePx > 0 ? $"{f.SizePx:0.#}px" : "",
        AfterBrush = N3BrushPreview.Create(f.Detail.Brushes[0]),
        BeforeBrush = N3BrushPreview.Create(f.Detail.Brushes[N3FontDetail.BeforeOffset]),
        AfterTip = N3BrushPreview.Describe("ワイプ後の文字", f.Detail.Brushes[0]),
        BeforeTip = N3BrushPreview.Describe("ワイプ前の文字", f.Detail.Brushes[N3FontDetail.BeforeOffset]),
        ActionText = exists ? "置き換え" : "追加",
    };

}

/// <summary>読み込み確認画面のアイコン 1 行分。</summary>
public partial class N3ImportIconRow : ObservableObject
{
    /// <summary>置き換える文字列。</summary>
    public string Name { get; set; } = "";

    public string FileText { get; set; } = "";

    public string PathTip { get; set; } = "";

    public string OptionsText { get; set; } = "";

    public ImageSource? Thumb { get; set; }

    public bool ImageExists { get; set; }

    public EmojiEntry Entry { get; set; } = new();

    [ObservableProperty]
    private string statusText = "";

    [ObservableProperty]
    private bool isSelected;

    public static N3ImportIconRow From(N3ProjIcon icon)
    {
        var e = icon.Entry;
        string before = Path.GetFileName(e.ImageBefore);
        string after = string.IsNullOrEmpty(e.ImageAfter) ? "" : Path.GetFileName(e.ImageAfter);
        ImageSource? thumb = null;
        if (icon.ImageExists)
        {
            try { thumb = new BitmapImage(new Uri(e.ImageBefore)) { DecodePixelHeight = 56 }; }
            catch (Exception) { thumb = null; }
        }
        return new N3ImportIconRow
        {
            Name = e.ReplaceChar,
            FileText = icon.ImageExists
                ? (after.Length > 0 ? $"{before} ／ {after}" : before)
                : $"⚠ 画像が見つかりません: {before}",
            PathTip = string.IsNullOrEmpty(e.ImageAfter) ? e.ImageBefore : $"{e.ImageBefore}\n{e.ImageAfter}",
            OptionsText = e.Options ?? "",
            Thumb = thumb,
            ImageExists = icon.ImageExists,
            Entry = e,
        };
    }
}

public sealed partial class N3ProjImportDialog : ContentDialog
{
    private readonly MainViewModel _vm;
    private readonly N3ProjImportPreview _preview;
    private readonly int _matchedLines;

    public ObservableCollection<N3ImportFontRow> FontRows { get; } = new();

    public ObservableCollection<N3ImportIconRow> IconRows { get; } = new();

    /// <summary>「読み込む」で確定した取り込み項目。</summary>
    public N3ProjImportChoices Result { get; private set; } = new();

    public N3ProjImportDialog(MainViewModel vm, N3ProjImportPreview preview, N3ProjImportFocus focus)
    {
        _vm = vm;
        _preview = preview;

        var existing = new HashSet<string>(vm.Settings.N3FontSets.Select(f => f.Name));
        var projectNames = new HashSet<string>(preview.FontSets.Select(f => f.Name));
        foreach (var f in preview.FontSets.Where(f => f.Name.Length > 0))
        {
            var row = N3ImportFontRow.From(f, existing.Contains(f.Name));
            // ページの文字の大きさのために書き出しで作ったフォント設定（「（麻衣）+4」など）は、最初は選ばない（書き出すたびに作り直すため）
            if (N3PageFontSize.IsDerivedName(f.Name, projectNames)) row.IsSelected = false;
            row.PropertyChanged += OnFontRowChanged;
            FontRows.Add(row);
        }

        foreach (var icon in preview.Icons)
        {
            var row = N3ImportIconRow.From(icon);
            row.PropertyChanged += OnIconRowChanged;
            IconRows.Add(row);
        }

        InitializeComponent();
        Resources["ContentDialogMaxWidth"] = 980d;
        Resources["ContentDialogMaxHeight"] = 980d;

        var s = preview.Settings;
        var settings = vm.Settings;

        // ---- プロジェクトの概要 ----
        FileNameText.Text = Path.GetFileName(preview.Path);
        string tabs = preview.Tabs.Count > 0
            ? string.Join("・", preview.Tabs.Select(t => $"{t.Name} {t.LyricLineCount} 行"))
            : "なし";
        SummaryText.Text = $"{preview.Path}\n保存したバージョン {s.AppVersion ?? "不明"} ／ 画面 {s.ScreenWidth}×{s.ScreenHeight} ／ 歌詞設定: {tabs} ／ フォント設定 {s.Fonts.Count} 件 ／ レイアウト {s.Layouts.Count} 件";
        var (matched, total) = vm.CountMatchedLyricLines(preview);
        _matchedLines = matched;
        LyricsMatchText.Text = total == 0
            ? "歌詞を開いていないため、行ごとの表示時刻は取り込めません"
            : $"開いている歌詞 {total} 行のうち {matched} 行が、このプロジェクトの歌詞行と一致しました";

        // ---- チェック・プレビュー ----
        CheckFontDetail.Text = s.MainFont is { } font
            ? $"{font.FontName}{(string.IsNullOrEmpty(font.FaceName) ? "" : $"（{font.FaceName}）")} {font.SizePx:0.#}px・縁取り {font.EdgeSizePx:0.#}px ／ 画面の横幅 {s.ScreenWidth}px" +
              $"（現在: {settings.FontFamily} {settings.FontSizePx:0.#}px ／ {settings.ScreenWidthPx}px。ファイル > 設定 の値を置き換えます）"
            : $"画面の横幅 {s.ScreenWidth}px（歌詞で使われているフォントが見つかりません）";
        LineTimesDetail.Text = s.LineTimes.Count > 0
            ? $"{s.LineTimes.Count} 行分（この曲を開いている間だけ使います）"
            : "表示区間が設定された行がありません";
        LineTimesBox.IsEnabled = s.LineTimes.Count > 0;

        // ---- アイコン・動画 ----
        IconsBox.IsEnabled = IconRows.Count > 0;
        IconTargetBox.SelectedIndex = 0;
        UpdateIconStatuses();
        foreach (var row in IconRows)
        {
            // 取り込むと何かが変わるもの（追加・置き換え）で、画像があるものを初期選択
            row.IsSelected = row.ImageExists && row.StatusText is "追加" or "置き換え";
        }

        string? currentMedia = vm.MediaPath;
        bool sameMedia = currentMedia is { Length: > 0 } && preview.MediaPath is { Length: > 0 } &&
            string.Equals(Path.GetFullPath(currentMedia), Path.GetFullPath(preview.MediaPath), StringComparison.OrdinalIgnoreCase);
        if (preview.MediaPath is not { Length: > 0 } media)
        {
            MediaDetail.Text = "背景素材の動画・音声が設定されていません";
            MediaBox.IsEnabled = false;
        }
        else if (!preview.MediaExists)
        {
            MediaDetail.Text = $"⚠ ファイルが見つかりません: {media}";
            MediaBox.IsEnabled = false;
        }
        else
        {
            MediaDetail.Text = media + (sameMedia ? "（現在も同じファイルです）"
                : currentMedia is { Length: > 0 } ? $"（現在: {Path.GetFileName(currentMedia)}）" : "（現在: なし）");
        }

        // ---- 表示時刻 ----
        string current = $"ワイプ前 {settings.DisplayLeadSeconds:0.###} 秒・ワイプ後 {settings.DisplayTailSeconds:0.###} 秒・表示間隔 {settings.N3IntervalSeconds:0.###} 秒・上段を{(settings.N3TopLong ? "長め" : "短め")}に";
        if (preview.Timing is { } t)
        {
            TimingDetail.Text =
                $"ワイプ前 {t.LeadMs / 1000.0:0.###} 秒・ワイプ後 {t.TailMs / 1000.0:0.###} 秒・表示間隔 {t.IntervalMs / 1000.0:0.###} 秒・上段を{(preview.MainTopLong ? "長め" : "短め")}に表示" +
                $"（このプロジェクトの表示時刻から推定。{t.Total} 行中 {t.Matched} 行が自動計算と一致）\n現在: {current}";
        }
        else
        {
            TimingDetail.Text = "表示時刻が設定された行がありません";
            TimingBox.IsEnabled = false;
        }

        LineShowBox.IsEnabled = matched > 0;
        LineShowBox.IsChecked = matched > 0; // 読み込んだプロジェクトの表示時刻のまま見る・直す・書き出す（自動調整は右のパネル「表示時刻」で実行する）

        // ---- レイアウト ----
        int layoutPages = matched > 0 ? vm.CountPageLayoutImports(preview) : 0;
        PageLayoutDetail.Text = matched == 0
            ? "開いている歌詞と一致する行がありません"
            : layoutPages == 0
                ? "NicoKaraPrep が自動で選ぶレイアウトと違うページはありません（取り込むページはありません）"
                : $"NicoKaraPrep が自動で選ぶレイアウトと違う {layoutPages} ページを、ページごとの手動指定にします（歌詞が同じ行のページだけ）。" +
                  "行リストのレイアウトの欄に ✎ が付き、字幕のプレビュー・n3proj 書き出しもそのレイアウトになります" +
                  "（ニコカラメーカー3 の「適用対象レイアウト」の範囲が、このプロジェクトを作ったときと今とで違うと、自動で選ぶレイアウトが変わります）";
        PageLayoutBox.IsEnabled = layoutPages > 0;

        // ---- 字幕アクション ----
        var (projectAction, actionLines) = vm.CountLineActionImports(preview);
        if (projectAction is null)
        {
            LineActionsTitle.Text = "字幕アクションを取り込む（このプロジェクトの歌詞行には字幕アクションがありません）";
            LineActionsDetail.Text = "曲の既定・行ごとの指定は今のままです";
            LineActionsBox.IsEnabled = false;
        }
        else
        {
            var currentAction = vm.ResolveCurrentDefaultSubtitleAction(out var currentSource);
            LineActionsTitle.Text = $"字幕アクション（曲の既定: {vm.DescribeSubtitleAction(projectAction)}、行ごとの指定 {actionLines} 行）を取り込む";
            LineActionsDetail.Text =
                "このプロジェクトの歌詞行でいちばん多い字幕アクションを曲の既定にし" +
                $"（現在: {vm.DescribeSubtitleAction(currentAction)}（{MainViewModel.DefaultSubtitleActionSourceLabel(currentSource)}））、" +
                (matched == 0
                    ? "開いている歌詞と一致する行が無いため、行ごとの指定は取り込みません。"
                    : $"それと違うアクションの {actionLines} 行を行ごとの指定にします（歌詞が同じ行だけ。同じアクションの行は指定を外して曲の既定に従わせます）。") +
                "字幕アクションは、レイアウト設定ビュー（F4）と右のパネル「レイアウト」で変えられます";
        }

        // ---- 書き出しのベース ----
        string? currentBase = vm.N3ProjSettings.BasePath;
        bool sameBase = currentBase is { Length: > 0 } && string.Equals(Path.GetFullPath(currentBase), Path.GetFullPath(preview.Path), StringComparison.OrdinalIgnoreCase);
        BaseDetail.Text = "このプロジェクトのフォント設定・レイアウト・タイトル・出力設定を、n3proj 書き出しで引き継ぎます" +
            (sameBase ? "（現在もこのプロジェクトがベースです）"
                : currentBase is { Length: > 0 } ? $"（現在のベース: {Path.GetFileName(currentBase)}）" : "（現在のベース: なし）");

        // ---- フォント設定 ----
        FontSetsBox.IsEnabled = FontRows.Count > 0;

        AutoBox.IsChecked = settings.N3AutoImportNearby;

        // ---- 初期選択 ----
        if (focus == N3ProjImportFocus.FontSets)
        {
            FontSetsBox.IsChecked = FontRows.Count > 0;
        }
        else
        {
            CheckFontBox.IsChecked = true;
            LineTimesBox.IsChecked = s.LineTimes.Count > 0;
            BaseBox.IsChecked = currentBase is not { Length: > 0 } || sameBase;
            IconsBox.IsChecked = IconRows.Any(r => r.IsSelected);
            MediaBox.IsChecked = MediaBox.IsEnabled && !sameMedia && (currentMedia is not { Length: > 0 } || !File.Exists(currentMedia));
            PageLayoutBox.IsChecked = PageLayoutBox.IsEnabled; // 違うページがあれば、ニコカラメーカーと同じレイアウトにする
            LineActionsBox.IsChecked = LineActionsBox.IsEnabled; // アクションがあれば、ニコカラメーカーと同じアクションにする
        }

        UpdateLineShowDetail();
        UpdateFontSetsState();
        UpdateIconsState();
        PrimaryButtonClick += (_, _) => Apply();
    }

    private void UpdateLineShowDetail()
    {
        LineShowDetail.Text = _matchedLines == 0
            ? "開いている歌詞と一致する行がありません"
            : $"歌詞が同じ {_matchedLines} 行の表示開始・終了を、ニコカラメーカーの値のまま行に持たせます。字幕のプレビュー・チェック・書き出しはその値のままです。" +
              "自動調整は、行リストの右のパネル「表示時刻」で実行します（手で直した行はそのまま）";
    }

    private void UpdateFontSetsState()
    {
        bool on = FontSetsBox.IsChecked == true;
        FontSetPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        int selected = FontRows.Count(r => r.IsSelected);
        FontSetsTitle.Text = FontRows.Count == 0
            ? "フォント設定を NicoKaraPrep に取り込む（フォント設定がありません）"
            : $"フォント設定を NicoKaraPrep に取り込む（{FontRows.Count} 件中 {selected} 件を選択）";
    }

    private void UpdateIconStatuses()
    {
        bool global = IconTargetBox.SelectedIndex == 1;
        foreach (var row in IconRows)
        {
            row.StatusText = _vm.DescribeIconImport(row.Entry, global);
        }
    }

    private void UpdateIconsState()
    {
        bool on = IconsBox.IsChecked == true;
        IconPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        int selected = IconRows.Count(r => r.IsSelected);
        IconsTitle.Text = IconRows.Count == 0
            ? "アイコン（@Emoji）を取り込む（このプロジェクトにはアイコンがありません）"
            : $"アイコン（@Emoji）を取り込む（{IconRows.Count} 件中 {selected} 件を選択）";
    }

    private void OnIconsClick(object sender, RoutedEventArgs e) => UpdateIconsState();

    private void OnIconTargetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IconRows.Count > 0 && IconsTitle is not null) UpdateIconStatuses();
    }

    private void OnIconRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(N3ImportIconRow.IsSelected) && IconsTitle is not null) UpdateIconsState();
    }

    private void OnSelectAllIconsClick(object sender, RoutedEventArgs e)
    {
        foreach (var r in IconRows) r.IsSelected = true;
    }

    private void OnSelectNoIconsClick(object sender, RoutedEventArgs e)
    {
        foreach (var r in IconRows) r.IsSelected = false;
    }

    private void OnTimingClick(object sender, RoutedEventArgs e) => UpdateLineShowDetail();

    private void OnFontSetsClick(object sender, RoutedEventArgs e) => UpdateFontSetsState();

    private void OnFontRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(N3ImportFontRow.IsSelected)) UpdateFontSetsState();
    }

    private void OnSelectAllFontsClick(object sender, RoutedEventArgs e)
    {
        foreach (var r in FontRows) r.IsSelected = true;
    }

    private void OnSelectNoFontsClick(object sender, RoutedEventArgs e)
    {
        foreach (var r in FontRows) r.IsSelected = false;
    }

    private void Apply()
    {
        Result = new N3ProjImportChoices
        {
            CheckFont = CheckFontBox.IsChecked == true,
            LineTimes = LineTimesBox.IsChecked == true && LineTimesBox.IsEnabled,
            Timing = TimingBox.IsChecked == true && TimingBox.IsEnabled,
            LineShowTimes = LineShowBox.IsChecked == true && LineShowBox.IsEnabled,
            PageLayouts = PageLayoutBox.IsChecked == true && PageLayoutBox.IsEnabled,
            LineActions = LineActionsBox.IsChecked == true && LineActionsBox.IsEnabled,
            ExportBase = BaseBox.IsChecked == true,
            FontSetNames = FontSetsBox.IsChecked == true
                ? FontRows.Where(r => r.IsSelected).Select(r => r.Name).ToList()
                : new List<string>(),
            IconNames = IconsBox.IsChecked == true
                ? IconRows.Where(r => r.IsSelected).Select(r => r.Name).ToList()
                : new List<string>(),
            IconsGlobal = IconTargetBox.SelectedIndex == 1,
            Media = MediaBox.IsChecked == true && MediaBox.IsEnabled,
        };
        _vm.Settings.N3AutoImportNearby = AutoBox.IsChecked == true;
        _vm.Settings.Save();
    }
}
