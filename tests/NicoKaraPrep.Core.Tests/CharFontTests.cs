using System.Text.Json.Nodes;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;
using NicoKaraPrep.Core.Project;

namespace NicoKaraPrep.Core.Tests;

/// <summary>文字単位のフォント設定名の手動指定（CharUnit.FontSetName）。</summary>
public class CharFontTests
{
    private static readonly string[] Names = { "標準", "（麻衣）", "（のりこ）", "（麻衣）（のりこ）", "（コーラス）" };

    private static LyricsDocument Doc(params string[] lines) => LrcFormat.Parse(string.Join("\r\n", lines) + "\r\n");

    /// <summary>行の表示文字ごとの手動指定（無ければ "-"）を "|" でつなぐ。</summary>
    private static string Fonts(LyricsLine line) =>
        string.Join("|", CharFontOperations.DisplayUnits(line).Select(c => c.FontSetName ?? "-"));

    /// <summary>行の表示文字ごとのフォント番号（スペーサーを除く）。</summary>
    private static string Resolved(LyricsLine line, int[] fonts) =>
        string.Concat(line.Chars.Select((c, i) => c.IsSpacer ? "" : fonts[i].ToString()));

    // ------------------------------------------------------------ 範囲の指定と保存

    [Fact]
    public void 範囲_指定した文字だけに付きスペーサーも含める()
    {
        var line = Doc("[00:01:00]あ[00:02:00][00:02:00]い[00:03:00]う[00:04:00]").Lines[0];
        Assert.Contains(line.Chars, c => c.IsSpacer);
        int spacer = line.Chars.FindIndex(c => c.IsSpacer);
        int changed = CharFontOperations.SetRange(line, spacer - 1, spacer + 1, "（麻衣）");
        Assert.Equal(2, changed); // スペーサーは数えない
        Assert.Equal("（麻衣）|（麻衣）|-", Fonts(line));
        Assert.Equal("（麻衣）", line.Chars[spacer].FontSetName);
        Assert.True(line.HasCharFonts);
        Assert.True(line.HasN3Overrides);

        Assert.Equal(0, CharFontOperations.SetRange(line, spacer - 1, spacer + 1, "（麻衣）")); // 同じ指定は変わらない
        Assert.Equal(1, CharFontOperations.SetRange(line, spacer + 1, spacer + 1, null)); // 「い」だけ自動に戻す
        Assert.Equal("（麻衣）|-|-", Fonts(line));
    }

    [Fact]
    public void 保存_範囲にまとめて同じ歌詞なら読み戻せる()
    {
        var doc = Doc("[00:01:00]あいうえお[00:02:00]", "[00:03:00]かきく[00:04:00]");
        var units = CharFontOperations.DisplayUnits(doc.Lines[0]);
        units[1].FontSetName = "（麻衣）";
        units[2].FontSetName = "（麻衣）";
        units[4].FontSetName = "（コーラス）";

        var saved = LineExportSettings.Collect(doc);
        var s = Assert.Single(saved);
        Assert.Equal(0, s.Index);
        Assert.Equal("あいうえお", s.CharText);
        Assert.Equal(new[] { (1, 2, "（麻衣）"), (4, 1, "（コーラス）") }, s.CharFonts!.Select(r => (r.Start, r.Length, r.Name)));

        var reloaded = Doc("[00:01:00]あいうえお[00:02:00]", "[00:03:00]かきく[00:04:00]");
        LineExportSettings.Apply(reloaded, saved);
        Assert.Equal("-|（麻衣）|（麻衣）|-|（コーラス）", Fonts(reloaded.Lines[0]));

        // 歌詞が外で変わっていたら文字の指定は当てない（別の文字へ付かないように）
        var changed = Doc("[00:01:00]あいXうえお[00:02:00]", "[00:03:00]かきく[00:04:00]");
        LineExportSettings.Apply(changed, saved);
        Assert.False(changed.Lines[0].HasCharFonts);
    }

    [Fact]
    public void 複製と名前の変更と解除()
    {
        var line = Doc("[00:01:00]あい[00:02:00]").Lines[0];
        CharFontOperations.SetRange(line, 0, 1, "（麻衣）");
        var copy = line.Clone();
        Assert.Equal("（麻衣）|（麻衣）", Fonts(copy));

        Assert.Equal(2, CharFontOperations.Rename(copy, "（麻衣）", "（まい）"));
        Assert.Equal("（まい）|（まい）", Fonts(copy));
        Assert.Equal("（麻衣）|（麻衣）", Fonts(line)); // 元の行は別

        Assert.True(CharFontOperations.Clear(copy));
        Assert.False(copy.HasCharFonts);
        Assert.False(CharFontOperations.Clear(copy));
    }

    // ------------------------------------------------------------ 書き換えたときの引き継ぎ

    [Fact]
    public void 書き換え_同じ文字へ写し範囲の途中に足した文字も同じ名前()
    {
        var old = Doc("[00:01:00]あいうえお[00:02:00]").Lines[0];
        var units = CharFontOperations.DisplayUnits(old);
        units[1].FontSetName = "（麻衣）";
        units[2].FontSetName = "（麻衣）";
        units[3].FontSetName = "（麻衣）";

        var edited = Doc("[00:01:00]あいXうZえお[00:02:00]").Lines[0];
        CharFontOperations.CopyCharFonts(old, edited);
        Assert.Equal("-|（麻衣）|（麻衣）|（麻衣）|（麻衣）|（麻衣）|-", Fonts(edited));

        // 範囲の端の外に足した文字は付けない
        var outside = Doc("[00:01:00]Yあいうえお[00:02:00]").Lines[0];
        CharFontOperations.CopyCharFonts(old, outside);
        Assert.Equal("-|-|（麻衣）|（麻衣）|（麻衣）|-", Fonts(outside));

        // 消した文字の分は詰まる
        var removed = Doc("[00:01:00]あうえお[00:02:00]").Lines[0];
        CharFontOperations.CopyCharFonts(old, removed);
        Assert.Equal("-|（麻衣）|（麻衣）|-", Fonts(removed));
    }

    [Fact]
    public void 書き換え_指定が無ければ何もしない()
    {
        var old = Doc("[00:01:00]あい[00:02:00]").Lines[0];
        var edited = Doc("[00:01:00]あいう[00:02:00]").Lines[0];
        CharFontOperations.CopyCharFonts(old, edited);
        Assert.False(edited.HasCharFonts);
    }

    // ------------------------------------------------------------ 書き出しのフォントの決め方

    [Fact]
    public void 決め方_文字の指定が最優先で後ろへ引き継がない()
    {
        var doc = Doc(
            "[00:01:00]（麻衣）あい（のりこ）う[00:02:00]",
            "[00:03:00]え[00:04:00]");
        var units = CharFontOperations.DisplayUnits(doc.Lines[0]);
        Assert.Equal("い", units[5].Text);
        units[5].FontSetName = "（コーラス）";

        var resolver = new N3FontResolver(Names, null, true);
        Assert.Equal("1111" + "1" + "4" + "22222" + "2", Resolved(doc.Lines[0], resolver.Resolve(doc.Lines[0])));
        Assert.Equal("2", Resolved(doc.Lines[1], resolver.Resolve(doc.Lines[1]))); // （のりこ）を引き継ぐ
    }

    [Fact]
    public void 決め方_行の指定より文字の指定が優先し無い名前は使わない()
    {
        var doc = Doc("[00:01:00]あいう[00:02:00]");
        var line = doc.Lines[0];
        line.FontSetName = "（麻衣）";
        var units = CharFontOperations.DisplayUnits(line);
        units[1].FontSetName = "（のりこ）";
        units[2].FontSetName = "（無い名前）";

        var result = N3FontResolver.ResolveLines(new[] { doc }, Names, null, true)[0][0];
        Assert.Equal(new[] { 1, 2, 1 }, result.Runs);
        Assert.True(result.Manual);
        Assert.True(result.CharManual);
        Assert.Equal(line.Chars.Count, result.Units.Count);
    }

    [Fact]
    public void 決め方_書き出しの文字のフォント番号に出る()
    {
        var doc = Doc("[00:01:00]あ[00:01:50]い[00:02:00]う[00:03:00]");
        CharFontOperations.DisplayUnits(doc.Lines[0])[1].FontSetName = "（コーラス）";
        var layouts = new N3ProjWriter.LayoutResolver(new List<N3ProjLayoutInfo> { new("下寄せ2行", 0, 2) }, null, null, null, new List<string>(), "t");
        var action = ("SHINTA.CharFadeInFadeOut", new JsonObject { ["$type"] = "CharFadeInFadeOutSettingsModel" });
        var lines = N3ProjWriter.BuildLineInfos(doc, new N3ShowTimeSettings(), doc.EmojiEntries, new N3FontResolver(Names, null, true), layouts, action, "Ver 13.79", out _);
        var chars = lines[0]!["LyricsCharInfos"]!.AsArray();
        Assert.Equal(new[] { "あ:0", "い:4", "う:0" }, chars.Select(c => $"{c!["Char"]}:{c["FontIndex"]}"));
    }

    // ------------------------------------------------------------ 文字の種類と文字種別フォント

    [Theory]
    [InlineData("漢", 0)]
    [InlineData("あ", 1)]
    [InlineData("ア", 1)]
    [InlineData("ー", 1)]
    [InlineData("ｱ", 1)]
    [InlineData("A", 2)]
    [InlineData("7", 2)]
    [InlineData("!", 2)]
    [InlineData(" ", 2)]
    [InlineData("！", 0)]
    [InlineData("Ａ", 0)]
    [InlineData("𠮷", 0)]
    public void 文字の種類(string text, int expected)
    {
        Assert.Equal(expected, N3FontLibrary.FaceIndexFor(text, ruby: false));
        Assert.Equal(expected + 3, N3FontLibrary.FaceIndexFor(text, ruby: true));
    }

    [Fact]
    public void 描くときの文字種別フォント_英数はかなを継承する()
    {
        var font = new N3FontSet { Name = "テスト" };
        font.Detail.Faces[0].FontName = "漢字のフォント";
        font.Detail.Faces[0].SizePx = 80;
        font.Detail.Faces[1].FontName = "かなのフォント";
        font.Detail.Faces[4].FontName = "ルビかなのフォント";

        Assert.Equal("かなのフォント", N3FontLibrary.RenderFace(font, 2).FontName);
        Assert.Equal("漢字のフォント", N3FontLibrary.EffectiveFace(font, 2).FontName);
        Assert.Equal(80, N3FontLibrary.RenderFace(font, 2).SizePx);
        Assert.Equal("ルビかなのフォント", N3FontLibrary.RenderFace(font, 5).FontName);
        Assert.Equal(40, N3FontLibrary.RenderFace(font, 5).SizePx); // ルビは歌詞の半分
    }
}
