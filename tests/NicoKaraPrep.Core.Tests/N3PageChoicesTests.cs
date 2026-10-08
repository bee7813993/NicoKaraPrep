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
