using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Tests;

public class LineOperationsTests
{
    private static LyricsDocument Doc(params string[] lines)
    {
        var doc = new LyricsDocument();
        foreach (string l in lines) doc.Lines.Add(TextEditModeFormat.ParseLyricLine(l));
        return doc;
    }

    [Fact]
    public void 行分割_タグとチェックとルビが追従する()
    {
        var doc = Doc("[2|00:01:00]歌{漢字|[1|00:02:00]かん＋[1|00:02:50]じ}詞[00:03:00]");
        // 「歌」の後（charIndex=1）で分割
        LineOperations.SplitLine(doc, 0, 1);

        Assert.Equal(2, doc.Lines.Count);
        // 前半: 歌 + 補完された行末タグ（後半の先頭タグ 00:02:00）
        Assert.Equal("[2|00:01:00]歌[00:02:00]", TextEditModeFormat.WriteLyricLine(doc.Lines[0]));
        // 後半: 漢字（ルビ・チェック数維持）+ 元の行末タグ
        Assert.Equal("{漢字|[1|00:02:00]かん＋[1|00:02:50]じ}詞[00:03:00]", TextEditModeFormat.WriteLyricLine(doc.Lines[1]));
    }

    [Fact]
    public void 行分割_ルビ連結の切り離し()
    {
        var doc = Doc("{漢字|[1|00:01:00]かん＋[1|00:02:00]じ}[00:03:00]");
        // 漢 と 字 の間で分割
        LineOperations.SplitLine(doc, 0, 1);

        Assert.False(doc.Lines[0].Chars[^1].RubyJoinsNext);
        Assert.Equal("かん", doc.Lines[0].Chars[0].Ruby);
        Assert.Equal("じ", doc.Lines[1].Chars[0].Ruby);
    }

    [Fact]
    public void 行結合_行末タグが2連タグとして残る()
    {
        var doc = Doc("[1|00:01:00]あい[00:02:00]", "[1|00:03:00]うえ[00:04:00]");
        LineOperations.JoinWithNextLine(doc, 0);

        Assert.Single(doc.Lines);
        // 00:02:00 は次行先頭 00:03:00 と異なるため 2連タグ（スペーサー）で残る
        Assert.Equal("[1|00:01:00]あい[00:02:00][1|00:03:00]うえ[00:04:00]", TextEditModeFormat.WriteLyricLine(doc.Lines[0]));
    }

    [Fact]
    public void 行結合_スペースを挟むオプション()
    {
        var doc = Doc("[1|00:01:00]あい[00:02:00]", "[1|00:03:00]うえ[00:04:00]");
        LineOperations.JoinWithNextLine(doc, 0, insertSpace: true);

        Assert.Single(doc.Lines);
        Assert.Equal("[1|00:01:00]あい[00:02:00] [1|00:03:00]うえ[00:04:00]", TextEditModeFormat.WriteLyricLine(doc.Lines[0]));
    }

    [Fact]
    public void 行結合_同時刻なら行末タグは捨てる()
    {
        var doc = Doc("[1|00:01:00]あい[00:03:00]", "[1|00:03:00]うえ[00:04:00]");
        LineOperations.JoinWithNextLine(doc, 0);
        Assert.Equal("[1|00:01:00]あい[1|00:03:00]うえ[00:04:00]", TextEditModeFormat.WriteLyricLine(doc.Lines[0]));
    }

    [Fact]
    public void 分割して結合すると元に戻る()
    {
        string src = "[2|00:01:00]歌詞のテスト[00:03:00]";
        var doc = Doc(src);
        LineOperations.SplitLine(doc, 0, 3);
        LineOperations.JoinWithNextLine(doc, 0);
        Assert.Equal(src, TextEditModeFormat.WriteLyricLine(doc.Lines[0]));
    }

    [Fact]
    public void 空行の挿入と削除()
    {
        var doc = Doc("[00:01:00]あ", "[00:02:00]い");
        LineOperations.InsertEmptyLine(doc, 1);
        Assert.Equal(3, doc.Lines.Count);
        Assert.True(doc.Lines[1].IsEmpty);

        LineOperations.DeleteLine(doc, 1);
        Assert.Equal(2, doc.Lines.Count);
    }

    [Fact]
    public void 選択行の抽出()
    {
        var doc = LrcFormat.Parse(string.Join("\r\n",
            "@Title=曲名",
            "@Emoji=★,star.png",
            "[00:01:00]1行目[00:02:00]",
            "[00:03:00]2行目[00:04:00]",
            "[00:05:00]3行目[00:06:00]"));

        var extracted = LineOperations.ExtractLines(doc, new[] { 2, 0 });
        Assert.Equal(2, extracted.Lines.Count);
        Assert.Equal("1行目", extracted.Lines[0].GetDisplayText()); // 順序は元の並び
        Assert.Equal("3行目", extracted.Lines[1].GetDisplayText());
        Assert.Equal("曲名", extracted.GetTag("Title"));
        Assert.Single(extracted.EmojiEntries);

        // 元のドキュメントは変更されない
        Assert.Equal(3, doc.Lines.Count);
    }
}
