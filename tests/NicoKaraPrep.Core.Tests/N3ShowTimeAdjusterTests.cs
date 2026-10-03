using System.Text.Json;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;
using NicoKaraPrep.Core.Project;
using NicoKaraPrep.Core.Validation;

namespace NicoKaraPrep.Core.Tests;

/// <summary>
/// 表示時刻を行に持たせる流れ（読み込んだ値をそのまま使う → 自動調整を実行 → 手で直す → 書き出す）。
/// 自動調整（<see cref="N3ShowTimeAdjuster"/>）・読み込み（<see cref="N3ProjImport.LoadShowTimes"/>）・出どころの保存・チェック。
/// </summary>
public class N3ShowTimeAdjusterTests
{
    private static LyricsDocument Lyrics(params string[] lines) => LrcFormat.Parse(string.Join("\r\n", lines) + "\r\n");

    /// <summary>ワイプ前 1500 / ワイプ後 800 / 表示間隔 300（保護時間は自動 400）。</summary>
    private static N3ShowTimeSettings Show(int leadMs = 1500) => new() { LeadMs = leadMs, TailMs = 800, IntervalMs = 300 };

    private static N3ShowTimeSettings Yield(params string[] emoji) =>
        new() { LeadMs = 1500, TailMs = 800, IntervalMs = 300, EmojiLeadYield = true, LeadMatcher = new EmojiMatcher(emoji) };

    /// <summary>
    /// 2 行ページ 2 枚。自動の計算（ワイプ前 1.5 秒）では、ページ 1 は 8500 から、ページ 2 は 14500 から出る。
    /// 下の段の組（1 行目 → 4 行目）は重なるので、4 行目は前の行の終了 14800 ＋ 表示間隔 300 = 15100 まで遅れる。
    /// </summary>
    private static LyricsDocument TwoPages() => Lyrics(
        "[00:10:00]あ[00:12:00]",
        "[00:12:50]い[00:14:00]",
        "",
        "[00:16:00]う[00:18:00]",
        "[00:18:50]え[00:20:00]");

    private static (int? Begin, int? End, ShowTimeOrigin BeginOrigin, ShowTimeOrigin EndOrigin) Stored(LyricsLine l) =>
        (l.ShowBeginCs, l.ShowEndCs, l.ShowBeginOrigin, l.ShowEndOrigin);

    [Fact]
    public void 自動調整_計算した表示時刻を自動調整の値として全行に持たせる()
    {
        var doc = TwoPages();
        var live = N3ShowTimePlanner.Plan(doc, Show());
        var result = N3ShowTimeAdjuster.Run(doc, Show());

        Assert.Equal(new N3ShowTimeAdjustResult(4, 0, 0), result);
        Assert.Equal((850, 1280, ShowTimeOrigin.Auto, ShowTimeOrigin.Auto), Stored(doc.Lines[0]));
        Assert.Equal((850, 1480, ShowTimeOrigin.Auto, ShowTimeOrigin.Auto), Stored(doc.Lines[1]));
        Assert.Equal((1450, 1880, ShowTimeOrigin.Auto, ShowTimeOrigin.Auto), Stored(doc.Lines[3]));
        Assert.Equal((1510, 2080, ShowTimeOrigin.Auto, ShowTimeOrigin.Auto), Stored(doc.Lines[4]));
        Assert.Null(doc.Lines[2].ShowBeginCs); // 空行には持たせない

        // 持たせた値は、その後の計算でそのまま使う（自動の計算と同じ値）
        var plans = N3ShowTimePlanner.Plan(doc, Show());
        foreach (var (i, p) in live) Assert.Equal((p.BeginMs, p.EndMs), (plans[i].BeginMs, plans[i].EndMs));
        // 行リストの ✎ は付かない（手で指定した値ではない）
        Assert.All(doc.Lines, l => Assert.False(l.HasManualN3Overrides));
        Assert.True(doc.Lines[0].HasN3Overrides); // 保存はする
    }

    [Fact]
    public void 自動調整_実行し直すと手で直した値は残しほかの値は計算し直す()
    {
        var doc = TwoPages();
        N3ShowTimeAdjuster.Run(doc, Show());
        doc.Lines[4].ShowBeginCs = 1600; // 4 行目の表示開始を手で直す
        doc.Lines[4].ShowBeginOrigin = ShowTimeOrigin.Manual;

        // ワイプ前を 1.0 秒にして実行し直す: ページ 1 は 9000、ページ 2 は 15000 から（4 行目の開始は手で直した 16000 のまま）
        var result = N3ShowTimeAdjuster.Run(doc, Show(1000));
        Assert.Equal(new N3ShowTimeAdjustResult(4, 1, 0), result);
        Assert.Equal((900, 1280, ShowTimeOrigin.Auto, ShowTimeOrigin.Auto), Stored(doc.Lines[0]));
        Assert.Equal((1500, 1880, ShowTimeOrigin.Auto, ShowTimeOrigin.Auto), Stored(doc.Lines[3]));
        Assert.Equal((1600, 2080, ShowTimeOrigin.Manual, ShowTimeOrigin.Auto), Stored(doc.Lines[4]));
        Assert.True(doc.Lines[4].HasManualN3Overrides);
        Assert.False(doc.Lines[3].HasManualN3Overrides);
    }

    [Fact]
    public void 読み込み_全行にそのまま持たせ_自動調整で計算し直す()
    {
        var doc = TwoPages();
        var actual = new Dictionary<int, (int BeginMs, int EndMs)> { [0] = (8000, 12500), [1] = (8000, 14400), [3] = (14000, 18800), [4] = (14900, 20800) };
        Assert.Equal(4, N3ProjImport.LoadShowTimes(doc, actual));
        Assert.Equal((800, 1250, ShowTimeOrigin.Loaded, ShowTimeOrigin.Loaded), Stored(doc.Lines[0]));

        // 読み込んだ値をそのまま使う（自動の計算とは違う値でも）
        var plans = N3ShowTimePlanner.Plan(doc, Show());
        Assert.Equal((8000, 12500), (plans[0].BeginMs, plans[0].EndMs));
        Assert.Equal((14900, 20800), (plans[4].BeginMs, plans[4].EndMs));
        Assert.Equal(new N3ShowTimeOriginCounts(0, 4, 0, 0), N3ShowTimeAdjuster.Count(doc));
        // いま実行すると 4 行とも変わる（1 行目 8500・12800、2 行目 8500・14800、4 行目 14500、5 行目 15100）
        Assert.Equal(4, N3ShowTimeAdjuster.CountOutdated(doc, Show()));

        N3ShowTimeAdjuster.Run(doc, Show());
        Assert.Equal((850, 1280, ShowTimeOrigin.Auto, ShowTimeOrigin.Auto), Stored(doc.Lines[0]));
        Assert.Equal(new N3ShowTimeOriginCounts(0, 0, 4, 0), N3ShowTimeAdjuster.Count(doc));
        Assert.Equal(0, N3ShowTimeAdjuster.CountOutdated(doc, Show()));
        // 設定を変えると、実行し直すと変わる行が出る（ワイプ前 1.0 秒: 1・2・4 行目の開始が変わる）
        Assert.Equal(3, N3ShowTimeAdjuster.CountOutdated(doc, Show(1000)));
    }

    [Fact]
    public void 数える_手動_読み込み_自動調整_未設定()
    {
        var doc = TwoPages();
        N3ProjImport.LoadShowTimes(doc, new Dictionary<int, (int, int)> { [0] = (8000, 12500), [3] = (14000, 18800) });
        doc.Lines[4].ShowEndCs = 2100; // 手動
        Assert.Equal(new N3ShowTimeOriginCounts(1, 2, 0, 1), N3ShowTimeAdjuster.Count(doc));
        Assert.True(N3ShowTimeAdjuster.HasRecomputable(doc));
        N3ShowTimeAdjuster.ClearRecomputable(doc);
        Assert.Equal(new N3ShowTimeOriginCounts(1, 0, 0, 3), N3ShowTimeAdjuster.Count(doc));
        Assert.False(N3ShowTimeAdjuster.HasRecomputable(doc));
    }

    [Fact]
    public void 写しと保存_出どころも写し_以前の版のファイルは手動指定として読む()
    {
        var doc = TwoPages();
        N3ShowTimeAdjuster.Run(doc, Show());
        doc.Lines[4].ShowBeginOrigin = ShowTimeOrigin.Manual;
        doc.Lines[3].ShowEndOrigin = ShowTimeOrigin.Loaded;

        var clone = doc.Lines[3].Clone();
        Assert.Equal(Stored(doc.Lines[3]), Stored(clone));

        var saved = LineExportSettings.Collect(doc);
        Assert.Equal(new[] { 0, 1, 3, 4 }, saved.Select(s => s.Index));
        string json = JsonSerializer.Serialize(saved);
        Assert.Contains("\"ShowEndOrigin\":\"Loaded\"", json); // 名前で保存する
        var back = TwoPages();
        LineExportSettings.Apply(back, JsonSerializer.Deserialize<List<LineExportSettings>>(json));
        for (int i = 0; i < doc.Lines.Count; i++) Assert.Equal(Stored(doc.Lines[i]), Stored(back.Lines[i]));

        // 以前の版（出どころの無いファイル）の表示時刻は手動指定
        var legacy = TwoPages();
        LineExportSettings.Apply(legacy, JsonSerializer.Deserialize<List<LineExportSettings>>("[{\"Index\":3,\"ShowBeginCs\":1400}]"));
        Assert.True(legacy.Lines[3].HasManualShowBegin);
        Assert.True(legacy.Lines[3].HasManualN3Overrides);
    }

    /// <summary>
    /// 1 行ページ 2 枚。2 ページ目の行頭の絵文字（20000〜22000）。前のページの行とは重ならない（自動の計算では 18500 から出る）。
    /// </summary>
    private static LyricsDocument EmojiPages() => Lyrics(
        "@Emoji=（花帆）,a.png",
        "[00:10:00]あ[00:12:00]",
        "",
        "[00:20:00]（花帆）[00:22:00]う[00:23:00]");

    [Fact]
    public void 行に持たせた表示開始が絵文字の開始より後なら書き出しで絵文字を縮める()
    {
        var doc = EmojiPages();
        N3ShowTimeAdjuster.Run(doc, Yield("（花帆）"));
        Assert.Equal(1850, doc.Lines[2].ShowBeginCs);
        // 自動調整の後に、表示開始が 21000 になった（例えば読み込んだ値や、タイムタグを直す前の値）
        doc.Lines[2].ShowBeginCs = 2100;
        var (plans, clamped) = N3EmojiLead.PrepareTab(doc, Yield("（花帆）"));
        Assert.Equal(21000, plans[2].BeginMs);
        Assert.Equal("[00:21:00]（花帆）[00:22:00]う[00:23:00]", LrcFormat.WriteLyricLine(clamped.Lines[2]));
    }

    [Theory]
    [InlineData(ShowTimeOrigin.Auto)]   // 自動調整の後にタイムタグや設定が変わって古くなった値
    [InlineData(ShowTimeOrigin.Loaded)] // 読み込んだ値
    public void チェック_読み込んだ値や古い自動調整の値で絵文字を縮めても知らせない(ShowTimeOrigin origin)
    {
        var doc = EmojiPages();
        var s = Yield("（花帆）");
        N3ShowTimeAdjuster.Run(doc, s);
        doc.Lines[2].ShowBeginCs = 2100; // 絵文字は 2.0→1.0 秒に縮む
        doc.Lines[2].ShowBeginOrigin = origin;
        Assert.Empty(N3ShowTimeValidator.Validate(doc, N3ShowTimePlanner.Plan(doc, s), s));

        // 手で指定した値なら知らせる
        doc.Lines[2].ShowBeginOrigin = ShowTimeOrigin.Manual;
        var warning = Assert.Single(N3ShowTimeValidator.Validate(doc, N3ShowTimePlanner.Plan(doc, s), s));
        Assert.Contains("表示開始の手動指定のため、絵文字が 2.0→1.0 秒に縮みます", warning.Message);
    }

    [Fact]
    public void チェック_行に持たせた表示時刻が歌い出し_歌い終わりからはみ出す行を知らせる()
    {
        var doc = EmojiPages();
        var s = Yield("（花帆）");
        N3ShowTimeAdjuster.Run(doc, s);
        Assert.Empty(N3ShowTimeValidator.Validate(doc, N3ShowTimePlanner.Plan(doc, s), s));

        // 表示開始が歌い出し（絵文字の後ろの文字 22000）より後
        doc.Lines[2].ShowBeginCs = 2250;
        var late = Assert.Single(N3ShowTimeValidator.Validate(doc, N3ShowTimePlanner.Plan(doc, s), s));
        Assert.Equal(IssueSeverity.Warning, late.Severity);
        Assert.Equal("3行目（ページ2）: 表示開始 [00:22:50] が歌い出し [00:22:00] より後です（ワイプが始まってから行が出ます）", late.Message);

        // 表示終了が歌い終わり（23000）より前（前のページの行との組で知らせる行ではない）
        doc.Lines[2].ShowBeginCs = 1850;
        doc.Lines[2].ShowEndCs = 2250;
        var early = Assert.Single(N3ShowTimeValidator.Validate(doc, N3ShowTimePlanner.Plan(doc, s), s));
        Assert.Equal(IssueSeverity.Error, early.Severity);
        Assert.Equal("3行目（ページ2）: 表示終了 [00:22:50] が歌い終わり [00:23:00] より前です（ワイプの途中で消えます）", early.Message);
    }

    [Fact]
    public void チェック_自動調整の値が規則どおりなら自動の計算と同じに知らせる()
    {
        // 前の行のワイプ終了 23000・次の行の絵文字 22000〜24000（歌い出しまで 1.0 秒）: 前の行をワイプの最後まで見せ、絵文字は 1.0 秒
        var doc = Lyrics(
            "@Emoji=（花帆）,a.png",
            "[00:20:00]あ[00:23:00]",
            "[00:21:00]い[00:26:00]",
            "",
            "[00:22:00]（花帆）[00:24:00]う[00:26:00]",
            "[00:27:00]え[00:29:00]");
        var s = Yield("（花帆）");
        string expected = "4行目（ページ1→2・下から2行目）: 前の行（1行目）をワイプの最後まで見せるため、絵文字が 2.0→1.0 秒に縮みます";
        Assert.Equal(expected, Assert.Single(N3ShowTimeValidator.Validate(doc, N3ShowTimePlanner.Plan(doc, s), s)).Message);

        N3ShowTimeAdjuster.Run(doc, s);
        Assert.Equal(expected, Assert.Single(N3ShowTimeValidator.Validate(doc, N3ShowTimePlanner.Plan(doc, s), s)).Message);
    }
}
