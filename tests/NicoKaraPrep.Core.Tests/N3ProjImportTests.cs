using System.Text.Json.Nodes;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Tests;

public class N3ProjImportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "NicoKaraPrepTests", Guid.NewGuid().ToString("N"));

    public N3ProjImportTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 後始末の失敗は無視 */ }
    }

    private static LyricsDocument Song() => LrcFormat.Parse(string.Join("\r\n",
        "@Emoji=（花帆）,a.png",
        "[00:10:00]（花帆）[00:12:00]あ[00:13:00]い[00:14:00]",
        "[00:14:50]う[00:16:00]え[00:17:00]",
        "",
        "[00:17:40]お[00:18:00]か[00:19:00]",
        "[00:19:20]き[00:21:00]く[00:22:00]",
        "",
        "[00:40:00]け[00:41:00]こ[00:42:00]",
        "[00:42:50]さ[00:44:00]し[00:45:00]",
        ""));

    /// <summary>NicoKaraPrep で書き出し、ニコカラメーカー上で手直ししたことにしたプロジェクトを作る。</summary>
    private string ExportAndEdit(LyricsDocument doc, N3ShowTimeSettings show, Action<JsonArray>? edit = null)
    {
        string path = Path.Combine(_dir, "song.n3proj");
        N3ProjWriter.Write(path,
            new[] { new N3ProjExportTab { Name = "メイン", Document = doc, LyricsPath = Path.Combine(_dir, "song.lrc") } },
            new N3ProjExportOptions
            {
                ShowTime = show,
                EmojiEntries = doc.EmojiEntries,
                DefaultFont = new N3FontSet { Name = "標準", FontFamily = "メイリオ" },
            });
        if (edit is not null)
        {
            var root = N3ProjFormat.ReadJsonObject(path);
            edit(root["SourceLyricsInfos"]![0]!["LineInfos"]!.AsArray());
            N3ProjWriter.SaveZip(path, root);
        }
        return path;
    }

    private static JsonObject LyricLine(JsonArray lines, int n) =>
        lines.OfType<JsonObject>().Where(l => l["Kind"]!.GetValue<int>() == 1).ElementAt(n);

    [Fact]
    public void 解析_タブとフォント設定と表示時刻の設定を推定できる()
    {
        var doc = Song();
        string path = ExportAndEdit(doc, new N3ShowTimeSettings { LeadMs = 1800, TailMs = 500, IntervalMs = 300 });

        var preview = N3ProjImport.Analyze(path);
        Assert.Single(preview.Tabs);
        Assert.Equal("メイン", preview.Tabs[0].Name);
        Assert.Equal(6, preview.Tabs[0].LyricLineCount);
        Assert.Single(preview.FontSets);
        Assert.Equal("標準", preview.FontSets[0].Name);

        Assert.NotNull(preview.Timing);
        Assert.Equal(1800, preview.Timing!.LeadMs);
        Assert.Equal(500, preview.Timing.TailMs);
        Assert.Equal(6, preview.Timing.Total);
        Assert.Equal(6, preview.Timing.Matched);
    }

    [Fact]
    public void 照合_ニコカラメーカーで調整した行だけが手動指定として取り込まれる()
    {
        var doc = Song();
        var show = new N3ShowTimeSettings { LeadMs = 1500, TailMs = 800, IntervalMs = 300 };
        int editedEnd = 0;
        string path = ExportAndEdit(doc, show, lines =>
        {
            // 最後のページの上段の表示終了を 1 秒延ばす（ニコカラメーカーでの手動調整）
            var line = LyricLine(lines, 4);
            editedEnd = line["ShowEndTime"]!.GetValue<int>() + 1000;
            line["ShowEndTime"] = editedEnd;
        });

        var preview = N3ProjImport.Analyze(path);
        Assert.Equal(5, preview.Timing!.Matched); // 手直しした 1 行以外は自動計算どおり

        var (source, matched) = N3ProjImport.FindSource(doc, "メイン", preview.Tabs);
        Assert.NotNull(source);
        Assert.Equal(6, matched.Count);

        var target = doc.Clone();
        var touched = N3ProjImport.ApplyShowTimes(target, matched, show);
        int editedLine = 6; // ドキュメント上の行番号（空行を含む。5 番目の歌詞行「けこ」）
        Assert.Equal(new[] { editedLine }, touched.ToArray());
        Assert.Null(target.Lines[editedLine].ShowBeginCs);
        Assert.Equal(editedEnd / 10, target.Lines[editedLine].ShowEndCs);

        // 取り込み後の自動計算はニコカラメーカーの値をすべて再現する
        var plans = N3ShowTimePlanner.Plan(target, show);
        foreach (var (i, (b, e)) in matched)
        {
            Assert.Equal(b, plans[i].BeginMs);
            Assert.Equal(e, plans[i].EndMs);
        }
    }

    [Fact]
    public void 照合_歌詞を直した行は対応せずそれ以外は順番どおりに対応する()
    {
        var doc = Song();
        string path = ExportAndEdit(doc, new N3ShowTimeSettings());
        var preview = N3ProjImport.Analyze(path);

        var edited = doc.Clone();
        edited.Lines[3] = LrcFormat.ParseLyricLine("[00:17:40]お[00:18:00]か[00:19:10]"); // 「おか」のタグを直した
        edited.Lines.Insert(0, LrcFormat.ParseLyricLine("[00:05:00]前[00:06:00]")); // 先頭に行を追加（以降は 1 行ずれる）

        var matched = N3ProjImport.MatchLines(edited, preview.Tabs[0]);
        Assert.Equal(5, matched.Count);
        Assert.False(matched.ContainsKey(0)); // 追加した行
        Assert.False(matched.ContainsKey(4)); // 直した行
        Assert.True(matched.ContainsKey(1));  // 「（花帆）あい」
        Assert.True(matched.ContainsKey(5));  // 「きく」
        Assert.True(matched.ContainsKey(8));  // 「さし」
    }

    [Fact]
    public void 照合_同じ名前のタブが無ければ一番多く対応するタブを使う()
    {
        var doc = Song();
        string path = ExportAndEdit(doc, new N3ShowTimeSettings());
        var preview = N3ProjImport.Analyze(path);

        var (source, matched) = N3ProjImport.FindSource(doc, "パート1", preview.Tabs);
        Assert.Equal("メイン", source?.Name);
        Assert.Equal(6, matched.Count);
    }

    [Fact]
    public void アイコン_歌詞設定のEmojiタグを読み相対パスは歌詞ファイルのフォルダから解決する()
    {
        string lyricsDir = Path.Combine(_dir, "lyrics");
        Directory.CreateDirectory(lyricsDir);
        File.WriteAllBytes(Path.Combine(lyricsDir, "kaho.png"), new byte[] { 1 });

        var root = new JsonObject
        {
            ["SourceLyricsInfos"] = new JsonArray(
                new JsonObject
                {
                    ["SettingsName"] = "メイン",
                    ["SourceLyricsPath"] = Path.Combine(lyricsDir, "song.lrc"),
                    ["AtTagsForSave"] = "@Ruby1=漢,かん\n@Emoji=（花帆）,kaho.png,kaho_d.png,NoDecor,Zoom=150\n@emoji=★,D:\\icons\\star.png",
                },
                new JsonObject
                {
                    ["SettingsName"] = "コーラス1",
                    ["SourceLyricsPath"] = null,
                    ["AtTagsForSave"] = "@Emoji=（花帆）,other.png\n@Emoji=（梢）,kozue.png",
                }),
        };

        var icons = N3ProjImport.ReadIcons(root, Path.Combine(_dir, "project.n3proj"));
        Assert.Equal(new[] { "（花帆）", "★", "（梢）" }, icons.Select(i => i.Entry.ReplaceChar).ToArray());

        var kaho = icons[0];
        Assert.Equal(Path.Combine(lyricsDir, "kaho.png"), kaho.Entry.ImageBefore);
        Assert.Equal(Path.Combine(lyricsDir, "kaho_d.png"), kaho.Entry.ImageAfter);
        Assert.Equal("NoDecor,Zoom=150", kaho.Entry.Options);
        Assert.True(kaho.ImageExists);
        Assert.Equal("メイン", kaho.SourceTab);

        Assert.Equal(@"D:\icons\star.png", icons[1].Entry.ImageBefore);
        Assert.False(icons[1].ImageExists);

        // 歌詞ファイルが未設定のタブはプロジェクトのフォルダから
        Assert.Equal(Path.Combine(_dir, "kozue.png"), icons[2].Entry.ImageBefore);
    }

    [Fact]
    public void 動画_絶対パスが無ければプロジェクトからの相対パスを使う()
    {
        File.WriteAllBytes(Path.Combine(_dir, "movie.mp4"), new byte[] { 1 });
        var root = new JsonObject
        {
            ["SourceInfo"] = new JsonObject
            {
                ["MoviePath"] = @"Z:\moved\movie.mp4",
                ["MovieRelativePath"] = "movie.mp4",
                ["SoundTrackList"] = new JsonArray(),
            },
        };
        Assert.Equal(Path.Combine(_dir, "movie.mp4"), N3ProjImport.ReadMediaPath(root, Path.Combine(_dir, "p.n3proj")));

        var noMovie = new JsonObject
        {
            ["SourceInfo"] = new JsonObject
            {
                ["MoviePath"] = null,
                ["MovieRelativePath"] = "",
                ["SoundTrackList"] = new JsonArray(new JsonObject { ["Path"] = @"Z:\a\song.m4a", ["RelativePath"] = "" }),
            },
        };
        Assert.Equal(@"Z:\a\song.m4a", N3ProjImport.ReadMediaPath(noMovie, Path.Combine(_dir, "p.n3proj")));
    }

    [Fact]
    public void アイコン_書き出したプロジェクトから読み戻せる()
    {
        var doc = Song();
        string path = ExportAndEdit(doc, new N3ShowTimeSettings());
        var preview = N3ProjImport.Analyze(path);
        var icon = Assert.Single(preview.Icons);
        Assert.Equal("（花帆）", icon.Entry.ReplaceChar);
        Assert.Equal(Path.Combine(_dir, "a.png"), icon.Entry.ImageBefore);
    }

    /// <summary>
    /// 実プロジェクトの表示時刻を、推定した設定の自動計算がどれだけ再現できるか。
    /// 環境変数 TTT_N3PROJ_SAMPLE に n3proj を指定して実行する（手動調整された行があるため一致率で判定。
    /// しきい値は TTT_N3PROJ_MIN_MATCH、既定 0.8。実測: Circle of Love 100%、Burn 82%、Boooooom Boooooom Bee 83%）。
    /// </summary>
    [Fact]
    public void ゴールデン_実プロジェクトの表示時刻を自動計算で再現できる()
    {
        string? sample = Environment.GetEnvironmentVariable("TTT_N3PROJ_SAMPLE");
        if (string.IsNullOrEmpty(sample) || !File.Exists(sample)) return;

        double threshold = double.TryParse(Environment.GetEnvironmentVariable("TTT_N3PROJ_MIN_MATCH"), out double t) ? t : 0.8;
        var preview = N3ProjImport.Analyze(sample);
        Assert.NotNull(preview.Timing);
        var timing = preview.Timing!;
        double ratio = (double)timing.Matched / timing.Total;
        Assert.True(ratio >= threshold,
            $"一致 {timing.Matched}/{timing.Total}（{ratio:P1}）ワイプ前 {timing.LeadMs} / ワイプ後 {timing.TailMs} / 間隔 {timing.IntervalMs}");
    }
}
