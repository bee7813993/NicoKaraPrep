using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.ViewModels;

/// <summary>n3proj 読み込みで取り込む項目（読み込み確認画面で選ぶ）。</summary>
public sealed class N3ProjImportChoices
{
    /// <summary>字幕フォント・縁取り・画面の横幅を、横幅チェックとプレビューに使う（ファイル > 設定 の値を置き換え）。</summary>
    public bool CheckFont { get; set; }

    /// <summary>ページ衝突チェックに、ニコカラメーカーが計算した実際の表示区間を使う。</summary>
    public bool LineTimes { get; set; }

    /// <summary>表示時刻の設定値（ワイプ前・ワイプ後・表示間隔・上段の表示）を取り込む。</summary>
    public bool Timing { get; set; }

    /// <summary>ニコカラメーカーの表示時刻を、歌詞が同じ行すべてに、読み込んだ値としてそのまま持たせる（自動調整は右のパネル「表示時刻」で実行する）。</summary>
    public bool LineShowTimes { get; set; }

    /// <summary>ニコカラメーカーで設定したページのレイアウトを、自動で選ぶものと違うページだけ、ページの手動指定として取り込む。</summary>
    public bool PageLayouts { get; set; }

    /// <summary>
    /// ニコカラメーカーで設定した字幕アクションを取り込む（曲の既定 = 歌詞行でいちばん多いアクション。それと違うアクションの行だけ、
    /// 行ごとの指定にする。行の対応はページのレイアウトと同じで、歌詞が同じ行だけ）。
    /// </summary>
    public bool LineActions { get; set; } = true;

    /// <summary>n3proj 書き出しのベースにする。</summary>
    public bool ExportBase { get; set; }

    /// <summary>NicoKaraPrep のフォント設定として取り込むフォント設定の名前（空 = 取り込まない）。</summary>
    public List<string> FontSetNames { get; set; } = new();

    /// <summary>取り込むアイコン（@Emoji）の置き換え文字列（空 = 取り込まない）。</summary>
    public List<string> IconNames { get; set; } = new();

    /// <summary>アイコンをアプリ共通（全曲）に取り込む。false はこの曲専用。</summary>
    public bool IconsGlobal { get; set; }

    /// <summary>プロジェクトの背景素材（動画・音声）をメディア再生に使う。</summary>
    public bool Media { get; set; }
}

/// <summary>ニコカラメーカー3 プロジェクト（n3proj）の読み込み。読み込みはすべてここを通る。</summary>
public partial class MainViewModel
{
    /// <summary>n3proj の内容を調べる（読み込み確認画面に表示する）。</summary>
    public N3ProjImportPreview PrepareN3ProjImport(string path)
    {
        int intervalHint = Nkm3Env?.IntervalMs ?? (int)Math.Round(Settings.N3IntervalSeconds * 1000);
        return N3ProjImport.Analyze(path, intervalHint);
    }

    /// <summary>
    /// n3proj の 2 つ目以降の歌詞設定（コーラスなど）の歌詞ファイルを、自分のファイルを持つタブとして開く
    /// （上書き保存ではそれぞれのファイルへ保存し、メインの歌詞ファイルには含めない）。
    /// 同じ名前のタブ・同じファイルを開いているタブがあれば開かない。タブの歌詞行がみな同じレイアウト設定なら、
    /// そのタブのレイアウト（n3proj の書き出しの設定）にする（決めていなければ）。
    /// 戻り値は開いたタブの名前と、歌詞ファイルが見つからない・読めなかったタブの名前。
    /// </summary>
    public (List<string> Opened, List<string> Missing) OpenProjectExtraTabs(N3ProjImportPreview preview)
    {
        StoreActiveTab();
        var opened = new List<string>();
        var missing = new List<string>();
        foreach (var source in preview.Tabs.Skip(1))
        {
            string? path = N3ProjImport.FindLyricsFile(preview.Path, source);
            string name = source.Name.Length > 0 ? source.Name
                : Path.GetFileNameWithoutExtension(source.LyricsRelativePath ?? source.LyricsPath ?? "") is { Length: > 0 } fileName ? fileName
                : $"パート{Tabs.Count}";
            if (path is null)
            {
                missing.Add(name);
                continue;
            }
            bool already = Tabs.Any(t => t.Name == name
                || SameFile(t.CopyFilePath, path)
                || (t.IsMain && SameFile(t.FilePath, path)));
            if (already) continue;

            LyricsDocument doc;
            try
            {
                doc = ReadLyricsFile(path);
            }
            catch (Exception)
            {
                missing.Add(name); // 読めないファイルは開かない（ほかのタブは開く）
                continue;
            }
            Tabs.Add(new TabState
            {
                Name = name,
                Document = doc,
                Format = FormatFromExtension(path),
                CopyFilePath = path,
                OwnFile = true,
            });
            if (source.LayoutName is { Length: > 0 } layout && !N3ProjSettings.TabLayouts.ContainsKey(name))
            {
                N3ProjSettings.TabLayouts[name] = layout;
            }
            // 歌詞の文字がみな同じフォント設定なら、タブの最初のフォントにする（ニコカラメーカー3 はタブをまたいで引き継がない。決めていなければ）
            if (source.FontSetName is { Length: > 0 } font && !N3ProjSettings.TabFontSetNames.ContainsKey(name))
            {
                N3ProjSettings.TabFontSetNames[name] = font;
            }
            opened.Add(name);
        }
        if (opened.Count > 0) SaveProject();
        return (opened, missing);
    }

    private static bool SameFile(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// 「ページのレイアウトを取り込む」を選んだときに手動指定になるページの数（試算。読み込むプロジェクトを書き出しのベースにした場合のレイアウトの並びで比べる）。
    /// </summary>
    public int CountPageLayoutImports(N3ProjImportPreview preview)
    {
        StoreActiveTab();
        var layouts = EffectiveLayoutsFor(preview.Path);
        var matcher = CreateEmojiMatcher();
        int count = 0;
        foreach (var tab in Tabs)
        {
            var (source, _) = N3ProjImport.FindSource(tab.Document, tab.Name, preview.Tabs, matcher);
            if (source is not null) count += PageLayoutDifferences(tab, source, layouts).Count;
        }
        return count;
    }

    /// <summary>
    /// ニコカラメーカーで設定したページのレイアウトが、NicoKaraPrep が自動で選ぶもの（行数に応じたレイアウト・タブに固定したレイアウト）と違うページ
    /// （ページの行の添字と、ニコカラメーカーのレイアウト名）。手動で指定したページ・歌詞が一致しないページ・ベースに無い名前は含めない。
    /// </summary>
    private List<(List<int> Lines, string Layout)> PageLayoutDifferences(TabState tab, N3ProjSourceTab source, List<N3LayoutSettings> layouts)
    {
        var result = new List<(List<int>, string)>();
        if (layouts.Count == 0) return result;
        var map = N3ProjImport.MatchLineIndexes(tab.Document, source, CreateEmojiMatcher());
        if (map.Count == 0) return result;
        var plans = N3ShowTimePlanner.Plan(tab.Document, CreateShowTimeSettings(tab.Name));
        string? fixedLayout = N3ProjSettings.TabLayouts.GetValueOrDefault(tab.Name) is { Length: > 0 } fl ? fl : null;
        var resolver = new N3ProjWriter.LayoutResolver(layouts.Select(l => l.Info).ToList(), fixedLayout,
            Nkm3Env?.LayoutSelectableBegin, Nkm3Env?.LayoutSelectableEnd, new List<string>(), tab.Name);
        foreach (var page in plans.GroupBy(p => p.Value.PageIndex))
        {
            var lines = page.Select(p => p.Key).OrderBy(i => i).ToList();
            if (lines.Any(i => tab.Document.Lines[i].LayoutName is { Length: > 0 } n && resolver.FindIndex(n) is not null)) continue; // 手動の指定はそのまま
            string? nkm3 = lines
                .Select(i => map.TryGetValue(i, out int s) && s < source.LayoutNames.Count ? source.LayoutNames[s] : null)
                .FirstOrDefault(n => n is not null);
            if (nkm3 is null || resolver.FindIndex(nkm3) is null) continue;
            string auto = layouts[Math.Clamp(resolver.Resolve(lines.Count), 0, layouts.Count - 1)].Name;
            if (auto != nkm3) result.Add((lines, nkm3));
        }
        return result;
    }

    /// <summary>
    /// 「字幕アクションを取り込む」の試算: 曲の既定にするアクション（プロジェクトの歌詞行でいちばん多いもの。アクションが無ければ null）と、
    /// それと違うアクションを行ごとの指定にする行の数（開いている歌詞と一致する行だけ）。
    /// </summary>
    public (N3SubtitleAction? Default, int Lines) CountLineActionImports(N3ProjImportPreview preview)
    {
        var def = ProjectDefaultSubtitleAction(preview);
        if (def is null) return (null, 0);
        StoreActiveTab();
        var matcher = CreateEmojiMatcher();
        int lines = 0;
        foreach (var tab in Tabs)
        {
            var (source, _) = N3ProjImport.FindSource(tab.Document, tab.Name, preview.Tabs, matcher);
            if (source is not null) lines += LineActionTargets(tab, source, def).Count(t => t.Action is not null);
        }
        return (def, lines);
    }

    /// <summary>n3proj の歌詞行でいちばん多い字幕アクション（全部の歌詞設定。アクションが無ければ null）。</summary>
    private static N3SubtitleAction? ProjectDefaultSubtitleAction(N3ProjImportPreview preview) =>
        N3SubtitleActionCatalog.MostCommon(preview.Tabs.SelectMany(t => t.SubtitleActions));

    /// <summary>
    /// 字幕アクションの取り込みで行に持たせるもの（行の添字と、行ごとの指定。曲の既定 <paramref name="def"/> と同じなら null = 曲の既定に従う）。
    /// 歌詞が一致する行だけ（行の対応はページのレイアウトの取り込みと同じ）。アクションの分からない行は含めない（今のまま）。
    /// </summary>
    private List<(int Line, N3SubtitleAction? Action)> LineActionTargets(TabState tab, N3ProjSourceTab source, N3SubtitleAction def)
    {
        var result = new List<(int, N3SubtitleAction?)>();
        var map = N3ProjImport.MatchLineIndexes(tab.Document, source, CreateEmojiMatcher());
        foreach (var (line, s) in map.OrderBy(p => p.Key))
        {
            if (line < 0 || line >= tab.Document.Lines.Count || tab.Document.Lines[line].IsEmpty) continue;
            if (s < 0 || s >= source.SubtitleActions.Count || source.SubtitleActions[s] is not { } action) continue;
            result.Add((line, action.SameAs(def) ? null : action));
        }
        return result;
    }

    /// <summary>n3proj を書き出しのベースにした場合のレイアウト設定の並び（読めなければ今の並び）。</summary>
    private List<N3LayoutSettings> EffectiveLayoutsFor(string projectPath)
    {
        try
        {
            var root = N3ProjFormat.ReadJsonObject(projectPath);
            var (_, height) = N3LayoutReader.ScreenSize(root);
            var baseLayouts = N3LayoutReader.Read(root, height);
            if (baseLayouts.Count == 0) baseLayouts = N3LayoutReader.Defaults(height);
            return N3LayoutLibrary.Effective(baseLayouts, Settings.N3Layouts, N3ProjSettings.MergeLayouts);
        }
        catch (Exception)
        {
            return GetEffectiveLayouts();
        }
    }

    /// <summary>歌詞行が n3proj の歌詞行に対応する数（全タブの合計）と歌詞行の総数。</summary>
    public (int Matched, int Total) CountMatchedLyricLines(N3ProjImportPreview preview)
    {
        StoreActiveTab();
        var matcher = CreateEmojiMatcher();
        int matched = 0, total = 0;
        foreach (var tab in Tabs)
        {
            total += tab.Document.Lines.Count(l => !l.IsEmpty);
            matched += N3ProjImport.FindSource(tab.Document, tab.Name, preview.Tabs, matcher).Lines.Count;
        }
        return (matched, total);
    }

    /// <summary>読み込み確認画面で選んだ項目を取り込む。</summary>
    public void ApplyN3ProjImport(N3ProjImportPreview preview, N3ProjImportChoices choices)
    {
        StoreActiveTab();
        var done = new List<string>();
        bool settingsChanged = false;
        // 歌詞行の照合では、絵文字・＿の開始タグを除いて比べる（書き出しで絵文字の開始を寄せた行も、絵文字の秒数を変えた行も対応させる）
        var matcher = CreateEmojiMatcher();

        // 1) 表示時刻の設定値（手動指定の取り込みより先に適用し、その設定で差分を取る）
        if (choices.Timing && preview.Timing is { } timing)
        {
            Settings.DisplayLeadSeconds = timing.LeadMs / 1000.0;
            Settings.DisplayTailSeconds = timing.TailMs / 1000.0;
            Settings.N3IntervalSeconds = timing.IntervalMs / 1000.0;
            Settings.N3TopLong = preview.MainTopLong;
            foreach (var tab in Tabs)
            {
                var (source, _) = N3ProjImport.FindSource(tab.Document, tab.Name, preview.Tabs, matcher);
                if (source is null) continue;
                if (source.TopLong == Settings.N3TopLong) N3ProjSettings.TabTopLong.Remove(tab.Name);
                else N3ProjSettings.TabTopLong[tab.Name] = source.TopLong;
            }
            settingsChanged = true;
            done.Add($"表示時刻の設定（ワイプ前 {timing.LeadMs / 1000.0:0.###} 秒・ワイプ後 {timing.TailMs / 1000.0:0.###} 秒・間隔 {timing.IntervalMs / 1000.0:0.###} 秒・上段を{(preview.MainTopLong ? "長め" : "短め")}に）");
        }

        // 2) 横幅チェック・プレビュー用の字幕フォントと画面サイズ
        if (choices.CheckFont)
        {
            ApplyN3ProjCheckFont(preview.Settings);
            settingsChanged = true;
            done.Add(preview.Settings.MainFont is { } f
                ? $"字幕フォント {f.FontName} {Settings.FontSizePx:0.#}px・画面 {preview.Settings.ScreenWidth}px"
                : $"画面 {preview.Settings.ScreenWidth}px");
        }

        // 3) ページ衝突チェック用の実表示区間
        if (choices.LineTimes)
        {
            _n3projLineTimes = preview.Settings.LineTimes.Count > 0 ? preview.Settings.LineTimes : null;
            done.Add($"実際の表示区間 {preview.Settings.LineTimes.Count} 行分");
        }

        // 4) 表示時刻 → 歌詞が同じ行すべてに、読み込んだ値としてそのまま持たせる（自動調整は右のパネル「表示時刻」で実行する）
        if (choices.LineShowTimes)
        {
            int lines = 0;
            foreach (var tab in Tabs)
            {
                var (source, matched) = N3ProjImport.FindSource(tab.Document, tab.Name, preview.Tabs, matcher);
                if (source is null || matched.Count == 0) continue;
                tab.UndoStack.Add(tab.Document.Clone()); // 元に戻せるように
                tab.RedoStack.Clear();
                lines += N3ProjImport.LoadShowTimes(tab.Document, matched);
                tab.IsModified = true;
            }
            if (lines > 0)
            {
                IsModified = _activeTab.IsModified;
                UpdateTitle();
            }
            done.Add($"表示時刻 {lines} 行（そのまま）");
        }

        // 5) 書き出しのベース
        if (choices.ExportBase)
        {
            N3ProjSettings.BasePath = preview.Path;
            done.Add("書き出しのベース");
        }

        // 5') ページのレイアウト（ニコカラメーカーで設定したもの）→ 自動で選ぶものと違うページだけ、ページの手動指定に
        //     （ベースを変えたあとのレイアウトの並びで比べる。ニコカラメーカーの「適用対象レイアウト」の範囲は曲ごとに違うことがあるため）
        if (choices.PageLayouts)
        {
            var layouts = GetEffectiveLayouts();
            int pages = 0;
            foreach (var tab in Tabs)
            {
                var (source, _) = N3ProjImport.FindSource(tab.Document, tab.Name, preview.Tabs, matcher);
                if (source is null) continue;
                var diffs = PageLayoutDifferences(tab, source, layouts);
                if (diffs.Count == 0) continue;
                tab.UndoStack.Add(tab.Document.Clone());
                tab.RedoStack.Clear();
                foreach (var (lines, name) in diffs)
                {
                    foreach (int i in lines) tab.Document.Lines[i].LayoutName = name;
                }
                tab.IsModified = true;
                pages += diffs.Count;
            }
            if (pages > 0)
            {
                IsModified = _activeTab.IsModified;
                UpdateTitle();
            }
            done.Add($"ページのレイアウト {pages} ページ");
        }

        // 5'') 字幕アクション（ニコカラメーカーで設定したもの）→ 曲の既定 = 歌詞行でいちばん多いアクション（曲の設定へ直接入れて、最後に保存）。
        //      それと違うアクションの行だけ行ごとの指定にし、同じ行は指定を外して曲の既定に従わせる（歌詞が同じ行だけ）
        bool songActionChanged = false;
        if (choices.LineActions && ProjectDefaultSubtitleAction(preview) is { } projectAction)
        {
            songActionChanged = !N3SubtitleAction.AreSame(N3ProjSettings.SubtitleAction, projectAction);
            N3ProjSettings.SubtitleAction = projectAction;
            int lines = 0;
            bool changed = false;
            foreach (var tab in Tabs)
            {
                var (source, _) = N3ProjImport.FindSource(tab.Document, tab.Name, preview.Tabs, matcher);
                if (source is null) continue;
                var targets = LineActionTargets(tab, source, projectAction);
                lines += targets.Count(t => t.Action is not null);
                var changes = targets.Where(t => !N3SubtitleAction.AreSame(tab.Document.Lines[t.Line].SubtitleAction, t.Action)).ToList();
                if (changes.Count == 0) continue;
                tab.UndoStack.Add(tab.Document.Clone()); // 元に戻せるように
                tab.RedoStack.Clear();
                foreach (var (i, action) in changes) tab.Document.Lines[i].SubtitleAction = action?.Clone();
                tab.IsModified = true;
                changed = true;
            }
            if (changed)
            {
                IsModified = _activeTab.IsModified;
                UpdateTitle();
            }
            done.Add($"字幕アクション（曲の既定: {DescribeSubtitleAction(projectAction)}・行ごとの指定 {lines} 行）");
        }

        // 6) アイコン（@Emoji）
        if (choices.IconNames.Count > 0)
        {
            var names = new HashSet<string>(choices.IconNames, StringComparer.Ordinal);
            var icons = preview.Icons.Where(i => names.Contains(i.Entry.ReplaceChar)).Select(i => i.Entry).ToList();
            int n = ImportIcons(icons, choices.IconsGlobal);
            if (choices.IconsGlobal) settingsChanged = true;
            done.Add($"アイコン {n} 件（{(choices.IconsGlobal ? "アプリ共通" : "この曲専用")}）");
        }

        // 7) 動画（メディア再生）
        if (choices.Media && preview.MediaExists)
        {
            MediaPath = preview.MediaPath;
            done.Add($"動画 {Path.GetFileName(preview.MediaPath)}");
        }

        // 8) フォント設定（アプリ共通へ。同じ名前は置き換え、それ以外は末尾に追加）
        if (choices.FontSetNames.Count > 0)
        {
            var names = new HashSet<string>(choices.FontSetNames);
            int added = 0, replaced = 0;
            ReplaceCommonFontSets(() =>
            {
                foreach (var f in preview.FontSets.Where(f => names.Contains(f.Name)))
                {
                    var copy = f.Clone();
                    int i = Settings.N3FontSets.FindIndex(x => x.Name == f.Name);
                    if (i >= 0)
                    {
                        // フォント設定ビューで選んでいたものが置き換わっても選択が外れないよう、識別子は引き継ぐ
                        copy.Id = Settings.N3FontSets[i].Id;
                        Settings.N3FontSets[i] = copy;
                        replaced++;
                    }
                    else
                    {
                        Settings.N3FontSets.Add(copy);
                        added++;
                    }
                }
                N3FontLibrary.EnsureIds(Settings.N3FontSets);
            });
            settingsChanged = true;
            done.Add($"フォント設定 {added + replaced} 件（追加 {added}・置き換え {replaced}）");
        }

        if (settingsChanged) Settings.Save();
        SaveProject();
        if (songActionChanged) SongDefaultSubtitleActionChanged?.Invoke(this, EventArgs.Empty);

        StatusText = done.Count == 0
            ? "読み込む項目が選ばれていません"
            : $"ニコカラメーカー3 プロジェクトを読み込みました（{Path.GetFileName(preview.Path)}）: {string.Join(" / ", done)}";
    }

    /// <summary>
    /// アイコン（@Emoji）を取り込む。同じ置き換え文字列の定義は置き換え（パレットの位置は保つ）、それ以外は追加。
    /// この曲専用ならすべてのタブの歌詞に入れ（歌詞ファイルの保存で保存される）、アプリ共通なら設定に入れる。
    /// </summary>
    public int ImportIcons(IReadOnlyList<EmojiEntry> icons, bool global)
    {
        if (icons.Count == 0) return 0;
        if (global)
        {
            foreach (var icon in icons)
            {
                var e = icon.Clone();
                int i = Settings.GlobalEmojiList.FindIndex(x => x.ReplaceChar == e.ReplaceChar);
                if (i >= 0)
                {
                    e.Slot = Settings.GlobalEmojiList[i].Slot;
                    Settings.GlobalEmojiList[i] = e;
                }
                else
                {
                    var used = new HashSet<int>(Settings.GlobalEmojiList.Where(x => x.Slot is >= 1 and <= 20).Select(x => x.Slot!.Value));
                    int slot = Enumerable.Range(1, 20).FirstOrDefault(s => !used.Contains(s));
                    e.Slot = slot == 0 ? null : slot;
                    Settings.GlobalEmojiList.Add(e);
                }
            }
            Settings.Save();
        }
        else
        {
            StoreActiveTab();
            foreach (var tab in Tabs)
            {
                tab.UndoStack.Add(tab.Document.Clone());
                tab.RedoStack.Clear();
            }

            // 表示中のタブ（パレットに出る）に入れてパレットの位置を割り当て、ほかのタブにも同じ位置で入れる
            MergeSongIcons(Document, icons.Select(e => { var c = e.Clone(); c.Slot = null; return c; }));
            AssignSlotsToSongEmoji();
            var slots = new Dictionary<string, int?>();
            foreach (var e in Document.EmojiEntries) slots.TryAdd(e.ReplaceChar, e.Slot);
            foreach (var tab in Tabs.Where(t => t != _activeTab))
            {
                MergeSongIcons(tab.Document, icons.Select(e => { var c = e.Clone(); c.Slot = slots.GetValueOrDefault(e.ReplaceChar); return c; }));
                tab.IsModified = true;
            }
            MarkModified();
        }
        RefreshEmojiSlots();
        return icons.Count;
    }

    private static void MergeSongIcons(LyricsDocument doc, IEnumerable<EmojiEntry> icons)
    {
        foreach (var e in icons)
        {
            int i = doc.EmojiEntries.FindIndex(x => x.ReplaceChar == e.ReplaceChar);
            if (i >= 0)
            {
                e.Slot = doc.EmojiEntries[i].Slot ?? e.Slot;
                doc.EmojiEntries[i] = e;
            }
            else
            {
                doc.EmojiEntries.Add(e);
            }
        }
    }

    /// <summary>
    /// アイコンを取り込んだときの扱い（読み込み確認画面の表示用）: 追加 / 置き換え / 同じ（取り込んでも変わらない）。
    /// </summary>
    public string DescribeIconImport(EmojiEntry icon, bool global)
    {
        string? baseDir = Tabs.FirstOrDefault(t => t.IsMain)?.FilePath is string mp ? Path.GetDirectoryName(mp) : null;
        bool Same(EmojiEntry a) =>
            SamePath(a.ImageBefore, icon.ImageBefore, baseDir) &&
            SamePath(a.ImageAfter, icon.ImageAfter, baseDir) &&
            NormalizeOptions(a.Options) == NormalizeOptions(icon.Options);

        var inGlobal = Settings.GlobalEmojiList.FirstOrDefault(x => x.ReplaceChar == icon.ReplaceChar);
        if (global)
        {
            return inGlobal is null ? "追加" : Same(inGlobal) ? "同じ" : "置き換え";
        }
        var inSong = Document.EmojiEntries.FirstOrDefault(x => x.ReplaceChar == icon.ReplaceChar);
        if (inSong is not null) return Same(inSong) ? "同じ" : "置き換え";
        return inGlobal is not null && Same(inGlobal) ? "同じ（アプリ共通にあり）" : "追加";
    }

    private static bool SamePath(string? a, string? b, string? baseDir)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return string.IsNullOrEmpty(a) == string.IsNullOrEmpty(b);
        try
        {
            string fa = Path.IsPathRooted(a) || baseDir is null ? a : Path.GetFullPath(Path.Combine(baseDir, a));
            string fb = Path.IsPathRooted(b) || baseDir is null ? b : Path.GetFullPath(Path.Combine(baseDir, b));
            return string.Equals(Path.GetFullPath(fa), Path.GetFullPath(fb), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string NormalizeOptions(string? options) =>
        string.Join(",", (options ?? "").Split(',').Select(o => o.Trim()).Where(o => o.Length > 0));

    /// <summary>n3proj の主フォント・縁取り・画面の横幅を、横幅チェックとプレビューの設定にする。</summary>
    private void ApplyN3ProjCheckFont(N3ProjSettings s)
    {
        Settings.ScreenWidthPx = s.ScreenWidth;
        if (s.MainFont is { } font)
        {
            Settings.FontFamily = font.FontName;
            Settings.FontSizePx = Math.Round(font.SizePx, 1);
            Settings.FontBold = font.IsBoldLike;
            Settings.EdgeSizePx = Math.Round(font.EdgeSizePx, 1);
        }
    }

    /// <summary>
    /// 歌詞ファイルを開いたとき、同じフォルダの n3proj から「字幕フォントと画面サイズ」「実際の表示区間」を読み込む
    /// （設定で無効にできる。ほかの項目はファイル > ニコカラメーカー3 プロジェクトを読み込み で選んで読み込む）。
    /// </summary>
    private void AutoImportNearbyN3Proj(string lyricsPath)
    {
        _n3projLineTimes = null;
        if (!Settings.N3AutoImportNearby) return;
        if (N3ProjFormat.FindNear(lyricsPath) is not string n3proj) return;
        try
        {
            var s = N3ProjFormat.Read(n3proj);
            ApplyN3ProjCheckFont(s);
            Settings.Save();
            _n3projLineTimes = s.LineTimes.Count > 0 ? s.LineTimes : null;
            string font = s.MainFont is { } f ? $"{f.FontName} {Settings.FontSizePx:0.#}px・" : "";
            StatusText += $"　同じフォルダの {Path.GetFileName(n3proj)} から {font}画面 {s.ScreenWidth}px・実際の表示区間 {s.LineTimes.Count} 行分を読み込みました";
        }
        catch (Exception)
        {
            // n3proj が読めなくても歌詞の読み込みは成功扱い
        }
    }
}
