using System.Text.Json.Nodes;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;
using NicoKaraPrep.Core.Validation;

namespace NicoKaraPrep.Core.Tests;

/// <summary>
/// 前後のページの行を場所で組にすること（NicoKaraPrep の機能。同じ段は描画範囲、段が違えばレイアウトが違うページの間の文字の枠）と、
/// 1 行だけのページの段を表示時刻の計算で決めること、タブごとに最初のフォントから決め直すこと（ニコカラメーカー3 と同じ）の確認。
/// </summary>
public class N3RowPlacementTests
{
    private static LyricsDocument Lyrics(params string[] lines) => LrcFormat.Parse(string.Join("\r\n", lines) + "\r\n");

    /// <summary>ワイプ前 1500 / ワイプ後 800 / 表示間隔 300。</summary>
    private static N3ShowTimeSettings Current() => new() { LeadMs = 1500, TailMs = 800, IntervalMs = 300 };

    /// <summary>画面の横いっぱい（50〜1870）の行の四角。</summary>
    private static N3LineBounds Row(int top, int bottom) => new(50, top, 1870, bottom);

    /// <summary>行の場所（文字の枠を省くと描画範囲と同じ）。</summary>
    private static N3LineArea Area(N3LineBounds ink, N3LineBounds? box = null, int layout = 0) => new(ink, box ?? ink, layout);

    /// <summary>前のページ（0・1 行目）は画面の上の方、次のページ（3・4 行目）は下の方に出る（上下に重ならない）。</summary>
    private static Dictionary<int, N3LineArea> Apart() => new()
    {
        [0] = Area(Row(100, 200)), [1] = Area(Row(250, 350)), [3] = Area(Row(800, 900)), [4] = Area(Row(950, 1050)),
    };

    /// <summary>
    /// 2 行のページ 2 枚。上の段（0 → 3 行目）は、前の行の表示終了（12000+800）が次の行の表示開始（12500−1500）より後、
    /// 下の段（1 → 4 行目）も 11000+800 &gt; 11000（同じ段なら詰める）。
    /// </summary>
    private static LyricsDocument TwoPages() => Lyrics(
        "[00:10:00]あ[00:12:00]",
        "[00:10:50]か[00:11:00]",
        "",
        "[00:12:50]い[00:14:00]",
        "[00:13:00]き[00:13:50]");

    [Fact]
    public void 組_同じ段は描画範囲_段が違えばレイアウトが違うときだけ文字の枠()
    {
        // 上の段の行（描画範囲は下に 10px はみ出す）と下の段の行（ルビで描画範囲が上に 10px はみ出す）。文字の枠は行間 20px 離れている
        var upper = Area(Row(300, 410), Row(300, 400));
        var lower = Area(Row(410, 530), Row(420, 530));
        Assert.True(N3RowPlacement.Paired(upper, 2, upper, 2));
        Assert.False(N3RowPlacement.Paired(upper, 2, Area(Row(410, 500)), 2)); // 同じ段でも接するだけなら組にしない
        Assert.True(N3RowPlacement.Paired(upper, 2, Area(Row(405, 500)), 2));  // 同じ段は描画範囲が少しでも重なれば組
        // 段が違う: レイアウトが同じなら、描画範囲が重なっていても組にしない（隣り合う段のルビ・縁が触れるのはレイアウトどおり）
        var touching = upper with { Ink = Row(300, 415) };
        Assert.False(N3RowPlacement.Paired(lower, 1, touching, 2));
        // レイアウトが違えば、文字の枠が重なるときだけ組
        Assert.False(N3RowPlacement.Paired(lower, 1, touching with { Layout = 1 }, 2));
        Assert.True(N3RowPlacement.Paired(lower, 1, Area(Row(400, 500), layout: 1), 2));
        // 左右に離れていれば組にしない（左寄せ 5 行と右寄せ 5 行のページ）
        Assert.False(N3RowPlacement.Paired(Area(new(50, 0, 700, 100)), 1, Area(new(1200, 0, 1870, 100)), 1));

        var areas = new Dictionary<int, N3LineArea> { [0] = lower, [1] = upper, [2] = Area(Row(420, 530)), [3] = Area(Row(300, 400), layout: 1) };
        N3LineArea? At(int line, int row) => areas.TryGetValue(line, out var a) ? a : null;
        List<(int, int)> Pairs((int, int)[] prev, (int, int)[] next, bool useAreas = true) =>
            N3RowPlacement.Pairs(prev, next, useAreas ? At : null);
        // 場所を使わなければ同じ段どうし
        Assert.Equal(new[] { (0, 2) }, Pairs(new[] { (0, 1) }, new[] { (2, 1), (3, 2) }, useAreas: false));
        // 場所の分からない行（9）は同じ段どうし
        Assert.Equal(new[] { (0, 9) }, Pairs(new[] { (0, 1) }, new[] { (9, 1) }));
        Assert.Empty(Pairs(new[] { (0, 1) }, new[] { (9, 2) }));
        // 次の行の段の順、同じ次の行の中は前の行の段の順
        Assert.Equal(new[] { (0, 2), (1, 3) }, Pairs(new[] { (1, 2), (0, 1) }, new[] { (3, 2), (2, 1) }));
    }

    [Fact]
    public void 表示時刻_別の場所に出る行は詰めない_重なる行は今までどおり()
    {
        var doc = TwoPages();
        var same = N3ShowTimePlanner.Plan(doc, Current());
        Assert.True(same[0].Adjusted && same[3].Adjusted);

        var apart = Current();
        apart.LineAreas = Apart();
        var plans = N3ShowTimePlanner.Plan(doc, apart);
        // ページの歌い出しの 1.5 秒前〜行の歌い終わりの 0.8 秒後のまま
        Assert.Equal((8500, 12800), (plans[0].BeginMs, plans[0].EndMs));
        Assert.Equal((8500, 11800), (plans[1].BeginMs, plans[1].EndMs));
        Assert.Equal((11000, 14800), (plans[3].BeginMs, plans[3].EndMs));
        Assert.Equal((11000, 14300), (plans[4].BeginMs, plans[4].EndMs));
        Assert.DoesNotContain(plans.Values, p => p.Adjusted);

        // 上の段どうしだけ上下に重なる（同じ場所）なら、その組は今までどおり詰める
        var overlap = Current();
        overlap.LineAreas = new Dictionary<int, N3LineArea>
        {
            [0] = Area(Row(100, 200)), [1] = Area(Row(250, 350)), [3] = Area(Row(150, 250)), [4] = Area(Row(950, 1050)),
        };
        var o = N3ShowTimePlanner.Plan(doc, overlap);
        Assert.Equal((same[0].EndMs, same[3].BeginMs), (o[0].EndMs, o[3].BeginMs));
        Assert.Equal((11800, 11000), (o[1].EndMs, o[4].BeginMs));
    }

    [Fact]
    public void ページ衝突_別の場所に出る行は知らせない()
    {
        var doc = TwoPages();
        var settings = new PageCollisionSettings(); // 表示前 1.5 秒・表示後 0.5 秒: 1250 > 1100 で重なる
        Assert.NotEmpty(PageRowCollisionValidator.Validate(doc, settings));
        settings.LineAreas = Apart();
        Assert.Empty(PageRowCollisionValidator.Validate(doc, settings));
    }

    [Fact]
    public void チェック表示時刻_別の場所に出る行は組にしない()
    {
        // N3ShowTimeValidatorTests の Example3: 上の段の次の行の絵文字（22000）で、前の行がワイプの途中（22500 の手前）で消える
        var doc = Lyrics(
            "@Emoji=（花帆）,a.png",
            "[00:20:00]あ[00:22:50]",
            "[00:21:00]い[00:26:00]",
            "",
            "[00:22:00]（花帆）[00:24:00]う[00:26:00]",
            "[00:27:00]え[00:29:00]");
        var s = Current();
        var issues = N3ShowTimeValidator.Validate(doc, N3ShowTimePlanner.Plan(doc, s), s);
        Assert.Contains(issues, i => i.Severity == IssueSeverity.Error && i.RelatedLineIndex == 0);

        s.LineAreas = Apart();
        var apart = N3ShowTimeValidator.Validate(doc, N3ShowTimePlanner.Plan(doc, s), s);
        Assert.Empty(apart);
    }

    [Fact]
    public void 書き出し_表示時刻の設定はタブの設定を使う()
    {
        var doc = TwoPages();
        var tabShow = Current();
        tabShow.LineAreas = Apart();
        var options = new N3ProjExportOptions
        {
            ShowTime = Current(), // 全体の設定は段だけで組にする
            DefaultFont = new N3FontSet { Name = "標準", FontFamily = "メイリオ", SizePx = 80, EdgePx = 8 },
        };
        var tab = new N3ProjExportTab { Name = "メイン", Document = doc, LyricsPath = @"C:\v\a.lrc", ShowTime = tabShow };
        var root = N3ProjWriter.BuildProjectJson(@"C:\v\a.n3proj", new[] { new N3ProjWriter.TabSource(tab, tab.LyricsPath, LrcFormat.Write(doc), DateTime.Now) },
            options, null, new List<string>(), out _, out _);
        var shown = root["SourceLyricsInfos"]![0]!["LineInfos"]!.AsArray()
            .Where(l => l!["Kind"]!.GetValue<int>() == 1)
            .Select(l => (l!["ShowBeginTime"]!.GetValue<int>(), l["ShowEndTime"]!.GetValue<int>()))
            .ToList();
        Assert.Equal(new[] { (8500, 12800), (8500, 11800), (11000, 14800), (11000, 14300) }, shown);
    }

    [Fact]
    public void ページ衝突_1行のページはプレビューで決めた段で比べる()
    {
        // 1 行のページ 2 枚。前の行の表示終了（12000+500）が次の行の表示開始（12500−1500）より後
        var doc = Lyrics("[00:10:00]あ[00:12:00]", "", "[00:12:50]い[00:14:00]");
        var settings = new PageCollisionSettings();
        // 昇格の規則（次のページまで余裕があれば下から 2 行目・最後のページは 2 行目）だけだと、前の行は 1 行目・次の行は 2 行目で比べない
        Assert.Empty(PageRowCollisionValidator.Validate(doc, settings));
        // プレビューで決めた段（前の行は 1 行目・次の行は前の行がまだ出ているので 2 行目）でも、段が違うので比べない
        settings.SingleLineRows = new Dictionary<int, int> { [0] = 1, [2] = 2 };
        Assert.Empty(PageRowCollisionValidator.Validate(doc, settings));
        // プレビューで決めた段がどちらも 1 行目なら、昇格の規則より優先して比べる
        settings.SingleLineRows = new Dictionary<int, int> { [0] = 1, [2] = 1 };
        var issue = Assert.Single(PageRowCollisionValidator.Validate(doc, settings));
        Assert.Contains("下から1行目", issue.Message);
    }

    // ------------------------------------------------------------ 段の違う組・1 行だけのページの段

    /// <summary>2 行のページの上の段・下の段の場所（描画範囲は文字の枠よりルビ・縁の分だけ広く、隣り合う段の描画範囲は触れている）。</summary>
    private static readonly N3LineArea Upper = Area(Row(740, 910), Row(760, 895));
    private static readonly N3LineArea Lower = Area(Row(900, 1040), Row(915, 1030));

    /// <summary>
    /// 2 行のページ（0 上の段（Ｂ）・1 下の段（Ａ））の次に、絵文字 2 つの行だけのページ（3 行目）。
    /// 3 行目の絵文字の先行タグ 202.42 秒、本当の歌い出し 205.42 秒。下の段（1 行目）のワイプ終了は 205.25 秒。
    /// </summary>
    private static LyricsDocument SinglePageAfterTwo(string lowerLine = "[03:19:92]（Ａ）[03:22:92]あ[03:24:81]だ[03:25:25]") => Lyrics(
        "[03:15:94]（Ｂ）[03:18:94]こ[03:22:25]な[03:22:74]",
        lowerLine,
        "",
        "[03:22:42]（Ａ）（Ｂ）[03:25:42]ス[03:26:90]ド[03:27:01]");

    /// <summary>ワイプ前 1500 / ワイプ後 1000 / 表示間隔 300。絵文字の分だけ遅らせる。場所は 2 行のページの上下の段と、3 行目の両方の段（同じレイアウト）。</summary>
    private static N3ShowTimeSettings SinglePageSettings() => new()
    {
        LeadMs = 1500, TailMs = 1000, IntervalMs = 300,
        EmojiLeadYield = true,
        LeadMatcher = new EmojiMatcher(new[] { "（Ａ）", "（Ｂ）", "＿" }),
        LineAreas = new Dictionary<int, N3LineArea> { [0] = Upper, [1] = Lower, [3] = Lower },
        SingleLineAreas = new Dictionary<int, N3SingleLineAreas> { [3] = new(Lower, Upper) },
    };

    [Fact]
    public void 一行のページ_上の段に上げるほうが早く出せれば上げて上の段の行と組にする()
    {
        var doc = SinglePageAfterTwo();
        var plans = N3ShowTimePlanner.Plan(doc, SinglePageSettings());
        // 下の段のままなら、下の段の行（ワイプ終了 205.25）の後にしか出せない。上の段に上げれば、上の段の行（202.74＋1.0 で消える）の後に
        // 本当の歌い出しの 1.5 秒前（203.92）から出せる（下の段の行はまだ出ているので、ニコカラメーカー3 でも上の段に出る）
        Assert.Equal(2, plans[3].Row);
        Assert.Equal(203920, plans[3].BeginMs);
        Assert.Equal(203740, plans[0].EndMs);  // 上の段の行は削らない（表示間隔の 1/4 まで詰める）
        Assert.Equal(206250, plans[1].EndMs);  // 下の段の行はそのまま（描画範囲は触れているが、同じレイアウトの隣の段なので組にしない）
        Assert.True(plans[3].EmojiYieldMs > 0);
        var s = SinglePageSettings();
        Assert.Equal(new[] { (0, 3) }, N3ShowTimePlanner.Pairs(plans, s));
        Assert.Empty(N3ShowTimeValidator.Validate(doc, plans, s));
    }

    [Fact]
    public void 一行のページ_下の段の行が先に消えていれば下の段のまま()
    {
        // 下の段の行が 201.00 に消える: 上の段に上げても、出るときには下の段の行はもう無いので、ニコカラメーカー3 では下の段に出る
        var doc = SinglePageAfterTwo("[03:16:00]（Ａ）[03:17:00]あ[03:20:00]");
        var plans = N3ShowTimePlanner.Plan(doc, SinglePageSettings());
        Assert.Equal(1, plans[3].Row);
        Assert.Equal(new[] { (1, 3) }, N3ShowTimePlanner.Pairs(plans, SinglePageSettings()));
        Assert.True(plans[3].BeginMs >= plans[1].EndMs);
    }

    [Fact]
    public void 一行のページ_場所を使わなければ今までどおり段だけ()
    {
        var doc = SinglePageAfterTwo();
        var s = SinglePageSettings();
        s.LineAreas = null;
        var plans = N3ShowTimePlanner.Plan(doc, s);
        // 昇格の規則（次のページが無い最後のページは上の段）で上の段の行と組
        Assert.Equal(2, plans[3].Row);
        Assert.Equal(new[] { (0, 3) }, N3ShowTimePlanner.Pairs(plans, s));
    }

    /// <summary>前のページは 3 行（下から 1・2・3 行目）、次のページは 2 行。</summary>
    private static LyricsDocument ThreeThenTwo() => Lyrics(
        "[00:10:00]あ[00:12:00]",
        "[00:10:50]か[00:13:00]",
        "[00:11:00]さ[00:13:50]",
        "",
        "[00:13:20]い[00:14:00]",
        "[00:13:50]き[00:15:00]");

    [Fact]
    public void 段の違う行もレイアウトが違えば文字の枠が重なるときに組にして詰める()
    {
        var doc = ThreeThenTwo();
        var s = Current();
        // 次のページはレイアウトが違い、5 行目（下から 1 行目）の文字の枠が前のページの 1 行目（下から 2 行目）と重なる
        s.LineAreas = new Dictionary<int, N3LineArea>
        {
            [0] = Area(Row(500, 600)), [1] = Area(Row(650, 750)), [2] = Area(Row(800, 900)),
            [4] = Area(Row(300, 400), layout: 1), [5] = Area(Row(700, 780), layout: 1),
        };
        var plans = N3ShowTimePlanner.Plan(doc, s);
        Assert.Equal(new[] { (1, 5) }, N3ShowTimePlanner.Pairs(plans, s));
        Assert.Equal((2, 1), (plans[1].Row, plans[5].Row));
        Assert.True(plans[5].BeginMs >= plans[1].EndMs); // 前の行が消えてから出る
        // 段だけで組にすると、下から 1 行目どうし（2 → 5 行目）・2 行目どうし（1 → 4 行目）
        var rowsOnly = Current();
        Assert.Equal(new[] { (2, 5), (1, 4) }, N3ShowTimePlanner.Pairs(N3ShowTimePlanner.Plan(doc, rowsOnly), rowsOnly));
    }

    [Fact]
    public void 段の違う行はレイアウトが同じなら描画範囲が触れていても組にしない()
    {
        // 2 行のページ 2 枚（同じレイアウト）。前のページの下の段（1 行目）がまだ歌っているあいだに、次のページの上の段（3 行目）を出す
        var doc = TwoPages();
        var s = Current();
        s.LineAreas = new Dictionary<int, N3LineArea> { [0] = Upper, [1] = Lower, [3] = Upper, [4] = Lower };
        var plans = N3ShowTimePlanner.Plan(doc, s);
        Assert.Equal(new[] { (1, 4), (0, 3) }, N3ShowTimePlanner.Pairs(plans, s));
        var collision = new PageCollisionSettings { LineAreas = s.LineAreas };
        Assert.DoesNotContain(PageRowCollisionValidator.Validate(doc, collision), i => i.Message.Contains("→2行目") || i.Message.Contains("→1行目"));
    }

    [Fact]
    public void チェック_段の違う組は両方の段を書く_重ねてよい時間までは知らせない()
    {
        var doc = Lyrics(
            "[00:10:00]あ[00:12:00]",
            "[00:10:50]か[00:13:00]",
            "",
            "[00:12:50]い[00:14:00]");
        // 3 行目（下から 1 行目・別のレイアウト）の文字の枠は、前のページの上の段（0 行目・下から 2 行目）と重なる
        var settings = new PageCollisionSettings
        {
            LineAreas = new Dictionary<int, N3LineArea> { [0] = Area(Row(500, 600)), [1] = Area(Row(650, 750)), [3] = Area(Row(550, 650), layout: 1) },
            SingleLineRows = new Dictionary<int, int> { [3] = 1 },
        };
        // 前の行の表示終了 12.00+0.50 − 次の行の表示開始 12.50−1.50 = 1.50 秒重なる
        var issue = Assert.Single(PageRowCollisionValidator.Validate(doc, settings));
        Assert.Equal(0, issue.RelatedLineIndex);
        Assert.Contains("下から2行目→1行目", issue.Message);
        settings.AllowedOverlapCs = 150; // 重ねてよい 1.5 秒: その時間までの重なりは知らせない
        Assert.Empty(PageRowCollisionValidator.Validate(doc, settings));
        settings.AllowedOverlapCs = 100; // 1.0 秒なら超えた 0.5 秒（しきい値 1 秒以下）は警告
        Assert.Equal(IssueSeverity.Warning, Assert.Single(PageRowCollisionValidator.Validate(doc, settings)).Severity);
    }

    // ------------------------------------------------------------ タブごとの最初のフォント

    [Fact]
    public void タブの最初のフォント_自動はメインが既定で2つ目以降はコーラスの名前()
    {
        var names = new[] { "（いきづらい部）", "（麻衣）", "（コーラス）" };
        Assert.Equal("（いきづらい部）", N3FontResolver.AutoStartName(true, names, "（いきづらい部）"));
        Assert.Equal("（コーラス）", N3FontResolver.AutoStartName(false, names, "（いきづらい部）"));
        Assert.Equal("標準", N3FontResolver.AutoStartName(false, new[] { "標準", "（麻衣）" }, "標準"));
    }

    [Fact]
    public void 書き出し_タブごとに最初のフォントから決め直す()
    {
        var main = Lyrics("@Emoji=（麻衣）,a.png", "[00:01:00]（麻衣）[00:01:00]歌[00:02:00]");
        var chorus = Lyrics("[00:03:00]声[00:04:00]");
        var other = Lyrics("[00:05:00]音[00:06:00]");
        var font = new N3FontSet { Name = "標準", FontFamily = "メイリオ", SizePx = 80, EdgePx = 8 };
        var options = new N3ProjExportOptions
        {
            EmojiEntries = main.EmojiEntries,
            DefaultFont = font,
            FontSets = new[] { font, new N3FontSet { Name = "（麻衣）", FontFamily = "メイリオ", SizePx = 80 }, new N3FontSet { Name = "（コーラス）", FontFamily = "メイリオ", SizePx = 80 } },
        };
        N3ProjWriter.TabSource Source(string name, LyricsDocument d, string? start) =>
            new(new N3ProjExportTab { Name = name, Document = d, LyricsPath = $@"C:\v\{name}.lrc", StartFontSetName = start }, $@"C:\v\{name}.lrc", LrcFormat.Write(d), DateTime.Now);
        var root = N3ProjWriter.BuildProjectJson(@"C:\v\a.n3proj",
            new[] { Source("メイン", main, null), Source("コーラス", chorus, "（コーラス）"), Source("ほか", other, null) },
            options, null, new List<string>(), out _, out _);
        var fontNames = root["LyricsFonts"]!.AsArray().Select(f => f!["SettingsName"]!.GetValue<string>()).ToList();
        List<string> FontsOf(int tab) => root["SourceLyricsInfos"]![tab]!["LineInfos"]!.AsArray()
            .Where(l => l!["Kind"]!.GetValue<int>() == 1)
            .SelectMany(l => l!["LyricsCharInfos"]!.AsArray().Select(c => fontNames[c!["FontIndex"]!.GetValue<int>()]))
            .Distinct().ToList();
        Assert.Equal(new[] { "（麻衣）" }, FontsOf(0));
        Assert.Equal(new[] { "（コーラス）" }, FontsOf(1)); // 前のタブの（麻衣）を引き継がない
        Assert.Equal(new[] { "標準" }, FontsOf(2));        // 指定が無ければ既定
    }

    [Fact]
    public void 読み込み_タブの文字がみな同じフォントならその名前()
    {
        static JsonObject Char(string ch, int font, int kind = 0) => new() { ["Kind"] = kind, ["Char"] = ch, ["FontIndex"] = font, ["IsRuby"] = false, ["BeginTime"] = 1000, ["EndTime"] = 2000 };
        static JsonObject Line(params JsonObject[] chars) => new() { ["Kind"] = 1, ["Raw"] = "[00:01:00]あ[00:02:00]", ["LyricsCharInfos"] = new JsonArray(chars) };
        var root = new JsonObject
        {
            ["LyricsFonts"] = new JsonArray(new JsonObject { ["SettingsName"] = "（いきづらい部）" }, new JsonObject { ["SettingsName"] = "（コーラス）" }),
            ["LyricsLayouts"] = new JsonArray(),
            ["SourceLyricsInfos"] = new JsonArray(
                new JsonObject { ["SettingsName"] = "メイン", ["LineInfos"] = new JsonArray(Line(Char("（いきづらい部）", 0, kind: 1), Char("あ", 0)), Line(Char("い", 1))) },
                new JsonObject { ["SettingsName"] = "コーラス2", ["LineInfos"] = new JsonArray(Line(Char("（", 1), Char("う", 1)), Line(Char("え", 1))) }),
        };
        var tabs = N3ProjImport.ReadSourceTabs(root);
        Assert.Null(tabs[0].FontSetName);              // メインは（いきづらい部）と（コーラス）が混ざる
        Assert.Equal("（コーラス）", tabs[1].FontSetName); // コーラス2 はみな（コーラス）
    }
}
