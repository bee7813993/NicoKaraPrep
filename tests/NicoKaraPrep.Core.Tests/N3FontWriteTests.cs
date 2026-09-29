using System.Text.Json;
using System.Text.Json.Nodes;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;
using static NicoKaraPrep.Core.Tests.N3FontSamples;

namespace NicoKaraPrep.Core.Tests;

/// <summary>フォント設定の n3proj への書き出し（新規・マージ）。</summary>
public class N3FontWriteTests
{
    private const string Ver = "Ver 13.79";

    private static N3ProjWriter.TabSource Tab()
    {
        var doc = LrcFormat.Parse("[00:01:00]あ[00:02:00]\r\n");
        return new N3ProjWriter.TabSource(new N3ProjExportTab { Name = "メイン", Document = doc, LyricsPath = @"C:\v\a.lrc" }, @"C:\v\a.lrc", LrcFormat.Write(doc), DateTime.Now);
    }

    /// <summary>フォント設定をベース（null なら無し）へ書き出した LyricsFonts。</summary>
    private static JsonArray Export(JsonObject? baseRoot, params N3FontSet[] fontSets)
    {
        var root = N3ProjWriter.BuildProjectJson(@"C:\v\a.n3proj", new[] { Tab() },
            new N3ProjExportOptions { BaseProject = baseRoot, FontSets = fontSets }, baseRoot, new List<string>(), out _, out _);
        return root["LyricsFonts"]!.AsArray();
    }

    private static string DetailJson(N3FontSet f) => JsonSerializer.Serialize(f.Detail);

    /// <summary>全項目に既定以外の値を入れたフォント設定。</summary>
    private static N3FontSet FullFont()
    {
        var f = new N3FontSet { Name = "（全部）", NkmGuid = "0f8fad5b-d9cb-469f-a165-70867728950e", NkmSynchronize = true };
        var d = f.Detail;
        for (int i = 0; i < N3FontDetail.BrushCount; i++)
        {
            d.Brushes[i] = new N3Brush
            {
                Type = i % 4,
                Color = $"{i * 16:X2}8040",
                AlphaPercent = i * 10,
                Stops = new List<N3GradientStop>
                {
                    new() { Position = 0, Color = "112233", AlphaPercent = 100 },
                    new() { Position = 0.34, Color = "445566", AlphaPercent = 50 },
                    new() { Position = 1, Color = "808080", AlphaPercent = 0 },
                },
                BitmapPath = i == 3 ? @"C:\img\tex.png" : "",
                BitmapScale = 100 + i,
            };
        }
        for (int i = 0; i < N3FontDetail.FaceCount; i++)
        {
            d.Faces[i] = new N3FontFace
            {
                FontName = $"フォント{i}",
                FaceName = i % 2 == 0 ? "ﾍﾋﾞｰ" : "Bold",
                SizePx = 30 + i * 10,
                XScale = 90 + i,
                EdgePx = 2 + i,
                UseEdge2 = i switch { 0 => true, 1 => false, _ => null },
                Edge2Px = 1 + i,
            };
        }
        d.DecorKind = 1;
        d.DecorSizePx = 6;
        d.BlurLevel = 1;
        return f;
    }

    // ------------------------------------------------------------ 書き出し（新規）

    [Fact]
    public void 書き出し_新規は全項目を書き読み戻すと一致する()
    {
        var f = FullFont();
        var fonts = Export(null, f);
        var back = N3ProjFormat.ReadFontSets(new JsonObject
        {
            ["SourceInfo"] = new JsonObject { ["BackgroundHeight"] = 1080 },
            ["LyricsFonts"] = fonts.DeepClone(),
        }).Single();
        Assert.Equal(DetailJson(f), DetailJson(back));
        Assert.Equal(f.NkmGuid, back.NkmGuid);
        Assert.True(back.NkmSynchronize);

        var set = fonts[0]!;
        Assert.Equal(f.NkmGuid, set["Guid"]!.GetValue<string>());
        Assert.True(set["Synchronize"]!.GetValue<bool>());
        Assert.Equal(0.3f, set["BrushInfos"]![3]!["SolidColor"]!["DxColor"]!["A"]!.GetValue<float>());
        Assert.Equal(95, set["FontInfos"]![5]!["XScale"]!.GetValue<int>());
    }

    [Fact]
    public void 書き出し_新規は未指定の色を既定色にしマーカーはニコカラメーカーの既定3点にする()
    {
        var fonts = Export(null, new N3FontSet { Name = "標準" });
        var brushes = fonts[0]!["BrushInfos"]!.AsArray();
        Assert.Equal(new[] { "FFFFFF", "000000", "FFFFFF", "000000", "4DA3FF", "FFFFFF", "000000", "000000" },
            brushes.Select(b => b!["SolidColor"]!["Web16"]!.GetValue<string>()));
        var stops = brushes[0]!["GradientStops"]!.AsArray();
        Assert.Equal(3, stops.Count);
        Assert.Equal(0.5f, stops[1]!["Position"]!.GetValue<float>());
        Assert.Equal(128 / 255f, stops[1]!["Color"]!["R"]!.GetValue<float>());
        Assert.False(fonts[0]!["Synchronize"]!.GetValue<bool>());

        // ルビの 0 は歌詞の半分、かな・英数は継承（空のサイズ）
        var faces = fonts[0]!["FontInfos"]!.AsArray();
        Assert.Equal(40, faces[3]!["CharSize"]!["Size"]!.GetValue<int>());
        Assert.Equal(4, faces[3]!["EdgeSize"]!["Size"]!.GetValue<int>());
        Assert.Equal(2, faces[3]!["EdgeSize2"]!["Size"]!.GetValue<int>());
        Assert.Equal(0, faces[1]!["CharSize"]!["Reference"]!.GetValue<int>());
        Assert.Equal(new[] { "歌詞／漢字", "かな", "英数", "ルビ／漢字", "かな", "英数" }, faces.Select(x => x!["SettingsName"]!.GetValue<string>()));
    }

    [Fact]
    public void 書き出し_透明のフォントは新規でもマージでも透明のまま()
    {
        var clear = new N3FontSet { Name = "（透明）" };
        foreach (var b in clear.Detail.Brushes)
        {
            b.Color = "FFFFFF";
            b.AlphaPercent = 0;
        }

        var created = Export(null, clear);
        Assert.All(created[0]!["BrushInfos"]!.AsArray(), b => Assert.Equal(0f, b!["SolidColor"]!["DxColor"]!["A"]!.GetValue<float>()));

        // 不透明のベースへマージ
        var opaque = N3ProjWriter.NewFontSet(new N3FontSet { Name = "（透明）" }, 1080, Ver);
        var merged = Export(ProjectOf(opaque), clear);
        Assert.Single(merged);
        Assert.All(merged[0]!["BrushInfos"]!.AsArray(), b => Assert.Equal(0f, b!["SolidColor"]!["DxColor"]!["A"]!.GetValue<float>()));

        // 読み戻しても 0
        var back = N3ProjFormat.ReadFontSets(ProjectOf((JsonObject)merged[0]!.DeepClone())).Single();
        Assert.All(back.Detail.Brushes, b => Assert.Equal(0, b.AlphaPercent));
    }

    [Fact]
    public void 書き出し_取り込んだGuidが既にあれば新しいGuidにして連動しない()
    {
        var a = new N3FontSet { Name = "a", NkmGuid = "0f8fad5b-d9cb-469f-a165-70867728950e", NkmSynchronize = true };
        var b = new N3FontSet { Name = "b", NkmGuid = "0f8fad5b-d9cb-469f-a165-70867728950e", NkmSynchronize = true };
        var fonts = Export(null, a, b);
        Assert.Equal(a.NkmGuid, fonts[0]!["Guid"]!.GetValue<string>());
        Assert.True(fonts[0]!["Synchronize"]!.GetValue<bool>());
        Assert.NotEqual(a.NkmGuid, fonts[1]!["Guid"]!.GetValue<string>());
        Assert.False(fonts[1]!["Synchronize"]!.GetValue<bool>());

        // 連動していないフォントは Guid を引き継いでも連動しない
        var c = new N3FontSet { Name = "c", NkmGuid = "7c9e6679-7425-40de-944b-e07fc1f90ae7" };
        var single = Export(null, c);
        Assert.Equal(c.NkmGuid, single[0]!["Guid"]!.GetValue<string>());
        Assert.False(single[0]!["Synchronize"]!.GetValue<bool>());
    }

    // ------------------------------------------------------------ 書き出し（マージ）

    [Fact]
    public void マージ_読み込んだフォントを戻すと元と同じ()
    {
        var original = NkmFont();
        var f = N3ProjFormat.ReadFontSets(ProjectOf((JsonObject)original.DeepClone())).Single();
        var merged = Export(ProjectOf((JsonObject)original.DeepClone()), f);

        var expected = (JsonObject)original.DeepClone();
        var actual = (JsonObject)merged[0]!.DeepClone();
        foreach (var o in new[] { expected, actual })
        {
            o.Remove("LastModified");
            o.Remove("ModifyAppVer");
            o["Index"] = 0;
        }
        Assert.Equal(expected.ToJsonString(), actual.ToJsonString());
    }

    [Fact]
    public void マージ_配色は丸ごと上書きし下位のGuidは残す()
    {
        var f = N3ProjFormat.ReadFontSets(ProjectOf(NkmFont())).Single();
        var b = f.Detail.Brushes[4];
        b.Type = N3Brush.TypeGradient;
        b.Color = "FF0000";
        b.AlphaPercent = 25;
        b.Stops = new List<N3GradientStop> { new() { Position = 0, Color = "000000" }, new() { Position = 1, Color = "FFFFFF", AlphaPercent = 40 } };
        b.BitmapPath = @"C:\img\a.png";
        b.BitmapScale = 150;

        var merged = Export(ProjectOf(NkmFont()), f)[0]!;
        var brush = merged["BrushInfos"]![4]!;
        Assert.Equal(1, brush["SelectedBrushTypeIndex"]!.GetValue<int>());
        Assert.Equal("FF0000", brush["SolidColor"]!["Web16"]!.GetValue<string>());
        Assert.Equal(0.25f, brush["SolidColor"]!["DxColor"]!["A"]!.GetValue<float>());
        Assert.Equal("bec83c4f-e3c5-4e8d-9d68-042878d48165", brush["SolidColor"]!["Guid"]!.GetValue<string>());
        Assert.Equal("5733235b-2b6d-4807-b4eb-7dce5f5aed03", brush["Guid"]!.GetValue<string>());
        Assert.Equal(2, brush["GradientStops"]!.AsArray().Count);
        Assert.Equal(0.4f, brush["GradientStops"]![1]!["Color"]!["A"]!.GetValue<float>());
        Assert.Equal(@"C:\img\a.png", brush["BitmapPath"]!.GetValue<string>());
        Assert.Equal(150, brush["BitmapScale"]!.GetValue<int>());
        Assert.Equal(Ver, brush["ModifyAppVer"]!.GetValue<string>());
    }

    [Fact]
    public void マージ_未指定の配色と継承のかな英数はベースのまま残す()
    {
        var f = new N3FontSet { Name = "（麻衣）（のりこ）", FontFamily = "游ゴシック", TextColorAfter = "FF0000", EdgeColorAfter = "", TextColorBefore = "", EdgeColorBefore = "" };
        var baseFont = NkmFont();
        baseFont["FontInfos"]![1]!["FontName"] = "かなのフォント";
        var merged = Export(ProjectOf(baseFont), f)[0]!;

        var brushes = merged["BrushInfos"]!.AsArray();
        Assert.Equal("FF0000", brushes[0]!["SolidColor"]!["Web16"]!.GetValue<string>());
        Assert.Equal(2, brushes[1]!["SelectedBrushTypeIndex"]!.GetValue<int>()); // 空はミルフィーユを残す
        Assert.Equal(2, brushes[4]!["SelectedBrushTypeIndex"]!.GetValue<int>());
        Assert.Equal(0.5f, brushes[3]!["SolidColor"]!["DxColor"]!["A"]!.GetValue<double>(), 3);
        Assert.Equal("かなのフォント", merged["FontInfos"]![1]!["FontName"]!.GetValue<string>());
        Assert.Equal("游ゴシック", merged["FontInfos"]![0]!["FontName"]!.GetValue<string>());
        Assert.False(merged["FontInfos"]![0]!["UseEdge2"]!.GetValue<bool>()); // 旧項目の false で上書き
        Assert.False(merged["FontInfos"]![1]!.AsObject().ContainsKey("XScale")); // 無いキーは足さない
    }

    [Fact]
    public void マージ_かな英数に値があれば上書きする()
    {
        var f = N3ProjFormat.ReadFontSets(ProjectOf(NkmFont())).Single();
        f.Detail.Faces[1].FontName = "Arial";
        f.Detail.Faces[1].XScale = 90;
        var face = Export(ProjectOf(NkmFont()), f)[0]!["FontInfos"]![1]!;
        Assert.Equal("Arial", face["FontName"]!.GetValue<string>());
        Assert.Equal(90, face["XScale"]!.GetValue<int>());
        Assert.Equal(0, face["CharSize"]!["Size"]!.GetValue<int>());
    }

    [Fact]
    public void マージ_テンプレート連動は編集していない同じGuidのときだけ残す()
    {
        var f = N3ProjFormat.ReadFontSets(ProjectOf(NkmFont())).Single();
        Assert.True(Export(ProjectOf(NkmFont()), f)[0]!["Synchronize"]!.GetValue<bool>());

        N3FontLibrary.MarkEdited(f);
        var edited = Export(ProjectOf(NkmFont()), f)[0]!;
        Assert.False(edited["Synchronize"]!.GetValue<bool>());
        Assert.Equal("4ad6be49-3d45-47e4-9aee-b4c14a0f6057", edited["Guid"]!.GetValue<string>());

        var other = N3ProjFormat.ReadFontSets(ProjectOf(NkmFont())).Single();
        other.NkmGuid = Guid.NewGuid().ToString();
        Assert.False(Export(ProjectOf(NkmFont()), other)[0]!["Synchronize"]!.GetValue<bool>());

        // ベースが連動していなければ連動しない
        var unsynced = NkmFont();
        unsynced["Synchronize"] = false;
        var fresh = N3ProjFormat.ReadFontSets(ProjectOf(NkmFont())).Single();
        Assert.False(Export(ProjectOf(unsynced), fresh)[0]!["Synchronize"]!.GetValue<bool>());
    }

    [Fact]
    public void マージ_ルビの0は歌詞の半分で上書きする()
    {
        var f = new N3FontSet { Name = "（麻衣）（のりこ）", SizePx = 60, EdgePx = 10, Edge2Px = 6 };
        var ruby = Export(ProjectOf(NkmFont()), f)[0]!["FontInfos"]![3]!;
        Assert.Equal(30, ruby["CharSize"]!["Size"]!.GetValue<int>());
        Assert.Equal(5, ruby["EdgeSize"]!["Size"]!.GetValue<int>());
        Assert.Equal(3, ruby["EdgeSize2"]!["Size"]!.GetValue<int>());
        Assert.Null(ruby["UseEdge2"]);
    }
}
