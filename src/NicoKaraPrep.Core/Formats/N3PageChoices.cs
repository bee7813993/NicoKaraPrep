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

/// <summary>
/// ページ（<see cref="LyricsDocument.GetPages"/> の 1 件。書き出しと同じ数え方）ごとのレイアウトの決まり方と、ページへ広げる処理。
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
