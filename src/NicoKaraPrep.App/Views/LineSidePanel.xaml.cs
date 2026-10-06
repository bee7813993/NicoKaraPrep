using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NicoKaraPrep.App.ViewModels;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.Views;

/// <summary>
/// 行リストのときの右のパネル。どのタブも、行リストで行（文字）を選んでから一覧の項目を押して指定する。
/// 「フォント」: 押したフォント設定を、選んだ文字（文字を選んでいなければ選んだ行）に指定する。
/// 「レイアウト」: 押したレイアウト設定を、選んだ行のページに指定する（レイアウト設定の値はレイアウト設定ビュー（F4）で編集する）。
/// 「字幕アクション」: 押した字幕アクションを、選んだ行（「ページの行すべてにそろえる」ならページ）に指定する（曲の既定はポップアップで編集する）。
/// </summary>
public sealed partial class LineSidePanel : UserControl
{
    /// <summary>レイアウトの一覧の「自動」の項目。</summary>
    private const string AutoLayoutItem = "（自動）";

    /// <summary>字幕アクションの一覧の「既定」の項目（続けて <see cref="N3SubtitleActionCatalog.Known"/> の順に並べる）。</summary>
    private const string DefaultActionItem = "（既定）";

    private MainViewModel? _vm;

    /// <summary>行リストで選んでいる行（ページの欄の対象。選んだ行が無ければ null）。</summary>
    private LineViewModel? _line;

    /// <summary>行リストで選んでいる行の添字（表示中のタブ。メイン画面から受け取る）。</summary>
    private Func<IReadOnlyList<int>>? _selectedLines;

    private string _fontsKey = "";

    /// <summary>「レイアウト」タブの一覧の作り直しを予約済みか。</summary>
    private bool _pageRefreshQueued;

    /// <summary>選んだ行のページで今使っているレイアウト設定の名前（「レイアウト設定を編集...」で選ぶ。分からなければ null）。</summary>
    private string? _pageLayoutName;

    public LineSidePanel()
    {
        InitializeComponent();
    }

    /// <summary>フォント設定を押した（null は「自動に戻す」）。</summary>
    public event EventHandler<string?>? FontPicked;

    /// <summary>レイアウトの一覧を押した（レイアウト設定名。null は「（自動）」）。選んだ行のページに指定する。</summary>
    public event EventHandler<string?>? PageLayoutPicked;

    /// <summary>字幕アクションの一覧を押した（Id。null は「（既定）」）。選んだ行（「ページの行すべてにそろえる」ならページ）に指定する。</summary>
    public event EventHandler<SideActionPick>? PageActionPicked;

    /// <summary>「レイアウト設定を編集...」を押した（選んだ行のページで使っているレイアウト設定の名前。分からなければ null）。</summary>
    public event EventHandler<string?>? EditLayoutsRequested;

    /// <summary>字幕アクションのタブの「曲の既定を設定...」を押した。</summary>
    public event EventHandler? SongActionRequested;

    /// <summary>メイン画面の ViewModel と、行リストで選んでいる行の添字を返す処理とつなぐ（起動時に 1 回）。</summary>
    public void Initialize(MainViewModel vm, Func<IReadOnlyList<int>> selectedLines)
    {
        _vm = vm;
        _selectedLines = selectedLines;
        PageActionWholePageCheck.IsChecked = vm.Settings.SideActionWholePage;
        // レイアウト設定の一覧（名前）・ページの指定・曲の既定の字幕アクションが変わったら、ページの欄を合わせる
        vm.LayoutsChanged += (_, _) => RefreshPagePane();
        vm.LineFontsUpdated += (_, _) => RefreshPagePane();
        vm.SongDefaultSubtitleActionChanged += (_, _) => RefreshPagePane();
        vm.PreviewModelChanged += (_, _) => RefreshFonts();
        RefreshFonts(force: true);
    }

    /// <summary>フォントを押したときに指定する先の説明（「10 行目の選んだ 3 文字に指定します」など）。</summary>
    public void SetTarget(string text) => FontTargetText.Text = text;

    private void OnPaneChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        bool layout = ReferenceEquals(sender.SelectedItem, LayoutPaneItem);
        bool action = ReferenceEquals(sender.SelectedItem, ActionPaneItem);
        FontPane.Visibility = layout || action ? Visibility.Collapsed : Visibility.Visible;
        LayoutPane.Visibility = layout ? Visibility.Visible : Visibility.Collapsed;
        ActionPane.Visibility = action ? Visibility.Visible : Visibility.Collapsed;
        if (layout || action) RefreshPagePane();
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

    // ------------------------------------------------------------ レイアウト・字幕アクション: 押した項目を、選んだ行（ページ）に指定する

    /// <summary>行リストで行を選び直した: 一覧の印（✓）と説明を、選んだ行に合わせる。</summary>
    public void SetLine(LineViewModel? line)
    {
        _line = line;
        RefreshPagePane();
    }

    /// <summary>
    /// 一覧の印と説明を今の歌詞・選択に合わせる（予約して、今の処理が終わってから行う。一覧を押した直後の ItemClick の中から呼ばれても、
    /// その一覧の項目を処理の途中で入れ替えないように）。レイアウト・字幕アクションのタブを出していなければ何もしない。
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
        if (_vm is null || (LayoutPane.Visibility != Visibility.Visible && ActionPane.Visibility != Visibility.Visible)) return;
        var vm = _vm;
        var doc = vm.Document;

        // 対象: 選んだ行（空行は除く。無ければ選んでいる 1 行）と、そのページ（ページの数え方は書き出し・指定と同じ）
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
        var pageLines = targetPages.SelectMany(p => p).Distinct().Where(k => !doc.Lines[k].IsEmpty).OrderBy(k => k).ToList();
        int first = anchor is not null && lines.Contains(anchor.Index) ? anchor.Index : lines[0];

        PageLayoutList.IsEnabled = true;
        PageActionList.IsEnabled = true;
        string pageTarget = targetPages.Count == 1
            ? $"{first + 1} 行目のページ（{pageLines.Count} 行）に指定します"
            : $"選んだ {lines.Count} 行のページ（{targetPages.Count} ページ・{pageLines.Count} 行）に指定します";
        LayoutTargetText.Text = pageTarget;
        ActionTargetText.Text = PageActionWholePageCheck.IsChecked == true
            ? pageTarget
            : lines.Count == 1 ? $"{first + 1} 行目に指定します" : $"選んだ {lines.Count} 行に指定します";

        if (LayoutPane.Visibility == Visibility.Visible) LoadPageLayouts(targetPages, first);
        if (ActionPane.Visibility == Visibility.Visible) LoadPageActions(lines, pageLines);
    }

    /// <summary>行を選んでいないとき: 一覧は出したまま押せなくする（印は付けない）。</summary>
    private void ShowNoPage()
    {
        var vm = _vm!;
        LayoutTargetText.Text = "行リストで行を選んでから、レイアウト設定を押してください";
        ActionTargetText.Text = "行リストで行を選んでから、字幕アクションを押してください";
        PageLayoutList.ItemsSource = new List<SidePickItem> { new(null, AutoLayoutItem, "ページの行数から選ぶ", false, "") }
            .Concat(DistinctLayouts(vm).Select(l => new SidePickItem(l.Name, l.Name, LayoutTexts.Summary(l), false, ""))).ToList();
        PageActionList.ItemsSource = new List<SidePickItem> { new(null, DefaultActionItem, "曲の既定", false, "") }
            .Concat(N3SubtitleActionCatalog.Known.Select(k => new SidePickItem(k.Id, k.Name, MotionText(k.Id), false, ""))).ToList();
        PageLayoutList.IsEnabled = false;
        PageActionList.IsEnabled = false;
        PageLayoutNote.Text = "";
        PageActionNote.Text = "";
        ShowSongAction();
        _pageLayoutName = null;
    }

    /// <summary>レイアウト設定の一覧（書き出しと同じ並び。名前の重なりと空の名前は除く）。</summary>
    private static List<Core.Formats.N3LayoutSettings> DistinctLayouts(MainViewModel vm) =>
        vm.GetEffectiveLayouts().Where(l => l.Name.Length > 0).GroupBy(l => l.Name, StringComparer.Ordinal).Select(g => g.First()).ToList();

    /// <summary>
    /// レイアウトの一覧: 「（自動）」と各レイアウト設定。ページの手動指定（ページの中で最初に、一覧にある名前を持つ行のもの。書き出しと同じ）が
    /// みな同じならその行に ✓、指定が無ければ「（自動）」に ✓（自動で使っているレイアウトも添える）、ページによって違えば印なし。
    /// </summary>
    private void LoadPageLayouts(List<List<int>> targetPages, int first)
    {
        var vm = _vm!;
        var doc = vm.Document;
        var layouts = DistinctLayouts(vm);
        var known = new HashSet<string>(layouts.Select(l => l.Name), StringComparer.Ordinal);
        var manuals = targetPages
            .Select(p => p.Select(k => doc.Lines[k].LayoutName).FirstOrDefault(n => n is { Length: > 0 } && known.Contains(n)))
            .ToList();
        // 自動のときに使っているレイアウト（表示時刻の決まらない行（タイムタグの無い行など）は行リストのレイアウトの欄が空なので、決まる行のもの）
        var shown = targetPages.Select(p => p.Select(ShownLayoutName).FirstOrDefault(s => s.Length > 0) ?? "").ToList();
        int anchorPage = Math.Max(0, targetPages.FindIndex(p => p.Contains(first)));
        _pageLayoutName = manuals[anchorPage] ?? (shown[anchorPage] is { Length: > 0 } s ? s : null);

        bool allAuto = manuals.All(m => m is null);
        string? sameManual = manuals.All(m => m is not null) && manuals.Distinct().Count() == 1 ? manuals[0] : null;
        string autoNow = allAuto && shown.Distinct().Count() == 1 ? shown[0] : "";

        var items = new List<SidePickItem>
        {
            new(null, AutoLayoutItem, autoNow.Length > 0 ? $"今は {autoNow}" : "ページの行数から選ぶ", allAuto,
                "手で指定したレイアウトを外して、ページの行数から選ぶようにします（書き出しと同じ決め方）"),
        };
        foreach (var l in layouts)
        {
            string summary = LayoutTexts.Summary(l);
            items.Add(new SidePickItem(l.Name, l.Name, autoNow == l.Name ? $"{summary}・自動で使用中" : summary, sameManual == l.Name,
                $"{l.Name}（{summary}）\n押すと、選んだ行のページ（同じページの行すべて）に指定します"));
        }
        PageLayoutList.ItemsSource = items;
        PageLayoutNote.Text = sameManual is not null
            ? $"「{sameManual}」を指定しています。押すと、選んだ行のページに指定します"
            : allAuto
                ? "押すと、選んだ行のページに指定します（同じページの行すべて）"
                : "ページによって指定が違います。押すと、選んだ行のページをそろえます";
    }

    /// <summary>
    /// 字幕アクションの一覧: 「（既定）」と 8 種類。対象の行（「ページの行すべてにそろえる」ならページの行）の指定がみな同じ種類ならその行に ✓、
    /// 指定が無ければ「（既定）」に ✓、行によって違えば印なし。
    /// </summary>
    private void LoadPageActions(List<int> lines, List<int> pageLines)
    {
        var vm = _vm!;
        var doc = vm.Document;
        bool whole = PageActionWholePageCheck.IsChecked == true;
        var target = whole ? pageLines : lines;
        var actions = target.Select(k => doc.Lines[k].SubtitleAction).ToList();
        var def = vm.ResolveCurrentDefaultSubtitleAction(out var source);
        bool allDefault = actions.All(a => a is null);
        string? sameId = actions.Count > 0 && actions[0] is { } a0 && actions.All(a => a is not null && a.Id == a0.Id) ? a0.Id : null;
        string where = whole ? "選んだ行のページの行すべて" : "選んだ行（ページのほかの行はそのまま）";

        var items = new List<SidePickItem>
        {
            new(null, DefaultActionItem, $"曲の既定: {N3SubtitleActionCatalog.DisplayName(def.Id)}", allDefault,
                $"手で指定した字幕アクションを外して、曲の既定に従わせます（{where}）\n" +
                $"曲の既定: {vm.DescribeSubtitleAction(def)}（{MainViewModel.DefaultSubtitleActionSourceLabel(source)}）。曲の既定は下の「曲の既定を設定...」で変えられます"),
        };
        foreach (var kind in N3SubtitleActionCatalog.Known)
        {
            bool current = sameId == kind.Id;
            string now = current && actions[0] is { } a ? $"\n今の指定: {vm.DescribeSubtitleAction(a)}" : "";
            items.Add(new SidePickItem(kind.Id, kind.Name, MotionText(kind.Id), current,
                $"{kind.Name}: {MotionText(kind.Id)}{now}\n押すと、{where}に指定します（設定値は、曲の既定と同じ種類なら曲の既定の値、違う種類ならその種類の既定値）"));
        }
        PageActionList.ItemsSource = items;

        ShowSongAction();
        int manual = actions.Count(a => a is not null);
        string state = allDefault ? "今は曲の既定です"
            : sameId is not null ? (N3SubtitleActionCatalog.IsKnown(sameId) ? "" : $"今は {N3SubtitleActionCatalog.DisplayName(sameId)} です")
            : $"行によって違います（手で指定 {manual} 行・既定 {actions.Count - manual} 行）";
        string target2 = whole ? $"押すと、選んだ行のページ（{pageLines.Count} 行）の行すべてに指定します" : $"押すと、選んだ {lines.Count} 行に指定します（ページのほかの行はそのまま）";
        PageActionNote.Text = state.Length > 0 ? $"{target2}。{state}" : target2;
    }

    /// <summary>字幕アクションのタブの下に、曲の既定（行ごとに指定していない行に使うもの）を出す。</summary>
    private void ShowSongAction()
    {
        var vm = _vm!;
        var def = vm.ResolveCurrentDefaultSubtitleAction(out var source);
        SongActionText.Text = $"曲の既定: {vm.DescribeSubtitleAction(def)}（{MainViewModel.DefaultSubtitleActionSourceLabel(source)}）";
    }

    private void OnSongActionClick(object sender, RoutedEventArgs e) => SongActionRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>字幕アクションの動き（ニコカラメーカー3 のヘルプの説明から。一覧の説明に出す）。</summary>
    private static string MotionText(string id) => id switch
    {
        N3SubtitleActionCatalog.NoActionId => "動かさずに出して消す",
        N3SubtitleActionCatalog.LineFadeInId => "行全体をフェードイン",
        N3SubtitleActionCatalog.LineFadeOutId => "行全体をフェードアウト",
        N3SubtitleActionCatalog.LineFadeInFadeOutId => "行全体をフェードイン・アウト",
        N3SubtitleActionCatalog.CharFadeInFadeOutId => "文字ごとにずらしてフェード",
        N3SubtitleActionCatalog.SpinFlipId => "文字ごとにスピンしながらフリップ",
        N3SubtitleActionCatalog.UtopiaId => "弾むように文字の大きさが変わる",
        N3SubtitleActionCatalog.SlideUpDownId => "行全体を上か下へスライド",
        _ => "",
    };

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

    private void OnPageLayoutClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not SidePickItem item) return;
        PageLayoutPicked?.Invoke(this, item.Key);
        RefreshPagePane(); // 指定が変わらなかったときも、印を今の指定に合わせる
    }

    private void OnPageActionClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not SidePickItem item) return;
        PageActionPicked?.Invoke(this, new SideActionPick(item.Key, PageActionWholePageCheck.IsChecked == true));
        RefreshPagePane();
    }

    /// <summary>「ページの行すべてにそろえる」を変えた（アプリ共通に覚える）。</summary>
    private void OnPageActionWholePageClick(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        _vm.Settings.SideActionWholePage = PageActionWholePageCheck.IsChecked == true;
        _vm.Settings.Save();
        RefreshPagePane();
    }

    private void OnEditLayoutsClick(object sender, RoutedEventArgs e) => EditLayoutsRequested?.Invoke(this, _pageLayoutName);
}

/// <summary>右パネル「レイアウト」「字幕アクション」タブの一覧の 1 行。</summary>
public sealed class SidePickItem
{
    public SidePickItem(string? key, string name, string detail, bool isCurrent, string toolTip)
    {
        Key = key;
        Name = name;
        Detail = detail;
        IsCurrent = isCurrent;
        ToolTip = toolTip;
    }

    /// <summary>指定する値（レイアウト設定名・字幕アクションの Id。null は「（自動）」「（既定）」）。</summary>
    public string? Key { get; }

    public string Name { get; }

    /// <summary>名前の右に薄く出す説明（「下寄せ・2 行」「行全体をフェードイン」など）。</summary>
    public string Detail { get; }

    /// <summary>選んだ行（ページ）の今の指定か（✓ を付ける）。</summary>
    public bool IsCurrent { get; }

    public string Mark => IsCurrent ? "✓" : "";

    public string ToolTip { get; }

    /// <summary>読み上げ・UI オートメーションでの名前（今の指定には「✓ 」を付ける）。</summary>
    public override string ToString() => IsCurrent ? $"✓ {Name}" : Name;
}

/// <summary>字幕アクションの一覧で押したもの（Id。null は「（既定）」）と、ページの行すべてにそろえるか。</summary>
public sealed record SideActionPick(string? Id, bool WholePage);
