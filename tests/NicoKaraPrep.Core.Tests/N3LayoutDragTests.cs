using NicoKaraPrep.Core.Formats;

namespace NicoKaraPrep.Core.Tests;

public class N3LayoutDragTests
{
    /// <summary>下寄せ・上の段が左寄せ・下の段が右寄せの 2 段（よくある形）。</summary>
    private static N3LayoutSettings Bottom2() =>
        new("下2行", 0, 2, LineSpacePx: 60, VerticalMarginPx: 50, HorizontalMarginPx: 50, new[] { 0, 2 }, 2, 0, 0, 0, 0);

    [Fact]
    public void 下寄せの一番下の段_上下余白と左右余白が変わる()
    {
        var s = N3LayoutDrag.Apply(Bottom2(), row: 1, rowsInPage: 2, dx: -20, dy: -30);
        Assert.Equal(80, s.VerticalMarginPx);   // 上へ 30 → 下余白 +30
        Assert.Equal(70, s.HorizontalMarginPx); // 右寄せの段を左へ 20 → 右の余白 +20
        Assert.Equal(60, s.LineSpacePx);
        Assert.True(N3LayoutDrag.IsAnchorRow(Bottom2(), 1, 2));
        Assert.Equal("下余白・左右余白", N3LayoutDrag.Targets(Bottom2(), 1, 2));
    }

    [Fact]
    public void 下寄せの上の段_行間が変わる()
    {
        var s = N3LayoutDrag.Apply(Bottom2(), row: 2, rowsInPage: 2, dx: 15, dy: -24);
        Assert.Equal(50, s.VerticalMarginPx);
        Assert.Equal(84, s.LineSpacePx);        // 上へ 24 → 行間 +24
        Assert.Equal(65, s.HorizontalMarginPx); // 左寄せの段を右へ 15 → 左の余白 +15
        Assert.Equal("行間・左右余白", N3LayoutDrag.Targets(Bottom2(), 2, 2));
    }

    [Fact]
    public void 下寄せの3段目_行間は動かした量を段の数で割る()
    {
        var s = N3LayoutDrag.Apply(Bottom2(), row: 3, rowsInPage: 3, dx: 0, dy: -30);
        Assert.Equal(75, s.LineSpacePx); // 3 段目は行間 2 つ分動く
    }

    [Fact]
    public void 上寄せ_一番上の段は上余白_下の段は行間()
    {
        var top = Bottom2() with { VerticalAlignment = 0 };
        var anchor = N3LayoutDrag.Apply(top, row: 2, rowsInPage: 2, dx: 0, dy: 10);
        Assert.Equal(60, anchor.VerticalMarginPx);
        var lower = N3LayoutDrag.Apply(top, row: 1, rowsInPage: 2, dx: 0, dy: 10);
        Assert.Equal(50, lower.VerticalMarginPx);
        Assert.Equal(70, lower.LineSpacePx);
        Assert.Equal("上余白・左右余白", N3LayoutDrag.Targets(top, 2, 2));
    }

    [Fact]
    public void 中央寄せ_行間だけが変わり_真ん中の段は動かない()
    {
        var center = Bottom2() with { VerticalAlignment = 1 };
        var bottomRow = N3LayoutDrag.Apply(center, row: 1, rowsInPage: 2, dx: 0, dy: 10);
        Assert.Equal(50, bottomRow.VerticalMarginPx);
        Assert.Equal(80, bottomRow.LineSpacePx); // 下の段を下へ 10 → 2 段が 10 ずつ離れる
        var middle = N3LayoutDrag.Apply(center, row: 2, rowsInPage: 3, dx: 0, dy: 10);
        Assert.Equal(60, middle.LineSpacePx);
        Assert.Equal("", N3LayoutDrag.Targets(center with { HorizontalAlignments = new[] { 1, 1, 1 } }, 2, 3));
    }

    [Fact]
    public void 中央の段は左右に動かない_余白は0未満にしない()
    {
        var centered = Bottom2() with { HorizontalAlignments = new[] { 1, 1 } };
        var s = N3LayoutDrag.Apply(centered, row: 1, rowsInPage: 2, dx: 100, dy: 200);
        Assert.Equal(50, s.HorizontalMarginPx);
        Assert.Equal(0, s.VerticalMarginPx);
        Assert.Equal("下余白", N3LayoutDrag.Targets(centered, 1, 2));
    }

    [Fact]
    public void 変えた値の説明()
    {
        var before = Bottom2();
        var after = before with { VerticalMarginPx = 80, LineSpacePx = 72 };
        Assert.Equal("下余白 50 → 80 px・行間 60 → 72 px", N3LayoutDrag.DescribeChange(before, after));
        Assert.Equal("", N3LayoutDrag.DescribeChange(before, before));
        Assert.Equal("下余白 50 px・左右余白 50 px・行間 60 px", N3LayoutDrag.Describe(before));
    }
}
