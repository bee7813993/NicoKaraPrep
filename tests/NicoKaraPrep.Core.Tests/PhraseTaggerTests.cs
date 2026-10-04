using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Tests;

public class PhraseTaggerTests
{
    /// <summary>ワイプ前 1.5 秒・ワイプ後 0.5 秒・後奏 3 秒。</summary>
    private static readonly PhraseTiming Timing = new() { LeadCs = 150, TailCs = 50, AloneCs = 300 };

    private static LyricsDocument Doc(params string[] lines)
    {
        var doc = new LyricsDocument();
        foreach (string l in lines) doc.Lines.Add(LrcFormat.ParseLyricLine(l));
        return doc;
    }

    private static string Line(LyricsDocument doc, int i) => LrcFormat.WriteLyricLine(doc.Lines[i]);

    private static string[] All(LyricsDocument doc) => doc.Lines.Select(LrcFormat.WriteLyricLine).ToArray();

    [Fact]
    public void 間奏_空行に入れると定型文だけのページ_画面に出るのは前のページが消えてワイプ前のあと_次のページが出るワイプ後の前()
    {
        // 歌い終わり 00:12:00 → 前のページが消える 00:12:50 → 画面 00:14:00〜00:24:00（次のページが出る 00:24:50 の 0.5 秒前）
        // → タグ [00:15:50]〜[00:23:50]（ニコカラメーカー3 はタグの 1.5 秒前から 0.5 秒後まで表示する）
        var doc = Doc("[00:10:00]歌[00:12:00]", "", "[00:26:00]詞[00:28:00]");
        var r = PhraseTagger.Insert(doc, 1, 0, "（間奏 約{秒}秒）", Timing);

        Assert.Equal("（間奏 約14秒）", r.Text);
        Assert.Equal(new[] { "[00:10:00]歌[00:12:00]", "", "[00:15:50]（間奏 約14秒）[00:23:50]", "", "[00:26:00]詞[00:28:00]" }, All(doc));
        Assert.Equal(2, r.LineIndex);
        Assert.True(r.OwnLine);
        Assert.False(r.Squeezed);
    }

    [Fact]
    public void 後奏_最後の行の行末に入れると後ろに定型文だけのページ_ワイプは3秒()
    {
        var doc = Doc("[00:10:00]歌[00:12:00]");
        var r = PhraseTagger.Insert(doc, 0, 1, "（後奏）", Timing);

        Assert.Equal(new[] { "[00:10:00]歌[00:12:00]", "", "[00:15:50]（後奏）[00:18:50]" }, All(doc));
        Assert.Equal(2, r.LineIndex);
    }

    [Fact]
    public void 後奏_曲の終わりが分かれば曲の終わりまで表示()
    {
        // ユーザーの例: 最後のタグ 03:33.45・曲の長さ 03:56 → 画面に出るのは 03:35.45〜03:56.00（タグ [03:36:95]〜[03:55:50]）
        var doc = Doc("[03:30:00]歌[03:33:45]");
        PhraseTagger.Insert(doc, 0, 1, "（後奏）", new PhraseTiming { LeadCs = 150, TailCs = 50, SongEndCs = 23600 });

        Assert.Equal("[03:36:95]（後奏）[03:55:50]", Line(doc, 2));
    }

    [Fact]
    public void フォント設定_定型文だけの行は行に_行の途中は定型文の文字に指定する()
    {
        var doc = Doc("[00:10:00]あ[00:12:00]", "", "[00:26:00]い[00:28:00]");
        var r = PhraseTagger.Insert(doc, 1, 0, "（間奏）", Timing, "情報中");
        Assert.Equal("情報中", doc.Lines[r.LineIndex].FontSetName);
        Assert.All(doc.Lines[r.LineIndex].Chars, c => Assert.Null(c.FontSetName));
        Assert.Null(doc.Lines[0].FontSetName);

        var doc2 = Doc("[00:10:00]あ[00:12:00][00:20:00]い[00:22:00]");
        PhraseTagger.Insert(doc2, 0, 1, "（間奏）", Timing, "情報中");
        var line = doc2.Lines[0];
        Assert.Null(line.FontSetName);
        Assert.Equal(new[] { null, "情報中", "情報中", "情報中", "情報中", null }, line.Chars.Select(c => c.FontSetName).ToArray());

        // 空なら指定しない
        var doc3 = Doc("[00:10:00]あ[00:12:00]");
        var r3 = PhraseTagger.Insert(doc3, 0, 1, "（後奏）", Timing, " ");
        Assert.Null(doc3.Lines[r3.LineIndex].FontSetName);
    }

    [Fact]
    public void 前奏_最初の行の行頭に入れると前に定型文だけのページ_曲の頭から表示()
    {
        // 次のページが出る 00:08:50 の 0.5 秒前まで表示（タグは 00:07:50 まで）、曲の頭（00:00）から表示（タグは 00:01:50 から）
        var doc = Doc("[00:10:00]あ[00:12:00]");
        var r = PhraseTagger.Insert(doc, 0, 0, "（前奏）", Timing);

        Assert.Equal(new[] { "[00:01:50]（前奏）[00:07:50]", "", "[00:10:00]あ[00:12:00]" }, All(doc));
        Assert.Equal(0, r.LineIndex);
    }

    [Fact]
    public void 行頭_前の行とのあいだに定型文だけのページを作る()
    {
        var doc = Doc("[00:10:00]あ[00:12:00]", "[00:20:00]い[00:22:00]");
        PhraseTagger.Insert(doc, 1, 0, "（間奏）", Timing);

        Assert.Equal(new[] { "[00:10:00]あ[00:12:00]", "", "[00:15:50]（間奏）[00:17:50]", "", "[00:20:00]い[00:22:00]" }, All(doc));
    }

    [Fact]
    public void 前の行の表示終了の指定があればそれを前のページが消える時刻にする()
    {
        var doc = Doc("[00:10:00]あ[00:12:00]", "", "[00:30:00]い[00:32:00]");
        doc.Lines[0].ShowEndCs = 1400;
        PhraseTagger.Insert(doc, 1, 0, "（間奏）", Timing);

        Assert.Equal("[00:17:00]（間奏）[00:27:50]", Line(doc, 2));
    }

    [Fact]
    public void 間が短くて入らなければ前後の歌に合わせる()
    {
        var doc = Doc("[00:10:00]あ[00:12:00]", "", "[00:14:00]い[00:16:00]");
        var r = PhraseTagger.Insert(doc, 1, 0, "（間奏）", Timing);

        Assert.True(r.Squeezed);
        Assert.Equal("[00:12:00]（間奏）[00:14:00]", Line(doc, 2));
    }

    [Fact]
    public void ページ区切りにしない設定なら空行は足さない()
    {
        var doc = Doc("[00:10:00]あ[00:12:00]", "[00:20:00]い[00:22:00]");
        PhraseTagger.Insert(doc, 1, 0, "（間奏）", new PhraseTiming { LeadCs = 150, TailCs = 50, SeparatePages = false });

        Assert.Equal(new[] { "[00:10:00]あ[00:12:00]", "[00:15:50]（間奏）[00:17:50]", "[00:20:00]い[00:22:00]" }, All(doc));
    }

    [Fact]
    public void 行の途中_二連タグの間は直前の終わりのタグを定型文の開始が代わる()
    {
        var doc = Doc("[00:10:00]あ[00:12:00][00:20:00]い[00:22:00]");
        var r = PhraseTagger.Insert(doc, 0, 1, "（間奏）", Timing);

        Assert.False(r.OwnLine);
        Assert.Equal(1, r.DisplayOffset);
        Assert.Equal(new[] { "[00:10:00]あ[00:12:00]（間奏）[00:20:00]い[00:22:00]" }, All(doc));
    }

    [Fact]
    public void 行の途中_後ろの文字の開始と違うときは終わりのタグを2連タグで置く()
    {
        var doc = Doc("[00:10:00]あい[00:20:00]う[00:22:00]");
        PhraseTagger.Insert(doc, 0, 1, "★", Timing);

        Assert.Equal("[00:10:00]あ[00:20:00]★[00:20:00]い[00:20:00]う[00:22:00]", Line(doc, 0));
    }

    [Fact]
    public void タグがどこにも無ければ文字だけ入れて秒数は不明()
    {
        var doc = Doc("あいう");
        var r = PhraseTagger.Insert(doc, 0, 3, "（後奏 {秒}秒）", Timing);

        Assert.Equal("（後奏 ?秒）", r.Text);
        Assert.Null(r.StartCs);
        Assert.Null(r.EndCs);
        Assert.Equal(new[] { "あいう", "", "（後奏 ?秒）" }, All(doc));
    }

    [Fact]
    public void 探す_秒数の入った定型文も見つかる_重なりは前から()
    {
        var line = LrcFormat.ParseLyricLine("あ（間奏）い（間奏 約3秒）う");
        var occ = PhraseTagger.FindOccurrences(line, new[] { "（間奏）", "（間奏 約{秒}秒）", "" });

        Assert.Equal(2, occ.Count);
        Assert.Equal(("（間奏）", 1, 1), (occ[0].Value, occ[0].Start, occ[0].DisplayStart));
        Assert.Equal(("（間奏 約3秒）", 6), (occ[1].Value, occ[1].DisplayStart));
    }

    [Fact]
    public void 消す_定型文だけのページは足した空行ごと元に戻る()
    {
        string[] original = { "[00:10:00]歌[00:12:00]", "", "[00:26:00]詞[00:28:00]" };
        var doc = Doc(original);
        var r = PhraseTagger.Insert(doc, 1, 0, "（間奏 約{秒}秒）", Timing);
        var occ = Assert.Single(PhraseTagger.FindOccurrences(doc.Lines[r.LineIndex], new[] { "（間奏 約{秒}秒）" }));

        var caret = PhraseTagger.Delete(doc, r.LineIndex, occ);
        Assert.Equal(original, All(doc));
        Assert.Equal((1, 0), caret);
    }

    [Fact]
    public void 消す_前奏と後奏も足した空行ごと元に戻る()
    {
        var doc = Doc("[00:10:00]あ[00:12:00]");
        var r1 = PhraseTagger.Insert(doc, 0, 0, "（前奏）", Timing);
        PhraseTagger.Delete(doc, r1.LineIndex, PhraseTagger.FindOccurrences(doc.Lines[r1.LineIndex], new[] { "（前奏）" })[0]);
        Assert.Equal(new[] { "[00:10:00]あ[00:12:00]" }, All(doc));

        var r2 = PhraseTagger.Insert(doc, 0, 1, "（後奏）", Timing);
        var caret = PhraseTagger.Delete(doc, r2.LineIndex, PhraseTagger.FindOccurrences(doc.Lines[r2.LineIndex], new[] { "（後奏）" })[0]);
        Assert.Equal(new[] { "[00:10:00]あ[00:12:00]" }, All(doc));
        Assert.Equal((0, 1), caret);
    }

    [Fact]
    public void 消す_行の途中は直前の文字の終わりのタグを残す()
    {
        string original = "[00:10:00]あ[00:12:00][00:20:00]い[00:22:00]";
        var doc = Doc(original);
        PhraseTagger.Insert(doc, 0, 1, "（間奏）", Timing);
        var caret = PhraseTagger.Delete(doc, 0, PhraseTagger.FindOccurrences(doc.Lines[0], new[] { "（間奏）" })[0]);

        Assert.Equal(original, Line(doc, 0));
        Assert.Equal((0, 1), caret);
    }
}
