using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NicoKaraPrep.App.Services.Subtitles;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.ViewModels;

/// <summary>
/// レイアウト設定ビューの右のページの一覧（全タブ。書き出しと同じページの数え方）と、選んだページへの指定（レイアウト・字幕アクション）、
/// 中央の「使っているページ」、見本。ページへの指定は歌詞の変更（タブごとの 元に戻す の履歴に積む）。
/// </summary>
public sealed partial class LayoutViewModel
{
    /// <summary>ページの一覧（タブの順・ページの順）。</summary>
    public ObservableCollection<LayoutPageItem> Pages { get; } = new();

    /// <summary>選んだレイアウトを使っているページ（中央の「使っているページ」。<see cref="Pages"/> と同じ行のオブジェクト）。</summary>
    public ObservableCollection<LayoutPageItem> UsagePages { get; } = new();

    /// <summary>ページの一覧を作り直している最中か（ListView の選択の変化を選択の操作として扱わない）。</summary>
    public bool IsRebuildingPages { get; private set; }

    private List<LayoutPageItem> _selectedPages = new();

    /// <summary>ページの一覧で選んでいるページ。</summary>
    public IReadOnlyList<LayoutPageItem> SelectedPages => _selectedPages;

    /// <summary>ページの字幕アクションの選択肢（（既定）＋ 8 種類）。</summary>
    public IReadOnlyList<LayoutActionChoice> PageActionChoices { get; }

    /// <summary>ページの字幕アクションのコンボで選んでいるもの（<see cref="PageActionChoices"/> の番号）。</summary>
    [ObservableProperty]
    private int pageActionIndex;

    [ObservableProperty]
    private string pageSummary = "";

    [ObservableProperty]
    private string pageTargetText = "";

    [ObservableProperty]
    private bool hasPageSelection;

    /// <summary>「選んだレイアウトをこのページに」を押せるか（ページとレイアウトを選んでいる）。</summary>
    [ObservableProperty]
    private bool canApplyLayout;

    [ObservableProperty]
    private string applyLayoutToolTip = "";

    [ObservableProperty]
    private bool hasUsage;

    [ObservableProperty]
    private string usageSummary = "";

    /// <summary>
    /// ページの一覧を作り直す。ページ（タブの名前とページの番号）の並びが同じなら行を使い回し（一覧の選択を保つ）、違えば作り直して
    /// 同じページを選び直す。
    /// </summary>
    private void RefreshPages(List<LayoutPageInfo> infos)
    {
        var songDefault = _main.ResolveSongSubtitleAction(out _);
        var keys = infos.Select(i => LayoutPageItem.PageKey(i.TabName, i.PageIndex)).ToList();
        if (Pages.Select(p => p.Key).SequenceEqual(keys))
        {
            for (int i = 0; i < infos.Count; i++) Pages[i].Update(infos[i], songDefault);
        }
        else
        {
            var selectedKeys = new HashSet<string>(_selectedPages.Select(p => p.Key), StringComparer.Ordinal);
            IsRebuildingPages = true;
            try
            {
                Pages.Clear();
                foreach (var info in infos) Pages.Add(new LayoutPageItem(info, songDefault));
            }
            finally
            {
                IsRebuildingPages = false;
            }
            SelectPages(Pages.Where(p => selectedKeys.Contains(p.Key)).ToList());
        }

        int tabs = infos.Select(i => i.TabName).Distinct().Count();
        int manualLayouts = infos.Count(i => i.LayoutChoice.Source == N3PageLayoutSource.Manual);
        int manualActions = infos.Count(i => i.Action.State != N3PageActionState.Default);
        PageSummary = infos.Count == 0
            ? "歌詞のページがありません"
            : $"全 {infos.Count} ページ（{tabs} タブ）・レイアウトの手動指定 {manualLayouts} ページ・字幕アクションの指定 {manualActions} ページ";
    }

    /// <summary>ページの一覧でページを選ぶ（ビューに一覧の選択を合わせるよう知らせる）。</summary>
    public void SelectPages(IReadOnlyList<LayoutPageItem> pages)
    {
        _selectedPages = pages.Where(Pages.Contains).ToList();
        UpdatePageTargets();
        PagesSelectRequested?.Invoke(this, _selectedPages);
        PreviewChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>ページの一覧で選んだページ（ビューの一覧の選択が変わったとき）。</summary>
    public void SetSelectedPages(IReadOnlyList<LayoutPageItem> pages)
    {
        _selectedPages = pages.ToList();
        UpdatePageTargets();
        PreviewChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>選んだページへの操作のボタン・説明の状態。</summary>
    private void UpdatePageTargets()
    {
        int count = _selectedPages.Count;
        HasPageSelection = count > 0;
        CanApplyLayout = count > 0 && SelectedLayout is not null;
        int lines = _selectedPages.Sum(p => p.Info.Lines.Count);
        PageTargetText = count == 0
            ? "ページの一覧でページを選ぶと、レイアウト・字幕アクションを指定できます（Ctrl・Shift を押しながら押すと複数選べます）"
            : $"選んだページ: {_selectedPages[0].Title}{(count > 1 ? $" ほか {count - 1} ページ" : "")}（{lines} 行）";
        ApplyLayoutToolTip = SelectedLayout is { } layout
            ? $"選んだページに、レイアウト設定「{layout.Name}」を手動指定します（ページの行すべてに書きます。行リストへ戻って Ctrl+Z で戻せます）"
            : "左の一覧でレイアウト設定を選んでください";
    }

    /// <summary>中央の「使っているページ」（選んだレイアウトのページ）を作り直す。</summary>
    private void UpdateUsage()
    {
        UpdatePageTargets();
        UsagePages.Clear();
        if (SelectedLayout is not { } item)
        {
            HasUsage = false;
            UsageSummary = "";
            return;
        }
        foreach (var page in Pages.Where(p => p.Info.LayoutName == item.Name)) UsagePages.Add(page);
        HasUsage = UsagePages.Count > 0;
        if (!HasUsage)
        {
            UsageSummary = $"「{item.Name}」を使っているページはありません";
            return;
        }
        var bySource = UsagePages.GroupBy(p => p.Info.LayoutChoice.Source).ToDictionary(g => g.Key, g => g.Count());
        var parts = new List<string>();
        if (bySource.GetValueOrDefault(N3PageLayoutSource.Manual) is int manual and > 0) parts.Add($"手動 {manual}");
        if (bySource.GetValueOrDefault(N3PageLayoutSource.TabFixed) is int tabFixed and > 0) parts.Add($"タブ固定 {tabFixed}");
        if (bySource.GetValueOrDefault(N3PageLayoutSource.Auto) is int auto and > 0) parts.Add($"自動 {auto}");
        UsageSummary = $"「{item.Name}」を使っているページ: {UsagePages.Count} ページ（{string.Join("・", parts)}）。押すと右のページの一覧で選びます";
    }

    // ------------------------------------------------------------ ページへの指定

    /// <summary>「選んだレイアウトをこのページに」。</summary>
    public void ApplyLayoutToSelectedPages()
    {
        if (SelectedLayout is { } layout) ApplyLayout(layout.Name);
    }

    /// <summary>「レイアウトを自動に戻す」。</summary>
    public void ResetLayoutOfSelectedPages() => ApplyLayout(null);

    private void ApplyLayout(string? name)
    {
        var pages = _selectedPages.ToList();
        if (pages.Count == 0) return;
        var (lines, otherTab) = WritePages(pages, (tab, indexes) => _main.SetPageLinesLayout(tab, indexes, name));
        if (lines == 0)
        {
            SetStatus(name is null ? "選んだページにはレイアウトの指定がありません" : $"選んだページは、もうレイアウト設定「{name}」を指定しています");
            return;
        }
        SetStatus(name is null
            ? $"選んだ {pages.Count} ページ（{lines} 行）のレイアウトの指定を自動に戻しました（{UndoHint(otherTab)}）"
            : $"選んだ {pages.Count} ページ（{lines} 行）にレイアウト設定「{name}」を指定しました（{UndoHint(otherTab)}）");
    }

    /// <summary>コンボで選んだ字幕アクションを、選んだページに指定する（（既定）なら既定に戻す）。</summary>
    public void ApplyActionToSelectedPages()
    {
        var choice = PageActionChoices[Math.Clamp(PageActionIndex, 0, PageActionChoices.Count - 1)];
        ApplyAction(choice.Id is null ? null : ActionFor(choice.Id));
    }

    /// <summary>「アクションを既定に戻す」。</summary>
    public void ResetActionOfSelectedPages() => ApplyAction(null);

    private void ApplyAction(N3SubtitleAction? action)
    {
        var pages = _selectedPages.ToList();
        if (pages.Count == 0) return;
        var (lines, otherTab) = WritePages(pages, (tab, indexes) => _main.SetPageLinesSubtitleAction(tab, indexes, action));
        string name = N3SubtitleActionCatalog.Describe(action);
        if (lines == 0)
        {
            SetStatus(action is null ? "選んだページには字幕アクションの指定がありません" : $"選んだページは、もう字幕アクション「{name}」を指定しています");
            return;
        }
        SetStatus(action is null
            ? $"選んだ {pages.Count} ページ（{lines} 行）の字幕アクションを曲の既定に戻しました（{UndoHint(otherTab)}）"
            : $"選んだ {pages.Count} ページ（{lines} 行）に字幕アクション「{name}」を指定しました（{UndoHint(otherTab)}）");
    }

    /// <summary>
    /// ページの字幕アクションに使うもの。曲の既定と同じ種類ならその値（曲の既定の値のままページに固定する）、違えばニコカラメーカー3 の設定の値
    /// （AddOns。無ければ既定値）。
    /// </summary>
    private N3SubtitleAction ActionFor(string id)
    {
        var song = _main.ResolveSongSubtitleAction(out _);
        return song.Id == id ? song : N3SubtitleActionCatalog.CreateDefault(id, AddOnDefaults(id));
    }

    /// <summary>
    /// ページの行へ書く（タブごとに 1 回。表示中でないタブはタブを切り替えずに書く）。書いたら行リストの表示・字幕のプレビュー・一覧を作り直す。
    /// 変えた行の数と、表示中でないタブを変えたかを返す。
    /// </summary>
    private (int Lines, bool OtherTab) WritePages(IReadOnlyList<LayoutPageItem> pages, Func<TabState, IReadOnlyList<int>, int> write)
    {
        int lines = 0;
        bool otherTab = false;
        try
        {
            foreach (var group in pages.GroupBy(p => p.Info.Tab))
            {
                int n = write(group.Key, group.SelectMany(p => p.Info.Lines).ToList());
                lines += n;
                if (n > 0 && !ReferenceEquals(group.Key, _main.ActiveTab)) otherTab = true;
            }
        }
        catch (Exception ex)
        {
            SetStatus($"エラー: ページへ指定できませんでした（{ex.Message}）");
        }
        if (lines > 0)
        {
            try
            {
                // 行リストのレイアウトの表示・手動指定の印と字幕のプレビューを作り直す（チェックはビューを抜けるときにメイン画面が予約する）
                _main.UpdateLineFonts();
                foreach (var line in _main.Lines) line.RaiseOverrideMark();
            }
            catch (Exception ex)
            {
                SetStatus($"エラー: 行リストの表示を作り直せませんでした（{ex.Message}）");
            }
            Refresh(null);
        }
        return (lines, otherTab);
    }

    /// <summary>ページへの指定の戻し方の説明。</summary>
    private static string UndoHint(bool otherTab) => otherTab
        ? "表示中のタブは行リストへ戻って Ctrl+Z、ほかのタブはそのタブを表示して Ctrl+Z で戻せます"
        : "行リストへ戻って Ctrl+Z で戻せます";

    // ------------------------------------------------------------ 見本

    /// <summary>見本に出しているページ（選んだページ → 選んだレイアウトを使っているページ → 最初のページ）。</summary>
    private LayoutPageItem? PreviewPage => _selectedPages.FirstOrDefault() ?? UsagePages.FirstOrDefault() ?? Pages.FirstOrDefault();

    /// <summary>
    /// 見本の材料: 見本のページの歌詞（字幕のプレビューの材料のそのページの行）を、選んだレイアウト（編集中の値）で並べる
    /// （ワイプ前の状態で全部の行を出す。字幕アクションは描かない）。説明の文も返す。
    /// </summary>
    public (SubtitlePreviewModel? Model, string Note) BuildPreview()
    {
        var layout = CurrentSettings;
        var model = _main.PreviewModel;
        if (layout is null) return (null, "左の一覧でレイアウト設定を選ぶと、見本を出します");
        if (model is null) return (null, "字幕のプレビューの材料がまだ無いため、見本を出せません");
        var empty = new SubtitlePreviewModel(model.ScreenWidth, model.ScreenHeight, new List<PreviewLine>());
        if (PreviewPage is not { } page) return (empty, "歌詞のページがありません");
        if (!model.Pages.TryGetValue((page.Info.TabIndex, page.Info.PageIndex), out var lines))
        {
            return (empty, $"{page.Title} には表示時刻を決められる行が無いため（タイムタグが無いなど）、見本を出せません");
        }
        var spacing = new SubtitleSpacing((float)layout.LyricsIntervalPx, (float)layout.RubyIntervalPx, (float)layout.LyricsAndRubyIntervalPx, layout.RubyAlignment, layout.AllowBiting);
        var placed = lines.Select(p => p with
        {
            Source = p.Source.WithSpacing(spacing),
            Layout = layout,
            BeginMs = 0,
            EndMs = int.MaxValue,
            Wipe = Array.Empty<N3WipeTimeline.Group>(),
        }).ToList();
        string which = _selectedPages.Count > 0 ? "選んだページ" : UsagePages.Count > 0 ? "このレイアウトを使っているページ" : "最初のページ";
        return (new SubtitlePreviewModel(model.ScreenWidth, model.ScreenHeight, placed),
            $"{which}（{page.Title}）の歌詞を「{layout.Name}」で並べています（ワイプ前の状態で全部の行を表示）");
    }
}
