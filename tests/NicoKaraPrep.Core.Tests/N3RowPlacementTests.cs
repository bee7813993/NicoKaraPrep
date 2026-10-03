using System.Text.Json.Nodes;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;
using NicoKaraPrep.Core.Validation;

namespace NicoKaraPrep.Core.Tests;

/// <summary>
/// レイアウトで位置が違う行（前後のページの同じ段でも、画面上の上下の範囲が重ならない行）を組にしないこと（NicoKaraPrep の機能）と、
/// タブごとに最初のフォントから決め直すこと（ニコカラメーカー3 と同じ）の確認。
/// </summary>
public class N3RowPlacementTests
{
    private static LyricsDocument Lyrics(params string[] lines) => LrcFormat.Parse(string.Join("\r\n", lines) + "\r\n");

    /// <summary>ワイプ前 1500 / ワイプ後 800 / 表示間隔 300。</summary>
    private static N3ShowTimeSettings Current() => new() { LeadMs = 1500, TailMs = 800, IntervalMs = 300 };

    /// <summary>前のページ（0・1 行目）は画面の上の方、次のページ（3・4 行目）は下の方に出る（上下に重ならない）。</summary>
    private static Dictionary<int, N3LineBounds> Apart() => new()
    {
        [0] = Row(100, 200), [1] = Row(250, 350), [3] = Row(800, 900), [4] = Row(950, 1050),
    };

    /// <summary>画面の横いっぱい（50〜1870）の行の四角。</summary>
    private static N3LineBounds Row(int top, int bottom) => new(50, top, 1870, bottom);

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
    public void 四角が分からなければ同じ場所_左右にも上下にも重なれば同じ場所()
    {
        var bounds = new Dictionary<int, N3LineBounds>
        {
            [0] = Row(0, 100), [1] = Row(100, 200), [2] = Row(50, 150),
            [3] = new(50, 0, 700, 100),    // 左寄せの短い行
            [4] = new(1200, 0, 1870, 100), // 同じ高さの右寄せの短い行（左右に離れている）
        };
        Assert.True(N3RowPlacement.SamePlace(null, 0, 1));
        Assert.True(N3RowPlacement.SamePlace(bounds, 0, 9));
        Assert.False(N3RowPlacement.SamePlace(bounds, 0, 1)); // 接するだけなら重ならない
        Assert.True(N3RowPlacement.SamePlace(bounds, 0, 2));
        Assert.True(N3RowPlacement.SamePlace(bounds, 2, 1));
        Assert.False(N3RowPlacement.SamePlace(bounds, 3, 4)); // 上下は重なるが左右に離れている（左寄せ 5 行と右寄せ 5 行のページ）
        Assert.True(N3RowPlacement.SamePlace(bounds, 0, 4));
    }

    [Fact]
    public void 表示時刻_別の場所に出る行は詰めない_重なる行は今までどおり()
    {
        var doc = TwoPages();
        var same = N3ShowTimePlanner.Plan(doc, Current());
        Assert.True(same[0].Adjusted && same[3].Adjusted);

        var apart = Current();
        apart.LineBounds = Apart();
        var plans = N3ShowTimePlanner.Plan(doc, apart);
        // ページの歌い出しの 1.5 秒前〜行の歌い終わりの 0.8 秒後のまま
        Assert.Equal((8500, 12800), (plans[0].BeginMs, plans[0].EndMs));
        Assert.Equal((8500, 11800), (plans[1].BeginMs, plans[1].EndMs));
        Assert.Equal((11000, 14800), (plans[3].BeginMs, plans[3].EndMs));
        Assert.Equal((11000, 14300), (plans[4].BeginMs, plans[4].EndMs));
        Assert.DoesNotContain(plans.Values, p => p.Adjusted);

        // 上の段どうしだけ上下に重なる（同じ場所）なら、その組は今までどおり詰める
        var overlap = Current();
        overlap.LineBounds = new Dictionary<int, N3LineBounds> { [0] = Row(100, 200), [1] = Row(250, 350), [3] = Row(150, 250), [4] = Row(950, 1050) };
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
        settings.LineBounds = Apart();
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

        s.LineBounds = Apart();
        var apart = N3ShowTimeValidator.Validate(doc, N3ShowTimePlanner.Plan(doc, s), s);
        Assert.Empty(apart);
    }

    [Fact]
    public void 書き出し_表示時刻の設定はタブの設定を使う()
    {
        var doc = TwoPages();
        var tabShow = Current();
        tabShow.LineBounds = Apart();
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
