using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;
using NicoKaraPrep.Core.Project;

namespace NicoKaraPrep.App.ViewModels;

/// <summary>ニコカラメーカー3 プロジェクト（n3proj）書き出しと、行ごとの表示時刻・フォント設定。</summary>
public partial class MainViewModel
{
    /// <summary>曲ごとの n3proj 書き出し設定（.tttproj に保存）。</summary>
    public N3ProjSongSettings N3ProjSettings { get; private set; } = new();

    /// <summary>このマシンにインストールされたニコカラメーカー3 の設定（起動時に検出。無ければ null）。</summary>
    public Nkm3Environment? Nkm3Env { get; } = Nkm3Environment.Detect();

    /// <summary>
    /// アプリ設定から表示時刻計算の設定を作る。tabName を指定すると、そのタブの
    /// 「上段の表示（短め／長め）」の個別指定（n3proj 書き出し設定）を反映する。
    /// </summary>
    public N3ShowTimeSettings CreateShowTimeSettings(string? tabName = null) => new()
    {
        PageMode = Settings.PageMode,
        FixedLineCount = Settings.FixedLineCount,
        LeadMs = (int)Math.Round(Settings.DisplayLeadSeconds * 1000),
        TailMs = (int)Math.Round(Settings.DisplayTailSeconds * 1000),
        IntervalMs = (int)Math.Round(Settings.N3IntervalSeconds * 1000),
        ProtectMs = Settings.N3ProtectSeconds > 0 ? (int)Math.Round(Settings.N3ProtectSeconds * 1000) : null,
        TopLong = tabName is not null && N3ProjSettings.TabTopLong.TryGetValue(tabName, out bool topLong) ? topLong : Settings.N3TopLong,
        AlignFromTop = Settings.CollisionAlignFromTop,
    };

    /// <summary>表示中のドキュメントの行ごとの表示時刻（自動計算＋手動指定。ms）。</summary>
    public Dictionary<int, N3LinePlan> PlanShowTimes() =>
        N3ShowTimePlanner.Plan(Document, CreateShowTimeSettings(_activeTab.Name));

    /// <summary>行の表示開始・終了の手動指定を設定する（null = 自動に戻す）。</summary>
    public bool SetLineShowTime(int index, int? beginCs, int? endCs)
    {
        if (index < 0 || index >= Document.Lines.Count) return false;
        var line = Document.Lines[index];
        if (line.ShowBeginCs == beginCs && line.ShowEndCs == endCs) return false;
        PushUndo();
        line.ShowBeginCs = beginCs;
        line.ShowEndCs = endCs;
        MarkModified();
        SaveProject();
        return true;
    }

    /// <summary>
    /// 行に適用するフォント設定名の手動指定を設定する（null / 空 = 自動）。ニコカラメーカー3 で行のフォントを選んだときと同じく、
    /// その行の文字ごとの手動指定は消す（行全体が 1 つのフォント設定になる。自動に戻すときも文字の指定ごと戻す）。
    /// </summary>
    public bool SetLineFontSet(int index, string? name)
    {
        if (index < 0 || index >= Document.Lines.Count) return false;
        string? value = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        var line = Document.Lines[index];
        if (line.FontSetName == value && !line.HasCharFonts) return false;
        PushUndo();
        line.FontSetName = value;
        CharFontOperations.Clear(line);
        MarkModified();
        SaveProject();
        return true;
    }

    /// <summary>
    /// 行の文字の範囲（<see cref="LyricsLine.Chars"/> の添字、両端を含む）にフォント設定名を手動指定する（null / 空 = その文字を自動に戻す）。
    /// 元に戻す（Ctrl+Z）は 1 回で戻る。変わった文字の数を返す。
    /// </summary>
    public int SetCharFontSet(int index, int startUnit, int endUnit, string? name)
    {
        if (index < 0 || index >= Document.Lines.Count) return 0;
        var line = Document.Lines[index];
        if (line.IsEmpty) return 0;
        var probe = line.Clone();
        if (CharFontOperations.SetRange(probe, startUnit, endUnit, name) == 0) return 0;
        PushUndo();
        int changed = CharFontOperations.SetRange(line, startUnit, endUnit, name);
        MarkModified();
        SaveProject();
        return changed;
    }

    /// <summary>
    /// 複数の行に適用するフォント設定名の手動指定をまとめて設定する（null / 空 = 自動）。元に戻す（Ctrl+Z）は 1 回で全部戻る。
    /// 空行と、すでにその指定になっている行は変えない。変えた行の数を返す。
    /// </summary>
    public int SetLinesFontSet(IReadOnlyList<int> indexes, string? name)
    {
        string? value = string.IsNullOrWhiteSpace(name) ? null : name;
        var targets = indexes.Distinct()
            .Where(i => i >= 0 && i < Document.Lines.Count && !Document.Lines[i].IsEmpty &&
                        (Document.Lines[i].FontSetName != value || Document.Lines[i].HasCharFonts))
            .ToList();
        if (targets.Count == 0) return 0;
        PushUndo();
        foreach (int i in targets)
        {
            Document.Lines[i].FontSetName = value;
            CharFontOperations.Clear(Document.Lines[i]); // 行全体を 1 つのフォント設定にする（文字ごとの指定は消す）
        }
        MarkModified();
        SaveProject();
        return targets.Count;
    }

    /// <summary>選択行の手動指定（表示時刻・フォント）をすべて解除する。</summary>
    public int ClearLineOverrides(IReadOnlyList<int> indexes)
    {
        var targets = indexes.Where(i => i >= 0 && i < Document.Lines.Count && Document.Lines[i].HasN3Overrides).ToList();
        if (targets.Count == 0) return 0;
        PushUndo();
        foreach (int i in targets)
        {
            var line = Document.Lines[i];
            line.ShowBeginCs = null;
            line.ShowEndCs = null;
            line.FontSetName = null;
            line.LayoutName = null;
            line.FontSizeDelta = 0;
            CharFontOperations.Clear(line);
        }
        MarkModified();
        SaveProject();
        return targets.Count;
    }

    /// <summary>行ごとの表示開始の手動指定（ページ衝突チェック用）。</summary>
    private Dictionary<int, int>? BuildManualShowBegins()
    {
        var result = new Dictionary<int, int>();
        for (int i = 0; i < Document.Lines.Count; i++)
        {
            if (Document.Lines[i].ShowBeginCs is int b) result[i] = b;
        }
        return result.Count > 0 ? result : null;
    }

    /// <summary>ベース n3proj の候補（曲の設定 → 歌詞ファイルと同じフォルダ → 最後に使ったもの）。</summary>
    public string? SuggestN3ProjBasePath()
    {
        if (N3ProjSettings.BasePath is { Length: > 0 } b && File.Exists(b)) return b;
        string? mainPath = Tabs.FirstOrDefault(t => t.IsMain)?.FilePath ?? CurrentFilePath;
        if (mainPath is not null && N3ProjFormat.FindNear(mainPath) is string near) return near;
        if (Settings.N3LastBasePath.Length > 0 && File.Exists(Settings.N3LastBasePath)) return Settings.N3LastBasePath;
        return null;
    }

    /// <summary>n3proj の推奨保存先（前回の書き出し先 → 歌詞ファイルのフォルダ）。</summary>
    public string? SuggestN3ProjOutputPath()
    {
        if (N3ProjSettings.OutputPath is { Length: > 0 } o && Directory.Exists(Path.GetDirectoryName(o))) return o;
        string? folder = GetDefaultSaveFolder();
        string baseName = Tabs.FirstOrDefault(t => t.IsMain)?.FilePath is string mp
            ? Path.GetFileNameWithoutExtension(mp)
            : "lyrics";
        return folder is null ? null : Path.Combine(folder, baseName + ".n3proj");
    }

    /// <summary>書き出し対象になるタブ（空でないもの）。</summary>
    public List<TabState> GetN3ProjExportTabs()
    {
        StoreActiveTab();
        return Tabs.Where(t => t.Document.Lines.Any(l => !l.IsEmpty)).ToList();
    }

    /// <summary>
    /// ニコカラメーカー3 プロジェクトを書き出す。歌詞ファイル（lrc）はプロジェクトと同じフォルダに
    /// 「プロジェクト名.lrc」「プロジェクト名_タブ名.lrc」として書き出す
    /// （開いている歌詞ファイル自体は上書きしない）。
    /// </summary>
    public N3ProjExportResult ExportN3Proj(string projectPath, N3ProjSongSettings settings)
    {
        projectPath = Path.GetFullPath(projectPath);
        string dir = Path.GetDirectoryName(projectPath)!;
        string baseName = Path.GetFileNameWithoutExtension(projectPath);

        var openFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in Tabs)
        {
            if (t.FilePath is string fp) openFiles.Add(Path.GetFullPath(fp));
            if (t.CopyFilePath is string cp) openFiles.Add(Path.GetFullPath(cp));
        }

        var tabs = new List<N3ProjExportTab>();
        foreach (var tab in GetN3ProjExportTabs())
        {
            string lrcName = tab.IsMain ? baseName + ".lrc" : $"{baseName}_{SafeFileName(tab.Name)}.lrc";
            string lrcPath = Path.Combine(dir, lrcName);
            if (openFiles.Contains(lrcPath))
            {
                lrcPath = Path.Combine(dir, Path.GetFileNameWithoutExtension(lrcName) + "_ニコカラ.lrc");
            }
            tabs.Add(new N3ProjExportTab
            {
                Name = tab.Name,
                Document = tab.Document,
                LyricsPath = lrcPath,
                LayoutName = settings.TabLayouts.GetValueOrDefault(tab.Name) is { Length: > 0 } layout ? layout : null,
                TopLong = settings.TabTopLong.TryGetValue(tab.Name, out bool topLong) ? topLong : null,
            });
        }
        if (tabs.Count == 0) throw new InvalidOperationException("書き出す歌詞がありません");

        string? basePath = settings.BasePath is { Length: > 0 } bp && File.Exists(bp) ? bp : null;
        string? baseVersion = null;
        if (basePath is not null)
        {
            try { baseVersion = N3ProjFormat.Read(basePath).AppVersion; }
            catch (Exception) { baseVersion = null; }
        }

        int width = Settings.ScreenWidthPx > 0 ? Settings.ScreenWidthPx : 1920;
        var options = new N3ProjExportOptions
        {
            BaseProjectPath = basePath,
            ProjectName = settings.ProjectName is { Length: > 0 } pn ? pn : baseName,
            MediaPath = MediaPath,
            ScreenWidth = width,
            ScreenHeight = width == 1920 ? 1080 : (int)Math.Round(width * 9.0 / 16),
            ShowTime = CreateShowTimeSettings(),
            EmojiEntries = GetEffectiveEmojiList(),
            FontSets = ExportFontSets,
            MergeFontSets = settings.MergeFontSets,
            Layouts = Settings.N3Layouts,
            MergeLayouts = settings.MergeLayouts,
            DefaultFont = new N3FontSet
            {
                Name = "標準",
                FontFamily = Settings.FontFamily,
                FontFace = Settings.FontBold ? "Bold" : "",
                SizePx = Settings.FontSizePx,
                EdgePx = Settings.EdgeSizePx,
            },
            DefaultFontSetName = settings.DefaultFontSetName is { Length: > 0 } dn ? dn : null,
            LayoutSelectableBegin = Nkm3Env?.LayoutSelectableBegin,
            LayoutSelectableEnd = Nkm3Env?.LayoutSelectableEnd,
            CharFadeSettings = Nkm3Env?.CharFadeSettings,
            AppVersion = Nkm3Env?.AppVersion ?? baseVersion ?? N3ProjWriter.DefaultAppVersion,
            LrcEncoding = LrcEncoding,
        };

        var result = N3ProjWriter.Write(projectPath, tabs, options);

        settings.OutputPath = projectPath;
        N3ProjSettings = settings;
        Settings.N3LastBasePath = basePath ?? "";
        Settings.Save();
        SaveProject();
        RememberSaveFolder(projectPath);

        string warn = result.Warnings.Count > 0 ? $"　⚠ {string.Join(" / ", result.Warnings)}" : "";
        string sized = result.SizedFontSets is { Count: > 0 } names
            ? $"。ページの文字の大きさのために作ったフォント設定 {names.Count} 件（{string.Join("・", names.Take(3))}{(names.Count > 3 ? " など" : "")}）"
            : "";
        StatusText = $"ニコカラメーカー3 プロジェクトを書き出しました: {Path.GetFileName(projectPath)}" +
                     $"（歌詞 {result.LyricsLineCount} 行・歌詞ファイル {result.LyricsPaths.Count} 件・フォント設定 {result.FontSetCount} 件{sized}）{warn}";
        return result;
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        string s = new string(chars).Trim();
        return s.Length == 0 ? "tab" : s;
    }
}
