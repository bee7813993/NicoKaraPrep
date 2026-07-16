using TimeTagTool.Core.Model;

namespace TimeTagTool.Core.Validation;

/// <summary>ページ間行衝突チェックの設定。</summary>
public sealed class PageCollisionSettings
{
    /// <summary>ページ分割方式。</summary>
    public PageSplitMode PageMode { get; set; } = PageSplitMode.EmptyLine;

    /// <summary>固定行数モードのページ行数。</summary>
    public int FixedLineCount { get; set; } = 2;

    /// <summary>行の表示開始 = 先頭タグの何秒前か（10ms 単位。ニコカラメーカー側の設定に合わせる）。</summary>
    public int DisplayLeadCs { get; set; } = 150;

    /// <summary>行の表示終了 = 最終タグの何秒後か（10ms 単位）。</summary>
    public int DisplayTailCs { get; set; } = 50;

    /// <summary>
    /// true: 上から数えて同じ行番号同士を比較（1行目↔1行目、2行目↔2行目）
    /// false: 下から数えて同じ位置同士を比較（デフォルト）
    /// </summary>
    public bool AlignFromTop { get; set; }

    /// <summary>この重なり量（10ms 単位）を超えたらエラー、以下は警告（ニコカラメーカー側の自動調整余地）。</summary>
    public int ErrorThresholdCs { get; set; } = 100;

    /// <summary>タグを無視する文字の判定（絵文字の先行タグ除外に使用）。</summary>
    public Func<CharUnit, bool>? ExcludeChar { get; set; }

    /// <summary>
    /// 行ごとの実表示区間（10ms 単位）。キー = 行インデックス。
    /// n3proj から取り込んだニコカラメーカーの実際の表示時間。
    /// 前ページ側の「表示終了」にのみ使用する。
    /// （表示開始はニコカラメーカーの調整機能で前後するため、判定には
    /// 　「希望表示開始 = 先頭タグ − 表示前秒数」を常に使う。実際の開始が
    /// 　これより早い場合は空きがあれば早く出すだけの演出で、問題ではない）
    /// </summary>
    public IReadOnlyDictionary<int, (int StartCs, int EndCs)>? LineDisplayCs { get; set; }
}

/// <summary>
/// ニコカラメーカー3 のページ表示を想定した、隣接ページ間の「下から同じ位置の行」同士の
/// 表示時間の重なりを検出する（このツールの最重要チェック）。
/// </summary>
public static class PageRowCollisionValidator
{
    public static List<ValidationIssue> Validate(LyricsDocument doc, PageCollisionSettings settings)
    {
        var issues = new List<ValidationIssue>();
        var pages = doc.GetPages(settings.PageMode, settings.FixedLineCount);
        var exclude = settings.ExcludeChar;

        for (int p = 0; p + 1 < pages.Count; p++)
        {
            var prev = pages[p];
            var next = pages[p + 1];
            int rows = Math.Min(prev.Count, next.Count);

            for (int k = 1; k <= rows; k++)
            {
                // 上から（または下から）数えて同じ行番号同士だけを比較する。
                // 例（上から）: ページの 2 行目は次ページの 2 行目の表示開始までに消えていればよく、
                //               次ページの 1 行目とは画面上の位置が異なるため比較しない。
                int prevLineIdx = settings.AlignFromTop ? prev[k - 1] : prev[^k];
                int nextLineIdx = settings.AlignFromTop ? next[k - 1] : next[^k];

                // 前行の表示終了: n3proj の実表示時間があれば優先、無ければ推定（最終タグ＋表示後秒数）
                int displayEnd;
                if (settings.LineDisplayCs?.TryGetValue(prevLineIdx, out var prevDisp) == true)
                {
                    displayEnd = prevDisp.EndCs;
                }
                else if (doc.Lines[prevLineIdx].GetLastTimeCs(exclude) is int pl)
                {
                    displayEnd = pl + settings.DisplayTailCs;
                }
                else
                {
                    continue;
                }

                // 次行の希望表示開始: 常に「先頭タグ − 表示前秒数」で判定する。
                // （ニコカラメーカーは空いていれば早く・塞がっていれば遅く表示を調整するため、
                // 　実際の表示開始ではなく「これまでに出したい時刻」に間に合うかを見る）
                if (doc.Lines[nextLineIdx].GetFirstTimeCs(exclude) is not int nf) continue;
                int displayStart = nf - settings.DisplayLeadCs;

                int overlap = displayEnd - displayStart;
                if (overlap <= 0) continue;

                string rowLabel = settings.AlignFromTop ? $"上から{k}行目" : $"下から{k}行目";
                var severity = overlap > settings.ErrorThresholdCs ? IssueSeverity.Error : IssueSeverity.Warning;
                issues.Add(new ValidationIssue(
                    severity,
                    "ページ衝突",
                    nextLineIdx,
                    $"ページ{p + 1}→{p + 2} {rowLabel}: 前行の表示終了 {TimeTag.Format(displayEnd)} > " +
                    $"次行の表示開始 {TimeTag.Format(displayStart)}（重なり {overlap / 100.0:F2} 秒、" +
                    $"{prevLineIdx + 1}行目 と {nextLineIdx + 1}行目）",
                    RelatedLineIndex: prevLineIdx));
            }
        }

        return issues;
    }
}
