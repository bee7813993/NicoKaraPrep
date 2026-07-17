using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Tests;

public class LrcFormatTests
{
    [Fact]
    public void 行解析_行頭タグと行末タグ()
    {
        var line = LrcFormat.ParseLyricLine("[00:01:00]あい[00:02:00]う[00:03:00]");
        Assert.Equal(3, line.Chars.Count);
        Assert.Equal("あ", line.Chars[0].Text);
        Assert.Equal(100, line.Chars[0].TimeCs);
        Assert.Equal("い", line.Chars[1].Text);
        Assert.Null(line.Chars[1].TimeCs);
        Assert.Equal("う", line.Chars[2].Text);
        Assert.Equal(200, line.Chars[2].TimeCs);
        Assert.Equal(300, line.EndTimeCs);
    }

    [Fact]
    public void 行解析_2連タグはスペーサーになる()
    {
        var line = LrcFormat.ParseLyricLine("[00:01:00]あ[00:02:00][00:03:00]い");
        Assert.Equal(3, line.Chars.Count);
        Assert.True(line.Chars[1].IsSpacer);
        Assert.Equal(200, line.Chars[1].TimeCs);
        Assert.Equal("い", line.Chars[2].Text);
        Assert.Equal(300, line.Chars[2].TimeCs);
    }

    [Fact]
    public void 行解析_サロゲートペアは1文字扱い()
    {
        var line = LrcFormat.ParseLyricLine("[00:01:00]𠮷野家");
        Assert.Equal(3, line.Chars.Count);
        Assert.Equal("𠮷", line.Chars[0].Text);
    }

    [Fact]
    public void 行書き出し_ラウンドトリップ()
    {
        string src = "[00:01:00]あい[00:02:00][00:03:00]う[00:04:00]";
        var line = LrcFormat.ParseLyricLine(src);
        Assert.Equal(src, LrcFormat.WriteLyricLine(line));
    }

    [Fact]
    public void ドキュメント解析_メタデータと絵文字とルビ()
    {
        string src = string.Join("\r\n",
            "@Title=テスト曲",
            "@Artist=歌手",
            "@Emoji=★,star1.png,star2.png",
            "@Ruby1=漢字,かんじ",
            "",
            "[00:01:00]漢[00:01:50]字[00:02:00]の歌[00:03:00]",
            "");
        var doc = LrcFormat.Parse(src);

        Assert.Equal("テスト曲", doc.GetTag("Title"));
        Assert.Equal("歌手", doc.GetTag("Artist"));
        Assert.Single(doc.EmojiEntries);
        Assert.Equal("★", doc.EmojiEntries[0].ReplaceChar);
        Assert.Equal("star2.png", doc.EmojiEntries[0].ImageAfter);

        Assert.Equal(2, doc.Lines.Count);
        Assert.True(doc.Lines[0].IsEmpty);

        var chars = doc.Lines[1].Chars;
        Assert.Equal("かんじ", chars[0].Ruby);
        Assert.True(chars[0].RubyJoinsNext);
        Assert.Equal("", chars[1].Ruby);
        Assert.False(chars[1].RubyJoinsNext);
        Assert.Null(chars[2].Ruby);
        Assert.Empty(doc.UnappliedRubyEntries);
    }

    [Fact]
    public void ルビ_時刻による読み分け()
    {
        string src = string.Join("\r\n",
            "@Ruby1=風,かぜ",
            "@Ruby2=風,ふう,[00:10:00]",
            "[00:01:00]風が吹く[00:02:00]",
            "[00:10:00]風のように[00:11:00]");
        var doc = LrcFormat.Parse(src);

        Assert.Equal("かぜ", doc.Lines[0].Chars[0].Ruby);
        Assert.Equal("ふう", doc.Lines[1].Chars[0].Ruby);
    }

    [Fact]
    public void ルビ_未適用エントリは保持される()
    {
        string src = string.Join("\r\n",
            "@Ruby1=存在しない,そんざいしない",
            "[00:01:00]歌詞[00:02:00]");
        var doc = LrcFormat.Parse(src);
        Assert.Single(doc.UnappliedRubyEntries);

        string written = LrcFormat.Write(doc);
        Assert.Contains("@Ruby1=存在しない,そんざいしない", written);
    }

    [Fact]
    public void ドキュメント_ラウンドトリップ()
    {
        string src = string.Join("\r\n",
            "@Title=テスト曲",
            "@Offset=100",
            "@Emoji=★,star1.png,star2.png",
            "@Ruby1=漢字,かんじ",
            "[00:01:00]漢[00:01:50]字[00:02:00]の歌[00:03:00]",
            "",
            "[00:10:00]★が光る[00:12:00]",
            "") ;
        var doc = LrcFormat.Parse(src);
        string written = LrcFormat.Write(doc);
        var doc2 = LrcFormat.Parse(written);
        string written2 = LrcFormat.Write(doc2);

        Assert.Equal(written, written2);
        Assert.Contains("@Title=テスト曲", written);
        Assert.Contains("@Emoji=★,star1.png,star2.png", written);
        Assert.Contains("@Ruby1=漢字,かんじ", written);
        Assert.Contains("[00:01:00]漢[00:01:50]字[00:02:00]の歌[00:03:00]", written);
    }

    [Fact]
    public void ルビ生成_同一親文字で読みが違えば適用区間で区切られる()
    {
        var doc = new LyricsDocument();
        var line1 = LrcFormat.ParseLyricLine("[00:01:00]風[00:02:00]");
        line1.Chars[0].Ruby = "かぜ";
        var line2 = LrcFormat.ParseLyricLine("[00:10:00]風[00:11:00]");
        line2.Chars[0].Ruby = "ふう";
        var line3 = LrcFormat.ParseLyricLine("[00:20:00]風[00:21:00]");
        line3.Chars[0].Ruby = "かぜ";
        doc.Lines.Add(line1);
        doc.Lines.Add(line2);
        doc.Lines.Add(line3);

        var entries = LrcFormat.BuildRubyEntries(doc);
        Assert.Equal(3, entries.Count);

        // 区間が連続して閉じている（最初は開始なし、最後は終了なし）
        Assert.Equal(("かぜ", (int?)null, (int?)1000), (entries[0].Ruby, entries[0].StartCs, entries[0].EndCs));
        Assert.Equal(("ふう", (int?)1000, (int?)2000), (entries[1].Ruby, entries[1].StartCs, entries[1].EndCs));
        Assert.Equal(("かぜ", (int?)2000, (int?)null), (entries[2].Ruby, entries[2].StartCs, entries[2].EndCs));

        // タグ表記: 開始なしは「,,終了」形式
        string written = LrcFormat.Write(doc);
        Assert.Contains("@Ruby1=風,かぜ,,[00:10:00]", written);
        Assert.Contains("@Ruby2=風,ふう,[00:10:00],[00:20:00]", written);
        Assert.Contains("@Ruby3=風,かぜ,[00:20:00]", written);
    }

    [Fact]
    public void 絵文字_デフォルトは全件出力()
    {
        var doc = new LyricsDocument();
        doc.EmojiEntries.Add(new EmojiEntry { ReplaceChar = "★", ImageBefore = "a.png" });
        doc.EmojiEntries.Add(new EmojiEntry { ReplaceChar = "♪", ImageBefore = "b.png" });
        doc.Lines.Add(LrcFormat.ParseLyricLine("[00:01:00]★だけ使う[00:02:00]"));

        string written = LrcFormat.Write(doc);
        Assert.Contains("@Emoji=★,a.png", written);
        Assert.Contains("@Emoji=♪,b.png", written);
    }

    [Fact]
    public void 絵文字_使用中のみ出力は複数文字の置き換え文字列でも判定できる()
    {
        var doc = new LyricsDocument();
        doc.EmojiEntries.Add(new EmojiEntry { ReplaceChar = "(歩夢)", ImageBefore = "a.png" });
        doc.EmojiEntries.Add(new EmojiEntry { ReplaceChar = "(愛)", ImageBefore = "b.png" });
        doc.Lines.Add(LrcFormat.ParseLyricLine("[00:01:00](歩夢)だけ使う[00:02:00]"));

        string written = LrcFormat.Write(doc, new LrcWriteOptions { EmitOnlyUsedEmoji = true });
        Assert.Contains("@Emoji=(歩夢),a.png", written);
        Assert.DoesNotContain("@Emoji=(愛),b.png", written);
    }

    [Fact]
    public void 絵文字_実効リストの上書き出力()
    {
        var doc = new LyricsDocument();
        doc.Lines.Add(LrcFormat.ParseLyricLine("[00:01:00]あ[00:02:00]"));

        var effective = new List<EmojiEntry>
        {
            new() { ReplaceChar = "(歩夢)", ImageBefore = "a.png", Options = "NoDecor,Zoom=150" },
        };
        string written = LrcFormat.Write(doc, new LrcWriteOptions { EmojiEntriesOverride = effective });
        Assert.Contains("@Emoji=(歩夢),a.png,,NoDecor,Zoom=150", written);
    }

    [Fact]
    public void 絵文字_保存先フォルダ配下の画像は相対パスで出力()
    {
        var doc = new LyricsDocument();
        doc.EmojiEntries.Add(new EmojiEntry { ReplaceChar = "(歩夢)", ImageBefore = @"C:\songs\icons\歩夢.png" });
        doc.EmojiEntries.Add(new EmojiEntry { ReplaceChar = "(愛)", ImageBefore = @"D:\other\愛.png" });
        doc.EmojiEntries.Add(new EmojiEntry { ReplaceChar = "(玲)", ImageBefore = @"C:\songs\玲.png", ImageAfter = @"C:\songs\icons\玲後.png" });
        doc.Lines.Add(LrcFormat.ParseLyricLine("[00:01:00]あ[00:02:00]"));

        string written = LrcFormat.Write(doc, new LrcWriteOptions { BaseFolder = @"C:\songs" });
        Assert.Contains(@"@Emoji=(歩夢),icons\歩夢.png", written);   // 配下 → 相対
        Assert.Contains(@"@Emoji=(愛),D:\other\愛.png", written);    // 配下でない → 絶対のまま
        Assert.Contains(@"@Emoji=(玲),玲.png,icons\玲後.png", written); // 同一フォルダ → ファイル名のみ
    }
}
