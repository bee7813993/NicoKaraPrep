using System.Text.Json.Nodes;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Tests;

public class N3FontResolverTests
{
    private static readonly string[] Names = { "標準", "（麻衣）", "（のりこ）", "（麻衣）（のりこ）" };

    private static LyricsDocument Doc(params string[] lines) => LrcFormat.Parse(string.Join("\r\n", lines) + "\r\n");

    /// <summary>行の各文字のフォント番号を 1 文字ずつ並べる（2 連タグ用スペーサーは "_"）。</summary>
    private static string Fonts(LyricsLine line, int[] fonts) =>
        string.Concat(line.Chars.Select((c, i) => c.IsSpacer ? "_" : fonts[i].ToString()));

    /// <summary>文書の歌詞行を順に解決し、行ごとのフォント番号の並びを返す（空行は ""）。</summary>
    private static List<string> ResolveAll(LyricsDocument doc, IReadOnlyList<string>? names = null, string? defaultName = null, bool continueAcrossLines = true)
    {
        var resolver = new N3FontResolver(names ?? Names, defaultName, continueAcrossLines);
        return doc.Lines.Select(l => l.IsEmpty ? "" : Fonts(l, resolver.Resolve(l))).ToList();
    }

    // ------------------------------------------------------------ 照合

    [Fact]
    public void 照合_名前が現れた位置から切り替わる()
    {
        var doc = Doc("[00:01:00]あ（麻衣）い[00:02:00]");
        Assert.Equal(new[] { "011111" }, ResolveAll(doc));
    }

    [Fact]
    public void 照合_長い名前を優先する()
    {
        var doc = Doc(
            "[00:01:00]（麻衣）（のりこ）踊[00:02:00]",
            "[00:03:00]（麻衣）歌（のりこ）[00:04:00]");
        Assert.Equal(new[] { "3333333333", "1111122222" }, ResolveAll(doc));
    }

    [Fact]
    public void 照合_スペーサーを挟んだ結合名が当たる()
    {
        // 絵文字を続けて挿入したときの 2 連タグ（ニコカラメーカー3 はタイムタグを飛ばして照合する）
        var doc = Doc("[00:01:00]（麻衣）[00:02:00][00:01:00]（のりこ）[00:02:00]踊[00:03:00]");
        Assert.Contains(doc.Lines[0].Chars, c => c.IsSpacer);
        Assert.Equal(new[] { "3333_333333" }, ResolveAll(doc));
    }

    [Fact]
    public void 書き出し_スペーサーを挟んだ連続絵文字はどちらも結合名のフォントになる()
    {
        var doc = Doc("@Emoji=（麻衣）,a.png", "@Emoji=（のりこ）,b.png",
            "[00:01:00]（麻衣）[00:02:00][00:01:00]（のりこ）[00:02:00]踊[00:03:00]");
        var layouts = new N3ProjWriter.LayoutResolver(new List<N3ProjLayoutInfo> { new("下寄せ2行", 0, 2) }, null, null, null, new List<string>(), "t");
        var action = ("SHINTA.CharFadeInFadeOut", new JsonObject { ["$type"] = "CharFadeInFadeOutSettingsModel" });
        var lines = N3ProjWriter.BuildLineInfos(doc, new N3ShowTimeSettings(), doc.EmojiEntries, new N3FontResolver(Names, null, true), layouts, action, "Ver 13.79", out _);

        var chars = lines[0]!["LyricsCharInfos"]!.AsArray();
        Assert.Equal(new[] { "（麻衣）:3", "（のりこ）:3", "踊:3" }, chars.Select(c => $"{c!["Char"]}:{c["FontIndex"]}"));
    }

    [Fact]
    public void 照合_スペーサーを挟んでも結合名が無ければそれぞれの名前が当たる()
    {
        var doc = Doc("[00:01:00]（麻衣）[00:02:00][00:01:00]（のりこ）[00:02:00]踊[00:03:00]");
        Assert.Equal(new[] { "1111_222222" }, ResolveAll(doc, new[] { "標準", "（麻衣）", "（のりこ）" }));
    }

    [Fact]
    public void 照合_名前の途中や行頭のスペーサーも飛ばす()
    {
        var doc = Doc(
            "[00:01:00]（麻[00:01:20][00:01:30]衣）歌[00:02:00]",
            "[00:02:50][00:03:00]（のりこ）え[00:04:00]");
        Assert.Equal(new[] { "11_111", "_222222" }, ResolveAll(doc));
    }

    [Fact]
    public void 集計_スペーサーを挟んだ結合名も数える()
    {
        var doc = Doc("[00:01:00]（麻衣）[00:02:00][00:01:00]（のりこ）[00:02:00]踊[00:03:00]");
        var usage = N3FontResolver.CountUsage(doc, Names, null, true);
        Assert.Equal(10, usage["（麻衣）（のりこ）"]);
        Assert.Equal(0, usage["（麻衣）"]);
        Assert.Equal(new[] { 0 }, N3FontResolver.LinesUsing(doc, Names, null, true, "（麻衣）（のりこ）"));
    }

    [Fact]
    public void 照合_同じ名前が複数あると先の番号を使う()
    {
        var doc = Doc("[00:01:00]（麻衣）あ[00:02:00]");
        Assert.Equal(new[] { "11111" }, ResolveAll(doc, new[] { "標準", "（麻衣）", "（麻衣）" }));
    }

    // ------------------------------------------------------------ 行をまたいだ継続・既定

    [Fact]
    public void 継続_行が変わってもフォントを維持する()
    {
        var doc = Doc("[00:01:00]（麻衣）あ[00:02:00]", "", "[00:03:00]い[00:04:00]");
        Assert.Equal(new[] { "11111", "", "1" }, ResolveAll(doc));
    }

    [Fact]
    public void 継続_維持しないときは行ごとに既定へ戻す()
    {
        var doc = Doc("[00:01:00]（麻衣）あ[00:02:00]", "[00:03:00]い[00:04:00]");
        Assert.Equal(new[] { "11111", "0" }, ResolveAll(doc, continueAcrossLines: false));
        Assert.Equal(new[] { "11111", "2" }, ResolveAll(doc, defaultName: "（のりこ）", continueAcrossLines: false));
    }

    [Fact]
    public void 既定_無い名前を既定にすると0番を使う()
    {
        var doc = Doc("[00:01:00]あ[00:02:00]");
        Assert.Equal(new[] { "0" }, ResolveAll(doc, defaultName: "無い"));
    }

    // ------------------------------------------------------------ 行の手動指定

    [Fact]
    public void 手動指定_行全体をそのフォントにし行内のパート記号は次の行に効く()
    {
        var doc = Doc("[00:01:00]あ（麻衣）い[00:02:00]", "[00:03:00]う[00:04:00]");
        doc.Lines[0].FontSetName = "（のりこ）";
        Assert.Equal(new[] { "222222", "1" }, ResolveAll(doc));
    }

    [Fact]
    public void 手動指定_存在しない名前は無視する()
    {
        var doc = Doc("[00:01:00]あ（麻衣）い[00:02:00]");
        doc.Lines[0].FontSetName = "無い";
        Assert.Equal(new[] { "011111" }, ResolveAll(doc));
    }

    // ------------------------------------------------------------ 集計

    [Fact]
    public void 集計_フォントごとの文字数を数える()
    {
        var doc = Doc(
            "[00:01:00]前（麻衣）あい[00:02:00]",
            "[00:03:00]う[00:04:00][00:04:10]え[00:05:00]", // 2 連タグのスペーサーは数えない
            "",
            "[00:06:00]（のりこ）お[00:07:00]");
        var usage = N3FontResolver.CountUsage(doc, Names, null, true);
        Assert.Equal(1, usage["標準"]);
        Assert.Equal(8, usage["（麻衣）"]);
        Assert.Equal(6, usage["（のりこ）"]);
        Assert.Equal(0, usage["（麻衣）（のりこ）"]);
    }

    [Fact]
    public void 集計_複数の文書では前の文書のフォントを引き継ぐ()
    {
        var main = Doc("[00:01:00]（麻衣）あ[00:02:00]");
        var chorus = Doc("[00:03:00]い[00:04:00]");
        var usage = N3FontResolver.CountUsage(new[] { main, chorus }, Names, null, true);
        Assert.Equal(0, usage["標準"]);
        Assert.Equal(6, usage["（麻衣）"]);
    }

    [Fact]
    public void 集計_複数の文書ではフォントを使う行も前の文書のフォントを引き継いで返す()
    {
        var main = Doc("[00:01:00]（麻衣）あ[00:02:00]", "[00:03:00]（のりこ）い[00:04:00]");
        var chorus = Doc("[00:05:00]う[00:06:00]", "", "[00:07:00]（麻衣）え[00:08:00]");
        var docs = new[] { main, chorus };
        Assert.Equal(new[] { (0, 1), (1, 0) }, N3FontResolver.LinesUsing(docs, Names, null, true, "（のりこ）"));
        Assert.Equal(new[] { (0, 0), (1, 2) }, N3FontResolver.LinesUsing(docs, Names, null, true, "（麻衣）"));
        Assert.Empty(N3FontResolver.LinesUsing(docs, Names, null, true, "標準"));
        // 文書 1 つだけなら既定のフォントから始める
        Assert.Equal(new[] { 0 }, N3FontResolver.LinesUsing(chorus, Names, null, true, "標準"));
    }

    [Fact]
    public void 集計_フォントを使う行の一覧()
    {
        var doc = Doc(
            "[00:01:00]前（麻衣）あ[00:02:00]",
            "",
            "[00:03:00]い[00:04:00]",
            "[00:05:00]（のりこ）う[00:06:00]");
        Assert.Equal(new[] { 0, 2 }, N3FontResolver.LinesUsing(doc, Names, null, true, "（麻衣）"));
        Assert.Equal(new[] { 0 }, N3FontResolver.LinesUsing(doc, Names, null, true, "標準"));
        Assert.Equal(new[] { 3 }, N3FontResolver.LinesUsing(doc, Names, null, true, "（のりこ）"));
        Assert.Empty(N3FontResolver.LinesUsing(doc, Names, null, true, "（麻衣）（のりこ）"));
        Assert.Empty(N3FontResolver.LinesUsing(doc, Names, null, true, "無い"));
    }

    [Fact]
    public void 集計_フォントが無ければ空()
    {
        var doc = Doc("[00:01:00]あ[00:02:00]");
        Assert.Empty(N3FontResolver.CountUsage(doc, Array.Empty<string>(), null, true));
        Assert.Empty(N3FontResolver.LinesUsing(doc, Array.Empty<string>(), null, true, "標準"));
        Assert.Equal(new[] { "0" }, ResolveAll(doc, Array.Empty<string>()));
        Assert.Empty(N3FontResolver.ResolveLines(new[] { doc }, Array.Empty<string>(), null, true)[0][0].Runs);
    }

    // ------------------------------------------------------------ 行ごとの一覧（行リストの表示）

    [Fact]
    public void 行ごと_切り替わった順に並び次の行へ引き継ぐ()
    {
        var doc = Doc(
            "[00:01:00]あ[00:02:00]",
            "[00:03:00]（麻衣）（のりこ）歌[00:04:00]",
            "",
            "[00:05:00]続き[00:06:00]",
            "[00:07:00]い（麻衣）う（のりこ）え[00:08:00]");
        var lines = N3FontResolver.ResolveLines(new[] { doc }, Names, null, true)[0];
        Assert.Equal(5, lines.Count);
        Assert.Equal(new[] { 0 }, lines[0].Runs);
        Assert.Equal(new[] { 3 }, lines[1].Runs); // 記号が続く箇所は組み合わせのフォント設定だけ
        Assert.Empty(lines[2].Runs);
        Assert.Equal(new[] { 3 }, lines[3].Runs);
        Assert.Equal(new[] { 3, 1, 2 }, lines[4].Runs);
        Assert.All(lines, l => Assert.False(l.Manual));
    }

    [Fact]
    public void 行ごと_手動指定は行全体をそろえ次の行の引き継ぎには影響しない()
    {
        var doc = Doc(
            "[00:01:00]（麻衣）あ[00:02:00]",
            "[00:03:00]い（のりこ）う[00:04:00]",
            "[00:05:00]え[00:06:00]");
        doc.Lines[1].FontSetName = "（麻衣）（のりこ）";
        var lines = N3FontResolver.ResolveLines(new[] { doc }, Names, null, true)[0];
        Assert.Equal(new[] { 3 }, lines[1].Runs);
        Assert.True(lines[1].Manual);
        Assert.Equal(new[] { 2 }, lines[2].Runs); // 手動指定の行の中の記号は、そのあとの行に効く（書き出しと同じ）
    }

    [Fact]
    public void 行ごと_無い名前の手動指定は使われない()
    {
        var doc = Doc("[00:01:00]（麻衣）あ[00:02:00]");
        doc.Lines[0].FontSetName = "（無い名前）";
        var line = N3FontResolver.ResolveLines(new[] { doc }, Names, null, true)[0][0];
        Assert.Equal(new[] { 1 }, line.Runs);
        Assert.False(line.Manual);
    }

    [Fact]
    public void 行ごと_スペーサーは数えず前の文書のフォントを引き継ぐ()
    {
        // 行頭のスペーサーは前の行のフォントのままだが、文字ではないので並びに入れない
        var main = Doc("[00:01:00]（麻衣）あ[00:02:00]", "[00:02:50][00:03:00]（のりこ）え[00:04:00]");
        var chorus = Doc("[00:05:00]お[00:06:00]");
        var result = N3FontResolver.ResolveLines(new[] { main, chorus }, Names, "（麻衣）", true);
        Assert.Equal(new[] { 2 }, result[0][1].Runs);
        Assert.Equal(new[] { 2 }, result[1][0].Runs);
    }

    [Fact]
    public void 行ごと_書き出しの文字のフォントと同じ()
    {
        var doc = Doc("@Emoji=（麻衣）,a.png", "@Emoji=（のりこ）,b.png",
            "[00:01:00]前[00:01:50]（麻衣）[00:02:00]歌[00:03:00]",
            "",
            "[00:04:00]（のりこ）[00:04:50]詞[00:05:00]",
            "[00:06:00]続[00:07:00]");
        var layouts = new N3ProjWriter.LayoutResolver(new List<N3ProjLayoutInfo> { new("下寄せ2行", 0, 2) }, null, null, null, new List<string>(), "t");
        var action = ("SHINTA.CharFadeInFadeOut", new JsonObject { ["$type"] = "CharFadeInFadeOutSettingsModel" });
        var written = N3ProjWriter.BuildLineInfos(doc, new N3ShowTimeSettings(), doc.EmojiEntries, new N3FontResolver(Names, null, true), layouts, action, "Ver 13.79", out _)
            .Where(l => l!["Kind"]!.GetValue<int>() == 1)
            .Select(l => l!["LyricsCharInfos"]!.AsArray().Select(c => c!["FontIndex"]!.GetValue<int>()).Distinct().ToArray())
            .ToList();
        var resolved = N3FontResolver.ResolveLines(new[] { doc }, Names, null, true)[0].Where(l => l.Runs.Count > 0).Select(l => l.Runs.ToArray()).ToList();
        Assert.Equal(written, resolved);
        Assert.Equal(new[] { 0, 1 }, resolved[0]);
    }
}
