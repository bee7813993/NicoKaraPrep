using System.Text.Json.Nodes;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;
using NicoKaraPrep.Core.Project;

namespace NicoKaraPrep.Core.Tests;

/// <summary>NicoKaraPrep で編集するレイアウト設定（N3Layout）と、行のページのレイアウトの手動指定。</summary>
public class N3LayoutTests
{
    private static LyricsDocument Doc(params string[] lines) => LrcFormat.Parse(string.Join("\r\n", lines) + "\r\n");

    private static N3ProjWriter.TabSource Tab(LyricsDocument doc, string path) =>
        new(new N3ProjExportTab { Name = "メイン", Document = doc, LyricsPath = path }, path, LrcFormat.Write(doc), DateTime.Now);

    private static string Describe(N3LayoutSettings l) =>
        $"{l.Name}#{l.Index} v{l.VerticalAlignment} ls{l.LineSpacePx:0.#} vm{l.VerticalMarginPx:0.#} hm{l.HorizontalMarginPx:0.#} " +
        $"[{string.Join(",", l.HorizontalAlignments)}] sh{l.SmartHorizon} li{l.LyricsIntervalPx:0.#} ri{l.RubyIntervalPx:0.#} lr{l.LyricsAndRubyIntervalPx:0.#} ra{l.RubyAlignment} b{l.AllowBiting}";

    [Fact]
    public void 合わせ方_同じ名前は上書きし無い名前は後ろへ()
    {
        var baseLayouts = N3LayoutReader.Defaults(1080);
        var mine = N3Layout.FromSettings(baseLayouts.First(l => l.Name == "下寄せ2行"));
        mine.LineSpacePx = -10;
        mine.HorizontalAlignments = new List<int> { 1, 1, 1 };
        var added = new N3Layout { Name = "コーラス2行", VerticalAlignment = 0, HorizontalAlignments = new List<int> { 0, 2 } };

        var effective = N3LayoutLibrary.Effective(baseLayouts, new[] { mine, added }, merge: true);
        Assert.Equal(baseLayouts.Select(l => l.Name).Append("コーラス2行"), effective.Select(l => l.Name));
        Assert.Equal(Enumerable.Range(0, effective.Count), effective.Select(l => l.Index));
        var two = effective.First(l => l.Name == "下寄せ2行");
        Assert.Equal(-10, two.LineSpacePx);
        Assert.Equal(3, two.LineCount);

        var plain = N3LayoutLibrary.Effective(baseLayouts, new[] { mine, added }, merge: false);
        Assert.Equal(baseLayouts.Select(Describe), plain.Select(Describe));
    }

    [Fact]
    public void 書き出し_編集したレイアウトがベースに合わさり読み戻すと同じ()
    {
        var doc = Doc("[00:01:00]あ[00:02:00]");
        var baseRoot = N3ProjWriter.BuildProjectJson(@"C:\v\a.n3proj", new[] { Tab(doc, @"C:\v\a.lrc") }, new N3ProjExportOptions(), null, new List<string>(), out _, out _);
        var baseLayouts = N3LayoutReader.Read(baseRoot, 1080);
        string guid = baseRoot["LyricsLayouts"]![1]!["Guid"]!.GetValue<string>();

        var mine = N3Layout.FromSettings(baseLayouts[1]); // 下寄せ2行
        mine.LineSpacePx = -12;
        mine.VerticalMarginPx = 80;
        mine.HorizontalMarginPx = 30;
        mine.SmartHorizon = 1;
        mine.HorizontalAlignments = new List<int> { 0, 1, 2 };
        mine.LyricsIntervalPx = -4;
        mine.AllowBiting = true;
        mine.RubyIntervalPx = 2;
        mine.RubyAlignment = 2;
        mine.LyricsAndRubyIntervalPx = 6;
        var added = new N3Layout { Name = "新しいレイアウト", VerticalAlignment = 1, HorizontalAlignments = new List<int> { 1 }, LineSpacePx = 40 };

        var options = new N3ProjExportOptions { BaseProject = baseRoot.DeepClone().AsObject(), Layouts = new[] { mine, added } };
        var root = N3ProjWriter.BuildProjectJson(@"C:\v\b.n3proj", new[] { Tab(doc, @"C:\v\b.lrc") }, options, options.BaseProject, new List<string>(), out _, out _);
        var written = N3LayoutReader.Read(root, 1080);
        var expected = N3LayoutLibrary.Effective(baseLayouts, new[] { mine, added }, merge: true);
        Assert.Equal(expected.Select(Describe), written.Select(Describe));
        Assert.Equal(guid, root["LyricsLayouts"]![1]!["Guid"]!.GetValue<string>()); // 同じレイアウトを書き換えている
        Assert.Equal(written.Count - 1, root["LyricsLayouts"]![written.Count - 1]!["Index"]!.GetValue<int>());

        // 合わせない設定ならベースのまま
        var keep = new N3ProjExportOptions { BaseProject = baseRoot.DeepClone().AsObject(), Layouts = new[] { mine, added }, MergeLayouts = false };
        var keepRoot = N3ProjWriter.BuildProjectJson(@"C:\v\c.n3proj", new[] { Tab(doc, @"C:\v\c.lrc") }, keep, keep.BaseProject, new List<string>(), out _, out _);
        Assert.Equal(baseLayouts.Select(Describe), N3LayoutReader.Read(keepRoot, 1080).Select(Describe));
    }

    [Fact]
    public void 書き出し_ページのレイアウトの手動指定()
    {
        var doc = Doc(
            "[00:01:00]あ[00:02:00]",
            "[00:02:00]い[00:03:00]",
            "",
            "[00:04:00]う[00:05:00]",
            "[00:05:00]え[00:06:00]");
        doc.Lines[1].LayoutName = "上寄せ2行"; // 1 ページ目の 2 行目に指定 → ページ全体
        doc.Lines[3].LayoutName = "無い名前";   // 無い名前は行数から選ぶ
        var infos = N3LayoutReader.Defaults(1080).Select(l => l.Info).ToList();
        var layouts = new N3ProjWriter.LayoutResolver(infos, null, null, null, new List<string>(), "t");
        var action = new N3SubtitleAction("SHINTA.CharFadeInFadeOut", new JsonObject { ["$type"] = "CharFadeInFadeOutSettingsModel" });
        var lines = N3ProjWriter.BuildLineInfos(doc, new N3ShowTimeSettings(), doc.EmojiEntries, new N3FontResolver(new[] { "標準" }, null, true), layouts, action, "Ver 13.79", out _);
        var indexes = lines.Where(l => l!["Kind"]!.GetValue<int>() == 1).Select(l => l!["LayoutIndex"]!.GetValue<int>()).ToList();
        int top2 = infos.First(i => i.Name == "上寄せ2行").Index;
        int bottom2 = infos.First(i => i.Name == "下寄せ2行").Index;
        Assert.Equal(new[] { top2, top2, bottom2, bottom2 }, indexes);
        Assert.Equal(top2, layouts.FindIndex("上寄せ2行"));
        Assert.Null(layouts.FindIndex("無い名前"));
    }

    [Fact]
    public void 行の設定_レイアウトの手動指定を保存して読み戻す()
    {
        var doc = Doc("[00:01:00]あ[00:02:00]", "[00:02:00]い[00:03:00]");
        doc.Lines[1].LayoutName = "上寄せ2行";
        Assert.True(doc.Lines[1].HasN3Overrides);
        Assert.Equal("上寄せ2行", doc.Lines[1].Clone().LayoutName);
        var saved = LineExportSettings.Collect(doc);
        var s = Assert.Single(saved);
        Assert.Equal("上寄せ2行", s.LayoutName);

        var reloaded = Doc("[00:01:00]あ[00:02:00]", "[00:02:00]い[00:03:00]");
        LineExportSettings.Apply(reloaded, saved);
        Assert.Equal("上寄せ2行", reloaded.Lines[1].LayoutName);
        Assert.Null(reloaded.Lines[0].LayoutName);
    }

    [Fact]
    public void 編集用の複製は行ごとの左右を別に持つ()
    {
        var a = new N3Layout { Name = "a", HorizontalAlignments = new List<int> { 0, 2 } };
        var b = a.Clone();
        b.HorizontalAlignments.Add(1);
        Assert.Equal(2, a.HorizontalAlignments.Count);
        Assert.NotEqual(a.Id, new N3Layout().Id);
        a.HorizontalAlignments = new List<int>();
        Assert.Equal(new[] { 1 }, a.HorizontalAlignments); // 空にはしない
    }
}
