using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;
using NicoKaraPrep.Core.Project;
using NicoKaraPrep.Core.Validation;

namespace NicoKaraPrep.App.ViewModels;

/// <summary>読み書きするファイルの形式。</summary>
public enum DocumentFormat
{
    Lrc,
    Rlf,
}

public partial class MainViewModel : ObservableObject
{
    public const string AppName = "NicoKaraPrep";

    [ObservableProperty]
    private LineViewModel? selectedLine;

    [ObservableProperty]
    private string statusText = "ファイルを開くか、クリップボードから貼り付けてください";

    [ObservableProperty]
    private string windowTitle = AppName;

    [ObservableProperty]
    private bool isModified;

    public ObservableCollection<LineViewModel> Lines { get; } = new();

    public ObservableCollection<ValidationIssue> Issues { get; } = new();

    public LyricsDocument Document { get; private set; } = new();

    // ------------------------------------------------------------ タブ

    /// <summary>ドキュメントタブ（先頭は常にメイン）。</summary>
    public ObservableCollection<TabState> Tabs { get; } = new();

    private TabState _activeTab;

    public MainViewModel()
    {
        _activeTab = new TabState { IsMain = true };
        Tabs.Add(_activeTab);
    }

    public TabState ActiveTab => _activeTab;

    public int ActiveTabIndex => Tabs.IndexOf(_activeTab);

    public bool ActiveTabIsMain => _activeTab.IsMain;

    /// <summary>アクティブタブの内容をタブ状態へ書き戻す（切替・保存の前に呼ぶ）。</summary>
    private void StoreActiveTab()
    {
        _activeTab.Document = Document;
        _activeTab.FilePath = CurrentFilePath;
        _activeTab.Format = CurrentFormat;
        _activeTab.IsModified = IsModified;
    }

    private void ActivateTab(TabState tab)
    {
        _activeTab = tab;
        Document = tab.Document;
        CurrentFilePath = tab.FilePath;
        CurrentFormat = tab.Format;
        IsModified = tab.IsModified;
        RebuildLines();
        RefreshEmojiSlots();
        UpdateTitle();
    }

    public void SwitchTab(int index)
    {
        if (index < 0 || index >= Tabs.Count || Tabs[index] == _activeTab) return;
        StoreActiveTab();
        ActivateTab(Tabs[index]);
        StatusText = $"タブ「{_activeTab.Name}」に切り替えました";
    }

    /// <summary>
    /// 選択行を新しいタブへ移動して分離する。タブを閉じるとメインへ戻る。
    /// </summary>
    public TabState? SplitSelectedToNewTab(IReadOnlyList<int> indexes)
    {
        if (indexes.Count == 0) return null;
        if (indexes.Count >= Document.Lines.Count(l => !l.IsEmpty))
        {
            StatusText = "すべての行を分離することはできません";
            return null;
        }

        var newDoc = LineOperations.ExtractLines(Document, indexes, GetEffectiveEmojiList());
        foreach (int i in indexes.OrderByDescending(x => x))
        {
            LineOperations.DeleteLine(Document, i);
        }

        // 分離は「タブを閉じる」で戻せるため、混乱を避けて Undo 履歴はリセットする
        _activeTab.UndoStack.Clear();
        _activeTab.RedoStack.Clear();
        RebuildLines();
        MarkModified();
        StoreActiveTab();

        var tab = new TabState
        {
            Name = $"パート{Tabs.Count}",
            Document = newDoc,
            Format = CurrentFormat,
            IsModified = true,
        };
        Tabs.Add(tab);
        SaveProject();
        StatusText = $"{indexes.Count} 行を「{tab.Name}」タブへ分離しました（タブを閉じるとメインへ戻ります）";
        return tab;
    }

    /// <summary>
    /// 選択行を既存のタブへ移動する（時刻順にマージ。済マークなどの行状態は維持）。
    /// </summary>
    public bool MoveSelectedToTab(IReadOnlyList<int> indexes, TabState target)
    {
        if (target == _activeTab || indexes.Count == 0 || !Tabs.Contains(target)) return false;

        var lines = indexes.Distinct().OrderBy(x => x)
            .Where(i => i >= 0 && i < Document.Lines.Count)
            .Select(i => Document.Lines[i])
            .ToList();
        if (lines.Count == 0) return false;

        foreach (int i in indexes.Distinct().OrderByDescending(x => x))
        {
            LineOperations.DeleteLine(Document, i);
        }

        // タブ間の行移動は Undo 対象外（逆向きの移動で戻せる）
        _activeTab.UndoStack.Clear();
        _activeTab.RedoStack.Clear();
        RebuildLines();
        MarkModified();
        StoreActiveTab();

        var carrier = new LyricsDocument();
        carrier.Lines.AddRange(lines);
        MergeLinesByTime(target.Document, carrier);
        target.IsModified = true;
        target.UndoStack.Clear();
        target.RedoStack.Clear();

        SaveProject();
        StatusText = $"{lines.Count} 行を「{target.Name}」タブへ移動しました（時刻順に挿入）";
        return true;
    }

    /// <summary>タブを閉じて、行をメインへ時刻順にマージして戻す。</summary>
    public void CloseTab(TabState tab)
    {
        if (tab.IsMain || !Tabs.Contains(tab)) return;
        StoreActiveTab();

        var main = Tabs.First(t => t.IsMain);
        MergeLinesByTime(main.Document, tab.Document);
        main.IsModified = true;
        main.UndoStack.Clear();
        main.RedoStack.Clear();
        Tabs.Remove(tab);

        if (_activeTab == tab)
        {
            ActivateTab(main);
        }
        else if (_activeTab == main)
        {
            RebuildLines();
            MarkModified();
        }
        SaveProject();
        StatusText = $"タブ「{tab.Name}」の行をメインへ時刻順に戻しました";
    }

    /// <summary>タブの行を、先頭タイムタグの時刻順になるようメインへ挿入する。</summary>
    private static void MergeLinesByTime(LyricsDocument main, LyricsDocument part)
    {
        int insertAfter = -1;
        foreach (var line in part.Lines)
        {
            int idx;
            if (line.GetFirstTimeCs() is int t)
            {
                idx = 0;
                while (idx < main.Lines.Count &&
                       (main.Lines[idx].GetFirstTimeCs() is not int mt || mt <= t))
                {
                    idx++;
                }
            }
            else
            {
                idx = insertAfter + 1; // タグ無し行（空行など）は直前に挿入した行の次へ
            }
            main.Lines.Insert(idx, line);
            insertAfter = idx;
        }
    }

    /// <summary>すべての分離タブを閉じて、行をメインへ時刻順に戻す（タブ分離の初期化）。</summary>
    public void ResetAllTabs()
    {
        if (Tabs.Count <= 1)
        {
            StatusText = "分離タブはありません";
            return;
        }
        StoreActiveTab();

        var main = Tabs.First(t => t.IsMain);
        int lineCount = 0;
        foreach (var tab in Tabs.Where(t => !t.IsMain).ToList())
        {
            lineCount += tab.Document.Lines.Count;
            MergeLinesByTime(main.Document, tab.Document);
            Tabs.Remove(tab);
        }
        main.IsModified = true;
        main.UndoStack.Clear();
        main.RedoStack.Clear();
        ActivateTab(main);
        SaveProject();
        StatusText = $"タブ分離を解除し、{lineCount} 行をメインへ時刻順に戻しました";
    }

    public void RenameTab(TabState tab, string newName)
    {
        newName = newName.Trim();
        if (newName.Length == 0) return;
        tab.Name = newName;
        if (tab == _activeTab) UpdateTitle();
        SaveProject();
    }

    /// <summary>保存・エクスポートの推奨ファイル名（拡張子なし）。分離タブは「メイン名_タブ名」。</summary>
    public string GetSuggestedFileBaseName()
    {
        if (CurrentFilePath is string p) return Path.GetFileNameWithoutExtension(p);
        string baseName = Tabs.FirstOrDefault(t => t.IsMain)?.FilePath is string mp
            ? Path.GetFileNameWithoutExtension(mp)
            : "lyrics";
        return _activeTab.IsMain ? baseName : $"{baseName}_{_activeTab.Name}";
    }

    /// <summary>アプリ設定（検証・絵文字）。</summary>
    public AppSettings Settings { get; } = AppSettings.Load();

    /// <summary>テキスト幅の実測器（App 起動時に DirectWrite 実装を設定）。</summary>
    public ITextMeasurer? TextMeasurer { get; set; }

    public string? CurrentFilePath { get; private set; }

    public DocumentFormat CurrentFormat { get; private set; } = DocumentFormat.Lrc;

    /// <summary>lrc 保存時のエンコーディング（デフォルト BOM 付き UTF-8）。</summary>
    public Encoding LrcEncoding { get; set; } = EncodingDetector.Utf8Bom;

    // ------------------------------------------------------------ 読み込み

    public void OpenFile(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".rlf")
        {
            LoadDocument(RlfFormat.ReadFile(path), path, DocumentFormat.Rlf);
        }
        else
        {
            string text = EncodingDetector.ReadAllText(path, out var detected);
            LrcEncoding = detected;
            LoadDocument(TextEditModeFormat.Parse(text), path, DocumentFormat.Lrc);
        }

        // 曲プロジェクト（.tttproj）から済マーク・メディアパス・スロット並び順・分離タブを復元
        string? tabRestoreNote = null;
        if (SongProject.TryLoad(path) is { } project)
        {
            MediaPath = project.MediaPath;

            // タブ分離状態の復元:
            // 分離はファイル自体を書き換えないため、分離後のメインは MainText から復元する。
            // 歌詞ファイルが外部で変更されていたら（フィンガープリント不一致）、
            // 古いタブ状態は破棄してファイルの内容をそのまま表示する。
            bool restoreTabs = project.Tabs.Count > 0 && project.MainText.Length > 0;
            if (restoreTabs && project.FileFingerprint != SongProject.ComputeFingerprint(path))
            {
                restoreTabs = false;
                tabRestoreNote = "（歌詞ファイルが外部で変更されていたため、前回のタブ分離状態は破棄しました）";
            }

            if (restoreTabs)
            {
                var mainDoc = TextEditModeFormat.Parse(project.MainText);
                mainDoc.RlfExtras = Document.RlfExtras; // rlf 固有情報はファイル読込分を引き継ぐ
                Document = mainDoc;
                _activeTab.Document = mainDoc;
                RebuildLines();
            }

            foreach (int i in project.ExportedLines)
            {
                if (i >= 0 && i < Lines.Count) Lines[i].Exported = true;
            }
            ApplySavedSlotOrder(project.EmojiSlots);

            if (restoreTabs)
            {
                foreach (var pt in project.Tabs)
                {
                    var tabDoc = TextEditModeFormat.Parse(pt.Text);
                    foreach (int i in pt.ExportedLines)
                    {
                        if (i >= 0 && i < tabDoc.Lines.Count) tabDoc.Lines[i].Exported = true;
                    }
                    Tabs.Add(new TabState
                    {
                        Name = pt.Name,
                        Document = tabDoc,
                        Format = CurrentFormat,
                    });
                }
            }
        }
        else
        {
            MediaPath = null;
        }

        StatusText = $"読み込みました: {Path.GetFileName(path)}（{Lines.Count} 行）";

        // 同じフォルダに n3proj が 1 つだけあれば、フォント・画面幅設定を自動で取り込む
        _n3projLineTimes = null;
        if (N3ProjFormat.FindNear(path) is string n3proj)
        {
            try
            {
                ApplyN3ProjSettings(n3proj);
            }
            catch (Exception)
            {
                // n3proj が読めなくても歌詞の読み込みは成功扱い
            }
        }

        if (tabRestoreNote is not null)
        {
            StatusText += tabRestoreNote;
        }
    }

    /// <summary>クリップボード等のテキストを取り込む（テキスト編集モード形式 / lrc 両対応）。</summary>
    public void LoadFromText(string text)
    {
        _n3projLineTimes = null;
        LoadDocument(TextEditModeFormat.Parse(text), null, DocumentFormat.Lrc);
        StatusText = $"テキストを取り込みました（{Lines.Count} 行）";
    }

    public void LoadDocument(LyricsDocument doc, string? path, DocumentFormat format)
    {
        // ファイルを開き直したらタブ構成もリセット（分離タブは .tttproj から復元される）
        Tabs.Clear();
        _activeTab = new TabState
        {
            IsMain = true,
            Document = doc,
            FilePath = path,
            Format = format,
        };
        Tabs.Add(_activeTab);

        Document = doc;
        CurrentFilePath = path;
        CurrentFormat = format;
        IsModified = false;
        RebuildLines();
        AssignSlotsToSongEmoji();
        RefreshEmojiSlots();
        UpdateTitle();
    }

    /// <summary>
    /// 読み込んだファイル内の @Emoji タグをパレットのスロットへ自動登録する。
    /// グローバルリストが使っていないスロットを優先し、足りなければ上書きで割り当てる。
    /// </summary>
    private void AssignSlotsToSongEmoji()
    {
        var usedSlots = new HashSet<int>(
            Document.EmojiEntries.Where(e => e.Slot is >= 1 and <= 20).Select(e => e.Slot!.Value));
        var globalSlots = new HashSet<int>(
            Settings.GlobalEmojiList.Where(e => e.Slot is >= 1 and <= 20).Select(e => e.Slot!.Value));

        foreach (var e in Document.EmojiEntries.Where(e => e.Slot is null && e.ReplaceChar.Length > 0))
        {
            int slot = Enumerable.Range(1, 20).FirstOrDefault(i => !usedSlots.Contains(i) && !globalSlots.Contains(i));
            if (slot == 0) slot = Enumerable.Range(1, 20).FirstOrDefault(i => !usedSlots.Contains(i));
            if (slot == 0) break; // 20 スロット全て埋まっている
            e.Slot = slot;
            usedSlots.Add(slot);
        }
    }

    private void RebuildLines()
    {
        Lines.Clear();
        for (int i = 0; i < Document.Lines.Count; i++)
        {
            Lines.Add(new LineViewModel(Document.Lines[i], i));
        }
    }

    // ------------------------------------------------------------ 保存

    public void SaveTo(string path, DocumentFormat? format = null)
    {
        var f = format ?? FormatFromExtension(path);
        switch (f)
        {
            case DocumentFormat.Rlf:
                RlfFormat.WriteFile(path, Document);
                break;
            default:
                File.WriteAllText(path, LrcFormat.Write(Document), LrcEncoding);
                break;
        }
        CurrentFilePath = path;
        CurrentFormat = f;
        IsModified = false;
        UpdateTitle();
        SaveProject();
        RememberSaveFolder(path);
        StatusText = $"保存しました: {Path.GetFileName(path)}";
    }

    /// <summary>メインタブのファイルパス（上書き保存の対象）。</summary>
    public string? MainFilePath
    {
        get
        {
            StoreActiveTab();
            return Tabs.FirstOrDefault(t => t.IsMain)?.FilePath;
        }
    }

    /// <summary>全タブを時刻順に統合した全行ドキュメントを作る（タブ構成は変更しない）。</summary>
    public LyricsDocument BuildMergedDocument()
    {
        StoreActiveTab();
        var main = Tabs.First(t => t.IsMain);
        var merged = main.Document.Clone();
        foreach (var tab in Tabs.Where(t => !t.IsMain))
        {
            var carrier = new LyricsDocument();
            carrier.Lines.AddRange(tab.Document.Lines.Select(l => l.Clone()));
            MergeLinesByTime(merged, carrier);
        }
        return merged;
    }

    /// <summary>
    /// タブ含む全行を時刻順に統合してファイルへ保存する（マスター保存）。
    /// 画面上のタブ分離はそのまま維持される。
    /// </summary>
    public void SaveFullTo(string path, DocumentFormat? format = null)
    {
        var f = format ?? FormatFromExtension(path);
        var merged = BuildMergedDocument();
        if (f == DocumentFormat.Rlf)
        {
            RlfFormat.WriteFile(path, merged);
        }
        else
        {
            File.WriteAllText(path, LrcFormat.Write(merged), LrcEncoding);
        }

        var main = Tabs.First(t => t.IsMain);
        main.FilePath = path;
        main.Format = f;
        foreach (var tab in Tabs) tab.IsModified = false;
        if (_activeTab.IsMain)
        {
            CurrentFilePath = path;
            CurrentFormat = f;
        }
        IsModified = false;
        UpdateTitle();
        SaveProject();
        RememberSaveFolder(path);

        int tabCount = Tabs.Count - 1;
        StatusText = tabCount > 0
            ? $"タブ含む全 {merged.Lines.Count} 行を保存しました: {Path.GetFileName(path)}（タブ分離は維持）"
            : $"保存しました: {Path.GetFileName(path)}";
    }

    /// <summary>
    /// 表示中のタブの内容をファイルから読み込んで差し替える
    /// （RhythmicaLyrics でタイムタグ修正したタブ書き出しファイルを戻す用途。Ctrl+Z で戻せる）。
    /// </summary>
    public void ReloadActiveTabFromFile(string path)
    {
        LyricsDocument doc;
        if (Path.GetExtension(path).Equals(".rlf", StringComparison.OrdinalIgnoreCase))
        {
            doc = RlfFormat.ReadFile(path);
        }
        else
        {
            string text = EncodingDetector.ReadAllText(path, out _);
            doc = TextEditModeFormat.Parse(text);
        }

        PushUndo();
        Document = doc;
        _activeTab.Document = doc;
        RebuildLines();
        AssignSlotsToSongEmoji();
        RefreshEmojiSlots();
        MarkModified();
        SaveProject();
        StatusText = $"タブ「{_activeTab.Name}」へ読み込みました: {Path.GetFileName(path)}（{doc.Lines.Count} 行。Ctrl+Z で差し替え前に戻せます）";
    }

    /// <summary>
    /// 表示中のタブの内容だけをファイルへ書き出す（タブのファイル紐付けは変更しない）。
    /// </summary>
    public void SaveActiveTabCopyTo(string path, DocumentFormat? format = null)
    {
        var f = format ?? FormatFromExtension(path);
        if (f == DocumentFormat.Rlf)
        {
            RlfFormat.WriteFile(path, Document);
        }
        else
        {
            File.WriteAllText(path, LrcFormat.Write(Document), LrcEncoding);
        }
        RememberSaveFolder(path);
        StatusText = $"表示中のタブ「{_activeTab.Name}」を保存しました: {Path.GetFileName(path)}（{Document.Lines.Count} 行）";
    }

    /// <summary>
    /// 保存・エクスポートダイアログの初期フォルダ。
    /// 歌詞ファイルのフォルダ → メディアのフォルダ → 前回保存したフォルダ の優先順。
    /// </summary>
    public string? GetDefaultSaveFolder()
    {
        if (CurrentFilePath is string p &&
            Path.GetDirectoryName(p) is string lyricsDir && Directory.Exists(lyricsDir))
        {
            return lyricsDir;
        }
        if (Tabs.FirstOrDefault(t => t.IsMain)?.FilePath is string mainPath &&
            Path.GetDirectoryName(mainPath) is string mainDir && Directory.Exists(mainDir))
        {
            return mainDir; // 分離タブ（未保存）はメインのフォルダを既定にする
        }
        if (MediaPath is string m &&
            Path.GetDirectoryName(m) is string mediaDir && Directory.Exists(mediaDir))
        {
            return mediaDir;
        }
        if (!string.IsNullOrEmpty(Settings.LastSaveFolder) && Directory.Exists(Settings.LastSaveFolder))
        {
            return Settings.LastSaveFolder;
        }
        return null;
    }

    private void RememberSaveFolder(string filePath)
    {
        if (Path.GetDirectoryName(filePath) is string dir && dir.Length > 0)
        {
            Settings.LastSaveFolder = dir;
            Settings.Save();
        }
    }

    /// <summary>曲プロジェクト（.tttproj）を保存する（済マーク・メディアパス・分離タブ）。</summary>
    public void SaveProject()
    {
        StoreActiveTab();
        var main = Tabs.FirstOrDefault(t => t.IsMain);
        if (main?.FilePath is not string path) return;

        var project = new SongProject
        {
            MediaPath = MediaPath,
            ExportedLines = main.Document.Lines
                .Select((l, i) => (Line: l, Index: i))
                .Where(x => x.Line.Exported)
                .Select(x => x.Index)
                .ToList(),
        };
        foreach (var e in main.Document.EmojiEntries)
        {
            if (e.Slot is int slot && e.ReplaceChar.Length > 0)
            {
                project.EmojiSlots[e.ReplaceChar] = slot;
            }
        }
        foreach (var tab in Tabs.Where(t => !t.IsMain))
        {
            project.Tabs.Add(new SongProjectTab
            {
                Name = tab.Name,
                Text = TextEditModeFormat.Write(tab.Document),
                ExportedLines = tab.Document.Lines
                    .Select((l, i) => (Line: l, Index: i))
                    .Where(x => x.Line.Exported)
                    .Select(x => x.Index)
                    .ToList(),
            });
        }

        // 分離タブがあるときは、分離後のメインの内容も保存する
        // （分離は歌詞ファイル自体を書き換えないため、これが無いと開き直しで行が重複する）
        if (project.Tabs.Count > 0)
        {
            project.MainText = TextEditModeFormat.Write(main.Document);
            project.FileFingerprint = SongProject.ComputeFingerprint(path);
        }

        project.Save(path);
    }

    public static DocumentFormat FormatFromExtension(string path) =>
        Path.GetExtension(path).ToLowerInvariant() == ".rlf" ? DocumentFormat.Rlf : DocumentFormat.Lrc;

    /// <summary>テキスト編集モード形式の全文（クリップボード用）。</summary>
    public string GetTextEditModeText() => TextEditModeFormat.Write(Document);

    // ------------------------------------------------------------ 行編集

    /// <summary>行エディタの生テキストを選択行へ反映する。</summary>
    public bool ApplyRawTextToSelectedLine(string rawText)
    {
        if (SelectedLine is null) return false;
        if (rawText == SelectedLine.RawText) return false; // 変更なし
        PushUndo();
        var newLine = TextEditModeFormat.ParseLyricLine(rawText);
        int idx = SelectedLine.Index;
        Document.Lines[idx] = newLine;
        SelectedLine.ReplaceModel(newLine);
        MarkModified();
        return true;
    }

    public void MarkModified()
    {
        IsModified = true;
        UpdateTitle();
    }

    // ------------------------------------------------------------ Undo / Redo

    private const int MaxUndo = 100;

    /// <summary>ドキュメントを変更する操作の直前に呼ぶ（アクティブタブの履歴に積む）。</summary>
    public void PushUndo()
    {
        _activeTab.UndoStack.Add(Document.Clone());
        if (_activeTab.UndoStack.Count > MaxUndo) _activeTab.UndoStack.RemoveAt(0);
        _activeTab.RedoStack.Clear();
    }

    public bool Undo()
    {
        var undo = _activeTab.UndoStack;
        if (undo.Count == 0)
        {
            StatusText = "元に戻す操作はありません";
            return false;
        }
        _activeTab.RedoStack.Add(Document.Clone());
        Document = undo[^1];
        undo.RemoveAt(undo.Count - 1);
        _activeTab.Document = Document;
        AfterDocumentRestored("元に戻しました");
        return true;
    }

    public bool Redo()
    {
        var redo = _activeTab.RedoStack;
        if (redo.Count == 0)
        {
            StatusText = "やり直す操作はありません";
            return false;
        }
        _activeTab.UndoStack.Add(Document.Clone());
        Document = redo[^1];
        redo.RemoveAt(redo.Count - 1);
        _activeTab.Document = Document;
        AfterDocumentRestored("やり直しました");
        return true;
    }

    private void AfterDocumentRestored(string message)
    {
        RebuildLines();
        RefreshEmojiSlots();
        MarkModified();
        StatusText = message;
    }

    // ------------------------------------------------------------ 行操作

    /// <summary>構造変更後に行 VM を作り直す（済マークはモデル側に保持されている）。</summary>
    public void RebuildLinesPreservingMarks() => RebuildLines();

    /// <summary>選択行を生テキストのカーソル位置で分割する。成功時は新しい行のインデックスを返す。</summary>
    public int? SplitSelectedLine(string rawText, int cursor)
    {
        if (SelectedLine is null) return null;
        int index = SelectedLine.Index;
        PushUndo();

        var line = TextEditModeFormat.ParseLyricLine(rawText);
        cursor = SnapCursorOutsideToken(rawText, Math.Clamp(cursor, 0, rawText.Length));
        int charIndex = TextEditModeFormat.ParseLyricLine(rawText[..cursor]).Chars.Count;

        Document.Lines[index] = line;
        LineOperations.SplitLine(Document, index, charIndex);
        RebuildLinesPreservingMarks();
        MarkModified();
        StatusText = "行を分割しました";
        return index + 1;
    }

    /// <summary>選択行に次の行を結合する。</summary>
    public bool JoinSelectedWithNext()
    {
        if (SelectedLine is null) return false;
        int index = SelectedLine.Index;
        if (index + 1 >= Document.Lines.Count) return false;
        PushUndo();
        LineOperations.JoinWithNextLine(Document, index);
        RebuildLinesPreservingMarks();
        MarkModified();
        StatusText = "行を結合しました";
        return true;
    }

    /// <summary>選択行の下に空行（ページ区切り）を挿入する。</summary>
    public void InsertEmptyLineBelowSelection()
    {
        int index = SelectedLine?.Index + 1 ?? Document.Lines.Count;
        PushUndo();
        LineOperations.InsertEmptyLine(Document, index);
        RebuildLinesPreservingMarks();
        MarkModified();
        StatusText = "空行（ページ区切り）を挿入しました";
    }

    /// <summary>指定行を削除する。</summary>
    public void DeleteLines(IReadOnlyList<int> indexes)
    {
        PushUndo();
        foreach (int i in indexes.OrderByDescending(x => x))
        {
            LineOperations.DeleteLine(Document, i);
        }
        RebuildLinesPreservingMarks();
        MarkModified();
        StatusText = $"{indexes.Count} 行を削除しました";
    }

    // ------------------------------------------------------------ エクスポート

    /// <summary>選択行をテキスト編集モード形式のテキストにする。</summary>
    public string ExportLinesAsText(IReadOnlyList<int> indexes)
    {
        var doc = LineOperations.ExtractLines(Document, indexes, GetEffectiveEmojiList());
        return TextEditModeFormat.Write(doc);
    }

    /// <summary>選択行をファイル（lrc / rlf / txt）へエクスポートする。</summary>
    public void ExportLinesToFile(string path, IReadOnlyList<int> indexes)
    {
        var doc = LineOperations.ExtractLines(Document, indexes, GetEffectiveEmojiList());
        if (FormatFromExtension(path) == DocumentFormat.Rlf)
        {
            RlfFormat.WriteFile(path, doc);
        }
        else
        {
            File.WriteAllText(path, LrcFormat.Write(doc), LrcEncoding);
        }
        MarkExported(indexes, true);
        RememberSaveFolder(path);
        StatusText = $"{indexes.Count} 行をエクスポートしました: {Path.GetFileName(path)}";
    }

    /// <summary>済マークを付け外しする。</summary>
    public void MarkExported(IReadOnlyList<int> indexes, bool value)
    {
        foreach (int i in indexes)
        {
            if (i >= 0 && i < Lines.Count) Lines[i].Exported = value;
        }
        SaveProject();
    }

    // ------------------------------------------------------------ メディア再生

    /// <summary>関連付けられたメディアファイル（動画/音源）のパス。</summary>
    [ObservableProperty]
    private string? mediaPath;

    /// <summary>@TimeRatio（winamp 時間補正）。未指定は 1.0。</summary>
    public double TimeRatio =>
        double.TryParse(Document.GetTag("TimeRatio"), out double r) && r > 0 ? r : 1.0;

    /// <summary>@Offset（ms）。未指定は 0。</summary>
    public double OffsetMs =>
        double.TryParse(Document.GetTag("Offset"), out double o) ? o : 0;

    /// <summary>タイムタグ時刻（10ms 単位）→ メディア再生位置（秒）。</summary>
    public double TagCsToMediaSeconds(int cs) => (cs * 10.0 * TimeRatio + OffsetMs) / 1000.0;

    /// <summary>メディア再生位置（秒）→ タイムタグ時刻（10ms 単位）。</summary>
    public int MediaSecondsToTagCs(double seconds) =>
        (int)Math.Round((seconds * 1000.0 - OffsetMs) / (10.0 * TimeRatio));

    /// <summary>再生位置（タグ時刻）に該当する行インデックスを返す。</summary>
    public int? FindLineIndexAtTime(int cs)
    {
        int? best = null;
        int bestStart = int.MinValue;
        for (int i = 0; i < Document.Lines.Count; i++)
        {
            var line = Document.Lines[i];
            if (line.IsEmpty) continue;
            if (line.GetFirstTimeCs() is not int start) continue;
            if (start <= cs && start > bestStart)
            {
                best = i;
                bestStart = start;
            }
        }
        return best;
    }

    /// <summary>再生追従ハイライトを更新し、現在行のインデックスを返す。</summary>
    public int? UpdateCurrentLine(int cs)
    {
        int? index = FindLineIndexAtTime(cs);
        for (int i = 0; i < Lines.Count; i++)
        {
            Lines[i].IsCurrent = i == index;
        }
        return index;
    }

    // ------------------------------------------------------------ 絵文字

    public ObservableCollection<EmojiSlotViewModel> EmojiSlots { get; } =
        new(Enumerable.Range(1, 20).Select(i => new EmojiSlotViewModel(i)));

    /// <summary>20 スロットに入りきらなかった曲内 @Emoji（クリック挿入のみ）。</summary>
    public ObservableCollection<EmojiEntry> UnslottedEmoji { get; } = new();

    /// <summary>スロット表示を（グローバル＋曲上書きから）更新する。</summary>
    public void RefreshEmojiSlots()
    {
        foreach (var slot in EmojiSlots)
        {
            var song = Document.EmojiEntries.FirstOrDefault(e => e.Slot == slot.Slot);
            var global = Settings.GlobalEmojiList.FirstOrDefault(e => e.Slot == slot.Slot);
            slot.Entry = song ?? global;
            slot.IsSongOverride = song is not null;
        }

        UnslottedEmoji.Clear();
        var seen = new HashSet<string>();
        foreach (var e in Document.EmojiEntries.Where(e => e.Slot is null && e.ReplaceChar.Length > 0))
        {
            if (seen.Add(e.ReplaceChar)) UnslottedEmoji.Add(e);
        }
        foreach (var e in Settings.GlobalEmojiList.Where(e => e.Slot is null && e.ReplaceChar.Length > 0))
        {
            if (seen.Add(e.ReplaceChar)) UnslottedEmoji.Add(e);
        }
    }

    /// <summary>曲プロジェクトに保存されたスロット並び順を適用する。</summary>
    private void ApplySavedSlotOrder(Dictionary<string, int> savedSlots)
    {
        if (savedSlots.Count == 0) return;

        // いったん全曲内エントリのスロットを外し、保存された割り当てを適用
        foreach (var e in Document.EmojiEntries) e.Slot = null;

        var used = new HashSet<int>();
        foreach (var e in Document.EmojiEntries)
        {
            if (savedSlots.TryGetValue(e.ReplaceChar, out int slot) &&
                slot is >= 1 and <= 20 && used.Add(slot))
            {
                e.Slot = slot;
            }
        }

        // 保存に無かったエントリは空きスロットへ
        AssignSlotsToSongEmoji();
        RefreshEmojiSlots();
    }

    /// <summary>
    /// パレットの現在の並び順（EmojiSlots の順）どおりにスロット番号 1–20 を振り直す。
    /// ドラッグ＆ドロップによる並び替えの確定処理。
    /// </summary>
    public void ApplySlotOrderFromView()
    {
        var entries = EmojiSlots.Select(s => s.Entry).ToList();
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i] is { } e) e.Slot = i + 1;
        }

        // VM をスロット番号順に作り直す
        EmojiSlots.Clear();
        for (int i = 1; i <= 20; i++)
        {
            EmojiSlots.Add(new EmojiSlotViewModel(i));
        }
        RefreshEmojiSlots();
        Settings.Save();  // グローバル分の並びを保存
        SaveProject();    // 曲分の並びを保存
        StatusText = "絵文字スロットの並びを変更しました";
    }

    /// <summary>スロット外のエントリをキー付きスロットへ昇格させる（空きが無ければ末尾スロットと入れ替え）。</summary>
    public void PromoteEmojiToSlot(EmojiEntry entry)
    {
        int free = Enumerable.Range(1, 20).FirstOrDefault(i => EmojiSlots.First(s => s.Slot == i).Entry is null);
        if (free == 0)
        {
            // 空きが無い → スロット 20 の曲内エントリを外して入れ替え
            var last = Document.EmojiEntries.FirstOrDefault(e => e.Slot == 20);
            if (last is not null) last.Slot = null;
            free = 20;
        }
        entry.Slot = free;
        RefreshEmojiSlots();
        SaveProject();
        StatusText = $"{entry.ReplaceChar} をキー {EmojiSlotViewModel.KeyLabels[free - 1]} に割り当てました（ドラッグで並び替えできます）";
    }

    private EmojiTagSettings EmojiTagSettings => new()
    {
        LeadCs = Settings.EmojiLeadCs,
        PerEmoji = Settings.EmojiTagPerEmoji,
    };

    /// <summary>
    /// 実効絵文字リスト（＋追加の文字列）から出現マッチャーを作る。
    /// プレースホルダ文字（＿）も絵文字と同じタグ付け・除外の対象に含める。
    /// </summary>
    public EmojiMatcher CreateEmojiMatcher(string? extra = null)
    {
        var strings = GetEffectiveEmojiList().Select(e => e.ReplaceChar);
        if (!string.IsNullOrEmpty(Settings.PlaceholderChar)) strings = strings.Append(Settings.PlaceholderChar);
        if (extra is not null) strings = strings.Append(extra);
        return new EmojiMatcher(strings);
    }

    /// <summary>スロット番号 → 置き換え文字列。未設定なら null。</summary>
    public string? GetSlotEmojiString(int slotNumber)
    {
        var slot = EmojiSlots.FirstOrDefault(s => s.Slot == slotNumber);
        return slot is not null && slot.HasEntry ? slot.Entry!.ReplaceChar : null;
    }

    /// <summary>
    /// 選択行の生テキスト raw のカーソル位置 cursor に絵文字（置き換え文字列）を挿入し、
    /// タイムタグを自動付与した新しい生テキストとカーソル位置を返す。
    /// </summary>
    public (string NewRaw, int NewCursor)? InsertEmojiIntoRaw(string raw, int cursor, string emojiChar)
    {
        PushUndo();
        cursor = SnapCursorOutsideToken(raw, Math.Clamp(cursor, 0, raw.Length));

        var line = TextEditModeFormat.ParseLyricLine(raw);
        int charIndex = TextEditModeFormat.ParseLyricLine(raw[..cursor]).Chars.Count;

        var matcher = CreateEmojiMatcher(emojiChar);
        int inserted = EmojiTagger.InsertEmoji(line, charIndex, emojiChar, matcher, EmojiTagSettings);

        if (SelectedLine is not null)
        {
            Document.Lines[SelectedLine.Index] = line;
            SelectedLine.ReplaceModel(line);
            MarkModified();
        }

        StatusText = EmojiTagger.HasUntaggableEmoji(line, matcher)
            ? $"絵文字 {emojiChar} を挿入しました（直後にタイムタグ付きの文字が無いため時刻は未設定です）"
            : $"絵文字 {emojiChar} を挿入しました";

        string newRaw = TextEditModeFormat.WriteLyricLine(line);
        int newCursor = FindCursorAfterCharIndex(newRaw, charIndex + inserted);
        return (newRaw, newCursor);
    }

    /// <summary>ドキュメント全体の絵文字タイムタグを付け直す。</summary>
    public void RetagAllEmoji()
    {
        var matcher = CreateEmojiMatcher();
        if (matcher.IsEmpty)
        {
            StatusText = "絵文字リストが空です（絵文字 > 絵文字リスト編集 で設定してください）";
            return;
        }
        PushUndo();
        EmojiTagger.RetagAll(Document, matcher, EmojiTagSettings);
        foreach (var line in Lines) line.RaiseAllChanged();
        MarkModified();
        StatusText = "絵文字のタイムタグを再計算しました";
    }

    /// <summary>カーソルが [..] / {..} トークンの内側にある場合、トークンの直後へ移動する。</summary>
    internal static int SnapCursorOutsideToken(string raw, int cursor)
    {
        int i = 0;
        while (i < raw.Length)
        {
            char c = raw[i];
            if (c is '[' or '{')
            {
                char close = c == '[' ? ']' : '}';
                int end = raw.IndexOf(close, i + 1);
                if (end < 0) break;
                if (cursor > i && cursor <= end) return end + 1;
                i = end + 1;
                continue;
            }
            i++;
        }
        return cursor;
    }

    /// <summary>生テキスト上で charIndex 個の CharUnit を消費した直後の位置を返す。</summary>
    private static int FindCursorAfterCharIndex(string raw, int charIndex)
    {
        for (int pos = 0; pos <= raw.Length; pos++)
        {
            pos = SnapCursorOutsideToken(raw, pos);
            if (pos > raw.Length) break;
            if (TextEditModeFormat.ParseLyricLine(raw[..pos]).Chars.Count >= charIndex)
            {
                return pos;
            }
        }
        return raw.Length;
    }

    // ------------------------------------------------------ 絵文字挿入ビュー

    /// <summary>
    /// 挿入ビュー用の全文テキスト（タグ・スペーサーなし、行区切りは \r）。
    /// WinUI の TextBox は改行を \r に正規化するため、オフセット計算も \r 前提で行う。
    /// </summary>
    public string BuildInsertViewText() =>
        string.Join("\r", Document.Lines.Select(l => l.GetDisplayText()));

    /// <summary>挿入ビューの表示オフセット → (行, 行内の表示文字オフセット)。</summary>
    public (int LineIndex, int CharOffset)? MapInsertViewOffset(int offset)
    {
        int pos = 0;
        for (int i = 0; i < Document.Lines.Count; i++)
        {
            string t = Document.Lines[i].GetDisplayText();
            if (offset <= pos + t.Length) return (i, Math.Max(0, offset - pos));
            pos += t.Length + 1; // \r
        }
        return Document.Lines.Count == 0
            ? null
            : (Document.Lines.Count - 1, Document.Lines[^1].GetDisplayText().Length);
    }

    /// <summary>指定行の先頭の表示オフセット。</summary>
    public int GetInsertViewLineStart(int lineIndex)
    {
        int pos = 0;
        for (int i = 0; i < lineIndex && i < Document.Lines.Count; i++)
        {
            pos += Document.Lines[i].GetDisplayText().Length + 1;
        }
        return pos;
    }

    /// <summary>
    /// 挿入ビューのカーソル位置に絵文字（置き換え文字列）を挿入する。
    /// 成功時は挿入後のカーソル位置（表示オフセット）を返す。
    /// </summary>
    public int? InsertEmojiAtViewOffset(int offset, string emojiChar)
    {
        if (MapInsertViewOffset(offset) is not var (lineIndex, charOffset)) return null;

        PushUndo();
        var line = Document.Lines[lineIndex];

        // 表示文字オフセット → CharUnit 挿入位置（スペーサーは表示幅 0）
        int unitIndex = DisplayOffsetToUnitIndex(line, charOffset);

        var matcher = CreateEmojiMatcher(emojiChar);
        EmojiTagger.InsertEmoji(line, unitIndex, emojiChar, matcher, EmojiTagSettings);

        if (lineIndex < Lines.Count) Lines[lineIndex].RaiseAllChanged();
        MarkModified();

        StatusText = EmojiTagger.HasUntaggableEmoji(line, matcher)
            ? $"絵文字 {emojiChar} を挿入しました（直後にタイムタグ付きの文字が無いため時刻は未設定です）"
            : $"絵文字 {emojiChar} を挿入しました";

        return GetInsertViewLineStart(lineIndex) + charOffset + emojiChar.Length;
    }

    /// <summary>行内の表示文字オフセット → CharUnit 挿入位置（スペーサーは表示幅 0）。</summary>
    private static int DisplayOffsetToUnitIndex(LyricsLine line, int charOffset)
    {
        int unitIndex = 0;
        int disp = 0;
        foreach (var c in line.Chars)
        {
            if (disp >= charOffset) break;
            if (!c.IsSpacer) disp += c.Text.Length;
            unitIndex++;
        }
        return unitIndex;
    }

    /// <summary>
    /// 挿入ビューのカーソル位置にある絵文字を削除する。
    /// forward=true は Delete（カーソル位置の絵文字）、false は Backspace（カーソル直前の絵文字）。
    /// 成功時は削除後のカーソル位置を返す。絵文字以外は削除しない。
    /// </summary>
    public int? DeleteEmojiAtViewOffset(int offset, bool forward)
    {
        if (MapInsertViewOffset(offset) is not var (lineIndex, charOffset)) return null;
        var line = Document.Lines[lineIndex];
        var matcher = CreateEmojiMatcher();
        if (matcher.IsEmpty)
        {
            StatusText = "絵文字リストが空です";
            return null;
        }

        // 各出現の表示オフセット範囲を計算して対象を探す
        var occurrences = matcher.FindOccurrences(line.Chars);
        var emojiUnitIndexes = new HashSet<int>();
        foreach (var o in occurrences)
        {
            for (int i = o.Start; i < o.EndExclusive; i++) emojiUnitIndexes.Add(i);
        }

        EmojiMatcher.Occurrence? target = null;
        int targetDispStart = 0;
        {
            int disp = 0;
            int occIdx = 0;
            for (int i = 0; i < line.Chars.Count && occIdx < occurrences.Count; i++)
            {
                var occ = occurrences[occIdx];
                if (i == occ.Start)
                {
                    int dispEnd = disp + occ.Value.Length;
                    bool hit = forward
                        ? charOffset >= disp && charOffset < dispEnd   // Delete: カーソルが出現の中か先頭
                        : charOffset > disp && charOffset <= dispEnd;  // Backspace: カーソルが出現の中か直後
                    if (hit)
                    {
                        target = occ;
                        targetDispStart = disp;
                        break;
                    }
                    disp = dispEnd;
                    i = occ.EndExclusive - 1;
                    occIdx++;
                    continue;
                }
                if (!line.Chars[i].IsSpacer) disp += line.Chars[i].Text.Length;
            }
        }

        if (target is not { } t)
        {
            StatusText = "カーソル位置に絵文字がありません（歌詞の文字はここでは削除できません）";
            return null;
        }

        PushUndo();

        // 出現ユニット＋絵文字チェーン用の隣接スペーサーを削除
        var removeIndexes = new List<int>();
        for (int i = t.Start; i < t.EndExclusive; i++) removeIndexes.Add(i);
        int after = t.EndExclusive;
        if (after < line.Chars.Count && line.Chars[after].IsSpacer && emojiUnitIndexes.Contains(after + 1))
        {
            removeIndexes.Add(after);
        }
        else
        {
            int before = t.Start - 1;
            if (before >= 0 && line.Chars[before].IsSpacer && emojiUnitIndexes.Contains(before - 1))
            {
                removeIndexes.Add(before);
            }
        }
        foreach (int i in removeIndexes.OrderByDescending(x => x))
        {
            line.Chars.RemoveAt(i);
        }

        EmojiTagger.RetagLine(line, matcher, EmojiTagSettings);
        if (lineIndex < Lines.Count) Lines[lineIndex].RaiseAllChanged();
        MarkModified();
        StatusText = $"絵文字 {t.Value} を削除しました";

        return GetInsertViewLineStart(lineIndex) + targetDispStart;
    }

    /// <summary>挿入ビューのカーソル位置で行を分割する。成功時は新しいカーソル位置を返す。</summary>
    public int? SplitLineAtViewOffset(int offset)
    {
        if (MapInsertViewOffset(offset) is not var (lineIndex, charOffset)) return null;
        var line = Document.Lines[lineIndex];
        int unitIndex = DisplayOffsetToUnitIndex(line, charOffset);

        PushUndo();
        LineOperations.SplitLine(Document, lineIndex, unitIndex);
        RebuildLinesPreservingMarks();
        MarkModified();
        StatusText = "行を分割しました";
        return offset + 1; // 改行が 1 文字分入る
    }

    /// <summary>挿入ビューのカーソル行に次の行を結合する。成功時はカーソル位置（変化なし）を返す。</summary>
    public int? JoinLineAtViewOffset(int offset)
    {
        if (MapInsertViewOffset(offset) is not var (lineIndex, _)) return null;
        if (lineIndex + 1 >= Document.Lines.Count) return null;

        PushUndo();
        LineOperations.JoinWithNextLine(Document, lineIndex);
        RebuildLinesPreservingMarks();
        MarkModified();
        StatusText = "行を結合しました";
        return offset;
    }

    // ------------------------------------------------------ テンプレート

    /// <summary>
    /// 現在の設定＋実効絵文字リスト（曲内 @Emoji 含む）をテンプレートとして保存する。
    /// </summary>
    public void SaveTemplate(string path)
    {
        var snapshot = new AppSettings();
        snapshot.CopyFrom(Settings);
        snapshot.GlobalEmojiList = GetEffectiveEmojiList().Select(e => e.Clone()).ToList();
        snapshot.Save(path);
        StatusText = $"テンプレートを保存しました: {Path.GetFileName(path)}（絵文字 {snapshot.GlobalEmojiList.Count} 件＋フォント・チェック設定）";
    }

    /// <summary>テンプレートを読み込んでアプリ設定（グローバル）に適用する。</summary>
    public void LoadTemplate(string path)
    {
        var template = AppSettings.Load(path);
        Settings.CopyFrom(template);
        Settings.Save();
        RefreshEmojiSlots();
        StatusText = $"テンプレートを適用しました: {Path.GetFileName(path)}（絵文字 {Settings.GlobalEmojiList.Count} 件、{Settings.FontFamily} {Settings.FontSizePx:F0}px）";
    }

    // -------------------------------------------------- ニコカラメーカー連携

    /// <summary>n3proj から取り込んだ行ごとの実表示区間（ページ衝突チェックで推定値の代わりに使う）。</summary>
    private List<N3ProjLineTime>? _n3projLineTimes;

    /// <summary>n3proj から画面幅・フォント設定・実表示区間を取り込む。</summary>
    public void ApplyN3ProjSettings(string path)
    {
        var s = N3ProjFormat.Read(path);
        Settings.ScreenWidthPx = s.ScreenWidth;
        if (s.MainFont is { } font)
        {
            Settings.FontFamily = font.FontName;
            Settings.FontSizePx = Math.Round(font.SizePx, 1);
            Settings.FontBold = font.IsBoldLike;
        }
        Settings.Save();
        _n3projLineTimes = s.LineTimes.Count > 0 ? s.LineTimes : null;

        string fontInfo = s.MainFont is { } f
            ? $"{f.FontName} {Settings.FontSizePx}px / 画面 {s.ScreenWidth}px"
            : $"画面 {s.ScreenWidth}px（フォント情報なし）";
        string timeInfo = _n3projLineTimes is { Count: > 0 } lt ? $" / 実表示区間 {lt.Count} 行分" : "";
        StatusText = $"ニコカラメーカーの設定を取り込みました: {fontInfo}{timeInfo}（{Path.GetFileName(path)}）";
    }

    /// <summary>
    /// n3proj の実表示区間をドキュメントの行に対応付ける。
    /// 行の最初の実文字のタグ時刻（絵文字の先行タグ除く）が ±50ms で一致した行だけに適用する。
    /// </summary>
    private Dictionary<int, (int StartCs, int EndCs)>? BuildLineDisplayOverrides(Func<CharUnit, bool>? exclude)
    {
        if (_n3projLineTimes is not { Count: > 0 } lineTimes) return null;

        var result = new Dictionary<int, (int, int)>();
        for (int i = 0; i < Document.Lines.Count; i++)
        {
            if (Document.Lines[i].GetFirstTimeCs(exclude) is not int firstCs) continue;

            foreach (var lt in lineTimes)
            {
                int firstCharCs = MediaSecondsToTagCs(lt.FirstCharBeginMs / 1000.0);
                if (Math.Abs(firstCharCs - firstCs) <= 5) // ±50ms
                {
                    result[i] = (MediaSecondsToTagCs(lt.ShowBeginMs / 1000.0),
                                 MediaSecondsToTagCs(lt.ShowEndMs / 1000.0));
                    break;
                }
            }
        }
        return result.Count > 0 ? result : null;
    }

    /// <summary>
    /// 挿入ビューのカーソル位置に対応するタイムタグ時刻を返す（カーソル位置から再生用）。
    /// カーソル位置以降で最初にタグを持つ実文字（絵文字・プレースホルダを除く）の時刻。
    /// </summary>
    public int? FindTimeAtViewOffset(int offset)
    {
        if (MapInsertViewOffset(offset) is not var (lineIndex, charOffset)) return null;
        var matcher = CreateEmojiMatcher();

        for (int li = lineIndex; li < Document.Lines.Count; li++)
        {
            var line = Document.Lines[li];
            var emojiUnits = matcher.IsEmpty ? null : matcher.CollectUnits(line);
            int startUnit = li == lineIndex ? DisplayOffsetToUnitIndex(line, charOffset) : 0;
            for (int i = startUnit; i < line.Chars.Count; i++)
            {
                var c = line.Chars[i];
                if (c.IsSpacer || emojiUnits?.Contains(c) == true) continue;
                if (c.TimeCs is int t) return t;
            }
            if (line.EndTimeCs is int end && li == lineIndex) return end;
        }
        return null;
    }

    /// <summary>
    /// 再生位置（タグ時刻）に対応する挿入ビューのカーソル位置。
    /// 「次に歌われる実文字」（絵文字を除く、時刻 &gt; cs の最初の文字）の直前を返す。
    /// </summary>
    public int? FindInsertViewOffsetForTime(int cs)
    {
        var matcher = CreateEmojiMatcher();
        int pos = 0;
        foreach (var line in Document.Lines)
        {
            var emojiUnits = matcher.IsEmpty ? null : matcher.CollectUnits(line);
            foreach (var c in line.Chars)
            {
                if (c.IsSpacer) continue;
                bool isEmoji = emojiUnits?.Contains(c) == true;
                if (!isEmoji && c.TimeCs is int t && t > cs)
                {
                    return pos;
                }
                pos += c.Text.Length;
            }
            pos += 1; // \r
        }
        return null;
    }

    // ------------------------------------------------------------ 検証

    /// <summary>グローバル＋曲ごと上書きをマージした実効 @Emoji リスト。</summary>
    public List<EmojiEntry> GetEffectiveEmojiList()
    {
        var result = new List<EmojiEntry>();
        var songChars = new HashSet<string>(Document.EmojiEntries.Select(e => e.ReplaceChar));
        result.AddRange(Document.EmojiEntries);
        result.AddRange(Settings.GlobalEmojiList.Where(e => !songChars.Contains(e.ReplaceChar)));
        return result;
    }

    /// <summary>全チェックを実行して検証パネルと行リストの表示を更新する。</summary>
    public void RunValidation()
    {
        var emoji = GetEffectiveEmojiList();
        var matcher = CreateEmojiMatcher(); // プレースホルダ（＿）も除外対象に含む
        Func<CharUnit, bool>? exclude = null;
        if (!matcher.IsEmpty)
        {
            var emojiUnits = matcher.CollectUnits(Document);
            if (emojiUnits.Count > 0) exclude = emojiUnits.Contains;
        }

        Issues.Clear();
        foreach (var line in Lines) line.ResetIssueMarks();

        // 1) ページ間行衝突（最重要）
        // n3proj 由来の実表示区間があれば推定値の代わりに使う
        var collisionSettings = Settings.ToCollisionSettings(exclude);
        collisionSettings.LineDisplayCs = BuildLineDisplayOverrides(exclude);
        foreach (var issue in PageRowCollisionValidator.Validate(Document, collisionSettings))
        {
            Issues.Add(issue);

            // 対象行の背景と、衝突している時間セルを強調する
            if (issue.LineIndex >= 0 && issue.LineIndex < Lines.Count)
            {
                Lines[issue.LineIndex].SetRowIssue(issue.Severity);
                Lines[issue.LineIndex].MarkStartTimeIssue(issue.Severity); // 次行の表示開始
            }
            if (issue.RelatedLineIndex is int prev && prev >= 0 && prev < Lines.Count)
            {
                Lines[prev].SetRowIssue(issue.Severity);
                Lines[prev].MarkEndTimeIssue(issue.Severity); // 前行の表示終了
            }
        }

        // 2) 横幅（ピクセル実測）
        if (TextMeasurer is not null)
        {
            string? baseDir = CurrentFilePath is string p ? Path.GetDirectoryName(p) : null;
            var widthSettings = Settings.ToLineWidthSettings(emoji, baseDir);
            var results = LineWidthValidator.Measure(Document, widthSettings, TextMeasurer);
            foreach (var issue in LineWidthValidator.ToIssues(results, widthSettings))
            {
                Issues.Add(issue);
            }
            for (int i = 0; i < Lines.Count && i < results.Count; i++)
            {
                Lines[i].SetWidthResult(results[i]);
                if (results[i].Severity is IssueSeverity s)
                {
                    Lines[i].SetRowIssue(s);
                }
            }
        }

        // 3) 同時歌唱などの重なり情報（正常ケースの目印）
        var overlapped = OverlapInfoDetector.Detect(Document, exclude);
        for (int i = 0; i < Lines.Count; i++)
        {
            Lines[i].SetOverlapInfo(overlapped.Contains(i));
        }

        // 4) 各行に適用フォント情報を表示（n3proj 取り込み結果の確認用）
        foreach (var line in Lines)
        {
            line.SetFontInfo(Settings.FontFamily, Settings.FontSizePx);
        }

        int errors = Issues.Count(i => i.Severity == IssueSeverity.Error);
        int warnings = Issues.Count(i => i.Severity == IssueSeverity.Warning);
        StatusText = Issues.Count == 0
            ? "チェック OK（問題なし）"
            : $"チェック結果: エラー {errors} 件 / 警告 {warnings} 件";
    }

    private void UpdateTitle()
    {
        string? filePath = CurrentFilePath ?? Tabs.FirstOrDefault(t => t.IsMain)?.FilePath;
        string name = filePath is null ? "(無題)" : Path.GetFileName(filePath);
        string tab = _activeTab.IsMain ? "" : $" [{_activeTab.Name}]";
        WindowTitle = $"{name}{tab}{(IsModified ? " *" : "")} - {AppName}";
    }
}
