using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Validation;

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

    /// <summary>
    /// 1 行だけのページが「下から 2 行目」に昇格するために必要な、
    /// 次ページの上段行の表示開始までの余裕（10ms 単位。0 = 重ならなければ昇格）。
    /// </summary>
    public int SingleLinePromoteGapCs { get; set; }

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
/// ニコカラメーカー3 のページ表示を想定した、隣接ページ間の「同じ画面位置の行」同士の
/// 表示時間の重なりを検出する（このツールの最重要チェック）。
///
/// 下から対応付けモードでは、1 行だけのページはニコカラメーカーの仕様に合わせて
/// 「次ページの上段行の表示開始まで十分時間がある場合、下から 2 行目に表示される」
/// ものとして扱う。
/// </summary>
public static class PageRowCollisionValidator
{
    public static List<ValidationIssue> Validate(LyricsDocument doc, PageCollisionSettings settings)
    {
        var issues = new List<ValidationIssue>();
        var pages = doc.GetPages(settings.PageMode, settings.FixedLineCount);
        var exclude = settings.ExcludeChar;

        // 行の表示終了（n3proj の実表示時間があれば優先、無ければ 最終タグ＋表示後秒数）
        int? DisplayEnd(int lineIdx)
        {
            if (settings.LineDisplayCs?.TryGetValue(lineIdx, out var disp) == true) return disp.EndCs;
            if (doc.Lines[lineIdx].GetLastTimeCs(exclude) is int last) return last + settings.DisplayTailCs;
            return null;
        }

        // 行の希望表示開始（常に 先頭タグ − 表示前秒数。ニコカラメーカーの早出し/遅延調整は
        // 演出であり、「この時刻までに出したい」に間に合うかを判定基準とする）
        int? DisplayStart(int lineIdx)
        {
            if (doc.Lines[lineIdx].GetFirstTimeCs(exclude) is int first) return first - settings.DisplayLeadCs;
            return null;
        }

        // ページごとの「画面位置 → 行インデックス」の対応を作る
        // 上から対応付け: 位置 = 上から k 行目 / 下から対応付け: 位置 = 下から k 行目
        Dictionary<int, int> BuildRowMap(int pageIdx)
        {
            var page = pages[pageIdx];
            var map = new Dictionary<int, int>();

            if (settings.AlignFromTop)
            {
                for (int k = 1; k <= page.Count; k++) map[k] = page[k - 1];
                return map;
            }

            // 下から対応付け:
            // 1 行だけのページは、次ページの上段（下から 2 行目）の表示開始まで
            // 十分時間があれば「下から 2 行目」に昇格して表示される
            if (page.Count == 1)
            {
                int line = page[0];
                bool promoted = false;
                if (pageIdx + 1 < pages.Count)
                {
                    var next = pages[pageIdx + 1];
                    int candidate = next.Count >= 2 ? next[^2] : next[0];
                    if (DisplayEnd(line) is int end && DisplayStart(candidate) is int start)
                    {
                        promoted = start - end >= settings.SingleLinePromoteGapCs;
                    }
                }
                else
                {
                    promoted = true; // 最終ページの 1 行は上段に出るものとして扱う
                }
                map[promoted ? 2 : 1] = line;
                return map;
            }

            for (int k = 1; k <= page.Count; k++) map[k] = page[^k];
            return map;
        }

        var rowMaps = new Dictionary<int, int>[pages.Count];
        for (int i = 0; i < pages.Count; i++) rowMaps[i] = BuildRowMap(i);

        for (int p = 0; p + 1 < pages.Count; p++)
        {
            foreach (var (pos, prevLineIdx) in rowMaps[p])
            {
                // 同じ画面位置に次ページの行が来る場合だけ比較する
                if (!rowMaps[p + 1].TryGetValue(pos, out int nextLineIdx)) continue;

                if (DisplayEnd(prevLineIdx) is not int displayEnd) continue;
                if (DisplayStart(nextLineIdx) is not int displayStart) continue;

                int overlap = displayEnd - displayStart;
                if (overlap <= 0) continue;

                string rowLabel = settings.AlignFromTop ? $"上から{pos}行目" : $"下から{pos}行目";
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
