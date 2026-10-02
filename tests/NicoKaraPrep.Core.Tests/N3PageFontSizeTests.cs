using System.Text.Json.Nodes;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;
using NicoKaraPrep.Core.Project;

namespace NicoKaraPrep.Core.Tests;

/// <summary>ページごとの文字の大きさの増減（大きさだけを変えたフォント設定）。</summary>
public class N3PageFontSizeTests
{
    private static N3FontSet Font(string name, double lyric, double kana, double alnum, double ruby, double rubyKana)
    {
        var f = new N3FontSet { Name = name };
        f.Detail.Faces[0] = new N3FontFace { FontName = "メイリオ", FaceName = "Bold", SizePx = lyric, EdgePx = 8 };
        f.Detail.Faces[1].SizePx = kana;
        f.Detail.Faces[2].SizePx = alnum;
        f.Detail.Faces[3].SizePx = ruby;
        f.Detail.Faces[4].SizePx = rubyKana;
        return f;
    }

    [Fact]
    public void 名前は元の名前に増減を付ける()
    {
        Assert.Equal("（麻衣）+4", N3PageFontSize.DerivedName("（麻衣）", 4));
        Assert.Equal("標準-12", N3PageFontSize.DerivedName("標準", -12));
    }

    [Fact]
    public void 同じプロジェクトにある名前に増減を付けた名前だけを書き出しで作ったものとみなす()
    {
        var names = new[] { "（麻衣）", "標準2", "A+B" };
        Assert.True(N3PageFontSize.IsDerivedName("（麻衣）+4", names));
        Assert.True(N3PageFontSize.IsDerivedName("標準2-10", names));
        Assert.True(N3PageFontSize.IsDerivedName("A+B+2", names));
        Assert.False(N3PageFontSize.IsDerivedName("（のりこ）+4", names)); // 元の名前が無い
        Assert.False(N3PageFontSize.IsDerivedName("標準2", names));
        Assert.False(N3PageFontSize.IsDerivedName("（麻衣）+0", names));
    }

    [Fact]
    public void 歌詞は増減を足しルビは同じ割合で整数に丸め継承はそのまま()
    {
        // 歌詞／漢字 80・かな 継承・英数 72・ルビ／漢字 継承（歌詞の半分）・ルビ／かな 30・ルビ／英数 継承
        var sizes = N3PageFontSize.OffsetFaceSizes(Font("a", 80, 0, 72, 0, 30), 4);
        Assert.Equal(new double[] { 84, 0, 76, 0, 32, 0 }, sizes); // 30 × 84/80 = 31.5 → 32

        // 縮めるときも同じ
        sizes = N3PageFontSize.OffsetFaceSizes(Font("a", 80, 0, 0, 40, 0), -8);
        Assert.Equal(new double[] { 72, 0, 0, 36, 0, 0 }, sizes);

        // 小さくしすぎない
        sizes = N3PageFontSize.OffsetFaceSizes(Font("a", 80, 60, 0, 0, 0), -100);
        Assert.Equal(N3PageFontSize.MinSizePx, sizes[0]);
        Assert.Equal(N3PageFontSize.MinSizePx, sizes[1]);
    }

    [Fact]
    public void 歌詞の漢字が継承ならニコカラメーカーの既定の大きさに足す()
    {
        var sizes = N3PageFontSize.OffsetFaceSizes(Font("a", 0, 0, 0, 0, 0), 4);
        Assert.Equal(N3FontLibrary.NkmDefaultFace().SizePx + 4, sizes[0]);
    }

    [Fact]
    public void 大きさを変えたフォント設定は名前と大きさだけが変わる()
    {
        var font = Font("（麻衣）", 80, 0, 72, 0, 30);
        font.EdgePx = 8;
        var derived = N3PageFontSize.Derive(font, 4);
        Assert.Equal("（麻衣）+4", derived.Name);
        Assert.Equal(84, derived.SizePx);
        Assert.Equal(8, derived.EdgePx);
        Assert.Equal(76, derived.Detail.Faces[2].SizePx);
        Assert.Equal(0, derived.RubySizePx);
        Assert.Equal(80, font.SizePx); // 元は変えない
    }

    [Fact]
    public void ページの中で最初に0以外を持つ行の値をページの行すべてに当てる()
    {
        var doc = LrcFormat.Parse("[00:01:00]あ[00:02:00]\r\n[00:03:00]い[00:04:00]\r\n\r\n[00:05:00]う[00:06:00]\r\n[00:07:00]え[00:08:00]\r\n\r\n[00:09:00]お[00:10:00]\r\n");
        doc.Lines[1].FontSizeDelta = 4;
        doc.Lines[3].FontSizeDelta = -2;
        doc.Lines[4].FontSizeDelta = 6;
        var deltas = N3PageFontSize.LineDeltas(doc, PageSplitMode.EmptyLine, 2);
        Assert.Equal(4, deltas[0]);
        Assert.Equal(4, deltas[1]);
        Assert.Equal(-2, deltas[3]);
        Assert.Equal(-2, deltas[4]);
        Assert.False(deltas.ContainsKey(6)); // 指定の無いページ
        Assert.False(deltas.ContainsKey(2)); // 空行
    }

    [Fact]
    public void 行の設定として保存して読み直せる()
    {
        var doc = LrcFormat.Parse("[00:01:00]あ[00:02:00]\r\n[00:03:00]い[00:04:00]\r\n");
        doc.Lines[1].FontSizeDelta = -6;
        Assert.True(doc.Lines[1].HasN3Overrides);
        var saved = LineExportSettings.Collect(doc);
        var copy = LrcFormat.Parse("[00:01:00]あ[00:02:00]\r\n[00:03:00]い[00:04:00]\r\n");
        LineExportSettings.Apply(copy, saved);
        Assert.Equal(0, copy.Lines[0].FontSizeDelta);
        Assert.Equal(-6, copy.Lines[1].FontSizeDelta);
        Assert.Equal(-6, copy.Lines[1].Clone().FontSizeDelta);
    }

    private static N3ProjWriter.TabSource Tab(LyricsDocument doc)
    {
        const string path = @"C:\v\size.lrc";
        return new N3ProjWriter.TabSource(new N3ProjExportTab { Name = "メイン", Document = doc, LyricsPath = path }, path, LrcFormat.Write(doc), DateTime.Now);
    }

    private static List<int> FontIndexes(JsonObject root, int lineInfo)
    {
        var lines = root["SourceLyricsInfos"]![0]!["LineInfos"]!.AsArray();
        var lyric = lines.Where(l => l!["Kind"]!.GetValue<int>() == 1).ToList();
        return lyric[lineInfo]!["LyricsCharInfos"]!.AsArray().Select(c => c!["FontIndex"]!.GetValue<int>()).ToList();
    }

    private static int FontIndexOf(JsonObject root, string name)
    {
        var fonts = root["LyricsFonts"]!.AsArray();
        for (int i = 0; i < fonts.Count; i++)
        {
            if (fonts[i]!["SettingsName"]!.GetValue<string>() == name) return i;
        }
        return -1;
    }

    private static double CharSize(JsonObject root, int font, int face) =>
        N3FontJson.SizePx(root["LyricsFonts"]![font]!["FontInfos"]![face]!["CharSize"], 1080);

    [Fact]
    public void 書き出しでは大きさを変えたフォント設定を足してページの文字に当てる()
    {
        // 1 行目は「あ」が既定の「標準」、「（麻衣）」から先が（麻衣）
        var doc = LrcFormat.Parse("[00:01:00]あ（麻衣）い[00:02:00]\r\n[00:03:00]う[00:04:00]\r\n\r\n[00:05:00]え[00:06:00]\r\n");
        doc.Lines[0].FontSizeDelta = 4;
        doc.Lines[1].FontSizeDelta = 4;
        var options = new N3ProjExportOptions
        {
            FontSets = new List<N3FontSet> { Font("標準", 80, 0, 0, 0, 0), Font("（麻衣）", 90, 0, 0, 0, 0) },
            DefaultFontSetName = "標準",
        };
        var sized = new List<string>();
        var root = N3ProjWriter.BuildProjectJson(@"C:\v\size.n3proj", new[] { Tab(doc) }, options, null, new List<string>(), out _, out int fontCount, sized);

        int mai = FontIndexOf(root, "（麻衣）");
        int maiPlus = FontIndexOf(root, "（麻衣）+4");
        int std = FontIndexOf(root, "標準");
        int stdPlus = FontIndexOf(root, "標準+4");
        Assert.True(maiPlus >= 0 && stdPlus >= 0);
        Assert.Equal(fontCount, root["LyricsFonts"]!.AsArray().Count);
        Assert.Equal(new[] { "（麻衣）+4" }, sized.Where(n => n.StartsWith("（麻衣）")));

        // 1 ページ目（+4）の文字は大きさを変えたもの、2 ページ目は元のもの
        var first = FontIndexes(root, 0);
        Assert.Equal(stdPlus, first[0]);
        Assert.All(first.Skip(1), f => Assert.Equal(maiPlus, f));
        Assert.All(FontIndexes(root, 1), f => Assert.Equal(maiPlus, f)); // 前の行の（麻衣）を引き継ぐ
        Assert.All(FontIndexes(root, 2), f => Assert.Equal(mai, f));
        Assert.True(std >= 0);

        // 大きさ: 歌詞 +4、ルビ／漢字（書き出しでは歌詞の半分）も同じ割合
        Assert.Equal(94, CharSize(root, maiPlus, 0));
        Assert.Equal(47, CharSize(root, maiPlus, 3));
        Assert.Equal(90, CharSize(root, mai, 0));
        Assert.Equal(84, CharSize(root, stdPlus, 0));
        Assert.False(root["LyricsFonts"]![maiPlus]!["Synchronize"]!.GetValue<bool>());
        Assert.NotEqual(root["LyricsFonts"]![mai]!["Guid"]!.GetValue<string>(), root["LyricsFonts"]![maiPlus]!["Guid"]!.GetValue<string>());
    }

    [Fact]
    public void 前の書き出しをベースにしても同じ名前のフォント設定は増やさず作り直す()
    {
        var doc = LrcFormat.Parse("[00:01:00]（麻衣）あ[00:02:00]\r\n");
        doc.Lines[0].FontSizeDelta = -10;
        var options = new N3ProjExportOptions
        {
            FontSets = new List<N3FontSet> { Font("（麻衣）", 90, 0, 0, 0, 0) },
        };
        var first = N3ProjWriter.BuildProjectJson(@"C:\v\size.n3proj", new[] { Tab(doc) }, options, null, new List<string>(), out _, out int count1);

        // （麻衣）の大きさを変えてから、前の書き出しをベースに書き出す
        options.FontSets = new List<N3FontSet> { Font("（麻衣）", 100, 0, 0, 0, 0) };
        options.BaseProject = first;
        var second = N3ProjWriter.BuildProjectJson(@"C:\v\size.n3proj", new[] { Tab(doc) }, options, (JsonObject)first.DeepClone(), new List<string>(), out _, out int count2);
        Assert.Equal(count1, count2);
        int derived = FontIndexOf(second, "（麻衣）-10");
        Assert.Equal(FontIndexOf(first, "（麻衣）-10"), derived);
        Assert.Equal(90, CharSize(second, derived, 0));
        Assert.All(FontIndexes(second, 0), f => Assert.Equal(derived, f));
    }
}
