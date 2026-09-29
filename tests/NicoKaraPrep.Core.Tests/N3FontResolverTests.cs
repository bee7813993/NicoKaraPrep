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
    }
}
