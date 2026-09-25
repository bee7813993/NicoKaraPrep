using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NicoKaraPrep.App.ViewModels;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.Views;

/// <summary>読み込み確認画面の初期選択。</summary>
public enum N3ProjImportFocus
{
    /// <summary>チェック用の設定と書き出しのベース（従来の「設定を読み込み」と同じ）。</summary>
    Default,

    /// <summary>フォント設定だけ（フォント設定の編集画面の「n3proj から取り込み」から）。</summary>
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
        AfterBrush = ToBrush(f.TextColorAfter),
        BeforeBrush = ToBrush(f.TextColorBefore),
        AfterTip = ColorTip("ワイプ後の文字色", f.TextColorAfter),
        BeforeTip = ColorTip("ワイプ前の文字色", f.TextColorBefore),
        ActionText = exists ? "置き換え" : "追加",
    };

    private static Brush ToBrush(string web16) =>
        N3FontSet.TryParseWeb16(web16, out byte r, out byte g, out byte b)
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b))
            : new SolidColorBrush(Colors.Transparent);

    private static string ColorTip(string label, string web16) =>
        N3FontSet.IsValidWeb16(web16) ? $"{label} #{web16.ToUpperInvariant()}" : $"{label}: 単色以外（取り込まれません）";
}

public sealed partial class N3ProjImportDialog : ContentDialog
{
    private readonly MainViewModel _vm;
    private readonly N3ProjImportPreview _preview;
    private readonly int _matchedLines;
    private readonly int _lineShowCurrent;
    private readonly int _lineShowEstimated;

    public ObservableCollection<N3ImportFontRow> FontRows { get; } = new();

    /// <summary>「読み込む」で確定した取り込み項目。</summary>
    public N3ProjImportChoices Result { get; private set; } = new();

    public N3ProjImportDialog(MainViewModel vm, N3ProjImportPreview preview, N3ProjImportFocus focus)
    {
        _vm = vm;
        _preview = preview;

        var existing = new HashSet<string>(vm.Settings.N3FontSets.Select(f => f.Name));
        foreach (var f in preview.FontSets.Where(f => f.Name.Length > 0))
        {
            var row = N3ImportFontRow.From(f, existing.Contains(f.Name));
            row.PropertyChanged += OnFontRowChanged;
            FontRows.Add(row);
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

        _lineShowCurrent = matched > 0 ? vm.CountLineShowTimeImports(preview, withEstimatedTiming: false) : 0;
        _lineShowEstimated = matched > 0 && preview.Timing is not null ? vm.CountLineShowTimeImports(preview, withEstimatedTiming: true) : 0;
        LineShowBox.IsEnabled = matched > 0;

        // ---- 書き出しのベース ----
        string? currentBase = vm.N3ProjSettings.BasePath;
        bool sameBase = currentBase is not null && string.Equals(Path.GetFullPath(currentBase), Path.GetFullPath(preview.Path), StringComparison.OrdinalIgnoreCase);
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
            BaseBox.IsChecked = currentBase is null || sameBase;
        }

        UpdateLineShowDetail();
        UpdateFontSetsState();
        PrimaryButtonClick += (_, _) => Apply();
    }

    private void UpdateLineShowDetail()
    {
        bool withTiming = TimingBox.IsChecked == true;
        int n = withTiming ? _lineShowEstimated : _lineShowCurrent;
        if (_matchedLines == 0)
        {
            LineShowDetail.Text = "開いている歌詞と一致する行がありません";
            return;
        }
        if (n == 0)
        {
            LineShowDetail.Text = "NicoKaraPrep の自動計算と違う行はありません（取り込む行はありません）";
            return;
        }

        string text = $"NicoKaraPrep の自動計算と違う {n} 行を、行ごとの手動指定にします（歌詞が同じ行だけ。{(withTiming ? "取り込む表示時刻の設定" : "現在の表示時刻の設定")}で比較）。行リストに ✎ が付き、n3proj 書き出しもその時刻になります";
        if (!withTiming && TimingBox.IsEnabled && _lineShowEstimated < n)
        {
            text += $"\n⚠ 表示時刻の設定が違うため多くの行が対象になっています。「表示時刻の設定値を取り込む」も選ぶと、ニコカラメーカーで調整された {_lineShowEstimated} 行だけになります";
        }
        LineShowDetail.Text = text;
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
            ExportBase = BaseBox.IsChecked == true,
            FontSetNames = FontSetsBox.IsChecked == true
                ? FontRows.Where(r => r.IsSelected).Select(r => r.Name).ToList()
                : new List<string>(),
        };
        _vm.Settings.N3AutoImportNearby = AutoBox.IsChecked == true;
        _vm.Settings.Save();
    }
}
