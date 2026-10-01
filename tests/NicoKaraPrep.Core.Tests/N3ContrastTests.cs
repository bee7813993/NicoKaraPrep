using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Tests;

public class N3ContrastTests
{
    private static N3Brush Solid(string color, int alpha = 100) => new() { Type = N3Brush.TypeSolid, Color = color, AlphaPercent = alpha };

    /// <summary>メイン色の反転のキャラ用フォント（メイン色 → [1][4][7]、白 → [0][3][5]）。</summary>
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

    private static N3Brush Mille(params string[] colors) => new()
    {
        Type = N3Brush.TypeMilleFeuille,
        Stops = colors.Select((c, i) => new N3GradientStop { Position = i == colors.Length - 1 ? 1 : (double)i / (colors.Length - 1), Color = c }).ToList(),
    };

    [Fact]
    public void 判定_多色はマーカーごとに比べる_見づらいマーカーの番号も分かる()
    {
        var f = Chara("000000");
        f.Detail.Brushes[4] = Mille("AE62FF", "FFFF00", "FFFFFF"); // 最後のマーカーは塗りに使われない
        var issue = Assert.Single(N3Contrast.Check(f), i => i.TextSlot == 4);
        Assert.Equal("FFFF00", issue.TextColor);
        Assert.Equal((1, -1), (issue.TextStop, issue.EdgeStop));
        Assert.False(issue.BothSolid);
        Assert.Empty(N3Contrast.Suggest(issue)); // 塗りが分からなければ、多色の候補は作れない
    }

    [Fact]
    public void 候補_文字が多色なら見づらいマーカーの色を暗く_縁はどの色とも比が足りる色()
    {
        var f = Chara("000000");
        f.Detail.Brushes[4] = Mille("AE62FF", "FFFF00", "808080");
        var issue = N3Contrast.Check(f).Single(i => i.TextSlot == 4);
        var s = N3Contrast.Suggest(issue, f.Detail);

        var stop = s.First(x => x.Kind == N3ContrastSuggestionKind.StopLightness);
        Assert.Equal((4, 1, "FFFF00"), (stop.Slot, stop.Stop, stop.OriginalColor));
        Assert.True(N3Contrast.Ratio(stop.Color, "FFFFFF") >= N3Contrast.TargetRatio, stop.Color);
        Assert.True(N3Contrast.Luminance(stop.Color) < N3Contrast.Luminance("FFFF00"));

        Assert.Equal("000000", s.Single(x => x.Kind == N3ContrastSuggestionKind.EdgeBlackOrWhite).Color);
        Assert.All(s.Where(x => x.Slot == 5), x =>
            Assert.True(N3Contrast.Ratio(x.Color, "AE62FF") >= N3Contrast.TargetRatio && N3Contrast.Ratio(x.Color, "FFFF00") >= N3Contrast.TargetRatio, x.Color));
    }

    [Fact]
    public void 候補_縁が多色なら見づらいマーカーの色を暗く_文字は黒か白()
    {
        // 手で作ったミルフィーユ（青と黄）のメイン色と、白のベース色。ワイプ後は白の文字に青と黄の縁
        var f = Chara("000000");
        var mille = Mille("5383C3", "FAD764", "808080");
        foreach (int slot in new[] { 1, 4, 7 }) f.Detail.Brushes[slot] = mille.Clone();
        var issues = N3Contrast.Check(f);
        Assert.Equal(new[] { (4, 5), (0, 1) }, issues.Select(i => (i.TextSlot, i.EdgeSlot)));

        var after = issues[1];
        Assert.Equal(("FFFFFF", "FAD764", -1, 1), (after.TextColor, after.EdgeColor, after.TextStop, after.EdgeStop));
        var s = N3Contrast.Suggest(after, f.Detail);
        var text = s.Single(x => x.Kind == N3ContrastSuggestionKind.TextBlackOrWhite);
        Assert.Equal((0, "000000"), (text.Slot, text.Color));
        var stop = s.Single(x => x.Kind == N3ContrastSuggestionKind.StopLightness);
        Assert.Equal((1, 1, "FAD764"), (stop.Slot, stop.Stop, stop.OriginalColor));
        Assert.True(N3Contrast.Ratio(stop.Color, "FFFFFF") >= N3Contrast.TargetRatio, stop.Color);
        Assert.DoesNotContain(s, x => x.Kind == N3ContrastSuggestionKind.TextLightness); // 白より明るくはできない
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
