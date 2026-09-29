using System.Text.Json;
using NicoKaraPrep.Core.Model;
using NicoKaraPrep.Core.Project;

namespace NicoKaraPrep.Core.Tests;

public class N3FontSetTests
{
    /// <summary>旧版（全項目モデル以前）の N3FontSet の JSON。</summary>
    private const string OldJson = """
        {
          "Name": "（花帆）",
          "FontFamily": "HGS創英角ﾎﾟｯﾌﾟ体",
          "FontFace": "ﾍﾋﾞｰ",
          "SizePx": 80,
          "EdgePx": 15,
          "UseEdge2": true,
          "Edge2Px": 5,
          "RubySizePx": 40,
          "RubyEdgePx": 7,
          "TextColorAfter": "FFFFFF",
          "EdgeColorAfter": "F8B500",
          "Edge2ColorAfter": "",
          "DecorColorAfter": "FFFFFF",
          "TextColorBefore": "F8B500",
          "EdgeColorBefore": "FFFFFF",
          "Edge2ColorBefore": "",
          "DecorColorBefore": "F8B500",
          "DecorKind": 2,
          "DecorSizePx": 10,
          "BlurLevel": 1
        }
        """;

    [Fact]
    public void モデル_新規の既定値は従来と同じ()
    {
        var f = new N3FontSet();
        Assert.Equal("メイリオ", f.FontFamily);
        Assert.Equal("Bold", f.FontFace);
        Assert.Equal(80, f.SizePx);
        Assert.Equal(8, f.EdgePx);
        Assert.False(f.UseEdge2);
        Assert.Equal(4, f.Edge2Px);
        Assert.Equal(0, f.RubySizePx);
        Assert.Equal(0, f.RubyEdgePx);
        Assert.Equal(new[] { "FFFFFF", "000000", "", "", "4DA3FF", "FFFFFF", "", "" },
            new[] { f.TextColorAfter, f.EdgeColorAfter, f.Edge2ColorAfter, f.DecorColorAfter, f.TextColorBefore, f.EdgeColorBefore, f.Edge2ColorBefore, f.DecorColorBefore });
        Assert.Equal(0, f.DecorKind);
        Assert.Equal(10, f.DecorSizePx);
        Assert.Equal(2, f.BlurLevel);

        // 詳細: かな・英数とルビは継承、色の無い箇所は未指定
        Assert.All(new[] { 1, 2, 3, 4, 5 }, i => Assert.True(f.Detail.Faces[i].IsInherited));
        Assert.True(f.Detail.Brushes[2].IsUnset);
        Assert.False(f.Detail.Brushes[0].IsUnset);
        Assert.All(f.Detail.Brushes, b => Assert.Equal(100, b.AlphaPercent));
        Assert.False(f.NkmSynchronize);
        Assert.Null(f.NkmGuid);
        Assert.False(string.IsNullOrEmpty(f.Id));
    }

    [Fact]
    public void モデル_旧項目は詳細の歌詞とルビの漢字と配色へ転送する()
    {
        var f = new N3FontSet { FontFamily = "游ゴシック", FontFace = "", SizePx = 90, EdgePx = 6, UseEdge2 = true, Edge2Px = 3, RubySizePx = 45, RubyEdgePx = 5 };
        var d = f.Detail;
        Assert.Equal("游ゴシック", d.Faces[0].FontName);
        Assert.Equal("", d.Faces[0].FaceName);
        Assert.Equal(90, d.Faces[0].SizePx);
        Assert.Equal(6, d.Faces[0].EdgePx);
        Assert.True(d.Faces[0].UseEdge2);
        Assert.Equal(3, d.Faces[0].Edge2Px);
        Assert.Equal(45, d.Faces[3].SizePx);
        Assert.Equal(5, d.Faces[3].EdgePx);

        f.DecorColorBefore = "123456";
        Assert.Equal("123456", d.Brushes[7].Color);
        f.DecorKind = 1;
        f.DecorSizePx = 3;
        f.BlurLevel = 0;
        Assert.Equal((1, 3.0, 0), (d.DecorKind, d.DecorSizePx, d.BlurLevel));

        // 縁 2 の継承（null）は旧項目では false に見える
        d.Faces[0].UseEdge2 = null;
        Assert.False(f.UseEdge2);
    }

    [Fact]
    public void モデル_単色以外の箇所は旧項目では空に見え色を入れると単色になる()
    {
        var f = new N3FontSet();
        var b = f.Detail.Brushes[4];
        b.Type = N3Brush.TypeMilleFeuille;
        b.Color = "9B9B9B";
        b.Stops = new List<N3GradientStop> { new() { Position = 0, Color = "66C5EC" }, new() { Position = 0.5, Color = "AE62FF" }, new() { Position = 1, Color = "808080" } };
        Assert.Equal("", f.TextColorBefore);

        // 空を入れても単色以外の箇所は変わらない（旧版の画面で保存しても消えない）
        f.TextColorBefore = "";
        Assert.Equal(N3Brush.TypeMilleFeuille, b.Type);
        Assert.Equal("9B9B9B", b.Color);

        f.TextColorBefore = "FF0000";
        Assert.Equal(N3Brush.TypeSolid, b.Type);
        Assert.Equal("FF0000", f.TextColorBefore);
        Assert.Equal(3, b.Stops.Count); // マーカーは残る（ニコカラメーカーと同じ）
    }

    [Fact]
    public void JSON互換_旧形式を読むと詳細に入りIdが付く()
    {
        var f = JsonSerializer.Deserialize<N3FontSet>(OldJson)!;
        Assert.Equal("（花帆）", f.Name);
        Assert.Equal("HGS創英角ﾎﾟｯﾌﾟ体", f.Detail.Faces[0].FontName);
        Assert.Equal("ﾍﾋﾞｰ", f.Detail.Faces[0].FaceName);
        Assert.Equal(80, f.Detail.Faces[0].SizePx);
        Assert.Equal(15, f.Detail.Faces[0].EdgePx);
        Assert.True(f.Detail.Faces[0].UseEdge2);
        Assert.Equal(5, f.Detail.Faces[0].Edge2Px);
        Assert.Equal(40, f.Detail.Faces[3].SizePx);
        Assert.Equal(7, f.Detail.Faces[3].EdgePx);
        Assert.Equal("F8B500", f.Detail.Brushes[1].Color);
        Assert.Equal("F8B500", f.Detail.Brushes[4].Color);
        Assert.True(f.Detail.Brushes[2].IsUnset);
        Assert.Equal(2, f.Detail.DecorKind);
        Assert.Equal(1, f.Detail.BlurLevel);
        Assert.True(Guid.TryParse(f.Id, out _));

        // 保存し直すと同じ Id が残る
        var again = JsonSerializer.Deserialize<N3FontSet>(JsonSerializer.Serialize(f))!;
        Assert.Equal(f.Id, again.Id);
    }

    [Fact]
    public void JSON互換_保存すると旧項目の後に詳細を書き旧項目だけでも読める()
    {
        var f = JsonSerializer.Deserialize<N3FontSet>(OldJson)!;
        f.Detail.Brushes[4].Type = N3Brush.TypeGradient;
        f.Detail.Brushes[0].AlphaPercent = 0;
        f.Detail.Faces[1].FontName = "Arial";
        f.NkmGuid = "b6c2957b-4754-46aa-8029-35138698a53a";
        f.NkmSynchronize = true;
        string json = JsonSerializer.Serialize(f);

        using (var doc = JsonDocument.Parse(json))
        {
            var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
            foreach (string old in new[] { "Name", "FontFamily", "FontFace", "SizePx", "EdgePx", "UseEdge2", "Edge2Px", "RubySizePx", "RubyEdgePx",
                "TextColorAfter", "EdgeColorAfter", "Edge2ColorAfter", "DecorColorAfter", "TextColorBefore", "EdgeColorBefore", "Edge2ColorBefore", "DecorColorBefore",
                "DecorKind", "DecorSizePx", "BlurLevel" })
            {
                Assert.Contains(old, keys);
            }
            Assert.Equal("Detail", keys[^1]);
            Assert.Equal("", doc.RootElement.GetProperty("TextColorBefore").GetString()); // グラデーションは旧項目では空
        }

        var back = JsonSerializer.Deserialize<N3FontSet>(json)!;
        Assert.Equal(JsonSerializer.Serialize(f), JsonSerializer.Serialize(back));
        Assert.Equal(N3Brush.TypeGradient, back.Detail.Brushes[4].Type);
        Assert.Equal("F8B500", back.Detail.Brushes[4].Color);
        Assert.Equal(0, back.Detail.Brushes[0].AlphaPercent);
        Assert.Equal("Arial", back.Detail.Faces[1].FontName);
        Assert.True(back.NkmSynchronize);
    }

    [Fact]
    public void JSON互換_旧項目と詳細の両方があれば後に来る詳細が勝つ()
    {
        const string detail = """{"Faces":[{"FontName":"詳細のフォント","SizePx":72,"UseEdge2":null}]}""";
        var back = JsonSerializer.Deserialize<N3FontSet>(
            """{"Name":"a","FontFamily":"旧項目のフォント","SizePx":10,"UseEdge2":false,"Detail":""" + detail + "}")!;
        Assert.Equal("詳細のフォント", back.FontFamily);
        Assert.Equal(72, back.SizePx);
        Assert.Null(back.Detail.Faces[0].UseEdge2);

        // 詳細が先にあれば後の旧項目が勝つ（読み込みは JSON の順）
        var reversed = JsonSerializer.Deserialize<N3FontSet>(
            """{"Name":"a","Detail":""" + detail + ""","FontFamily":"旧項目のフォント","SizePx":10}""")!;
        Assert.Equal("旧項目のフォント", reversed.FontFamily);
        Assert.Equal(10, reversed.SizePx);
    }

    [Fact]
    public void JSON互換_配色やフォントの数が足りない詳細は補う()
    {
        var f = JsonSerializer.Deserialize<N3FontSet>("""{"Name":"a","Detail":{"Brushes":[{"Type":1,"Color":"112233"}],"Faces":[null,{"FontName":"Arial"}]}}""")!;
        Assert.Equal(N3FontDetail.BrushCount, f.Detail.Brushes.Length);
        Assert.Equal(N3FontDetail.FaceCount, f.Detail.Faces.Length);
        Assert.Equal(1, f.Detail.Brushes[0].Type);
        Assert.True(f.Detail.Brushes[7].IsUnset);
        Assert.True(f.Detail.Faces[0].IsInherited);
        Assert.Equal("Arial", f.Detail.Faces[1].FontName);
    }

    [Fact]
    public void 複製_深いコピーで元に影響しない()
    {
        var f = new N3FontSet { Name = "a" };
        f.Detail.Brushes[1].Stops.Add(new N3GradientStop { Position = 0.5, Color = "123456" });
        var c = f.Clone();
        Assert.Equal(f.Id, c.Id);

        c.Detail.Brushes[1].Stops[0].Color = "FFFFFF";
        c.Detail.Brushes[1].Stops.Add(new N3GradientStop());
        c.Detail.Faces[0].FontName = "別";
        c.Detail.Brushes[0] = new N3Brush { Color = "000000" };
        c.SizePx = 1;

        Assert.Equal("123456", f.Detail.Brushes[1].Stops[0].Color);
        Assert.Single(f.Detail.Brushes[1].Stops);
        Assert.Equal("メイリオ", f.FontFamily);
        Assert.Equal("FFFFFF", f.TextColorAfter);
        Assert.Equal(80, f.SizePx);
    }

    [Fact]
    public void JSON互換_アプリ設定の旧形式のフォント設定を読める()
    {
        string json = "{\"N3FontSets\":[" + OldJson + "]}";
        var settings = JsonSerializer.Deserialize<AppSettings>(json)!;
        var f = Assert.Single(settings.N3FontSets);
        Assert.Equal("（花帆）", f.Name);
        Assert.Equal(15, f.Detail.Faces[0].EdgePx);
        Assert.Equal("F8B500", f.Detail.Brushes[7].Color);

        var back = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal(JsonSerializer.Serialize(f), JsonSerializer.Serialize(back.N3FontSets[0]));
    }
}
