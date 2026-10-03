using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Tests;

/// <summary>絵文字の先行を譲る規則の、歌詞の側の計算（本当の歌い出し・絵文字の開始の寄せ）。</summary>
public class N3EmojiLeadTests
{
    private static readonly EmojiMatcher Kaho = new(new[] { "（花帆）", "（さやか）", "＿" });

    private static LyricsLine Line(string text) => LrcFormat.ParseLyricLine(text);

    private static string Text(LyricsLine line) => LrcFormat.WriteLyricLine(line);

    // ------------------------------------------------------------ 本当の歌い出し

    [Fact]
    public void 本当の歌い出し_絵文字とプレースホルダのタグを除きスペーサーと行末タグは含める()
    {
        Assert.Equal(24000, N3EmojiLead.RealStartMs(Line("[00:22:00]（花帆）[00:24:00]う[00:26:00]"), Kaho));
        Assert.Equal(24000, N3EmojiLead.RealStartMs(Line("[00:22:00]＿[00:24:00]う[00:26:00]"), Kaho));
        // 連続絵文字の間のスペーサー（T）
        Assert.Equal(13500, N3EmojiLead.RealStartMs(Line("[00:11:50]（花帆）[00:13:50][00:11:50]（さやか）[00:13:50]う[00:15:00]"), Kaho));
        // G キーの先行タグ（行頭のスペーサー）は実のタグ
        Assert.Equal(22000, N3EmojiLead.RealStartMs(Line("[00:22:00][00:24:00]う[00:26:00]"), Kaho));
        // 絵文字の後ろにタグの付いた文字が無ければ行末タグ
        Assert.Equal(13000, N3EmojiLead.RealStartMs(Line("[00:11:00]（花帆）う[00:13:00]"), Kaho));
        // matcher が無い・空なら行内の最小
        Assert.Equal(22000, N3EmojiLead.RealStartMs(Line("[00:22:00]（花帆）[00:24:00]う[00:26:00]"), null));
        Assert.Equal(22000, N3EmojiLead.RealStartMs(Line("[00:22:00]（花帆）[00:24:00]う[00:26:00]"), new EmojiMatcher(Array.Empty<string>())));
    }

    [Fact]
    public void 本当の歌い出し_寄せない絵文字のタグは除かない()
    {
        // T の無い絵文字のタグは寄せないので実のタグ
        Assert.Equal(22000, N3EmojiLead.RealStartMs(Line("[00:22:00]（花帆）"), Kaho));
        Assert.Equal(22000, N3EmojiLead.RealStartMs(Line("[00:24:00]う[00:25:00]え[00:22:00]（花帆）"), Kaho));
        Assert.Equal(22000, N3EmojiLead.RealStartMs(Line("[00:24:00]う[00:25:00]え[00:22:00]＿"), Kaho));
        // 出現の中の 2 つ目以降のタグも寄せないので実のタグ（除くのは出現の最初のタグ 22000 だけ）
        Assert.Equal(22500, N3EmojiLead.RealStartMs(Line("[00:22:00]（花[00:22:50]帆）[00:24:00]う[00:26:00]"), Kaho));
        // 同時歌唱で T（い 11000）より後の絵文字のタグは T 以上なので、数えても最小は変わらない
        Assert.Equal(11000, N3EmojiLead.RealStartMs(Line("[00:12:00]（花帆）[00:11:00]い[00:13:00]"), Kaho));
    }

    [Fact]
    public void 先行タグ_出現ごとに基準時刻と組で返しブロックはまとめて扱う()
    {
        // PerEmoji: 出現ごとにタグ。基準時刻はブロックの後ろの実文字
        var per = N3EmojiLead.FindLeads(Line("[00:11:50]（花帆）[00:13:50][00:11:50]（さやか）[00:13:50]う[00:15:00]"), Kaho);
        Assert.Equal(new[] { new LeadOccurrence(0, 1150, 1350), new LeadOccurrence(5, 1150, 1350) }, per);

        // ブロックモード: ブロックの先頭だけがタグを持つ
        var block = N3EmojiLead.FindLeads(Line("[00:11:50]（花帆）（さやか）[00:13:50]う[00:15:00]"), Kaho);
        Assert.Equal(new[] { new LeadOccurrence(0, 1150, 1350) }, block);

        // 基準にできるタグが無い出現
        var none = N3EmojiLead.FindLeads(Line("[00:10:00]あ[00:11:00]（花帆）"), Kaho);
        Assert.Equal(new[] { new LeadOccurrence(1, 1100, null) }, none);
    }

    // ------------------------------------------------------------ 表示秒数の下限を割らない最も遅い表示開始

    [Fact]
    public void 下限を割らない最も遅い表示開始_ワイプ前の表示時間と元の表示秒数の短い方を残す()
    {
        // 行頭の 2 秒の絵文字: ワイプ前の表示時間 1.5 秒を残す（T − 1500）。寄せるとちょうど 1.5 秒
        var head = Line("[00:22:00]（花帆）[00:24:00]う[00:26:00]");
        Assert.Equal(22500, N3EmojiLead.LatestBeginMs(head, Kaho, 1500));
        Assert.Equal(new[] { (2.0, 1.5) }, N3EmojiLead.Describe(head, 22500, Kaho));
        Assert.Equal(22500, N3EmojiLead.LatestBeginMs(Line("[00:22:00]＿[00:24:00]う[00:26:00]"), Kaho, 1500));
        // 表示秒数がワイプ前の表示時間より短い絵文字（1 秒）は縮めない（E）
        Assert.Equal(23000, N3EmojiLead.LatestBeginMs(Line("[00:23:00]（花帆）[00:24:00]う[00:26:00]"), Kaho, 1500));
        // 複数あれば厳しい方（連続絵文字の 2 つ: 12000 と 12500）
        Assert.Equal(12000, N3EmojiLead.LatestBeginMs(Line("[00:11:50]（花帆）[00:13:50][00:12:50]（さやか）[00:13:50]う[00:15:00]"), Kaho, 1500));
        // 行の途中の絵文字も数える
        Assert.Equal(13500, N3EmojiLead.LatestBeginMs(Line("[00:10:00]あ[00:13:00]（花帆）[00:15:00]い[00:16:00]"), Kaho, 1500));
        // 寄せない出現（T の無い出現・同時歌唱で E ≧ T）は数えない。対象が無ければ null
        Assert.Null(N3EmojiLead.LatestBeginMs(Line("[00:10:00]あ[00:11:00]（花帆）"), Kaho, 1500));
        Assert.Null(N3EmojiLead.LatestBeginMs(Line("[00:10:00]あ[00:12:00]（花帆）[00:11:00]い[00:13:00]"), Kaho, 1500));
        Assert.Null(N3EmojiLead.LatestBeginMs(Line("[00:10:00]あ[00:11:00]い[00:12:00]"), Kaho, 1500));
        Assert.Null(N3EmojiLead.LatestBeginMs(head, null, 1500));
        Assert.Null(N3EmojiLead.LatestBeginMs(head, new EmojiMatcher(Array.Empty<string>()), 1500));
    }

    // ------------------------------------------------------------ 絵文字の開始の寄せ

    [Fact]
    public void 寄せ_行頭の絵文字は表示開始へ寄せる()
    {
        var line = Line("[00:21:18](愛)[00:23:18]鮮[00:24:00]や[00:25:41]");
        var clamped = N3EmojiLead.ClampLine(line, 21680, new EmojiMatcher(new[] { "(愛)" }));
        Assert.Equal("[00:21:68](愛)[00:23:18]鮮[00:24:00]や[00:25:41]", Text(clamped));
        Assert.Equal(new[] { (2.0, 1.5) }, N3EmojiLead.Describe(line, 21680, new EmojiMatcher(new[] { "(愛)" })));
    }

    [Fact]
    public void 寄せ_連続絵文字は両方とも寄せる()
    {
        var line = Line("[00:11:50]（花帆）[00:13:50][00:11:50]（さやか）[00:13:50]う[00:15:00]");
        var clamped = N3EmojiLead.ClampLine(line, 12475, Kaho);
        Assert.Equal("[00:12:48]（花帆）[00:13:50][00:12:48]（さやか）[00:13:50]う[00:15:00]", Text(clamped));
        Assert.Equal(2, N3EmojiLead.Describe(line, 12475, Kaho).Count);
    }

    [Fact]
    public void 寄せ_ブロックモードは先頭だけを寄せる()
    {
        var clamped = N3EmojiLead.ClampLine(Line("[00:11:50]（花帆）（さやか）[00:13:50]う[00:15:00]"), 12475, Kaho);
        Assert.Equal("[00:12:48]（花帆）（さやか）[00:13:50]う[00:15:00]", Text(clamped));
    }

    [Fact]
    public void 寄せ_表示開始より後の絵文字は変えない()
    {
        // 行の途中の絵文字（い の先行）は表示開始 8500 より後
        var line = Line("[00:10:00]あ[00:11:00]（花帆）[00:13:00]い[00:14:00]");
        Assert.Same(line, N3EmojiLead.ClampLine(line, 8500, Kaho));
        Assert.Empty(N3EmojiLead.Describe(line, 8500, Kaho));
        // 表示開始ちょうども変えない
        var head = Line("[00:21:68](愛)[00:23:18]鮮[00:25:41]");
        Assert.Same(head, N3EmojiLead.ClampLine(head, 21680, new EmojiMatcher(new[] { "(愛)" })));
    }

    [Fact]
    public void 寄せ_同時歌唱で基準時刻より後の絵文字のタグは変えない()
    {
        // 絵文字のタグ 12000 が前のパートの終わりを兼ね、基準時刻 T（い 11000）より後
        var line = Line("[00:10:00]あ[00:12:00]（花帆）[00:11:00]い[00:13:00]");
        Assert.Same(line, N3EmojiLead.ClampLine(line, 15000, Kaho));
        Assert.Empty(N3EmojiLead.Describe(line, 15000, Kaho));
    }

    [Fact]
    public void 寄せ_基準時刻の無い絵文字は変えない()
    {
        var line = Line("[00:10:00]あ[00:11:00]（花帆）");
        Assert.Same(line, N3EmojiLead.ClampLine(line, 12000, Kaho));
    }

    [Fact]
    public void 寄せ_10ms単位に切り上げ基準時刻を超えない()
    {
        var line = Line("[00:22:00]（花帆）[00:24:00]う[00:26:00]");
        // 表示開始 22975 → 22980（絵文字は 1.02 秒）
        Assert.Equal("[00:22:98]（花帆）[00:24:00]う[00:26:00]", Text(N3EmojiLead.ClampLine(line, 22975, Kaho)));
        Assert.Equal(new[] { (2.0, 1.02) }, N3EmojiLead.Describe(line, 22975, Kaho));
        // 表示開始が基準時刻より後でも T まで（絵文字は 0 秒）
        Assert.Equal("[00:24:00]（花帆）[00:24:00]う[00:26:00]", Text(N3EmojiLead.ClampLine(line, 25000, Kaho)));
        // プレースホルダも同じ
        Assert.Equal("[00:22:98]＿[00:24:00]う[00:26:00]", Text(N3EmojiLead.ClampLine(Line("[00:22:00]＿[00:24:00]う[00:26:00]"), 22975, Kaho)));
        // matcher が無ければ変えない
        Assert.Same(line, N3EmojiLead.ClampLine(line, 22975, null));
    }

    [Fact]
    public void 寄せ_元の行とドキュメントは変えない()
    {
        var doc = LrcFormat.Parse(string.Join("\r\n",
            "@Emoji=（花帆）,a.png",
            "[00:20:00]あ[00:22:50]",
            "[00:21:00]い[00:26:00]",
            "",
            "[00:22:00]（花帆）[00:24:00]う[00:26:00]",
            "[00:27:00]え[00:29:00]",
            ""));
        string before = LrcFormat.Write(doc);
        var show = new N3ShowTimeSettings { LeadMs = 1500, TailMs = 800, IntervalMs = 300, EmojiLeadYield = true, LeadMatcher = Kaho };

        var (plans, clamped) = N3EmojiLead.PrepareTab(doc, show);
        Assert.NotSame(doc, clamped);
        Assert.Equal(before, LrcFormat.Write(doc));
        Assert.Equal("[00:22:00]（花帆）[00:24:00]う[00:26:00]", Text(doc.Lines[3]));
        Assert.Equal("[00:22:50]（花帆）[00:24:00]う[00:26:00]", Text(clamped.Lines[3]));
        Assert.NotSame(doc.Lines[3].Chars[0], clamped.Lines[3].Chars[0]);
        // 寄せる所の無い行は同じ内容。曲の設定（@Emoji）も写る
        Assert.Equal(Text(doc.Lines[0]), Text(clamped.Lines[0]));
        Assert.Equal(doc.EmojiEntries.Select(e => e.ReplaceChar), clamped.EmojiEntries.Select(e => e.ReplaceChar));
        // 表示時刻は元の歌詞で計算した値（絵文字はワイプ前の表示時間 1.5 秒を残す）
        Assert.Equal(22500, plans[3].BeginMs);
        Assert.Equal(N3ShowTimePlanner.Plan(doc, show)[3], plans[3]);

        // 何度呼んでも同じ
        var (plans2, clamped2) = N3EmojiLead.PrepareTab(doc, show);
        Assert.Equal(LrcFormat.Write(clamped), LrcFormat.Write(clamped2));
        Assert.Equal(plans.OrderBy(kv => kv.Key), plans2.OrderBy(kv => kv.Key));

        // 寄せる所が無ければ元のドキュメントそのもの
        Assert.Same(doc, N3EmojiLead.ClampDocument(doc, new Dictionary<int, N3LinePlan>(), Kaho));
        // 規則オフなら元のドキュメントそのもの
        show.EmojiLeadYield = false;
        Assert.Same(doc, N3EmojiLead.PrepareTab(doc, show).Clamped);
    }
}
