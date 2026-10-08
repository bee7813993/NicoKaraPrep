using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Tests;

/// <summary>ニコカラメーカー3 のレイアウト設定テンプレート（Settings\TemplateLayout\*.tpl）の読み込み。</summary>
public class N3LayoutTemplateReaderTests : IDisposable
{
    /// <summary>実物のテンプレート（ニコカラメーカー3 の Settings\TemplateLayout から写した「3行左左右」「上寄せ2行3」）のフォルダ。</summary>
    private static readonly string FixtureFolder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TemplateLayout");

    /// <summary>「3行左左右」（下寄せ・上の行から 左・左・右）。</summary>
    private static readonly string ThreeRows = Path.Combine(FixtureFolder, "e0c07c62-cd53-44ce-938b-5a240f0bfb9b.tpl");

    /// <summary>「上寄せ2行3」（上寄せ・上の行から 左・右・スマート水平配置は左右余白揃え）。</summary>
    private static readonly string TopTwoRows = Path.Combine(FixtureFolder, "561991d8-3fed-4a93-b07f-7c2e58894d6e.tpl");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "NicoKaraPrepTests", Guid.NewGuid().ToString("N"));

    public N3LayoutTemplateReaderTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 後始末の失敗は無視 */ }
    }

    private static string Describe(N3Layout l) =>
        $"{l.Name} v{l.VerticalAlignment} ls{l.LineSpacePx:0.#} vm{l.VerticalMarginPx:0.#} hm{l.HorizontalMarginPx:0.#} " +
        $"[{string.Join(",", l.HorizontalAlignments)}] sh{l.SmartHorizon} li{l.LyricsIntervalPx:0.#} ri{l.RubyIntervalPx:0.#} " +
        $"lr{l.LyricsAndRubyIntervalPx:0.#} ra{l.RubyAlignment} b{l.AllowBiting}";

    private string WriteTemplate(string name, string json)
    {
        string path = Path.Combine(_dir, name);
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        using var writer = new StreamWriter(zip.CreateEntry("0").Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        writer.Write(json);
        return path;
    }

    /// <summary>実物のテンプレートの JSON をツリーで読む（項目を変えて書き直すため）。</summary>
    private static JsonObject FixtureJson(string path) => (JsonObject)JsonNode.Parse(N3ProjFormat.ReadJson(path))!;

    [Fact]
    public void 実物_フォルダのテンプレートを名前の順に読む()
    {
        var errors = new List<string>();
        var list = N3LayoutTemplateReader.ReadTemplateFolder(FixtureFolder, errors);
        Assert.Empty(errors);
        Assert.Equal(new[] { "3行左左右", "上寄せ2行3" }, list.Select(l => l.Name));

        // 3行左左右: 下寄せ・行間 55・上下余白 50・左右余白 50・行ごとの左右配置は上の行から 左・左・右
        var three = list[0];
        Assert.Equal(2, three.VerticalAlignment);
        Assert.Equal(new[] { 0, 0, 2 }, three.HorizontalAlignments);
        Assert.Equal(55, three.LineSpacePx);
        Assert.Equal(50, three.VerticalMarginPx);
        Assert.Equal(50, three.HorizontalMarginPx);
        Assert.Equal(0, three.SmartHorizon);

        // 上寄せ2行3: 上寄せ・行間 55・上下余白 80・左右余白 50・左・右・スマート水平配置は左右余白揃え
        var top = list[1];
        Assert.Equal(0, top.VerticalAlignment);
        Assert.Equal(new[] { 0, 2 }, top.HorizontalAlignments);
        Assert.Equal(55, top.LineSpacePx);
        Assert.Equal(80, top.VerticalMarginPx);
        Assert.Equal(50, top.HorizontalMarginPx);
        Assert.Equal(2, top.SmartHorizon);

        // 文字・ルビの項目（実物はどちらも 0・食い込みを許容しない）
        Assert.All(list, l => Assert.Equal((0.0, false, 0.0, 0, 0.0),
            (l.LyricsIntervalPx, l.AllowBiting, l.RubyIntervalPx, l.RubyAlignment, l.LyricsAndRubyIntervalPx)));
    }

    [Fact]
    public void 実物_n3projのLyricsLayoutsの1件と同じ読み方をする()
    {
        foreach (string path in new[] { ThreeRows, TopTwoRows })
        {
            var root = new JsonObject { ["LyricsLayouts"] = new JsonArray(FixtureJson(path)) };
            var fromProject = N3Layout.FromSettings(Assert.Single(N3LayoutReader.Read(root, 1080)));
            Assert.Equal(Describe(fromProject), Describe(N3LayoutTemplateReader.ReadTemplate(path)));
        }
    }

    [Fact]
    public void 実物_画面の高さに合わせてpxに換算する()
    {
        var top = N3LayoutTemplateReader.ReadTemplate(TopTwoRows, height: 720);
        Assert.Equal(36.7, top.LineSpacePx);       // 55 × 720 / 1080
        Assert.Equal(53.3, top.VerticalMarginPx);  // 80 × 720 / 1080
        Assert.Equal(33.3, top.HorizontalMarginPx); // 50 × 720 / 1080

        // 0 以下は FHD 基準
        Assert.Equal(55, N3LayoutTemplateReader.ReadTemplate(TopTwoRows, height: 0).LineSpacePx);
    }

    [Fact]
    public void 名前_JSONの名前が無ければファイル名を使う()
    {
        var noName = FixtureJson(ThreeRows);
        noName.Remove("SettingsName");
        WriteTemplate("名前なし.tpl", noName.ToJsonString());
        var empty = FixtureJson(ThreeRows);
        empty["SettingsName"] = "";
        WriteTemplate("空の名前.tpl", empty.ToJsonString());

        var errors = new List<string>();
        var list = N3LayoutTemplateReader.ReadTemplateFolder(_dir, errors);
        Assert.Empty(errors);
        Assert.Equal(new[] { "名前なし", "空の名前" }.Order(StringComparer.Ordinal), list.Select(l => l.Name));
        Assert.All(list, l => Assert.Equal(new[] { 0, 0, 2 }, l.HorizontalAlignments));
    }

    [Fact]
    public void 読めないファイル_壊れたZIPやJSONでない中身は飛ばしてerrorsに入れる()
    {
        File.Copy(ThreeRows, Path.Combine(_dir, Path.GetFileName(ThreeRows)));
        File.Copy(TopTwoRows, Path.Combine(_dir, Path.GetFileName(TopTwoRows)));
        File.WriteAllText(Path.Combine(_dir, "broken.tpl"), "zip ではない");
        WriteTemplate("text.tpl", "これは JSON ではありません");
        WriteTemplate("array.tpl", "[1, 2]");
        WriteTemplate("font.tpl", "{\"SettingsName\":\"（麻衣）\",\"BrushInfos\":[],\"FontInfos\":[]}"); // フォント設定のテンプレート
        File.WriteAllText(Path.Combine(_dir, "other.txt"), "対象外");

        var errors = new List<string>();
        var list = N3LayoutTemplateReader.ReadTemplateFolder(_dir, errors);
        Assert.Equal(new[] { "3行左左右", "上寄せ2行3" }, list.Select(l => l.Name));
        Assert.Collection(errors,
            e => Assert.StartsWith("array.tpl:", e),
            e => Assert.StartsWith("broken.tpl:", e),
            e => Assert.StartsWith("font.tpl:", e),
            e => Assert.StartsWith("text.tpl:", e));

        Assert.Empty(N3LayoutTemplateReader.ReadTemplateFolder(Path.Combine(_dir, "無いフォルダ")));
    }

    [Fact]
    public void 読めないファイル_1つ読みでは例外にする()
    {
        string broken = Path.Combine(_dir, "broken.tpl");
        File.WriteAllText(broken, "zip ではない");
        Assert.Throws<InvalidDataException>(() => N3LayoutTemplateReader.ReadTemplate(broken));
        Assert.Throws<InvalidDataException>(() => N3LayoutTemplateReader.ParseTemplateJson("[1, 2]"));
    }

    [Fact]
    public void 取り込み_書き出しのレイアウトの並びの後ろへ足せる()
    {
        var templates = N3LayoutTemplateReader.ReadTemplateFolder(FixtureFolder);
        var baseLayouts = N3LayoutReader.Defaults(1080);
        var effective = N3LayoutLibrary.Effective(baseLayouts, templates, merge: true);
        Assert.Equal(baseLayouts.Select(l => l.Name).Concat(new[] { "3行左左右", "上寄せ2行3" }), effective.Select(l => l.Name));
        Assert.Equal(3, effective.Single(l => l.Name == "3行左左右").LineCount);
        Assert.NotEqual(templates[0].Id, N3LayoutTemplateReader.ReadTemplate(ThreeRows).Id); // 読むたびに別の識別子
    }

    [Fact]
    public void 環境_テンプレートフォルダは設定フォルダのTemplateLayout()
    {
        var env = Nkm3Environment.Load(_dir);
        Assert.Equal(Path.Combine(_dir, "TemplateLayout"), env.TemplateLayoutFolder);
        Assert.Equal("TemplateLayout", Nkm3Environment.TemplateLayoutFolderName);
        Assert.All(Nkm3Environment.FindTemplateLayoutFolders(), d =>
        {
            Assert.True(Directory.Exists(d));
            Assert.Equal("TemplateLayout", Path.GetFileName(d));
        });
        Assert.All(Nkm3Environment.FindTemplateFontFolders(), d => Assert.Equal("TemplateFont", Path.GetFileName(d)));
    }
}
