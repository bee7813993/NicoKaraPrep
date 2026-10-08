using System.Text.Json.Nodes;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Tests;

public class N3ProjWriterTests
{
    private static LyricsDocument Doc(params string[] lines) => LrcFormat.Parse(string.Join("\r\n", lines) + "\r\n");

    private static N3FontResolver Fonts(params string[] names) => new(names, null, true);

    private static N3ProjWriter.LayoutResolver Layouts(params (string Name, int Count)[] layouts) =>
        new(layouts.Select((l, i) => new N3ProjLayoutInfo(l.Name, i, l.Count)).ToList(), null, null, null, new List<string>(), "t");

    private static N3SubtitleAction Action() =>
        new("SHINTA.CharFadeInFadeOut", new JsonObject { ["$type"] = "CharFadeInFadeOutSettingsModel", ["FadeInTime"] = 250 });

    private static JsonArray Build(LyricsDocument doc, N3ShowTimeSettings? show = null, N3FontResolver? fonts = null, N3ProjWriter.LayoutResolver? layouts = null)
    {
        return N3ProjWriter.BuildLineInfos(
            doc,
            show ?? new N3ShowTimeSettings(),
            doc.EmojiEntries,
            fonts ?? Fonts("標準"),
            layouts ?? Layouts(("下寄せ2行", 2)),
            Action(),
            "Ver 13.79",
            out _);
    }

    private static void AssertChar(JsonNode? node, string text, int begin, int end, int kind = 0, int? font = null)
    {
        Assert.NotNull(node);
        Assert.Equal(text, node!["Char"]!.GetValue<string>());
        Assert.Equal(begin, node["BeginTime"]!.GetValue<int>());
        Assert.Equal(end, node["EndTime"]!.GetValue<int>());
        Assert.Equal(kind, node["Kind"]!.GetValue<int>());
        if (font is int f) Assert.Equal(f, node["FontIndex"]!.GetValue<int>());
    }

    private static JsonArray Chars(JsonArray lines, int index) => lines[index]!["LyricsCharInfos"]!.AsArray();

    private static List<int> Kinds(JsonArray lines) => lines.Select(l => l!["Kind"]!.GetValue<int>()).ToList();

    // ------------------------------------------------------------ 文字の時刻

    [Fact]
    public void 文字時刻_1文字ずつタグ()
    {
        var lines = Build(Doc("[00:01:00]あ[00:02:00]い[00:03:00]"));
        var chars = Chars(lines, 0);
        Assert.Equal(2, chars.Count);
        AssertChar(chars[0], "あ", 1000, 2000);
        AssertChar(chars[1], "い", 2000, 3000);
    }

    [Fact]
    public void 文字時刻_複数文字のグループは先頭と末尾だけが時刻を持つ()
    {
        var chars = Chars(Build(Doc("[00:01:00]Break[00:02:00]")), 0);
        Assert.Equal(5, chars.Count);
        AssertChar(chars[0], "B", 1000, -1);
        AssertChar(chars[1], "r", -1, -1);
        AssertChar(chars[3], "a", -1, -1);
        AssertChar(chars[4], "k", -1, 2000);
    }

    [Fact]
    public void 文字時刻_2連タグは文字なし区間として扱う()
    {
        var chars = Chars(Build(Doc("[00:01:00]あ[00:02:00][00:03:00]い[00:04:00]")), 0);
        Assert.Equal(2, chars.Count);
        AssertChar(chars[0], "あ", 1000, 2000);
        AssertChar(chars[1], "い", 3000, 4000);
    }

    [Fact]
    public void 文字時刻_行頭のタグ無し文字は最初のタグの時刻で行末のタグ無し文字は終了なし()
    {
        var chars = Chars(Build(Doc("ab[00:01:00]c")), 0);
        Assert.Equal(3, chars.Count);
        AssertChar(chars[0], "a", 1000, -1);
        AssertChar(chars[1], "b", -1, 1000);
        AssertChar(chars[2], "c", 1000, -1);

        // 行頭の絵文字 1 つ（実プロジェクト: " [01:04:60]Day..." / "(ニジガク虹)[01:21:12]ぐ..."）
        var doc = Doc("@Emoji=（花帆）,a.png", "（花帆）[00:02:00]歌[00:03:00]");
        var emoji = Chars(Build(doc), 0);
        AssertChar(emoji[0], "（花帆）", 2000, 2000, kind: 1);
        AssertChar(emoji[1], "歌", 2000, 3000);
    }

    [Fact]
    public void 絵文字_置き換え文字列全体が1文字のインライングラフィックスになる()
    {
        var doc = Doc("@Emoji=（花帆）,a.png", "[00:01:00]（花帆）[00:01:00]＿[00:02:00]歌[00:03:00]");
        var chars = Chars(Build(doc), 0);
        Assert.Equal(3, chars.Count);
        AssertChar(chars[0], "（花帆）", 1000, 1000, kind: 1);
        AssertChar(chars[1], "＿", 1000, 2000);
        AssertChar(chars[2], "歌", 2000, 3000);
    }

    [Fact]
    public void 絵文字_直後にタグなし文字が続く場合は同じグループ()
    {
        var doc = Doc("@Emoji=（花帆）,a.png", "[00:01:00]（花帆）（[00:01:00]も[00:02:00]");
        var chars = Chars(Build(doc), 0);
        Assert.Equal(3, chars.Count);
        AssertChar(chars[0], "（花帆）", 1000, -1, kind: 1);
        AssertChar(chars[1], "（", -1, 1000);
        AssertChar(chars[2], "も", 1000, 2000);
    }

    // ------------------------------------------------------------ 行種別

    [Fact]
    public void 行種別_空行の直前にページ区切りが入り末尾には改行ぶんの空行が付く()
    {
        var lines = Build(Doc(
            "[00:01:00]あ[00:02:00]",
            "[00:02:00]い[00:03:00]",
            "",
            "[00:03:50]う[00:04:00]",
            "",
            "",
            "[00:04:50]え[00:05:00]",
            ""));
        Assert.Equal(new List<int> { 1, 1, 2, 0, 1, 2, 0, 0, 1, 0, 0 }, Kinds(lines));
        Assert.Equal("", lines[2]!["Raw"]!.GetValue<string>());
        Assert.Equal(-1, lines[2]!["ShowBeginTime"]!.GetValue<int>());
        Assert.Equal(-1, lines[2]!["LayoutIndex"]!.GetValue<int>());
        Assert.Equal("AddOnSettingsModel", lines[2]!["SubtitleActionSettings"]!["$type"]!.GetValue<string>());
    }

    [Fact]
    public void 行種別_間奏のように間隔が広い区切りは段落区切り()
    {
        var lines = Build(Doc(
            "[00:01:00]あ[00:02:00]",
            "",
            "[00:20:00]い[00:21:00]"));
        Assert.Equal(new List<int> { 1, 3, 0, 1, 0 }, Kinds(lines));
    }

    [Fact]
    public void 行種別_固定行数モードは行数ごとにページ区切り()
    {
        var show = new N3ShowTimeSettings { PageMode = PageSplitMode.FixedLineCount, FixedLineCount = 2 };
        var lines = Build(Doc(
            "[00:01:00]あ[00:02:00]",
            "[00:02:00]い[00:03:00]",
            "[00:03:00]う[00:04:00]",
            "[00:04:00]え[00:05:00]"), show);
        Assert.Equal(new List<int> { 1, 1, 2, 1, 1, 0 }, Kinds(lines));
    }

    [Fact]
    public void 行種別_先頭の空行は区切りにならない()
    {
        var lines = Build(Doc("", "[00:01:00]あ[00:02:00]"));
        Assert.Equal(new List<int> { 0, 1, 0 }, Kinds(lines));
    }

    // ------------------------------------------------------------ 表示時刻

    [Fact]
    public void 表示時刻_同じページの行は同時に表示され各行は自分の最終タグ後に消える()
    {
        var doc = Doc("[00:10:00]あ[00:12:00]", "[00:13:00]い[00:15:00]");
        var plans = N3ShowTimePlanner.Plan(doc, new N3ShowTimeSettings());
        Assert.Equal(8500, plans[0].BeginMs);
        Assert.Equal(8500, plans[1].BeginMs);
        Assert.Equal(12800, plans[0].EndMs);
        Assert.Equal(15800, plans[1].EndMs);
        Assert.False(plans[0].Adjusted);
    }

    [Fact]
    public void 表示時刻_上段を長めにすると上段もページの最後まで表示()
    {
        var doc = Doc("[00:10:00]あ[00:12:00]", "[00:13:00]い[00:15:00]");
        var plans = N3ShowTimePlanner.Plan(doc, new N3ShowTimeSettings { TopLong = true });
        Assert.Equal(15800, plans[0].EndMs);
        Assert.Equal(15800, plans[1].EndMs);
    }

    [Fact]
    public void 表示時刻_同じ段の前後の行は表示間隔ワイプ後ワイプ前の順に詰める()
    {
        // ページ1: A(上段) B(下段) / ページ2: C(上段) D(下段)
        var doc = Doc(
            "[00:10:00]あ[00:12:00]",
            "[00:13:00]い[00:20:00]",
            "",
            "[00:20:50]う[00:22:00]",
            "[00:21:00]え[00:25:00]");
        var plans = N3ShowTimePlanner.Plan(doc, new N3ShowTimeSettings());
        // B（下段）と D（下段）: 間の時間は 1000ms しかない（1500 + 800 + 300 に足りない）。
        // 表示間隔を 75 まで → B のワイプ後を保護時間 400 まで → D のワイプ前を削る（525）
        Assert.Equal(20400, plans[1].EndMs);
        Assert.True(plans[1].Adjusted);
        Assert.Equal(20475, plans[4].BeginMs);
        Assert.True(plans[4].Adjusted);
        // C は A（上段、12800 に消える）と十分離れているので希望どおり
        Assert.Equal(19000, plans[3].BeginMs);
        Assert.False(plans[3].Adjusted);
    }

    [Fact]
    public void 表示時刻_手動指定は自動計算より優先され動かされない()
    {
        var doc = Doc("[00:10:00]あ[00:12:00]", "[00:13:00]い[00:15:00]");
        doc.Lines[0].ShowBeginCs = 500;
        doc.Lines[1].ShowEndCs = 1700;
        var plans = N3ShowTimePlanner.Plan(doc, new N3ShowTimeSettings());
        Assert.Equal(5000, plans[0].BeginMs);
        Assert.True(plans[0].BeginIsManual);
        Assert.Equal(17000, plans[1].EndMs);
        Assert.True(plans[1].EndIsManual);
        // 行情報にも反映される
        var lines = Build(doc);
        Assert.Equal(5000, lines[0]!["ShowBeginTime"]!.GetValue<int>());
        Assert.Equal(17000, lines[1]!["ShowEndTime"]!.GetValue<int>());
    }

    // ------------------------------------------------------------ フォント・レイアウト

    [Fact]
    public void フォント_パート記号で切り替わり次の行にも引き継がれる()
    {
        var doc = Doc("@Emoji=（花帆）,a.png",
            "[00:01:00]（花帆）[00:01:00]歌[00:02:00]",
            "[00:02:00]詞[00:03:00]");
        var lines = Build(doc, fonts: Fonts("標準", "（花帆）"));
        AssertChar(Chars(lines, 0)[0], "（花帆）", 1000, 1000, kind: 1, font: 1);
        AssertChar(Chars(lines, 0)[1], "歌", 1000, 2000, font: 1);
        AssertChar(Chars(lines, 1)[0], "詞", 2000, 3000, font: 1);
    }

    [Fact]
    public void フォント_行の手動指定はその行だけを統一する()
    {
        var doc = Doc("@Emoji=（花帆）,a.png",
            "[00:01:00]（花帆）[00:01:00]歌[00:02:00]",
            "[00:02:00]詞[00:03:00]",
            "[00:03:00]続[00:04:00]");
        doc.Lines[1].FontSetName = "標準";
        var lines = Build(doc, fonts: Fonts("標準", "（花帆）"));
        AssertChar(Chars(lines, 1)[0], "詞", 2000, 3000, font: 0);
        AssertChar(Chars(lines, 2)[0], "続", 3000, 4000, font: 1);
    }

    [Fact]
    public void レイアウト_ページの行数と同じ行数のレイアウトを選ぶ()
    {
        var resolver = Layouts(("2行", 2), ("3行", 3), ("1行", 1));
        Assert.Equal(0, resolver.Resolve(2));
        Assert.Equal(1, resolver.Resolve(3));
        Assert.Equal(2, resolver.Resolve(1));
        Assert.Equal(1, resolver.Resolve(4)); // 無ければ最大行数のもの
    }

    [Fact]
    public void レイアウト_適用対象の範囲の外は選ばない()
    {
        // 範囲は「2行」〜「3行」。1 行のページは、範囲の外の「1行」ではなく範囲の中の「2行」（ニコカラメーカー3 の自動設定と同じ）
        var infos = new List<N3ProjLayoutInfo> { new("2行", 0, 2), new("3行", 1, 3), new("コーラス1行", 2, 1), new("4行", 3, 4) };
        var resolver = new N3ProjWriter.LayoutResolver(infos, null, "2行", "3行", new List<string>(), "t");
        Assert.Equal(0, resolver.Resolve(1));
        Assert.Equal(0, resolver.Resolve(2));
        Assert.Equal(1, resolver.Resolve(3));
        Assert.Equal(1, resolver.Resolve(4)); // 範囲の中に無ければ範囲の中で最も行数の多いもの（範囲の外の「4行」は選ばない）

        // 範囲の名前がプロジェクトに無ければ、すべてが対象
        var all = new N3ProjWriter.LayoutResolver(infos, null, "無い", "無い", new List<string>(), "t");
        Assert.Equal(2, all.Resolve(1));
        Assert.Equal(3, all.Resolve(4));
    }

    [Fact]
    public void レイアウト_固定名の指定()
    {
        var infos = new List<N3ProjLayoutInfo> { new("2行", 0, 2), new("コーラス", 1, 1) };
        var resolver = new N3ProjWriter.LayoutResolver(infos, "コーラス", null, null, new List<string>(), "t");
        Assert.Equal(1, resolver.Resolve(2));

        var warnings = new List<string>();
        var missing = new N3ProjWriter.LayoutResolver(infos, "無い", null, null, warnings, "t");
        Assert.Equal(0, missing.Resolve(2));
        Assert.Single(warnings);
    }

    // ------------------------------------------------------------ プロジェクト全体

    private static N3ProjWriter.TabSource Tab(string name, LyricsDocument doc, string path) =>
        new(new N3ProjExportTab { Name = name, Document = doc, LyricsPath = path }, path, LrcFormat.Write(doc), DateTime.Now);

    [Fact]
    public void プロジェクト全体_ベース無しでも必要な項目がそろう()
    {
        var doc = Doc("@Emoji=（花帆）,a.png", "[00:01:00]（花帆）[00:01:00]歌[00:02:00]", "[00:02:00]詞[00:03:00]");
        var options = new N3ProjExportOptions
        {
            ProjectName = "テスト",
            MediaPath = @"C:\v\song.mp4",
            EmojiEntries = doc.EmojiEntries,
            DefaultFont = new N3FontSet { Name = "標準", FontFamily = "メイリオ", SizePx = 80, EdgePx = 8 },
        };
        var warnings = new List<string>();
        var root = N3ProjWriter.BuildProjectJson(@"C:\v\テスト.n3proj", new[] { Tab("メイン", doc, @"C:\v\テスト.lrc") }, options, null, warnings, out int lines, out int fonts);

        Assert.Equal(2, lines);
        Assert.Equal(1, fonts);
        Assert.Equal("テスト", root["SettingsName"]!.GetValue<string>());
        Assert.Equal(@"C:\v\song.mp4", root["SourceInfo"]!["MoviePath"]!.GetValue<string>());
        Assert.Equal("song.mp4", root["SourceInfo"]!["MovieRelativePath"]!.GetValue<string>());
        Assert.Equal(1920, root["SourceInfo"]!["BackgroundWidth"]!.GetValue<int>());
        Assert.Equal(3, root["SourceLyricsInfos"]!.AsArray().Count);
        Assert.Equal(3, root["TitleInfos"]!.AsArray().Count);
        Assert.Equal(7, root["LyricsLayouts"]!.AsArray().Count);

        var font = root["LyricsFonts"]![0]!;
        Assert.Equal("標準", font["SettingsName"]!.GetValue<string>());
        Assert.Equal(8, font["BrushInfos"]!.AsArray().Count);
        Assert.Equal(6, font["FontInfos"]!.AsArray().Count);
        Assert.Equal("メイリオ", font["FontInfos"]![0]!["FontName"]!.GetValue<string>());
        Assert.Equal(80, font["FontInfos"]![0]!["CharSize"]!["Size"]!.GetValue<int>());
        Assert.Equal(1080, font["FontInfos"]![0]!["CharSize"]!["Reference"]!.GetValue<int>());
        Assert.Equal("FFFFFF", font["BrushInfos"]![0]!["SolidColor"]!["Web16"]!.GetValue<string>());

        var info = root["SourceLyricsInfos"]![0]!;
        Assert.Equal("メイン", info["SettingsName"]!.GetValue<string>());
        Assert.Equal("テスト.lrc", info["SourceLyricsRelativePath"]!.GetValue<string>());
        Assert.StartsWith("@Emoji=（花帆）,a.png", info["AtTagsForSave"]!.GetValue<string>());
        Assert.Equal("SHINTA.EmptyLineBreaker", info["LastSelectedAddOns"]!["LineBreakerId"]!.GetValue<string>());
        var line = info["LineInfos"]![0]!;
        Assert.Equal("SHINTA.CharFadeInFadeOut", line["SubtitleActionId"]!.GetValue<string>());
        // 型判別子は最初のプロパティでなければならない
        Assert.Equal("$type", line["SubtitleActionSettings"]!.AsObject().First().Key);
        Assert.Equal("CharFadeInFadeOutSettingsModel", line["SubtitleActionSettings"]!["$type"]!.GetValue<string>());
        Assert.Equal("[00:01:00]（花帆）[00:01:00]歌[00:02:00]", line["Raw"]!.GetValue<string>());
        Assert.Equal(1, root["SourceLyricsInfos"]![0]!["LineInfos"]![0]!["LayoutIndex"]!.GetValue<int>()); // 下寄せ2行
    }

    [Fact]
    public void プロジェクト全体_ベースの同名フォント設定を上書きし無い名前は追加する()
    {
        var doc = Doc("[00:01:00]あ[00:02:00]");
        var scratch = new N3ProjExportOptions { DefaultFont = new N3FontSet { Name = "標準", FontFamily = "メイリオ" } };
        var baseRoot = N3ProjWriter.BuildProjectJson(@"C:\v\a.n3proj", new[] { Tab("メイン", doc, @"C:\v\a.lrc") }, scratch, null, new List<string>(), out _, out _);
        string baseGuid = baseRoot["LyricsFonts"]![0]!["Guid"]!.GetValue<string>();

        var options = new N3ProjExportOptions
        {
            BaseProject = baseRoot,
            FontSets = new[]
            {
                new N3FontSet { Name = "標準", FontFamily = "游ゴシック", SizePx = 90, TextColorAfter = "FF0000", EdgeColorAfter = "" },
                new N3FontSet { Name = "（花帆）", FontFamily = "メイリオ" },
            },
        };
        var root = N3ProjWriter.BuildProjectJson(@"C:\v\b.n3proj", new[] { Tab("メイン", doc, @"C:\v\b.lrc") }, options, baseRoot, new List<string>(), out _, out int fonts);
        Assert.Equal(2, fonts);
        var first = root["LyricsFonts"]![0]!;
        Assert.Equal(baseGuid, first["Guid"]!.GetValue<string>()); // 同じ設定を更新している
        Assert.Equal("游ゴシック", first["FontInfos"]![0]!["FontName"]!.GetValue<string>());
        Assert.Equal(90, first["FontInfos"]![0]!["CharSize"]!["Size"]!.GetValue<int>());
        Assert.Equal("FF0000", first["BrushInfos"]![0]!["SolidColor"]!["Web16"]!.GetValue<string>());
        Assert.Equal("000000", first["BrushInfos"]![1]!["SolidColor"]!["Web16"]!.GetValue<string>()); // 空は維持
        Assert.Equal("（花帆）", root["LyricsFonts"]![1]!["SettingsName"]!.GetValue<string>());
        Assert.Equal(1, root["LyricsFonts"]![1]!["Index"]!.GetValue<int>());
    }

    [Fact]
    public void プロジェクト全体_フォント設定名の並びは書き出すプロジェクトと同じ()
    {
        var doc = Doc("[00:01:00]あ[00:02:00]");
        static List<string> NamesOf(JsonObject root) => root["LyricsFonts"]!.AsArray().Select(n => n!["SettingsName"]!.GetValue<string>()).ToList();
        var scratch = new N3ProjExportOptions
        {
            DefaultFont = new N3FontSet { Name = "標準" },
            FontSets = new[] { new N3FontSet { Name = "標準" }, new N3FontSet { Name = "（花帆）" } },
        };
        var baseRoot = N3ProjWriter.BuildProjectJson(@"C:\v\a.n3proj", new[] { Tab("メイン", doc, @"C:\v\a.lrc") }, scratch, null, new List<string>(), out _, out _);
        var baseNames = NamesOf(baseRoot);
        Assert.Equal(new[] { "標準", "（花帆）" }, baseNames);

        var sets = new[] { new N3FontSet { Name = "（麻衣）" }, new N3FontSet { Name = "（花帆）" }, new N3FontSet { Name = " " }, new N3FontSet { Name = "（麻衣）" } };
        foreach (bool merge in new[] { true, false })
        {
            foreach (bool withBase in new[] { true, false })
            {
                var root = withBase ? baseRoot.DeepClone().AsObject() : null;
                var options = new N3ProjExportOptions { BaseProject = root, FontSets = sets, MergeFontSets = merge, DefaultFont = new N3FontSet { Name = "標準" } };
                var built = N3ProjWriter.BuildProjectJson(@"C:\v\b.n3proj", new[] { Tab("メイン", doc, @"C:\v\b.lrc") }, options, root, new List<string>(), out _, out _);
                Assert.Equal(NamesOf(built), N3ProjWriter.ExportFontNames(withBase ? baseNames : Array.Empty<string>(), sets, merge));
            }
        }
        Assert.Equal(new[] { "標準", "（花帆）", "（麻衣）" }, N3ProjWriter.ExportFontNames(baseNames, sets, true));
        Assert.Equal(new[] { "（麻衣）", "（花帆）" }, N3ProjWriter.ExportFontNames(Array.Empty<string>(), sets, true));
        Assert.Equal(new[] { "標準" }, N3ProjWriter.ExportFontNames(Array.Empty<string>(), sets, false));
    }

    [Fact]
    public void プロジェクト全体_ZIPに保存して読み戻せる()
    {
        string dir = Path.Combine(Path.GetTempPath(), "NicoKaraPrepTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var doc = Doc("[00:01:00]あ[00:02:00]", "[00:02:00]い[00:03:00]", "", "[00:05:00]う[00:06:00]");
            var tabs = new[] { new N3ProjExportTab { Name = "メイン", Document = doc, LyricsPath = Path.Combine(dir, "song.lrc") } };
            var options = new N3ProjExportOptions { DefaultFont = new N3FontSet { Name = "標準", FontFamily = "メイリオ", SizePx = 72 } };
            string path = Path.Combine(dir, "song.n3proj");
            var result = N3ProjWriter.Write(path, tabs, options);

            Assert.True(File.Exists(path));
            Assert.True(File.Exists(Path.Combine(dir, "song.lrc")));
            Assert.Equal(3, result.LyricsLineCount);

            var settings = N3ProjFormat.Read(path);
            Assert.Equal("メイリオ", settings.MainFont!.FontName);
            Assert.Equal(72, settings.MainFont.SizePx, 1);
            Assert.Equal(7, settings.Layouts.Count);
            Assert.Equal(3, settings.LineTimes.Count);
            Assert.Equal(new[] { "標準" }, settings.FontSetNames);

            // BOM 付き UTF-8 の JSON が入っている
            string json = N3ProjFormat.ReadJson(path);
            Assert.StartsWith("{", json);
            var fontSets = N3ProjFormat.ReadFontSets(path);
            Assert.Single(fontSets);
            Assert.Equal("メイリオ", fontSets[0].FontFamily);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ------------------------------------------------------------ 字幕アクション

    private static JsonObject Obj(string json) => JsonNode.Parse(json)!.AsObject();

    /// <summary>旧書式（画面用の項目入り）の文字単位フェード（実データ What is my LIFE.n3proj の写し）。</summary>
    private const string OldCharFade =
        """{"$type":"CharFadeInFadeOutSettingsModel","IntroDelay":350,"IntroDelayTimeTag":"[00:00:35]","WholeFadeOut":false,"DelayInlineGraphicsIsEnabled":true,"TailDelayVisibility":0,"TailDelay":250,"TailDelayTimeTag":"[00:00:25]","DelayInlineGraphics":true,"FadeInTimeVisibility":0,"FadeInTime":250,"FadeInTimeTag":"[00:00:25]","FadeOutTimeVisibility":0,"FadeOutTime":250,"FadeOutTimeTag":"[00:00:25]"}""";

    /// <summary>旧書式の行フェード（実データ What is my LIFE.n3proj のフェードイン/アウトの写し）。</summary>
    private const string OldLineFade =
        """{"$type":"SubtitleActionSettingsModel","FadeInTimeVisibility":0,"FadeInTime":250,"FadeInTimeTag":"[00:00:25]","FadeOutTimeVisibility":0,"FadeOutTime":250,"FadeOutTimeTag":"[00:00:25]"}""";

    /// <summary>新しい書式の文字単位フェード（実データのほとんどの値: 表示終了基準にしない・アイコンを遅らせる。版数だけ変える）。</summary>
    private static string NewCharFade(string ver) =>
        $$"""{"$type":"CharFadeInFadeOutSettingsModel","IntroDelay":350,"WholeFadeOut":false,"TailDelay":250,"DelayInlineGraphics":true,"FadeInTime":250,"FadeOutTime":250,"CreateAppVer":"{{ver}}","ModifyAppVer":""}""";

    /// <summary>歌詞行の字幕アクションだけを持つベースの n3proj（タブごとの歌詞行。各歌詞行の後ろに区切り行を挟む）。</summary>
    private static JsonObject BaseWithActions(params (string Id, string Settings)[][] tabs)
    {
        var infos = new JsonArray();
        foreach (var tab in tabs)
        {
            var lines = new JsonArray();
            foreach (var (id, settings) in tab)
            {
                lines.Add(new JsonObject { ["Kind"] = 1, ["SubtitleActionId"] = id, ["SubtitleActionSettings"] = Obj(settings) });
                lines.Add(new JsonObject { ["Kind"] = 2, ["SubtitleActionId"] = "SHINTA.NoAction", ["SubtitleActionSettings"] = Obj("""{"$type":"AddOnSettingsModel"}""") });
            }
            infos.Add(new JsonObject { ["SettingsName"] = "メイン", ["LineInfos"] = lines });
        }
        return new JsonObject { ["SourceLyricsInfos"] = infos };
    }

    [Fact]
    public void 字幕アクション_行の指定が優先し_無い行は既定_どれも新しい書式で書く()
    {
        var doc = Doc("[00:01:00]あ[00:02:00]", "[00:02:00]い[00:03:00]", "", "[00:04:00]う[00:05:00]", "[00:05:00]え[00:06:00]");
        var shared = new N3SubtitleAction("SHINTA.LineFadeInFadeOut", Obj(OldLineFade));
        doc.Lines[1].SubtitleAction = shared;
        doc.Lines[4].SubtitleAction = shared; // 同じオブジェクトを 2 行に持たせても書ける
        doc.Lines[3].SubtitleAction = new N3SubtitleAction("SHINTA.Future", Obj("""{"TailDelay":250,"$type":"FutureSettingsModel","DelayInlineGraphics":false}"""));

        var lines = N3ProjWriter.BuildLineInfos(doc, new N3ShowTimeSettings(), doc.EmojiEntries, Fonts("標準"), Layouts(("下寄せ2行", 2)),
            new N3SubtitleAction("SHINTA.CharFadeInFadeOut", Obj(OldCharFade)), "Ver 13.79", out _);

        string Id(int n) => LyricLine(lines, n)["SubtitleActionId"]!.GetValue<string>();
        string Settings(int n) => LyricLine(lines, n)["SubtitleActionSettings"]!.ToJsonString();
        Assert.Equal(new[] { "SHINTA.CharFadeInFadeOut", "SHINTA.LineFadeInFadeOut", "SHINTA.Future", "SHINTA.LineFadeInFadeOut" }, Enumerable.Range(0, 4).Select(Id));
        Assert.Equal(NewCharFade("Ver 13.79"), Settings(0)); // 既定（旧書式）も新しい書式で
        Assert.Equal("""{"$type":"SubtitleActionSettingsModel","FadeInTime":250,"FadeOutTime":250,"CreateAppVer":"Ver 13.79","ModifyAppVer":""}""", Settings(1));
        Assert.Equal(Settings(1), Settings(3));
        // 知らない Id は中身のまま（型判別子だけ最初へ）
        Assert.Equal("""{"$type":"FutureSettingsModel","TailDelay":250,"DelayInlineGraphics":false}""", Settings(2));
        Assert.Equal(OldLineFade, shared.Settings.ToJsonString()); // 行の指定そのものは変えない

        // 区切り行・空行は今のまま（Id は空、AddOnSettingsModel）
        var separator = lines.OfType<JsonObject>().First(l => l["Kind"]!.GetValue<int>() == 2);
        Assert.Equal("", separator["SubtitleActionId"]!.GetValue<string>());
        Assert.Equal("""{"$type":"AddOnSettingsModel","CreateAppVer":"Ver 13.79","ModifyAppVer":""}""", separator["SubtitleActionSettings"]!.ToJsonString());
    }

    [Fact]
    public void 字幕アクション_絵文字の先行を譲る規則で歌詞を写しても行の指定を書く()
    {
        var doc = CircleOfLove();
        Assert.StartsWith("(愛)鮮", doc.Lines[3].GetDisplayText());
        doc.Lines[3].SubtitleAction = N3SubtitleActionCatalog.CreateDefault("SHINTA.LineFadeIn");

        var lines = N3ProjWriter.BuildLineInfos(doc, YieldShow(doc), doc.EmojiEntries, Fonts("標準"), Layouts(("下寄せ2行", 2)), Action(), "Ver 13.79", out _);
        Assert.Equal("SHINTA.CharFadeInFadeOut", LyricLine(lines, 0)["SubtitleActionId"]!.GetValue<string>());
        Assert.Equal("SHINTA.LineFadeIn", LyricLine(lines, 2)["SubtitleActionId"]!.GetValue<string>());
        Assert.Equal("SHINTA.CharFadeInFadeOut", LyricLine(lines, 3)["SubtitleActionId"]!.GetValue<string>());
    }

    [Fact]
    public void 字幕アクション_既定は曲の既定_ベースでいちばん多いもの_ニコカラメーカー3のId_文字単位フェードの順()
    {
        // ベース: 最初の歌詞行はフェードイン/アウトだが、文字単位フェード（旧書式と新しい書式）が 2 行でいちばん多い
        var baseRoot = BaseWithActions(
            new[] { ("SHINTA.LineFadeInFadeOut", OldLineFade), ("SHINTA.CharFadeInFadeOut", OldCharFade) },
            new[] { ("SHINTA.CharFadeInFadeOut", NewCharFade("Ver 11.15")), ("SHINTA.NoAction", """{"$type":"AddOnSettingsModel"}""") });
        var addOns = new Dictionary<string, JsonObject>
        {
            ["SHINTA.LineFadeOut"] = Obj("""{"FadeInTime":250,"FadeOutTime":600,"CreateAppVer":"Ver 12.00","ModifyAppVer":""}"""),
            ["SHINTA.CharFadeInFadeOut"] = Obj("""{"IntroDelay":500}"""),
        };

        // 曲の既定があればそれ（写しを返す）
        var song = N3SubtitleActionCatalog.CreateDefault("SHINTA.NoAction");
        var a = N3ProjWriter.ResolveDefaultAction(baseRoot, new N3ProjExportOptions { DefaultSubtitleAction = song, DefaultSubtitleActionIdFromNkm3 = "SHINTA.LineFadeOut" }, out var source);
        Assert.Equal(N3SubtitleActionSource.Song, source);
        Assert.True(song.SameAs(a));
        Assert.NotSame(song, a);

        // 自動: ベースの歌詞行でいちばん多いもの（区切り行の「アクションしない」は数えない）
        a = N3ProjWriter.ResolveDefaultAction(baseRoot, new N3ProjExportOptions { DefaultSubtitleActionIdFromNkm3 = "SHINTA.LineFadeOut" }, out source);
        Assert.Equal(N3SubtitleActionSource.Base, source);
        Assert.Equal("SHINTA.CharFadeInFadeOut", a.Id);
        Assert.Equal(OldCharFade, a.Settings.ToJsonString()); // そのグループで最初に出たものの写し（書式は書き出しでそろえる）

        // ベースが無い・歌詞行にアクションが無ければ、ニコカラメーカー3 の「すべて同じ字幕アクションにする」の Id（値はアドオンの設定）
        foreach (var noActions in new JsonObject?[] { null, new JsonObject { ["SourceLyricsInfos"] = new JsonArray() } })
        {
            a = N3ProjWriter.ResolveDefaultAction(noActions, new N3ProjExportOptions { DefaultSubtitleActionIdFromNkm3 = "SHINTA.LineFadeOut", AddOnSettings = addOns }, out source);
            Assert.Equal(N3SubtitleActionSource.Nkm3, source);
            Assert.Equal("SHINTA.LineFadeOut", a.Id);
            Assert.Equal(600, a.GetInt("FadeOutTime"));
        }

        // ニコカラメーカー3 の Id はカタログの 8 種類どれでもよい
        a = N3ProjWriter.ResolveDefaultAction(null, new N3ProjExportOptions { DefaultSubtitleActionIdFromNkm3 = "SHINTA.SpinFlip" }, out source);
        Assert.Equal(N3SubtitleActionSource.Nkm3, source);
        Assert.Equal("SHINTA.SpinFlip", a.Id);

        // Id が無い・知らない（設定項目の分からない）Id なら文字単位フェード（値はアドオンの設定、無い項目は既定値）
        foreach (string? id in new[] { null, "", "SHINTA.Future" })
        {
            a = N3ProjWriter.ResolveDefaultAction(null, new N3ProjExportOptions { DefaultSubtitleActionIdFromNkm3 = id, AddOnSettings = addOns }, out source);
            Assert.Equal(N3SubtitleActionSource.Standard, source);
            Assert.Equal("SHINTA.CharFadeInFadeOut", a.Id);
            Assert.Equal(500, a.GetInt("IntroDelay"));
            Assert.Equal(250, a.GetInt("TailDelay"));
        }
        // ベースもニコカラメーカー3 の設定も無ければ、ニコカラメーカー3 の初期値（表示終了基準にする・アイコンを遅らせない）
        Assert.Equal(
            """{"$type":"CharFadeInFadeOutSettingsModel","IntroDelay":350,"WholeFadeOut":true,"TailDelay":250,"DelayInlineGraphics":false,"FadeInTime":250,"FadeOutTime":250,"CreateAppVer":"","ModifyAppVer":""}""",
            N3ProjWriter.ResolveDefaultAction(null, new N3ProjExportOptions()).Settings.ToJsonString());
    }

    [Fact]
    public void 字幕アクション_上下スライド量は書き出すプロジェクトの画面の高さで書く()
    {
        var doc = Doc("[00:01:00]あ[00:02:00]", "[00:02:00]い[00:03:00]");
        doc.Lines[1].SubtitleAction = N3SubtitleActionCatalog.CreateDefault("SHINTA.SlideUpDown"); // 1080 の画面で -50px
        var options = new N3ProjExportOptions { DefaultFont = new N3FontSet { Name = "標準" }, ScreenWidth = 1280, ScreenHeight = 720 };
        var root = N3ProjWriter.BuildProjectJson(@"C:\v\b.n3proj", new[] { Tab("メイン", doc, @"C:\v\b.lrc") }, options, null, new List<string>(), out _, out _);

        var amount = LyricLine(root["SourceLyricsInfos"]![0]!["LineInfos"]!.AsArray(), 1)["SubtitleActionSettings"]!["SlideAmount"]!.AsObject();
        Assert.Equal(-33, amount["Size"]!.GetValue<int>());
        Assert.Equal(720, amount["Reference"]!.GetValue<int>());
        Assert.Equal(-50.0 / 1080, amount["Ratio"]!.GetValue<double>(), 12);
        Assert.Equal(1080, doc.Lines[1].SubtitleAction!.Settings["SlideAmount"]!["Reference"]!.GetValue<int>()); // 行の指定は変えない
    }

    [Fact]
    public void 字幕アクション_旧書式のベースから書き出すと全歌詞行を新しい書式で書く()
    {
        var baseRoot = BaseWithActions(new[]
        {
            ("SHINTA.LineFadeIn", """{"$type":"SubtitleActionSettingsModel","FadeInTimeVisibility":0,"FadeInTime":250,"FadeInTimeTag":"[00:00:25]","FadeOutTimeVisibility":1,"FadeOutTime":250,"FadeOutTimeTag":"[00:00:25]"}"""),
            ("SHINTA.CharFadeInFadeOut", OldCharFade),
            ("SHINTA.CharFadeInFadeOut", OldCharFade),
        });
        var doc = Doc("[00:01:00]あ[00:02:00]", "", "[00:03:00]い[00:04:00]", "[00:04:00]う[00:05:00]");
        doc.Lines[2].SubtitleAction = N3SubtitleActionCatalog.CreateDefault("SHINTA.NoAction");

        JsonArray Export(JsonObject root, N3SubtitleAction? songDefault)
        {
            var options = new N3ProjExportOptions { BaseProject = root, DefaultFont = new N3FontSet { Name = "標準" }, AppVersion = "Ver 13.90", DefaultSubtitleAction = songDefault };
            var built = N3ProjWriter.BuildProjectJson(@"C:\v\b.n3proj", new[] { Tab("メイン", doc, @"C:\v\b.lrc") }, options, root, new List<string>(), out _, out _);
            Assert.Equal("SHINTA.UnificationSubtitleActionSelector", built["SourceLyricsInfos"]![0]!["LastSelectedAddOns"]!["SubtitleActionSelectorId"]!.GetValue<string>());
            return built["SourceLyricsInfos"]![0]!["LineInfos"]!.AsArray();
        }

        // 自動: ベースでいちばん多い文字単位フェード（最初の行のフェードインではない）を、画面用の項目を落とした新しい書式で
        var lines = Export(baseRoot.DeepClone().AsObject(), null);
        Assert.Equal("SHINTA.CharFadeInFadeOut", LyricLine(lines, 0)["SubtitleActionId"]!.GetValue<string>());
        Assert.Equal(NewCharFade("Ver 13.90"), LyricLine(lines, 0)["SubtitleActionSettings"]!.ToJsonString());
        Assert.Equal("SHINTA.NoAction", LyricLine(lines, 1)["SubtitleActionId"]!.GetValue<string>()); // 行の指定
        Assert.Equal("""{"$type":"AddOnSettingsModel","CreateAppVer":"Ver 13.90","ModifyAppVer":""}""", LyricLine(lines, 1)["SubtitleActionSettings"]!.ToJsonString());
        Assert.Equal("SHINTA.CharFadeInFadeOut", LyricLine(lines, 2)["SubtitleActionId"]!.GetValue<string>());

        // 曲の既定があればベースより優先（行の指定はそのまま）
        var song = N3SubtitleActionCatalog.CreateDefault("SHINTA.LineFadeInFadeOut");
        song.Set("FadeInTime", 500);
        lines = Export(baseRoot.DeepClone().AsObject(), song);
        Assert.Equal("""{"$type":"SubtitleActionSettingsModel","FadeInTime":500,"FadeOutTime":250,"CreateAppVer":"Ver 13.90","ModifyAppVer":""}""", LyricLine(lines, 0)["SubtitleActionSettings"]!.ToJsonString());
        Assert.Equal("SHINTA.NoAction", LyricLine(lines, 1)["SubtitleActionId"]!.GetValue<string>());
        Assert.Equal("SHINTA.LineFadeInFadeOut", LyricLine(lines, 2)["SubtitleActionId"]!.GetValue<string>());
    }

    // ------------------------------------------------------------ 絵文字の先行を譲る規則

    /// <summary>Circle of Love 冒頭 3 ページ（上段の行頭に (愛)。ページ1→2・2→3 の上段が重なる）。</summary>
    private static LyricsDocument CircleOfLove() => Doc(
        "@Emoji=(愛),a.png",
        "[00:16:21](愛)[00:18:21]曇[00:19:00]り[00:20:58]",
        "[00:19:86]「[00:20:70]大[00:23:12]",
        "",
        "[00:21:18](愛)[00:23:18]鮮[00:24:00]や[00:25:41]",
        "[00:25:49]O[00:25:95]ur[00:27:80]",
        "",
        "[00:26:20](愛)[00:28:20]無[00:29:00]限[00:30:46]",
        "[00:29:65]「[00:30:60]楽[00:32:94]");

    /// <summary>ワイプ前 1500 / ワイプ後 800 / 表示間隔 300。絵文字（曲の @Emoji ＋ ＿）の先行を譲る規則のオン・オフ。</summary>
    private static N3ShowTimeSettings YieldShow(LyricsDocument doc, bool on = true) => new()
    {
        LeadMs = 1500,
        TailMs = 800,
        IntervalMs = 300,
        EmojiLeadYield = on,
        LeadMatcher = new EmojiMatcher(doc.EmojiEntries.Select(e => e.ReplaceChar).Append("＿")),
    };

    private static N3ProjExportOptions ExportOptions(LyricsDocument doc, N3ShowTimeSettings show) => new()
    {
        ShowTime = show,
        EmojiEntries = doc.EmojiEntries,
        DefaultFont = new N3FontSet { Name = "標準" },
    };

    private static JsonObject LyricLine(JsonArray lines, int n) =>
        lines.OfType<JsonObject>().Where(l => l["Kind"]!.GetValue<int>() == 1).ElementAt(n);

    /// <summary>行の文字の開始時刻の最小（-1 を除く）。</summary>
    private static int FirstCharBegin(JsonObject line) =>
        line["LyricsCharInfos"]!.AsArray().Select(c => c!["BeginTime"]!.GetValue<int>()).Where(b => b >= 0).Min();

    /// <summary>書き出すたびに変わる Guid と LastModified を除いた JSON。</summary>
    private static string WithoutIdentity(JsonNode node)
    {
        static void Strip(JsonNode? n)
        {
            switch (n)
            {
                case JsonObject o:
                    o.Remove("Guid");
                    o.Remove("LastModified");
                    foreach (var kv in o) Strip(kv.Value);
                    break;
                case JsonArray a:
                    foreach (var item in a) Strip(item);
                    break;
            }
        }
        var copy = node.DeepClone();
        Strip(copy);
        return copy.ToJsonString();
    }

    [Fact]
    public void 絵文字の先行を譲る_遅らせた行の絵文字は表示開始へ寄せ文字の時刻とRawとlrcをそろえる()
    {
        string dir = Path.Combine(Path.GetTempPath(), "NicoKaraPrepTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var doc = CircleOfLove();
            string before = LrcFormat.Write(doc);
            string path = Path.Combine(dir, "song.n3proj");
            string lrcPath = Path.Combine(dir, "song.lrc");
            N3ProjWriter.Write(path, new[] { new N3ProjExportTab { Name = "メイン", Document = doc, LyricsPath = lrcPath } }, ExportOptions(doc, YieldShow(doc)));
            var lines = N3ProjFormat.ReadJsonObject(path)["SourceLyricsInfos"]![0]!["LineInfos"]!.AsArray();

            // ページ2 の上段: 21680 に出て、絵文字は 21680〜23180（元は 21180〜。前の行と重ならないよう遅らせた分だけ縮める）
            var page2 = LyricLine(lines, 2);
            Assert.Equal(21680, page2["ShowBeginTime"]!.GetValue<int>());
            AssertChar(page2["LyricsCharInfos"]![0], "(愛)", 21680, 23180, kind: 1);
            AssertChar(page2["LyricsCharInfos"]![1], "鮮", 23180, 24000);
            Assert.Equal("[00:21:68](愛)[00:23:18]鮮[00:24:00]や[00:25:41]", page2["Raw"]!.GetValue<string>());
            // ページ3 の上段
            var page3 = LyricLine(lines, 4);
            Assert.Equal(26510, page3["ShowBeginTime"]!.GetValue<int>());
            AssertChar(page3["LyricsCharInfos"]![0], "(愛)", 26510, 28200, kind: 1);
            Assert.Equal("[00:26:51](愛)[00:28:20]無[00:29:00]限[00:30:46]", page3["Raw"]!.GetValue<string>());
            // 重ならない行（ページ1 の上段）の絵文字はそのまま。前の行はワイプ後を残す
            var page1 = LyricLine(lines, 0);
            Assert.Equal((14710, 21380), (page1["ShowBeginTime"]!.GetValue<int>(), page1["ShowEndTime"]!.GetValue<int>()));
            AssertChar(page1["LyricsCharInfos"]![0], "(愛)", 16210, 18210, kind: 1);
            Assert.Equal("[00:16:21](愛)[00:18:21]曇[00:19:00]り[00:20:58]", page1["Raw"]!.GetValue<string>());
            // どの行も表示開始 ≦ 最初の文字（絵文字）の開始
            foreach (var line in lines.OfType<JsonObject>().Where(l => l["Kind"]!.GetValue<int>() == 1))
            {
                Assert.True(line["ShowBeginTime"]!.GetValue<int>() <= FirstCharBegin(line), line["Raw"]!.GetValue<string>());
            }

            // 書き出した lrc も同じ寄せ（ニコカラメーカーが lrc を読み直しても文字の時刻が変わらない）
            var written = LrcFormat.Parse(File.ReadAllText(lrcPath));
            var raws = lines.OfType<JsonObject>().Where(l => l["Kind"]!.GetValue<int>() == 1).Select(l => l["Raw"]!.GetValue<string>());
            Assert.Equal(raws, written.Lines.Where(l => !l.IsEmpty).Select(LrcFormat.WriteLyricLine));
            // ユーザーの歌詞は変えない
            Assert.Equal(before, LrcFormat.Write(doc));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void 絵文字の先行を譲る_BuildProjectJsonでもタブの上段を長めにと合わせて効く()
    {
        var doc = CircleOfLove();
        var root = N3ProjWriter.BuildProjectJson(@"C:\v\a.n3proj", new[] { Tab("メイン", doc, @"C:\v\a.lrc") }, ExportOptions(doc, YieldShow(doc)), null, new List<string>(), out _, out _);
        var lines = root["SourceLyricsInfos"]![0]!["LineInfos"]!.AsArray();
        Assert.Equal(21380, LyricLine(lines, 0)["ShowEndTime"]!.GetValue<int>());
        Assert.Equal(21680, LyricLine(lines, 2)["ShowBeginTime"]!.GetValue<int>());
        Assert.Equal("[00:21:68](愛)[00:23:18]鮮[00:24:00]や[00:25:41]", LyricLine(lines, 2)["Raw"]!.GetValue<string>());

        // タブだけ上段を長めに（全体の設定の写しにタブの指定を当てても、規則の設定は残る）
        var tab = new N3ProjWriter.TabSource(new N3ProjExportTab { Name = "メイン", Document = doc, LyricsPath = @"C:\v\a.lrc", TopLong = true }, @"C:\v\a.lrc", LrcFormat.Write(doc), DateTime.Now);
        var longRoot = N3ProjWriter.BuildProjectJson(@"C:\v\a.n3proj", new[] { tab }, ExportOptions(doc, YieldShow(doc)), null, new List<string>(), out _, out _);
        var info = longRoot["SourceLyricsInfos"]![0]!;
        Assert.Equal("SHINTA.TopLongAdjuster", info["LastSelectedAddOns"]!["ShowTimeAdjusterId"]!.GetValue<string>());
        var longLines = info["LineInfos"]!.AsArray();
        Assert.Equal((21680, 26400), (LyricLine(longLines, 2)["ShowBeginTime"]!.GetValue<int>(), LyricLine(longLines, 2)["ShowEndTime"]!.GetValue<int>()));
        Assert.Equal(26700, LyricLine(longLines, 4)["ShowBeginTime"]!.GetValue<int>());
        Assert.Equal("[00:26:70](愛)[00:28:20]無[00:29:00]限[00:30:46]", LyricLine(longLines, 4)["Raw"]!.GetValue<string>());
    }

    [Fact]
    public void 絵文字の先行を譲る_規則オフなら書き出しは今と同じ()
    {
        var doc = CircleOfLove();
        var tab = Tab("メイン", doc, @"C:\v\a.lrc");
        JsonObject Build(N3ShowTimeSettings show) =>
            N3ProjWriter.BuildProjectJson(@"C:\v\a.n3proj", new[] { tab }, ExportOptions(doc, show), null, new List<string>(), out _, out _);

        var plain = Build(new N3ShowTimeSettings { LeadMs = 1500, TailMs = 800, IntervalMs = 300 });
        var off = Build(YieldShow(doc, on: false));
        Assert.Equal(WithoutIdentity(plain), WithoutIdentity(off));
        // 行の値はニコカラメーカー3 の実プロジェクトの値・歌詞のタグのまま
        var page2 = LyricLine(off["SourceLyricsInfos"]![0]!["LineInfos"]!.AsArray(), 2);
        Assert.Equal(20780, page2["ShowBeginTime"]!.GetValue<int>());
        Assert.Equal("[00:21:18](愛)[00:23:18]鮮[00:24:00]や[00:25:41]", page2["Raw"]!.GetValue<string>());
        // 比べ方が違いを見分けられること（規則オンとは違う）
        Assert.NotEqual(WithoutIdentity(plain), WithoutIdentity(Build(YieldShow(doc))));
    }

    [Fact]
    public void 絵文字の先行を譲る_規則オフならlrcは歌詞そのもの()
    {
        string dir = Path.Combine(Path.GetTempPath(), "NicoKaraPrepTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var doc = CircleOfLove();
            string lrcPath = Path.Combine(dir, "song.lrc");
            N3ProjWriter.Write(Path.Combine(dir, "song.n3proj"), new[] { new N3ProjExportTab { Name = "メイン", Document = doc, LyricsPath = lrcPath } }, ExportOptions(doc, YieldShow(doc, on: false)));
            string expected = LrcFormat.Write(doc, new LrcWriteOptions { EmojiEntriesOverride = doc.EmojiEntries, BaseFolder = dir });
            Assert.Equal(expected, File.ReadAllText(lrcPath));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void 表示時刻の設定の写しはすべての項目を写す()
    {
        // 項目を足して CloneShowSettings に足し忘れると、書き出しだけ黙って違う計算になるのを防ぐ
        var props = typeof(N3ShowTimeSettings).GetProperties().Where(p => p.CanWrite).ToList();
        var s = new N3ShowTimeSettings();
        int n = 0;
        foreach (var p in props)
        {
            n++;
            object value = p.PropertyType == typeof(int) ? 1000 + n
                : p.PropertyType == typeof(int?) ? 2000 + n
                : p.PropertyType == typeof(bool) ? !(bool)p.GetValue(s)!
                : p.PropertyType == typeof(PageSplitMode) ? PageSplitMode.FixedLineCount
                : p.PropertyType == typeof(EmojiMatcher) ? new EmojiMatcher(new[] { "★" })
                : p.PropertyType == typeof(IReadOnlyDictionary<int, N3LineBounds>) ? new Dictionary<int, N3LineBounds> { [n] = new(n, n, n + 1, n + 1) }
                : throw new InvalidOperationException($"{p.Name}（{p.PropertyType.Name}）に入れる値をテストに足してください");
            p.SetValue(s, value);
        }
        var copy = N3ProjWriter.CloneShowSettings(s);
        foreach (var p in props)
        {
            Assert.True(Equals(p.GetValue(s), p.GetValue(copy)), $"{p.Name} が写されていない");
        }
    }

    /// <summary>
    /// ニコカラメーカー3 が保存した実プロジェクトと同じ行構造・文字時刻・文字ごとのフォントを生成できることを確認する。
    /// 環境変数 TTT_N3PROJ_SAMPLE に n3proj のパス（歌詞ファイルが同じ場所にあること）を設定して実行する。
    /// </summary>
    [Fact]
    public void ゴールデン_実プロジェクトの行と文字時刻が一致する()
    {
        string? sample = Environment.GetEnvironmentVariable("TTT_N3PROJ_SAMPLE");
        if (string.IsNullOrEmpty(sample) || !File.Exists(sample)) return;

        var baseRoot = N3ProjFormat.ReadJsonObject(sample);
        var infos = baseRoot["SourceLyricsInfos"]!.AsArray();
        var fontNames = baseRoot["LyricsFonts"]!.AsArray().Select(n => n!["SettingsName"]!.GetValue<string>()).ToList();
        var mismatches = new List<string>();
        int compared = 0;

        foreach (var info in infos)
        {
            string? lyricsPath = LyricsPathOf(info!, sample);
            if (lyricsPath is null) continue;
            var expected = info!["LineInfos"]!.AsArray();
            if (expected.Count == 0) continue;

            var doc = LrcFormat.Parse(EncodingDetector.ReadAllText(lyricsPath, out _));
            var fonts = new N3FontResolver(fontNames, null, true);
            var lines = N3ProjWriter.BuildLineInfos(doc, new N3ShowTimeSettings(), doc.EmojiEntries, fonts, Layouts(("下寄せ2行", 2)), Action(), "Ver 13.79", out _);

            var expectedKinds = Kinds(expected);
            var actualKinds = Kinds(lines);
            if (!expectedKinds.SequenceEqual(actualKinds))
            {
                mismatches.Add($"{Path.GetFileName(lyricsPath)}: 行種別の並びが不一致\n  期待 {string.Join("", expectedKinds)}\n  実際 {string.Join("", actualKinds)}");
            }

            for (int i = 0; i < Math.Min(expected.Count, lines.Count); i++)
            {
                if (expected[i]!["Kind"]!.GetValue<int>() != 1 || lines[i]!["Kind"]!.GetValue<int>() != 1) continue;
                compared++;
                var ec = expected[i]!["LyricsCharInfos"]!.AsArray();
                var ac = lines[i]!["LyricsCharInfos"]!.AsArray();
                string Fmt(JsonArray a) => string.Join(" ", a.Select(c => $"{c!["Char"]}:{c["Kind"]}:{c["BeginTime"]}:{c["EndTime"]}"));
                if (Fmt(ec) != Fmt(ac))
                {
                    mismatches.Add($"{Path.GetFileName(lyricsPath)} 行{i}: {expected[i]!["Raw"]}\n  期待 {Fmt(ec)}\n  実際 {Fmt(ac)}");
                }
                // 文字ごとのフォント（パート記号による自動設定）。ニコカラメーカー3 の「コーラス自動色分け」
                // （括弧で括られた部分をコーラス用フォントにする。NicoKaraPrep には無い）で説明できる文字の
                // 食い違いだけは失敗にしない（同じ行でも、それ以外の文字は比べる）
                string FmtFont(JsonArray a) => string.Join(" ", a.Select(c => $"{c!["Char"]}:{c["FontIndex"]}"));
                if (FmtFont(ec) != FmtFont(ac) && !OnlyAutoChorusDiffers(ec, ac, fontNames))
                {
                    mismatches.Add($"{Path.GetFileName(lyricsPath)} 行{i} フォント: {expected[i]!["Raw"]}\n  期待 {FmtFont(ec)}\n  実際 {FmtFont(ac)}");
                }
                if (mismatches.Count > 12) break;
            }
        }

        Assert.True(compared > 0, "比較できる歌詞行がありませんでした");
        Assert.True(mismatches.Count == 0, string.Join("\n", mismatches.Take(12)));
    }

    /// <summary>
    /// 実プロジェクトをベースに書き出したプロジェクトが、ベースと同じ JSON 構造（キーの集合）を持つことを確認する。
    /// 環境変数 TTT_N3PROJ_SAMPLE にベースの n3proj、TTT_N3PROJ_OUT に出力先フォルダ（省略時は一時フォルダ）を設定する。
    /// </summary>
    [Fact]
    public void ゴールデン_ベースから書き出したプロジェクトは同じ構造を持つ()
    {
        string? sample = Environment.GetEnvironmentVariable("TTT_N3PROJ_SAMPLE");
        if (string.IsNullOrEmpty(sample) || !File.Exists(sample)) return;

        var baseRoot = N3ProjFormat.ReadJsonObject(sample);
        var sourceInfos = baseRoot["SourceLyricsInfos"]!.AsArray();
        var tabs = new List<N3ProjExportTab>();
        string? outEnv = Environment.GetEnvironmentVariable("TTT_N3PROJ_OUT");
        string outDir = string.IsNullOrEmpty(outEnv)
            ? Path.Combine(Path.GetTempPath(), "NicoKaraPrepTests", Guid.NewGuid().ToString("N"))
            : outEnv;
        Directory.CreateDirectory(outDir);
        string baseName = Path.GetFileNameWithoutExtension(sample) + "_NicoKaraPrep";

        foreach (var info in sourceInfos)
        {
            string? lyricsPath = LyricsPathOf(info!, sample);
            if (lyricsPath is null) continue;
            var doc = LrcFormat.Parse(EncodingDetector.ReadAllText(lyricsPath, out _));
            string name = info!["SettingsName"]?.GetValue<string>() ?? $"タブ{tabs.Count + 1}";
            tabs.Add(new N3ProjExportTab
            {
                Name = name,
                Document = doc,
                LyricsPath = Path.Combine(outDir, tabs.Count == 0 ? baseName + ".lrc" : $"{baseName}_{name}.lrc"),
            });
        }
        if (tabs.Count == 0) return;

        try
        {
            var options = new N3ProjExportOptions
            {
                BaseProjectPath = sample,
                EmojiEntries = tabs[0].Document.EmojiEntries,
                MediaPath = baseRoot["SourceInfo"]?["MoviePath"]?.GetValue<string>(),
            };
            string projectPath = Path.Combine(outDir, baseName + ".n3proj");
            var result = N3ProjWriter.Write(projectPath, tabs, options);
            Assert.True(result.LyricsLineCount > 0);

            var written = N3ProjFormat.ReadJsonObject(projectPath);
            var basePaths = new SortedSet<string>();
            var outPaths = new SortedSet<string>();
            CollectPaths(baseRoot, "", basePaths);
            CollectPaths(written, "", outPaths);
            // 字幕アクションの設定値の中身は、書き出すアクション（ベースの歌詞行でいちばん多いもの）を新しい書式にそろえたものなので比べない
            // （ベースの旧書式の画面用の項目・ほかのアクションの項目は出力に無い）。代わりに下で、全歌詞行がそのアクションかを確かめる
            static bool InActionSettings(string p) => p.Contains("/SubtitleActionSettings/");
            var missing = basePaths.Except(outPaths).Where(p => !InActionSettings(p)).ToList();
            // 古いバージョンで保存されたベースには字幕アクション設定の版数情報が無いことがある（現行版は書く）
            var extra = outPaths.Except(basePaths)
                .Where(p => !p.EndsWith("/CreateAppVer") && !p.EndsWith("/ModifyAppVer") && !InActionSettings(p))
                .ToList();
            Assert.True(missing.Count == 0 && extra.Count == 0,
                $"ベースにあって出力に無いキー: {string.Join(", ", missing.Take(10))}\n出力にだけあるキー: {string.Join(", ", extra.Take(10))}");

            var expectedAction = N3ProjFormat.MostCommonSubtitleAction(baseRoot);
            var lyricLines = written["SourceLyricsInfos"]!.AsArray()
                .SelectMany(i => i!["LineInfos"]!.AsArray()).OfType<JsonObject>()
                .Where(l => l["Kind"]!.GetValue<int>() == 1).ToList();
            Assert.All(lyricLines, l =>
            {
                var settings = l["SubtitleActionSettings"]!.AsObject();
                Assert.Equal("$type", settings.First().Key);
                Assert.DoesNotContain(settings, kv => N3SubtitleActionCatalog.IsScreenItem(kv.Key));
                if (expectedAction is not null) Assert.True(expectedAction.SameAs(N3ProjFormat.ReadLineSubtitleAction(l)), "ベースでいちばん多いアクションではない");
            });

            var settings = N3ProjFormat.Read(projectPath);
            Assert.True(settings.LineTimes.Count > 0);
            Assert.Equal(baseRoot["LyricsFonts"]!.AsArray().Count, settings.Fonts.Count);
        }
        finally
        {
            if (string.IsNullOrEmpty(outEnv)) Directory.Delete(outDir, recursive: true);
        }
    }

    /// <summary>コーラス自動色分けのコーラス開始文字・終了文字（ニコカラメーカー3 の既定）。</summary>
    private static readonly char[] ChorusBeginChars = { '（', '(', '[' };
    private static readonly char[] ChorusEndChars = { '）', ')', ']' };

    /// <summary>
    /// 文字ごとのフォントの食い違いが、ニコカラメーカー3 の「歌詞のコーラス部分を自動色分けする」で説明できる文字だけにあるか。
    /// 説明できるのは次の文字だけ:
    /// ・括弧（コーラス開始文字から対応する終了文字まで。行内で閉じなければ行末まで）の中。ただし期待のフォントが
    ///   括弧全体で 1 種類（コーラス用フォント）のときだけ
    /// ・その括弧の前にある、コーラス用フォントと同じ名前のパート記号から括弧の手前まで
    ///   （NicoKaraPrep はパート記号から切り替えるが、ニコカラメーカー3 は括弧から切り替えている）
    /// フォント設定名と同じ文字（パート記号の絵文字）は括弧に数えない。
    /// </summary>
    private static bool OnlyAutoChorusDiffers(JsonArray expected, JsonArray actual, IReadOnlyList<string> fontNames)
    {
        if (expected.Count != actual.Count) return false;
        string CharAt(int i) => expected[i]!["Char"]!.GetValue<string>();
        int ExpectedFont(int i) => expected[i]!["FontIndex"]!.GetValue<int>();
        int FontIndexOfName(string name)
        {
            for (int n = 0; n < fontNames.Count; n++)
            {
                if (fontNames[n] == name) return n;
            }
            return -1;
        }

        var allowed = new bool[expected.Count];
        int marker = -1; // 直前のパート記号の位置
        for (int i = 0; i < expected.Count; i++)
        {
            string c = CharAt(i);
            if (FontIndexOfName(c) >= 0)
            {
                marker = i;
                continue;
            }
            if (c.IndexOfAny(ChorusBeginChars) < 0) continue;

            int end = i;
            while (end + 1 < expected.Count && CharAt(end).IndexOfAny(ChorusEndChars) < 0) end++;
            int chorus = ExpectedFont(i);
            bool uniform = true;
            for (int k = i; k <= end; k++) uniform &= ExpectedFont(k) == chorus;
            if (uniform)
            {
                int from = marker >= 0 && FontIndexOfName(CharAt(marker)) == chorus ? marker : i;
                for (int k = from; k <= end; k++) allowed[k] = true;
            }
            marker = -1;
            i = end;
        }

        for (int i = 0; i < expected.Count; i++)
        {
            if (ExpectedFont(i) != actual[i]!["FontIndex"]!.GetValue<int>() && !allowed[i]) return false;
        }
        return true;
    }

    /// <summary>
    /// 歌詞設定タブの歌詞ファイル。記録された絶対パスに無ければ、n3proj と同じフォルダから相対パスで探す
    /// （サンプルを別の場所へ複製したとき用）。どちらにも無ければ null。
    /// </summary>
    private static string? LyricsPathOf(JsonNode info, string projectPath)
    {
        string? path = info["SourceLyricsPath"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;
        string? relative = info["SourceLyricsRelativePath"]?.GetValue<string>();
        if (string.IsNullOrEmpty(relative)) return null;
        string near = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectPath))!, relative);
        return File.Exists(near) ? near : null;
    }

    private static void CollectPaths(JsonNode? node, string path, SortedSet<string> result)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var kv in o)
                {
                    result.Add(path + "/" + kv.Key);
                    CollectPaths(kv.Value, path + "/" + kv.Key, result);
                }
                break;
            case JsonArray a:
                foreach (var item in a) CollectPaths(item, path + "[]", result);
                break;
        }
    }
}
