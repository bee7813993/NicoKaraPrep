using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Tests;

public class LineOperationsTests
{
    private static LyricsDocument Doc(params string[] lines)
    {
        var doc = new LyricsDocument();
        foreach (string l in lines) doc.Lines.Add(TextEditModeFormat.ParseLyricLine(l));
        return doc;
    }

    [Fact]
    public void 行分割_タグとチェックとルビが追従する()
    {
        var doc = Doc("[2|00:01:00]歌{漢字|[1|00:02:00]かん＋[1|00:02:50]じ}詞[00:03:00]");
        // 「歌」の後（charIndex=1）で分割
        LineOperations.SplitLine(doc, 0, 1);

        Assert.Equal(2, doc.Lines.Count);
        // 前半: 歌 + 補完された行末タグ（後半の先頭タグ 00:02:00）
        Assert.Equal("[2|00:01:00]歌[00:02:00]", TextEditModeFormat.WriteLyricLine(doc.Lines[0]));
        // 後半: 漢字（ルビ・チェック数維持）+ 元の行末タグ
        Assert.Equal("{漢字|[1|00:02:00]かん＋[1|00:02:50]じ}詞[00:03:00]", TextEditModeFormat.WriteLyricLine(doc.Lines[1]));
    }

    [Fact]
    public void 行分割_ルビ連結の切り離し()
    {
        var doc = Doc("{漢字|[1|00:01:00]かん＋[1|00:02:00]じ}[00:03:00]");
        // 漢 と 字 の間で分割
        LineOperations.SplitLine(doc, 0, 1);

        Assert.False(doc.Lines[0].Chars[^1].RubyJoinsNext);
        Assert.Equal("かん", doc.Lines[0].Chars[0].Ruby);
        Assert.Equal("じ", doc.Lines[1].Chars[0].Ruby);
    }

    [Fact]
    public void 行結合_行末タグが2連タグとして残る()
    {
        var doc = Doc("[1|00:01:00]あい[00:02:00]", "[1|00:03:00]うえ[00:04:00]");
        LineOperations.JoinWithNextLine(doc, 0);

        Assert.Single(doc.Lines);
        // 00:02:00 は次行先頭 00:03:00 と異なるため 2連タグ（スペーサー）で残る
        Assert.Equal("[1|00:01:00]あい[00:02:00][1|00:03:00]うえ[00:04:00]", TextEditModeFormat.WriteLyricLine(doc.Lines[0]));
    }

    [Fact]
    public void 行結合_スペースを挟むオプション()
    {
        var doc = Doc("[1|00:01:00]あい[00:02:00]", "[1|00:03:00]うえ[00:04:00]");
        LineOperations.JoinWithNextLine(doc, 0, insertSpace: true);

        Assert.Single(doc.Lines);
        Assert.Equal("[1|00:01:00]あい[00:02:00] [1|00:03:00]うえ[00:04:00]", TextEditModeFormat.WriteLyricLine(doc.Lines[0]));
    }

    [Fact]
    public void 行結合_同時刻なら行末タグは捨てる()
    {
        var doc = Doc("[1|00:01:00]あい[00:03:00]", "[1|00:03:00]うえ[00:04:00]");
        LineOperations.JoinWithNextLine(doc, 0);
        Assert.Equal("[1|00:01:00]あい[1|00:03:00]うえ[00:04:00]", TextEditModeFormat.WriteLyricLine(doc.Lines[0]));
    }

    [Fact]
    public void 行末で分割すると本当の空行ができる()
    {
        // 行末タグ（EndTimeCs）は前半行に残り、後半はページ区切りになる空行
        var doc = Doc("[1|00:01:00]あい[00:02:00]");
        LineOperations.SplitLine(doc, 0, 2);

        Assert.Equal(2, doc.Lines.Count);
        Assert.True(doc.Lines[1].IsEmpty);
        Assert.Equal("[1|00:01:00]あい[00:02:00]", TextEditModeFormat.WriteLyricLine(doc.Lines[0]));
    }

    [Fact]
    public void 行末のスペーサー手前で分割しても本当の空行ができる()
    {
        // 行末が 2 連タグ（実文字 + タグ付きスペーサー + 行末タグ）の行を
        // 挿入ビューの「行末」相当位置（スペーサーの手前）で分割する
        var doc = Doc("[1|00:01:00]あ[00:02:00][00:03:00]");
        LineOperations.SplitLine(doc, 0, 1);

        Assert.Equal(2, doc.Lines.Count);
        Assert.True(doc.Lines[1].IsEmpty);
        Assert.Equal("[1|00:01:00]あ[00:02:00][00:03:00]", TextEditModeFormat.WriteLyricLine(doc.Lines[0]));
    }

    [Fact]
    public void 分割して結合すると元に戻る()
    {
        string src = "[2|00:01:00]歌詞のテスト[00:03:00]";
        var doc = Doc(src);
        LineOperations.SplitLine(doc, 0, 3);
        LineOperations.JoinWithNextLine(doc, 0);
        Assert.Equal(src, TextEditModeFormat.WriteLyricLine(doc.Lines[0]));
    }

    [Fact]
    public void タブ分離の解除で元のページ区切り位置へ戻る()
    {
        // ページ1: A, B ／ ページ2: C（B はページ1の末尾行）
        var doc = Doc("[00:10:00]あ", "[00:12:00]い", "", "[00:20:00]う");
        doc.AssignSplitOrderKeys();

        // B（行1）をタブへ分離（クローンして本体から削除）
        var tab = LineOperations.ExtractLines(doc, new[] { 1 });
        LineOperations.DeleteLine(doc, 1);
        Assert.Equal(3, doc.Lines.Count);

        // 解除 → B は空行（ページ区切り）の前＝元の位置へ戻る
        LineOperations.MergeLines(doc, tab);
        Assert.Equal(4, doc.Lines.Count);
        Assert.Equal("い", doc.Lines[1].GetDisplayText());
        Assert.True(doc.Lines[2].IsEmpty);
        Assert.Equal("う", doc.Lines[3].GetDisplayText());
    }

    [Fact]
    public void タブ分離の解除_次ページ先頭の行も元の位置へ戻る()
    {
        // C はページ2 の先頭行（時刻だけでは空行の前後どちらか判別できないケース）
        var doc = Doc("[00:10:00]あ", "", "[00:12:00]い", "[00:20:00]う");
        doc.AssignSplitOrderKeys();

        var tab = LineOperations.ExtractLines(doc, new[] { 2 });
        LineOperations.DeleteLine(doc, 2);

        LineOperations.MergeLines(doc, tab);
        Assert.True(doc.Lines[1].IsEmpty);
        Assert.Equal("い", doc.Lines[2].GetDisplayText());
        Assert.Equal("う", doc.Lines[3].GetDisplayText());
    }

    [Fact]
    public void キーが無い行は従来どおり時刻順に挿入される()
    {
        var doc = Doc("[00:10:00]あ", "[00:20:00]う");
        var tab = Doc("[00:12:00]い");
        LineOperations.MergeLines(doc, tab);
        Assert.Equal("あ", doc.Lines[0].GetDisplayText());
        Assert.Equal("い", doc.Lines[1].GetDisplayText());
        Assert.Equal("う", doc.Lines[2].GetDisplayText());
    }

    /// <summary>行エディタの「適用」と同じ手順（読み直した行に、文字ごとのフォントと行の設定を写して置き換える）。</summary>
    private static void ReplaceLineText(LyricsDocument doc, int index, string rawText)
    {
        var newLine = TextEditModeFormat.ParseLyricLine(rawText);
        CharFontOperations.CopyCharFonts(doc.Lines[index], newLine);
        newLine.CopyLineSettingsFrom(doc.Lines[index]);
        doc.Lines[index] = newLine;
    }

    [Fact]
    public void 行を直しても行の設定とページの文字の大きさが残る()
    {
        // 1 行だけのページの文字の大きさ（行設定の「文字の大きさ」）
        var doc = Doc("[00:10:00]あ", "", "[00:20:00]う");
        doc.AssignSplitOrderKeys();
        doc.Lines[0].FontSizeDelta = 4;
        doc.Lines[0].FontSetName = "（麻衣）";
        doc.Lines[0].LayoutName = "2行";
        doc.Lines[0].ShowBeginCs = 900;
        doc.Lines[0].ShowBeginOrigin = ShowTimeOrigin.Auto;

        ReplaceLineText(doc, 0, "[00:10:00]あお");

        var line = doc.Lines[0];
        Assert.Equal("あお", line.GetDisplayText());
        Assert.Equal(4, line.FontSizeDelta);
        Assert.Equal(0, line.SplitOrderKey);
        Assert.Equal("（麻衣）", line.FontSetName);
        Assert.Equal("2行", line.LayoutName);
        Assert.Equal(900, line.ShowBeginCs);
        Assert.Equal(ShowTimeOrigin.Auto, line.ShowBeginOrigin);
        Assert.Equal(4, N3PageFontSize.LineDeltas(doc, PageSplitMode.EmptyLine, 2).GetValueOrDefault(0));
    }

    [Fact]
    public void 分離タブで直した行も解除で元のページ区切り位置へ戻る()
    {
        // ページ1: あ, い ／ ページ2: う（い はページ1の末尾行。時刻順だと空行の後ろ＝次のページへ入ってしまう）
        var doc = Doc("[00:10:00]あ", "[00:12:00]い", "", "[00:20:00]う");
        doc.AssignSplitOrderKeys();
        var tab = LineOperations.ExtractLines(doc, new[] { 1 });
        LineOperations.DeleteLine(doc, 1);

        ReplaceLineText(tab, 0, "[00:12:00]いえ");

        LineOperations.MergeLines(doc, tab);
        Assert.Equal("あ", doc.Lines[0].GetDisplayText());
        Assert.Equal("いえ", doc.Lines[1].GetDisplayText());
        Assert.True(doc.Lines[2].IsEmpty);
        Assert.Equal("う", doc.Lines[3].GetDisplayText());
    }

    [Fact]
    public void 行エディタで分割しても元の位置のキーとページの文字の大きさが残る()
    {
        var doc = Doc("[00:10:00]あ", "[00:12:00]い[00:13:00]う", "", "[00:20:00]え");
        doc.AssignSplitOrderKeys();
        doc.Lines[1].FontSizeDelta = -2;

        // 通常ビューの分割と同じ手順（行エディタの文字列から読み直して置き換え、「い」の後で分ける）
        ReplaceLineText(doc, 1, "[00:12:00]い[00:13:00]う");
        LineOperations.SplitLine(doc, 1, 1);

        Assert.Equal(1, doc.Lines[1].SplitOrderKey);
        Assert.Equal(1, doc.Lines[2].SplitOrderKey); // 後半の行も元の行の位置に付いて並ぶ
        Assert.Equal(-2, doc.Lines[1].FontSizeDelta);
    }

    [Fact]
    public void 行の設定はすべて引き継ぐ()
    {
        // 歌詞（行末タグ）と、呼び出し側で扱うエクスポート済みの印のほかは、すべて写す（行の設定を足して写し忘れたら、ここで落ちる）
        var props = typeof(LyricsLine).GetProperties()
            .Where(p => p.CanWrite && p.Name is not (nameof(LyricsLine.EndTimeCs) or nameof(LyricsLine.Exported)))
            .ToList();
        var from = new LyricsLine();
        foreach (var p in props) p.SetValue(from, SampleValue(p.PropertyType));

        var to = new LyricsLine();
        to.CopyLineSettingsFrom(from);

        foreach (var p in props) Assert.True(SameValue(p.GetValue(from), p.GetValue(to)), $"{p.Name} が引き継がれていない");

        // 字幕アクションは写し（行エディタで直した行と元の行で設定値のオブジェクトを共有しない）
        Assert.NotSame(from.SubtitleAction, to.SubtitleAction);
        Assert.NotSame(from.SubtitleAction!.Settings, to.SubtitleAction!.Settings);
    }

    [Fact]
    public void 行の写しも行の設定をすべて持つ()
    {
        // 行の写し（Undo・タブ分離・書き出しの絵文字の寄せ）でも、行の設定を足して写し忘れたら、ここで落ちる
        var props = typeof(LyricsLine).GetProperties().Where(p => p.CanWrite).ToList();
        var from = new LyricsLine();
        from.Chars.Add(new CharUnit { Text = "あ", TimeCs = 100 });
        foreach (var p in props) p.SetValue(from, SampleValue(p.PropertyType));

        var copy = from.Clone();

        foreach (var p in props) Assert.True(SameValue(p.GetValue(from), p.GetValue(copy)), $"{p.Name} が写されていない");
        Assert.NotSame(from.SubtitleAction, copy.SubtitleAction);
        Assert.Equal("あ", copy.GetDisplayText());
    }

    private static object SampleValue(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        if (t == typeof(int)) return 7;
        if (t == typeof(string)) return "x";
        if (t == typeof(bool)) return true;
        if (t.IsEnum) return Enum.GetValues(t).Cast<object>().Last();
        if (t == typeof(N3SubtitleAction)) return N3SubtitleActionCatalog.CreateDefault(N3SubtitleActionCatalog.LineFadeInId);
        throw new NotSupportedException($"{t.Name} の見本の値をここに足す");
    }

    /// <summary>同じ値か（字幕アクションは中身で比べる）。</summary>
    private static bool SameValue(object? a, object? b) =>
        a is N3SubtitleAction x ? N3SubtitleAction.AreSame(x, b as N3SubtitleAction) : Equals(a, b);

    [Fact]
    public void 空行の挿入と削除()
    {
        var doc = Doc("[00:01:00]あ", "[00:02:00]い");
        LineOperations.InsertEmptyLine(doc, 1);
        Assert.Equal(3, doc.Lines.Count);
        Assert.True(doc.Lines[1].IsEmpty);

        LineOperations.DeleteLine(doc, 1);
        Assert.Equal(2, doc.Lines.Count);
    }

    [Fact]
    public void 選択行の抽出()
    {
        var doc = LrcFormat.Parse(string.Join("\r\n",
            "@Title=曲名",
            "@Emoji=★,star.png",
            "[00:01:00]1行目[00:02:00]",
            "[00:03:00]2行目[00:04:00]",
            "[00:05:00]3行目[00:06:00]"));

        var extracted = LineOperations.ExtractLines(doc, new[] { 2, 0 });
        Assert.Equal(2, extracted.Lines.Count);
        Assert.Equal("1行目", extracted.Lines[0].GetDisplayText()); // 順序は元の並び
        Assert.Equal("3行目", extracted.Lines[1].GetDisplayText());
        Assert.Equal("曲名", extracted.GetTag("Title"));
        Assert.Single(extracted.EmojiEntries);

        // 元のドキュメントは変更されない
        Assert.Equal(3, doc.Lines.Count);
    }
}
