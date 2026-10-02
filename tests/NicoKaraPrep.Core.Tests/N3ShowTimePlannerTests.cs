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
    public void 絵文字の先行を譲る_遅らせても足りなければ前の行のワイプ後を保護時間まで残してワイプ途中で消さない()
    {
        // 2 行ページ 2 枚。上段の次の行の絵文字（22000）が、前の行のワイプ終了（22500）より前に始まる
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

        var plans = N3ShowTimePlanner.Plan(doc, Yield("（花帆）"));
        Assert.Equal(22900, plans[0].EndMs); // ワイプ後は保護時間 400
        Assert.Equal(22975, plans[3].BeginMs); // 本当の歌い出し 24000 の 1025 前（ワイプ前より短い）
        Assert.Equal(975, plans[3].EmojiYieldMs);
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
        Assert.Equal(12400, plans[0].EndMs);
        Assert.Equal(12475, plans[3].BeginMs);
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
        Assert.Equal(22975, N3ShowTimePlanner.Plan(placeholder, s)[3].BeginMs);

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
}
