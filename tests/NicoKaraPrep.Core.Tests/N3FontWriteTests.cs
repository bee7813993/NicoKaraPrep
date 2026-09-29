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
    public void 書き出し_色が未指定で不透明度だけ指定した箇所は新規では既定色マージではベースの色にその不透明度を書く()
    {
        var f = new N3FontSet { Name = "（麻衣）（のりこ）" };
        var edge2 = f.Detail.Brushes[2]; // 既定で色が空（ワイプ後の縁 2）
        Assert.Equal("", edge2.Color);
        edge2.AlphaPercent = 0;
        Assert.False(edge2.IsUnset);
        Assert.True(f.Detail.Brushes[3].IsUnset); // 色が空で不透明度 100% は未指定

        var created = Export(null, f)[0]!["BrushInfos"]!.AsArray();
        Assert.Equal("FFFFFF", created[2]!["SolidColor"]!["Web16"]!.GetValue<string>());
        Assert.Equal(0f, created[2]!["SolidColor"]!["DxColor"]!["A"]!.GetValue<float>());

        var mergedSet = Export(ProjectOf(NkmFont()), f)[0]!;
        var merged = mergedSet["BrushInfos"]!.AsArray();
        var sc = merged[2]!["SolidColor"]!;
        Assert.Equal("000000", sc["Web16"]!.GetValue<string>());
        Assert.Equal(0.0, sc["DxColor"]!["R"]!.GetValue<double>());
        Assert.Equal(0f, sc["DxColor"]!["A"]!.GetValue<float>());
        Assert.Equal("bec83c4f-e3c5-4e8d-9d68-042878d48165", sc["Guid"]!.GetValue<string>());
        Assert.Equal(0.5, merged[3]!["SolidColor"]!["DxColor"]!["A"]!.GetValue<double>(), 3); // 未指定はベースのまま

        // 読み戻すとベースの色で不透明度 0
        var back = N3ProjFormat.ReadFontSets(ProjectOf((JsonObject)mergedSet.DeepClone())).Single();
        Assert.Equal(("000000", 0), (back.Detail.Brushes[2].Color, back.Detail.Brushes[2].AlphaPercent));
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

    [Fact]
    public void 書き出し_詳細の文字列やマーカーがnullのJSONも空として読み新規でもマージでも書ける()
    {
        var f = JsonSerializer.Deserialize<N3FontSet>(
            """{"Name":"（麻衣）（のりこ）","Detail":{"Brushes":[{"Color":null,"Stops":null,"BitmapPath":null},{"Type":1,"Color":"112233","Stops":[null,{"Position":0.5,"Color":null}]}],"Faces":[{"FontName":null,"FaceName":null,"SizePx":60},{"FontName":null},null,{"FontName":null,"FaceName":null}]}}""")!;
        var b0 = f.Detail.Brushes[0];
        Assert.Equal("", b0.Color);
        Assert.Empty(b0.Stops);
        Assert.Equal("", b0.BitmapPath);
        Assert.True(b0.IsUnset);
        Assert.Equal("", Assert.Single(f.Detail.Brushes[1].Stops).Color);
        Assert.Equal("", f.FontFamily);
        Assert.True(f.Detail.Faces[1].IsInherited);
        Assert.True(f.Detail.Faces[3].IsInherited);

        var created = Export(null, f);
        Assert.Equal(N3FontDetail.BrushCount, created[0]!["BrushInfos"]!.AsArray().Count);
        var merged = Export(ProjectOf(NkmFont()), f);
        Assert.Equal("112233", merged[0]!["BrushInfos"]![1]!["SolidColor"]!["Web16"]!.GetValue<string>());
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
    public void マージ_マーカーと画像が未指定なら単色へ切り替えてもベースのまま残す()
    {
        var baseFont = NkmFont();
        baseFont["BrushInfos"]![1]!["BitmapPath"] = @"C:\img\base.png";
        baseFont["BrushInfos"]![1]!["BitmapScale"] = 120;
        var f = new N3FontSet { Name = "（麻衣）（のりこ）", EdgeColorAfter = "000000" }; // ベースはミルフィーユ

        var brush = Export(ProjectOf(baseFont), f)[0]!["BrushInfos"]![1]!;
        Assert.Equal(0, brush["SelectedBrushTypeIndex"]!.GetValue<int>());
        Assert.Equal("000000", brush["SolidColor"]!["Web16"]!.GetValue<string>());
        var stops = brush["GradientStops"]!.AsArray();
        Assert.Equal(3, stops.Count);
        Assert.Equal(0.68235296, stops[1]!["Color"]!["R"]!.GetValue<double>(), 6); // 既定 3 点ではなくベースのマーカー
        Assert.Equal(@"C:\img\base.png", brush["BitmapPath"]!.GetValue<string>());
        Assert.Equal(120, brush["BitmapScale"]!.GetValue<int>());
    }

    [Fact]
    public void マージ_塗りの種類が画像なら空のパスでも画像の設定を書く()
    {
        var baseFont = NkmFont();
        baseFont["BrushInfos"]![2]!["BitmapPath"] = @"C:\img\base.png";
        var f = N3ProjFormat.ReadFontSets(ProjectOf(NkmFont())).Single();
        f.Detail.Brushes[2].Type = N3Brush.TypeBitmap;
        f.Detail.Brushes[2].BitmapScale = 80;

        var brush = Export(ProjectOf(baseFont), f)[0]!["BrushInfos"]![2]!;
        Assert.Equal(N3Brush.TypeBitmap, brush["SelectedBrushTypeIndex"]!.GetValue<int>());
        Assert.Equal("", brush["BitmapPath"]!.GetValue<string>());
        Assert.Equal(80, brush["BitmapScale"]!.GetValue<int>());
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
    public void マージ_ルビのサイズと縁の0は歌詞の半分で上書きし縁2はベースのまま残す()
    {
        var f = new N3FontSet { Name = "（麻衣）（のりこ）", SizePx = 60, EdgePx = 10, Edge2Px = 10 };
        var ruby = Export(ProjectOf(NkmFont()), f)[0]!["FontInfos"]![3]!;
        Assert.Equal(30, ruby["CharSize"]!["Size"]!.GetValue<int>());
        Assert.Equal(5, ruby["EdgeSize"]!["Size"]!.GetValue<int>());
        Assert.Equal(3, ruby["EdgeSize2"]!["Size"]!.GetValue<int>()); // 歌詞の半分（5）ではなくベースの 3
        Assert.False(ruby["UseEdge2"]!.GetValue<bool>()); // 継承（null）で消さない
    }

    /// <summary>ルビのフォント名・フェイス・横倍率・縁 2 と歌詞の横倍率を設定したベース。</summary>
    private static JsonObject BaseWithRubyFont()
    {
        var baseFont = NkmFont();
        var faces = baseFont["FontInfos"]!.AsArray();
        faces[0]!["XScale"] = 90;
        faces[3]!["FontName"] = "ルビ用";
        faces[3]!["FontFaceName"] = "Bold";
        faces[3]!["XScale"] = 80;
        faces[3]!["UseEdge2"] = true;
        faces[3]!["EdgeSize2"] = new JsonObject { ["Size"] = 6, ["Reference"] = 1080, ["Ratio"] = 6 / 1080.0 };
        return baseFont;
    }

    [Fact]
    public void マージ_従来の項目だけのフォントはルビのフォント名や横倍率をベースのまま残す()
    {
        // 旧版の settings.json（詳細なし）と、旧項目だけを設定して作ったフォント（フォント設定ダイアログと同じ作り方）
        var fromOldJson = JsonSerializer.Deserialize<N3FontSet>(
            """{"Name":"（麻衣）（のりこ）","FontFamily":"メイリオ","FontFace":"Bold","SizePx":60,"EdgePx":10,"UseEdge2":false,"Edge2Px":4}""")!;
        var fromFlat = new N3FontSet { Name = "（麻衣）（のりこ）", FontFamily = "メイリオ", FontFace = "Bold", SizePx = 60, EdgePx = 10, UseEdge2 = false, Edge2Px = 4 };

        foreach (var f in new[] { fromOldJson, fromFlat })
        {
            var faces = Export(ProjectOf(BaseWithRubyFont()), f)[0]!["FontInfos"]!.AsArray();

            // 従来の項目は上書きする
            Assert.Equal("メイリオ", faces[0]!["FontName"]!.GetValue<string>());
            Assert.Equal(60, faces[0]!["CharSize"]!["Size"]!.GetValue<int>());
            Assert.False(faces[0]!["UseEdge2"]!.GetValue<bool>());
            Assert.Equal(4, faces[0]!["EdgeSize2"]!["Size"]!.GetValue<int>());
            Assert.Equal(30, faces[3]!["CharSize"]!["Size"]!.GetValue<int>());
            Assert.Equal(5, faces[3]!["EdgeSize"]!["Size"]!.GetValue<int>());

            // 旧項目に無い項目はベースのまま
            Assert.Equal(90, faces[0]!["XScale"]!.GetValue<int>());
            Assert.Equal("ルビ用", faces[3]!["FontName"]!.GetValue<string>());
            Assert.Equal("Bold", faces[3]!["FontFaceName"]!.GetValue<string>());
            Assert.Equal(80, faces[3]!["XScale"]!.GetValue<int>());
            Assert.True(faces[3]!["UseEdge2"]!.GetValue<bool>());
            Assert.Equal(6, faces[3]!["EdgeSize2"]!["Size"]!.GetValue<int>());
        }
    }

    [Fact]
    public void マージ_ルビと歌詞の継承でない項目は上書きする()
    {
        var f = N3ProjFormat.ReadFontSets(ProjectOf(BaseWithRubyFont())).Single();
        f.Detail.Faces[0].XScale = 95;
        var ruby = f.Detail.Faces[3];
        ruby.FontName = "別のルビ";
        ruby.FaceName = "ﾍﾋﾞｰ";
        ruby.XScale = 70;
        ruby.UseEdge2 = false;
        ruby.Edge2Px = 2;

        var faces = Export(ProjectOf(BaseWithRubyFont()), f)[0]!["FontInfos"]!.AsArray();
        Assert.Equal(95, faces[0]!["XScale"]!.GetValue<int>());
        Assert.Equal("別のルビ", faces[3]!["FontName"]!.GetValue<string>());
        Assert.Equal("ﾍﾋﾞｰ", faces[3]!["FontFaceName"]!.GetValue<string>());
        Assert.Equal(70, faces[3]!["XScale"]!.GetValue<int>());
        Assert.False(faces[3]!["UseEdge2"]!.GetValue<bool>());
        Assert.Equal(2, faces[3]!["EdgeSize2"]!["Size"]!.GetValue<int>());
    }

    [Fact]
    public void マージ_全項目を持つフォントは継承に戻した歌詞とルビの項目も継承として書く()
    {
        var f = N3ProjFormat.ReadFontSets(ProjectOf(BaseWithRubyFont())).Single();
        Assert.True(f.HasFullDetail);
        f.Detail.Faces[0].XScale = 0;
        var ruby = f.Detail.Faces[3];
        ruby.FontName = "";
        ruby.FaceName = "";
        ruby.XScale = 0;
        ruby.UseEdge2 = null;
        ruby.Edge2Px = 0;

        var merged = Export(ProjectOf(BaseWithRubyFont()), f);
        var faces = merged[0]!["FontInfos"]!.AsArray();
        Assert.Equal(0, faces[0]!["XScale"]!.GetValue<int>());
        Assert.Equal("", faces[3]!["FontName"]!.GetValue<string>());
        Assert.Equal("", faces[3]!["FontFaceName"]!.GetValue<string>());
        Assert.Equal(0, faces[3]!["XScale"]!.GetValue<int>());
        Assert.Null(faces[3]!["UseEdge2"]);

        // 書き出した結果の実効値が NicoKaraPrep の実効値と同じ（ルビの縁 2 の 0 は歌詞の半分）
        var back = N3ProjFormat.ReadFontSets(ProjectOf((JsonObject)merged[0]!.DeepClone())).Single();
        foreach (int i in new[] { 0, 3 })
        {
            var expected = N3FontLibrary.EffectiveFace(f, i);
            var actual = N3FontLibrary.EffectiveFace(back, i);
            Assert.Equal((expected.FontName, expected.FaceName, expected.XScale, expected.UseEdge2, expected.Edge2Px),
                (actual.FontName, actual.FaceName, actual.XScale, actual.UseEdge2, actual.Edge2Px));
        }
        Assert.Equal("HGS創英角ﾎﾟｯﾌﾟ体", N3FontLibrary.EffectiveFace(back, 3).FontName);
        Assert.Equal(2.5, back.Detail.Faces[3].Edge2Px);
    }

    [Fact]
    public void マージ_編集した従来の項目だけのフォントはルビの継承もそのまま書く()
    {
        var f = new N3FontSet { Name = "（麻衣）（のりこ）", FontFamily = "メイリオ", SizePx = 60 };
        N3FontLibrary.MarkEdited(f);

        var faces = Export(ProjectOf(BaseWithRubyFont()), f)[0]!["FontInfos"]!.AsArray();
        Assert.Equal(0, faces[0]!["XScale"]!.GetValue<int>());
        Assert.Equal("", faces[3]!["FontName"]!.GetValue<string>());
        Assert.Equal(0, faces[3]!["XScale"]!.GetValue<int>());
        Assert.Null(faces[3]!["UseEdge2"]);
    }
}
