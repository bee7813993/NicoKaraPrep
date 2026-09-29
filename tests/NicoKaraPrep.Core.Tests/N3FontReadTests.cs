using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;
using static NicoKaraPrep.Core.Tests.N3FontSamples;

namespace NicoKaraPrep.Core.Tests;

/// <summary>フォント設定の読み込み（n3proj・テンプレート）。</summary>
public class N3FontReadTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "NicoKaraPrepTests", Guid.NewGuid().ToString("N"));

    public N3FontReadTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 後始末の失敗は無視 */ }
    }

    // ------------------------------------------------------------ 読み込み

    [Fact]
    public void 読み込み_n3projのフォント設定を全項目取り込む()
    {
        var f = N3ProjFormat.ReadFontSets(ProjectOf(NkmFont())).Single();
        Assert.Equal("（麻衣）（のりこ）", f.Name);
        Assert.Equal("4ad6be49-3d45-47e4-9aee-b4c14a0f6057", f.NkmGuid);
        Assert.True(f.NkmSynchronize);
        Assert.NotNull(f.ImportedUtc);

        var d = f.Detail;
        // ミルフィーユの箇所も種類・マーカーと、残っている単色を読む
        Assert.Equal(new[] { 0, 2, 0, 0, 2, 0, 0, 2 }, d.Brushes.Select(b => b.Type));
        Assert.Equal("9B9B9B", d.Brushes[4].Color);
        Assert.Equal(new[] { "66C5EC", "AE62FF", "808080" }, d.Brushes[4].Stops.Select(s => s.Color));
        Assert.Equal(new[] { 0, 0.5, 1 }, d.Brushes[4].Stops.Select(s => s.Position));
        Assert.Equal(3, d.Brushes[0].Stops.Count); // 単色の箇所の既定マーカーも読む
        Assert.Equal(50, d.Brushes[3].AlphaPercent);
        Assert.Equal(100, d.Brushes[0].AlphaPercent);
        Assert.Equal("", f.EdgeColorAfter); // 旧項目ではミルフィーユは空

        // フォントフェース 6 種（UseEdge2 の null / false、XScale の欠落は 0）
        Assert.Equal("HGS創英角ﾎﾟｯﾌﾟ体", d.Faces[0].FontName);
        Assert.Equal("ﾍﾋﾞｰ", d.Faces[0].FaceName);
        Assert.Equal(80, d.Faces[0].SizePx);
        Assert.Equal(15, d.Faces[0].EdgePx);
        Assert.Null(d.Faces[0].UseEdge2);
        Assert.Equal(5, d.Faces[0].Edge2Px);
        Assert.True(d.Faces[1].IsInherited);
        Assert.Equal(0, d.Faces[1].XScale);
        Assert.Equal(40, d.Faces[3].SizePx);
        Assert.Equal(10, d.Faces[3].EdgePx);
        Assert.False(d.Faces[3].UseEdge2);
        Assert.Equal(3, d.Faces[3].Edge2Px);
        Assert.Equal((2, 10.0, 0), (d.DecorKind, d.DecorSizePx, d.BlurLevel));
    }

    [Fact]
    public void 読み込み_画面の高さに合わせてpxに換算する()
    {
        var root = ProjectOf(NkmFont());
        root["SourceInfo"]!["BackgroundHeight"] = 720;
        var f = N3ProjFormat.ReadFontSets(root).Single();
        Assert.Equal(80 * 720 / 1080.0, f.Detail.Faces[0].SizePx, 1);
    }

    // ------------------------------------------------------------ テンプレート

    private string WriteTemplate(string name, JsonObject font)
    {
        string path = Path.Combine(_dir, name);
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        using var writer = new StreamWriter(zip.CreateEntry("0").Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        writer.Write(font.ToJsonString());
        return path;
    }

    [Fact]
    public void テンプレート_tplを読むとテンプレートと連動したフォントとして取り込む()
    {
        var font = NkmFont();
        font["Synchronize"] = false;
        string path = WriteTemplate("4ad6be49-3d45-47e4-9aee-b4c14a0f6057.tpl", font);

        var t = N3FontTemplateReader.ReadTemplate(path);
        Assert.Equal("（麻衣）（のりこ）", t.Name);
        Assert.True(t.NkmSynchronize);
        Assert.Equal("4ad6be49-3d45-47e4-9aee-b4c14a0f6057", t.NkmGuid);
        Assert.Equal(Path.GetFullPath(path), t.ImportedFrom);
        Assert.Equal(80, t.Detail.Faces[0].SizePx);
        Assert.Equal(0, t.Detail.Faces[1].XScale); // XScale の無い古い形
        Assert.Equal(N3Brush.TypeMilleFeuille, t.Detail.Brushes[7].Type);
    }

    [Fact]
    public void テンプレート_フォルダを名前の順に読み読めないファイルは飛ばす()
    {
        var b = NkmFont();
        b["SettingsName"] = "（b）";
        b["Guid"] = "7c9e6679-7425-40de-944b-e07fc1f90ae7";
        var a = NkmFont();
        a["SettingsName"] = "（a）";
        a["Guid"] = "0f8fad5b-d9cb-469f-a165-70867728950e";
        WriteTemplate("7c9e6679-7425-40de-944b-e07fc1f90ae7.tpl", b);
        WriteTemplate("0f8fad5b-d9cb-469f-a165-70867728950e.tpl", a);
        File.WriteAllText(Path.Combine(_dir, "broken.tpl"), "zip ではない");
        File.WriteAllText(Path.Combine(_dir, "other.txt"), "対象外");

        var errors = new List<string>();
        var list = N3FontTemplateReader.ReadTemplateFolder(_dir, errors);
        Assert.Equal(new[] { "（a）", "（b）" }, list.Select(f => f.Name));
        Assert.Single(errors);
        Assert.StartsWith("broken.tpl:", errors[0]);
        Assert.Empty(N3FontTemplateReader.ReadTemplateFolder(Path.Combine(_dir, "無いフォルダ")));
    }

    [Fact]
    public void 環境_テンプレートフォルダは設定フォルダのTemplateFont()
    {
        var env = Nkm3Environment.Load(_dir);
        Assert.Equal(Path.Combine(_dir, "TemplateFont"), env.TemplateFontFolder);
        Assert.All(Nkm3Environment.FindTemplateFontFolders(), d => Assert.True(Directory.Exists(d)));
    }
}
