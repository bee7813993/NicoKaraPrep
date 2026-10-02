using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Tests;

public class N3RecentColorsTests
{
    [Fact]
    public void 入れる_新しい順_同じ色は先頭へ動かす_不透明度が違えば別の色()
    {
        var list = new List<string>();
        Assert.True(N3RecentColors.Add(list, "66c5ec", 100));
        Assert.True(N3RecentColors.Add(list, "#88D66E", 100));
        Assert.True(N3RecentColors.Add(list, "66C5EC", 50));
        Assert.Equal(new[] { "66C5EC@50", "88D66E", "66C5EC" }, list);

        Assert.True(N3RecentColors.Add(list, "66C5EC", 100));
        Assert.Equal(new[] { "66C5EC", "66C5EC@50", "88D66E" }, list);

        // 先頭と同じなら変わらない
        Assert.False(N3RecentColors.Add(list, "66c5ec", 100));
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public void 入れる_多ければ古いものから消す_読めないものは消す_読めない色は入れない()
    {
        var list = new List<string> { "壊れた", "123456@abc" };
        for (int i = 0; i < N3RecentColors.Max + 3; i++) N3RecentColors.Add(list, $"0000{i:X2}", 100);
        Assert.Equal(N3RecentColors.Max, list.Count);
        Assert.Equal($"0000{N3RecentColors.Max + 2:X2}", list[0]);
        Assert.DoesNotContain("壊れた", list);
        Assert.DoesNotContain("123456@abc", list);

        Assert.False(N3RecentColors.Add(list, "", 100));
        Assert.False(N3RecentColors.Add(list, "XYZ", 100));
    }

    [Fact]
    public void 読む_色と不透明度_読めないものは除く()
    {
        var colors = N3RecentColors.Colors(new[] { "66C5EC", "88d66e@40", "bad", "123456@101", "" });
        Assert.Equal(2, colors.Count);
        Assert.Equal(("66C5EC", 100), (colors[0].Color, colors[0].AlphaPercent));
        Assert.Equal(("88D66E", 40), (colors[1].Color, colors[1].AlphaPercent));
        Assert.Empty(colors[0].FontNames);
        Assert.Empty(N3RecentColors.Colors(null));
    }
}
