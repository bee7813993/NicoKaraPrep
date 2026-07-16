using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Tests;

public class TimeTagTests
{
    [Theory]
    [InlineData(0, "[00:00:00]")]
    [InlineData(1, "[00:00:01]")]
    [InlineData(100, "[00:01:00]")]
    [InlineData(6000, "[01:00:00]")]
    [InlineData(12 * 6000 + 34 * 100 + 56, "[12:34:56]")]
    public void Format_基本(int cs, string expected)
    {
        Assert.Equal(expected, TimeTag.Format(cs));
    }

    [Theory]
    [InlineData("[00:00:00]", 0, 10)]
    [InlineData("[12:34:56]", 12 * 6000 + 34 * 100 + 56, 10)]
    [InlineData("[01:02]", 62 * 100, 7)]
    public void TryParseAt_基本(string s, int expectedCs, int expectedLen)
    {
        Assert.True(TimeTag.TryParseAt(s, 0, out int cs, out int len));
        Assert.Equal(expectedCs, cs);
        Assert.Equal(expectedLen, len);
    }

    [Theory]
    [InlineData("[aa:bb:cc]")]
    [InlineData("[00:60:00]")] // 秒が60以上
    [InlineData("[00:0]")]
    [InlineData("歌詞")]
    [InlineData("")]
    public void TryParseAt_不正な形式(string s)
    {
        Assert.False(TimeTag.TryParseAt(s, 0, out _, out _));
    }

    [Fact]
    public void ラウンドトリップ()
    {
        for (int cs = 0; cs < 60 * 6000; cs += 137)
        {
            string tag = TimeTag.Format(cs);
            Assert.True(TimeTag.TryParseAt(tag, 0, out int parsed, out _));
            Assert.Equal(cs, parsed);
        }
    }
}
