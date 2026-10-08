using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Formats;

/// <summary>自動調整を実行した結果の行数。</summary>
/// <param name="AutoLines">自動調整の値を持たせた行（表示開始・終了のどちらかを計算した行）。</param>
/// <param name="ManualLines">手で指定した表示時刻を残した行（表示開始・終了のどちらかが手動）。</param>
/// <param name="UntimedLines">タイムタグが無く、表示時刻を決められない歌詞の行。</param>
public sealed record N3ShowTimeAdjustResult(int AutoLines, int ManualLines, int UntimedLines);

/// <summary>行の表示時刻を、出どころごとに数えた行数（歌詞の行だけ。1 行は手動 → 読み込み → 自動調整 → 未設定 の順に 1 つに数える）。</summary>
/// <param name="Manual">手で指定した表示時刻を持つ行。</param>
/// <param name="Loaded">n3proj から読み込んだ表示時刻を持つ行。</param>
/// <param name="Auto">自動調整を実行して決めた表示時刻を持つ行。</param>
/// <param name="Live">表示時刻を持たない行（書き出し・画面の表示のたびに自動で計算する）。</param>
public sealed record N3ShowTimeOriginCounts(int Manual, int Loaded, int Auto, int Live);

/// <summary>
/// 表示時刻の自動調整（「表示時刻の自動調整」（行設定の「自動調整...」）の「自動調整を実行」）。
/// 手で指定した表示時刻（<see cref="ShowTimeOrigin.Manual"/>）は残し、ほかの値（n3proj から読み込んだ値・前回の自動調整の値・未設定）を
/// <see cref="N3ShowTimePlanner"/> で計算し直して、自動調整の値（<see cref="ShowTimeOrigin.Auto"/>）として行に持たせる。
/// 行に持たせた表示時刻は、その後の書き出し・字幕のプレビュー・チェックでそのまま使う（歌詞や設定を変えても、実行し直すまで変わらない）。
/// </summary>
public static class N3ShowTimeAdjuster
{
    /// <summary>自動調整を実行する（<paramref name="doc"/> の行の表示時刻を書き換える）。</summary>
    public static N3ShowTimeAdjustResult Run(LyricsDocument doc, N3ShowTimeSettings settings)
    {
        ClearRecomputable(doc);
        var plans = N3ShowTimePlanner.Plan(doc, settings);
        int auto = 0, manual = 0, untimed = 0;
        for (int i = 0; i < doc.Lines.Count; i++)
        {
            var line = doc.Lines[i];
            if (line.IsEmpty) continue;
            if (line.HasManualShowBegin || line.HasManualShowEnd) manual++;
            if (!plans.TryGetValue(i, out var p))
            {
                untimed++;
                continue;
            }
            bool set = false;
            if (line.ShowBeginCs is null)
            {
                line.ShowBeginCs = ToCs(p.BeginMs);
                line.ShowBeginOrigin = ShowTimeOrigin.Auto;
                set = true;
            }
            if (line.ShowEndCs is null)
            {
                line.ShowEndCs = ToCs(p.EndMs);
                line.ShowEndOrigin = ShowTimeOrigin.Auto;
                set = true;
            }
            if (set) auto++;
        }
        return new N3ShowTimeAdjustResult(auto, manual, untimed);
    }

    /// <summary>自動調整で計算し直す表示時刻（手で指定した値以外）を外す（値を持たない = 自動で計算する状態に戻す）。</summary>
    public static void ClearRecomputable(LyricsDocument doc)
    {
        foreach (var line in doc.Lines)
        {
            if (line.ShowBeginCs is not null && line.ShowBeginOrigin != ShowTimeOrigin.Manual) line.ShowBeginCs = null;
            if (line.ShowEndCs is not null && line.ShowEndOrigin != ShowTimeOrigin.Manual) line.ShowEndCs = null;
        }
    }

    /// <summary>
    /// いま自動調整を実行したら決まる表示時刻（行には持たせない。行設定の説明・チェックで、実行し直すと変わるかを見るため）。
    /// 手で指定した値は残し、ほかの値を外した写しで計算する。
    /// </summary>
    public static Dictionary<int, N3LinePlan> PlanFresh(LyricsDocument doc, N3ShowTimeSettings settings)
    {
        var copy = doc.Clone();
        ClearRecomputable(copy);
        return N3ShowTimePlanner.Plan(copy, settings);
    }

    /// <summary>自動調整の値・読み込んだ値（実行し直すと計算し直す値）を 1 つでも持つか。</summary>
    public static bool HasRecomputable(LyricsDocument doc) => doc.Lines.Any(l =>
        (l.ShowBeginCs is not null && l.ShowBeginOrigin != ShowTimeOrigin.Manual) ||
        (l.ShowEndCs is not null && l.ShowEndOrigin != ShowTimeOrigin.Manual));

    /// <summary>行の表示時刻を出どころごとに数える。</summary>
    public static N3ShowTimeOriginCounts Count(LyricsDocument doc)
    {
        int manual = 0, loaded = 0, auto = 0, live = 0;
        foreach (var line in doc.Lines)
        {
            if (line.IsEmpty) continue;
            bool HasOrigin(ShowTimeOrigin o) =>
                (line.ShowBeginCs is not null && line.ShowBeginOrigin == o) || (line.ShowEndCs is not null && line.ShowEndOrigin == o);
            if (HasOrigin(ShowTimeOrigin.Manual)) manual++;
            else if (HasOrigin(ShowTimeOrigin.Loaded)) loaded++;
            else if (HasOrigin(ShowTimeOrigin.Auto)) auto++;
            else live++;
        }
        return new N3ShowTimeOriginCounts(manual, loaded, auto, live);
    }

    /// <summary>
    /// 行に持たせた自動調整の値・読み込んだ値のうち、いま自動調整を実行し直すと変わる行の数（5ms 以内の違いは数えない）。
    /// 手で指定した値と、値を持たない行は数えない。
    /// </summary>
    public static int CountOutdated(LyricsDocument doc, N3ShowTimeSettings settings)
    {
        if (!HasRecomputable(doc)) return 0;
        var plans = N3ShowTimePlanner.Plan(doc, settings);
        var fresh = PlanFresh(doc, settings);
        int n = 0;
        foreach (var (i, p) in plans)
        {
            var line = doc.Lines[i];
            bool beginRe = line.ShowBeginCs is not null && line.ShowBeginOrigin != ShowTimeOrigin.Manual;
            bool endRe = line.ShowEndCs is not null && line.ShowEndOrigin != ShowTimeOrigin.Manual;
            if (!beginRe && !endRe) continue;
            if (!fresh.TryGetValue(i, out var f)) continue;
            if ((beginRe && Math.Abs(f.BeginMs - p.BeginMs) > 5) || (endRe && Math.Abs(f.EndMs - p.EndMs) > 5)) n++;
        }
        return n;
    }

    /// <summary>ms を 10ms 単位へ（四捨五入）。</summary>
    public static int ToCs(int ms) => (int)Math.Round(ms / 10.0, MidpointRounding.AwayFromZero);
}
