using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Formats;

/// <summary>n3proj へ書き出す歌詞設定タブ 1 つ分（ニコカラメーカーの歌詞設定パネルのタブに対応）。</summary>
public sealed class N3ProjExportTab
{
    /// <summary>歌詞設定の名称（メイン / コーラス1 / パート名 など）。</summary>
    public string Name { get; set; } = "メイン";

    public LyricsDocument Document { get; set; } = new();

    /// <summary>書き出す lrc の絶対パス。</summary>
    public string LyricsPath { get; set; } = "";

    /// <summary>固定レイアウト名（null / 空 = ページの行数に応じて自動選択）。</summary>
    public string? LayoutName { get; set; }

    /// <summary>上段の行を長めに表示するか（null = 全体設定に従う）。</summary>
    public bool? TopLong { get; set; }
}

/// <summary>n3proj 書き出しのオプション。</summary>
public sealed class N3ProjExportOptions
{
    /// <summary>ベースにする既存 n3proj（背景素材・フォント・レイアウト・タイトル等を引き継ぐ）。null = 標準設定で生成。</summary>
    public string? BaseProjectPath { get; set; }

    /// <summary>読み込み済みのベース（テスト用。指定時は BaseProjectPath より優先）。</summary>
    public JsonObject? BaseProject { get; set; }

    /// <summary>プロジェクト名（空 = ファイル名）。</summary>
    public string ProjectName { get; set; } = "";

    /// <summary>背景素材にする動画（または音声）ファイル。null = ベースのまま。</summary>
    public string? MediaPath { get; set; }

    /// <summary>ベース無しのときの画面サイズ。</summary>
    public int ScreenWidth { get; set; } = 1920;

    public int ScreenHeight { get; set; } = 1080;

    /// <summary>行の表示時刻の計算設定。</summary>
    public N3ShowTimeSettings ShowTime { get; set; } = new();

    /// <summary>実効 @Emoji リスト（lrc のヘッダ出力とインライングラフィックス判定に使用）。</summary>
    public IReadOnlyList<EmojiEntry> EmojiEntries { get; set; } = Array.Empty<EmojiEntry>();

    /// <summary>NicoKaraPrep 側で定義したフォント設定（同名のベース設定を上書き、無ければ追加）。</summary>
    public IReadOnlyList<N3FontSet> FontSets { get; set; } = Array.Empty<N3FontSet>();

    public bool MergeFontSets { get; set; } = true;

    /// <summary>ベース無し・フォント設定無しのときに作る唯一のフォント設定。</summary>
    public N3FontSet? DefaultFont { get; set; }

    /// <summary>行の既定フォント設定名（パート記号が現れるまで適用。null = 先頭の設定）。</summary>
    public string? DefaultFontSetName { get; set; }

    /// <summary>パート記号によるフォント切り替えを次の行以降にも引き継ぐ（ニコカラメーカーの「行が変わっても維持」）。</summary>
    public bool ContinueFontAcrossLines { get; set; } = true;

    /// <summary>レイアウト自動選択の対象範囲（ニコカラメーカーの「適用対象レイアウト」。null = 全部）。</summary>
    public string? LayoutSelectableBegin { get; set; }

    public string? LayoutSelectableEnd { get; set; }

    /// <summary>字幕アクション「文字単位フェード」の設定（ニコカラメーカーのアドオン設定 JSON。null = 既定値）。</summary>
    public JsonObject? CharFadeSettings { get; set; }

    /// <summary>CreateAppVer / ModifyAppVer に書くバージョン文字列。</summary>
    public string AppVersion { get; set; } = N3ProjWriter.DefaultAppVersion;

    /// <summary>lrc の文字コード（ニコカラメーカー推奨の BOM 付き UTF-8 が既定）。</summary>
    public Encoding LrcEncoding { get; set; } = EncodingDetector.Utf8Bom;
}

/// <summary>書き出し結果。</summary>
public sealed record N3ProjExportResult(
    string ProjectPath,
    IReadOnlyList<string> LyricsPaths,
    int LyricsLineCount,
    int FontSetCount,
    IReadOnlyList<string> Warnings);

/// <summary>
/// ニコカラメーカー3 のプロジェクト（.n3proj）を書き出す。
///
/// n3proj は ZIP（エントリ名 "0"）に UTF-8 の JSON を 1 つ入れたもので、内容はニコカラメーカーの
/// ProjectDataModel（SourceInfo / SourceLyricsInfos / TitleInfos / LyricsFonts / LyricsLayouts …）。
/// 歌詞設定（SourceLyricsInfos）はニコカラメーカーがアドオン（行分け・フォント選択・レイアウト選択・
/// 表示時刻調整・字幕アクション）を実行した結果と同じ形で生成し、ニコカラメーカー側での再計算を不要にする。
/// 行の種別: 0=空行 1=歌詞 2=ページ区切り 3=段落区切り（空行の直前に挿入される合成行）。
/// 文字の時刻: 同じタグ区間の文字は 先頭=(開始,-1) 中間=(-1,-1) 末尾=(-1,終了)、1 文字なら (開始,終了)。
/// </summary>
public static class N3ProjWriter
{
    public const string DefaultAppVersion = "Ver 13.79";

    /// <summary>ニコカラメーカーの TIME_TAG_TIME_MAX。</summary>
    public const int TimeMax = 5999990;

    private static readonly DateTime NoTime = DateTime.MinValue;

    /// <summary>lrc を書き出し、n3proj を保存する。</summary>
    public static N3ProjExportResult Write(string projectPath, IReadOnlyList<N3ProjExportTab> tabs, N3ProjExportOptions options)
    {
        if (tabs.Count == 0) throw new ArgumentException("書き出すタブがありません", nameof(tabs));

        var warnings = new List<string>();
        JsonObject? baseRoot = options.BaseProject;
        if (baseRoot is null && !string.IsNullOrEmpty(options.BaseProjectPath))
        {
            baseRoot = N3ProjFormat.ReadJsonObject(options.BaseProjectPath);
        }

        var sources = new List<TabSource>();
        var lrcPaths = new List<string>();
        foreach (var tab in tabs)
        {
            if (string.IsNullOrEmpty(tab.LyricsPath))
            {
                throw new ArgumentException($"タブ「{tab.Name}」の歌詞ファイルのパスが指定されていません");
            }
            string lrcPath = Path.GetFullPath(tab.LyricsPath);
            string text = LrcFormat.Write(tab.Document, new LrcWriteOptions
            {
                EmojiEntriesOverride = options.EmojiEntries,
                BaseFolder = Path.GetDirectoryName(lrcPath),
            });
            Directory.CreateDirectory(Path.GetDirectoryName(lrcPath)!);
            File.WriteAllText(lrcPath, text, options.LrcEncoding);
            sources.Add(new TabSource(tab, lrcPath, text, File.GetLastWriteTime(lrcPath)));
            lrcPaths.Add(lrcPath);
        }

        var root = BuildProjectJson(projectPath, sources, options, baseRoot, warnings, out int lineCount, out int fontCount);
        SaveZip(projectPath, root);
        return new N3ProjExportResult(projectPath, lrcPaths, lineCount, fontCount, warnings);
    }

    /// <summary>n3proj の ZIP を書く（エントリ "0" に BOM 付き UTF-8 の JSON）。</summary>
    public static void SaveZip(string projectPath, JsonObject root)
    {
        string json = root.ToJsonString();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(projectPath))!);
        using var fs = File.Create(projectPath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        var entry = zip.CreateEntry(N3ProjFormat.ZipEntryName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        writer.Write(json);
    }

    /// <summary>歌詞設定タブと、書き出した lrc の内容。</summary>
    internal sealed record TabSource(N3ProjExportTab Tab, string LyricsPath, string LrcText, DateTime LyricsLastModified);

    /// <summary>プロジェクト全体の JSON を組み立てる（ファイルは書かない）。</summary>
    internal static JsonObject BuildProjectJson(
        string projectPath,
        IReadOnlyList<TabSource> sources,
        N3ProjExportOptions options,
        JsonObject? baseRoot,
        List<string> warnings,
        out int lineCount,
        out int fontCount)
    {
        string ver = string.IsNullOrWhiteSpace(options.AppVersion) ? DefaultAppVersion : options.AppVersion;
        string projectDir = Path.GetDirectoryName(Path.GetFullPath(projectPath)) ?? "";
        var root = baseRoot ?? NewRoot(ver);

        // ---- 背景素材 ----
        if (root["SourceInfo"] is not JsonObject source)
        {
            source = NewSourceInfo(options.ScreenWidth, options.ScreenHeight, ver);
            root["SourceInfo"] = source;
        }
        int height = source["BackgroundHeight"]?.GetValue<int>() ?? options.ScreenHeight;
        if (height <= 0) height = 1080;
        ApplyMedia(source, options.MediaPath, projectDir, ver);

        // ---- フォント設定 ----
        if (root["LyricsFonts"] is not JsonArray fonts)
        {
            fonts = new JsonArray();
            root["LyricsFonts"] = fonts;
        }
        if (options.MergeFontSets)
        {
            foreach (var f in options.FontSets) MergeFontSet(fonts, f, height, ver);
        }
        if (fonts.Count == 0)
        {
            fonts.Add(NewFontSet(options.DefaultFont ?? new N3FontSet { Name = "標準" }, height, ver));
        }
        RenumberSettings(fonts);
        fontCount = fonts.Count;
        var fontNames = fonts.Select((n, i) => (Name: n?["SettingsName"]?.GetValue<string>() ?? "", Index: i)).ToList();

        // ---- レイアウト設定 ----
        if (root["LyricsLayouts"] is not JsonArray layouts || layouts.Count == 0)
        {
            layouts = NewDefaultLayouts(height, ver);
            root["LyricsLayouts"] = layouts;
        }
        RenumberSettings(layouts);
        var layoutInfos = layouts.Select((n, i) => new N3ProjLayoutInfo(
            n?["SettingsName"]?.GetValue<string>() ?? "",
            i,
            n?["HorizontalAlignments"] is JsonArray ha ? Math.Max(1, ha.Count) : 1)).ToList();

        // ---- タイトル ----
        if (root["TitleInfos"] is not JsonArray titles || titles.Count == 0)
        {
            root["TitleInfos"] = NewDefaultTitles(layoutInfos, ver);
        }

        // ---- 歌詞設定 ----
        var infos = new JsonArray();
        lineCount = 0;
        // 全タブで 1 つを共有する（前のタブの最後のフォントを次のタブへ引き継ぐ。ニコカラメーカー3 の動作は未確認）
        var fontResolver = new N3FontResolver(fontNames.Select(f => f.Name).ToList(), options.DefaultFontSetName, options.ContinueFontAcrossLines);
        var action = ResolveSubtitleAction(baseRoot, options.CharFadeSettings, ver);
        for (int t = 0; t < sources.Count; t++)
        {
            var src = sources[t];
            var show = CloneShowSettings(options.ShowTime);
            show.TopLong = src.Tab.TopLong ?? options.ShowTime.TopLong;
            var layoutResolver = new LayoutResolver(layoutInfos, src.Tab.LayoutName, options.LayoutSelectableBegin, options.LayoutSelectableEnd, warnings, src.Tab.Name);
            var lines = BuildLineInfos(src.Tab.Document, show, options.EmojiEntries, fontResolver, layoutResolver, action, ver, out int count);
            lineCount += count;
            infos.Add(BuildLyricsInfo(src, t, projectDir, lines, show, ver));
        }
        for (int t = sources.Count; t < 3; t++)
        {
            infos.Add(EmptyLyricsInfo(t, options.ShowTime, ver));
        }
        root["SourceLyricsInfos"] = infos;

        // ---- プロジェクト自体 ----
        string name = !string.IsNullOrWhiteSpace(options.ProjectName)
            ? options.ProjectName.Trim()
            : Path.GetFileNameWithoutExtension(projectPath);
        root["SettingsName"] = name;
        root["Index"] = 0;
        root["Synchronize"] = false;
        root["SynchronizedTime"] = NoTime;
        root["Guid"] = Guid.NewGuid().ToString();
        root["LastModified"] = DateTime.UtcNow;
        if (root["CreateAppVer"] is null) root["CreateAppVer"] = ver;
        root["ModifyAppVer"] = ver;
        if (!root.ContainsKey("DestPath")) root["DestPath"] = null;
        if (root["DestFormat"] is null) root["DestFormat"] = 1;
        if (root["Mp4SettingsIndex"] is null) root["Mp4SettingsIndex"] = 0;
        if (root["DoubleFrame"] is null) root["DoubleFrame"] = false;
        return root;
    }

    // ------------------------------------------------------------ 歌詞設定

    private static JsonObject BuildLyricsInfo(TabSource src, int index, string projectDir, JsonArray lines, N3ShowTimeSettings show, string ver)
    {
        string atTags = string.Join("\n",
            src.LrcText.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.StartsWith('@')));
        var o = new JsonObject
        {
            ["SourceLyricsPath"] = src.LyricsPath,
            ["SourceLyricsRelativePath"] = RelativePath(projectDir, src.LyricsPath),
            ["SourceLyricsLastModified"] = src.LyricsLastModified,
            ["LastSelectedAddOns"] = AddOns(show),
            ["AtTagsForSave"] = atTags,
            ["LineInfos"] = lines,
        };
        AddSettingsName(o, src.Tab.Name, index);
        AddIdentity(o, ver, DateTime.UtcNow, ver);
        return o;
    }

    private static JsonObject EmptyLyricsInfo(int index, N3ShowTimeSettings show, string ver)
    {
        var o = new JsonObject
        {
            ["SourceLyricsPath"] = null,
            ["SourceLyricsRelativePath"] = "",
            ["SourceLyricsLastModified"] = NoTime,
            ["LastSelectedAddOns"] = AddOns(show),
            ["AtTagsForSave"] = "",
            ["LineInfos"] = new JsonArray(),
        };
        AddSettingsName(o, index == 0 ? "メイン" : $"コーラス{index}", index);
        AddIdentity(o, ver, NoTime, ver);
        return o;
    }

    private static JsonObject AddOns(N3ShowTimeSettings show) => new()
    {
        ["LineBreakerId"] = show.PageMode == PageSplitMode.FixedLineCount ? "SHINTA.SeqLinesBreaker" : "SHINTA.EmptyLineBreaker",
        ["FontSelectorId"] = "SHINTA.PartFontSelector",
        ["LayoutSelectorId"] = "SHINTA.LinesLayoutSelector",
        ["ShowTimeAdjusterId"] = show.TopLong ? "SHINTA.TopLongAdjuster" : "SHINTA.TopShortAdjuster",
        ["SubtitleActionSelectorId"] = "SHINTA.UnificationSubtitleActionSelector",
    };

    /// <summary>
    /// 歌詞行（LineInfos）を生成する。空行の直前にページ区切り／段落区切りの合成行を挿入し、
    /// 各歌詞行に文字の時刻・フォント・レイアウト・表示時刻・字幕アクションを付ける。
    /// </summary>
    internal static JsonArray BuildLineInfos(
        LyricsDocument doc,
        N3ShowTimeSettings show,
        IReadOnlyList<EmojiEntry> emoji,
        N3FontResolver fonts,
        LayoutResolver layouts,
        (string Id, JsonObject Settings) action,
        string ver,
        out int lyricCount)
    {
        var arr = new JsonArray();
        var plans = N3ShowTimePlanner.Plan(doc, show);
        var pages = doc.GetPages(show.PageMode, show.FixedLineCount);
        var layoutOfLine = new Dictionary<int, int>();
        foreach (var page in pages)
        {
            int layout = layouts.Resolve(page.Count);
            foreach (int i in page) layoutOfLine[i] = layout;
        }
        var matcher = new EmojiMatcher(emoji.Select(e => e.ReplaceChar));

        int pendingBlank = 0;
        bool started = false;
        int? prevLastMs = null;
        int lyricsSincePage = 0;
        lyricCount = 0;

        for (int i = 0; i < doc.Lines.Count; i++)
        {
            var line = doc.Lines[i];
            if (line.IsEmpty)
            {
                pendingBlank++;
                continue;
            }

            bool breakHere = started && (show.PageMode == PageSplitMode.FixedLineCount
                ? lyricsSincePage >= show.FixedLineCount
                : pendingBlank > 0);
            if (breakHere)
            {
                int kind = 2; // ページ区切り
                if (prevLastMs is int pl && N3ShowTimePlanner.SingStartMs(line) is int nf && show.IsParagraphGap(pl, nf))
                {
                    kind = 3; // 段落区切り（間奏など）
                }
                arr.Add(LineInfo(kind, new JsonArray(), -1, -1, "", NoActionSettings(ver), -1, "", ver));
                lyricsSincePage = 0;
            }
            for (int b = 0; b < pendingBlank; b++)
            {
                arr.Add(LineInfo(0, new JsonArray(), -1, -1, "", NoActionSettings(ver), -1, "", ver));
            }
            pendingBlank = 0;
            started = true;
            lyricsSincePage++;

            var tokens = Tokenize(line, matcher);
            var fontByUnit = fonts.Resolve(line);
            var chars = BuildCharInfos(tokens, fontByUnit, line.EndTimeCs);
            plans.TryGetValue(i, out var plan);
            arr.Add(LineInfo(
                1,
                chars,
                plan?.BeginMs ?? -1,
                plan?.EndMs ?? -1,
                action.Id,
                (JsonObject)action.Settings.DeepClone(),
                layoutOfLine.GetValueOrDefault(i, -1),
                LrcFormat.WriteLyricLine(line),
                ver));
            lyricCount++;
            if (N3ShowTimePlanner.SingEndMs(line) is int last) prevLastMs = last;
        }
        // 末尾の空行（区切りは入らない）。lrc は最終行も改行で終わるため、ニコカラメーカーは
        // 最後の改行の後ろを空行 1 つとして数える（実プロジェクトとの照合で確認済み）。
        for (int b = 0; b < pendingBlank + 1; b++)
        {
            arr.Add(LineInfo(0, new JsonArray(), -1, -1, "", NoActionSettings(ver), -1, "", ver));
        }
        return arr;
    }

    /// <summary>歌詞行を構成する単位（1 文字、またはインライングラフィックスの置き換え文字列全体）。</summary>
    internal readonly record struct Token(string Text, bool IsEmoji, int? TagCs, int UnitIndex);

    /// <summary>行の CharUnit 列をトークンに分解する（絵文字は 1 トークン、スペーサーはタグだけの空トークン）。</summary>
    internal static List<Token> Tokenize(LyricsLine line, EmojiMatcher emoji)
    {
        var tokens = new List<Token>();
        var occurrences = emoji.IsEmpty ? new List<EmojiMatcher.Occurrence>() : emoji.FindOccurrences(line.Chars);
        int o = 0;
        int i = 0;
        while (i < line.Chars.Count)
        {
            if (o < occurrences.Count && occurrences[o].Start == i)
            {
                var occ = occurrences[o];
                int? tag = null;
                for (int k = occ.Start; k < occ.EndExclusive; k++)
                {
                    if (line.Chars[k].TimeCs is int t) { tag = t; break; }
                }
                tokens.Add(new Token(occ.Value, true, tag, i));
                i = occ.EndExclusive;
                o++;
                continue;
            }
            var c = line.Chars[i];
            tokens.Add(new Token(c.IsSpacer ? "" : c.Text, false, c.TimeCs, i));
            i++;
        }
        return tokens;
    }

    /// <summary>
    /// トークン列を LyricsCharInfos に変換する。同じタグ区間の文字グループは
    /// 先頭 (開始, -1)・中間 (-1, -1)・末尾 (-1, 終了)、1 文字なら (開始, 終了) とする（ニコカラメーカーと同じ）。
    /// 行頭のタグの無いグループは開始・終了とも最初のタグの時刻、行末のタグの無いグループは終了なし (-1)。
    /// </summary>
    internal static JsonArray BuildCharInfos(List<Token> tokens, int[] fontByUnit, int? lineEndCs)
    {
        var arr = new JsonArray();
        var group = new List<Token>();
        int begin = -1;

        void Flush(int end)
        {
            var real = group.Where(t => t.Text.Length > 0).ToList();
            for (int k = 0; k < real.Count; k++)
            {
                var t = real[k];
                int b = k == 0 ? begin : -1;
                int e = k == real.Count - 1 ? end : -1;
                arr.Add(new JsonObject
                {
                    ["Kind"] = t.IsEmoji ? 1 : 0,
                    ["Char"] = t.Text,
                    ["BeginTime"] = b,
                    ["EndTime"] = e,
                    ["FontIndex"] = t.UnitIndex < fontByUnit.Length ? fontByUnit[t.UnitIndex] : 0,
                    ["IsRuby"] = false,
                });
            }
            group.Clear();
        }

        foreach (var t in tokens)
        {
            if (t.TagCs is int tag)
            {
                // 行頭のタグの無い文字（行頭の絵文字など）は、最初のタグの時刻に始まり同じ時刻に終わる
                // （ニコカラメーカーと同じ。実プロジェクトで確認）
                if (begin < 0) begin = tag * 10;
                Flush(tag * 10);
                begin = tag * 10;
            }
            group.Add(t);
        }
        Flush(lineEndCs is int le ? le * 10 : -1);
        return arr;
    }

    private static JsonObject LineInfo(int kind, JsonArray chars, int showBegin, int showEnd, string actionId, JsonObject actionSettings, int layoutIndex, string raw, string ver) => new()
    {
        ["Kind"] = kind,
        ["LyricsCharInfos"] = chars,
        ["ShowBeginTime"] = showBegin,
        ["ShowEndTime"] = showEnd,
        ["SubtitleActionId"] = actionId,
        ["SubtitleActionSettings"] = actionSettings,
        ["LayoutIndex"] = layoutIndex,
        ["Raw"] = raw,
        ["Guid"] = Guid.NewGuid().ToString(),
        ["LastModified"] = NoTime,
        ["CreateAppVer"] = ver,
        ["ModifyAppVer"] = "",
    };

    private static JsonObject NoActionSettings(string ver) => new()
    {
        ["$type"] = "AddOnSettingsModel",
        ["CreateAppVer"] = ver,
        ["ModifyAppVer"] = "",
    };

    /// <summary>歌詞行の字幕アクション。ベースの最初の歌詞行の設定を引き継ぎ、無ければ文字単位フェードの既定値。</summary>
    private static (string Id, JsonObject Settings) ResolveSubtitleAction(JsonObject? baseRoot, JsonObject? charFade, string ver)
    {
        if (baseRoot?["SourceLyricsInfos"] is JsonArray infos)
        {
            foreach (var info in infos)
            {
                if (info?["LineInfos"] is not JsonArray lines) continue;
                foreach (var l in lines)
                {
                    if (l is JsonObject lo &&
                        (lo["Kind"]?.GetValue<int>() ?? -1) == 1 &&
                        lo["SubtitleActionId"]?.GetValue<string>() is { Length: > 0 } id &&
                        lo["SubtitleActionSettings"] is JsonObject st &&
                        st["$type"] is not null)
                    {
                        return (id, (JsonObject)st.DeepClone());
                    }
                }
            }
        }

        var s = new JsonObject
        {
            ["$type"] = "CharFadeInFadeOutSettingsModel",
            ["IntroDelay"] = charFade?["IntroDelay"]?.GetValue<int>() ?? 350,
            ["WholeFadeOut"] = charFade?["WholeFadeOut"]?.GetValue<bool>() ?? false,
            ["TailDelay"] = charFade?["TailDelay"]?.GetValue<int>() ?? 250,
            ["DelayInlineGraphics"] = charFade?["DelayInlineGraphics"]?.GetValue<bool>() ?? true,
            ["FadeInTime"] = charFade?["FadeInTime"]?.GetValue<int>() ?? 250,
            ["FadeOutTime"] = charFade?["FadeOutTime"]?.GetValue<int>() ?? 250,
            ["CreateAppVer"] = ver,
            ["ModifyAppVer"] = "",
        };
        return ("SHINTA.CharFadeInFadeOut", s);
    }

    private static N3ShowTimeSettings CloneShowSettings(N3ShowTimeSettings s) => new()
    {
        PageMode = s.PageMode,
        FixedLineCount = s.FixedLineCount,
        LeadMs = s.LeadMs,
        TailMs = s.TailMs,
        IntervalMs = s.IntervalMs,
        ProtectMs = s.ProtectMs,
        TopLong = s.TopLong,
        AlignFromTop = s.AlignFromTop,
        SingleLinePromoteGapMs = s.SingleLinePromoteGapMs,
    };

    // ------------------------------------------------------------ レイアウト選択（行数）

    /// <summary>ニコカラメーカーの「行数に応じてレイアウトを設定」相当（固定名の指定も可）。</summary>
    internal sealed class LayoutResolver
    {
        private readonly List<N3ProjLayoutInfo> _layouts;
        private readonly int? _fixed;
        private readonly int _rangeBegin;
        private readonly int _rangeEnd;
        private readonly Dictionary<int, int> _cache = new();

        public LayoutResolver(List<N3ProjLayoutInfo> layouts, string? fixedName, string? selectableBegin, string? selectableEnd, List<string> warnings, string tabName)
        {
            _layouts = layouts;
            if (!string.IsNullOrEmpty(fixedName))
            {
                var f = layouts.FirstOrDefault(l => l.Name == fixedName);
                if (f is null)
                {
                    warnings.Add($"タブ「{tabName}」のレイアウト「{fixedName}」がベースに無いため、行数から自動選択しました");
                }
                else
                {
                    _fixed = f.Index;
                }
            }
            _rangeBegin = 0;
            _rangeEnd = layouts.Count - 1;
            if (selectableBegin is not null && layouts.FirstOrDefault(l => l.Name == selectableBegin) is { } b) _rangeBegin = b.Index;
            if (selectableEnd is not null && layouts.FirstOrDefault(l => l.Name == selectableEnd) is { } e) _rangeEnd = e.Index;
            if (_rangeEnd < _rangeBegin)
            {
                _rangeBegin = 0;
                _rangeEnd = layouts.Count - 1;
            }
        }

        public int Resolve(int lineCount)
        {
            if (_fixed is int f) return f;
            if (_layouts.Count == 0) return 0;
            if (_cache.TryGetValue(lineCount, out int cached)) return cached;

            var inRange = _layouts.Where(l => l.Index >= _rangeBegin && l.Index <= _rangeEnd).ToList();
            var pick = inRange.FirstOrDefault(l => l.LineCount == lineCount)
                ?? _layouts.FirstOrDefault(l => l.LineCount == lineCount)
                ?? inRange.Where(l => l.LineCount > lineCount).OrderBy(l => l.LineCount).FirstOrDefault()
                ?? _layouts.Where(l => l.LineCount > lineCount).OrderBy(l => l.LineCount).FirstOrDefault()
                ?? _layouts.OrderByDescending(l => l.LineCount).First();
            _cache[lineCount] = pick.Index;
            return pick.Index;
        }
    }

    // ------------------------------------------------------------ 背景素材

    private static readonly string[] MovieExts = { ".mp4", ".avi", ".wmv" };
    private static readonly string[] SoundExts = { ".m4a", ".mp3", ".wav", ".wma" };

    private static JsonObject NewSourceInfo(int width, int height, string ver)
    {
        var o = new JsonObject
        {
            ["SourceKind"] = 0,
            ["MoviePath"] = null,
            ["MovieRelativePath"] = "",
            ["MoviePlusSound"] = false,
            ["BackgroundColor"] = ColorBind("000000", ver),
            ["BackgroundWidth"] = width > 0 ? width : 1920,
            ["BackgroundHeight"] = height > 0 ? height : 1080,
            ["ImagePath"] = null,
            ["ImageRelativePath"] = "",
            ["Fps"] = 30,
            ["SoundTrackList"] = new JsonArray(),
            ["SoundPath"] = null,
        };
        AddIdentity(o, ver, NoTime, "");
        return o;
    }

    private static void ApplyMedia(JsonObject source, string? mediaPath, string projectDir, string ver)
    {
        if (string.IsNullOrWhiteSpace(mediaPath)) return;
        string ext = Path.GetExtension(mediaPath).ToLowerInvariant();
        if (MovieExts.Contains(ext))
        {
            source["SourceKind"] = 0;
            source["MoviePath"] = mediaPath;
            source["MovieRelativePath"] = RelativePath(projectDir, mediaPath);
            return;
        }
        if (SoundExts.Contains(ext) && string.IsNullOrEmpty(source["MoviePath"]?.GetValue<string>()))
        {
            source["SourceKind"] = 3; // 背景色
            var track = new JsonObject
            {
                ["Path"] = mediaPath,
                ["RelativePath"] = RelativePath(projectDir, mediaPath),
                ["AudioStreamIndex"] = 0,
                ["Name"] = null,
            };
            AddIdentity(track, ver, NoTime, "");
            source["SoundTrackList"] = new JsonArray(track);
        }
    }

    // ------------------------------------------------------------ フォント設定

    private static readonly string[] BrushNames =
    {
        "ワイプ後／文字色", "縁取り色", "縁取り 2 色", "飾り色",
        "ワイプ前／文字色", "縁取り色", "縁取り 2 色", "飾り色",
    };

    /// <summary>同名のフォント設定があればフォント・単色・文字飾りを上書きし、無ければ追加する。</summary>
    private static void MergeFontSet(JsonArray fonts, N3FontSet f, int reference, string ver)
    {
        if (string.IsNullOrWhiteSpace(f.Name)) return;
        var existing = fonts.OfType<JsonObject>().FirstOrDefault(s => s["SettingsName"]?.GetValue<string>() == f.Name);
        if (existing is null)
        {
            fonts.Add(NewFontSet(f, reference, ver));
            return;
        }

        if (existing["FontInfos"] is JsonArray fis)
        {
            if (fis.Count > 0 && fis[0] is JsonObject main)
            {
                main["FontName"] = f.FontFamily;
                main["FontFaceName"] = f.FontFace;
                main["CharSize"] = SizeAndRatio(f.SizePx, reference);
                main["EdgeSize"] = SizeAndRatio(f.EdgePx, reference);
                main["UseEdge2"] = f.UseEdge2;
                main["EdgeSize2"] = SizeAndRatio(f.Edge2Px, reference);
                main["ModifyAppVer"] = ver;
                main["LastModified"] = DateTime.UtcNow;
            }
            if (fis.Count > 3 && fis[3] is JsonObject ruby)
            {
                ruby["CharSize"] = SizeAndRatio(f.RubySizePx > 0 ? f.RubySizePx : f.SizePx / 2, reference);
                ruby["EdgeSize"] = SizeAndRatio(f.RubyEdgePx > 0 ? f.RubyEdgePx : f.EdgePx / 2, reference);
            }
        }
        if (existing["BrushInfos"] is JsonArray brushes)
        {
            string[] colors =
            {
                f.TextColorAfter, f.EdgeColorAfter, f.Edge2ColorAfter, f.DecorColorAfter,
                f.TextColorBefore, f.EdgeColorBefore, f.Edge2ColorBefore, f.DecorColorBefore,
            };
            for (int i = 0; i < colors.Length && i < brushes.Count; i++)
            {
                if (!N3FontSet.IsValidWeb16(colors[i]) || brushes[i] is not JsonObject b) continue;
                b["SelectedBrushTypeIndex"] = 0;
                b["SolidColor"] = ColorBind(colors[i], ver);
                b["ModifyAppVer"] = ver;
            }
        }
        existing["DecorKind"] = f.DecorKind;
        existing["DecorSize"] = SizeAndRatio(f.DecorSizePx, reference);
        existing["BlurLevel"] = f.BlurLevel;
        existing["ModifyAppVer"] = ver;
        existing["LastModified"] = DateTime.UtcNow;
    }

    /// <summary>フォント設定タブ 1 つ分を新規に作る（配色 8 件・フォントフェース 6 件）。</summary>
    internal static JsonObject NewFontSet(N3FontSet f, int reference, string ver)
    {
        string[] colors =
        {
            Or(f.TextColorAfter, "FFFFFF"), Or(f.EdgeColorAfter, "000000"), Or(f.Edge2ColorAfter, "FFFFFF"), Or(f.DecorColorAfter, "000000"),
            Or(f.TextColorBefore, "4DA3FF"), Or(f.EdgeColorBefore, "FFFFFF"), Or(f.Edge2ColorBefore, "000000"), Or(f.DecorColorBefore, "000000"),
        };
        var brushes = new JsonArray();
        for (int i = 0; i < 8; i++)
        {
            var b = new JsonObject
            {
                ["SelectedBrushTypeIndex"] = 0,
                ["SolidColor"] = ColorBind(colors[i], ver),
                ["GradientStops"] = new JsonArray(
                    GradientStop(0, 1f, 1f, 1f),
                    GradientStop(0.5f, 0.5019608f, 0.5019608f, 0.5019608f),
                    GradientStop(1, 0.5019608f, 0.5019608f, 0.5019608f)),
                ["BitmapPath"] = "",
                ["BitmapScale"] = 100,
            };
            AddSettingsName(b, BrushNames[i], 0);
            AddIdentity(b, ver, NoTime, "");
            brushes.Add(b);
        }

        double rubySize = f.RubySizePx > 0 ? f.RubySizePx : f.SizePx / 2;
        double rubyEdge = f.RubyEdgePx > 0 ? f.RubyEdgePx : f.EdgePx / 2;
        var fontInfos = new JsonArray(
            FontInfo("歌詞／漢字", f.FontFamily, f.FontFace, f.SizePx, f.EdgePx, f.UseEdge2, f.Edge2Px, "デフォルトフォント", reference, ver),
            FontInfo("かな", "", "", null, null, null, null, "歌詞／漢字", reference, ver),
            FontInfo("英数", "", "", null, null, null, null, "歌詞／漢字", reference, ver),
            FontInfo("ルビ／漢字", "", "", rubySize, rubyEdge, null, f.Edge2Px / 2, "歌詞／漢字", reference, ver),
            FontInfo("かな", "", "", null, null, null, null, "ルビ／漢字", reference, ver),
            FontInfo("英数", "", "", null, null, null, null, "ルビ／漢字", reference, ver));

        var set = new JsonObject
        {
            ["BrushInfos"] = brushes,
            ["FontInfos"] = fontInfos,
            ["DecorKind"] = f.DecorKind,
            ["DecorSize"] = SizeAndRatio(f.DecorSizePx, reference),
            ["BlurLevel"] = f.BlurLevel,
        };
        AddSettingsName(set, f.Name, 0);
        AddIdentity(set, ver, DateTime.UtcNow, "");
        return set;
    }

    private static string Or(string value, string fallback) => N3FontSet.IsValidWeb16(value) ? value.Trim().TrimStart('#').ToUpperInvariant() : fallback;

    private static JsonObject FontInfo(string name, string fontName, string face, double? sizePx, double? edgePx, bool? useEdge2, double? edge2Px, string fallback, int reference, string ver)
    {
        var o = new JsonObject
        {
            ["FontName"] = fontName,
            ["FontFaceName"] = face,
            ["CharSize"] = sizePx is double s ? SizeAndRatio(s, reference) : BlankSize(),
            ["XScale"] = 0,
            ["EdgeSize"] = edgePx is double e ? SizeAndRatio(e, reference) : BlankSize(),
            ["UseEdge2"] = useEdge2,
            ["EdgeSize2"] = edge2Px is double e2 ? SizeAndRatio(e2, reference) : BlankSize(),
            ["FallbackName"] = fallback,
        };
        AddSettingsName(o, name, 0);
        AddIdentity(o, ver, NoTime, "");
        return o;
    }

    // ------------------------------------------------------------ レイアウト・タイトル

    private static JsonArray NewDefaultLayouts(int reference, string ver)
    {
        var arr = new JsonArray
        {
            Layout("下寄せ1行", 2, new[] { 1 }, reference, ver),
            Layout("下寄せ2行", 2, new[] { 0, 2 }, reference, ver),
            Layout("下寄せ3行", 2, new[] { 0, 1, 2 }, reference, ver),
            Layout("上寄せ2行", 0, new[] { 0, 2 }, reference, ver),
            Layout("コーラス", 0, new[] { 1 }, reference, ver),
            Layout("タイトル左上", 0, new[] { 0 }, reference, ver),
            Layout("タイトル中央", 1, new[] { 1 }, reference, ver),
        };
        return arr;
    }

    private static JsonObject Layout(string name, int vertical, int[] horizontals, int reference, string ver)
    {
        var aligns = new JsonArray();
        foreach (int h in horizontals)
        {
            var a = new JsonObject { ["HorizontalLayoutAlignment"] = h };
            AddIdentity(a, ver, NoTime, "");
            aligns.Add(a);
        }
        var o = new JsonObject
        {
            ["SelectedVerticalAlignmentIndex"] = vertical,
            ["LineSpace"] = SizeAndRatio(60, reference),
            ["SmartHorizon"] = 2,
            ["VerticalMargin"] = SizeAndRatio(50, reference),
            ["HorizontalMargin"] = SizeAndRatio(50, reference),
            ["HorizontalAlignments"] = aligns,
            ["LyricsInterval"] = SizeAndRatio(0, reference),
            ["AllowBiting"] = false,
            ["RubyInterval"] = SizeAndRatio(0, reference),
            ["RubyAlignment"] = 0,
            ["LyricsAndRubyInterval"] = SizeAndRatio(0, reference),
        };
        AddSettingsName(o, name, 0);
        AddIdentity(o, ver, NoTime, "");
        return o;
    }

    private static JsonArray NewDefaultTitles(List<N3ProjLayoutInfo> layouts, string ver)
    {
        int titleLayout = layouts.FirstOrDefault(l => l.Name.Contains("タイトル"))?.Index ?? 0;
        var arr = new JsonArray();
        for (int i = 0; i < 3; i++)
        {
            var show = new JsonObject
            {
                ["Kind"] = 0,
                ["HeadOffset"] = 0,
                ["HeadEnd"] = TimeMax,
                ["Interval"] = 10000,
                ["TailOffset"] = 0,
                ["BeginTime"] = 0,
                ["EndTime"] = TimeMax,
                ["CreateAppVer"] = ver,
                ["ModifyAppVer"] = "",
            };
            var t = new JsonObject
            {
                ["ShowTime"] = show,
                ["LayoutIndex"] = titleLayout,
                ["LineInfos"] = new JsonArray(),
            };
            AddSettingsName(t, $"タイトル{i + 1}", i);
            AddIdentity(t, ver, NoTime, "");
            arr.Add(t);
        }
        return arr;
    }

    // ------------------------------------------------------------ 共通部品

    private static JsonObject NewRoot(string ver) => new()
    {
        ["SourceInfo"] = null,
        ["SourceLyricsInfos"] = new JsonArray(),
        ["TitleInfos"] = new JsonArray(),
        ["LyricsFonts"] = new JsonArray(),
        ["LyricsLayouts"] = new JsonArray(),
        ["DestPath"] = null,
        ["DestFormat"] = 1,
        ["Mp4SettingsIndex"] = 0,
        ["DoubleFrame"] = false,
        ["SettingsName"] = "",
        ["Index"] = 0,
        ["Synchronize"] = false,
        ["SynchronizedTime"] = NoTime,
        ["Guid"] = Guid.NewGuid().ToString(),
        ["LastModified"] = DateTime.UtcNow,
        ["CreateAppVer"] = ver,
        ["ModifyAppVer"] = ver,
    };

    /// <summary>SettingsName / Index / Synchronize / SynchronizedTime（SettingsNameModel 相当）。</summary>
    private static void AddSettingsName(JsonObject o, string name, int index)
    {
        o["SettingsName"] = name;
        o["Index"] = index;
        o["Synchronize"] = false;
        o["SynchronizedTime"] = NoTime;
    }

    /// <summary>Guid / LastModified / CreateAppVer / ModifyAppVer（UndoNotificationObject 相当）。</summary>
    private static void AddIdentity(JsonObject o, string createVer, DateTime lastModified, string modifyVer)
    {
        o["Guid"] = Guid.NewGuid().ToString();
        o["LastModified"] = lastModified;
        o["CreateAppVer"] = createVer;
        o["ModifyAppVer"] = modifyVer;
    }

    private static void RenumberSettings(JsonArray arr)
    {
        for (int i = 0; i < arr.Count; i++)
        {
            if (arr[i] is JsonObject o) o["Index"] = i;
        }
    }

    /// <summary>SizeAndRatio（px 値と画面高さに対する比率）。</summary>
    internal static JsonObject SizeAndRatio(double px, int reference)
    {
        if (px < 0) px = 0;
        return new JsonObject
        {
            ["Size"] = (int)Math.Round(px),
            ["Reference"] = reference,
            ["Ratio"] = reference > 0 ? px / reference : 0.0,
        };
    }

    /// <summary>未指定（継承）を表す SizeAndRatio。</summary>
    private static JsonObject BlankSize() => new() { ["Size"] = 0, ["Reference"] = 0, ["Ratio"] = 0 };

    /// <summary>ColorBindModel（DxColor + Web16）。</summary>
    internal static JsonObject ColorBind(string web16, string ver)
    {
        if (!N3FontSet.TryParseWeb16(web16, out byte r, out byte g, out byte b))
        {
            r = g = b = 0;
        }
        var o = new JsonObject
        {
            ["DxColor"] = Color4(r / 255f, g / 255f, b / 255f),
            ["Web16"] = $"{r:X2}{g:X2}{b:X2}",
        };
        AddIdentity(o, ver, NoTime, "");
        return o;
    }

    private static JsonObject Color4(float r, float g, float b)
    {
        float sum = r + g + b;
        return new JsonObject
        {
            ["R"] = r,
            ["G"] = g,
            ["B"] = b,
            ["A"] = 1f,
            ["SumRGB"] = sum,
            ["Average"] = sum / 3f,
            ["Luma"] = 0.299f * r + 0.587f * g + 0.114f * b,
        };
    }

    private static JsonObject GradientStop(float position, float r, float g, float b) => new()
    {
        ["Position"] = position,
        ["Color"] = Color4(r, g, b),
    };

    private static string RelativePath(string baseDir, string path)
    {
        try
        {
            if (string.IsNullOrEmpty(baseDir) || string.IsNullOrEmpty(path)) return path;
            return Path.GetRelativePath(baseDir, path);
        }
        catch (Exception)
        {
            return path;
        }
    }
}
