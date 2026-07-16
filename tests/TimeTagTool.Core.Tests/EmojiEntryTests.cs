using TimeTagTool.Core.Model;

namespace TimeTagTool.Core.Tests;

public class EmojiEntryTests
{
    [Fact]
    public void 解析_フル指定()
    {
        var e = EmojiEntry.ParseTagValue("★,before.png,after.png,Zoom=150%,Fix");
        Assert.Equal("★", e.ReplaceChar);
        Assert.Equal("before.png", e.ImageBefore);
        Assert.Equal("after.png", e.ImageAfter);
        Assert.Equal("Zoom=150%,Fix", e.Options);
    }

    [Fact]
    public void 解析_ワイプ後省略でオプションあり()
    {
        var e = EmojiEntry.ParseTagValue("★,before.png,Zoom=50%");
        Assert.Equal("before.png", e.ImageBefore);
        Assert.Null(e.ImageAfter);
        Assert.Equal("Zoom=50%", e.Options);
    }

    [Fact]
    public void 解析_画像1枚のみ()
    {
        var e = EmojiEntry.ParseTagValue("★,before.png");
        Assert.Equal("before.png", e.ImageBefore);
        Assert.Null(e.ImageAfter);
        Assert.Null(e.Options);
    }

    [Fact]
    public void ラウンドトリップ()
    {
        string[] values =
        [
            "★,before.png,after.png,Zoom=150%,Fix",
            "★,before.png,after.png",
            "★,before.png",
        ];
        foreach (string v in values)
        {
            var e = EmojiEntry.ParseTagValue(v);
            Assert.Equal(v, e.ToTagValue());
        }
    }
}
