using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Tests;

public class TextEditModeFormatTests
{
    [Fact]
    public void チェック数付きタグの解析()
    {
        var line = TextEditModeFormat.ParseLyricLine("[2|00:12:34]漢[1|00:13:00]字");
        Assert.Equal(2, line.Chars.Count);
        Assert.Equal("漢", line.Chars[0].Text);
        Assert.Equal(2, line.Chars[0].CheckCount);
        Assert.Equal(12 * 100 + 34, line.Chars[0].TimeCs); // [00:12:34] = 12秒34
        Assert.Equal(1, line.Chars[1].CheckCount);
    }

    [Fact]
    public void チェック数のみの解析()
    {
        var line = TextEditModeFormat.ParseLyricLine("[3]漢字");
        Assert.Equal(3, line.Chars[0].CheckCount);
        Assert.Null(line.Chars[0].TimeCs);
        Assert.Equal(0, line.Chars[1].CheckCount);
    }

    [Fact]
    public void ルビブロックの解析_複数親文字()
    {
        var line = TextEditModeFormat.ParseLyricLine("{漢字|[1|00:01:00]かん＋[1|00:01:50]じ}");
        Assert.Equal(2, line.Chars.Count);
        Assert.Equal("漢", line.Chars[0].Text);
        Assert.Equal("かん", line.Chars[0].Ruby);
        Assert.Equal(100, line.Chars[0].TimeCs);
        Assert.True(line.Chars[0].RubyJoinsNext);
        Assert.Equal("字", line.Chars[1].Text);
        Assert.Equal("じ", line.Chars[1].Ruby);
        Assert.Equal(150, line.Chars[1].TimeCs);
        Assert.False(line.Chars[1].RubyJoinsNext);
    }

    [Fact]
    public void ルビブロックの解析_補助タイムタグ()
    {
        var line = TextEditModeFormat.ParseLyricLine("{陽|[2|00:01:00]よ[00:01:20]う}");
        Assert.Single(line.Chars);
        var c = line.Chars[0];
        Assert.Equal("陽", c.Text);
        Assert.Equal(100, c.TimeCs);
        Assert.Equal(2, c.CheckCount);
        Assert.Equal("よう", c.Ruby);
        Assert.Equal([120], c.AuxTimeTagsCs);
    }

    [Fact]
    public void 連続タグはスペーサーになる()
    {
        var line = TextEditModeFormat.ParseLyricLine("[1|00:01:00]あ[1|00:02:00][1|00:03:00]い");
        Assert.Equal(3, line.Chars.Count);
        Assert.True(line.Chars[1].IsSpacer);
        Assert.Equal(200, line.Chars[1].TimeCs);
        Assert.Equal(300, line.Chars[2].TimeCs);
    }

    [Fact]
    public void エスケープの解析()
    {
        var line = TextEditModeFormat.ParseLyricLine("あ$2759い$0024う");
        Assert.Equal("あ|い$う", line.GetDisplayText());
    }

    [Fact]
    public void サロゲートペアエスケープの解析()
    {
        var line = TextEditModeFormat.ParseLyricLine("$D842$DFB7野家");
        Assert.Equal(3, line.Chars.Count);
        Assert.Equal("𠮷", line.Chars[0].Text);
    }

    [Fact]
    public void 行ラウンドトリップ_チェックとルビ()
    {
        string src = "[2|00:12:34]歌{漢字|[1|00:13:00]かん＋[1|00:13:50]じ}う[00:14:00]";
        var line = TextEditModeFormat.ParseLyricLine(src);
        string written = TextEditModeFormat.WriteLyricLine(line);
        Assert.Equal(src, written);
    }

    [Fact]
    public void 行ラウンドトリップ_2連タグ()
    {
        string src = "[1|00:01:00]あ[1|00:02:00][1|00:03:00]い[00:04:00]";
        var line = TextEditModeFormat.ParseLyricLine(src);
        string written = TextEditModeFormat.WriteLyricLine(line);
        Assert.Equal(src, written);
    }

    [Fact]
    public void 全文ラウンドトリップ()
    {
        string src = string.Join("\r\n",
            "@Title=テスト曲",
            "@Artist=歌手",
            "[2|00:12:34]歌{漢字|[1|00:13:00]かん＋[1|00:13:50]じ}詞[00:14:00]",
            "",
            "[1|00:20:00]次のページ[00:22:00]",
            "");
        var doc = TextEditModeFormat.Parse(src);
        string written = TextEditModeFormat.Write(doc);
        Assert.Equal(src, written);
    }

    [Fact]
    public void lrcとの相互変換()
    {
        // テキスト編集モード → lrc（チェック数・ルビ詳細は落ちるがタグと文字は保持）
        var doc = TextEditModeFormat.Parse("[2|00:12:34]歌{漢字|[1|00:13:00]かん＋[1|00:13:50]じ}");
        string lrc = LrcFormat.Write(doc);
        Assert.Contains("[00:12:34]歌[00:13:00]漢[00:13:50]字", lrc);
        Assert.Contains("@Ruby1=漢字,かんじ", lrc);
    }

    [Fact]
    public void チェック数を出力しないオプション()
    {
        var line = TextEditModeFormat.ParseLyricLine("[2|00:12:34]歌");
        string written = TextEditModeFormat.WriteLyricLine(line, new TextEditWriteOptions { IncludeChecks = false });
        Assert.Equal("[00:12:34]歌", written);
    }
}
