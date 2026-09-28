using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Tests;

/// <summary>
/// 表示時刻の自動計算がニコカラメーカー3 と同じになることの確認。
/// 期待値はニコカラメーカー3 が自動設定した実プロジェクト（Circle of Love / Sugar Sugar Yummy Yummy Parfait /
/// Eternalize Love）の値そのもの。
/// </summary>
public class N3ShowTimePlannerTests
{
    private static readonly N3ShowTimeSettings Current = new() { LeadMs = 1500, TailMs = 800, IntervalMs = 300 };

    private static (int End, int Begin) Resolve(int prevEnd, int prevLast, int nextBegin, int nextFirst, N3ShowTimeSettings? s = null, int? prevEndShort = null) =>
        N3ShowTimePlanner.ResolvePairForAnalysis(prevEnd, prevEndShort ?? prevEnd, prevLast, false, nextBegin, nextFirst, false, s ?? Current);

    [Theory]
    // (0) 次の行を「前の行の終了＋表示間隔」まで遅らせるだけで足りる
    [InlineData(47840, 47040, 42920, 51170, 47840, 48140)]
    // (1) 表示間隔を詰める（ワイプ前・ワイプ後はそのまま）
    [InlineData(33740, 32940, 29560, 35370, 33740, 33870)]
    // (1) 表示間隔を 75 まで詰めても足りず、前の行のワイプ後を少し削る
    [InlineData(23920, 23120, 19680, 25490, 23915, 23990)]
    // (2)(3) 前の行のワイプ後を保護時間 400 まで、残りは次の行のワイプ前から
    [InlineData(28600, 27800, 24700, 29650, 28200, 28275)]
    [InlineData(44280, 43480, 42920, 44420, 43880, 43955)]
    // (4) 次の行のワイプ前の保護時間を優先し、表示間隔 0、前の行のワイプ後は保護時間未満
    [InlineData(21380, 20580, 19680, 21180, 20780, 20780)]
    [InlineData(125260, 124460, 123400, 124900, 124500, 124500)]
    public void 同じ段の前後の行_実プロジェクトの値と一致(int prevEnd, int prevLast, int nextBegin, int nextFirst, int expectedEnd, int expectedBegin)
    {
        var (end, begin) = Resolve(prevEnd, prevLast, nextBegin, nextFirst);
        Assert.Equal(expectedEnd, end);
        Assert.Equal(expectedBegin, begin);
    }

    [Fact]
    public void 同じ段の前後の行_歌い出しがワイプ終了直後なら切り替えは前の行のワイプ終了時()
    {
        // 間が 200ms（保護時間 400 未満）: 前の行はワイプ終了で消え、次の行はそこから表示
        Assert.Equal((10000, 10000), Resolve(10800, 10000, 8700, 10200));
    }

    [Fact]
    public void 同じ段の前後の行_歌い出しが前の行のワイプ終了より早ければ歌い出しで切り替える()
    {
        Assert.Equal((9800, 9800), Resolve(10800, 10000, 8300, 9800));
    }

    [Theory]
    // Sugar Sugar Yummy Yummy Parfait（ワイプ前 1800 / ワイプ後 1000、上段を長めに）
    [InlineData(1800, 1000, 22000, 18720, 17720, 19180, 20980, 18880, 19180)]
    [InlineData(1800, 1000, 8850, 7460, 6460, 7510, 9310, 7435, 7510)]
    // Eternalize Love コーラス（ワイプ前 1000 / ワイプ後 500、上段を長めに）
    [InlineData(1000, 500, 10270, 6630, 6130, 9710, 10710, 9410, 9710)]
    public void 上段を長めに_延長分は表示間隔を保ったまま先に削る(int lead, int tail, int prevEnd, int prevEndShort, int prevLast, int nextBegin, int nextFirst, int expectedEnd, int expectedBegin)
    {
        var s = new N3ShowTimeSettings { LeadMs = lead, TailMs = tail, IntervalMs = 300, TopLong = true };
        var (end, begin) = Resolve(prevEnd, prevLast, nextBegin, nextFirst, s, prevEndShort);
        Assert.Equal(expectedEnd, end);
        Assert.Equal(expectedBegin, begin);
    }

    [Fact]
    public void 手動指定の側は動かさない()
    {
        // 前の行の終了が手動: 次の行の開始だけで調整（ワイプ前の保護時間 400 まで。それ以上は重なっても前の行の終了まで）
        var a = N3ShowTimePlanner.ResolvePairForAnalysis(21380, 21380, 20580, true, 19680, 21180, false, Current);
        Assert.Equal((21380, 21380), a);
        // 次の行の開始が手動: 前の行の終了だけで調整（ワイプ終了より前には消さない）
        var b = N3ShowTimePlanner.ResolvePairForAnalysis(21380, 21380, 20580, false, 19680, 21180, true, Current);
        Assert.Equal((20580, 19680), b);
    }

    [Fact]
    public void 行の歌唱開始と終了は行内の最小と最大の時刻()
    {
        // 同時歌唱などでタグが巻き戻る行（行末より行途中の文字の方が遅い）
        var line = LrcFormat.ParseLyricLine("[00:01:00]あ[00:03:00]い[00:02:00]う[00:02:50]");
        Assert.Equal(1000, N3ShowTimePlanner.SingStartMs(line));
        Assert.Equal(3000, N3ShowTimePlanner.SingEndMs(line));
    }

    [Fact]
    public void ページ内の行は同時に表示されページ間は同じ段どうしで詰める()
    {
        // Circle of Love 冒頭 3 ページ（ワイプ前 1500 / ワイプ後 800 / 表示間隔 300）
        var doc = LrcFormat.Parse(string.Join("\r\n",
            "@Emoji=(愛),a.png",
            "[00:16:21](愛)[00:18:21]曇[00:19:00]り[00:20:58]",
            "[00:19:86]「[00:20:70]大[00:23:12]",
            "",
            "[00:21:18](愛)[00:23:18]鮮[00:24:00]や[00:25:41]",
            "[00:25:49]O[00:25:95]ur[00:27:80]",
            "",
            "[00:26:20](愛)[00:28:20]無[00:29:00]限[00:30:46]",
            "[00:29:65]「[00:30:60]楽[00:32:94]",
            ""));
        var plans = N3ShowTimePlanner.Plan(doc, Current);

        // 実プロジェクトの値
        Assert.Equal((14710, 20780), (plans[0].BeginMs, plans[0].EndMs));
        Assert.Equal((14710, 23915), (plans[1].BeginMs, plans[1].EndMs));
        Assert.Equal((20780, 25800), (plans[3].BeginMs, plans[3].EndMs));
        Assert.Equal((23990, 28200), (plans[4].BeginMs, plans[4].EndMs));
        Assert.Equal(25800, plans[6].BeginMs);
        Assert.Equal(28275, plans[7].BeginMs);
    }
}
