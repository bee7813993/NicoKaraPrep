using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Tests;

public class N3ColorPaletteTests
{
    private static IReadOnlyList<N3ColorPattern> Patterns => N3ColorPatterns.BuiltIns;

    private static N3Brush Solid(string color, int alpha = 100) => new() { Type = N3Brush.TypeSolid, Color = color, AlphaPercent = alpha };

    /// <summary>メイン色の反転のフォント（メイン色 → [1][4][7]、ベース色 → [0][3][5]）。</summary>
    private static N3FontSet Inverse(string name, string main, string baseColor = "FFFFFF")
    {
        var f = new N3FontSet { Name = name };
        string[] colors = { baseColor, main, "000000", baseColor, main, baseColor, "FFFFFF", main };
        for (int i = 0; i < 8; i++) f.Detail.Brushes[i] = Solid(colors[i]);
        return f;
    }

    private static string Text(N3PaletteGroup g) =>
        string.Join(" ", g.Colors.Select(c => $"{c.Color}{(c.AlphaPercent != 100 ? $"/{c.AlphaPercent}" : "")}[{string.Join(",", c.FontNames)}]"));

    [Fact]
    public void 役割ごとに集める_同じ色は1つ_一覧の順()
    {
        var fonts = new[]
        {
            Inverse("（麻衣）", "66c5ec"),
            Inverse("（玲）", "88D66E"),
            Inverse("（麻衣・別）", "66C5EC"),
            Inverse("（花火）", "FF2021", baseColor: "FFFFF0"),
        };
        var groups = N3ColorPalette.Collect(fonts, Patterns, new[] { "メイン色", "ベース色" });
        Assert.Equal(new[] { "メイン色", "ベース色" }, groups.Select(g => g.RoleName));
        Assert.Equal("66C5EC[（麻衣）,（麻衣・別）] 88D66E[（玲）] FF2021[（花火）]", Text(groups[0]));
        Assert.Equal("FFFFFF[（麻衣）,（玲）,（麻衣・別）] FFFFF0[（花火）]", Text(groups[1]));
    }

    [Fact]
    public void ミルフィーユとグラデーションと画像と未指定は入れない_不透明度が違えば別の色()
    {
        var mille = Inverse("（麻衣）（玲）", "66C5EC");
        var stops = new N3Brush
        {
            Type = N3Brush.TypeMilleFeuille,
            Stops = new List<N3GradientStop>
            {
                new() { Position = 0, Color = "66C5EC" },
                new() { Position = 0.5, Color = "88D66E" },
                new() { Position = 1, Color = "808080" },
            },
        };
        foreach (int s in new[] { 1, 4, 7 }) mille.Detail.Brushes[s] = stops.Clone();

        var unset = Inverse("（未指定）", "");
        var half = Inverse("（半透明）", "123456");
        foreach (int s in new[] { 1, 4, 7 }) half.Detail.Brushes[s] = Solid("123456", 50);

        var groups = N3ColorPalette.Collect(new[] { mille, unset, half, Inverse("（濃い）", "123456") }, Patterns, new[] { "メイン色" });
        Assert.Equal("123456/50[（半透明）] 123456[（濃い）]", Text(groups[0]));
    }

    [Fact]
    public void 個別のフォントは入れない_同じ名前の役割はほかのパターンでもまとめる()
    {
        var individual = Inverse("（個別）", "ABCDEF");
        individual.ColorPatternId = N3ColorPatterns.NoneId;

        // 自作のパターン（役割の名前に「メイン色」を使う。ワイプ前後で同じ色）
        var mine = new N3ColorPattern
        {
            Id = "user:1",
            Name = "自作",
            Roles = new List<string> { "メイン色", "縁" },
            Slots = new[] { 0, 1, -1, 0, 0, 1, -1, 0 },
        };
        var same = new N3FontSet { Name = "（情報）", ColorPatternId = "user:1" };
        string[] colors = { "FEDCBA", "000000", "000000", "FEDCBA", "FEDCBA", "000000", "FFFFFF", "FEDCBA" };
        for (int i = 0; i < 8; i++) same.Detail.Brushes[i] = Solid(colors[i]);

        var patterns = N3ColorPatterns.All(new[] { mine });
        var groups = N3ColorPalette.Collect(new[] { individual, same, Inverse("（麻衣）", "66C5EC") }, patterns, new[] { "メイン色", "ベース色", "メイン色", "" });
        Assert.Equal(2, groups.Count);
        Assert.Equal("FEDCBA[（情報）] 66C5EC[（麻衣）]", Text(groups[0]));
        Assert.Equal("FFFFFF[（麻衣）]", Text(groups[1]));
    }

    [Fact]
    public void 画像はどの箇所でも集める_同じパスは1つ_空のパスは除く()
    {
        var a = Inverse("（麻衣）", "66C5EC");
        a.Detail.Brushes[4] = new N3Brush { Type = N3Brush.TypeBitmap, BitmapPath = @"C:\img\star.png", BitmapScale = 150 };
        var b = Inverse("（玲）", "88D66E");
        b.Detail.Brushes[0] = new N3Brush { Type = N3Brush.TypeBitmap, BitmapPath = @"c:\IMG\Star.png ", BitmapScale = 100 };
        b.Detail.Brushes[3] = new N3Brush { Type = N3Brush.TypeBitmap, BitmapPath = "" };
        b.Detail.Brushes[5] = new N3Brush { Type = N3Brush.TypeBitmap, BitmapPath = @"C:\img\wave.png", BitmapScale = 100 };

        var images = N3ColorPalette.CollectImages(new[] { a, b });
        Assert.Equal(2, images.Count);
        Assert.Equal(@"C:\img\star.png", images[0].Path);
        Assert.Equal(150, images[0].Scale);
        Assert.Equal(new[] { "（麻衣）", "（玲）" }, images[0].FontNames);
        Assert.Equal(@"C:\img\wave.png", images[1].Path);
    }
}
