using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NicoKaraPrep.App.ViewModels;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.Views;

/// <summary>
/// 行リストのときの右のパネル。「フォント」: 押したフォント設定を、行リストで選んだ文字（文字を選んでいなければ選んだ行）に指定する。
/// 「レイアウト」: 選んだ行のページのレイアウトと字幕アクションを選んで指定する（レイアウト設定の値・字幕アクションの曲の既定は
/// レイアウト設定ビュー（F4）で編集する）。「表示時刻」: 表示時刻の自動調整のパラメーターと実行。
/// </summary>
public sealed partial class LineSidePanel : UserControl
{
    /// <summary>レイアウトの欄の「自動」の項目。</summary>
    private const string AutoLayoutItem = "（自動）";

    /// <summary>字幕アクションの欄の「既定」の項目（続けて <see cref="N3SubtitleActionCatalog.Known"/> の順に並べる）。</summary>
    private const string DefaultActionItem = "（既定）";

    private MainViewModel? _vm;

    /// <summary>表示時刻の欄に値を入れている最中か。</summary>
    private bool _loading;

    /// <summary>行リストで選んでいる行（ページの欄の対象。選んだ行が無ければ null）。</summary>
    private LineViewModel? _line;

    /// <summary>行リストで選んでいる行の添字（表示中のタブ。メイン画面から受け取る）。</summary>
    private Func<IReadOnlyList<int>>? _selectedLines;

    private string _fontsKey = "";

    /// <summary>ページの欄（レイアウト・字幕アクション）に値を入れている最中か（そのあいだの選択の変化は指定にしない）。</summary>
    private bool _pageLoading;

    /// <summary>ページの欄の作り直しを予約済みか。</summary>
    private bool _pageRefreshQueued;

    /// <summary>レイアウトの欄の候補の名前（変わったときだけ作り直す）。</summary>
    private string? _pageLayoutNamesKey;

    /// <summary>選んだ行のページで今使っているレイアウト設定の名前（「レイアウト設定を編集...」で選ぶ。分からなければ null）。</summary>
    private string? _pageLayoutName;

    public LineSidePanel()
    {
        InitializeComponent();
        PageActionBox.Items.Add(DefaultActionItem);
        foreach (var kind in N3SubtitleActionCatalog.Known) PageActionBox.Items.Add(kind.Name);
    }

    /// <summary>フォント設定を押した（null は「自動に戻す」）。</summary>
    public event EventHandler<string?>? FontPicked;

    /// <summary>ページのレイアウトの欄で選んだ（レイアウト設定名。null は「（自動）」）。選んだ行のページに指定する。</summary>
    public event EventHandler<string?>? PageLayoutPicked;

    /// <summary>ページの字幕アクションの欄で選んだ（字幕アクションの Id。null は「（既定）」）。選んだ行のページに指定する。</summary>
    public event EventHandler<string?>? PageActionPicked;

    /// <summary>「レイアウト設定を編集...」を押した（選んだ行のページで使っているレイアウト設定の名前。分からなければ null）。</summary>
    public event EventHandler<string?>? EditLayoutsRequested;

    /// <summary>メイン画面の ViewModel と、行リストで選んでいる行の添字を返す処理とつなぐ（起動時に 1 回）。</summary>
    public void Initialize(MainViewModel vm, Func<IReadOnlyList<int>> selectedLines)
    {
        _vm = vm;
        _selectedLines = selectedLines;
        // レイアウト設定の一覧（名前）・ページの指定・曲の既定の字幕アクションが変わったら、ページの欄を合わせる
        vm.LayoutsChanged += (_, _) => RefreshPagePane();
        vm.LineFontsUpdated += (_, _) => RefreshPagePane();
        vm.SongDefaultSubtitleActionChanged += (_, _) => RefreshPagePane();
        vm.PreviewModelChanged += (_, _) =>
        {
            RefreshFonts();
            if (IsShowTimePaneVisible) RefreshShowTimeSummary(); // 歌詞を直した・取り込んだ・自動調整した後の行数
        };
        RefreshFonts(force: true);
    }

    /// <summary>フォントを押したときに指定する先の説明（「10 行目の選んだ 3 文字に指定します」など）。</summary>
    public void SetTarget(string text) => FontTargetText.Text = text;

    private void OnPaneChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        bool layout = ReferenceEquals(sender.SelectedItem, LayoutPaneItem);
        bool showTime = ReferenceEquals(sender.SelectedItem, ShowTimePaneItem);
        FontPane.Visibility = layout || showTime ? Visibility.Collapsed : Visibility.Visible;
        LayoutPane.Visibility = layout ? Visibility.Visible : Visibility.Collapsed;
        ShowTimePane.Visibility = showTime ? Visibility.Visible : Visibility.Collapsed;
        if (layout) RefreshPagePane();
        if (showTime) RefreshShowTimePane();
    }

    // ------------------------------------------------------------ フォント

    /// <summary>フォントの一覧を作り直す（中身が同じなら作り直さない。スクロールの位置を保つため）。</summary>
    public void RefreshFonts(bool force = false)
    {
        if (_vm is null) return;
        var groups = _vm.BuildFontPickGroups(FontSearchBox.Text);
        string key = string.Join("|", groups.Select(g =>
            g.Key + ":" + string.Join(",", g.Select(i => $"{i.Name}/{RuntimeHelpers.GetHashCode(i.After!)}/{RuntimeHelpers.GetHashCode(i.Before!)}"))));
        if (!force && key == _fontsKey) return;
        _fontsKey = key;
        FontGroupsSource.Source = groups;
        FontPickList.ItemsSource = FontGroupsSource.View;
    }

    private void OnFontSearchChanged(object sender, TextChangedEventArgs e) => RefreshFonts(force: true);

    private void OnFontPickClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is FontPickItem item) FontPicked?.Invoke(this, item.Name);
    }

    private void OnFontAutoClick(object sender, RoutedEventArgs e) => FontPicked?.Invoke(this, null);

    // ------------------------------------------------------------ レイアウト: 選んだ行のページ（レイアウトと字幕アクションの指定）

    /// <summary>行リストで行を選び直した: ページの欄（レイアウト・字幕アクション）を、選んだ行のページに合わせる。</summary>
    public void SetLine(LineViewModel? line)
    {
        _line = line;
        RefreshPagePane();
    }

    /// <summary>
    /// ページの欄を今の歌詞・選択に合わせる（予約して、今の処理が終わってから行う。欄で選んで指定した直後の
    /// SelectionChanged の中から呼ばれても、その欄の選択を処理の途中で変えないように）。レイアウトのタブを出していなければ何もしない。
    /// </summary>
    public void RefreshPagePane()
    {
        if (_pageRefreshQueued) return;
        _pageRefreshQueued = true;
        bool queued = DispatcherQueue.TryEnqueue(() =>
        {
            _pageRefreshQueued = false;
            RefreshPagePaneNow();
        });
        if (!queued) _pageRefreshQueued = false;
    }

    private void RefreshPagePaneNow()
    {
        if (_vm is null || LayoutPane.Visibility != Visibility.Visible) return;
        var vm = _vm;
        var doc = vm.Document;
        _pageLoading = true;
        try
        {
            // レイアウトの欄の候補（ベース＋編集したレイアウト。書き出しと同じ並び）
            var names = vm.GetEffectiveLayouts().Select(l => l.Name).Where(n => n.Length > 0).Distinct().ToList();
            string key = string.Join("\n", names);
            if (_pageLayoutNamesKey != key)
            {
                _pageLayoutNamesKey = key;
                PageLayoutBox.Items.Clear();
                PageLayoutBox.Items.Add(AutoLayoutItem);
                foreach (string n in names) PageLayoutBox.Items.Add(n);
            }

            // 対象: 選んだ行（空行は除く。無ければ選んでいる 1 行）のページ（ページの数え方は書き出し・指定と同じ）
            var lines = (_selectedLines?.Invoke() ?? Array.Empty<int>())
                .Where(i => i >= 0 && i < doc.Lines.Count && !doc.Lines[i].IsEmpty)
                .ToList();
            var anchor = _line is { } current && current.Index >= 0 && current.Index < doc.Lines.Count
                && ReferenceEquals(doc.Lines[current.Index], current.Model) && !current.Model.IsEmpty ? current : null;
            if (lines.Count == 0 && anchor is not null) lines.Add(anchor.Index);
            if (lines.Count == 0)
            {
                ShowNoPage();
                return;
            }

            var pages = doc.GetPages(vm.Settings.PageMode, vm.Settings.FixedLineCount);
            var targetPages = new List<List<int>>();
            foreach (int i in lines)
            {
                var page = pages.FirstOrDefault(p => p.Contains(i)) ?? new List<int> { i };
                if (!targetPages.Any(p => p[0] == page[0])) targetPages.Add(page);
            }
            var targetLines = targetPages.SelectMany(p => p).Distinct().Where(k => !doc.Lines[k].IsEmpty).OrderBy(k => k).ToList();
            int first = anchor is not null && lines.Contains(anchor.Index) ? anchor.Index : lines[0];

            PageLayoutBox.IsEnabled = true;
            PageActionBox.IsEnabled = true;
            PageTargetText.Text = targetPages.Count == 1
                ? $"{first + 1} 行目のページ（{targetLines.Count} 行）に指定します"
                : $"選んだ {lines.Count} 行のページ（{targetPages.Count} ページ・{targetLines.Count} 行）に指定します";

            LoadPageLayout(targetPages, names, first);
            LoadPageAction(targetLines);
        }
        finally
        {
            _pageLoading = false;
        }
    }

    /// <summary>行を選んでいないとき（または空行だけのとき）のページの欄。</summary>
    private void ShowNoPage()
    {
        PageTargetText.Text = "行リストで行を選ぶと、その行のページのレイアウトと字幕アクションを指定できます";
        PageLayoutBox.SelectedIndex = -1;
        PageLayoutBox.PlaceholderText = AutoLayoutItem;
        PageLayoutBox.IsEnabled = false;
        PageActionBox.SelectedIndex = -1;
        PageActionBox.PlaceholderText = DefaultActionItem;
        PageActionBox.IsEnabled = false;
        PageActionDetail.Text = "";
        _pageLayoutName = null;
    }

    /// <summary>
    /// レイアウトの欄: ページの手動指定（ページの中で最初に、一覧にある名前を持つ行のもの。書き出しと同じ）がみな同じならその名前を選ぶ。
    /// 指定が無ければ薄字に自動で選ぶレイアウト、ページによって違えば「（混在）」。
    /// </summary>
    private void LoadPageLayout(List<List<int>> targetPages, List<string> names, int first)
    {
        var doc = _vm!.Document;
        var known = new HashSet<string>(names, StringComparer.Ordinal);
        var manuals = targetPages
            .Select(p => p.Select(k => doc.Lines[k].LayoutName).FirstOrDefault(n => n is { Length: > 0 } && known.Contains(n)))
            .ToList();
        // 自動のときに出すレイアウト（表示時刻の決まらない行（タイムタグの無い行など）は行リストのレイアウトの欄が空なので、決まる行のもの）
        var shown = targetPages.Select(p => p.Select(ShownLayoutName).FirstOrDefault(s => s.Length > 0) ?? "").ToList();
        int anchorPage = Math.Max(0, targetPages.FindIndex(p => p.Contains(first)));
        _pageLayoutName = manuals[anchorPage] ?? (shown[anchorPage] is { Length: > 0 } s ? s : null);

        if (manuals.All(m => m is not null) && manuals.Distinct().Count() == 1)
        {
            PageLayoutBox.SelectedIndex = PageLayoutBox.Items.IndexOf(manuals[0]!);
            PageLayoutBox.PlaceholderText = AutoLayoutItem;
        }
        else if (manuals.All(m => m is null))
        {
            PageLayoutBox.SelectedIndex = -1;
            PageLayoutBox.PlaceholderText = shown.Distinct().Count() == 1 && shown[0] is { Length: > 0 } auto ? $"自動: {auto}" : AutoLayoutItem;
        }
        else
        {
            PageLayoutBox.SelectedIndex = -1;
            PageLayoutBox.PlaceholderText = "（混在）";
        }
    }

    /// <summary>行のページで今使っているレイアウト設定の名前（行リストのレイアウトの欄から ✎ と「（文字 +4）」を除いたもの。決まらない行は空）。</summary>
    private string ShownLayoutName(int index)
    {
        var vm = _vm!;
        if (index < 0 || index >= vm.Lines.Count) return "";
        string text = vm.Lines[index].LayoutText.TrimStart('✎');
        int delta = vm.PageFontSizeDelta(index);
        string suffix = $"（文字 {N3PageFontSize.Signed(delta)}）";
        return delta != 0 && text.EndsWith(suffix, StringComparison.Ordinal) ? text[..^suffix.Length] : text;
    }

    /// <summary>
    /// 字幕アクションの欄: ページの行の指定がみな同じならその種類を選ぶ（知らない Id は薄字に「そのほか（…）」）。
    /// 指定が無ければ薄字に曲の既定、行によって違えば「（混在）」。
    /// </summary>
    private void LoadPageAction(List<int> targetLines)
    {
        var vm = _vm!;
        var actions = targetLines.Select(k => vm.Document.Lines[k].SubtitleAction).ToList();
        if (actions.All(a => a is null))
        {
            var def = vm.ResolveCurrentDefaultSubtitleAction(out var source);
            string name = N3SubtitleActionCatalog.Describe(def);
            PageActionBox.SelectedIndex = -1;
            PageActionBox.PlaceholderText = $"既定: {N3SubtitleActionCatalog.DisplayName(def.Id)}";
            PageActionDetail.Text = $"曲の既定の字幕アクション: {name}（{MainViewModel.DefaultSubtitleActionSourceLabel(source)}）";
        }
        else if (actions[0] is { } a0 && actions.All(a => a0.SameAs(a)))
        {
            int kind = N3SubtitleActionCatalog.Known.ToList().FindIndex(k => k.Id == a0.Id);
            PageActionBox.SelectedIndex = kind >= 0 ? kind + 1 : -1;
            PageActionBox.PlaceholderText = kind >= 0 ? DefaultActionItem : N3SubtitleActionCatalog.DisplayName(a0.Id);
            PageActionDetail.Text = $"手で指定した字幕アクション: {N3SubtitleActionCatalog.Describe(a0)}";
        }
        else
        {
            PageActionBox.SelectedIndex = -1;
            PageActionBox.PlaceholderText = "（混在）";
            var kinds = actions.Select(a => a is null ? DefaultActionItem : N3SubtitleActionCatalog.DisplayName(a.Id)).Distinct().ToList();
            PageActionDetail.Text = $"行によって違います（{string.Join("・", kinds.Take(4))}{(kinds.Count > 4 ? " など" : "")}）。選ぶと、ページの行すべてをそろえます";
        }
    }

    private void OnPageLayoutChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_pageLoading || PageLayoutBox.SelectedItem is not string choice) return;
        PageLayoutPicked?.Invoke(this, choice == AutoLayoutItem ? null : choice);
        RefreshPagePane(); // 指定が変わらなかったときも、欄を今の指定に戻す
    }

    private void OnPageActionChanged(object sender, SelectionChangedEventArgs e)
    {
        int index = PageActionBox.SelectedIndex;
        if (_pageLoading || index < 0) return;
        PageActionPicked?.Invoke(this, index == 0 ? null : N3SubtitleActionCatalog.Known[index - 1].Id);
        RefreshPagePane();
    }

    private void OnEditLayoutsClick(object sender, RoutedEventArgs e) => EditLayoutsRequested?.Invoke(this, _pageLayoutName);

    // ------------------------------------------------------------ 表示時刻（自動調整のパラメーターと実行）

    /// <summary>表示時刻のパラメーターを変えた（設定は保存済み。メイン画面はプレビュー・行設定・チェックを作り直す）。</summary>
    public event EventHandler? ShowTimeSettingsChanged;

    /// <summary>「自動調整を実行」を押した。</summary>
    public event EventHandler? AutoShowTimeRequested;

    /// <summary>表示時刻のタブを出しているか。</summary>
    public bool IsShowTimePaneVisible => ShowTimePane.Visibility == Visibility.Visible;

    /// <summary>表示時刻のタブの欄を設定の値に合わせ、行数の説明を作り直す。</summary>
    public void RefreshShowTimePane()
    {
        if (_vm is null) return;
        var s = _vm.Settings;
        _loading = true;
        try
        {
            StLeadBox.Value = s.DisplayLeadSeconds;
            StTailBox.Value = s.DisplayTailSeconds;
            StIntervalBox.Value = s.N3IntervalSeconds;
            StProtectBox.Value = s.N3ProtectSeconds;
            StOverlapBox.Value = s.N3OverlapSeconds;
            StTopLongCheck.IsChecked = s.N3TopLong;
            StEmojiYieldCheck.IsChecked = s.N3EmojiLeadYield;
            StLayoutAwareCheck.IsChecked = s.N3LayoutAwareRows;
            if (_vm.Nkm3Env is { PreTimeMs: not null } env)
            {
                StImportNkm3Button.Visibility = Visibility.Visible;
                StImportNkm3Text.Text = $"ニコカラメーカーの設定値を取り込む（ワイプ前 {env.PreTimeMs / 1000.0:0.##} 秒・ワイプ後 {env.PostTimeMs / 1000.0:0.##} 秒・表示間隔 {env.IntervalMs / 1000.0:0.##} 秒）";
            }
        }
        finally
        {
            _loading = false;
        }
        RefreshShowTimeSummary();
    }

    /// <summary>表示中のタブの、表示時刻の出どころごとの行数と、実行し直すと変わる行の数を出す。</summary>
    public void RefreshShowTimeSummary()
    {
        if (_vm is null) return;
        var (c, outdated) = _vm.ShowTimeSummary();
        var parts = new List<string>();
        if (c.Manual > 0) parts.Add($"手で直した {c.Manual} 行");
        if (c.Loaded > 0) parts.Add($"読み込んだ {c.Loaded} 行");
        if (c.Auto > 0) parts.Add($"自動調整の {c.Auto} 行");
        if (c.Live > 0) parts.Add($"未設定（自動で計算）{c.Live} 行");
        string text = parts.Count == 0 ? "表示時刻を決められる行がありません" : "表示中のタブ: " + string.Join("・", parts);
        if (outdated > 0) text += $"\n今のパラメーターで実行し直すと {outdated} 行が変わります";
        ShowTimeSummaryText.Text = text;
    }

    private void OnShowTimeNumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || _vm is null) return;
        if (double.IsNaN(args.NewValue))
        {
            RefreshShowTimePane(); // 空にした欄は元の値に戻す
            return;
        }
        var s = _vm.Settings;
        double v = Math.Max(0, args.NewValue);
        if (ReferenceEquals(sender, StLeadBox)) s.DisplayLeadSeconds = v;
        else if (ReferenceEquals(sender, StTailBox)) s.DisplayTailSeconds = v;
        else if (ReferenceEquals(sender, StIntervalBox)) s.N3IntervalSeconds = v;
        else if (ReferenceEquals(sender, StProtectBox)) s.N3ProtectSeconds = v;
        else if (ReferenceEquals(sender, StOverlapBox)) s.N3OverlapSeconds = v;
        s.Save();
        ShowTimeSettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnShowTimeCheckClick(object sender, RoutedEventArgs e)
    {
        if (_loading || _vm is null) return;
        var s = _vm.Settings;
        if (ReferenceEquals(sender, StTopLongCheck)) s.N3TopLong = StTopLongCheck.IsChecked == true;
        else if (ReferenceEquals(sender, StEmojiYieldCheck)) s.N3EmojiLeadYield = StEmojiYieldCheck.IsChecked == true;
        else if (ReferenceEquals(sender, StLayoutAwareCheck)) s.N3LayoutAwareRows = StLayoutAwareCheck.IsChecked == true;
        s.Save();
        ShowTimeSettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnStImportNkm3Click(object sender, RoutedEventArgs e)
    {
        if (_vm?.Nkm3Env is not { } env) return;
        var s = _vm.Settings;
        if (env.PreTimeMs is int pre) s.DisplayLeadSeconds = pre / 1000.0;
        if (env.PostTimeMs is int post) s.DisplayTailSeconds = post / 1000.0;
        if (env.IntervalMs is int interval) s.N3IntervalSeconds = interval / 1000.0;
        s.Save();
        RefreshShowTimePane();
        ShowTimeSettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnRunAutoShowTimeClick(object sender, RoutedEventArgs e) => AutoShowTimeRequested?.Invoke(this, EventArgs.Empty);
}
