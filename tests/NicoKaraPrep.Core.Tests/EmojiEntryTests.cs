using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Tests;

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

    [Fact]
    public void 同じ定義_相対パスは歌詞のフォルダから見て比べる()
    {
        string folder = Path.Combine(Path.GetTempPath(), "nkp-emoji-test");
        var global = new EmojiEntry { ReplaceChar = "（花）", ImageBefore = Path.Combine(folder, "icons", "a.png"), Options = "Zoom=150%, Fix" };
        var fromLrc = EmojiEntry.ParseTagValue("（花）," + Path.Combine("icons", "a.png") + ",,Zoom=150%,Fix");
        Assert.True(fromLrc.SameDefinition(global, folder));
        Assert.False(fromLrc.SameDefinition(global, Path.Combine(folder, "other")));
    }

    [Fact]
    public void 同じ定義_文字列か画像かオプションが違えば別()
    {
        string a1 = Path.Combine(Path.GetTempPath(), "nkp-icons", "a.png");
        string b1 = Path.Combine(Path.GetTempPath(), "nkp-icons", "b.png");
        var a = new EmojiEntry { ReplaceChar = "★", ImageBefore = a1, ImageAfter = b1 };
        Assert.True(a.SameDefinition(new EmojiEntry { ReplaceChar = "★", ImageBefore = a1.ToUpperInvariant(), ImageAfter = b1, Slot = 3 }));
        Assert.False(a.SameDefinition(new EmojiEntry { ReplaceChar = "☆", ImageBefore = a1, ImageAfter = b1 }));
        Assert.False(a.SameDefinition(new EmojiEntry { ReplaceChar = "★", ImageBefore = a1 }));
        Assert.False(a.SameDefinition(new EmojiEntry { ReplaceChar = "★", ImageBefore = a1, ImageAfter = b1, Options = "Fix" }));
    }
}
