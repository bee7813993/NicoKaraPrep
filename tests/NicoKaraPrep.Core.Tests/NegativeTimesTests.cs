using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;
using NicoKaraPrep.Core.Project;
using NicoKaraPrep.Core.Validation;

namespace NicoKaraPrep.Core.Tests;

/// <summary>0 秒より前（マイナス）の時刻: 内部ではマイナスのまま持ち、保存・書き出しでは 0 秒、作業状態から戻す。</summary>
public class NegativeTimesTests
{
    private static readonly EmojiMatcher Emoji = new(["★"]);
    private static readonly EmojiTagSettings Lead2s = new() { LeadCs = 200, PerEmoji = true };

    private static LyricsDocument Doc(params string[] lines)
    {
        var doc = new LyricsDocument();
        foreach (string l in lines) doc.Lines.Add(TextEditModeFormat.ParseLyricLine(l));
        return doc;
    }

    private static string Norm(string line) => TextEditModeFormat.WriteLyricLine(TextEditModeFormat.ParseLyricLine(line));

    [Fact]
    public void マイナスの時刻を書き分ける()
    {
        Assert.Equal("[-00:01:05]", TimeTag.FormatSigned(-105));
        Assert.Equal("[00:01:05]", TimeTag.FormatSigned(105));
        Assert.Equal("[00:00:00]", TimeTag.Format(-105)); // ファイルへ書く形は 0
    }

    [Theory]
    [InlineData("[-00:01:05]", -105, 11)]
    [InlineData("[-01:00]", -6000, 8)]
    [InlineData("[-00:00:00]", 0, 11)]
    [InlineData("[00:01:05]", 105, 10)]
    public void マイナスの時刻を読める(string text, int cs, int length)
    {
        Assert.True(TimeTag.TryParseAt(text, 0, out int parsed, out int len));
        Assert.Equal(cs, parsed);
        Assert.Equal(length, len);
        Assert.True(TimeTag.TryParse(text.Trim('[', ']'), out int parsed2));
        Assert.Equal(cs, parsed2);
    }

    [Fact]
    public void 行の編集欄の書き方でマイナスが往復する()
    {
        // チェック数付きのタグ・ルビの中のタグと補助タグ・行末のタグ
        const string raw = "[1|-00:01:00]★{歌|[2|-00:00:50]うた[-00:00:20]}[-00:00:10]";
        var line = TextEditModeFormat.ParseLyricLine(raw);

        Assert.Equal(-100, line.Chars[0].TimeCs);
        Assert.Equal(1, line.Chars[0].CheckCount);
        Assert.Equal(-50, line.Chars[1].TimeCs);
        Assert.Equal(2, line.Chars[1].CheckCount);
        Assert.Equal(-20, line.Chars[1].AuxTimeTagsCs[0]);
        Assert.Equal(-10, line.EndTimeCs);
        Assert.Equal(raw, TextEditModeFormat.WriteLyricLine(line));
    }

    [Fact]
    public void アイコンの開始が0秒より前ならマイナスのまま()
    {
        var line = LrcFormat.ParseLyricLine("[00:01:00]歌詞[00:03:00]");
        EmojiTagger.InsertEmoji(line, 0, "★", Emoji, Lead2s);

        Assert.Equal(-100, line.Chars[0].TimeCs);
        Assert.Equal("[00:00:00]★[00:01:00]歌詞[00:03:00]", LrcFormat.WriteLyricLine(line)); // lrc には 0 秒で書く
    }

    [Fact]
    public void 遅らせるとマイナスの値から計算する()
    {
        var doc = Doc("[-00:01:00]★[00:01:00]歌詞[00:03:00]");

        TimeShiftOperations.Shift(doc, 300);

        Assert.Equal(Norm("[00:02:00]★[00:04:00]歌詞[00:06:00]"), TextEditModeFormat.WriteLyricLine(doc.Lines[0]));
    }

    [Fact]
    public void 早めると0秒より前でもマイナスのまま()
    {
        var doc = Doc("[00:01:00]あ[00:02:00]");
        doc.Lines[0].ShowBeginCs = 20;
        doc.Lines[0].ShowBeginOrigin = ShowTimeOrigin.Manual;

        TimeShiftOperations.Shift(doc, -150);

        Assert.Equal(Norm("[-00:00:50]あ[00:00:50]"), TextEditModeFormat.WriteLyricLine(doc.Lines[0]));
        Assert.Equal(-130, doc.Lines[0].ShowBeginCs);
    }

    [Fact]
    public void 出力用の写しは0秒にして元は変えない()
    {
        var doc = Doc("[-00:01:00]★[00:01:00]歌詞[00:03:00]", "[00:05:00]い[00:06:00]");
        doc.Lines[0].ShowBeginCs = -150;
        doc.UnappliedRubyEntries.Add(new RubyEntry("空", "そら", -50, 100));

        var output = NegativeTimes.ForOutput(doc, out int clamped);

        Assert.Equal(1, clamped);
        Assert.NotSame(doc, output);
        Assert.Equal(0, output.Lines[0].Chars[0].TimeCs);
        Assert.Equal(0, output.Lines[0].ShowBeginCs);
        Assert.Equal(new RubyEntry("空", "そら", 0, 100), output.UnappliedRubyEntries[0]);
        Assert.Equal(-100, doc.Lines[0].Chars[0].TimeCs); // 元はマイナスのまま
        Assert.Equal(-150, doc.Lines[0].ShowBeginCs);
    }

    [Fact]
    public void マイナスが無ければ出力用はそのまま()
    {
        var doc = Doc("[00:01:00]あ[00:02:00]");

        Assert.Same(doc, NegativeTimes.ForOutput(doc, out int clamped));
        Assert.Equal(0, clamped);
    }

    [Fact]
    public void rlfにはマイナスを001秒で書く()
    {
        var doc = Doc("[-00:01:00]★[00:01:00]歌詞[00:03:00]");

        var read = RlfFormat.Read(RlfFormat.Write(doc));

        Assert.Equal(1, read.Lines[0].Chars[0].TimeCs);
    }

    [Fact]
    public void 作業状態に残したマイナスを開き直した行へ戻す()
    {
        var doc = Doc("[-00:01:00]★[00:01:00]歌詞[00:03:00]", "[00:05:00]い[00:06:00]");
        var settings = LineExportSettings.Collect(doc);
        string lrc = LrcFormat.Write(NegativeTimes.ForOutput(doc, out _));

        var reopened = TextEditModeFormat.Parse(lrc);
        Assert.Equal(0, reopened.Lines[0].Chars[0].TimeCs);
        LineExportSettings.Apply(reopened, settings);

        Assert.Equal(-100, reopened.Lines[0].Chars[0].TimeCs);
        Assert.Single(settings); // マイナスのある行だけ（ほかの設定が無い行は残さない）
    }

    [Fact]
    public void rlfで開き直してもマイナスへ戻す()
    {
        var doc = Doc("[-00:01:00]★[00:01:00]歌詞[00:03:00]");
        var settings = LineExportSettings.Collect(doc);

        var reopened = RlfFormat.Read(RlfFormat.Write(NegativeTimes.ForOutput(doc, out _)));
        LineExportSettings.Apply(reopened, settings);

        Assert.Equal(-100, reopened.Lines[0].Chars[0].TimeCs);
    }

    [Fact]
    public void ほかのアプリでタグを直した行には戻さない()
    {
        var doc = Doc("[-00:01:00]★[00:01:00]歌詞[00:03:00]");
        var settings = LineExportSettings.Collect(doc);

        var changed = Doc("[00:00:30]★[00:01:00]歌詞[00:03:00]"); // 0 秒でなくなっている
        LineExportSettings.Apply(changed, settings);
        Assert.Equal(30, changed.Lines[0].Chars[0].TimeCs);

        var moreTags = Doc("[00:00:00]★[00:01:00]歌[00:02:00]詞[00:03:00]"); // タグの数が違う
        LineExportSettings.Apply(moreTags, settings);
        Assert.Equal(0, moreTags.Lines[0].Chars[0].TimeCs);
    }

    [Fact]
    public void マイナスのある行をチェックで知らせる()
    {
        var doc = Doc("[00:01:00]あ[00:02:00]", "[-00:01:00]★[-00:00:50]★[00:01:00]歌詞[00:03:00]");

        var issues = NegativeTimeValidator.Validate(doc);

        var issue = Assert.Single(issues);
        Assert.Equal(1, issue.LineIndex);
        Assert.Equal(IssueSeverity.Warning, issue.Severity);
        Assert.Contains("2 個", issue.Message);
        Assert.Contains("−1.00 秒", issue.Message);
        Assert.Equal(2, NegativeTimes.Count(doc));
        Assert.Equal(-100, NegativeTimes.Earliest(doc));
    }
}
