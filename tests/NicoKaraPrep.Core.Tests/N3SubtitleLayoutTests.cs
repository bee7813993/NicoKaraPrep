using System.Text.Json.Nodes;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Tests;

/// <summary>字幕のプレビュー用: レイアウト設定の読み込みとワイプの時刻。</summary>
public class N3SubtitleLayoutTests
{
    private static LyricsDocument Doc(params string[] lines) => LrcFormat.Parse(string.Join("\r\n", lines) + "\r\n");

    private static string Describe(IEnumerable<N3WipeTimeline.Group> groups, LyricsLine line) =>
        string.Join(" / ", groups.Select(g =>
            string.Concat(line.Chars.Skip(g.FirstUnit).Take(g.LastUnit - g.FirstUnit + 1).Where(c => !c.IsSpacer).Select(c => c.Text)) + $":{g.StartCs}-{g.EndCs}"));

    // ------------------------------------------------------------ レイアウト設定

    [Fact]
    public void レイアウト_ベースが無いときの既定()
    {
        var layouts = N3LayoutReader.Defaults(1080);
        Assert.Equal(new[] { "下寄せ1行", "下寄せ2行", "下寄せ3行", "上寄せ2行", "コーラス", "タイトル左上", "タイトル中央" }, layouts.Select(l => l.Name));
        var two = layouts[1];
        Assert.Equal(1, two.Index);
        Assert.Equal(2, two.VerticalAlignment);
        Assert.Equal(60, two.LineSpacePx);
        Assert.Equal(50, two.VerticalMarginPx);
        Assert.Equal(50, two.HorizontalMarginPx);
        Assert.Equal(new[] { 0, 2 }, two.HorizontalAlignments);
        Assert.Equal(2, two.SmartHorizon);
        Assert.Equal(2, two.LineCount);
    }

    [Fact]
    public void レイアウト_書き出したプロジェクトから読める()
    {
        var doc = Doc("[00:01:00]あ[00:02:00]");
        var tab = new N3ProjWriter.TabSource(new N3ProjExportTab { Name = "メイン", Document = doc, LyricsPath = @"C:\v\a.lrc" }, @"C:\v\a.lrc", LrcFormat.Write(doc), DateTime.Now);
        var root = N3ProjWriter.BuildProjectJson(@"C:\v\a.n3proj", new[] { tab }, new N3ProjExportOptions(), null, new List<string>(), out _, out _);
        var read = N3LayoutReader.Read(root, 1080);
        Assert.Equal(N3LayoutReader.Defaults(1080).Select(l => (l.Name, l.LineCount, l.VerticalAlignment)), read.Select(l => (l.Name, l.LineCount, l.VerticalAlignment)));

        // 画面の高さが違えば px も変わる（比率で持っているため）
        var half = N3LayoutReader.Read(root, 540);
        Assert.Equal(25, half[1].VerticalMarginPx);
    }

    [Fact]
    public void レイアウト_行ごとの左右配置()
    {
        var layouts = N3LayoutReader.Defaults(1080);
        var bottom2 = layouts.First(l => l.Name == "下寄せ2行");
        Assert.Equal(2, bottom2.AlignmentForRow(1, 2)); // 下の行は右寄せ
        Assert.Equal(0, bottom2.AlignmentForRow(2, 2)); // 上の行は左寄せ
        Assert.Equal(0, bottom2.AlignmentForRow(3, 3)); // 設定より多い行は上端と同じ

        var top2 = layouts.First(l => l.Name == "上寄せ2行");
        Assert.Equal(0, top2.AlignmentForRow(2, 2)); // 上の行（下から 2 行目）は左寄せ
        Assert.Equal(2, top2.AlignmentForRow(1, 2));
    }

    [Fact]
    public void レイアウト_ページの行数で選ぶ()
    {
        var infos = N3LayoutReader.Defaults(1080).Select(l => l.Info).ToList();
        var resolver = new N3ProjWriter.LayoutResolver(infos, null, null, null, new List<string>(), "メイン");
        Assert.Equal("下寄せ2行", infos[resolver.Resolve(2)].Name);
        Assert.Equal("下寄せ1行", infos[resolver.Resolve(1)].Name);
        Assert.Equal("下寄せ3行", infos[resolver.Resolve(3)].Name);
    }

    // ------------------------------------------------------------ ワイプの時刻

    [Fact]
    public void ワイプ_タグからタグまでがまとまり()
    {
        var line = Doc("[00:01:00]あい[00:02:00]う[00:03:00]").Lines[0];
        Assert.Equal("あい:100-200 / う:200-300", Describe(N3WipeTimeline.Groups(line), line));
    }

    [Fact]
    public void ワイプ_行頭のタグの無い文字は最初のタグで一度に()
    {
        var line = Doc("あ[00:01:00]い[00:02:00]").Lines[0];
        Assert.Equal("あ:100-100 / い:100-200", Describe(N3WipeTimeline.Groups(line), line));
    }

    [Fact]
    public void ワイプ_2連タグの最初のタグはまとまりの終わり()
    {
        var line = Doc("[00:01:00]あ[00:02:00][00:02:50]い[00:03:00]").Lines[0];
        Assert.Equal("あ:100-200 / い:250-300", Describe(N3WipeTimeline.Groups(line), line));
    }

    [Fact]
    public void ワイプ_同時に始まる絵文字()
    {
        var line = Doc("[00:02:94]（麻衣）[00:04:94][00:02:94]（のりこ）[00:04:94]Wan[00:05:05]").Lines[0];
        Assert.Equal("（麻衣）:294-494 / （のりこ）:294-494 / Wan:494-505", Describe(N3WipeTimeline.Groups(line), line));
    }

    [Fact]
    public void ワイプ_進み具合()
    {
        var g = new N3WipeTimeline.Group(0, 1, 100, 200);
        Assert.Equal(0, N3WipeTimeline.Progress(g, 50));
        Assert.Equal(0.5, N3WipeTimeline.Progress(g, 150));
        Assert.Equal(1, N3WipeTimeline.Progress(g, 250));
        Assert.Equal(1, N3WipeTimeline.Progress(new N3WipeTimeline.Group(0, 0, 100, 100), 100));
        Assert.Equal(0, N3WipeTimeline.Progress(new N3WipeTimeline.Group(0, 0, 100, 100), 99));
    }
}
