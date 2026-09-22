using System.Text.Json.Nodes;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Tests;

public class N3ProjWriterTests
{
    private static LyricsDocument Doc(params string[] lines) => LrcFormat.Parse(string.Join("\r\n", lines) + "\r\n");

    private static N3ProjWriter.FontResolver Fonts(params string[] names) =>
        new(names.Select((n, i) => (n, i)), null, true);

    private static N3ProjWriter.LayoutResolver Layouts(params (string Name, int Count)[] layouts) =>
        new(layouts.Select((l, i) => new N3ProjLayoutInfo(l.Name, i, l.Count)).ToList(), null, null, null, new List<string>(), "t");

    private static (string, JsonObject) Action() =>
        ("SHINTA.CharFadeInFadeOut", new JsonObject { ["$type"] = "CharFadeInFadeOutSettingsModel", ["FadeInTime"] = 250 });

    private static JsonArray Build(LyricsDocument doc, N3ShowTimeSettings? show = null, N3ProjWriter.FontResolver? fonts = null, N3ProjWriter.LayoutResolver? layouts = null)
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
    public void 文字時刻_先頭タグなしと末尾タグなし()
    {
        var chars = Chars(Build(Doc("ab[00:01:00]c")), 0);
        Assert.Equal(3, chars.Count);
        AssertChar(chars[0], "a", -1, -1);
        AssertChar(chars[1], "b", -1, 1000);
        AssertChar(chars[2], "c", 1000, -1);
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
    public void 表示時刻_前ページと重なると前行を短縮しそれでも重なれば次行を遅らせる()
    {
        // ページ1: A(上段) B(下段) / ページ2: C(上段) D(下段)
        var doc = Doc(
            "[00:10:00]あ[00:12:00]",
            "[00:13:00]い[00:20:00]",
            "",
            "[00:20:50]う[00:22:00]",
            "[00:21:00]え[00:25:00]");
        var plans = N3ShowTimePlanner.Plan(doc, new N3ShowTimeSettings());
        // C の希望表示開始 = 20500 - 1500 = 19000。B の表示終了 20800 と衝突 → 20000 + 保護 400 = 20400 まで短縮
        Assert.Equal(20400, plans[1].EndMs);
        Assert.True(plans[1].Adjusted);
        // D は B と同じ段（下段）。まだ重なるので D の表示開始を 20400 まで遅らせる（歌唱開始 21000 より前）
        Assert.Equal(20400, plans[4].BeginMs);
        Assert.True(plans[4].Adjusted);
        // C は A（上段、12800 に消える）と重ならないので希望どおり
        Assert.Equal(19000, plans[3].BeginMs);
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

    /// <summary>
    /// ニコカラメーカー3 が保存した実プロジェクトと同じ行構造・文字時刻を生成できることを確認する。
    /// 環境変数 TTT_N3PROJ_SAMPLE に n3proj のパス（歌詞ファイルが同じ場所にあること）を設定して実行する。
    /// </summary>
    [Fact]
    public void ゴールデン_実プロジェクトの行と文字時刻が一致する()
    {
        string? sample = Environment.GetEnvironmentVariable("TTT_N3PROJ_SAMPLE");
        if (string.IsNullOrEmpty(sample) || !File.Exists(sample)) return;

        var baseRoot = N3ProjFormat.ReadJsonObject(sample);
        var infos = baseRoot["SourceLyricsInfos"]!.AsArray();
        var fontNames = baseRoot["LyricsFonts"]!.AsArray().Select((n, i) => (n!["SettingsName"]!.GetValue<string>(), i)).ToList();
        var mismatches = new List<string>();
        int compared = 0;

        foreach (var info in infos)
        {
            string? lyricsPath = info!["SourceLyricsPath"]?.GetValue<string>();
            if (string.IsNullOrEmpty(lyricsPath) || !File.Exists(lyricsPath)) continue;
            var expected = info["LineInfos"]!.AsArray();
            if (expected.Count == 0) continue;

            var doc = LrcFormat.Parse(EncodingDetector.ReadAllText(lyricsPath, out _));
            var fonts = new N3ProjWriter.FontResolver(fontNames, null, true);
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
            string? lyricsPath = info!["SourceLyricsPath"]?.GetValue<string>();
            if (string.IsNullOrEmpty(lyricsPath) || !File.Exists(lyricsPath)) continue;
            var doc = LrcFormat.Parse(EncodingDetector.ReadAllText(lyricsPath, out _));
            string name = info["SettingsName"]?.GetValue<string>() ?? $"タブ{tabs.Count + 1}";
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
            var missing = basePaths.Except(outPaths).ToList();
            // 古いバージョンで保存されたベースには字幕アクション設定の版数情報が無いことがある（現行版は書く）
            var extra = outPaths.Except(basePaths)
                .Where(p => !p.EndsWith("/CreateAppVer") && !p.EndsWith("/ModifyAppVer"))
                .ToList();
            Assert.True(missing.Count == 0 && extra.Count == 0,
                $"ベースにあって出力に無いキー: {string.Join(", ", missing.Take(10))}\n出力にだけあるキー: {string.Join(", ", extra.Take(10))}");

            var settings = N3ProjFormat.Read(projectPath);
            Assert.True(settings.LineTimes.Count > 0);
            Assert.Equal(baseRoot["LyricsFonts"]!.AsArray().Count, settings.Fonts.Count);
        }
        finally
        {
            if (string.IsNullOrEmpty(outEnv)) Directory.Delete(outDir, recursive: true);
        }
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
