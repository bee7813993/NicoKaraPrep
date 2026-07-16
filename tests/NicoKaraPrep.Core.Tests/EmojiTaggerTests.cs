using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Tests;

public class EmojiTaggerTests
{
    private static readonly EmojiMatcher Emoji = new(["★", "♪"]);
    private static readonly EmojiTagSettings PerEmoji = new() { LeadCs = 200, PerEmoji = true };
    private static readonly EmojiTagSettings Block = new() { LeadCs = 200, PerEmoji = false };

    [Fact]
    public void 単独絵文字_直後の文字の時刻から先行タグ()
    {
        var line = LrcFormat.ParseLyricLine("[00:10:00]歌詞[00:12:00]");
        EmojiTagger.InsertEmoji(line, 0, "★", Emoji, PerEmoji);

        // [00:08:00]★[00:10:00]歌詞[00:12:00]
        Assert.Equal("[00:08:00]★[00:10:00]歌詞[00:12:00]", LrcFormat.WriteLyricLine(line));
        Assert.Equal(800, line.Chars[0].TimeCs);
    }

    [Fact]
    public void 連続絵文字_デフォルトは各絵文字にタグと2連タグ()
    {
        var line = LrcFormat.ParseLyricLine("[00:10:00]歌詞[00:12:00]");
        EmojiTagger.InsertEmoji(line, 0, "★", Emoji, PerEmoji);
        EmojiTagger.InsertEmoji(line, 1, "♪", Emoji, PerEmoji);

        // 各絵文字: 開始 T−2s、終了 T（間はスペーサーの [00:10:00]）
        Assert.Equal("[00:08:00]★[00:10:00][00:08:00]♪[00:10:00]歌詞[00:12:00]", LrcFormat.WriteLyricLine(line));
    }

    [Fact]
    public void 連続絵文字_ブロックモードは先頭のみ()
    {
        var line = LrcFormat.ParseLyricLine("[00:10:00]歌詞[00:12:00]");
        EmojiTagger.InsertEmoji(line, 0, "★", Emoji, Block);
        EmojiTagger.InsertEmoji(line, 1, "♪", Emoji, Block);

        Assert.Equal("[00:08:00]★♪[00:10:00]歌詞[00:12:00]", LrcFormat.WriteLyricLine(line));
    }

    [Fact]
    public void 再タグ付けは冪等()
    {
        var line = LrcFormat.ParseLyricLine("[00:10:00]歌詞[00:12:00]");
        EmojiTagger.InsertEmoji(line, 0, "★", Emoji, PerEmoji);
        EmojiTagger.InsertEmoji(line, 1, "♪", Emoji, PerEmoji);
        string once = LrcFormat.WriteLyricLine(line);

        EmojiTagger.RetagLine(line, Emoji, PerEmoji);
        EmojiTagger.RetagLine(line, Emoji, PerEmoji);
        Assert.Equal(once, LrcFormat.WriteLyricLine(line));
    }

    [Fact]
    public void 実文字の時刻変更後の再計算()
    {
        var line = LrcFormat.ParseLyricLine("[00:10:00]歌詞[00:12:00]");
        EmojiTagger.InsertEmoji(line, 0, "★", Emoji, PerEmoji);

        line.Chars.First(c => c.Text == "歌").TimeCs = 2000;
        EmojiTagger.RetagLine(line, Emoji, PerEmoji);
        Assert.Equal("[00:18:00]★[00:20:00]歌詞[00:12:00]", LrcFormat.WriteLyricLine(line));
    }

    [Fact]
    public void モード切替_各絵文字からブロックへ()
    {
        var line = LrcFormat.ParseLyricLine("[00:10:00]歌詞[00:12:00]");
        EmojiTagger.InsertEmoji(line, 0, "★", Emoji, PerEmoji);
        EmojiTagger.InsertEmoji(line, 1, "♪", Emoji, PerEmoji);

        EmojiTagger.RetagLine(line, Emoji, Block);
        Assert.Equal("[00:08:00]★♪[00:10:00]歌詞[00:12:00]", LrcFormat.WriteLyricLine(line));
    }

    [Fact]
    public void 基準タグが無い場合はタグ無し()
    {
        var line = LrcFormat.ParseLyricLine("タグなし歌詞");
        EmojiTagger.InsertEmoji(line, 0, "★", Emoji, PerEmoji);
        Assert.Null(line.Chars[0].TimeCs);
        Assert.True(EmojiTagger.HasUntaggableEmoji(line, Emoji));
    }

    [Fact]
    public void 行末タグのみの場合はそれを基準にする()
    {
        var line = LrcFormat.ParseLyricLine("歌詞[00:12:00]");
        EmojiTagger.InsertEmoji(line, line.Chars.Count, "★", Emoji, PerEmoji);
        Assert.Equal(1000, line.Chars[^1].TimeCs); // 12:00 − 2s
        Assert.False(EmojiTagger.HasUntaggableEmoji(line, Emoji));
    }

    [Fact]
    public void 先行時間が曲頭より前なら0にクランプ()
    {
        var line = LrcFormat.ParseLyricLine("[00:01:00]歌詞[00:12:00]");
        EmojiTagger.InsertEmoji(line, 0, "★", Emoji, PerEmoji);
        Assert.Equal(0, line.Chars[0].TimeCs);
    }
}

public class EmojiTaggerMultiCharTests
{
    private static readonly EmojiMatcher Matcher = new(["（花帆）", "（さやか）", "（Edel Note）"]);
    private static readonly EmojiTagSettings PerEmoji = new() { LeadCs = 200, PerEmoji = true };
    private static readonly EmojiTagSettings Block = new() { LeadCs = 200, PerEmoji = false };

    [Fact]
    public void 複数文字の置き換え文字列を挿入()
    {
        var line = LrcFormat.ParseLyricLine("[00:10:00]歌詞[00:12:00]");
        int inserted = EmojiTagger.InsertEmoji(line, 0, "（花帆）", Matcher, PerEmoji);

        Assert.Equal(4, inserted); // （花帆） = 4 CharUnit
        // 先頭ユニットにのみ先行タグが付き、終了は「歌」のタグ
        Assert.Equal("[00:08:00]（花帆）[00:10:00]歌詞[00:12:00]", LrcFormat.WriteLyricLine(line));
        Assert.Equal(800, line.Chars[0].TimeCs);
        Assert.Null(line.Chars[1].TimeCs);
        Assert.Null(line.Chars[2].TimeCs);
        Assert.Null(line.Chars[3].TimeCs);
    }

    [Fact]
    public void 複数文字絵文字の連続_各絵文字モード()
    {
        var line = LrcFormat.ParseLyricLine("[00:10:00]歌詞[00:12:00]");
        EmojiTagger.InsertEmoji(line, 0, "（花帆）", Matcher, PerEmoji);
        EmojiTagger.InsertEmoji(line, 4, "（さやか）", Matcher, PerEmoji);

        Assert.Equal(
            "[00:08:00]（花帆）[00:10:00][00:08:00]（さやか）[00:10:00]歌詞[00:12:00]",
            LrcFormat.WriteLyricLine(line));
    }

    [Fact]
    public void 複数文字絵文字の連続_ブロックモード()
    {
        var line = LrcFormat.ParseLyricLine("[00:10:00]歌詞[00:12:00]");
        EmojiTagger.InsertEmoji(line, 0, "（花帆）", Matcher, Block);
        EmojiTagger.InsertEmoji(line, 4, "（さやか）", Matcher, Block);

        Assert.Equal("[00:08:00]（花帆）（さやか）[00:10:00]歌詞[00:12:00]", LrcFormat.WriteLyricLine(line));
    }

    [Fact]
    public void 複数文字絵文字_再タグは冪等()
    {
        var line = LrcFormat.ParseLyricLine("[00:10:00]歌詞[00:12:00]");
        EmojiTagger.InsertEmoji(line, 0, "（花帆）", Matcher, PerEmoji);
        EmojiTagger.InsertEmoji(line, 4, "（さやか）", Matcher, PerEmoji);
        string once = LrcFormat.WriteLyricLine(line);

        EmojiTagger.RetagLine(line, Matcher, PerEmoji);
        EmojiTagger.RetagLine(line, Matcher, PerEmoji);
        Assert.Equal(once, LrcFormat.WriteLyricLine(line));
    }

    [Fact]
    public void 別々のブロックはそれぞれの直後の文字を基準にする()
    {
        // （花帆）→「あ」、（さやか）→「い」を基準にする
        var line = LrcFormat.ParseLyricLine("[00:10:00]あ[00:20:00]い[00:30:00]");
        EmojiTagger.InsertEmoji(line, 0, "（花帆）", Matcher, PerEmoji);
        // 「い」の直前（花帆4ユニット+あ の後）
        EmojiTagger.InsertEmoji(line, 5, "（さやか）", Matcher, PerEmoji);

        Assert.Equal(
            "[00:08:00]（花帆）[00:10:00]あ[00:18:00]（さやか）[00:20:00]い[00:30:00]",
            LrcFormat.WriteLyricLine(line));
    }

    [Fact]
    public void マッチャー_lrcラウンドトリップ後も出現を検出()
    {
        var line = LrcFormat.ParseLyricLine("[00:08:00]（花帆）[00:10:00]歌詞[00:12:00]");
        var occ = Matcher.FindOccurrences(line.Chars);
        Assert.Single(occ);
        Assert.Equal("（花帆）", occ[0].Value);
        Assert.Equal(0, occ[0].Start);
        Assert.Equal(4, occ[0].Length);
    }

    [Fact]
    public void 検証の除外判定_複数文字絵文字の先行タグは無視される()
    {
        var doc = new LyricsDocument();
        doc.Lines.Add(LrcFormat.ParseLyricLine("[00:05:00]あ[00:10:00]"));
        doc.Lines.Add(new LyricsLine());
        doc.Lines.Add(LrcFormat.ParseLyricLine("[00:09:00]（花帆）[00:30:00]い[00:35:00]"));

        var units = Matcher.CollectUnits(doc);
        Func<CharUnit, bool> exclude = units.Contains;

        Assert.Equal(3000, doc.Lines[2].GetFirstTimeCs(exclude)); // （花帆）の 09:00 は無視
    }
}
