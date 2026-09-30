using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Tests;

public class N3ContrastTests
{
    private static N3Brush Solid(string color, int alpha = 100) => new() { Type = N3Brush.TypeSolid, Color = color, AlphaPercent = alpha };

    /// <summary>キャラ色の反転のキャラ用フォント（キャラ色 → [1][4][7]、白 → [0][3][5]）。</summary>
    private static N3FontSet Chara(string color)
    {
        var f = new N3FontSet { Name = "（X）" };
        f.Detail.Faces[0].EdgePx = 8;
        string[] colors = { "FFFFFF", color, "000000", "FFFFFF", color, "FFFFFF", "FFFFFF", color };
        for (int i = 0; i < 8; i++) f.Detail.Brushes[i] = Solid(colors[i]);
        return f;
    }

    [Fact]
    public void 明るさの比()
    {
        Assert.Equal(21, N3Contrast.Ratio("000000", "FFFFFF"), 3);
        Assert.Equal(1, N3Contrast.Ratio("FF0000", "ff0000"), 6);
        Assert.InRange(N3Contrast.Ratio("FFFF00", "FFFFFF"), 1.0, 1.1); // 黄色と白
        Assert.InRange(N3Contrast.Ratio("66C5EC", "FFFFFF"), 1.8, 2.1); // 水色と白（見づらいとはしない）
        Assert.True(double.IsNaN(N3Contrast.Ratio("", "FFFFFF")));
    }

    [Fact]
    public void 判定_黄色の文字と白の縁は見づらい_ワイプ前後の両方()
    {
        var issues = N3Contrast.Check(Chara("FFFF00"));
        Assert.Equal(new[] { (4, 5), (0, 1) }, issues.Select(i => (i.TextSlot, i.EdgeSlot)));
        Assert.All(issues, i => Assert.True(i.BothSolid));
        Assert.Equal("FFFF00", issues[0].TextColor);
        Assert.Equal("FFFFFF", issues[0].EdgeColor);
    }

    [Fact]
    public void 判定_いつもの色は見づらいとしない_透明_未指定も見ない()
    {
        Assert.Empty(N3Contrast.Check(Chara("AE62FF")));
        Assert.Empty(N3Contrast.Check(Chara("66C5EC")));

        var clear = Chara("FFFF00");
        clear.Detail.Brushes[4] = Solid("FFFF00", 0);
        clear.Detail.Brushes[1] = Solid("");
        Assert.Empty(N3Contrast.Check(clear));
    }

    [Fact]
    public void 判定_多色はマーカーごとに比べる_色の候補は出さない()
    {
        var f = Chara("000000");
        var mille = new N3Brush
        {
            Type = N3Brush.TypeMilleFeuille,
            Stops = new List<N3GradientStop>
            {
                new() { Position = 0, Color = "AE62FF" },
                new() { Position = 0.5, Color = "FFFF00" },
                new() { Position = 1, Color = "FFFFFF" }, // 最後のマーカーは塗りに使われない
            },
        };
        f.Detail.Brushes[4] = mille;
        var issue = Assert.Single(N3Contrast.Check(f), i => i.TextSlot == 4);
        Assert.Equal("FFFF00", issue.TextColor);
        Assert.False(issue.BothSolid);
        Assert.Empty(N3Contrast.Suggest(issue));
    }

    [Fact]
    public void 候補_文字を暗く_縁を文字の濃い色に_縁を黒に()
    {
        var issue = N3Contrast.Check(Chara("FFFF00")).Single(i => i.TextSlot == 4);
        var s = N3Contrast.Suggest(issue);
        Assert.Equal(
            new[] { N3ContrastSuggestionKind.TextLightness, N3ContrastSuggestionKind.EdgeShade, N3ContrastSuggestionKind.EdgeBlackOrWhite },
            s.Select(x => x.Kind));
        Assert.All(s, x => Assert.True(x.Ratio >= N3Contrast.TargetRatio, $"{x.Color} {x.Ratio}"));

        // 文字の候補は黄色の色合いのまま暗くしたもの（白との比が目標を少し超える程度）
        var text = s[0];
        Assert.Equal(4, text.Slot);
        N3FontSet.TryParseWeb16(text.Color, out byte r, out byte g, out byte b);
        Assert.Equal(r, g);
        Assert.True(b < 20 && r < 0xFF && r > 0x80, text.Color);
        Assert.InRange(text.Ratio, 2.5, 2.8);

        Assert.Equal(5, s[1].Slot); // 縁（白）を濃い黄色に
        Assert.Equal("000000", s[2].Color);
    }

    [Fact]
    public void 候補_暗い色どうしなら明るくする()
    {
        var f = Chara("000000");
        f.Detail.Brushes[4] = Solid("0D1040"); // 濃い紺の文字
        f.Detail.Brushes[5] = Solid("000000"); // 黒の縁
        var issue = N3Contrast.Check(f).Single(i => i.TextSlot == 4);
        var s = N3Contrast.Suggest(issue);
        Assert.True(N3Contrast.Luminance(s[0].Color) > N3Contrast.Luminance("0D1040")); // 文字を明るく
        Assert.Equal("FFFFFF", s.Single(x => x.Kind == N3ContrastSuggestionKind.EdgeBlackOrWhite).Color);
    }

    [Fact]
    public void 候補_灰色の文字には灰色の縁()
    {
        var f = Chara("000000");
        f.Detail.Brushes[4] = Solid("E0E0E0");
        var issue = N3Contrast.Check(f).Single(i => i.TextSlot == 4);
        var shade = N3Contrast.Suggest(issue).Single(x => x.Kind == N3ContrastSuggestionKind.EdgeShade);
        N3FontSet.TryParseWeb16(shade.Color, out byte r, out byte g, out byte b);
        Assert.True(r == g && g == b, shade.Color);
    }
}
