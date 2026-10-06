using System.Text.Json.Nodes;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Tests;

/// <summary>ページごとのレイアウト・字幕アクションの決まり方（レイアウト設定ビューのページの一覧と書き出しで共通）。</summary>
public class N3PageChoicesTests
{
    private static LyricsDocument Doc(params string[] lines) => LrcFormat.Parse(string.Join("\r\n", lines) + "\r\n");

    private static LyricsDocument FourLines() => Doc(
        "[00:01:00]あ[00:02:00]",
        "[00:02:00]い[00:03:00]",
        "",
        "[00:04:00]う[00:05:00]",
        "[00:05:00]え[00:06:00]",
        "[00:06:00]お[00:07:00]");

    private static List<N3ProjLayoutInfo> Infos() => N3LayoutReader.Defaults(1080).Select(l => l.Info).ToList();

    private static N3ProjWriter.LayoutResolver Resolver(string? fixedName = null) =>
        new(Infos(), fixedName, null, null, new List<string>(), "t");

    [Fact]
    public void レイアウト_手動指定はページの最初の有効な名前で_無い名前は飛ばす()
    {
        var doc = FourLines();
        doc.Lines[3].LayoutName = "無い名前";
        doc.Lines[4].LayoutName = "上寄せ2行";
        doc.Lines[5].LayoutName = "下寄せ1行";
        var pages = doc.GetPages(PageSplitMode.EmptyLine);
        var resolver = Resolver();

        var first = N3PageChoices.ChooseLayout(doc, pages[0], resolver);
        Assert.Equal(N3PageLayoutSource.Auto, first.Source);
        Assert.Null(first.ManualName);
        Assert.Equal(resolver.Resolve(2), first.LayoutIndex);

        var second = N3PageChoices.ChooseLayout(doc, pages[1], resolver);
        Assert.Equal(N3PageLayoutSource.Manual, second.Source);
        Assert.Equal("上寄せ2行", second.ManualName);
        Assert.Equal(resolver.FindIndex("上寄せ2行"), second.LayoutIndex);
    }

    [Fact]
    public void レイアウト_タブの固定は手動指定より後で_無い名前の固定は自動()
    {
        var doc = FourLines();
        doc.Lines[4].LayoutName = "上寄せ2行";
        var pages = doc.GetPages(PageSplitMode.EmptyLine);

        var fixedResolver = Resolver("下寄せ1行");
        Assert.True(fixedResolver.IsFixed);
        var first = N3PageChoices.ChooseLayout(doc, pages[0], fixedResolver);
        Assert.Equal(N3PageLayoutSource.TabFixed, first.Source);
        Assert.Equal(fixedResolver.FindIndex("下寄せ1行"), first.LayoutIndex);
        Assert.Equal(N3PageLayoutSource.Manual, N3PageChoices.ChooseLayout(doc, pages[1], fixedResolver).Source);

        var missing = Resolver("無い名前");
        Assert.False(missing.IsFixed);
        Assert.Equal(N3PageLayoutSource.Auto, N3PageChoices.ChooseLayout(doc, pages[0], missing).Source);
    }

    [Fact]
    public void レイアウト_書き出しの行のレイアウト番号と同じ()
    {
        var doc = FourLines();
        doc.Lines[1].LayoutName = "上寄せ2行";
        var resolver = Resolver();
        var show = new N3ShowTimeSettings();
        var action = new N3SubtitleAction("SHINTA.CharFadeInFadeOut", new JsonObject { ["$type"] = "CharFadeInFadeOutSettingsModel" });
        var lines = N3ProjWriter.BuildLineInfos(doc, show, doc.EmojiEntries, new N3FontResolver(new[] { "標準" }, null, true), resolver, action, "Ver 13.79", out _);
        var written = lines.Where(l => l!["Kind"]!.GetValue<int>() == 1).Select(l => l!["LayoutIndex"]!.GetValue<int>()).ToList();

        var expected = new List<int>();
        foreach (var page in doc.GetPages(show.PageMode, show.FixedLineCount))
        {
            int index = N3PageChoices.ChooseLayout(doc, page, resolver).LayoutIndex;
            expected.AddRange(page.Select(_ => index));
        }
        Assert.Equal(expected, written);
    }

    [Fact]
    public void アクション_既定_手動_混在()
    {
        var doc = FourLines();
        var pages = doc.GetPages(PageSplitMode.EmptyLine);
        var none = N3PageChoices.SummarizeActions(doc, pages[0]);
        Assert.Equal(N3PageActionState.Default, none.State);
        Assert.Null(none.Action);
        Assert.Equal(2, none.DefaultLines);

        // 同じ設定値なら別のオブジェクトでも同じ指定
        doc.Lines[0].SubtitleAction = N3SubtitleActionCatalog.CreateDefault(N3SubtitleActionCatalog.LineFadeInId);
        doc.Lines[1].SubtitleAction = N3SubtitleActionCatalog.CreateDefault(N3SubtitleActionCatalog.LineFadeInId);
        var manual = N3PageChoices.SummarizeActions(doc, pages[0]);
        Assert.Equal(N3PageActionState.Manual, manual.State);
        Assert.Equal(N3SubtitleActionCatalog.LineFadeInId, manual.Action!.Id);
        Assert.Equal(2, manual.ManualLines);

        // 指定のある行と無い行
        doc.Lines[3].SubtitleAction = N3SubtitleActionCatalog.CreateDefault(N3SubtitleActionCatalog.NoActionId);
        var partly = N3PageChoices.SummarizeActions(doc, pages[1]);
        Assert.Equal(N3PageActionState.Mixed, partly.State);
        Assert.Equal((1, 2, 1), (partly.ManualLines, partly.DefaultLines, partly.ManualKinds));

        // 違うアクション・同じ Id で違う値
        doc.Lines[4].SubtitleAction = N3SubtitleActionCatalog.CreateDefault(N3SubtitleActionCatalog.LineFadeOutId);
        doc.Lines[5].SubtitleAction = N3SubtitleActionCatalog.CreateDefault(N3SubtitleActionCatalog.LineFadeOutId);
        doc.Lines[5].SubtitleAction!.Set("FadeOutTime", 500);
        var mixed = N3PageChoices.SummarizeActions(doc, pages[1]);
        Assert.Equal(N3PageActionState.Mixed, mixed.State);
        Assert.Equal(3, mixed.ManualKinds);
        Assert.Equal(0, mixed.DefaultLines);

        // Id が空の指定は指定なし（書き出しと同じ）
        doc.Lines[0].SubtitleAction = new N3SubtitleAction("", null);
        doc.Lines[1].SubtitleAction = null;
        Assert.Equal(N3PageActionState.Default, N3PageChoices.SummarizeActions(doc, pages[0]).State);
    }

    [Fact]
    public void ページへ広げる_空行と範囲の外は飛ばし同じページの行をそろえる()
    {
        var doc = FourLines();
        Assert.Equal(new[] { 3, 4, 5 }, N3PageChoices.ExpandToPages(doc, new[] { 4 }, PageSplitMode.EmptyLine, 2));
        Assert.Equal(new[] { 0, 1, 3, 4, 5 }, N3PageChoices.ExpandToPages(doc, new[] { 5, 0, 2, 99, -1 }, PageSplitMode.EmptyLine, 2));
        Assert.Empty(N3PageChoices.ExpandToPages(doc, new[] { 2 }, PageSplitMode.EmptyLine, 2));

        // 固定行数（空行は数えない: [0,1] [3,4] [5]）
        Assert.Equal(new[] { 3, 4 }, N3PageChoices.ExpandToPages(doc, new[] { 3 }, PageSplitMode.FixedLineCount, 2));
        Assert.Equal(new[] { 5 }, N3PageChoices.ExpandToPages(doc, new[] { 5 }, PageSplitMode.FixedLineCount, 2));
    }
}
