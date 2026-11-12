using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Tests;

public class TimeShiftOperationsTests
{
    private static LyricsDocument Doc(params string[] lines)
    {
        var doc = new LyricsDocument();
        foreach (string l in lines) doc.Lines.Add(TextEditModeFormat.ParseLyricLine(l));
        return doc;
    }

    private static string Text(LyricsDocument doc, int line) => TextEditModeFormat.WriteLyricLine(doc.Lines[line]);

    /// <summary>テキスト編集モードの書き方を、書き出しと同じ形（チェック数付きなど）にそろえる。</summary>
    private static string Norm(string line) => TextEditModeFormat.WriteLyricLine(TextEditModeFormat.ParseLyricLine(line));

    [Fact]
    public void 文字のタグと行末タグとルビの補助タグを同じだけずらす()
    {
        var doc = Doc("[00:01:00]あ{歌|[2|00:01:50]う[00:01:80]た}[00:02:00]", "", "[00:10:00]い[00:11:00]");

        int moved = TimeShiftOperations.Shift(doc, 25);

        Assert.Equal(Norm("[00:01:25]あ{歌|[2|00:01:75]う[00:02:05]た}[00:02:25]"), Text(doc, 0));
        Assert.True(doc.Lines[1].IsEmpty); // 空行は空行のまま
        Assert.Equal(Norm("[00:10:25]い[00:11:25]"), Text(doc, 2));
        Assert.Equal(6, moved);
    }

    [Fact]
    public void 早めるときも同じだけ動かす()
    {
        var doc = Doc("[00:05:00]あ[00:06:00]");

        TimeShiftOperations.Shift(doc, -150);

        Assert.Equal(Norm("[00:03:50]あ[00:04:50]"), Text(doc, 0));
    }

    [Fact]
    public void 行の表示時刻は出どころを問わずずらし出どころは残す()
    {
        var doc = Doc("[00:05:00]あ[00:06:00]", "[00:08:00]い[00:09:00]");
        doc.Lines[0].ShowBeginCs = 350;
        doc.Lines[0].ShowBeginOrigin = ShowTimeOrigin.Manual;
        doc.Lines[0].ShowEndCs = 680;
        doc.Lines[0].ShowEndOrigin = ShowTimeOrigin.Loaded;
        doc.Lines[1].ShowEndCs = 980;
        doc.Lines[1].ShowEndOrigin = ShowTimeOrigin.Auto;

        TimeShiftOperations.Shift(doc, 100);

        Assert.Equal(450, doc.Lines[0].ShowBeginCs);
        Assert.Equal(ShowTimeOrigin.Manual, doc.Lines[0].ShowBeginOrigin);
        Assert.Equal(780, doc.Lines[0].ShowEndCs);
        Assert.Equal(ShowTimeOrigin.Loaded, doc.Lines[0].ShowEndOrigin);
        Assert.Null(doc.Lines[1].ShowBeginCs); // 自動のまま
        Assert.Equal(1080, doc.Lines[1].ShowEndCs);
    }

    [Fact]
    public void 表示時刻も0秒より前はマイナスのまま()
    {
        var doc = Doc("[00:01:00]あ[00:02:00]");
        doc.Lines[0].ShowBeginCs = 20;
        doc.Lines[0].ShowBeginOrigin = ShowTimeOrigin.Manual;

        TimeShiftOperations.Shift(doc, -100);

        Assert.Equal(-80, doc.Lines[0].ShowBeginCs);
        Assert.Equal(Norm("[00:00:00]あ[00:01:00]"), Text(doc, 0));
    }

    [Fact]
    public void 当てはめられなかった時刻付きのルビもずらす()
    {
        var doc = Doc("[00:01:00]あ[00:02:00]");
        doc.UnappliedRubyEntries.Add(new RubyEntry("空", "そら", 1000, 2000));
        doc.UnappliedRubyEntries.Add(new RubyEntry("海", "うみ"));
        doc.UnappliedRubyEntries.Add(new RubyEntry("山", "やま", null, 3000));

        TimeShiftOperations.Shift(doc, 50);

        Assert.Equal(new RubyEntry("空", "そら", 1050, 2050), doc.UnappliedRubyEntries[0]);
        Assert.Equal(new RubyEntry("海", "うみ"), doc.UnappliedRubyEntries[1]);
        Assert.Equal(new RubyEntry("山", "やま", null, 3050), doc.UnappliedRubyEntries[2]);
    }

    [Fact]
    public void ずらせる範囲は全タブのいちばん早いタグと遅いタグで決まる()
    {
        var main = Doc("[00:03:00]あ[00:04:00]", "[01:00:00]い[01:01:00]");
        var chorus = Doc("[00:02:50]う[00:03:00]");
        var empty = Doc("タグの無い行");

        var (min, max) = TimeShiftOperations.ShiftLimits(new[] { main, chorus, empty });

        Assert.Equal(TimeShiftOperations.MinTimeCs - 250, min); // 0 秒より前はマイナスのまま持てる
        Assert.Equal(TimeShiftOperations.MaxTimeCs - 6100, max);
    }

    [Fact]
    public void ずらせる範囲は補助タグも数える()
    {
        var doc = Doc("{歌|[2|00:01:50]う[00:01:20]た}[00:02:00]");

        Assert.Equal((120, 200), TimeShiftOperations.TagRange(doc));
    }

    [Fact]
    public void タグが無ければずらせない()
    {
        Assert.Equal((0, 0), TimeShiftOperations.ShiftLimits(new[] { Doc("あいう", "") }));
        Assert.Null(TimeShiftOperations.TagRange(Doc("あいう")));
    }

    [Fact]
    public void ずらして戻すと元どおり()
    {
        var doc = Doc("[00:01:00]あ{歌|[2|00:01:50]う[00:01:80]た}[00:02:00]", "[00:10:00]い[00:11:00]");
        string All() => string.Join("\n", doc.Lines.Select(l => TextEditModeFormat.WriteLyricLine(l)));
        string before = All();

        TimeShiftOperations.Shift(doc, -37);
        TimeShiftOperations.Shift(doc, 37);

        Assert.Equal(before, All());
    }

    [Fact]
    public void lrcに書き出すと時刻付きのルビもずれた時刻になる()
    {
        // 読みが途中で変わるルビは、変わる位置の時刻を適用区間に書く（ずらしたあとの時刻になる）
        var doc = Doc("{空|[00:01:00]そら}[00:02:00]", "{空|[00:05:00]くう}[00:06:00]");

        TimeShiftOperations.Shift(doc, 100);
        string lrc = LrcFormat.Write(doc);

        Assert.Contains("@Ruby1=空,そら,,[00:06:00]", lrc);
        Assert.Contains("@Ruby2=空,くう,[00:06:00]", lrc);
    }
}
