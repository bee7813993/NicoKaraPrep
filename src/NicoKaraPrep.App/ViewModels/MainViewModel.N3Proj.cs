using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;
using NicoKaraPrep.Core.Project;
using NicoKaraPrep.Core.Validation;

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
    /// 絵文字の分だけ行の表示を遅らせる規則（ニコカラメーカー3 には無い）の対象は、実効の @Emoji とプレースホルダ（＿）。
    /// </summary>
    /// <param name="tabName">タブの名前（そのタブの上段の表示・絵文字・行の画面上の範囲を使う。null = 表示中のタブの絵文字だけ）。</param>
    /// <param name="withBounds">
    /// 行の画面上の四角（<see cref="N3ShowTimeSettings.LineBounds"/>。字幕のプレビューを作ったときのもの）を当てるか。
    /// 四角を作るとき（ページ・段だけを決める計算）は false。
    /// </param>
    public N3ShowTimeSettings CreateShowTimeSettings(string? tabName = null, bool withBounds = true) => new()
    {
        PageMode = Settings.PageMode,
        FixedLineCount = Settings.FixedLineCount,
        LeadMs = (int)Math.Round(Settings.DisplayLeadSeconds * 1000),
        TailMs = (int)Math.Round(Settings.DisplayTailSeconds * 1000),
        IntervalMs = (int)Math.Round(Settings.N3IntervalSeconds * 1000),
        ProtectMs = Settings.N3ProtectSeconds > 0 ? (int)Math.Round(Settings.N3ProtectSeconds * 1000) : null,
        OverlapMs = Math.Max(0, (int)Math.Round(Settings.N3OverlapSeconds * 1000)),
        TopLong = tabName is not null && N3ProjSettings.TabTopLong.TryGetValue(tabName, out bool topLong) ? topLong : Settings.N3TopLong,
        AlignFromTop = Settings.CollisionAlignFromTop,
        EmojiLeadYield = Settings.N3EmojiLeadYield,
        LeadMatcher = CreateEmojiMatcherFor(DocumentOfTab(tabName)),
        LineBounds = withBounds && tabName is not null && Settings.N3LayoutAwareRows ? _lineBounds.GetValueOrDefault(tabName) : null,
    };

    /// <summary>名前のタブの歌詞（表示中のタブ・名前が無いときは表示中の歌詞）。</summary>
    private LyricsDocument DocumentOfTab(string? tabName) =>
        tabName is null || tabName == _activeTab.Name ? Document : Tabs.FirstOrDefault(t => t.Name == tabName)?.Document ?? Document;

    /// <summary>
    /// タブごとの最初のフォント設定名（書き出しと同じ決め方。曲の設定 <see cref="N3ProjSongSettings.TabFontSetNames"/> に
    /// 指定が無ければ自動: メインのタブは既定のフォント設定、2 つ目以降は名前に「コーラス」を含むもの。<see cref="N3FontResolver.AutoStartName"/>）。
    /// </summary>
    public List<string?> GetTabStartFontNames(IReadOnlyList<TabState> tabs, IReadOnlyList<string> names, string? defaultName, N3ProjSongSettings? settings = null)
    {
        var tabFonts = (settings ?? N3ProjSettings).TabFontSetNames;
        return tabs.Select(t => tabFonts.GetValueOrDefault(t.Name) is { Length: > 0 } n && names.Contains(n)
            ? n
            : N3FontResolver.AutoStartName(t.IsMain, names, defaultName)).ToList();
    }

    /// <summary>タブごとの最初のフォント設定名（今の書き出し設定のフォント設定の並びで）。</summary>
    public List<string?> GetTabStartFontNames(IReadOnlyList<TabState> tabs)
    {
        var (names, def) = GetExportFontNames();
        return GetTabStartFontNames(tabs, names, def);
    }

    /// <summary>表示中のドキュメントの行ごとの表示時刻（自動計算＋手動指定。ms）。</summary>
    public Dictionary<int, N3LinePlan> PlanShowTimes() =>
        N3ShowTimePlanner.Plan(Document, CreateShowTimeSettings(_activeTab.Name));

    /// <summary>
    /// いま自動調整を実行したら決まる表示時刻（表示中のタブ。行には持たせない）。自動調整の値・読み込んだ値を持つ行が無ければ null
    /// （そのときは <see cref="PlanShowTimes"/> と同じになる）。
    /// </summary>
    public Dictionary<int, N3LinePlan>? PlanShowTimesFresh() =>
        N3ShowTimeAdjuster.HasRecomputable(Document)
            ? N3ShowTimeAdjuster.PlanFresh(Document, CreateShowTimeSettings(_activeTab.Name))
            : null;

    /// <summary>
    /// 行設定パネルの説明に足す、絵文字の分だけ行の表示を遅らせる規則（右のパネル「表示時刻」の設定）の結果（表示中のタブの行）。
    /// 規則で表示を遅らせた行・絵文字を縮めた行だけ「・絵文字の分だけ遅らせた（絵文字 2.0→1.5 秒）」のような文を返す（それ以外は空）。
    /// 絵文字はワイプ前の表示時間（表示秒数がそれより短い絵文字はその秒数）までしか縮めず、それでも重なるときは前の行のワイプ後を削る。
    /// それより短くなる行は理由を書く（手で指定した表示時刻・前の行をワイプの最後まで見せるため など）。
    /// </summary>
    /// <param name="plan">説明する行の表示時刻。規則が決めた値（自動計算・いま実行し直しても同じ自動調整の値）なら、いま自動調整を実行したときの計算を渡す。</param>
    /// <param name="byRule">
    /// <paramref name="plan"/> が規則が決めた値か。false（読み込んだ値・古くなった自動調整の値）で絵文字が下限より短くなる行は、
    /// 理由を書かずに「表示開始に合わせて縮めた」とする（書き出しで自動で縮める）。
    /// </param>
    public string DescribeEmojiLeadYield(N3LinePlan plan, IReadOnlyDictionary<int, N3LinePlan> plans, bool byRule = true)
    {
        var show = CreateShowTimeSettings(_activeTab.Name);
        if (!show.YieldsEmojiLead || plan.LineIndex < 0 || plan.LineIndex >= Document.Lines.Count) return "";
        var line = Document.Lines[plan.LineIndex];
        var shrinks = N3EmojiLead.Describe(line, plan.BeginMs, show.LeadMatcher);
        string emoji = N3ShowTimeValidator.FormatShrinks(shrinks);
        if (N3ShowTimeValidator.BelowFloor(shrinks, show.LeadMs).Count > 0)
        {
            // 理由: 表示開始の手動指定／前のページの同じ段の行（前の行）の表示終了の手動指定／
            // 前の行と歌が重なる（どう詰めても前の行はワイプの途中で消える）／前の行をワイプの最後まで見せる（折衷）
            var prev = plan.Row > 0
                ? plans.Values.FirstOrDefault(p => p.PageIndex == plan.PageIndex - 1 && p.Row == plan.Row)
                : null;
            var prevLine = prev is not null && prev.LineIndex >= 0 && prev.LineIndex < Document.Lines.Count ? Document.Lines[prev.LineIndex] : null;
            string? cause = line.HasManualShowBegin ? "表示開始の手動指定のため"
                : prevLine?.HasManualShowEnd == true ? "前の行の表示終了の手動指定のため"
                : !byRule ? null
                : prev is not null && prevLine is not null && N3ShowTimePlanner.SingEndMs(prevLine) is int prevLast && prev.EndMs < prevLast
                    ? "前の行と歌が重なるため"
                : "前の行をワイプの最後まで見せるため";
            return cause is null ? $"・表示開始に合わせて絵文字を縮めた（{emoji} 秒）" : $"・{cause}絵文字を縮めた（{emoji} 秒）";
        }
        if (plan.EmojiYieldMs > 0)
        {
            return emoji.Length > 0
                ? $"・絵文字の分だけ遅らせた（絵文字 {emoji} 秒）"
                : $"・絵文字の分だけ遅らせた（{plan.EmojiYieldMs / 1000.0:0.0##} 秒。絵文字の秒数はそのまま）";
        }
        // 表示開始を行に持たせた行などで、表示開始より前になった絵文字だけを縮めた行
        return emoji.Length > 0 ? $"・表示開始に合わせて絵文字を縮めた（{emoji} 秒）" : "";
    }

    /// <summary>
    /// 行の表示時刻の出どころの名前（行設定パネルの説明の頭）。表示開始・終了で違えば「開始は手動・終了は自動調整」のように並べる。
    /// 値を持たない表示時刻は「自動」（書き出し・画面の表示のたびに計算する）。
    /// </summary>
    public static string ShowTimeOriginLabel(LyricsLine line)
    {
        static string Name(int? cs, ShowTimeOrigin origin) => cs is null ? "自動" : origin switch
        {
            ShowTimeOrigin.Loaded => "読み込み",
            ShowTimeOrigin.Auto => "自動調整",
            _ => "手動",
        };
        string begin = Name(line.ShowBeginCs, line.ShowBeginOrigin);
        string end = Name(line.ShowEndCs, line.ShowEndOrigin);
        return begin == end ? begin : $"開始は{begin}・終了は{end}";
    }

    /// <summary>行の表示開始・終了を設定する（null = 値を外して自動に戻す）。値は手で指定したもの（手動）にする。</summary>
    public bool SetLineShowTime(int index, int? beginCs, int? endCs)
    {
        if (index < 0 || index >= Document.Lines.Count) return false;
        var line = Document.Lines[index];
        bool beginSame = line.ShowBeginCs == beginCs && (beginCs is null || line.ShowBeginOrigin == ShowTimeOrigin.Manual);
        bool endSame = line.ShowEndCs == endCs && (endCs is null || line.ShowEndOrigin == ShowTimeOrigin.Manual);
        if (beginSame && endSame) return false;
        PushUndo();
        line.ShowBeginCs = beginCs;
        line.ShowEndCs = endCs;
        if (beginCs is not null) line.ShowBeginOrigin = ShowTimeOrigin.Manual;
        if (endCs is not null) line.ShowEndOrigin = ShowTimeOrigin.Manual;
        MarkModified();
        SaveProject();
        return true;
    }

    /// <summary>
    /// 行設定パネルの表示開始・終了の欄の確定。値のある欄は手で指定した値（手動）にする。
    /// 空の欄は、手で指定していた値なら外して自動に戻し、読み込んだ値・自動調整の値（欄の薄字）ならそのまま残す。
    /// </summary>
    public bool SetLineShowTimeFromBoxes(int index, int? beginCs, int? endCs)
    {
        if (index < 0 || index >= Document.Lines.Count) return false;
        var line = Document.Lines[index];
        (int? Cs, ShowTimeOrigin Origin) Next(int? typed, int? cs, ShowTimeOrigin origin) =>
            typed is int t ? (t, ShowTimeOrigin.Manual)
            : cs is not null && origin == ShowTimeOrigin.Manual ? (null, origin)
            : (cs, origin);
        var (b, bo) = Next(beginCs, line.ShowBeginCs, line.ShowBeginOrigin);
        var (e, eo) = Next(endCs, line.ShowEndCs, line.ShowEndOrigin);
        if (b == line.ShowBeginCs && e == line.ShowEndCs && (b is null || bo == line.ShowBeginOrigin) && (e is null || eo == line.ShowEndOrigin)) return false;
        PushUndo();
        line.ShowBeginCs = b;
        line.ShowBeginOrigin = bo;
        line.ShowEndCs = e;
        line.ShowEndOrigin = eo;
        MarkModified();
        SaveProject();
        return true;
    }

    /// <summary>
    /// 表示時刻の自動調整を実行する（右のパネル「表示時刻」。全タブ）。手で指定した表示時刻は残し、ほかの値（読み込んだ値・前回の自動調整の値・未設定）を
    /// 今の設定で計算し直して、自動調整の値として行に持たせる（<see cref="N3ShowTimeAdjuster"/>）。変わったタブは元に戻せる。結果をステータスに出す。
    /// </summary>
    public N3ShowTimeAdjustResult RunAutoShowTimes()
    {
        StoreActiveTab();
        int auto = 0, manual = 0, untimed = 0, changedTabs = 0;
        foreach (var tab in Tabs)
        {
            var settings = CreateShowTimeSettings(tab.Name);
            var trial = tab.Document.Clone();
            var r = N3ShowTimeAdjuster.Run(trial, settings);
            auto += r.AutoLines;
            manual += r.ManualLines;
            untimed += r.UntimedLines;
            if (SameShowTimes(trial, tab.Document)) continue;
            tab.UndoStack.Add(tab.Document.Clone());
            if (tab.UndoStack.Count > MaxUndo) tab.UndoStack.RemoveAt(0);
            tab.RedoStack.Clear();
            N3ShowTimeAdjuster.Run(tab.Document, settings);
            tab.IsModified = true;
            changedTabs++;
        }
        if (changedTabs > 0)
        {
            IsModified = _activeTab.IsModified;
            UpdateTitle();
            SaveProject();
        }
        string kept = manual > 0 ? $"。手で直した {manual} 行はそのまま" : "";
        string none = untimed > 0 ? $"。タイムタグの無い {untimed} 行は決められません" : "";
        StatusText = changedTabs > 0
            ? $"表示時刻を自動調整しました（{auto} 行{kept}{none}）"
            : $"表示時刻を自動調整しました（変わった行はありません{kept}{none}）";
        return new N3ShowTimeAdjustResult(auto, manual, untimed);
    }

    /// <summary>2 つのドキュメントの行の表示時刻（値と出どころ）が同じか。</summary>
    private static bool SameShowTimes(LyricsDocument a, LyricsDocument b)
    {
        if (a.Lines.Count != b.Lines.Count) return false;
        for (int i = 0; i < a.Lines.Count; i++)
        {
            var x = a.Lines[i];
            var y = b.Lines[i];
            if (x.ShowBeginCs != y.ShowBeginCs || x.ShowEndCs != y.ShowEndCs) return false;
            if (x.ShowBeginCs is not null && x.ShowBeginOrigin != y.ShowBeginOrigin) return false;
            if (x.ShowEndCs is not null && x.ShowEndOrigin != y.ShowEndOrigin) return false;
        }
        return true;
    }

    /// <summary>表示中のタブの、表示時刻の出どころごとの行数と、いま自動調整を実行し直すと変わる行の数（右のパネル「表示時刻」）。</summary>
    public (N3ShowTimeOriginCounts Counts, int Outdated) ShowTimeSummary() =>
        (N3ShowTimeAdjuster.Count(Document), N3ShowTimeAdjuster.CountOutdated(Document, CreateShowTimeSettings(_activeTab.Name)));

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

    /// <summary>行に持たせた表示開始（手で直した値・読み込んだ値・自動調整の値。ページ衝突チェック用）。</summary>
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

        // 行の画面上の四角（レイアウトで別の場所に出る行を詰めない）とタブの最初のフォントは、書き出す設定（ベース・タブのレイアウトなど）で決める。
        // 書き出せなかったときは元の設定に戻す
        var previousSettings = N3ProjSettings;
        N3ProjSettings = settings;
        try
        {
            return WriteN3Proj(projectPath, settings, dir, baseName, openFiles);
        }
        catch
        {
            N3ProjSettings = previousSettings;
            UpdateLineFonts();
            throw;
        }
    }

    private N3ProjExportResult WriteN3Proj(string projectPath, N3ProjSongSettings settings, string dir, string baseName, HashSet<string> openFiles)
    {
        UpdateLineFonts(); // 行の画面上の四角を、書き出す設定と今の歌詞で作り直す
        var exportTabs = GetN3ProjExportTabs();
        var startFonts = GetTabStartFontNames(exportTabs);
        var tabs = new List<N3ProjExportTab>();
        for (int k = 0; k < exportTabs.Count; k++)
        {
            var tab = exportTabs[k];
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
                StartFontSetName = startFonts[k],
                ShowTime = CreateShowTimeSettings(tab.Name), // タブの絵文字・行の画面上の四角
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

    /// <summary>
    /// 書き出し画面の曲の設定（ベース・タブごとのレイアウトと上段の表示・既定のフォント設定など）を、書き出さずに曲へ保存する
    /// （書き出し画面の「適用」。書き出し先は今のまま）。表示時刻の設定値（ワイプ前など）は書き出し画面がアプリの設定へ保存済み。
    /// </summary>
    public void ApplyN3ProjSongSettings(N3ProjSongSettings settings)
    {
        settings.OutputPath = N3ProjSettings.OutputPath;
        N3ProjSettings = settings;
        string? basePath = settings.BasePath is { Length: > 0 } bp && File.Exists(bp) ? bp : null;
        Settings.N3LastBasePath = basePath ?? "";
        Settings.Save();
        SaveProject();
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        string s = new string(chars).Trim();
        return s.Length == 0 ? "tab" : s;
    }
}
