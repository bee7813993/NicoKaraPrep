using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Formats;

/// <summary>ページのレイアウトがどう決まったか。</summary>
public enum N3PageLayoutSource
{
    /// <summary>ページの行に手で指定したレイアウト（<see cref="LyricsLine.LayoutName"/>）。</summary>
    Manual,

    /// <summary>タブに固定したレイアウト（n3proj の書き出しの設定 <c>TabLayouts</c>）。</summary>
    TabFixed,

    /// <summary>ページの行数から自動で選んだレイアウト。</summary>
    Auto,
}

/// <summary>ページ 1 つのレイアウト。</summary>
/// <param name="LayoutIndex">レイアウト設定の並び（書き出しの並び）の番号。</param>
/// <param name="Source">決まり方。</param>
/// <param name="ManualName">効いている手動指定の名前（手動でなければ null）。</param>
public sealed record N3PageLayoutChoice(int LayoutIndex, N3PageLayoutSource Source, string? ManualName);

/// <summary>ページの字幕アクションの指定のされ方。</summary>
public enum N3PageActionState
{
    /// <summary>どの行も指定が無い（曲の既定を使う）。</summary>
    Default,

    /// <summary>すべての行に同じアクションを指定している。</summary>
    Manual,

    /// <summary>行によって違う（指定のある行と無い行がある、または違うアクションを指定している）。</summary>
    Mixed,
}

/// <summary>ページの字幕アクションのまとめ。</summary>
/// <param name="State">指定のされ方。</param>
/// <param name="Action">すべての行が同じ指定のときのアクション（最初の行のもの。写しではない）。それ以外は null。</param>
/// <param name="ManualLines">指定のある行の数。</param>
/// <param name="DefaultLines">指定の無い（曲の既定を使う）行の数。</param>
/// <param name="ManualKinds">指定のある行のアクションの種類の数（同じ設定値のものを 1 つと数える）。</param>
public sealed record N3PageActionSummary(N3PageActionState State, N3SubtitleAction? Action, int ManualLines, int DefaultLines, int ManualKinds);

/// <summary>
/// ページ（<see cref="LyricsDocument.GetPages"/> の 1 件。書き出しと同じ数え方）ごとのレイアウト・字幕アクションの決まり方。
/// n3proj の書き出し（<see cref="N3ProjWriter"/>）とレイアウト設定ビューのページの一覧で同じものを使う。
/// </summary>
public static class N3PageChoices
{
    /// <summary>
    /// ページのレイアウト（書き出しの LayoutIndex と同じ決め方）: ページの中で最初に、並びにある名前を手で指定した行のレイアウト
    /// （並びに無い名前の指定は飛ばす）。無ければ <paramref name="layouts"/> の選び方（タブの固定レイアウトか、ページの行数から自動）。
    /// </summary>
    public static N3PageLayoutChoice ChooseLayout(LyricsDocument doc, IReadOnlyList<int> page, N3ProjWriter.LayoutResolver layouts)
    {
        foreach (int i in page)
        {
            if (doc.Lines[i].LayoutName is { Length: > 0 } name && layouts.FindIndex(name) is int found)
            {
                return new N3PageLayoutChoice(found, N3PageLayoutSource.Manual, name);
            }
        }
        return new N3PageLayoutChoice(layouts.Resolve(page.Count), layouts.IsFixed ? N3PageLayoutSource.TabFixed : N3PageLayoutSource.Auto, null);
    }

    /// <summary>
    /// ページの字幕アクションのまとめ。行の指定（<see cref="LyricsLine.SubtitleAction"/>。Id が空のものは指定なしとみなす。書き出しと同じ）を
    /// 比べ（<see cref="N3SubtitleAction.SameAs"/>）、どの行も指定なし・すべて同じ指定・混在 のどれかにする。空のページは指定なし。
    /// </summary>
    public static N3PageActionSummary SummarizeActions(LyricsDocument doc, IReadOnlyList<int> page)
    {
        var manual = new List<N3SubtitleAction>();
        int defaults = 0;
        foreach (int i in page)
        {
            if (doc.Lines[i].SubtitleAction is { Id.Length: > 0 } action) manual.Add(action);
            else defaults++;
        }
        var kinds = new List<N3SubtitleAction>();
        foreach (var a in manual)
        {
            if (!kinds.Any(k => k.SameAs(a))) kinds.Add(a);
        }
        if (manual.Count == 0) return new N3PageActionSummary(N3PageActionState.Default, null, 0, defaults, 0);
        if (defaults == 0 && kinds.Count == 1) return new N3PageActionSummary(N3PageActionState.Manual, manual[0], manual.Count, 0, 1);
        return new N3PageActionSummary(N3PageActionState.Mixed, null, manual.Count, defaults, kinds.Count);
    }

    /// <summary>
    /// 行を、同じページの行すべてに広げる（ページの指定をそろえる操作の対象。範囲の外・空行は飛ばし、ページに無い行はその行だけ）。
    /// ページの数え方は書き出しと同じ（<see cref="LyricsDocument.GetPages"/>）。小さい順に返す。
    /// </summary>
    public static List<int> ExpandToPages(LyricsDocument doc, IEnumerable<int> indexes, PageSplitMode mode, int fixedLineCount)
    {
        var pages = doc.GetPages(mode, fixedLineCount);
        var targets = new SortedSet<int>();
        foreach (int i in indexes)
        {
            if (i < 0 || i >= doc.Lines.Count || doc.Lines[i].IsEmpty) continue;
            var page = pages.FirstOrDefault(p => p.Contains(i));
            foreach (int k in page ?? new List<int> { i }) targets.Add(k);
        }
        return targets.ToList();
    }
}
