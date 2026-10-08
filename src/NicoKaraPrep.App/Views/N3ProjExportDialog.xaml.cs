using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NicoKaraPrep.App.ViewModels;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;
using NicoKaraPrep.Core.Project;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace NicoKaraPrep.App.Views;

/// <summary>書き出しダイアログの歌詞設定タブ 1 行分。</summary>
public partial class N3TabRow : ObservableObject
{
    public const string AutoLayout = "（行数に応じて自動）";

    public string Name { get; set; } = "";

    public string FileName { get; set; } = "";

    public string LineCountText { get; set; } = "";

    /// <summary>選べるレイアウト名（先頭は自動）。ダイアログ全体で共有。</summary>
    public ObservableCollection<string> LayoutChoices { get; set; } = new();

    [ObservableProperty]
    private string layoutChoice = AutoLayout;

    /// <summary>0 = 全体設定に従う / 1 = 上段を短めに / 2 = 上段を長めに。</summary>
    [ObservableProperty]
    private int topLongIndex;

    /// <summary>最初のフォントの選択肢（先頭は自動。自動で決まる名前を添える）。</summary>
    public ObservableCollection<string> FontChoices { get; set; } = new();

    /// <summary>自動の項目の表示（「（自動: （コーラス））」など）。</summary>
    public string AutoFont { get; set; } = "（自動）";

    [ObservableProperty]
    private string fontChoice = "";
}

public sealed partial class N3ProjExportDialog : ContentDialog
{
    private readonly MainViewModel _vm;
    private readonly ObservableCollection<string> _layoutChoices = new() { N3TabRow.AutoLayout };
    private string? _basePath;
    private List<string> _baseFontNames = new();

    public ObservableCollection<N3TabRow> Rows { get; } = new();

    /// <summary>OK 時の書き出し設定。</summary>
    public N3ProjSongSettings Result { get; private set; } = new();

    public N3ProjExportDialog(MainViewModel vm)
    {
        _vm = vm;
        var current = vm.N3ProjSettings;

        // タブの最初のフォント: 選べるのは書き出しのフォント設定の名前。自動の項目には、指定が無いときに決まる名前を添える
        var (fontNames, defaultFont) = vm.GetExportFontNames();
        var names = fontNames.Where(n => n.Length > 0).Distinct().ToList();
        var exportTabs = vm.GetN3ProjExportTabs();
        var autoStarts = vm.GetTabStartFontNames(exportTabs, fontNames, defaultFont, new N3ProjSongSettings());
        int k = 0;
        foreach (var tab in exportTabs)
        {
            int count = tab.Document.Lines.Count(l => !l.IsEmpty);
            string autoFont = $"（自動: {autoStarts[k++] ?? defaultFont ?? fontNames.FirstOrDefault() ?? "先頭のフォント設定"}）";
            var fontChoices = new ObservableCollection<string> { autoFont };
            foreach (string n in names) fontChoices.Add(n);
            Rows.Add(new N3TabRow
            {
                Name = tab.Name,
                FileName = tab.IsMain ? "メイン（プロジェクト名.lrc）" : $"プロジェクト名_{tab.Name}.lrc",
                LineCountText = $"{count} 行",
                LayoutChoices = _layoutChoices,
                LayoutChoice = current.TabLayouts.GetValueOrDefault(tab.Name) is { Length: > 0 } l ? l : N3TabRow.AutoLayout,
                TopLongIndex = current.TabTopLong.TryGetValue(tab.Name, out bool tl) ? (tl ? 2 : 1) : 0,
                FontChoices = fontChoices,
                AutoFont = autoFont,
                FontChoice = current.TabFontSetNames.GetValueOrDefault(tab.Name) is { Length: > 0 } f && names.Contains(f) ? f : autoFont,
            });
        }

        InitializeComponent();
        Resources["ContentDialogMaxWidth"] = 900d;
        Resources["ContentDialogMaxHeight"] = 900d;

        ProjectNameBox.Text = current.ProjectName;
        ProjectNameBox.PlaceholderText = Path.GetFileNameWithoutExtension(vm.SuggestN3ProjOutputPath() ?? "lyrics");
        MergeFontsCheck.IsChecked = current.MergeFontSets;
        MergeLayoutsCheck.IsChecked = current.MergeLayouts;
        var fonts = vm.ExportFontSets;
        int songFonts = vm.SongFontSets.Count;
        string songNote = songFonts > 0 ? $"。この曲専用 {songFonts} 件を含む" : "";
        MergeFontsCheck.Content = $"NicoKaraPrep のフォント設定（{fonts.Count} 件{songNote}）をベースへ反映する（同名は上書き、無い名前は追加）";
        MediaText.Text = vm.MediaPath is { Length: > 0 } m
            ? $"背景素材: {m}（メディア再生パネルのファイル）"
            : "背景素材: 未設定（ベースのまま。メディア再生パネルに動画を読み込むと設定されます）";

        // 表示時刻のパラメーターと自動調整は、「表示時刻の自動調整」（行設定の「自動調整...」）で行う（ここでは今の値と、行に持たせた表示時刻の数を案内する）
        var st = vm.Settings;
        var (counts, _) = vm.ShowTimeSummary();
        string stored = counts.Manual + counts.Loaded + counts.Auto > 0
            ? $"表示中のタブでは、手で直した {counts.Manual} 行・読み込んだ {counts.Loaded} 行・自動調整の {counts.Auto} 行は、その表示時刻のまま書き出します。"
            : "";
        ShowTimeNote.Text =
            "行の表示時刻は、「表示時刻の自動調整」（行設定の「自動調整...」）で決めます（自動調整のパラメーターと実行）。" + stored +
            $"表示時刻を持たない行は、書き出しのときに今のパラメーター（ワイプ前 {st.DisplayLeadSeconds:0.0#} 秒・ワイプ後 {st.DisplayTailSeconds:0.0#} 秒・表示間隔 {st.N3IntervalSeconds:0.0#} 秒・" +
            $"重ねてよい {st.N3OverlapSeconds:0.0#} 秒・上段を{(st.N3TopLong ? "長め" : "短め")}に・絵文字の分だけ遅らせる {(st.N3EmojiLeadYield ? "オン" : "オフ")}）で計算します。";

        SetBasePath(vm.SuggestN3ProjBasePath());
        DefaultFontBox.Text = current.DefaultFontSetName;

        PrimaryButtonClick += (_, _) => Apply();
        SecondaryButtonClick += (_, _) => Apply(); // 「適用」: 書き出さずに設定だけ保存する（画面の作り直しは呼び出し側）
    }

    private void SetBasePath(string? path)
    {
        _basePath = path is { Length: > 0 } && File.Exists(path) ? path : null;
        BasePathBox.Text = _basePath ?? "";

        _baseFontNames = new List<string>();
        var layoutNames = new List<string>();
        N3SubtitleAction? baseAction = null;
        string info;
        if (_basePath is null)
        {
            info = "標準設定で生成します（フォント設定: NicoKaraPrep のフォント設定か「標準」1 件、レイアウト: 下寄せ 1〜3 行など）";
        }
        else
        {
            try
            {
                var s = N3ProjFormat.Read(_basePath);
                _baseFontNames = s.FontSetNames.Where(n => n.Length > 0).ToList();
                layoutNames = s.Layouts.Select(l => l.Name).Where(n => n.Length > 0).ToList();
                baseAction = s.DefaultSubtitleAction;
                info = $"画面 {s.ScreenWidth}×{s.ScreenHeight} / フォント設定 {s.Fonts.Count} 件 / レイアウト {s.Layouts.Count} 件 / 保存バージョン {s.AppVersion ?? "不明"}";
            }
            catch (Exception ex)
            {
                info = $"⚠ ベースを読み込めません: {ex.Message}";
                _basePath = null;
            }
        }
        BaseInfoText.Text = info;
        ShowSubtitleAction(baseAction);

        // ベースが無ければ書き出しの既定のレイアウト、どちらにも NicoKaraPrep で足したレイアウトを加える
        if (_basePath is null) layoutNames = N3LayoutReader.Defaults(1080).Select(l => l.Name).ToList();
        layoutNames.AddRange(_vm.Settings.N3Layouts.Select(l => l.Name).Where(n => n.Length > 0));

        // レイアウト候補（自動 + ベースのレイアウト名）を差し替える（行の選択は維持）
        var selected = Rows.Select(r => r.LayoutChoice).ToList();
        _layoutChoices.Clear();
        _layoutChoices.Add(N3TabRow.AutoLayout);
        foreach (string n in layoutNames.Distinct()) _layoutChoices.Add(n);
        for (int i = 0; i < Rows.Count; i++)
        {
            Rows[i].LayoutChoice = _layoutChoices.Contains(selected[i]) ? selected[i] : N3TabRow.AutoLayout;
        }

        // 既定フォント設定の候補
        string text = DefaultFontBox.Text;
        DefaultFontBox.Items.Clear();
        foreach (string n in _baseFontNames.Concat(_vm.ExportFontSets.Select(f => f.Name)).Where(n => n.Length > 0).Distinct())
        {
            DefaultFontBox.Items.Add(n);
        }
        DefaultFontBox.Text = text;
    }

    /// <summary>
    /// 書き出す字幕アクションの案内（読むだけ）: 行ごとの指定が無い歌詞行に書く曲の既定（書き出しと同じ決め方。自動ならこの画面で選んでいるベースで決める）と、
    /// 行ごとの指定のある行の数。
    /// </summary>
    private void ShowSubtitleAction(N3SubtitleAction? baseAction)
    {
        var song = _vm.N3ProjSettings.SubtitleAction;
        var action = N3ProjWriter.ResolveDefaultAction(song, song is null ? baseAction : null,
            _vm.Nkm3Env?.DefaultSubtitleActionId, _vm.Nkm3Env?.AddOnSettings, out var source);
        int manual = _vm.CountLinesWithSubtitleAction();
        SubtitleActionText.Text =
            $"字幕アクション: {_vm.DescribeSubtitleAction(action)}（{MainViewModel.DefaultSubtitleActionSourceLabel(source)}）／行ごとの指定 {manual} 行" +
            "（行リストの右のパネル「字幕アクション」で変えられます）";
    }

    private async void OnBrowseBaseClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        picker.FileTypeFilter.Add(".n3proj");
        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null) return;
        SetBasePath(file.Path);
    }

    private void OnClearBaseClick(object sender, RoutedEventArgs e) => SetBasePath(null);

    private void Apply()
    {
        var result = new N3ProjSongSettings
        {
            BasePath = _basePath,
            OutputPath = _vm.N3ProjSettings.OutputPath,
            ProjectName = PlaceholderPrefill.EffectiveText(ProjectNameBox).Trim(),
            DefaultFontSetName = DefaultFontBox.Text.Trim(),
            MergeFontSets = MergeFontsCheck.IsChecked == true,
            MergeLayouts = MergeLayoutsCheck.IsChecked == true,
            // この画面に無い曲の設定は今の設定を引き継ぐ（作り直した設定で置き換えるため、写さないと消える）
            SubtitleAction = _vm.N3ProjSettings.SubtitleAction?.Clone(),
        };
        foreach (var row in Rows)
        {
            if (row.LayoutChoice != N3TabRow.AutoLayout && row.LayoutChoice.Length > 0)
            {
                result.TabLayouts[row.Name] = row.LayoutChoice;
            }
            if (row.TopLongIndex is 1 or 2)
            {
                result.TabTopLong[row.Name] = row.TopLongIndex == 2;
            }
            if (row.FontChoice is { Length: > 0 } font && font != row.AutoFont)
            {
                result.TabFontSetNames[row.Name] = font;
            }
        }
        Result = result;
    }

    private static double Value(NumberBox box, double fallback) => double.IsNaN(box.Value) ? fallback : box.Value;
}
