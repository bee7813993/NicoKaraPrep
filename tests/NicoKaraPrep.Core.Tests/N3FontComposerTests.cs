using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Tests;

public class N3FontComposerTests
{
    private static N3ColorPattern Chara => N3ColorPatterns.BuiltIns.Single(p => p.Id == N3ColorPatterns.CharaInverseId);

    private static N3Brush Solid(string color) => new() { Type = N3Brush.TypeSolid, Color = color, AlphaPercent = 100 };

    /// <summary>ユーザーのキャラ用フォント（メイン色 → [1][4][7]、白 → [0][3][5]）。</summary>
    private static N3FontSet Chara1(string name, string color)
    {
        var f = new N3FontSet { Name = name };
        f.Detail.Faces[0].FontName = "HGS創英角ﾎﾟｯﾌﾟ体";
        f.Detail.Faces[0].SizePx = 80;
        string[] colors = { "FFFFFF", color, "000000", "FFFFFF", color, "FFFFFF", "FFFFFF", color };
        for (int i = 0; i < 8; i++) f.Detail.Brushes[i] = Solid(colors[i]);
        return f;
    }

    private static string Stops(N3Brush b) =>
        string.Join(" ", b.Stops.Select(s => $"{s.Position:0.####}:{s.Color}"));

    // ------------------------------------------------------------ 帯

    [Fact]
    public void 帯_端ほど広い_既定はユーザーの手作業の比率とほぼ同じ()
    {
        Assert.Equal(new[] { 0.5, 0.5 }, N3FontComposer.BandWidths(2, 10));

        // 3 人: 0.34 / 0.66（手作業では 0.34 / 0.65）
        var three = N3FontComposer.BandWidths(3, 10);
        Assert.Equal(0.3438, Math.Round(three[0], 4));
        Assert.Equal(0.3125, Math.Round(three[1], 4));
        Assert.True(three[0] > three[1]);

        // 4 人: 境目が 0.26 / 0.5 / 0.74（手作業と同じ）
        var four = N3FontComposer.BandWidths(4, 10);
        Assert.Equal(0.26, Math.Round(four[0], 2));
        Assert.Equal(0.74, Math.Round(four[0] + four[1] + four[2], 2));

        // 0 % で等分
        Assert.All(N3FontComposer.BandWidths(5, 0), w => Assert.Equal(0.2, w, 10));
        Assert.Equal(1.0, N3FontComposer.BandWidths(6, 25).Sum(), 10);
    }

    [Fact]
    public void 帯_ミルフィーユは帯の上端と最後に塗られないマーカー_グラデーションは両端と帯の中央()
    {
        var colors = new List<(string, int)> { ("F8B500", 100), ("68BE8D", 100), ("E4007F", 100) };
        var mille = N3FontComposer.MultiColorBrush(colors, N3Brush.TypeMilleFeuille, 10);
        Assert.Equal(N3Brush.TypeMilleFeuille, mille.Type);
        Assert.Equal("0:F8B500 0.3438:68BE8D 0.6563:E4007F 1:808080", Stops(mille));

        var grad = N3FontComposer.MultiColorBrush(colors, N3Brush.TypeGradient, 10);
        Assert.Equal(N3Brush.TypeGradient, grad.Type);
        Assert.Equal("0:F8B500 0.5:68BE8D 1:E4007F", Stops(grad));
    }

    [Fact]
    public void 端を広く並べ直す_ミルフィーユは帯の上端と最後を100パーセント_色の並びはそのまま()
    {
        var mille = new N3Brush
        {
            Type = N3Brush.TypeMilleFeuille,
            Stops = new List<N3GradientStop>
            {
                new() { Position = 0.5, Color = "E4007F" },
                new() { Position = 0, Color = "F8B500" },
                new() { Position = 0.25, Color = "68BE8D" },
                new() { Position = 0.75, Color = "A2D7DD" },
                new() { Position = 1, Color = "808080" },
            },
        };
        N3FontComposer.WidenEnds(mille, 10);
        Assert.Equal("0:F8B500 0.2578:68BE8D 0.5:E4007F 0.7422:A2D7DD 1:808080", Stops(mille));

        // 0 % で等分（均等に配置と同じ）
        N3FontComposer.WidenEnds(mille, 0);
        Assert.Equal("0:F8B500 0.25:68BE8D 0.5:E4007F 0.75:A2D7DD 1:808080", Stops(mille));
    }

    [Fact]
    public void 端を広く並べ直す_グラデーションは両端と帯の中央_組み合わせで作ったときと同じ位置()
    {
        var colors = new List<(string, int)> { ("F8B500", 100), ("68BE8D", 100), ("E4007F", 100), ("A2D7DD", 100) };
        var made = N3FontComposer.MultiColorBrush(colors, N3Brush.TypeGradient, 10);
        Assert.Equal("0:F8B500 0.3789:68BE8D 0.6211:E4007F 1:A2D7DD", Stops(made));

        var grad = N3FontComposer.MultiColorBrush(colors, N3Brush.TypeGradient, 0);
        N3FontComposer.WidenEnds(grad, 10);
        Assert.Equal(Stops(made), Stops(grad));

        // マーカーが 1 つ・単色は変えない
        var one = new N3Brush { Type = N3Brush.TypeGradient, Stops = new List<N3GradientStop> { new() { Position = 0.3, Color = "F8B500" } } };
        N3FontComposer.WidenEnds(one, 10);
        Assert.Equal("0.3:F8B500", Stops(one));
        var solid = new N3Brush { Type = N3Brush.TypeSolid, Color = "F8B500", Stops = new List<N3GradientStop> { new() { Position = 0.3 }, new() { Position = 0.6 } } };
        N3FontComposer.WidenEnds(solid, 10);
        Assert.Equal(0.3, solid.Stops[0].Position);
    }

    // ------------------------------------------------------------ 作る

    [Fact]
    public void 作る_メイン色だけ多色_ベース色と色以外は最初の元フォント()
    {
        var kozue = Chara1("（梢）", "68BE8D");
        var ginko = Chara1("（吟子）", "A2D7DD");
        ginko.Detail.Brushes[0] = Solid("F0F0F0"); // 2 人目のベース色は使わない
        var f = N3FontComposer.Create(new[] { kozue, ginko }, Chara, N3Brush.TypeMilleFeuille, 10);

        Assert.Equal("（梢）（吟子）", f.Name);
        Assert.NotEqual(kozue.Id, f.Id);
        Assert.Equal(new[] { kozue.Id, ginko.Id }, f.Composition!.SourceIds);
        Assert.True(f.Composition.Linked);
        Assert.Equal(N3ColorPatterns.CharaInverseId, f.ColorPatternId);
        foreach (int s in new[] { 1, 4, 7 })
        {
            Assert.Equal(N3Brush.TypeMilleFeuille, f.Detail.Brushes[s].Type);
            Assert.Equal("0:68BE8D 0.5:A2D7DD 1:808080", Stops(f.Detail.Brushes[s]));
        }
        Assert.NotSame(f.Detail.Brushes[1], f.Detail.Brushes[4]);
        Assert.Equal("FFFFFF", f.Detail.Brushes[0].Color); // ベース色は最初の元フォント（白）
        Assert.Equal("HGS創英角ﾎﾟｯﾌﾟ体", f.Detail.Faces[0].FontName);
        Assert.True(N3ColorPatterns.Evaluate(f.Detail, Chara).IsExact);
    }

    [Fact]
    public void 作る_元が組み合わせフォントならその色を並べる()
    {
        var a = Chara1("（A）", "111111");
        var b = Chara1("（B）", "222222");
        var c = Chara1("（C）", "333333");
        var ab = N3FontComposer.Create(new[] { a, b }, Chara, N3Brush.TypeMilleFeuille, 0);
        var abc = N3FontComposer.Create(new[] { ab, c }, Chara, N3Brush.TypeMilleFeuille, 0);
        Assert.Equal("（A）（B）（C）", abc.Name);
        Assert.Equal("0:111111 0.3333:222222 0.6667:333333 1:808080", Stops(abc.Detail.Brushes[4]));
    }

    [Fact]
    public void 作り直す_元のメイン色を直すと作り直せる_手で変えたかも分かる()
    {
        var kozue = Chara1("（梢）", "68BE8D");
        var ginko = Chara1("（吟子）", "A2D7DD");
        var f = N3FontComposer.Create(new[] { kozue, ginko }, Chara, N3Brush.TypeMilleFeuille, 10);
        var all = new List<N3FontSet> { kozue, ginko, f };
        var patterns = N3ColorPatterns.All(null);
        Assert.True(N3FontComposer.MatchesSources(f, all, patterns));

        foreach (int s in new[] { 1, 4, 7 }) kozue.Detail.Brushes[s] = Solid("00AA00");
        Assert.False(N3FontComposer.MatchesSources(f, all, patterns));
        Assert.True(N3FontComposer.Recompose(f, all, patterns));
        Assert.Equal("0:00AA00 0.5:A2D7DD 1:808080", Stops(f.Detail.Brushes[4]));

        // 元が見つからなければ作り直さない
        Assert.False(N3FontComposer.Recompose(f, new List<N3FontSet> { kozue, f }, patterns));
    }

    // ------------------------------------------------------------ 歌詞から見つける

    [Fact]
    public void 見つける_続けて並んだ絵文字の組のうちフォント設定が無いもの()
    {
        var line = TextEditModeFormat.ParseLyricLine("[1|00:01:00]（梢）[1|00:01:00]（吟子）[1|00:02:00]あ[1|00:03:00]い（慈）う");
        var fonts = new HashSet<string> { "（梢）", "（吟子）", "（慈）" };
        var runs = N3FontComposer.FindMissingRuns(line, new[] { "（梢）", "（吟子）", "（慈）", "＿" }, fonts);
        var run = Assert.Single(runs);
        Assert.Equal(new[] { "（梢）", "（吟子）" }, run);

        // すでにあれば作らない
        fonts.Add("（梢）（吟子）");
        Assert.Empty(N3FontComposer.FindMissingRuns(line, new[] { "（梢）", "（吟子）", "（慈）" }, fonts));
    }

    [Fact]
    public void 見つける_3人_フォント設定の無い絵文字_同じ絵文字の繰り返しは除く()
    {
        var fonts = new HashSet<string> { "（梢）", "（吟子）", "（慈）" };
        var three = TextEditModeFormat.ParseLyricLine("（梢）（吟子）（慈）あ");
        Assert.Equal(new[] { "（梢）", "（吟子）", "（慈）" }, Assert.Single(N3FontComposer.FindMissingRuns(three, fonts, fonts)));

        var noFont = TextEditModeFormat.ParseLyricLine("（梢）（綴理）あ");
        Assert.Empty(N3FontComposer.FindMissingRuns(noFont, new[] { "（梢）", "（綴理）" }, fonts));

        var twice = TextEditModeFormat.ParseLyricLine("（梢）（梢）あ");
        Assert.Empty(N3FontComposer.FindMissingRuns(twice, fonts, fonts));
    }

    // ------------------------------------------------------------ 置き場所

    [Fact]
    public void 置き場所_同じキャラを含む組み合わせフォントのフォルダ_無ければ最初の元フォントの親()
    {
        var kozue = Chara1("（梢）", "68BE8D");
        var ginko = Chara1("（吟子）", "A2D7DD");
        var megu = Chara1("（慈）", "C8C2C6");
        var handmade = Chara1("（梢）（慈）", "000000"); // NKM3 で手作りした 2 人用（組み合わせの情報なし）
        var fonts = new List<N3FontSet> { kozue, ginko, megu, handmade };
        var grade = N3FontTreeNode.ForFolder("102期生");
        grade.Children.Add(N3FontTreeNode.ForFont(kozue.Id));
        grade.Children.Add(N3FontTreeNode.ForFont(megu.Id));
        var pair = N3FontTreeNode.ForFolder("２人組");
        pair.Children.Add(N3FontTreeNode.ForFont(handmade.Id));
        var roots = new List<N3FontTreeNode> { grade, pair, N3FontTreeNode.ForFont(ginko.Id) };

        var f = N3FontComposer.Create(new[] { kozue, ginko }, Chara, N3Brush.TypeMilleFeuille, 10);
        fonts.Add(f);
        N3FontComposer.PlaceInTree(roots, fonts, f, new[] { kozue, ginko });
        Assert.Equal(f.Id, pair.Children[^1].FontId);

        // 関係する組み合わせが無ければ、最初の元フォントと同じ親
        var g = N3FontComposer.Create(new[] { ginko, kozue }, Chara, N3Brush.TypeMilleFeuille, 10);
        var fonts2 = new List<N3FontSet> { kozue, ginko, megu, g };
        var roots2 = new List<N3FontTreeNode> { N3FontTreeNode.ForFont(ginko.Id), grade.Clone() };
        N3FontComposer.PlaceInTree(roots2, fonts2, g, new[] { ginko, kozue });
        Assert.Equal(g.Id, roots2[^1].FontId); // （吟子）は最上位なので最上位の末尾
    }

    [Fact]
    public void 置き場所_手作りの組み合わせの名前を見分ける()
    {
        var singles = new HashSet<string> { "（梢）", "（吟子）", "（慈）" };
        Assert.True(N3FontComposer.IsCombinationName("（梢）（吟子）", singles));
        Assert.True(N3FontComposer.IsCombinationName("（梢）（吟子）（慈）", singles));
        Assert.False(N3FontComposer.IsCombinationName("（梢）", singles));
        Assert.False(N3FontComposer.IsCombinationName("（梢）ソロ", singles));
    }

    // ------------------------------------------------------------ 保存

    [Fact]
    public void 保存_組み合わせの情報は複製で別のものになる()
    {
        var f = N3FontComposer.Create(new[] { Chara1("（A）", "111111"), Chara1("（B）", "222222") }, Chara, N3Brush.TypeGradient, 20);
        var c = f.Clone();
        c.Composition!.SourceIds.Add("x");
        c.Composition.Linked = false;
        Assert.Equal(2, f.Composition!.SourceIds.Count);
        Assert.True(f.Composition.Linked);
        Assert.Equal(N3Brush.TypeGradient, c.Composition.BrushType);
        Assert.Equal(20, c.Composition.EndWidenPercent);
    }
}
