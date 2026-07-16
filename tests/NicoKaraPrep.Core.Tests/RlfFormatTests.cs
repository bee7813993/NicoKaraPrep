using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Tests;

public class RlfFormatTests
{
    private static LyricsDocument BuildSampleDoc()
    {
        var doc = new LyricsDocument();
        doc.Metadata.Add(new MetadataTag("Title", "テスト曲"));
        doc.Metadata.Add(new MetadataTag("Artist", "歌手"));
        doc.Metadata.Add(new MetadataTag("Offset", "100"));
        doc.EmojiEntries.Add(new EmojiEntry { ReplaceChar = "★", ImageBefore = "star.png" });

        var line1 = new LyricsLine();
        line1.Chars.Add(new CharUnit { Text = "漢", TimeCs = 100, CheckCount = 2, Ruby = "かん", RubyJoinsNext = true });
        line1.Chars.Add(new CharUnit { Text = "字", TimeCs = 150, CheckCount = 1, Ruby = "じ" });
        line1.Chars.Add(new CharUnit { Text = "の" });
        line1.Chars.Add(new CharUnit { Text = "歌", TimeCs = 200, CheckCount = 1 });
        line1.EndTimeCs = 300;
        doc.Lines.Add(line1);

        doc.Lines.Add(new LyricsLine()); // 空行（ページ区切り）

        var line3 = new LyricsLine();
        line3.Chars.Add(new CharUnit { Text = "𠮷", TimeCs = 1000, CheckCount = 1 }); // サロゲートペア
        line3.Chars.Add(new CharUnit { Text = CharUnit.Spacer, TimeCs = 1100 });      // 2連タグ
        line3.Chars.Add(new CharUnit { Text = "あ", TimeCs = 1200, CheckCount = 1 });
        var aux = new CharUnit { Text = "陽", TimeCs = 1300, CheckCount = 2, Ruby = "よう" };
        aux.AuxTimeTagsCs.Add(1350);
        line3.Chars.Add(aux);
        line3.EndTimeCs = 1400;
        doc.Lines.Add(line3);

        return doc;
    }

    [Fact]
    public void ラウンドトリップ_合成データ()
    {
        var doc = BuildSampleDoc();
        byte[] bytes = RlfFormat.Write(doc);
        var doc2 = RlfFormat.Read(bytes);

        Assert.Equal(doc.Lines.Count, doc2.Lines.Count);
        Assert.Equal("テスト曲", doc2.GetTag("Title"));
        Assert.Equal("歌手", doc2.GetTag("Artist"));
        Assert.Equal("100", doc2.GetTag("Offset"));
        Assert.Single(doc2.EmojiEntries);
        Assert.Equal("★", doc2.EmojiEntries[0].ReplaceChar);

        var l1 = doc2.Lines[0];
        Assert.Equal(4, l1.Chars.Count);
        Assert.Equal("漢", l1.Chars[0].Text);
        Assert.Equal(100, l1.Chars[0].TimeCs);
        Assert.Equal(2, l1.Chars[0].CheckCount);
        Assert.Equal("かん", l1.Chars[0].Ruby);
        Assert.True(l1.Chars[0].RubyJoinsNext);
        Assert.Equal("じ", l1.Chars[1].Ruby);
        Assert.False(l1.Chars[1].RubyJoinsNext);
        Assert.Null(l1.Chars[2].TimeCs);
        Assert.Equal(300, l1.EndTimeCs);

        Assert.True(doc2.Lines[1].IsEmpty);

        var l3 = doc2.Lines[2];
        Assert.Equal("𠮷", l3.Chars[0].Text);
        Assert.True(l3.Chars[1].IsSpacer);
        Assert.Equal(1100, l3.Chars[1].TimeCs);
        Assert.Equal([1350], l3.Chars[3].AuxTimeTagsCs);
        Assert.Equal(1400, l3.EndTimeCs);
    }

    [Fact]
    public void ラウンドトリップ_2回目で安定()
    {
        var doc = BuildSampleDoc();
        byte[] bytes1 = RlfFormat.Write(doc);
        byte[] bytes2 = RlfFormat.Write(RlfFormat.Read(bytes1));
        Assert.Equal(bytes1, bytes2);
    }

    [Fact]
    public void SilencemSecはint変数として書き出される()
    {
        // RhythmicaLyrics 内部で at_ss_inp2 は int 変数。
        // str で書くと vload が型不一致で失敗し、歌詞が表示されなくなる。
        var doc = new LyricsDocument();
        doc.Metadata.Add(new MetadataTag("SilencemSec", "1234"));
        doc.Lines.Add(LrcFormat.ParseLyricLine("[00:01:00]あ[00:02:00]"));

        byte[] bytes = RlfFormat.Write(doc);
        var vars = HspVsaveFile.Read(bytes).ToDictionary(v => v.Name);

        Assert.Equal(HspVarType.Int, vars["at_ss_inp2"].Type);
        Assert.Equal(1234, vars["at_ss_inp2"].IntValues![0]);
        Assert.Equal(HspVarType.Str, vars["at_ti_inp2"].Type);

        var doc2 = RlfFormat.Read(bytes);
        Assert.Equal("1234", doc2.GetTag("SilencemSec"));
    }

    [Fact]
    public void セル変換_SJISと非SJISとスペーサー()
    {
        Assert.Equal("あ", RlfFormat.DecodeCell(RlfFormat.EncodeCell("あ")));
        Assert.Equal("𠮷", RlfFormat.DecodeCell(RlfFormat.EncodeCell("𠮷")));
        Assert.Equal("♥", RlfFormat.DecodeCell(RlfFormat.EncodeCell("♥"))); // SJIS外のBMP文字
        Assert.Equal(CharUnit.Spacer, RlfFormat.DecodeCell(RlfFormat.EncodeCell(CharUnit.Spacer)));
        Assert.Equal("", RlfFormat.DecodeCell(RlfFormat.EncodeCell("")));
    }

    [Fact]
    public void 実ファイル読込_環境変数指定時のみ()
    {
        string? path = Environment.GetEnvironmentVariable("TTT_RLF_SAMPLE");
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return; // サンプル未指定ならスキップ

        var doc = RlfFormat.ReadFile(path);
        Assert.NotEmpty(doc.Lines);
        Assert.Contains(doc.Lines, l => !l.IsEmpty);

        // 読み→書き→再読みの安定性
        byte[] written = RlfFormat.Write(doc);
        var doc2 = RlfFormat.Read(written);
        Assert.Equal(doc.Lines.Count, doc2.Lines.Count);
        for (int i = 0; i < doc.Lines.Count; i++)
        {
            Assert.Equal(doc.Lines[i].GetDisplayText(), doc2.Lines[i].GetDisplayText());
            Assert.Equal(doc.Lines[i].EndTimeCs, doc2.Lines[i].EndTimeCs);
            Assert.Equal(doc.Lines[i].Chars.Count, doc2.Lines[i].Chars.Count);
            for (int j = 0; j < doc.Lines[i].Chars.Count; j++)
            {
                Assert.Equal(doc.Lines[i].Chars[j].TimeCs, doc2.Lines[i].Chars[j].TimeCs);
                Assert.Equal(doc.Lines[i].Chars[j].CheckCount, doc2.Lines[i].Chars[j].CheckCount);
                Assert.Equal(doc.Lines[i].Chars[j].Ruby, doc2.Lines[i].Chars[j].Ruby);
            }
        }
    }
}
