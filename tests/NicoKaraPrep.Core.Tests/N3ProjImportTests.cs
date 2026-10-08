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

    // ------------------------------------------------------------ 絵文字の先行を譲る規則

    /// <summary>Circle of Love 冒頭 3 ページ（上段の行頭に (愛)。ページ1→2・2→3 の上段が重なる）。</summary>
    private static LyricsDocument CircleOfLove() => LrcFormat.Parse(string.Join("\r\n",
        "@Emoji=(愛),a.png",
        "[00:16:21](愛)[00:18:21]曇[00:19:00]り[00:20:58]",
        "[00:19:86]「[00:20:70]大[00:23:12]",
        "",
        "[00:21:18](愛)[00:23:18]鮮[00:24:00]や[00:25:41]",
        "[00:25:49]O[00:25:95]ur[00:27:80]",
        "",
        "[00:26:20](愛)[00:28:20]無[00:29:00]限[00:30:46]",
        "[00:29:65]「[00:30:60]楽[00:32:94]",
        ""));

    /// <summary>ワイプ前 1500 / ワイプ後 800 / 表示間隔 300。絵文字（(愛) ＋ ＿）の先行を譲る規則のオン・オフ。</summary>
    private static N3ShowTimeSettings YieldShow(bool on) => new()
    {
        LeadMs = 1500,
        TailMs = 800,
        IntervalMs = 300,
        EmojiLeadYield = on,
        LeadMatcher = new EmojiMatcher(new[] { "(愛)", "＿" }),
    };

    [Fact]
    public void 絵文字の先行を譲る_規則オンで書き出したものを同じ設定で取り込むと手動指定にならない()
    {
        var doc = CircleOfLove();
        var show = YieldShow(on: true);
        var preview = N3ProjImport.Analyze(ExportAndEdit(doc, show));

        var (source, matched) = N3ProjImport.FindSource(doc, "メイン", preview.Tabs, show.LeadMatcher);
        Assert.NotNull(source);
        Assert.Equal(6, matched.Count); // 絵文字の開始を寄せた行も対応する
        Assert.Equal((21680, 26210), matched[3]);

        Assert.Empty(N3ProjImport.ApplyShowTimes(doc.Clone(), matched, show));
    }

    [Fact]
    public void 絵文字の先行を譲る_規則オンで書き出した後にニコカラメーカー3で自動設定をやり直しても手動指定にならない()
    {
        var doc = CircleOfLove();
        var show = YieldShow(on: true);
        var preview = N3ProjImport.Analyze(ExportAndEdit(doc, show));
        var tab = preview.Tabs[0];
        // ニコカラメーカー3 の自動設定（規則なし）を、書き出した歌詞（絵文字の開始を寄せた Raw）でやり直したことにする
        var nkm3 = N3ShowTimePlanner.Plan(tab.Document, YieldShow(on: false));
        for (int k = 0; k < tab.ShowTimes.Count; k++)
        {
            if (tab.ShowTimes[k] is not null) tab.ShowTimes[k] = (nkm3[k].BeginMs, nkm3[k].EndMs);
        }
        var (_, matched) = N3ProjImport.FindSource(doc, "メイン", preview.Tabs, show.LeadMatcher);
        Assert.Equal(6, matched.Count);
        // 規則オンの計算 (21680, 26210) とも、元の歌詞を規則なしで計算した値 (20780, 25800) とも違う
        Assert.Equal((21055, 25810), matched[3]);

        Assert.Empty(N3ProjImport.ApplyShowTimes(doc.Clone(), matched, show));
    }

    [Fact]
    public void 絵文字の先行を譲る_ニコカラメーカー3と同じ計算のプロジェクトを規則オンで取り込んでも手動指定にならない()
    {
        var doc = CircleOfLove();
        int editedEnd = 0;
        // 規則オフ（ニコカラメーカー3 が自動設定したのと同じ値）で書き出し、最後のページの上段だけ手直しする
        var preview = N3ProjImport.Analyze(ExportAndEdit(doc, YieldShow(on: false), lines =>
        {
            var line = LyricLine(lines, 4);
            editedEnd = line["ShowEndTime"]!.GetValue<int>() + 1000;
            line["ShowEndTime"] = editedEnd;
        }));
        var show = YieldShow(on: true);
        var (_, matched) = N3ProjImport.FindSource(doc, "メイン", preview.Tabs, show.LeadMatcher);
        Assert.Equal(6, matched.Count);
        // ニコカラメーカー3 が詰めた行は、規則オンの計算とは違う
        var plans = N3ShowTimePlanner.Plan(doc, show);
        Assert.Equal((20780, 25800), matched[3]);
        Assert.NotEqual(matched[3].BeginMs, plans[3].BeginMs);

        // 規則なしの計算と一致する行は自動のまま。手直しした行だけが手動指定になる
        var target = doc.Clone();
        var touched = N3ProjImport.ApplyShowTimes(target, matched, show);
        Assert.Equal(new[] { 6 }, touched.ToArray());
        Assert.Null(target.Lines[6].ShowBeginCs);
        Assert.Equal(editedEnd / 10, target.Lines[6].ShowEndCs);
        Assert.Null(target.Lines[3].ShowBeginCs);
        Assert.Null(target.Lines[3].ShowEndCs);
    }

    [Fact]
    public void 重ねてよい時間_ニコカラメーカー3と同じ計算のプロジェクトを重ねる設定で取り込んでも手動指定にならない()
    {
        var doc = CircleOfLove();
        var preview = N3ProjImport.Analyze(ExportAndEdit(doc, YieldShow(on: false)));
        var show = YieldShow(on: false);
        show.OverlapMs = 500;
        var (_, matched) = N3ProjImport.FindSource(doc, "メイン", preview.Tabs, show.LeadMatcher);
        Assert.Equal(6, matched.Count);
        // 重ねる設定の計算は、ニコカラメーカー3 が詰めた値（重ねない計算）と違う
        Assert.Equal((20780, 25800), matched[3]);
        Assert.Equal(20555, N3ShowTimePlanner.Plan(doc, show)[3].BeginMs);
        Assert.Empty(N3ProjImport.ApplyShowTimes(doc.Clone(), matched, show));

        // 重ねる設定（絵文字の先行を譲る規則もオン）で書き出したものを、同じ設定で取り込んでも手動指定にならない
        var both = YieldShow(on: true);
        both.OverlapMs = 500;
        var preview2 = N3ProjImport.Analyze(ExportAndEdit(doc, both));
        var (_, matched2) = N3ProjImport.FindSource(doc, "メイン", preview2.Tabs, both.LeadMatcher);
        Assert.Equal(6, matched2.Count);
        Assert.Empty(N3ProjImport.ApplyShowTimes(doc.Clone(), matched2, both));
    }

    [Fact]
    public void 絵文字の先行を譲る_絵文字の開始を寄せた行も照合で対応する()
    {
        var doc = CircleOfLove();
        var show = YieldShow(on: true);
        var tab = N3ProjImport.Analyze(ExportAndEdit(doc, show)).Tabs[0];

        var map = N3ProjImport.MatchLineIndexes(doc, tab, show.LeadMatcher);
        Assert.Equal(new[] { 0, 1, 3, 4, 6, 7 }, map.Keys.OrderBy(k => k));
        // n3proj の行は絵文字の開始を寄せてある
        Assert.Equal("[00:21:68](愛)[00:23:18]鮮[00:24:00]や[00:25:41]", LrcFormat.WriteLyricLine(tab.Document.Lines[map[3]]));
        // 絵文字のタグも比べると、寄せた 2 行は対応しない
        Assert.Equal(new[] { 0, 1, 4, 7 }, N3ProjImport.MatchLineIndexes(doc, tab).Keys.OrderBy(k => k));

        // 絵文字の表示秒数を変えて付け直した歌詞も対応する
        var retagged = doc.Clone();
        EmojiTagger.RetagAll(retagged, new EmojiMatcher(new[] { "(愛)" }), new EmojiTagSettings { LeadCs = 150 });
        Assert.Equal(6, N3ProjImport.MatchLines(retagged, tab, show.LeadMatcher).Count);
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
    // ------------------------------------------------------------ プロジェクトの歌詞ファイル

    [Fact]
    public void 歌詞ファイル_プロジェクトの相対パスで探し_無ければ絶対パス()
    {
        string sub = Path.Combine(_dir, "moved");
        Directory.CreateDirectory(sub);
        string project = Path.Combine(sub, "song.n3proj");
        string lrc = Path.Combine(sub, "song.lrc");
        File.WriteAllText(lrc, "[00:01:00]あ[00:02:00]");
        string elsewhere = Path.Combine(_dir, "other.lrc");
        File.WriteAllText(elsewhere, "[00:01:00]い[00:02:00]");

        // フォルダごと移した: 保存されている絶対パスは古い場所でも、相対パスで見つかる
        var moved = new N3ProjSourceTab { Name = "メイン", LyricsPath = @"W:\old\song.lrc", LyricsRelativePath = "song.lrc" };
        Assert.Equal(lrc, N3ProjImport.FindLyricsFile(project, moved));

        // 相対パスに無ければ絶対パス
        var absolute = new N3ProjSourceTab { Name = "メイン", LyricsPath = elsewhere, LyricsRelativePath = "missing.lrc" };
        Assert.Equal(elsewhere, N3ProjImport.FindLyricsFile(project, absolute));

        // どちらにも無い・未設定
        Assert.Null(N3ProjImport.FindLyricsFile(project, new N3ProjSourceTab { Name = "メイン", LyricsPath = @"W:\old\song.lrc", LyricsRelativePath = "missing.lrc" }));
        Assert.Null(N3ProjImport.FindLyricsFile(project, new N3ProjSourceTab { Name = "メイン" }));
    }

    [Fact]
    public void 歌詞ファイル_書き出したプロジェクトから読み出せる()
    {
        string lrc = Path.Combine(_dir, "song.lrc");
        File.WriteAllText(lrc, "[00:10:00]あ[00:12:00]");
        string path = ExportAndEdit(Song(), new N3ShowTimeSettings());
        var preview = N3ProjImport.Analyze(path);
        var main = Assert.Single(preview.Tabs);
        Assert.Equal("song.lrc", main.LyricsRelativePath);
        Assert.Equal(lrc, N3ProjImport.FindLyricsFile(path, main));
    }

    [Fact]
    public void 歌詞ファイル_同じ曲か()
    {
        Assert.True(N3ProjImport.IsSameLyrics(@"C:\songs\a.lrc", @"c:\SONGS\A.lrc"));
        Assert.True(N3ProjImport.IsSameLyrics(@"C:\songs\a.rlf", @"C:\songs\a.lrc")); // rlf と、そこから作った lrc
        Assert.False(N3ProjImport.IsSameLyrics(@"C:\songs\a.lrc", @"C:\songs\b.lrc"));
        Assert.False(N3ProjImport.IsSameLyrics(@"C:\songs\a.lrc", @"C:\other\a.lrc"));
    }

    [Fact]
    public void 歌詞設定タブ_2つ目以降の歌詞とそろったレイアウトの名前を読む()
    {
        static JsonObject Line(string raw, int? layout)
        {
            var o = new JsonObject { ["Kind"] = 1, ["Raw"] = raw, ["ShowBeginTime"] = 0, ["ShowEndTime"] = 5000 };
            if (layout is int l) o["LayoutIndex"] = l;
            return o;
        }
        var root = new JsonObject
        {
            ["LyricsLayouts"] = new JsonArray(new JsonObject { ["SettingsName"] = "下寄せ2行" }, new JsonObject { ["SettingsName"] = "コーラス1行" }),
            ["SourceLyricsInfos"] = new JsonArray(
                new JsonObject
                {
                    ["SettingsName"] = "メイン",
                    ["SourceLyricsRelativePath"] = "song.lrc",
                    ["LineInfos"] = new JsonArray(Line("[00:01:00]あ[00:02:00]", 0), Line("[00:02:00]い[00:03:00]", 0)),
                },
                new JsonObject
                {
                    ["SettingsName"] = "コーラス2",
                    ["SourceLyricsRelativePath"] = "song_パート1.lrc",
                    ["LineInfos"] = new JsonArray(Line("[00:05:00]（コーラス）う[00:06:00]", 1), new JsonObject { ["Kind"] = 2 }, Line("[00:09:00]（コーラス）え[00:10:00]", 1)),
                },
                new JsonObject
                {
                    ["SettingsName"] = "まぜこぜ",
                    ["LineInfos"] = new JsonArray(Line("[00:11:00]お[00:12:00]", 0), Line("[00:12:00]か[00:13:00]", 1)),
                },
                new JsonObject
                {
                    ["SettingsName"] = "不明",
                    ["LineInfos"] = new JsonArray(Line("[00:14:00]き[00:15:00]", null)),
                }),
        };

        var tabs = N3ProjImport.ReadSourceTabs(root);
        Assert.Equal(new[] { "メイン", "コーラス2", "まぜこぜ", "不明" }, tabs.Select(t => t.Name));
        Assert.Equal("下寄せ2行", tabs[0].LayoutName);
        Assert.Equal("コーラス1行", tabs[1].LayoutName);
        Assert.Equal(2, tabs[1].LyricLineCount);
        Assert.Null(tabs[2].LayoutName); // 行ごとにばらばら
        Assert.Null(tabs[3].LayoutName); // レイアウトの番号が無い

        // 行ごとのレイアウトの名前（ページ区切りの空行は null）
        Assert.Equal(new string?[] { "コーラス1行", null, "コーラス1行" }, tabs[1].LayoutNames);
        Assert.Equal(new string?[] { "下寄せ2行", "コーラス1行" }, tabs[2].LayoutNames);
        Assert.Equal(new string?[] { null }, tabs[3].LayoutNames);

        // 歌詞の行の対応付け（ドキュメントの行 → タブの行）
        var doc = LrcFormat.Parse(string.Join("\r\n", "[00:05:00]（コーラス）う[00:06:00]", "", "[00:09:00]（コーラス）え[00:10:00]"));
        var map = N3ProjImport.MatchLineIndexes(doc, tabs[1]);
        Assert.Equal(0, map[0]);
        Assert.Equal(2, map[2]);
        Assert.Equal(new[] { 0, 2 }, map.Keys.OrderBy(k => k));

        // コーラスの歌詞ファイルもプロジェクトのフォルダから見つかる
        string chorus = Path.Combine(_dir, "song_パート1.lrc");
        File.WriteAllText(chorus, "[00:05:00]（コーラス）う[00:06:00]");
        Assert.Equal(chorus, N3ProjImport.FindLyricsFile(Path.Combine(_dir, "song.n3proj"), tabs[1]));
    }

    [Fact]
    public void 歌詞設定タブ_行ごとの字幕アクションを読む()
    {
        const string oldCharFade =
            """{"$type":"CharFadeInFadeOutSettingsModel","IntroDelay":350,"IntroDelayTimeTag":"[00:00:35]","WholeFadeOut":false,"DelayInlineGraphicsIsEnabled":true,"TailDelay":250,"DelayInlineGraphics":true,"FadeInTime":250,"FadeOutTime":250}""";
        // ユーザーが作った見本の n3proj（Ver 13.90）の上下スライドの写し
        const string slide =
            """{"$type":"SlideUpDownSettingsModel","SlideAmount":{"Size":-50,"Reference":1080,"Ratio":-0.046296296296296294},"FadeIn":false,"FadeOut":false,"FadeInTime":250,"FadeOutTime":250,"CreateAppVer":"Ver 13.90","ModifyAppVer":""}""";
        static JsonObject Line(string raw, string id, string? settings)
        {
            var o = new JsonObject { ["Kind"] = 1, ["Raw"] = raw, ["ShowBeginTime"] = 0, ["ShowEndTime"] = 5000, ["LayoutIndex"] = 0, ["SubtitleActionId"] = id };
            if (settings is not null) o["SubtitleActionSettings"] = JsonNode.Parse(settings);
            return o;
        }
        var root = new JsonObject
        {
            ["LyricsLayouts"] = new JsonArray(new JsonObject { ["SettingsName"] = "下寄せ2行" }),
            ["SourceLyricsInfos"] = new JsonArray(new JsonObject
            {
                ["SettingsName"] = "メイン",
                ["LineInfos"] = new JsonArray(
                    Line("[00:01:00]あ[00:02:00]", "SHINTA.CharFadeInFadeOut", oldCharFade),
                    new JsonObject { ["Kind"] = 2, ["SubtitleActionId"] = "", ["SubtitleActionSettings"] = new JsonObject { ["$type"] = "AddOnSettingsModel" } },
                    new JsonObject { ["Kind"] = 0 },
                    Line("[00:03:00]い[00:04:00]", "SHINTA.LineFadeOut", null),
                    Line("[00:05:00]う[00:06:00]", "", """{"$type":"AddOnSettingsModel"}"""),
                    Line("[00:07:00]え[00:08:00]", "SHINTA.SlideUpDown", slide)),
            }),
        };

        var tab = Assert.Single(N3ProjImport.ReadSourceTabs(root));
        // レイアウトの名前と同じ並び（ページ区切りの空行は null、Id が空の行も null）
        Assert.Equal(tab.LayoutNames.Count, tab.SubtitleActions.Count);
        Assert.Equal(new string?[] { "SHINTA.CharFadeInFadeOut", null, "SHINTA.LineFadeOut", null, "SHINTA.SlideUpDown" }, tab.SubtitleActions.Select(a => a?.Id));
        Assert.Equal(oldCharFade, tab.SubtitleActions[0]!.Settings.ToJsonString()); // 読んだまま（書式は書き出しでそろえる）
        Assert.Equal(250, tab.SubtitleActions[2]!.GetInt("FadeOutTime")); // 設定値が無ければ Id の既定値
        Assert.Equal(slide, tab.SubtitleActions[4]!.Settings.ToJsonString());

        // 読んだ設定値は n3proj の JSON から切り離した写し
        tab.SubtitleActions[0]!.Set("IntroDelay", 1);
        Assert.Equal(350, root["SourceLyricsInfos"]![0]!["LineInfos"]![0]!["SubtitleActionSettings"]!["IntroDelay"]!.GetValue<int>());
    }

    [Fact]
    public void 字幕アクション_書き出したプロジェクトから行ごとの指定といちばん多いアクションを読み戻せる()
    {
        var doc = Song();
        var lyricIndexes = Enumerable.Range(0, doc.Lines.Count).Where(i => !doc.Lines[i].IsEmpty).ToList();
        Assert.Equal(6, lyricIndexes.Count);
        // 6 行のうち 4 行をフェードイン（残りの 2 行は既定の文字単位フェード）
        foreach (int i in lyricIndexes.Skip(2)) doc.Lines[i].SubtitleAction = N3SubtitleActionCatalog.CreateDefault("SHINTA.LineFadeIn");
        string path = ExportAndEdit(doc, new N3ShowTimeSettings());

        var tab = Assert.Single(N3ProjImport.ReadSourceTabs(N3ProjFormat.ReadJsonObject(path)));
        var map = N3ProjImport.MatchLineIndexes(doc, tab);
        foreach (int i in lyricIndexes)
        {
            Assert.True(map.TryGetValue(i, out int k), $"行 {i} が対応しない");
            Assert.Equal(doc.Lines[i].SubtitleAction?.Id ?? "SHINTA.CharFadeInFadeOut", tab.SubtitleActions[k]!.Id);
        }

        var settings = N3ProjFormat.Read(path);
        Assert.Equal("SHINTA.LineFadeIn", settings.DefaultSubtitleAction!.Id);
        Assert.Equal("$type", settings.DefaultSubtitleAction.Settings.First().Key);
        Assert.Equal(250, settings.DefaultSubtitleAction.GetInt("FadeInTime"));
        Assert.True(settings.DefaultSubtitleAction.SameAs(N3ProjFormat.MostCommonSubtitleAction(N3ProjFormat.ReadJsonObject(path))));
    }

    [Fact]
    public void 曲プロジェクト_自分の歌詞ファイルを持つタブを保存して読み戻せる()
    {
        string song = Path.Combine(_dir, "song.lrc");
        var project = new NicoKaraPrep.Core.Project.SongProject();
        project.Tabs.Add(new NicoKaraPrep.Core.Project.SongProjectTab { Name = "コーラス2", FilePath = Path.Combine(_dir, "song_パート1.lrc"), OwnFile = true });
        project.Tabs.Add(new NicoKaraPrep.Core.Project.SongProjectTab { Name = "パート2", Text = "[00:01:00]あ[00:02:00]" });
        project.Save(song);

        var back = NicoKaraPrep.Core.Project.SongProject.TryLoad(song)!;
        Assert.Equal(2, back.Tabs.Count);
        Assert.True(back.Tabs[0].OwnFile);
        Assert.Equal(Path.Combine(_dir, "song_パート1.lrc"), back.Tabs[0].FilePath);
        Assert.False(back.Tabs[1].OwnFile); // 前からの分離タブ（旧形式も false で読む）
    }
}
