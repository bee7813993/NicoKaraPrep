using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Tests;

public class PhraseTaggerTests
{
    private static LyricsDocument Doc(params string[] lines)
    {
        var doc = new LyricsDocument();
        foreach (string l in lines) doc.Lines.Add(LrcFormat.ParseLyricLine(l));
        return doc;
    }

    private static string Line(LyricsDocument doc, int i) => LrcFormat.WriteLyricLine(doc.Lines[i]);

    [Fact]
    public void 間奏の行_前の行の歌い終わりから次の行の歌い出しまで_秒数を入れる()
    {
        var doc = Doc("[00:10:00]歌[00:12:00]", "", "[00:26:00]詞[00:28:00]");
        var r = PhraseTagger.Insert(doc, 1, 0, "（間奏 約{秒}秒）");

        Assert.Equal("（間奏 約14秒）", r.Text);
        Assert.Equal(1200, r.StartCs);
        Assert.Equal(2600, r.EndCs);
        Assert.Equal("[00:12:00]（間奏 約14秒）[00:26:00]", Line(doc, 1));
        // 前後の行は変えない
        Assert.Equal("[00:10:00]歌[00:12:00]", Line(doc, 0));
        Assert.Equal("[00:26:00]詞[00:28:00]", Line(doc, 2));
    }

    [Fact]
    public void 後奏_最後の行の後ろ_歌い終わりから既定の秒数()
    {
        var doc = Doc("[00:10:00]歌[00:12:00]");
        var r = PhraseTagger.Insert(doc, 0, 1, "（後奏）");

        Assert.Equal("[00:10:00]歌[00:12:00]（後奏）[00:15:00]", Line(doc, 0));
        Assert.Equal(1200 + PhraseTagger.DefaultTailCs, r.EndCs);
    }

    [Fact]
    public void 行頭_前の行の歌い終わりからこの行の歌い出しまで()
    {
        var doc = Doc("[00:10:00]あ[00:12:00]", "[00:20:00]い[00:22:00]");
        PhraseTagger.Insert(doc, 1, 0, "（間奏）");

        Assert.Equal("[00:12:00]（間奏）[00:20:00]い[00:22:00]", Line(doc, 1));
    }

    [Fact]
    public void 曲の頭_前に歌が無ければ歌い出しの先行秒数前から()
    {
        var doc = Doc("[00:10:00]あ[00:12:00]");
        PhraseTagger.Insert(doc, 0, 0, "（前奏）", leadCs: 300);

        Assert.Equal("[00:07:00]（前奏）[00:10:00]あ[00:12:00]", Line(doc, 0));
    }

    [Fact]
    public void 二連タグの間_直前の終わりのタグは定型文の開始が代わりになる()
    {
        var doc = Doc("[00:10:00]あ[00:12:00][00:20:00]い[00:22:00]");
        PhraseTagger.Insert(doc, 0, 1, "（間奏）");

        Assert.Equal("[00:10:00]あ[00:12:00]（間奏）[00:20:00]い[00:22:00]", Line(doc, 0));
    }

    [Fact]
    public void 後ろの文字の開始と違うときは終わりのタグを2連タグで置く()
    {
        // 後ろの「い」はタグなし。定型文の終わり（次のタグ 00:20:00 = 「う」の開始）を 2連タグで置く
        var doc = Doc("[00:10:00]あい[00:20:00]う[00:22:00]");
        PhraseTagger.Insert(doc, 0, 1, "★");

        Assert.Equal("[00:10:00]あ[00:20:00]★[00:20:00]い[00:20:00]う[00:22:00]", Line(doc, 0));
    }

    [Fact]
    public void タグがどこにも無ければ文字だけ入れて秒数は不明()
    {
        var doc = Doc("あいう");
        var r = PhraseTagger.Insert(doc, 0, 3, "（後奏 {秒}秒）");

        Assert.Equal("（後奏 ?秒）", r.Text);
        Assert.Null(r.StartCs);
        Assert.Null(r.EndCs);
        Assert.Equal("あいう（後奏 ?秒）", Line(doc, 0));
    }
}
