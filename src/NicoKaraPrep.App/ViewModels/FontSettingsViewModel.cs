using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.ViewModels;

/// <summary>
/// フォント設定ビュー（F3）の状態。一覧（この曲専用のまとまり → アプリ共通の階層）・選択中のフォント設定の編集・保存・元に戻す を受け持つ。
/// アプリ共通のフォント設定はフォルダと親子の階層で並べられ（<see cref="N3FontTree"/>）、フォント設定の一覧は常に階層の上からの順にそろえる
/// （書き出しのニコカラメーカー3 のフォント設定の並び＝この順）。検索・絞り込みの間は、階層の代わりに当てはまるものだけを平らに並べる。
/// 編集は「画面の値 → フォント設定へ書き込み → MarkEdited → 保存の予約（300ms）→ プレビューへ通知」の一方向で、
/// フォント設定から画面の値を読み直すのは、選択を変えたときと一覧を作り直したとき（元に戻す・取り込みなど）だけ。
/// </summary>
public sealed partial class FontSettingsViewModel : ObservableObject
{
    /// <summary>元に戻す の上限。</summary>
    private const int MaxUndo = 100;

    /// <summary>同じ欄の連続した変更（ColorPicker のドラッグなど）を 1 回の 元に戻す にまとめる間隔。</summary>
    private static readonly TimeSpan CoalesceWindow = TimeSpan.FromSeconds(1.5);

    private readonly MainViewModel _main;
    private readonly DispatcherQueueTimer _saveTimer;
    private readonly List<FontListItem> _all = new();
    private readonly List<UndoEntry> _undo = new();
    private readonly List<UndoEntry> _redo = new();
    private bool _commonDirty;
    private bool _songDirty;
    private string? _coalesceKey;
    private DateTime _lastEditUtc;
    private bool _rebuildQueued;

    /// <summary>左の一覧の階層の最上位（曲専用のまとまり → アプリ共通の階層）。</summary>
    private readonly List<FontTreeItem> _treeRoots = new();

    /// <summary>階層の節（識別子 → 節）。</summary>
    private readonly Dictionary<string, FontTreeItem> _treeItems = new(StringComparer.OrdinalIgnoreCase);

    public FontSettingsViewModel(MainViewModel main)
    {
        _main = main;
        Editor = new FontEditorViewModel(this);

        var queue = DispatcherQueue.GetForCurrentThread();
        _saveTimer = queue.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(300);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += (_, _) => FlushPendingSave();

        N3FontLibrary.EnsureIds(_main.Settings.N3FontSets);
        _main.DocumentReplacing += (_, _) => FlushPendingSave();
        _main.CommonFontSetsReplacing += (_, _) => OnCommonFontSetsReplacing();
        _main.CommonFontSetsReplaced += (_, _) => OnCommonFontSetsReplaced();
        _main.PropertyChanged += OnMainPropertyChanged;
        Rebuild(null);
    }

    /// <summary>メイン画面の ViewModel（行・タブ・設定）。</summary>
    public MainViewModel Main => _main;

    /// <summary>選択中のフォント設定の編集欄。</summary>
    public FontEditorViewModel Editor { get; }

    /// <summary>一覧のすべての行（曲専用 → アプリ共通の階層の上からの順。検索と絞り込みに関係なく）。</summary>
    public IReadOnlyList<FontListItem> AllItems => _all;

    /// <summary>検索・絞り込みの結果の一覧に表示している行（検索と絞り込みを通ったもの）。</summary>
    public ObservableCollection<FontListItem> Items { get; } = new();

    /// <summary>左の一覧の階層の最上位の節（曲専用のまとまり → アプリ共通の階層）。</summary>
    public IReadOnlyList<FontTreeItem> TreeRoots => _treeRoots;

    /// <summary>
    /// 階層を作り直したとき（一覧の作り直しの最後。選択はもう決まっている）。ビューはツリーの節を作り直して、今の選択を選ぶ。
    /// </summary>
    public event EventHandler? TreeRebuilt;

    /// <summary>一覧を作り直している最中か（ビューは選択の変化をツリーへ写さず、<see cref="TreeRebuilt"/> でまとめて写す）。</summary>
    public bool IsRebuildingTree { get; private set; }

    /// <summary>絞り込みの選択肢（FilterIndex の順）。</summary>
    public IReadOnlyList<string> FilterChoices { get; } = new[] { "すべて", "アプリ共通", "この曲専用", "未使用", "NKM3 連動" };

    [ObservableProperty]
    private string searchText = "";

    /// <summary>絞り込み（0 すべて / 1 アプリ共通 / 2 この曲専用 / 3 未使用 / 4 NKM3 連動）。</summary>
    [ObservableProperty]
    private int filterIndex;

    [ObservableProperty]
    private FontListItem? selectedItem;

    /// <summary>選択中のフォルダ・「この曲専用」のまとまり（フォント設定を選んでいるときは null）。</summary>
    [ObservableProperty]
    private FontTreeItem? selectedFolder;

    /// <summary>階層（ツリー）で見せているか（false = 検索・絞り込みの結果を平らに並べている）。</summary>
    [ObservableProperty]
    private bool isTreeMode = true;

    /// <summary>検索・絞り込みの結果を平らに並べているか（<see cref="IsTreeMode"/> の逆）。</summary>
    [ObservableProperty]
    private bool isFlatMode;

    /// <summary>フォルダ・まとまりを選んでいるか（中央にフォルダの欄を出す）。</summary>
    [ObservableProperty]
    private bool hasFolder;

    /// <summary>何も選んでいないか（中央に案内を出す）。</summary>
    [ObservableProperty]
    private bool hasNoSelection = true;

    [ObservableProperty]
    private string listSummary = "";

    [ObservableProperty]
    private bool canUndo;

    [ObservableProperty]
    private bool canRedo;

    /// <summary>一覧を作り直している最中か（ListView の選択の変化を選択の操作として扱わない）。</summary>
    public bool IsRebuilding { get; private set; }

    /// <summary>
    /// プレビューに出すフォント設定が変わったとき（選択が変わった・選択中のフォント設定を編集した・元に戻した）。
    /// 引数は表示すべきフォント設定（未選択なら null）。
    /// </summary>
    public event EventHandler<N3FontSet?>? PreviewTargetChanged;

    /// <summary>選択中のフォント設定（未選択なら null）。</summary>
    public N3FontSet? SelectedFont => SelectedItem?.Font;

    /// <summary>選択中の節の識別子（フォント設定の Id か、フォルダ・まとまりの識別子。未選択なら null）。</summary>
    public string? SelectedKey => SelectedItem?.Id ?? SelectedFolder?.Key;

    private List<N3FontSet> Common => _main.Settings.N3FontSets;

    private List<N3FontSet> Song => _main.SongFontSets;

    /// <summary>
    /// アプリ共通のフォント設定の階層（設定に保存する）。まだ無ければ、全部を最上位に今の順で並べて作る
    /// （取り込み元ごとのフォルダへの振り分けは、作ったあとに増えたものだけ）。
    /// </summary>
    private List<N3FontTreeNode> Tree
    {
        get
        {
            if (_main.Settings.N3FontHierarchy is { } tree) return tree;
            tree = new List<N3FontTreeNode>();
            N3FontTree.Normalize(tree, Common, groupImported: false);
            _main.Settings.N3FontHierarchy = tree;
            return tree;
        }
    }

    /// <summary>フォント設定（アプリ共通・この曲専用）の文字種別フォントで使っているフォント名（重複なし）。</summary>
    public IReadOnlyCollection<string> UsedFontNames() =>
        Common.Concat(Song)
            .SelectMany(f => f.Detail.Faces)
            .Select(face => face.FontName)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private void SetStatus(string text) => _main.StatusText = text;

    // ------------------------------------------------------------ 一覧

    /// <summary>
    /// 一覧をフォント設定から作り直し、<paramref name="selectKey"/>（null なら今の選択）の節（フォント設定・フォルダ）を選ぶ。
    /// 見つからなければ、表示している先頭のフォント設定を選ぶ。階層はフォント設定の一覧に合わせて整え、一覧は階層の順にそろえる。
    /// </summary>
    public void Rebuild(string? selectKey)
    {
        selectKey ??= SelectedKey;
        IsRebuildingTree = true;
        try
        {
            var tree = Tree;
            bool changed = N3FontTree.Normalize(tree, Common);
            changed |= N3FontTree.SortFonts(Common, tree);
            if (changed) MarkDirty(song: false);

            _all.Clear();
            foreach (var f in Song) _all.Add(new FontListItem(f, isSong: true));
            foreach (var f in Common) _all.Add(new FontListItem(f, isSong: false));
            BuildTreeItems();
            UpdateItemUsage();
            ApplyFilter(selectKey, forceReload: true);
        }
        finally
        {
            IsRebuildingTree = false;
        }
        TreeRebuilt?.Invoke(this, EventArgs.Empty);
        RunAnalysis();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter(SelectedKey, forceReload: false);

    partial void OnFilterIndexChanged(int value) => ApplyFilter(SelectedKey, forceReload: false);

    partial void OnIsTreeModeChanged(bool value)
    {
        IsFlatMode = !value;
        UpdateCommandState();
    }

    /// <summary>検索（名前か、上の階層の名前に含まれる）と絞り込みに当てはまるか。</summary>
    private bool Matches(FontListItem item)
    {
        if (SearchText.Length > 0
            && !item.Font.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            && !item.PathText.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return FilterIndex switch
        {
            1 => !item.IsSong,
            2 => item.IsSong,
            3 => item.Usage == 0,
            4 => item.IsLinked,
            _ => true,
        };
    }

    /// <summary>
    /// 検索と絞り込みを一覧に反映し、<paramref name="selectKey"/> の節を選ぶ（フォルダ・まとまりは階層で見せているときだけ）。
    /// forceReload なら選択が同じでも編集欄を読み直す。
    /// </summary>
    private void ApplyFilter(string? selectKey, bool forceReload)
    {
        IsRebuilding = true;
        try
        {
            Items.Clear();
            foreach (var item in _all.Where(Matches)) Items.Add(item);
        }
        finally
        {
            IsRebuilding = false;
        }
        IsTreeMode = SearchText.Length == 0 && FilterIndex == 0;

        // フォルダ・まとまり（階層でだけ選べる。フォント設定が 1 件も無く、フォルダだけあるときもフォルダを選ぶ）
        var group = IsTreeMode && selectKey is not null && FindTreeItem(selectKey) is { IsGroup: true } g ? g : null;
        if (group is null && IsTreeMode && Items.Count == 0) group = _treeRoots.FirstOrDefault(r => r.IsGroup);
        if (group is not null)
        {
            if (!ReferenceEquals(group, SelectedFolder))
            {
                SelectedFolder = group;
            }
            else
            {
                OnPropertyChanged(nameof(SelectedFolder));
                LoadFolderPanel();
            }
            UpdateListSummary();
            UpdateSelectionState();
            return;
        }

        var target = selectKey is null ? null : Items.FirstOrDefault(i => string.Equals(i.Id, selectKey, StringComparison.OrdinalIgnoreCase));
        target ??= Items.FirstOrDefault();
        if (!ReferenceEquals(target, SelectedItem))
        {
            SelectedItem = target; // 編集欄を読み直す
        }
        else
        {
            OnPropertyChanged(nameof(SelectedItem)); // 作り直しで外れた ListView の選択を戻す
            if (forceReload) LoadEditor();
        }
        if (target is null) SelectedFolder = null;
        UpdateListSummary();
        UpdateSelectionState();
    }

    partial void OnSelectedItemChanged(FontListItem? value)
    {
        _coalesceKey = null; // 別のフォント設定の同じ欄の変更を、前のフォント設定の変更とまとめない
        if (value is not null && SelectedFolder is not null) SelectedFolder = null;
        LoadEditor();
        if (!IsRebuilding) RunAnalysis(); // 選択中のフォント設定の使用状況
        UpdateSelectionState();
    }

    partial void OnSelectedFolderChanged(FontTreeItem? value)
    {
        if (value is not null && SelectedItem is not null) SelectedItem = null; // 編集欄を空にする
        LoadFolderPanel();
        UpdateSelectionState();
    }

    /// <summary>選択の種類に合わせて、中央の欄の切り替えとボタンの状態を更新する。</summary>
    private void UpdateSelectionState()
    {
        HasFolder = SelectedFolder is not null;
        HasNoSelection = SelectedItem is null && SelectedFolder is null;
        UpdateCommandState();
    }

    private void LoadEditor()
    {
        Editor.Load(SelectedItem);
        RaisePreview();
    }

    /// <summary>プレビューへ今の選択を知らせる。</summary>
    public void RaisePreview() => PreviewTargetChanged?.Invoke(this, SelectedFont);

    private void UpdateListSummary()
    {
        int common = _all.Count(i => !i.IsSong);
        int song = _all.Count - common;
        int folders = N3FontTree.Walk(Tree).Count(x => x.Node.IsFolder);
        string folderText = folders > 0 ? $"（フォルダ {folders}）" : "";
        string shown = IsTreeMode ? "" : $"（表示 {Items.Count} 件）";
        ListSummary = $"アプリ共通 {common} 件{folderText}・この曲専用 {song} 件{shown}";
    }

    /// <summary>Id で一覧の行を探す（絞り込みで隠れているものも含む）。</summary>
    public FontListItem? FindItem(string id) =>
        _all.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// フォント設定・フォルダを選ぶ。検索・絞り込みで隠れていれば（フォルダは階層で見せていなければ）、
    /// 検索と絞り込みを解除してから選ぶ。見つからなければ false。
    /// </summary>
    public bool Select(string key)
    {
        var item = FindItem(key);
        var group = item is null && FindTreeItem(key) is { IsGroup: true } g ? g : null;
        if (item is null && group is null) return false;
        bool hidden = item is not null ? !Items.Contains(item) : !IsTreeMode;
        if (hidden)
        {
            IsRebuilding = true;
            try
            {
                SearchText = "";
                FilterIndex = 0;
            }
            finally
            {
                IsRebuilding = false;
            }
            ApplyFilter(key, forceReload: false);
        }
        if (item is not null)
        {
            SelectedItem = item;
        }
        else
        {
            SelectedFolder = group;
        }
        return true;
    }

    /// <summary>名前でフォント設定を選ぶ（書き出しで使われるもの＝曲専用を優先）。見つからなければ false。</summary>
    public bool SelectByName(string name)
    {
        var font = _main.ExportFontSets.FirstOrDefault(f => f.Name == name);
        return font is not null && Select(font.Id);
    }

    private List<N3FontSet> ListOf(FontListItem item) => item.IsSong ? Song : Common;

    private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.SongFontSets)) return;

        // 別の曲に替わった: 曲専用の一覧が入れ替わるので、元に戻す の履歴（前の曲の一覧を含む）は捨てる
        _undo.Clear();
        _redo.Clear();
        _coalesceKey = null;
        UpdateUndoState();

        // 一覧の作り直しと使用状況の計算は、ファイルを開く処理（タブ・行の指定の復元）が終わってから行う
        _lineSelection = Array.Empty<int>();
        if (!_rebuildQueued)
        {
            _rebuildQueued = true;
            DispatcherQueue.GetForCurrentThread().TryEnqueue(() =>
            {
                _rebuildQueued = false;
                Rebuild(null);
            });
        }
    }

    // ------------------------------------------------------------ 編集

    /// <summary>
    /// フォント設定の内容を変える。元に戻す の記録（key が同じ連続した変更は 1 回にまとめる）→ 変更 →
    /// 編集済みの印（ニコカラメーカー3 のテンプレート連動を書き出しで外す）→ 保存の予約 → 表示とプレビューの更新。
    /// key が null の変更（一括の操作など）は、まとめずに必ず 1 回分として記録する。
    /// </summary>
    public void EditFont(N3FontSet font, string? key, Action<N3FontSet> change)
    {
        PushUndo(key is null ? null : $"{font.Id}:{key}"); // まとめるのは同じフォント設定の同じ欄だけ
        change(font);
        N3FontLibrary.MarkEdited(font);
        OnFontContentChanged(font);
    }

    private void OnFontContentChanged(N3FontSet font)
    {
        var item = _all.FirstOrDefault(i => ReferenceEquals(i.Font, font));
        item?.Refresh();
        MarkDirty(song: item?.IsSong ?? Song.Contains(font));
        ScheduleAnalysis();
        if (ReferenceEquals(SelectedFont, font))
        {
            Editor.RefreshState();
            RaisePreview();
        }
    }

    /// <summary>選択中のフォント設定の名前を変える（確定したとき）。行の手動指定の参照も新しい名前にする。</summary>
    public void RenameSelected(string newName)
    {
        if (SelectedItem is not { } item) return;
        if (newName == item.Font.Name)
        {
            Editor.RefreshState();
            return;
        }

        var entry = PushUndo(null);
        string? old = N3FontLibrary.Rename(ListOf(item), item.Id, newName);
        if (old is null || old == item.Font.Name)
        {
            DropLastUndo(entry);
            Editor.RefreshState();
            return;
        }
        string actual = item.Font.Name;

        // 同じ名前のフォント設定が書き出しにまだ残っていれば（曲専用と共通の両方にあったなど）、行はそちらを使い続ける
        int lines = 0;
        if (!_main.ExportFontSets.Any(f => f.Name == old))
        {
            lines = _main.RenameFontReferences(old, actual);
            entry.Renames.Add((old, actual));
        }
        MarkDirty(song: item.IsSong);
        RefreshAfterRename(item);

        string unique = actual != newName ? $"（「{newName}」は使われているため「{actual}」にしました）" : "";
        string refs = lines > 0 ? $"。{lines} 行のフォント指定も新しい名前にしました" : "";
        SetStatus($"フォント設定の名前を「{old}」から「{actual}」に変えました{unique}{refs}");
    }

    /// <summary>
    /// フォント設定を追加する。曲専用（絞り込みが「この曲専用」・曲専用のフォント設定かまとまりを選んでいる）なら曲専用の選択の直後、
    /// それ以外はアプリ共通の階層の、選んでいるフォルダの中の末尾・選んでいるフォント設定のすぐ後・最上位の末尾のどれか。
    /// </summary>
    public void AddNew()
    {
        bool song = FilterIndex == 2 || (SelectedItem?.IsSong ?? false) || (SelectedFolder?.IsSongGroup ?? false);
        PushUndo(null);
        var font = new N3FontSet { Name = N3FontLibrary.NewFontName };
        string where;
        if (song)
        {
            int? at = SelectedItem is { IsSong: true } sel ? Song.IndexOf(sel.Font) + 1 : null;
            N3FontLibrary.Add(Song, font, at);
            where = "この曲専用";
        }
        else
        {
            N3FontLibrary.Add(Common, font);
            where = "アプリ共通の" + PlaceNewCommonNode(N3FontTreeNode.ForFont(font.Id));
        }
        N3FontLibrary.MarkEdited(font); // 新規は全項目を NicoKaraPrep 側で決めたフォントとして扱う
        MarkDirty(song);
        ClearFilterFor(font.Id);
        SetStatus($"フォント設定「{font.Name}」を{where}に追加しました");
    }

    /// <summary>選択中のフォント設定を複製して直後（アプリ共通は階層の同じ親の下のすぐ後）に入れる。</summary>
    public void DuplicateSelected()
    {
        if (SelectedItem is not { } item) return;
        var entry = PushUndo(null);
        var copy = N3FontLibrary.Duplicate(ListOf(item), item.Id);
        if (copy is null)
        {
            DropLastUndo(entry);
            return;
        }
        if (!item.IsSong) N3FontTree.InsertAfter(Tree, item.Id, N3FontTreeNode.ForFont(copy.Id));
        MarkDirty(item.IsSong);
        ClearFilterFor(copy.Id);
        SetStatus($"フォント設定「{item.Font.Name}」を複製して「{copy.Name}」を作りました");
    }

    /// <summary>
    /// 選択中のフォント設定を消すと、書き出しでこの名前が使えなくなるときに、その名前を使っている行の数。
    /// 同じ名前のフォント設定がほかにもあるとき（曲専用と共通の両方など）は 0。
    /// </summary>
    public int CountLinesLosingFont(FontListItem item)
    {
        var export = _main.ExportFontSets;
        if (!export.Contains(item.Font)) return 0;
        var rest = N3FontLibrary.ResolveForExport(
            Common.Where(f => !ReferenceEquals(f, item.Font)).ToList(),
            Song.Where(f => !ReferenceEquals(f, item.Font)).ToList());
        if (rest.Any(f => f.Name == item.Font.Name)) return 0;
        return LinesUsingFont(item.Font.Name).Count;
    }

    /// <summary>
    /// フォント設定を削除する。アプリ共通の階層でこのフォント設定の下にあった節は、その位置へ繰り上げる。
    /// </summary>
    public void Delete(FontListItem item)
    {
        var list = ListOf(item);
        int index = _all.IndexOf(item);
        var loc = item.IsSong || !IsTreeMode ? null : N3FontTree.Find(Tree, item.Id);
        int children = item.IsSong ? 0 : N3FontTree.Find(Tree, item.Id)?.Node.Children.Count ?? 0;

        // 選び直す先: 階層で見せていれば同じ場所の次の節（下の節を繰り上げるならその先頭）・前の節・親、それ以外は一覧の次の行（無ければ前の行）
        string? next = loc is not null
            ? NextKeyAfterRemoving(loc, keepChildren: true)
            : (_all.Skip(index + 1).FirstOrDefault(i => Items.Contains(i)) ?? _all.Take(index).LastOrDefault(i => Items.Contains(i)))?.Id;

        var entry = PushUndo(null);
        if (!N3FontLibrary.Remove(list, item.Id))
        {
            DropLastUndo(entry);
            return;
        }
        if (!item.IsSong) N3FontTree.Remove(Tree, item.Id, keepChildren: true);
        MarkDirty(item.IsSong);
        Rebuild(next);
        string moved = children > 0 ? $"下にあった {children} 件は 1 つ上の階層へ移しました。" : "";
        SetStatus($"フォント設定「{item.Font.Name}」を削除しました（{moved}Ctrl+Z で元に戻せます）");
    }

    /// <summary>
    /// 選択中の節を上下に動かす。曲専用のフォント設定は曲専用の中で、アプリ共通のフォント設定・フォルダは階層の同じ親の下で
    /// （別の親へは移らない。階層を変えるのは ←・→・移動...・ドラッグ）。
    /// </summary>
    public void MoveSelected(int delta)
    {
        if (SelectedItem is { IsSong: true } item)
        {
            int i = Song.IndexOf(item.Font);
            int to = i + delta;
            if (i < 0 || to < 0 || to >= Song.Count) return;
            PushUndo(null);
            N3FontLibrary.Move(Song, item.Id, to);
            MarkDirty(song: true);
            Rebuild(item.Id);
            return;
        }
        if (CommonSelectionKey() is not { } key) return;
        var entry = PushUndo(null);
        if (!N3FontTree.MoveWithinSiblings(Tree, key, delta))
        {
            DropLastUndo(entry);
            return;
        }
        AfterTreeChanged(key);
    }

    /// <summary>追加・複製したフォント設定（作ったフォルダ）が見えるよう、検索と絞り込みを必要なら解除して選ぶ。</summary>
    private void ClearFilterFor(string key)
    {
        Rebuild(key);
        if (!string.Equals(SelectedKey, key, StringComparison.OrdinalIgnoreCase)) Select(key);
    }

    // ------------------------------------------------------------ ビューの出入り

    /// <summary>ビューを抜けたときの一覧の内容（戻ってきたときに、外で変わっていないかを調べる）。</summary>
    private string? _exitSignature;

    /// <summary>ビューを抜けるとき（保存のあと）。</summary>
    public void OnExit() => _exitSignature = Signature();

    /// <summary>
    /// ビューに入るとき。一覧を作り直す（外で n3proj を読み込んだ・行の指定が変わったなど）。
    /// 抜けている間にフォント設定が変わっていたら、元に戻す の履歴は捨てる（戻すと外での変更まで消えるため）。
    /// </summary>
    public void OnEnter()
    {
        N3FontLibrary.EnsureIds(Common);
        N3FontLibrary.EnsureIds(Song);
        if (_exitSignature is not null && _exitSignature != Signature())
        {
            _undo.Clear();
            _redo.Clear();
            UpdateUndoState();
        }
        _exitSignature = null;
        _coalesceKey = null;
        Rebuild(null);
    }

    private string Signature() => System.Text.Json.JsonSerializer.Serialize(new object[] { Common, Song, Tree });

    // ------------------------------------------------------------ 保存

    /// <summary>保存を予約する（300ms 以内の変更はまとめて保存する）。</summary>
    private void MarkDirty(bool song)
    {
        if (song) _songDirty = true;
        else _commonDirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>予約中の保存をすぐに行う（ビューを抜けるとき・文書を入れ替える前）。</summary>
    public void FlushPendingSave()
    {
        _saveTimer.Stop();
        try
        {
            if (_commonDirty)
            {
                _commonDirty = false;
                _main.Settings.Save();
            }
            if (_songDirty)
            {
                _songDirty = false;
                _main.SaveProject();
            }
        }
        catch (Exception ex)
        {
            SetStatus($"エラー: フォント設定を保存できませんでした（{ex.Message}）");
        }
    }

    // ------------------------------------------------------------ 元に戻す・やり直し

    /// <summary>元に戻す の記録 1 回分（その操作の前の一覧・階層と、行の参照を変えた名前の組）。</summary>
    private sealed class UndoEntry
    {
        public required List<N3FontSet> Common { get; init; }

        public required List<N3FontSet> Song { get; init; }

        public required List<N3FontTreeNode> Tree { get; init; }

        /// <summary>この操作で行の手動指定の参照を変えた名前（変更前, 変更後）。</summary>
        public List<(string Old, string New)> Renames { get; } = new();
    }

    private UndoEntry Snapshot() => new()
    {
        Common = Common.Select(f => f.Clone()).ToList(),
        Song = Song.Select(f => f.Clone()).ToList(),
        Tree = N3FontTree.Clone(Tree),
    };

    /// <summary>
    /// 変更の前に今の一覧を記録する。key が前回と同じで間隔が短ければ（同じ欄の連続した変更）、前回の記録にまとめる。
    /// key が null の操作（追加・削除など）は必ず 1 回分として記録する。
    /// </summary>
    private UndoEntry PushUndo(string? key)
    {
        var now = DateTime.UtcNow;
        if (key is not null && key == _coalesceKey && now - _lastEditUtc < CoalesceWindow && _undo.Count > 0)
        {
            _lastEditUtc = now;
            return _undo[^1];
        }

        var entry = Snapshot();
        _undo.Add(entry);
        if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
        _redo.Clear();
        _coalesceKey = key;
        _lastEditUtc = now;
        UpdateUndoState();
        return entry;
    }

    /// <summary>何も変わらなかった操作の記録を取り消す。</summary>
    private void DropLastUndo(UndoEntry entry)
    {
        if (_undo.Count > 0 && ReferenceEquals(_undo[^1], entry)) _undo.RemoveAt(_undo.Count - 1);
        _coalesceKey = null;
        UpdateUndoState();
    }

    private void UpdateUndoState()
    {
        CanUndo = _undo.Count > 0;
        CanRedo = _redo.Count > 0;
    }

    /// <summary>フォント設定ビューの中の操作を 1 つ戻す。</summary>
    public bool Undo()
    {
        if (_undo.Count == 0)
        {
            SetStatus("フォント設定で元に戻す操作はありません");
            return false;
        }
        var entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        var current = Snapshot();
        current.Renames.AddRange(entry.Renames);
        _redo.Add(current);
        Restore(entry, undo: true);
        SetStatus("フォント設定の変更を元に戻しました（Ctrl+Y でやり直し）");
        return true;
    }

    /// <summary>元に戻した操作をやり直す。</summary>
    public bool Redo()
    {
        if (_redo.Count == 0)
        {
            SetStatus("フォント設定でやり直す操作はありません");
            return false;
        }
        var entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        var current = Snapshot();
        current.Renames.AddRange(entry.Renames);
        _undo.Add(current);
        Restore(entry, undo: false);
        SetStatus("フォント設定の変更をやり直しました");
        return true;
    }

    /// <summary>
    /// 記録した一覧と階層に戻す（入れ物はそのまま、中身を入れ替える）。行の参照の名前も戻す・やり直す。
    /// 折りたたみは元に戻す の対象にしないので、今もある節は今の開閉のままにする。
    /// </summary>
    private void Restore(UndoEntry entry, bool undo)
    {
        var collapsed = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var (node, _) in N3FontTree.Walk(Tree)) collapsed.TryAdd(node.Key, node.Collapsed);

        Common.Clear();
        Common.AddRange(entry.Common);
        Song.Clear();
        Song.AddRange(entry.Song);
        Tree.Clear();
        Tree.AddRange(entry.Tree);
        foreach (var (node, _) in N3FontTree.Walk(Tree))
        {
            if (collapsed.TryGetValue(node.Key, out bool c)) node.Collapsed = c;
        }

        var renames = undo ? entry.Renames.AsEnumerable().Reverse().Select(r => (From: r.New, To: r.Old)) : entry.Renames.Select(r => (From: r.Old, To: r.New));
        foreach (var (from, to) in renames) _main.RenameFontReferences(from, to);

        _coalesceKey = null;
        UpdateUndoState();
        MarkDirty(song: false);
        MarkDirty(song: true);
        Rebuild(null);
    }
}
