using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NicoKaraPrep.App.ViewModels;
using NicoKaraPrep.Core.Formats;
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

        foreach (var tab in vm.GetN3ProjExportTabs())
        {
            int count = tab.Document.Lines.Count(l => !l.IsEmpty);
            Rows.Add(new N3TabRow
            {
                Name = tab.Name,
                FileName = tab.IsMain ? "メイン（プロジェクト名.lrc）" : $"プロジェクト名_{tab.Name}.lrc",
                LineCountText = $"{count} 行",
                LayoutChoices = _layoutChoices,
                LayoutChoice = current.TabLayouts.GetValueOrDefault(tab.Name) is { Length: > 0 } l ? l : N3TabRow.AutoLayout,
                TopLongIndex = current.TabTopLong.TryGetValue(tab.Name, out bool tl) ? (tl ? 2 : 1) : 0,
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

        LeadBox.Value = vm.Settings.DisplayLeadSeconds;
        TailBox.Value = vm.Settings.DisplayTailSeconds;
        IntervalBox.Value = vm.Settings.N3IntervalSeconds;
        ProtectBox.Value = vm.Settings.N3ProtectSeconds;
        OverlapBox.Value = vm.Settings.N3OverlapSeconds;
        TopLongCheck.IsChecked = vm.Settings.N3TopLong;
        EmojiLeadYieldCheck.IsChecked = vm.Settings.N3EmojiLeadYield;
        if (vm.Nkm3Env is { PreTimeMs: not null })
        {
            ImportNkm3Button.Visibility = Visibility.Visible;
            ImportNkm3Button.Content = $"ニコカラメーカーの設定値を取り込む（{vm.Nkm3Env.PreTimeMs / 1000.0:0.##} / {vm.Nkm3Env.PostTimeMs / 1000.0:0.##} / {vm.Nkm3Env.IntervalMs / 1000.0:0.##} 秒）";
        }

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
                info = $"画面 {s.ScreenWidth}×{s.ScreenHeight} / フォント設定 {s.Fonts.Count} 件 / レイアウト {s.Layouts.Count} 件 / 保存バージョン {s.AppVersion ?? "不明"}";
            }
            catch (Exception ex)
            {
                info = $"⚠ ベースを読み込めません: {ex.Message}";
                _basePath = null;
            }
        }
        BaseInfoText.Text = info;

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

    private void OnImportNkm3Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Nkm3Env is not { } env) return;
        if (env.PreTimeMs is int pre) LeadBox.Value = pre / 1000.0;
        if (env.PostTimeMs is int post) TailBox.Value = post / 1000.0;
        if (env.IntervalMs is int interval) IntervalBox.Value = interval / 1000.0;
    }

    private void Apply()
    {
        var s = _vm.Settings;
        s.DisplayLeadSeconds = Value(LeadBox, s.DisplayLeadSeconds);
        s.DisplayTailSeconds = Value(TailBox, s.DisplayTailSeconds);
        s.N3IntervalSeconds = Value(IntervalBox, s.N3IntervalSeconds);
        s.N3ProtectSeconds = Value(ProtectBox, 0);
        s.N3OverlapSeconds = Math.Max(0, Value(OverlapBox, 0));
        s.N3TopLong = TopLongCheck.IsChecked == true;
        s.N3EmojiLeadYield = EmojiLeadYieldCheck.IsChecked == true;
        s.Save();

        var result = new N3ProjSongSettings
        {
            BasePath = _basePath,
            OutputPath = _vm.N3ProjSettings.OutputPath,
            ProjectName = ProjectNameBox.Text.Trim(),
            DefaultFontSetName = DefaultFontBox.Text.Trim(),
            MergeFontSets = MergeFontsCheck.IsChecked == true,
            MergeLayouts = MergeLayoutsCheck.IsChecked == true,
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
        }
        Result = result;
    }

    private static double Value(NumberBox box, double fallback) => double.IsNaN(box.Value) ? fallback : box.Value;
}
