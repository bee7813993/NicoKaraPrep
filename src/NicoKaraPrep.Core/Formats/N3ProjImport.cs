using System.Text.Json.Nodes;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Formats;

/// <summary>n3proj の歌詞設定タブ 1 つ分（読み込み・照合用）。</summary>
public sealed class N3ProjSourceTab
{
    /// <summary>歌詞設定の名称（メイン / コーラス1 など）。</summary>
    public string Name { get; init; } = "";

    /// <summary>歌詞ファイルのパス（未設定なら null）。</summary>
    public string? LyricsPath { get; init; }

    /// <summary>プロジェクトのフォルダから見た歌詞ファイルの相対パス（未設定・空なら null）。</summary>
    public string? LyricsRelativePath { get; init; }

    /// <summary>表示時刻の自動設定が「上段歌詞を長めに表示する」か。</summary>
    public bool TopLong { get; init; }

    /// <summary>このタブの歌詞行がみな同じレイアウト設定を使っていれば、その名前（ばらばら・不明なら null）。</summary>
    public string? LayoutName { get; set; }

    /// <summary>
    /// 歌詞行（Raw）とページ区切り（空行）から組み立てたドキュメント。
    /// ページ区切り・段落区切りの合成行を空行として扱うので、固定行数の行分けでもページ構成が一致する。
    /// </summary>
    public LyricsDocument Document { get; } = new();

    /// <summary><see cref="Document"/> の各行の実際の表示時刻（ms）。空行・未設定は null。</summary>
    public List<(int BeginMs, int EndMs)?> ShowTimes { get; } = new();

    /// <summary><see cref="Document"/> の各行にニコカラメーカーが設定したレイアウト設定の名前。空行・不明は null。</summary>
    public List<string?> LayoutNames { get; } = new();

    /// <summary>歌詞行の数。</summary>
    public int LyricLineCount => Document.Lines.Count(l => !l.IsEmpty);

    /// <summary>表示時刻が設定されている歌詞行の数。</summary>
    public int TimedLineCount => ShowTimes.Count(t => t is not null);
}

/// <summary>n3proj の表示時刻から推定した、ニコカラメーカーの表示時刻設定。</summary>
/// <param name="LeadMs">ワイプ前の表示時間。</param>
/// <param name="TailMs">ワイプ後の表示時間。</param>
/// <param name="IntervalMs">歌詞の表示間隔。</param>
/// <param name="Matched">この設定の自動計算で表示時刻が完全に一致した行数。</param>
/// <param name="Total">表示時刻が設定されている行数。</param>
public sealed record N3TimingEstimate(int LeadMs, int TailMs, int IntervalMs, int Matched, int Total);

/// <summary>n3proj の @Emoji（アイコン）1 件。画像のパスは絶対パスに直してある。</summary>
/// <param name="Entry">アイコンの定義（置き換える文字列・ワイプ前後の画像・オプション）。</param>
/// <param name="ImageExists">ワイプ前の画像ファイルが存在するか。</param>
/// <param name="SourceTab">定義されていた歌詞設定タブの名前。</param>
public sealed record N3ProjIcon(EmojiEntry Entry, bool ImageExists, string SourceTab);

/// <summary>n3proj の読み込み内容（読み込み確認画面に表示し、選んだ項目だけを取り込む）。</summary>
public sealed class N3ProjImportPreview
{
    public string Path { get; init; } = "";

    /// <summary>画面サイズ・主フォント・フォント設定名・レイアウト・実表示区間など。</summary>
    public N3ProjSettings Settings { get; init; } = null!;

    /// <summary>フォント設定（NicoKaraPrep のフォント設定として取り込める形）。</summary>
    public List<N3FontSet> FontSets { get; init; } = new();

    /// <summary>歌詞設定タブ（歌詞行のあるもの）。</summary>
    public List<N3ProjSourceTab> Tabs { get; init; } = new();

    /// <summary>表示時刻の設定の推定（表示時刻が無ければ null）。</summary>
    public N3TimingEstimate? Timing { get; init; }

    /// <summary>アイコン（@Emoji）の定義（置き換える文字列ごとに 1 件）。</summary>
    public List<N3ProjIcon> Icons { get; init; } = new();

    /// <summary>背景素材の動画（無ければ音声トラック）のパス。未設定なら null。</summary>
    public string? MediaPath { get; init; }

    /// <summary><see cref="MediaPath"/> のファイルが存在するか。</summary>
    public bool MediaExists => MediaPath is { Length: > 0 } p && File.Exists(p);

    /// <summary>メインの歌詞設定が「上段歌詞を長めに表示する」か。</summary>
    public bool MainTopLong => Tabs.FirstOrDefault()?.TopLong ?? false;
}

/// <summary>
/// ニコカラメーカー3 のプロジェクト（n3proj）を NicoKaraPrep へ読み込むための解析と照合。
/// 読み込みは <see cref="Analyze"/> で内容を調べ、確認画面で選んだ項目だけを呼び出し側が適用する。
/// </summary>
public static class N3ProjImport
{
    public static N3ProjImportPreview Analyze(string path, int intervalHintMs = 300)
    {
        var root = N3ProjFormat.ReadJsonObject(path);
        var tabs = ReadSourceTabs(root);
        return new N3ProjImportPreview
        {
            Path = path,
            Settings = N3ProjFormat.Read(path),
            FontSets = N3ProjFormat.ReadFontSets(path),
            Tabs = tabs,
            Timing = EstimateTiming(tabs, intervalHintMs),
            Icons = ReadIcons(root, path),
            MediaPath = ReadMediaPath(root, path),
        };
    }

    /// <summary>
    /// 歌詞設定タブに保存されている @Emoji（アイコン）を読み出す（AtTagsForSave）。
    /// 画像のパスが相対パスなら、ニコカラメーカーと同じく歌詞ファイルのフォルダ（不明ならプロジェクトのフォルダ）
    /// からのパスとして絶対パスにする。同じ置き換え文字列は最初の定義を使う。
    /// </summary>
    public static List<N3ProjIcon> ReadIcons(JsonObject root, string projectPath)
    {
        var result = new List<N3ProjIcon>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string projectDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(projectPath)) ?? "";
        if (root["SourceLyricsInfos"] is not JsonArray infos) return result;

        foreach (var node in infos)
        {
            if (node is not JsonObject info) continue;
            string tabName = info["SettingsName"]?.GetValue<string>() ?? "";
            string? lyricsPath = info["SourceLyricsPath"]?.GetValue<string>();
            string baseDir = !string.IsNullOrEmpty(lyricsPath) && System.IO.Path.GetDirectoryName(lyricsPath) is { Length: > 0 } d ? d : projectDir;
            string tags = info["AtTagsForSave"]?.GetValue<string>() ?? "";

            foreach (string rawLine in tags.Split('\n'))
            {
                string line = rawLine.Trim();
                if (!line.StartsWith("@Emoji=", StringComparison.OrdinalIgnoreCase)) continue;
                var e = EmojiEntry.ParseTagValue(line["@Emoji=".Length..]);
                if (e.ReplaceChar.Length == 0 || !seen.Add(e.ReplaceChar)) continue;
                e.ImageBefore = ResolvePath(e.ImageBefore, baseDir);
                if (!string.IsNullOrEmpty(e.ImageAfter)) e.ImageAfter = ResolvePath(e.ImageAfter, baseDir);
                result.Add(new N3ProjIcon(e, e.ImageBefore.Length > 0 && File.Exists(e.ImageBefore), tabName));
            }
        }
        return result;
    }

    /// <summary>背景素材の動画（なければ最初の音声トラック）のパス。相対パスも試す。</summary>
    public static string? ReadMediaPath(JsonObject root, string projectPath)
    {
        if (root["SourceInfo"] is not JsonObject source) return null;
        string projectDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(projectPath)) ?? "";

        string? Pick(string? absolute, string? relative)
        {
            if (!string.IsNullOrEmpty(absolute) && File.Exists(absolute)) return absolute;
            if (!string.IsNullOrEmpty(relative))
            {
                string candidate = ResolvePath(relative, projectDir);
                if (File.Exists(candidate)) return candidate;
            }
            return string.IsNullOrEmpty(absolute) ? null : absolute;
        }

        var movie = Pick(source["MoviePath"]?.GetValue<string>(), source["MovieRelativePath"]?.GetValue<string>());
        if (movie is not null) return movie;
        if (source["SoundTrackList"] is JsonArray tracks)
        {
            foreach (var t in tracks)
            {
                if (t is JsonObject track && Pick(track["Path"]?.GetValue<string>(), track["RelativePath"]?.GetValue<string>()) is string sound)
                {
                    return sound;
                }
            }
        }
        return Pick(source["SoundPath"]?.GetValue<string>(), source["SoundRelativePath"]?.GetValue<string>());
    }

    private static string ResolvePath(string path, string baseDir)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        try
        {
            return System.IO.Path.IsPathRooted(path) ? path : System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, path));
        }
        catch (Exception)
        {
            return path;
        }
    }

    /// <summary>
    /// 歌詞設定タブの歌詞ファイルを探す。プロジェクトのフォルダからの相対パスを先に、次に保存されている絶対パスを見る
    /// （プロジェクトをフォルダごと移した・別のドライブ名で開いたときも見つかるように）。見つからなければ null。
    /// </summary>
    public static string? FindLyricsFile(string projectPath, N3ProjSourceTab tab)
    {
        var candidates = new List<string>();
        try
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(projectPath));
            if (dir is not null && tab.LyricsRelativePath is { } rel) candidates.Add(Path.GetFullPath(Path.Combine(dir, rel)));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // 相対パスが壊れていれば絶対パスだけを見る
        }
        if (tab.LyricsPath is { Length: > 0 } abs) candidates.Add(abs);
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// 2 つのパスが同じ曲の歌詞か（同じファイル、または同じフォルダで拡張子だけが違う。rlf と、そこから作った lrc など）。
    /// </summary>
    public static bool IsSameLyrics(string a, string b)
    {
        try
        {
            string fa = Path.GetFullPath(a);
            string fb = Path.GetFullPath(b);
            if (string.Equals(fa, fb, StringComparison.OrdinalIgnoreCase)) return true;
            return string.Equals(Path.GetDirectoryName(fa), Path.GetDirectoryName(fb), StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetFileNameWithoutExtension(fa), Path.GetFileNameWithoutExtension(fb), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>歌詞設定タブを読み出す（歌詞行の無いタブは除く）。</summary>
    public static List<N3ProjSourceTab> ReadSourceTabs(JsonObject root)
    {
        var result = new List<N3ProjSourceTab>();
        if (root["SourceLyricsInfos"] is not JsonArray infos) return result;
        var layoutNames = (root["LyricsLayouts"] as JsonArray)?.Select(l => l?["SettingsName"]?.GetValue<string>()).ToList() ?? new List<string?>();

        foreach (var node in infos)
        {
            if (node is not JsonObject info || info["LineInfos"] is not JsonArray lines) continue;
            var tab = new N3ProjSourceTab
            {
                Name = info["SettingsName"]?.GetValue<string>() ?? "",
                LyricsPath = info["SourceLyricsPath"]?.GetValue<string>(),
                LyricsRelativePath = info["SourceLyricsRelativePath"]?.GetValue<string>() is { Length: > 0 } rel ? rel : null,
                TopLong = info["LastSelectedAddOns"]?["ShowTimeAdjusterId"]?.GetValue<string>() == "SHINTA.TopLongAdjuster",
            };

            bool pendingBreak = false;
            var usedLayouts = new HashSet<int>();
            bool layoutUnknown = false;
            foreach (var ln in lines)
            {
                if (ln is not JsonObject l) continue;
                int kind = l["Kind"]?.GetValue<int>() ?? 0;
                if (kind is 2 or 3)
                {
                    pendingBreak = true; // ページ区切り・段落区切り
                    continue;
                }
                if (kind != 1) continue; // 空行はページ区切りの合成行で代表させる

                if (pendingBreak && tab.Document.Lines.Count > 0)
                {
                    tab.Document.Lines.Add(new LyricsLine());
                    tab.ShowTimes.Add(null);
                    tab.LayoutNames.Add(null);
                }
                pendingBreak = false;

                string raw = l["Raw"]?.GetValue<string>() ?? "";
                int begin = l["ShowBeginTime"]?.GetValue<int>() ?? -1;
                int end = l["ShowEndTime"]?.GetValue<int>() ?? -1;
                tab.Document.Lines.Add(LrcFormat.ParseLyricLine(raw));
                tab.ShowTimes.Add(begin >= 0 && end >= begin ? (begin, end) : null);
                if (l["LayoutIndex"] is JsonValue li && li.TryGetValue(out int layoutIndex))
                {
                    usedLayouts.Add(layoutIndex);
                    tab.LayoutNames.Add(layoutIndex >= 0 && layoutIndex < layoutNames.Count && layoutNames[layoutIndex] is { Length: > 0 } layoutName ? layoutName : null);
                }
                else
                {
                    layoutUnknown = true;
                    tab.LayoutNames.Add(null);
                }
            }

            if (!layoutUnknown && usedLayouts.Count == 1 && usedLayouts.First() is int only && only >= 0 && only < layoutNames.Count)
            {
                tab.LayoutName = layoutNames[only] is { Length: > 0 } name ? name : null;
            }
            if (tab.LyricLineCount > 0) result.Add(tab);
        }
        return result;
    }

    /// <summary>
    /// ニコカラメーカーが使った表示時刻設定（ワイプ前・ワイプ後・表示間隔）を推定する。
    /// 候補の組み合わせで自動計算し、実際の表示時刻と一致する行が最も多いものを選ぶ
    /// （手動調整された行や詰められた行があっても推定できるよう、単純な最頻値にはしない）。
    /// </summary>
    public static N3TimingEstimate? EstimateTiming(IReadOnlyList<N3ProjSourceTab> tabs, int intervalHintMs = 300)
    {
        var preVotes = new Dictionary<int, int>();
        var postVotes = new Dictionary<int, int>();
        int total = 0;
        foreach (var tab in tabs)
        {
            foreach (var page in tab.Document.GetPages(PageSplitMode.EmptyLine))
            {
                int? pageFirst = page.Select(i => N3ShowTimePlanner.SingStartMs(tab.Document.Lines[i])).Where(t => t is not null).Min();
                foreach (int i in page)
                {
                    if (tab.ShowTimes[i] is not (int b, int e)) continue;
                    total++;
                    if (pageFirst is int f) Vote(preVotes, f - b);
                    if (N3ShowTimePlanner.SingEndMs(tab.Document.Lines[i]) is int last) Vote(postVotes, e - last);
                }
            }
        }
        if (total == 0) return null;

        var preCandidates = Top(preVotes, 3, v => v > 0, minCount: 1);
        var postCandidates = Top(postVotes, 8, v => v >= 0, minCount: 2);
        if (postCandidates.Count == 0) postCandidates = Top(postVotes, 1, v => v >= 0, minCount: 1);
        if (preCandidates.Count == 0) preCandidates.Add(1500);
        if (postCandidates.Count == 0) postCandidates.Add(800);
        var intervalCandidates = new[] { intervalHintMs, 300 }.Where(v => v >= 0).Distinct().ToList();

        N3TimingEstimate? best = null;
        foreach (int interval in intervalCandidates)
        {
            foreach (int pre in preCandidates)
            {
                foreach (int post in postCandidates)
                {
                    int matched = 0;
                    foreach (var tab in tabs)
                    {
                        var plan = N3ShowTimePlanner.Plan(tab.Document, new N3ShowTimeSettings
                        {
                            LeadMs = pre,
                            TailMs = post,
                            IntervalMs = interval,
                            TopLong = tab.TopLong,
                        });
                        for (int i = 0; i < tab.ShowTimes.Count; i++)
                        {
                            if (tab.ShowTimes[i] is (int b, int e) && plan.TryGetValue(i, out var p) && p.BeginMs == b && p.EndMs == e)
                            {
                                matched++;
                            }
                        }
                    }
                    if (best is null || matched > best.Matched)
                    {
                        best = new N3TimingEstimate(pre, post, interval, matched, total);
                    }
                }
            }
        }
        return best;
    }

    private static void Vote(Dictionary<int, int> votes, int value) => votes[value] = votes.GetValueOrDefault(value) + 1;

    private static List<int> Top(Dictionary<int, int> votes, int count, Func<int, bool> filter, int minCount) =>
        votes.Where(kv => kv.Value >= minCount && filter(kv.Key))
             .OrderByDescending(kv => kv.Value)
             .Take(count)
             .Select(kv => kv.Key)
             .ToList();

    // ------------------------------------------------------------ 行の照合

    /// <summary>
    /// NicoKaraPrep のドキュメントの歌詞行と n3proj の歌詞行を、行の内容（lrc の行テキスト）で
    /// 順序を保って対応付ける（最長共通部分列）。歌詞を直した行は対応しない。
    /// 戻り値は ドキュメントの行インデックス → ニコカラメーカーの表示時刻（ms）。
    /// </summary>
    /// <param name="matcher">
    /// 絵文字（＿などのプレースホルダを含む）。指定すると、両方の行の絵文字の単位のタグを除いて比べる
    /// （書き出しで絵文字の開始を表示開始へ寄せた行や、絵文字の表示秒数を変えた行も対応させるため）。null なら行の lrc テキストそのもので比べる。
    /// </param>
    public static Dictionary<int, (int BeginMs, int EndMs)> MatchLines(LyricsDocument doc, N3ProjSourceTab source, EmojiMatcher? matcher = null) =>
        MatchLineIndexes(doc, source, matcher).ToDictionary(kv => kv.Key, kv => source.ShowTimes[kv.Value]!.Value);

    /// <summary>
    /// <see cref="MatchLines"/> と同じ対応付けで、ドキュメントの行インデックス → n3proj の歌詞設定タブの行インデックス（<see cref="N3ProjSourceTab.Document"/> の添字）を返す
    /// （表示時刻が設定された行だけ）。
    /// </summary>
    /// <param name="matcher">絵文字（＿を含む）。指定すると、両方の行の絵文字の単位のタグを除いて比べる（<see cref="MatchLines"/>）。</param>
    public static Dictionary<int, int> MatchLineIndexes(LyricsDocument doc, N3ProjSourceTab source, EmojiMatcher? matcher = null)
    {
        var docRows = new List<(int Index, string Key)>();
        for (int i = 0; i < doc.Lines.Count; i++)
        {
            if (!doc.Lines[i].IsEmpty) docRows.Add((i, MatchKey(doc.Lines[i], matcher)));
        }
        var srcRows = new List<(int Index, string Key)>();
        for (int k = 0; k < source.Document.Lines.Count; k++)
        {
            if (source.ShowTimes[k] is not null) srcRows.Add((k, MatchKey(source.Document.Lines[k], matcher)));
        }

        int n = docRows.Count, m = srcRows.Count;
        var lcs = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
        {
            for (int j = m - 1; j >= 0; j--)
            {
                lcs[i, j] = docRows[i].Key == srcRows[j].Key
                    ? lcs[i + 1, j + 1] + 1
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var result = new Dictionary<int, int>();
        int a = 0, c = 0;
        while (a < n && c < m)
        {
            if (docRows[a].Key == srcRows[c].Key)
            {
                result[docRows[a].Index] = srcRows[c].Index;
                a++;
                c++;
            }
            else if (lcs[a + 1, c] >= lcs[a, c + 1])
            {
                a++;
            }
            else
            {
                c++;
            }
        }
        return result;
    }

    /// <summary>
    /// 行の照合のキー: 行の lrc テキスト。matcher があれば、絵文字・＿の出現の単位のタグを除く
    /// （絵文字の開始タグは、書き出しでの寄せや表示秒数の変更で変わるため）。
    /// </summary>
    private static string MatchKey(LyricsLine line, EmojiMatcher? matcher)
    {
        if (matcher is null || matcher.IsEmpty) return LrcFormat.WriteLyricLine(line);
        var emojiUnits = N3EmojiLead.UnitIndexes(line, matcher);
        if (emojiUnits.Count == 0) return LrcFormat.WriteLyricLine(line);

        var sb = new System.Text.StringBuilder();
        for (int k = 0; k < line.Chars.Count; k++)
        {
            var c = line.Chars[k];
            if (c.TimeCs is int t && !emojiUnits.Contains(k)) sb.Append(TimeTag.Format(t));
            if (!c.IsSpacer) sb.Append(c.Text);
        }
        if (line.EndTimeCs is int end) sb.Append(TimeTag.Format(end));
        return sb.ToString();
    }

    /// <summary>
    /// NicoKaraPrep のタブに対応する n3proj の歌詞設定タブを選ぶ
    /// （同じ名前で 1 行以上対応するもの → 対応する行が最も多いもの）。
    /// </summary>
    /// <param name="matcher">絵文字（＿を含む）。行の照合で絵文字の単位のタグを除く（<see cref="MatchLines"/>）。</param>
    public static (N3ProjSourceTab? Tab, Dictionary<int, (int BeginMs, int EndMs)> Lines) FindSource(
        LyricsDocument doc, string tabName, IReadOnlyList<N3ProjSourceTab> sources, EmojiMatcher? matcher = null)
    {
        var same = sources.FirstOrDefault(s => s.Name == tabName);
        if (same is not null)
        {
            var lines = MatchLines(doc, same, matcher);
            if (lines.Count > 0) return (same, lines);
        }

        N3ProjSourceTab? best = null;
        var bestLines = new Dictionary<int, (int, int)>();
        foreach (var s in sources)
        {
            var lines = MatchLines(doc, s, matcher);
            if (lines.Count > bestLines.Count)
            {
                best = s;
                bestLines = lines;
            }
        }
        return (best, bestLines);
    }

    /// <summary>
    /// ニコカラメーカーの表示時刻を、NicoKaraPrep の自動計算と異なる行だけ行ごとの手動指定として取り込む。
    /// 手動指定は前後の行の自動計算にも影響するため、変化がなくなるまで繰り返す。
    /// 絵文字の先行を譲る規則（<see cref="N3ShowTimeSettings.EmojiLeadYield"/>）がオンなら、規則なし（ニコカラメーカー3 と同じ計算）の値と
    /// 一致する値も自動のままにする（ニコカラメーカー3 が自動設定したプロジェクトを読んでも、詰められた行が手動指定にならないように）。
    /// 規則オンで書き出した後にニコカラメーカー3 で自動設定をやり直した値（寄せた歌詞を規則なしで計算した値）も自動のままにする。
    /// 戻り値は手動指定を設定（変更）した行の集合。
    /// </summary>
    public static HashSet<int> ApplyShowTimes(
        LyricsDocument doc,
        IReadOnlyDictionary<int, (int BeginMs, int EndMs)> actual,
        N3ShowTimeSettings settings)
    {
        N3ShowTimeSettings? plain = null; // 規則なしの設定（規則がオンのときだけ）
        if (settings.YieldsEmojiLead)
        {
            plain = N3ProjWriter.CloneShowSettings(settings);
            plain.EmojiLeadYield = false;
        }

        var touched = new HashSet<int>();
        for (int iteration = 0; iteration < 8; iteration++)
        {
            var plan = N3ShowTimePlanner.Plan(doc, settings);
            var plainPlan = plain is null ? null : N3ShowTimePlanner.Plan(doc, plain);
            // 規則オンで書き出したプロジェクトでニコカラメーカー3 が表示時刻の自動設定をやり直した値
            // （絵文字の開始を寄せた歌詞＝書き出しと同じ写しを、規則なしで計算した値）
            var clampedPlan = plain is null ? null : N3ShowTimePlanner.Plan(N3EmojiLead.ClampDocument(doc, plan, settings.LeadMatcher), plain);
            bool changed = false;
            foreach (var (i, (begin, end)) in actual)
            {
                if (i < 0 || i >= doc.Lines.Count || !plan.TryGetValue(i, out var p)) continue;
                var line = doc.Lines[i];
                N3LinePlan? q = null, r = null;
                plainPlan?.TryGetValue(i, out q);
                clampedPlan?.TryGetValue(i, out r);
                if (!IsNear(p.BeginMs, begin) && !(q is not null && IsNear(q.BeginMs, begin)) && !(r is not null && IsNear(r.BeginMs, begin)))
                {
                    line.ShowBeginCs = ToCs(begin);
                    touched.Add(i);
                    changed = true;
                }
                if (!IsNear(p.EndMs, end) && !(q is not null && IsNear(q.EndMs, end)) && !(r is not null && IsNear(r.EndMs, end)))
                {
                    line.ShowEndCs = ToCs(end);
                    touched.Add(i);
                    changed = true;
                }
            }
            if (!changed) break;
        }
        return touched;
    }

    /// <summary>自動計算の値と実際の値が一致するとみなせるか（5ms 以内）。</summary>
    private static bool IsNear(int planMs, int actualMs) => Math.Abs(planMs - actualMs) <= 5;

    private static int ToCs(int ms) => (int)Math.Round(ms / 10.0, MidpointRounding.AwayFromZero);
}
