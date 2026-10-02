using System.Text.Json.Nodes;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Tests;

/// <summary>描画用のベースとの合わせ方（N3FontLibrary.MergeForPreview）が、書き出しの結果と同じになるか。</summary>
public class N3PreviewMergeTests
{
    private static LyricsDocument Doc(params string[] lines) => LrcFormat.Parse(string.Join("\r\n", lines) + "\r\n");

    private static N3ProjWriter.TabSource Tab(LyricsDocument doc, string path) =>
        new(new N3ProjExportTab { Name = "メイン", Document = doc, LyricsPath = path }, path, LrcFormat.Write(doc), DateTime.Now);

    /// <summary>すべての項目を指定したベースのフォント設定。</summary>
    private static N3FontSet FullBase(string name)
    {
        var f = new N3FontSet { Name = name, HasFullDetail = true };
        var d = f.Detail;
        for (int i = 0; i < N3FontDetail.FaceCount; i++)
        {
            d.Faces[i] = new N3FontFace
            {
                FontName = $"ベース{i}", FaceName = "Bold", SizePx = 70 - i * 5, XScale = 90 + i, EdgePx = 6 + i, UseEdge2 = true, Edge2Px = 3 + i,
            };
        }
        for (int i = 0; i < N3FontDetail.BrushCount; i++)
        {
            d.Brushes[i] = new N3Brush { Type = N3Brush.TypeSolid, Color = $"1{i}2{i}3{i}", AlphaPercent = 90 };
        }
        d.Brushes[4] = new N3Brush
        {
            Type = N3Brush.TypeMilleFeuille,
            Color = "ABCDEF",
            Stops = new List<N3GradientStop> { new() { Position = 0, Color = "FF0000" }, new() { Position = 1, Color = "0000FF" } },
        };
        d.DecorKind = 2;
        d.DecorSizePx = 12;
        return f;
    }

    private static string Describe(N3Brush b) =>
        $"t{b.Type} c{N3FontSet.NormalizeWeb16(b.Color)} a{b.AlphaPercent} s[{string.Join(";", b.Stops.Select(s => $"{s.Position:0.##}:{s.Color}"))}] i[{b.BitmapPath}]";

    private static string Describe(N3FontFace f) =>
        $"{f.FontName}/{f.FaceName}/{f.SizePx:0.#}/{f.XScale}/{f.EdgePx:0.#}/{f.UseEdge2}/{f.Edge2Px:0.#}";

    [Fact]
    public void 書き出しで合わせた結果と同じ()
    {
        var doc = Doc("[00:01:00]あ[00:02:00]");
        var scratch = new N3ProjExportOptions
        {
            DefaultFont = new N3FontSet { Name = "標準" },
            FontSets = new[] { FullBase("全部"), FullBase("従来") },
        };
        var baseRoot = N3ProjWriter.BuildProjectJson(@"C:\v\a.n3proj", new[] { Tab(doc, @"C:\v\a.lrc") }, scratch, null, new List<string>(), out _, out _);
        var baseSets = N3ProjFormat.ReadFontSets(baseRoot);

        // 全項目を持つフォント設定: 一部の配色が未指定、歌詞／かなは全部継承、ルビ／漢字は一部だけ指定
        var full = new N3FontSet { Name = "全部", HasFullDetail = true };
        full.Detail.Faces[0] = new N3FontFace { FontName = "自分", SizePx = 80, EdgePx = 8 };
        full.Detail.Faces[3] = new N3FontFace { FontName = "自分ルビ" };
        full.Detail.Brushes[0] = new N3Brush { Type = N3Brush.TypeSolid, Color = "FFFFFF" };
        full.Detail.Brushes[1] = new N3Brush { Type = N3Brush.TypeSolid, Color = "", AlphaPercent = 50 }; // 色は未指定で不透明度だけ
        full.Detail.Brushes[4] = new N3Brush { Type = N3Brush.TypeMilleFeuille }; // マーカーは未指定
        full.Detail.DecorKind = 1;
        full.Detail.DecorSizePx = 4;

        // 従来の項目しか持たないフォント設定
        var legacy = new N3FontSet { Name = "従来", FontFamily = "従来のフォント", SizePx = 60, EdgePx = 5, TextColorAfter = "00FF00" };

        var options = new N3ProjExportOptions { BaseProject = baseRoot.DeepClone().AsObject(), FontSets = new[] { full, legacy } };
        var root = N3ProjWriter.BuildProjectJson(@"C:\v\b.n3proj", new[] { Tab(doc, @"C:\v\b.lrc") }, options, options.BaseProject, new List<string>(), out _, out _);
        var written = N3ProjFormat.ReadFontSets(root);

        foreach (var own in new[] { full, legacy })
        {
            var expected = written.First(f => f.Name == own.Name);
            var merged = N3FontLibrary.MergeForPreview(own, baseSets.First(f => f.Name == own.Name));
            for (int i = 0; i < N3FontDetail.BrushCount; i++)
            {
                Assert.True(Describe(expected.Detail.Brushes[i]) == Describe(merged.Detail.Brushes[i]),
                    $"{own.Name} 配色 {i}: {Describe(expected.Detail.Brushes[i])} / {Describe(merged.Detail.Brushes[i])}");
            }
            for (int i = 0; i < N3FontDetail.FaceCount; i++)
            {
                string e = Describe(N3FontLibrary.RenderFace(expected, i));
                string m = Describe(N3FontLibrary.RenderFace(merged, i));
                Assert.True(e == m, $"{own.Name} フェイス {i}: {e} / {m}");
            }
            Assert.Equal(expected.Detail.DecorKind, merged.Detail.DecorKind);
            Assert.Equal(expected.Detail.DecorSizePx, merged.Detail.DecorSizePx);
        }
    }

    [Fact]
    public void ベースが無ければ複製()
    {
        var own = new N3FontSet { Name = "a", TextColorAfter = "112233" };
        var merged = N3FontLibrary.MergeForPreview(own, null);
        Assert.NotSame(own, merged);
        Assert.Equal("112233", merged.TextColorAfter);
    }
}
