using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;
using NicoKaraPrep.Core.Validation;

namespace NicoKaraPrep.Core.Tests;

/// <summary>表示時刻のチェック（書き出しと同じ表示時刻の計算で、前の行がワイプの途中で消える組・絵文字を縮めても足りない組）。</summary>
public class N3ShowTimeValidatorTests
{
    private static LyricsDocument Lyrics(params string[] lines) => LrcFormat.Parse(string.Join("\r\n", lines) + "\r\n");

    /// <summary>ワイプ前 1500 / ワイプ後 800 / 表示間隔 300。yield = 絵文字の分だけ遅らせる規則。</summary>
    private static N3ShowTimeSettings Settings(bool yield, params string[] emoji) =>
        new() { LeadMs = 1500, TailMs = 800, IntervalMs = 300, EmojiLeadYield = yield, LeadMatcher = new EmojiMatcher(emoji) };

    private static List<ValidationIssue> Validate(LyricsDocument doc, N3ShowTimeSettings s, IReadOnlySet<(int, int)>? skip = null) =>
        N3ShowTimeValidator.Validate(doc, N3ShowTimePlanner.Plan(doc, s), s, skip);

    /// <summary>2 行ページ 2 枚。上段の次の行の絵文字（22000）が、前の行のワイプ終了（22500）より前に始まる（planner の例 B）。</summary>
    private static LyricsDocument Example3() => Lyrics(
        "@Emoji=（花帆）,a.png",
        "[00:20:00]あ[00:22:50]",
        "[00:21:00]い[00:26:00]",
        "",
        "[00:22:00]（花帆）[00:24:00]う[00:26:00]",
        "[00:27:00]え[00:29:00]");

    [Fact]
    public void 前の行がワイプの途中で消えるとエラー_規則で解消できれば絵文字を縮めた警告だけ()
    {
        var doc = Example3();

        // 規則オフ（ニコカラメーカー3 と同じ計算）: 前の行は次の行の絵文字の開始（22000）で消える
        var off = Validate(doc, Settings(false, "（花帆）"));
        var error = Assert.Single(off);
        Assert.Equal(IssueSeverity.Error, error.Severity);
        Assert.Equal(N3ShowTimeValidator.Category, error.Category);
        Assert.Equal((3, 0), (error.LineIndex, error.RelatedLineIndex));
        Assert.Equal("4行目（ページ1→2・下から2行目）: 前の行（1行目）がワイプの途中（残り 0.5 秒）で消えます", error.Message);

        // 規則オン: 前の行はワイプ後を残す（22900）。絵文字は 1.02 秒（ワイプ前 1.5 秒より短い）に縮むので警告
        var on = Validate(doc, Settings(true, "（花帆）"));
        var warning = Assert.Single(on);
        Assert.Equal(IssueSeverity.Warning, warning.Severity);
        Assert.Equal((3, 0), (warning.LineIndex, warning.RelatedLineIndex));
        Assert.Equal("4行目（ページ1→2・下から2行目）: 絵文字を 2.0→1.02 秒に縮めても前の行（1行目）と重なります", warning.Message);
    }

    [Fact]
    public void 絵文字を縮めても前の行がワイプの途中で消えるならエラーに縮めた秒数を添える()
    {
        // 次の行の歌い出し（23000）自体が前の行のワイプ終了（24000）より前
        var doc = Lyrics(
            "@Emoji=（花帆）,a.png",
            "[00:20:00]あ[00:24:00]",
            "[00:21:00]い[00:26:00]",
            "",
            "[00:22:00]（花帆）[00:23:00]う[00:26:00]",
            "[00:27:00]え[00:29:00]");

        var off = Assert.Single(Validate(doc, Settings(false, "（花帆）")));
        Assert.Equal("4行目（ページ1→2・下から2行目）: 前の行（1行目）がワイプの途中（残り 2.0 秒）で消えます", off.Message);

        var on = Assert.Single(Validate(doc, Settings(true, "（花帆）")));
        Assert.Equal(IssueSeverity.Error, on.Severity);
        Assert.Equal("4行目（ページ1→2・下から2行目）: 絵文字を 1.0→0.0 秒に縮めても、前の行（1行目）がワイプの途中（残り 1.0 秒）で消えます", on.Message);

        // 上から対応付ける設定では段の数え方が変わる
        var top = Settings(false, "（花帆）");
        top.AlignFromTop = true;
        Assert.StartsWith("4行目（ページ1→2・上から1行目）: ", Assert.Single(Validate(doc, top)).Message);
    }

    [Fact]
    public void ワイプ前の表示時間ちょうどまで縮めて解消した組は出さない()
    {
        // Circle of Love 冒頭 3 ページ。規則で 2 つの上段の絵文字が 1.5 秒・1.69 秒になる（どちらもワイプ前 1.5 秒以上）
        var doc = LrcFormat.Parse(string.Join("\r\n",
            "@Emoji=(愛),a.png",
            "[00:16:21](愛)[00:18:21]曇[00:19:00]り[00:20:58]",
            "[00:19:86]「[00:20:70]大[00:23:12]",
            "",
            "[00:21:18](愛)[00:23:18]鮮[00:24:00]や[00:25:41]",
            "[00:25:49]O[00:25:95]ur[00:27:80]",
            "",
            "[00:26:20](愛)[00:28:20]無[00:29:00]限[00:30:46]",
            "[00:29:65]「[00:30:60]楽[00:32:94]",
            ""));
        var s = Settings(true, "(愛)");
        var plans = N3ShowTimePlanner.Plan(doc, s);
        Assert.Equal((2.0, 1.5), Assert.Single(N3EmojiLead.Describe(doc.Lines[3], plans[3].BeginMs, s.LeadMatcher)));
        Assert.Empty(N3ShowTimeValidator.Validate(doc, plans, s));
        Assert.Empty(Validate(doc, Settings(false, "(愛)")));
    }

    [Fact]
    public void ページ衝突のエラーが出ている組には重ねて出さない()
    {
        var doc = Example3();
        Assert.Empty(Validate(doc, Settings(false, "（花帆）"), new HashSet<(int, int)> { (0, 3) }));
        // 別の組を指定しても消えない
        Assert.Single(Validate(doc, Settings(false, "（花帆）"), new HashSet<(int, int)> { (1, 4) }));
    }

    [Fact]
    public void 表示開始の手動指定に合わせて縮める絵文字は警告しない()
    {
        var doc = Example3();
        doc.Lines[3].ShowBeginCs = 2300; // 絵文字（22000〜24000）は書き出しで 1.0 秒に縮む
        var s = Settings(true, "（花帆）");
        var plans = N3ShowTimePlanner.Plan(doc, s);
        Assert.Equal((2.0, 1.0), Assert.Single(N3EmojiLead.Describe(doc.Lines[3], plans[3].BeginMs, s.LeadMatcher)));
        Assert.Empty(N3ShowTimeValidator.Validate(doc, plans, s));
    }

    [Fact]
    public void 縮めた秒数の説明()
    {
        Assert.Equal("", N3ShowTimeValidator.FormatShrinks(Array.Empty<(double, double)>()));
        Assert.Equal("2.0→1.5・1.0→0.0", N3ShowTimeValidator.FormatShrinks(new[] { (2.0, 1.5), (2.0, 1.5), (1.0, 0.0) }));
        Assert.Equal("2.0→1.02", N3ShowTimeValidator.FormatShrinks(new[] { (2.0, 1.02) }));
    }
}
