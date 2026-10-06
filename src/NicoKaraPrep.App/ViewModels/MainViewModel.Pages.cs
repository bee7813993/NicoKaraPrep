using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.ViewModels;

/// <summary>
/// レイアウト設定ビューのページの一覧の 1 件（全タブのページ。ページの数え方・レイアウトの決め方は n3proj の書き出しと同じ）。
/// </summary>
/// <param name="Tab">ページのタブ。</param>
/// <param name="TabIndex">タブの並びの番号（<see cref="MainViewModel.Tabs"/> の順。字幕のプレビューの材料のタブの番号と同じ）。</param>
/// <param name="IsActiveTab">表示中のタブか。</param>
/// <param name="PageIndex">タブの中のページの番号（0 から。<see cref="LyricsDocument.GetPages"/> の順）。</param>
/// <param name="Lines">ページの行（タブの行番号。空行は入らない）。</param>
/// <param name="StartMs">歌い出し（ページの行の最初のタグの時刻 ms。タグが無ければ null）。</param>
/// <param name="FirstText">最初の行の歌詞。</param>
/// <param name="Layout">ページのレイアウト（並びが空なら null）。</param>
/// <param name="LayoutChoice">レイアウトの決まり方（手動・タブ固定・自動）。</param>
public sealed record LayoutPageInfo(
    TabState Tab,
    int TabIndex,
    bool IsActiveTab,
    int PageIndex,
    IReadOnlyList<int> Lines,
    int? StartMs,
    string FirstText,
    N3LayoutSettings? Layout,
    N3PageLayoutChoice LayoutChoice)
{
    public string TabName => Tab.Name;

    public string LayoutName => Layout?.Name ?? "";
}

/// <summary>
/// レイアウト設定ビュー（F4）のページの一覧と、表示中でないタブも含めたページへのレイアウトの指定、
/// 曲の既定の字幕アクションの「自動」の中身。
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// 全タブのページ（タブの順・ページの順）。ページの数え方（<see cref="LyricsDocument.GetPages"/>。<see cref="CreateShowTimeSettings"/> と同じ
    /// 空行区切りか固定行数）とレイアウトの決め方（<see cref="N3PageChoices.ChooseLayout"/>: 手動指定 → タブの固定 → 行数から自動）は書き出しと同じ。
    /// </summary>
    public List<LayoutPageInfo> GetLayoutPages()
    {
        var tabs = GetAllTabs();
        var layouts = GetEffectiveLayouts();
        var infos = layouts.Select(l => l.Info).ToList();
        var result = new List<LayoutPageInfo>();
        for (int t = 0; t < tabs.Count; t++)
        {
            var tab = tabs[t];
            var doc = tab.Document;
            var pages = doc.GetPages(Settings.PageMode, Settings.FixedLineCount);
            if (pages.Count == 0) continue;
            string? fixedLayout = N3ProjSettings.TabLayouts.GetValueOrDefault(tab.Name) is { Length: > 0 } fl ? fl : null;
            var resolver = new N3ProjWriter.LayoutResolver(infos, fixedLayout, Nkm3Env?.LayoutSelectableBegin, Nkm3Env?.LayoutSelectableEnd, new List<string>(), tab.Name);
            for (int p = 0; p < pages.Count; p++)
            {
                var page = pages[p];
                var choice = N3PageChoices.ChooseLayout(doc, page, resolver);
                var layout = layouts.Count > 0 ? layouts[Math.Clamp(choice.LayoutIndex, 0, layouts.Count - 1)] : null;
                int? start = null;
                foreach (int i in page)
                {
                    if (N3ShowTimePlanner.SingStartMs(doc.Lines[i]) is int s && (start is null || s < start)) start = s;
                }
                result.Add(new LayoutPageInfo(
                    tab, t, ReferenceEquals(tab, _activeTab), p, page, start, doc.Lines[page[0]].GetDisplayText(),
                    layout, choice));
            }
        }
        return result;
    }

    /// <summary>
    /// タブの行のページのレイアウトを手動指定する（null / 空 = 自動）。表示中のタブは <see cref="SetLinesLayout"/> と同じ。
    /// 表示中でないタブは、タブを切り替えずにそのタブの歌詞へ書き、元に戻す の履歴もそのタブに積む（そのタブを表示して Ctrl+Z で戻る）。
    /// 変えた行の数を返す。
    /// </summary>
    public int SetPageLinesLayout(TabState tab, IReadOnlyList<int> lines, string? name)
    {
        if (!Tabs.Contains(tab)) return 0;
        if (ReferenceEquals(tab, _activeTab)) return SetLinesLayout(lines, name);
        string? value = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        return WriteOtherTabPages(tab, lines, l => l.LayoutName != value, l => l.LayoutName = value);
    }

    /// <summary>
    /// 表示中でないタブの、指定した行と同じページの行すべてに書く（変わる行があれば、先にそのタブの 元に戻す の履歴に今の歌詞を積む）。
    /// 曲の設定（.tttproj）も保存する。変えた行の数を返す。
    /// </summary>
    private int WriteOtherTabPages(TabState tab, IReadOnlyList<int> lines, Func<LyricsLine, bool> differs, Action<LyricsLine> apply)
    {
        var doc = tab.Document;
        var changed = N3PageChoices.ExpandToPages(doc, lines, Settings.PageMode, Settings.FixedLineCount)
            .Where(i => differs(doc.Lines[i]))
            .ToList();
        if (changed.Count == 0) return 0;
        tab.UndoStack.Add(doc.Clone());
        if (tab.UndoStack.Count > MaxUndo) tab.UndoStack.RemoveAt(0);
        tab.RedoStack.Clear();
        foreach (int i in changed) apply(doc.Lines[i]);
        tab.IsModified = true;
        SaveProject();
        return changed.Count;
    }

    /// <summary>
    /// 曲の既定の字幕アクションを「自動」にしたときに使うもの（書き出しと同じ決め方 N3ProjWriter.ResolveDefaultAction:
    /// ベースの歌詞行でいちばん多いもの → ニコカラメーカー3 の「すべて同じ字幕アクション」の Id → 文字単位フェード）と、その出どころ。
    /// </summary>
    public N3SubtitleAction ResolveAutoSubtitleAction(out N3SubtitleActionSource source) =>
        N3ProjWriter.ResolveDefaultAction(null, ReadBaseMostCommonSubtitleAction(), Nkm3Env?.DefaultSubtitleActionId, Nkm3Env?.AddOnSettings, out source);

}
