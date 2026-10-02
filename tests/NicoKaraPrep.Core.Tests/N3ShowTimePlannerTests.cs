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

    // ------------------------------------------------------------ 絵文字の先行を譲る規則（NicoKaraPrep の機能）

    /// <summary>Circle of Love 冒頭 3 ページ（上段の行頭に (愛)。絵文字の先行は 2 秒）。</summary>
    private static LyricsDocument CircleOfLove() => LrcFormat.Parse(string.Join("\r\n",
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

    private static LyricsDocument Lyrics(params string[] lines) => LrcFormat.Parse(string.Join("\r\n", lines) + "\r\n");

    /// <summary>ワイプ前 1500 / ワイプ後 800 / 表示間隔 300 で、絵文字の先行を譲る規則をオンにした設定。</summary>
    private static N3ShowTimeSettings Yield(params string[] emoji) =>
        new() { LeadMs = 1500, TailMs = 800, IntervalMs = 300, EmojiLeadYield = true, LeadMatcher = new EmojiMatcher(emoji) };

    private static void AssertSamePlans(Dictionary<int, N3LinePlan> expected, Dictionary<int, N3LinePlan> actual)
    {
        Assert.Equal(expected.Keys.OrderBy(k => k), actual.Keys.OrderBy(k => k));
        foreach (var (i, p) in expected) Assert.Equal(p, actual[i]);
    }

    [Fact]
    public void 絵文字の先行を譲る_前の行のワイプ後を残し次の行は必要な分だけ遅らせる()
    {
        var plans = N3ShowTimePlanner.Plan(CircleOfLove(), Yield("(愛)"));

        // ページ1→2 の上段: 前の行 0 はワイプ後 800 を残し、次の行 3 は本当の歌い出し（23180）の 1500 前に出る（今は両方 20780）
        Assert.Equal(21380, plans[0].EndMs);
        Assert.Equal((21680, 26210), (plans[3].BeginMs, plans[3].EndMs));
        Assert.Equal(900, plans[3].EmojiYieldMs); // 規則なしの 20780 から
        // ページ2→3 の上段: 前の行 3 は 26210 まで（今は 25800）、次の行 6 は前の終了＋表示間隔まで遅らせるだけで足りる
        Assert.Equal(26510, plans[6].BeginMs);
        Assert.Equal(710, plans[6].EmojiYieldMs);
        Assert.True(plans[6].Adjusted);

        // 下段（絵文字なし）とページの表示開始は変わらない
        Assert.Equal((14710, 23915), (plans[1].BeginMs, plans[1].EndMs));
        Assert.Equal((23990, 28200), (plans[4].BeginMs, plans[4].EndMs));
        Assert.Equal(28275, plans[7].BeginMs);
        Assert.Equal(14710, plans[0].BeginMs);
        Assert.Equal(0, plans[0].EmojiYieldMs);
        Assert.Equal(0, plans[4].EmojiYieldMs);
    }

    [Fact]
    public void 絵文字の先行を譲る_規則オフや絵文字の指定が無ければニコカラメーカー3と同じ()
    {
        var doc = CircleOfLove();
        var current = N3ShowTimePlanner.Plan(doc, Current);
        var off = Yield("(愛)");
        off.EmojiLeadYield = false;
        var noMatcher = Yield();
        noMatcher.LeadMatcher = null;
        foreach (var s in new[] { off, noMatcher, Yield() })
        {
            Assert.False(s.YieldsEmojiLead);
            AssertSamePlans(current, N3ShowTimePlanner.Plan(doc, s));
        }
        // 実プロジェクトの値のまま
        Assert.Equal((20780, 25800), (current[3].BeginMs, current[3].EndMs));
        Assert.All(current.Values, p => Assert.Equal(0, p.EmojiYieldMs));
        // 既定はオフ（推定やゴールデンテストはニコカラメーカー3 と同じ計算）
        Assert.False(new N3ShowTimeSettings().EmojiLeadYield);
    }

    [Fact]
    public void 絵文字の先行を譲る_遅らせても足りなければ絵文字はワイプ前の表示時間を残し前の行のワイプ後を削る()
    {
        // 2 行ページ 2 枚。上段の次の行の絵文字（22000〜24000 の 2 秒）が、前の行のワイプ終了（22500）より前に始まる
        var doc = Lyrics(
            "@Emoji=（花帆）,a.png",
            "[00:20:00]あ[00:22:50]",
            "[00:21:00]い[00:26:00]",
            "",
            "[00:22:00]（花帆）[00:24:00]う[00:26:00]",
            "[00:27:00]え[00:29:00]");
        var now = N3ShowTimePlanner.Plan(doc, Current);
        Assert.Equal(22000, now[0].EndMs); // 今は前の行がワイプの途中で消える
        Assert.Equal(22000, now[3].BeginMs);

        var (plans, clamped) = N3EmojiLead.PrepareTab(doc, Yield("（花帆）"));
        // 絵文字はワイプ前の表示時間 1.5 秒を残す（本当の歌い出し 24000 の 1500 前に出る）。
        // 前の行はワイプ後を 0 まで削り、ワイプ終了ちょうどで消える（途中では消えない）
        Assert.Equal(22500, plans[0].EndMs);
        Assert.Equal(22500, plans[3].BeginMs);
        Assert.Equal(500, plans[3].EmojiYieldMs);
        Assert.Equal("[00:22:50]（花帆）[00:24:00]う[00:26:00]", LrcFormat.WriteLyricLine(clamped.Lines[3]));
        // 下段は絵文字に関係しない
        Assert.Equal((26400, 26475), (plans[1].EndMs, plans[4].BeginMs));
    }

    [Fact]
    public void 絵文字の先行を譲る_下段の行頭の絵文字()
    {
        var doc = Lyrics(
            "@Emoji=（花帆）,a.png",
            "[00:10:00]あ[00:12:00]",
            "[00:12:50]い[00:15:00]",
            "",
            "[00:17:00]う[00:19:00]",
            "[00:16:00]（花帆）[00:18:00]え[00:20:00]");
        var now = N3ShowTimePlanner.Plan(doc, Current);
        Assert.Equal((15400, 15475), (now[1].EndMs, now[4].BeginMs));

        var plans = N3ShowTimePlanner.Plan(doc, Yield("（花帆）"));
        Assert.Equal(15800, plans[1].EndMs);
        Assert.Equal(16100, plans[4].BeginMs);
        // 上段（絵文字なし）はページ共通の開始（下段の絵文字の 1500 前）のまま
        Assert.Equal(14500, plans[3].BeginMs);
    }

    [Fact]
    public void 絵文字の先行を譲る_連続する絵文字はスペーサーのタグを本当の歌い出しに数える()
    {
        // PerEmoji: [E]（花帆）[T][E]（さやか）[T]う。間のスペーサーのタグは T と同じ
        var doc = Lyrics(
            "@Emoji=（花帆）,a.png",
            "@Emoji=（さやか）,b.png",
            "[00:10:00]あ[00:12:00]",
            "[00:12:50]い[00:15:00]",
            "",
            "[00:11:50]（花帆）[00:13:50][00:11:50]（さやか）[00:13:50]う[00:15:00]",
            "[00:16:00]え[00:18:00]");
        var plans = N3ShowTimePlanner.Plan(doc, Yield("（花帆）", "（さやか）"));
        // 本当の歌い出しはスペーサーの 13500。2 秒の絵文字はどちらもワイプ前の表示時間 1.5 秒を残し（13500 − 1500）、
        // 前の行はワイプ終了（12000）ちょうどで消える
        Assert.Equal(12000, plans[0].EndMs);
        Assert.Equal(12000, plans[3].BeginMs);
    }

    [Fact]
    public void 絵文字の先行を譲る_次の行の表示開始が手動指定なら変えない()
    {
        var doc = Lyrics(
            "@Emoji=（花帆）,a.png",
            "[00:20:00]あ[00:22:50]",
            "[00:21:00]い[00:26:00]",
            "",
            "[00:22:00]（花帆）[00:24:00]う[00:26:00]",
            "[00:27:00]え[00:29:00]");
        doc.Lines[3].ShowBeginCs = 2210;
        var plans = N3ShowTimePlanner.Plan(doc, Yield("（花帆）"));
        AssertSamePlans(N3ShowTimePlanner.Plan(doc, Current), plans);
        Assert.Equal(22100, plans[3].BeginMs);
        Assert.Equal(0, plans[3].EmojiYieldMs);
    }

    [Fact]
    public void 絵文字の先行を譲る_上段を長めにの延長分も絵文字の先行を譲り切るまでは削らない()
    {
        // 前の上段は早く歌い終わり（10000）、下段が遅くまで続く（延長して 13800 まで）。次の上段の絵文字は 12000、歌い出しは 14000
        var doc = Lyrics(
            "@Emoji=（花帆）,a.png",
            "[00:08:00]あ[00:10:00]",
            "[00:10:50]い[00:13:00]",
            "",
            "[00:12:00]（花帆）[00:14:00]う[00:16:00]",
            "[00:16:50]え[00:18:00]");
        var now = N3ShowTimePlanner.Plan(doc, new N3ShowTimeSettings { LeadMs = 1500, TailMs = 800, IntervalMs = 300, TopLong = true });
        Assert.Equal((10425, 10500), (now[0].EndMs, now[3].BeginMs));

        var s = Yield("（花帆）");
        s.TopLong = true;
        var plans = N3ShowTimePlanner.Plan(doc, s);
        Assert.Equal(12200, plans[0].EndMs); // 延長分も一部残る
        Assert.Equal(12500, plans[3].BeginMs); // 本当の歌い出しの 1500 前
        Assert.Equal(13800, plans[1].EndMs);
    }

    [Fact]
    public void 絵文字の先行を譲る_プレースホルダは対象でGキーの先行タグは対象外()
    {
        // ＿ の先行（絵文字と同じに扱う）
        var placeholder = Lyrics(
            "[00:20:00]あ[00:22:50]",
            "[00:21:00]い[00:26:00]",
            "",
            "[00:22:00]＿[00:24:00]う[00:26:00]",
            "[00:27:00]え[00:29:00]");
        var s = Yield("（花帆）", "＿");
        Assert.Equal(22500, N3ShowTimePlanner.Plan(placeholder, s)[3].BeginMs);

        // G キーの先行タグ（スペーサーに載る。早く出したい行なので譲らない）
        var gKey = Lyrics(
            "[00:20:00]あ[00:22:50]",
            "[00:21:00]い[00:26:00]",
            "",
            "[00:22:00][00:24:00]う[00:26:00]",
            "[00:27:00]え[00:29:00]");
        AssertSamePlans(N3ShowTimePlanner.Plan(gKey, Current), N3ShowTimePlanner.Plan(gKey, s));
        Assert.Equal(22000, N3ShowTimePlanner.Plan(gKey, s)[3].BeginMs);
    }

    [Fact]
    public void 絵文字の先行を譲る_行頭の2連タグが行内の最小なら変えない()
    {
        // 2 連タグ（前の行の終わりのタグなど）は実のタグとして数える。それが絵文字の先行タグより早ければ規則は働かない
        var doc = Lyrics(
            "@Emoji=（花帆）,a.png",
            "[00:20:00]あ[00:22:50]",
            "[00:21:00]い[00:26:00]",
            "",
            "[00:21:50][00:22:00]（花帆）[00:24:00]う[00:26:00]",
            "[00:27:00]え[00:29:00]");
        var plans = N3ShowTimePlanner.Plan(doc, Yield("（花帆）"));
        AssertSamePlans(N3ShowTimePlanner.Plan(doc, Current), plans);
    }

    /// <summary>例 3 の形（2 行ページ 2 枚。前のページの上段のワイプは 22500 に終わる）で、次のページの上段を差し替えた歌詞。</summary>
    private static LyricsDocument Example3(string top) => Lyrics(
        "@Emoji=（花帆）,a.png",
        "[00:20:00]あ[00:22:50]",
        "[00:21:00]い[00:26:00]",
        "",
        top,
        "[00:27:00]お[00:29:00]");

    [Theory]
    // 行頭の絵文字（寄せる）
    [InlineData("[00:22:00]（花帆）[00:24:00]う[00:26:00]")]
    // 基準時刻 T の無い絵文字・＿のタグが行の最小（寄せない）
    [InlineData("[00:24:00]う[00:25:00]え[00:22:00]（花帆）")]
    [InlineData("[00:24:00]う[00:25:00]え[00:22:00]＿")]
    // 出現の中の 2 つ目のタグ（寄せない）
    [InlineData("[00:22:00]（花[00:22:50]帆）[00:24:00]う[00:26:00]")]
    public void 絵文字の先行を譲る_寄せた後の歌詞のタグはどれも表示開始より前にならない(string top)
    {
        var (plans, clamped) = N3EmojiLead.PrepareTab(Example3(top), Yield("（花帆）", "＿"));
        foreach (var (i, plan) in plans)
        {
            var line = clamped.Lines[i];
            foreach (int t in line.Chars.Select(c => c.TimeCs).Append(line.EndTimeCs).OfType<int>())
            {
                Assert.True(t * 10 >= plan.BeginMs, $"{i + 1}行目 {LrcFormat.WriteLyricLine(line)} のタグ {t * 10} が表示開始 {plan.BeginMs} より前");
            }
        }
    }

    [Fact]
    public void 絵文字の先行を譲る_基準時刻の無い絵文字のタグは寄せないので歌い出しに数える()
    {
        // 後ろにタグの付いた文字も行末タグも無い絵文字・＿のタグ（22000）が行の最小。寄せないので規則は働かない
        foreach (var top in new[] { "[00:24:00]う[00:25:00]え[00:22:00]（花帆）", "[00:24:00]う[00:25:00]え[00:22:00]＿" })
        {
            var doc = Example3(top);
            var plans = N3ShowTimePlanner.Plan(doc, Yield("（花帆）", "＿"));
            AssertSamePlans(N3ShowTimePlanner.Plan(doc, Current), plans);
            Assert.Equal((22000, 22000), (plans[0].EndMs, plans[3].BeginMs));
        }
    }

    [Fact]
    public void 絵文字の先行を譲る_出現の中の2つ目のタグは寄せないので歌い出しに数える()
    {
        // 寄せるのは出現の最初のタグ（22000）だけ。2 つ目のタグ（帆 22500）が本当の歌い出しで、前の行のワイプ終了と同時に切り替える
        var (plans, clamped) = N3EmojiLead.PrepareTab(Example3("[00:22:00]（花[00:22:50]帆）[00:24:00]う[00:26:00]"), Yield("（花帆）"));
        Assert.Equal(22500, plans[0].EndMs);
        Assert.Equal((22500, 26800), (plans[3].BeginMs, plans[3].EndMs));
        Assert.Equal("[00:22:50]（花[00:22:50]帆）[00:24:00]う[00:26:00]", LrcFormat.WriteLyricLine(clamped.Lines[3]));
    }

    // ------------------------------------------------------------ 絵文字の表示秒数の下限（ワイプ前の表示時間と元の表示秒数の短い方）

    /// <summary>
    /// 次の行のワイプ前の下限（nextMinPreMs）を足す前の ResolvePair の写し（HEAD 24750a7。ニコカラメーカー3 と同じ計算）。
    /// 下限 0 なら今までと 1ms も違わないことの確かめに使う。
    /// </summary>
    private static (int PrevEnd, int NextBegin) HeadResolvePair(
        int prevEnd, int prevEndShort, int prevLast, bool prevEndManual,
        int nextBegin, int nextFirst, bool nextBeginManual,
        N3ShowTimeSettings s)
    {
        int interval = Math.Max(0, s.IntervalMs);
        int minGap = interval / 4;
        int protect = s.EffectiveProtectMs;
        int lead = s.LeadMs;

        if (prevEnd + interval <= nextBegin) return (prevEnd, nextBegin);
        if (prevEndManual && nextBeginManual) return (prevEnd, nextBegin);

        if (!prevEndManual && prevEnd > prevEndShort)
        {
            prevEnd = Math.Max(prevEndShort, Math.Min(prevEnd, nextBegin - interval));
            if (prevEnd + interval <= nextBegin) return (prevEnd, nextBegin);
        }

        if (nextBeginManual)
        {
            int pe = prevEnd;
            if (pe + minGap > nextBegin) pe = Math.Max(nextBegin - minGap, prevLast + protect);
            if (pe > nextBegin) pe = Math.Max(nextBegin, prevLast);
            return (Math.Min(pe, prevEnd), nextBegin);
        }

        if (prevEndManual)
        {
            int nb = prevEnd + interval;
            if (nb > nextFirst - lead) nb = Math.Max(nextFirst - lead, prevEnd + minGap);
            if (nb > nextFirst - protect) nb = Math.Max(nextFirst - protect, prevEnd);
            return (prevEnd, Math.Max(nb, nextBegin));
        }

        int desired = prevEnd + interval;
        if (desired <= nextFirst - lead) return (prevEnd, desired);

        int begin = nextFirst - lead;
        if (prevEnd + minGap <= begin) return (prevEnd, Math.Max(begin, nextBegin));

        int end = begin - minGap;
        if (end >= prevLast + protect) return (Math.Min(end, prevEnd), Math.Max(begin, nextBegin));

        end = prevLast + protect;
        begin = end + minGap;
        if (nextFirst - begin >= protect) return (Math.Min(end, prevEnd), Math.Max(begin, nextBegin));

        int window = nextFirst - prevLast;
        if (window >= 2 * protect)
        {
            return (Math.Min(prevLast + protect, prevEnd), Math.Max(nextFirst - protect, nextBegin));
        }
        if (window >= protect)
        {
            int meet = nextFirst - protect;
            return (Math.Min(meet, prevEnd), Math.Max(meet, nextBegin));
        }
        if (window >= 0)
        {
            return (Math.Min(prevLast, prevEnd), Math.Max(prevLast, nextBegin));
        }
        return (Math.Min(nextFirst, prevEnd), Math.Max(nextFirst, nextBegin));
    }

    [Theory]
    // 間隔が十分 / 両方が手動 / 上段を長めにの延長分を削るだけで足りる
    [InlineData(10000, 10000, 9200, false, 10300, 11800, false, 10000, 10300)]
    [InlineData(20000, 20000, 19000, true, 19000, 20500, true, 20000, 19000)]
    [InlineData(22000, 18720, 17720, false, 19180, 20980, false, 18880, 19180)]
    // 次の行の開始が手動 / 前の行の終了が手動（2 つ目のしきい値まで進む）
    [InlineData(21380, 21380, 20580, false, 19680, 21180, true, 20580, 19680)]
    [InlineData(21380, 21380, 20580, true, 19680, 21180, false, 21380, 21380)]
    [InlineData(23000, 23000, 22500, true, 20500, 24000, false, 23000, 23075)]
    // (0) (1) (2) (3) (3') (4) (5) (6)
    [InlineData(47840, 47840, 47040, false, 42920, 51170, false, 47840, 48140)]
    [InlineData(33740, 33740, 32940, false, 29560, 35370, false, 33740, 33870)]
    [InlineData(23920, 23920, 23120, false, 19680, 25490, false, 23915, 23990)]
    [InlineData(28600, 28600, 27800, false, 24700, 29650, false, 28200, 28275)]
    [InlineData(20800, 20800, 20000, false, 19000, 20850, false, 20400, 20450)]
    [InlineData(21380, 21380, 20580, false, 19680, 21180, false, 20780, 20780)]
    [InlineData(10800, 10800, 10000, false, 8700, 10200, false, 10000, 10000)]
    [InlineData(10800, 10800, 10000, false, 8300, 9800, false, 9800, 9800)]
    public void 下限0なら今と同じ_手順ごと(int prevEnd, int prevEndShort, int prevLast, bool prevEndManual, int nextBegin, int nextFirst, bool nextBeginManual, int expectedEnd, int expectedBegin)
    {
        var head = HeadResolvePair(prevEnd, prevEndShort, prevLast, prevEndManual, nextBegin, nextFirst, nextBeginManual, Current);
        Assert.Equal((expectedEnd, expectedBegin), head);
        Assert.Equal(head, N3ShowTimePlanner.ResolvePair(prevEnd, prevEndShort, prevLast, prevEndManual, nextBegin, nextFirst, nextBeginManual, Current));
        Assert.Equal(head, N3ShowTimePlanner.ResolvePair(prevEnd, prevEndShort, prevLast, prevEndManual, nextBegin, nextFirst, nextBeginManual, Current, 0));
        Assert.Equal(head, N3ShowTimePlanner.ResolvePair(prevEnd, prevEndShort, prevLast, prevEndManual, nextBegin, nextFirst, nextBeginManual, Current, -100));
        Assert.Equal(head, N3ShowTimePlanner.ResolvePairForAnalysis(prevEnd, prevEndShort, prevLast, prevEndManual, nextBegin, nextFirst, nextBeginManual, Current));
    }

    [Fact]
    public void 下限0なら今と同じ_いろいろな入力と設定()
    {
        var settings = new[]
        {
            Current,
            new N3ShowTimeSettings { LeadMs = 1800, TailMs = 1000, IntervalMs = 300, ProtectMs = 600 },
            new N3ShowTimeSettings { LeadMs = 1000, TailMs = 500, IntervalMs = 0 },
            new N3ShowTimeSettings { LeadMs = 0, TailMs = 0, IntervalMs = 250, ProtectMs = 0 },
        };
        var rng = new Random(20261002);
        for (int n = 0; n < 20000; n++)
        {
            var s = settings[n % settings.Length];
            int prevLast = rng.Next(0, 60000);
            int prevEndShort = prevLast + rng.Next(-500, 1500);
            int prevEnd = prevEndShort + (rng.Next(3) == 0 ? rng.Next(0, 3000) : 0);
            int nextFirst = prevLast + rng.Next(-3000, 4000);
            int nextBegin = nextFirst - s.LeadMs - rng.Next(0, 3000);
            bool prevEndManual = rng.Next(4) == 0, nextBeginManual = rng.Next(4) == 0;
            var head = HeadResolvePair(prevEnd, prevEndShort, prevLast, prevEndManual, nextBegin, nextFirst, nextBeginManual, s);
            Assert.Equal(head, N3ShowTimePlanner.ResolvePair(prevEnd, prevEndShort, prevLast, prevEndManual, nextBegin, nextFirst, nextBeginManual, s));
            Assert.Equal(head, N3ShowTimePlanner.ResolvePair(prevEnd, prevEndShort, prevLast, prevEndManual, nextBegin, nextFirst, nextBeginManual, s, 0));
        }
    }

    [Theory]
    // (3) 次の行のワイプ前を下限 1000 まで（1025 残る）。前の行のワイプ後は保護時間 400
    [InlineData(22300, 21500, 23000, 1000, 21900, 21975)]
    // (3') 表示間隔を 0 まで（50 残る）。ワイプ後は保護時間 400、ワイプ前は下限 1000
    [InlineData(22350, 21550, 23000, 1000, 21950, 22000)]
    // (4) 前の行のワイプ後を 0 まで（200・0 残る）。ワイプ前は下限（1000・1500）
    [InlineData(22600, 21800, 23000, 1000, 22000, 22000)]
    [InlineData(23300, 22500, 24000, 1500, 22500, 22500)]
    // (5) 下限 300 が保護時間 400 より短い: 前の行のワイプ終了で切り替える（ワイプ前 350）
    [InlineData(23450, 22650, 23000, 300, 22650, 22650)]
    public void 下限つき_次の行のワイプ前は下限を残し足りない分は前の行を削る(int prevEnd, int prevLast, int nextFirst, int minPre, int expectedEnd, int expectedBegin)
    {
        var (end, begin) = N3ShowTimePlanner.ResolvePair(prevEnd, prevEnd, prevLast, false, 20000, nextFirst, false, Current, minPre);
        Assert.Equal((expectedEnd, expectedBegin), (end, begin));
        Assert.True(nextFirst - begin >= minPre, "次の行のワイプ前が下限を割った");
        // 下限が無いとき（同じ歌い出し）より次の行を遅らせることはない
        Assert.True(begin <= N3ShowTimePlanner.ResolvePair(prevEnd, prevEnd, prevLast, false, 20000, nextFirst, false, Current).NextBegin);
    }

    [Theory]
    // (5) 下限を残すと前の行のワイプ終了に間に合わない: 前の行をワイプの最後まで見せ、そのワイプ終了で切り替える
    //     （次の行のワイプ前は 250・1000 で、下限 300・1500 より短い）
    [InlineData(23550, 22750, 23000, 300, 22750, 22750)]
    [InlineData(23800, 23000, 24000, 1500, 23000, 23000)]
    // (6) 歌い出しが前の行のワイプ終了より前: 歌い出しで切り替える（前の行はワイプの途中で消える。下限が無いときと同じ）
    [InlineData(24800, 24000, 23000, 1000, 23000, 23000)]
    public void 下限つき_前の行のワイプ終了に間に合わないときは前の行をワイプの最後まで見せる(int prevEnd, int prevLast, int nextFirst, int minPre, int expectedEnd, int expectedBegin)
    {
        var (end, begin) = N3ShowTimePlanner.ResolvePair(prevEnd, prevEnd, prevLast, false, 20000, nextFirst, false, Current, minPre);
        Assert.Equal((expectedEnd, expectedBegin), (end, begin));
        Assert.True(nextFirst - begin < minPre, "下限を残せる組");
        Assert.Equal(Math.Min(prevLast, nextFirst), end); // 前の行はワイプ終了（歌い出しが先ならそこ）まで見せる
    }

    [Fact]
    public void 下限つき_前の行の終了が手動なら手動の終了を守る()
    {
        // 前の行の終了（手動 23000）より前には出せない: 下限 1500（22500 まで）を割って 23000 に出る（下限 0 なら表示間隔 1/4 を残して 23075）
        Assert.Equal((23000, 23000), N3ShowTimePlanner.ResolvePair(23000, 23000, 22500, true, 20500, 24000, false, Current, 1500));
        Assert.Equal((23000, 23075), N3ShowTimePlanner.ResolvePair(23000, 23000, 22500, true, 20500, 24000, false, Current));
        // 手動の終了の後でも下限を残せるなら、今と同じ（ワイプ前 1700）
        Assert.Equal((22000, 22300), N3ShowTimePlanner.ResolvePair(22000, 22000, 21500, true, 20500, 24000, false, Current, 1500));
        // 次の行の開始が手動なら下限は使わない（前の行の終了だけで調整する）
        Assert.Equal((20580, 19680), N3ShowTimePlanner.ResolvePair(21380, 21380, 20580, false, 19680, 21180, true, Current, 1000));
    }

    /// <summary>
    /// 例 3 の形（2 行ページ 2 枚）で、前のページの上段のワイプ終了と、次のページの上段の行頭の絵文字（22000 から）の基準時刻 T を差し替えた歌詞。
    /// </summary>
    private static LyricsDocument FloorExample(int prevLastCs, int tCs) => Lyrics(
        "@Emoji=（花帆）,a.png",
        $"[00:20:00]あ{Tag(prevLastCs)}",
        "[00:21:00]い[00:26:00]",
        "",
        $"[00:22:00]（花帆）{Tag(tCs)}う[00:30:00]",
        "[00:31:00]え[00:33:00]");

    private static string Tag(int cs) => $"[{cs / 6000:00}:{cs / 100 % 60:00}:{cs % 100:00}]";

    /// <summary>寄せた後の歌詞の、上段の行頭の絵文字の開始（ms）。</summary>
    private static int EmojiStartMs(LyricsDocument clamped) =>
        N3EmojiLead.FindLeads(clamped.Lines[3], new EmojiMatcher(new[] { "（花帆）" })).Single().Ecs * 10;

    [Theory]
    // (0) 前の行の終了＋表示間隔まで遅らせるだけで足りる（絵文字 1.9 秒）
    [InlineData(2100, 21800, 22100)]
    // (2) 前の行のワイプ後を保護時間まで
    [InlineData(2190, 22425, 22500)]
    // (3') 表示間隔を 0 まで（下限がワイプ前の表示時間と同じなので、(3) は (2) と同じ条件になり通らない）
    [InlineData(2205, 22450, 22500)]
    // (4) 前の行のワイプ後を 0 まで（ちょうど 0 になる 2250 まで。2250 より後は 下限つき_前の行のワイプ終了に間に合わない組だけ…）
    [InlineData(2230, 22500, 22500)]
    [InlineData(2250, 22500, 22500)]
    public void 下限つき_2秒の絵文字は手順を進めても1点5秒を残す(int prevLastCs, int expectedPrevEnd, int expectedBegin)
    {
        var (plans, clamped) = N3EmojiLead.PrepareTab(FloorExample(prevLastCs, 2400), Yield("（花帆）"));
        Assert.Equal((expectedPrevEnd, expectedBegin), (plans[0].EndMs, plans[3].BeginMs));
        Assert.True(24000 - EmojiStartMs(clamped) >= 1500);
    }

    [Theory]
    // (2) (3) (4)。表示開始は絵文字の開始（22000）より後にならない
    // （前の行のワイプ終了が 22000 より後で間に合わない組は 下限つき_前の行のワイプ終了に間に合わない組だけ… で確かめる）
    [InlineData(2100, 21425, 21500)]
    [InlineData(2150, 21900, 21975)]
    [InlineData(2180, 22000, 22000)]
    public void 下限つき_表示秒数1秒の絵文字は縮めない(int prevLastCs, int expectedPrevEnd, int expectedBegin)
    {
        var doc = FloorExample(prevLastCs, 2300);
        var (plans, clamped) = N3EmojiLead.PrepareTab(doc, Yield("（花帆）"));
        Assert.Equal((expectedPrevEnd, expectedBegin), (plans[0].EndMs, plans[3].BeginMs));
        Assert.Equal(LrcFormat.WriteLyricLine(doc.Lines[3]), LrcFormat.WriteLyricLine(clamped.Lines[3]));
    }

    [Theory]
    // (5) 2 秒の絵文字: 前の行のワイプ終了（23000・23300）から歌い出し（24000）まで 1.0・0.7 秒。
    //     前の行をワイプの最後まで見せて、そこで切り替える（絵文字は 1.0・0.7 秒）
    [InlineData(2300, 2400, 23000)]
    [InlineData(2330, 2400, 23300)]
    // (5) 1 秒の絵文字: 前の行のワイプ終了（22500）から歌い出し（23000）まで 0.5 秒（絵文字は 0.5 秒）
    [InlineData(2250, 2300, 22500)]
    // (6) 歌い出し（24000）が前の行のワイプ終了（24500）より前: 歌い出しで切り替える（絵文字は 0 秒。前の行はワイプの途中で消える）
    [InlineData(2450, 2400, 24000)]
    public void 下限つき_前の行のワイプ終了に間に合わない組だけ絵文字を下限より短くする(int prevLastCs, int tCs, int expected)
    {
        var (plans, clamped) = N3EmojiLead.PrepareTab(FloorExample(prevLastCs, tCs), Yield("（花帆）"));
        Assert.Equal((expected, expected), (plans[0].EndMs, plans[3].BeginMs));
        Assert.Equal(expected, EmojiStartMs(clamped));
    }

    [Theory]
    [InlineData(2230)] // 0.3 秒（下限が保護時間より短い。(5) まで進む）
    [InlineData(2300)] // 1 秒
    [InlineData(2400)] // 2 秒
    [InlineData(2500)] // 3 秒
    public void 下限つき_絵文字は前の行をワイプの最後まで見せる分しか下限を割らず前の行は今より削らない(int tCs)
    {
        const int e = 22000;
        int t = tCs * 10;
        int floor = Math.Min(1500, t - e);
        for (int prevLastCs = 2010; prevLastCs <= 2550; prevLastCs += 5)
        {
            var doc = FloorExample(prevLastCs, tCs);
            var (plans, clamped) = N3EmojiLead.PrepareTab(doc, Yield("（花帆）"));
            var now = N3ShowTimePlanner.Plan(doc, Current);
            string at = $"前の行のワイプ終了 {prevLastCs * 10}・基準時刻 {t}";
            // 下限を割るのは、前の行のワイプ終了から歌い出しまでが下限より短い組だけ（そのときはワイプ終了から歌い出しまで残す）
            int keep = Math.Min(floor, Math.Max(0, t - prevLastCs * 10));
            Assert.True(t - EmojiStartMs(clamped) >= keep, $"{at}: 絵文字 {t - EmojiStartMs(clamped)}ms が {keep}ms を割った");
            Assert.True(plans[0].EndMs >= Math.Min(prevLastCs * 10, t), $"{at}: 前の行が避けられるのにワイプの途中で消えた");
            Assert.True(plans[3].BeginMs <= t, $"{at}: 本当の歌い出しより後");
            Assert.True(plans[0].EndMs >= now[0].EndMs, $"{at}: 前の行が今（規則オフ）より削られた");
        }
    }

    [Fact]
    public void 下限つき_前の行の表示終了が手動なら手動の終了が勝ち絵文字は下限を割る()
    {
        var doc = FloorExample(2250, 2400);
        doc.Lines[0].ShowEndCs = 2300; // 前の行の表示終了を 23000 に手動指定（自動ならワイプ終了の 22500 で消える）
        var (plans, clamped) = N3EmojiLead.PrepareTab(doc, Yield("（花帆）"));
        Assert.Equal(23000, plans[0].EndMs);
        Assert.True(plans[0].EndIsManual);
        Assert.Equal(23000, plans[3].BeginMs);
        Assert.Equal("[00:23:00]（花帆）[00:24:00]う[00:30:00]", LrcFormat.WriteLyricLine(clamped.Lines[3])); // 絵文字は 1.0 秒（下限 1.5 秒より短い）
    }

    // ------------------------------------------------------------ 同じ段の行を重ねてよい時間（OverlapMs）

    private static N3ShowTimeSettings WithOverlap(N3ShowTimeSettings s, int overlapMs)
    {
        var c = N3ProjWriter.CloneShowSettings(s);
        c.OverlapMs = overlapMs;
        return c;
    }

    [Theory]
    // 前の行: ワイプ終了 22200・表示終了 23000（ワイプ後 800）。重ねてよい時間 500ms（表示間隔 300・その 1/4 は 75・保護時間 400）
    // (1) 前の行のワイプ後 800・次の行のワイプ前 1500 のまま 200ms 重ねる（重ねなければ (2) で前の行を 22725 で消す）
    [InlineData(24300, 23000, 22800)]
    // (2) 前の行のワイプ後を 625 に（425ms 重ねる）
    [InlineData(23900, 22825, 22400)]
    // (3) 次の行のワイプ前を 825 に（前の行のワイプ後は保護時間 400）
    [InlineData(23000, 22600, 22175)]
    // (3') ワイプ後・ワイプ前とも保護時間 400（450ms 重ねる）
    [InlineData(22550, 22600, 22150)]
    // (4) 前の行のワイプ後を 200・50 に（次の行のワイプ前は保護時間 400）
    [InlineData(22300, 22400, 21900)]
    [InlineData(22150, 22250, 21750)]
    // (5) 前の行はワイプ終了で消え、次の行はその 500ms 前から
    [InlineData(22000, 22200, 21700)]
    // (6) 歌い出し（21600）の 500ms 後まで前の行を残す（前の行はワイプの途中で消える）
    [InlineData(21600, 22100, 21600)]
    public void 重ねてよい時間_手順ごとに前の行と次の行の間隔をその分だけ減らす(int nextFirst, int expectedEnd, int expectedBegin)
    {
        var s = WithOverlap(Current, 500);
        var (end, begin) = N3ShowTimePlanner.ResolvePair(23000, 23000, 22200, false, 20000, nextFirst, false, s);
        Assert.Equal((expectedEnd, expectedBegin), (end, begin));
        Assert.True(end - begin <= 500, "重ねてよい時間より長く重なった");
        // 重ねない計算（ニコカラメーカー3 と同じ）より、前の行を削ったり次の行を遅らせたりはしない
        var (end0, begin0) = N3ShowTimePlanner.ResolvePair(23000, 23000, 22200, false, 20000, nextFirst, false, Current);
        Assert.True(end >= end0 && begin <= begin0, $"重ねない計算 ({end0}, {begin0}) より削った・遅らせた");
    }

    [Fact]
    public void 重ねてよい時間_重ならずに済む組は今と同じ()
    {
        var s = WithOverlap(Current, 500);
        // (0) 前の行の終了＋表示間隔まで次の行を遅らせるだけで足りる組は、重ねない
        Assert.Equal((23000, 23300), N3ShowTimePlanner.ResolvePair(23000, 23000, 22200, false, 20000, 24900, false, s));
        // 重ならない組はそのまま
        Assert.Equal((23000, 23500), N3ShowTimePlanner.ResolvePair(23000, 23000, 22200, false, 23500, 25000, false, s));
    }

    [Fact]
    public void 重ねてよい時間_手動指定の側は動かさず重ねてよい分まで重ねる()
    {
        var s = WithOverlap(Current, 500);
        // 次の行の開始が手動（22500）: 前の行は 22925 まで残る（重ねなければ 22500 で消える）
        Assert.Equal((22925, 22500), N3ShowTimePlanner.ResolvePair(23000, 23000, 22200, false, 22500, 24000, true, s));
        Assert.Equal((22500, 22500), N3ShowTimePlanner.ResolvePair(23000, 23000, 22200, false, 22500, 24000, true, Current));
        // 前の行の終了が手動（23000）: 次の行は 22575 から出る（重ねなければ 23075）
        Assert.Equal((23000, 22575), N3ShowTimePlanner.ResolvePair(23000, 23000, 22200, true, 20000, 24000, false, s));
        Assert.Equal((23000, 23075), N3ShowTimePlanner.ResolvePair(23000, 23000, 22200, true, 20000, 24000, false, Current));
    }

    [Fact]
    public void 重ねてよい時間_増やすほど前の行は残り次の行は早く出て_重なりは許した分まで()
    {
        var settings = new[]
        {
            Current,
            new N3ShowTimeSettings { LeadMs = 1800, TailMs = 1000, IntervalMs = 300, ProtectMs = 600 },
            new N3ShowTimeSettings { LeadMs = 1000, TailMs = 500, IntervalMs = 0 },
        };
        var rng = new Random(20261003);
        for (int n = 0; n < 20000; n++)
        {
            var s = settings[n % settings.Length];
            int prevLast = rng.Next(0, 60000);
            int prevEndShort = prevLast + rng.Next(0, 1500);
            int prevEnd = prevEndShort + (rng.Next(3) == 0 ? rng.Next(0, 3000) : 0);
            int nextFirst = prevLast + rng.Next(-3000, 4000);
            int nextBegin = nextFirst - s.LeadMs - rng.Next(0, 3000);
            int minPre = rng.Next(3) == 0 ? rng.Next(0, s.LeadMs + 1) : 0;
            int x1 = rng.Next(0, 1500), x2 = x1 + rng.Next(0, 1500);
            var a = N3ShowTimePlanner.ResolvePair(prevEnd, prevEndShort, prevLast, false, nextBegin, nextFirst, false, WithOverlap(s, x1), minPre);
            var b = N3ShowTimePlanner.ResolvePair(prevEnd, prevEndShort, prevLast, false, nextBegin, nextFirst, false, WithOverlap(s, x2), minPre);
            string at = $"n={n} prevEnd={prevEnd} short={prevEndShort} last={prevLast} nextBegin={nextBegin} first={nextFirst} minPre={minPre} x={x1}/{x2}";
            Assert.True(b.PrevEnd >= a.PrevEnd, $"{at}: 前の行の終了が早まった {a} → {b}");
            Assert.True(b.NextBegin <= a.NextBegin, $"{at}: 次の行の開始が遅れた {a} → {b}");
            Assert.True(b.PrevEnd - b.NextBegin <= x2, $"{at}: 重なり {b.PrevEnd - b.NextBegin} が {x2} を超えた");
        }
    }

    [Fact]
    public void 重ねてよい時間_絵文字の行でも前の行のワイプ後を残して重ねる()
    {
        // 例 3: 前の行のワイプ終了 22500、次の行の絵文字 22000〜24000。重ねなければ前の行はワイプ終了ちょうど（22500）で消える
        var doc = FloorExample(2250, 2400);
        var (plans0, _) = N3EmojiLead.PrepareTab(doc, Yield("（花帆）"));
        Assert.Equal((22500, 22500), (plans0[0].EndMs, plans0[3].BeginMs));

        var s = Yield("（花帆）");
        s.OverlapMs = 500;
        var (plans, clamped) = N3EmojiLead.PrepareTab(doc, s);
        Assert.Equal((22925, 22500), (plans[0].EndMs, plans[3].BeginMs)); // 前の行のワイプ後 425ms を残し、425ms 重ねる
        Assert.Equal(22500, EmojiStartMs(clamped)); // 絵文字はワイプ前の表示時間 1.5 秒を残す
    }
}
