using System.Text.Json;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;
using NicoKaraPrep.Core.Project;

namespace NicoKaraPrep.Core.Tests;

/// <summary>
/// .tttproj の行設定を、保存したときの行の文字で当て直す（歌詞ファイルを保存しないまま行を足し引きしてから開き直したとき）。
/// </summary>
public class LineSettingsRelocateTests
{
    private static LyricsDocument Doc(params string[] lines)
    {
        var doc = new LyricsDocument();
        foreach (string l in lines) doc.Lines.Add(LrcFormat.ParseLyricLine(l));
        return doc;
    }

    private static readonly PhraseTiming Timing = new() { LeadCs = 150, TailCs = 50 };

    /// <summary>保存して JSON から読み直した設定（.tttproj と同じ）。</summary>
    private static List<LineExportSettings> SaveAndLoad(LyricsDocument doc) =>
        JsonSerializer.Deserialize<List<LineExportSettings>>(JsonSerializer.Serialize(LineExportSettings.Collect(doc)))!;

    /// <summary>ユーザーの例の形: 間奏の場所に空行 2 つ、その後ろに（ゆかり）（あかり）の行。</summary>
    private static LyricsDocument Song() => Doc(
        "[02:08:78]二つの声が[02:12:00]",
        "",
        "",
        "[02:30:37]（ゆかり）迷った日には[02:34:00]",
        "[02:34:59]（あかり）怖い夜には[02:38:00]");

    [Fact]
    public void 歌詞ファイルを保存せずに定型文の行を足して開き直しても_定型文のフォントは別の行に付かない()
    {
        var edited = Song();
        edited.Lines[3].FontSetName = "（ゆかり）"; // 手で指定した行（定型文の行より後ろ）
        var r = PhraseTagger.Insert(edited, 2, 0, "（間奏）", Timing, "（コーラス）");
        Assert.Equal("（コーラス）", edited.Lines[r.LineIndex].FontSetName);
        var saved = SaveAndLoad(edited); // .tttproj は保存された（行の番号は定型文を入れたあと）

        var reopened = Song(); // 歌詞ファイルは保存していない（定型文の行は無い）
        LineExportSettings.Apply(reopened, saved);

        Assert.All(reopened.Lines.Where(l => l.GetDisplayText().Contains("あかり")), l => Assert.Null(l.FontSetName));
        Assert.Equal("（ゆかり）", reopened.Lines[3].FontSetName); // ずれた番号でも、同じ文字の行へ戻る
        Assert.Equal(1, reopened.Lines.Count(l => l.FontSetName is not null));
    }

    [Fact]
    public void 保存したままの文書なら同じ行へ()
    {
        var doc = Song();
        PhraseTagger.Insert(doc, 2, 0, "（間奏）", Timing, "（コーラス）");
        doc.Lines[0].ShowBeginCs = 12000;
        var saved = SaveAndLoad(doc);

        var again = Doc(doc.Lines.Select(LrcFormat.WriteLyricLine).ToArray());
        LineExportSettings.Apply(again, saved);
        for (int i = 0; i < doc.Lines.Count; i++)
        {
            Assert.Equal(doc.Lines[i].FontSetName, again.Lines[i].FontSetName);
            Assert.Equal(doc.Lines[i].ShowBeginCs, again.Lines[i].ShowBeginCs);
        }
    }

    [Fact]
    public void 外で歌詞を直しただけ_行の数が同じなら同じ番号の行へ()
    {
        var doc = Doc("[00:01:00]あいう[00:02:00]", "[00:03:00]かきく[00:04:00]");
        doc.Lines[1].FontSetName = "（麻衣）";
        var saved = SaveAndLoad(doc);

        var fixedTypo = Doc("[00:01:00]あいう[00:02:00]", "[00:03:00]かきけ[00:04:00]");
        LineExportSettings.Apply(fixedTypo, saved);
        Assert.Equal("（麻衣）", fixedTypo.Lines[1].FontSetName);

        // 行の数が違い、同じ文字の行も無ければ当てない
        var shifted = Doc("", "[00:01:00]あいう[00:02:00]", "[00:03:00]かきけ[00:04:00]");
        LineExportSettings.Apply(shifted, saved);
        Assert.All(shifted.Lines, l => Assert.Null(l.FontSetName));
    }

    [Fact]
    public void 同じ文字の行が複数あれば近いほう_番号も文字も合う行は先に取る()
    {
        var doc = Doc("[00:01:00]サビ[00:02:00]", "[00:03:00]A[00:04:00]", "[00:05:00]サビ[00:06:00]", "[00:07:00]B[00:08:00]", "[00:09:00]サビ[00:10:00]");
        doc.Lines[2].FontSetName = "（中）";
        doc.Lines[4].FontSetName = "（後）";
        var saved = SaveAndLoad(doc);

        // 先頭に 1 行足した（番号が 1 つずつずれた）
        var shifted = Doc("", "[00:01:00]サビ[00:02:00]", "[00:03:00]A[00:04:00]", "[00:05:00]サビ[00:06:00]", "[00:07:00]B[00:08:00]", "[00:09:00]サビ[00:10:00]");
        LineExportSettings.Apply(shifted, saved);
        Assert.Equal(new string?[] { null, null, null, "（中）", null, "（後）" }, shifted.Lines.Select(l => l.FontSetName).ToArray());
    }

    [Fact]
    public void 以前の版のファイル_文字を持たない設定は番号のまま()
    {
        var doc = Song();
        LineExportSettings.Apply(doc, JsonSerializer.Deserialize<List<LineExportSettings>>("[{\"Index\":4,\"FontSetName\":\"（コーラス）\"}]"));
        Assert.Equal("（コーラス）", doc.Lines[4].FontSetName);
    }
}
