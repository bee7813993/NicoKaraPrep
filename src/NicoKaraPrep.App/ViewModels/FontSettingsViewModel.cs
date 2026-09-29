using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.ViewModels;

/// <summary>
/// フォント設定ビュー（F3）の状態。一覧（アプリ共通 → 曲専用）・選択中のフォント設定の編集・保存・元に戻す を受け持つ。
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
        _main.PropertyChanged += OnMainPropertyChanged;
        Rebuild(null);
    }

    /// <summary>メイン画面の ViewModel（行・タブ・設定）。</summary>
    public MainViewModel Main => _main;

    /// <summary>選択中のフォント設定の編集欄。</summary>
    public FontEditorViewModel Editor { get; }

    /// <summary>一覧のすべての行（アプリ共通 → 曲専用。検索と絞り込みに関係なく）。</summary>
    public IReadOnlyList<FontListItem> AllItems => _all;

    /// <summary>一覧に表示している行（検索と絞り込みを通ったもの）。</summary>
    public ObservableCollection<FontListItem> Items { get; } = new();

    /// <summary>絞り込みの選択肢（FilterIndex の順）。</summary>
    public IReadOnlyList<string> FilterChoices { get; } = new[] { "すべて", "アプリ共通", "この曲専用", "未使用", "NKM3 連動" };

    [ObservableProperty]
    private string searchText = "";

    /// <summary>絞り込み（0 すべて / 1 アプリ共通 / 2 この曲専用 / 3 未使用 / 4 NKM3 連動）。</summary>
    [ObservableProperty]
    private int filterIndex;

    [ObservableProperty]
    private FontListItem? selectedItem;

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

    private List<N3FontSet> Common => _main.Settings.N3FontSets;

    private List<N3FontSet> Song => _main.SongFontSets;

    private void SetStatus(string text) => _main.StatusText = text;

    // ------------------------------------------------------------ 一覧

    /// <summary>
    /// 一覧をフォント設定から作り直し、<paramref name="selectId"/>（null なら今の選択）のフォント設定を選ぶ。
    /// 見つからなければ、表示している先頭を選ぶ。
    /// </summary>
    public void Rebuild(string? selectId)
    {
        selectId ??= SelectedItem?.Id;
        _all.Clear();
        foreach (var f in Common) _all.Add(new FontListItem(f, isSong: false));
        foreach (var f in Song) _all.Add(new FontListItem(f, isSong: true));
        UpdateItemUsage();
        ApplyFilter(selectId, forceReload: true);
        RunAnalysis();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter(SelectedItem?.Id, forceReload: false);

    partial void OnFilterIndexChanged(int value) => ApplyFilter(SelectedItem?.Id, forceReload: false);

    private bool Matches(FontListItem item)
    {
        if (SearchText.Length > 0 && !item.Font.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)) return false;
        return FilterIndex switch
        {
            1 => !item.IsSong,
            2 => item.IsSong,
            3 => item.Usage == 0,
            4 => item.IsLinked,
            _ => true,
        };
    }

    /// <summary>検索と絞り込みを一覧に反映する。forceReload なら選択が同じでも編集欄を読み直す。</summary>
    private void ApplyFilter(string? selectId, bool forceReload)
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

        var target = selectId is null ? null : Items.FirstOrDefault(i => string.Equals(i.Id, selectId, StringComparison.OrdinalIgnoreCase));
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
        UpdateListSummary();
    }

    partial void OnSelectedItemChanged(FontListItem? value)
    {
        LoadEditor();
        if (!IsRebuilding) RunAnalysis(); // 選択中のフォント設定の使用状況
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
        string shown = Items.Count == _all.Count ? "" : $"（表示 {Items.Count} 件）";
        ListSummary = $"アプリ共通 {common} 件・この曲専用 {song} 件{shown}";
    }

    /// <summary>Id で一覧の行を探す（絞り込みで隠れているものも含む）。</summary>
    public FontListItem? FindItem(string id) =>
        _all.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// フォント設定を選ぶ。絞り込みで隠れていれば、検索と絞り込みを解除してから選ぶ。見つからなければ false。
    /// </summary>
    public bool Select(string id)
    {
        var item = FindItem(id);
        if (item is null) return false;
        if (!Items.Contains(item))
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
            ApplyFilter(id, forceReload: false);
        }
        SelectedItem = item;
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
        Rebuild(null);
    }

    // ------------------------------------------------------------ 編集

    /// <summary>
    /// フォント設定の内容を変える。元に戻す の記録（key が同じ連続した変更は 1 回にまとめる）→ 変更 →
    /// 編集済みの印（ニコカラメーカー3 のテンプレート連動を書き出しで外す）→ 保存の予約 → 表示とプレビューの更新。
    /// key が null の変更（一括の操作など）は、まとめずに必ず 1 回分として記録する。
    /// </summary>
    public void EditFont(N3FontSet font, string? key, Action<N3FontSet> change)
    {
        PushUndo(key);
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
        Rebuild(item.Id);

        string unique = actual != newName ? $"（「{newName}」は使われているため「{actual}」にしました）" : "";
        string refs = lines > 0 ? $"。{lines} 行のフォント指定も新しい名前にしました" : "";
        SetStatus($"フォント設定の名前を「{old}」から「{actual}」に変えました{unique}{refs}");
    }

    /// <summary>フォント設定を追加する（絞り込みが「この曲専用」なら曲専用、それ以外はアプリ共通）。</summary>
    public void AddNew()
    {
        bool song = FilterIndex == 2 || (SelectedItem?.IsSong ?? false);
        var list = song ? Song : Common;
        PushUndo(null);
        var font = new N3FontSet { Name = N3FontLibrary.NewFontName };
        int? at = SelectedItem is { } sel && sel.IsSong == song ? list.IndexOf(sel.Font) + 1 : null;
        N3FontLibrary.Add(list, font, at);
        N3FontLibrary.MarkEdited(font); // 新規は全項目を NicoKaraPrep 側で決めたフォントとして扱う
        MarkDirty(song);
        ClearFilterFor(font.Id);
        SetStatus($"フォント設定「{font.Name}」を{(song ? "この曲専用" : "アプリ共通")}に追加しました");
    }

    /// <summary>選択中のフォント設定を複製して直後に入れる。</summary>
    public void DuplicateSelected()
    {
        if (SelectedItem is not { } item) return;
        PushUndo(null);
        var copy = N3FontLibrary.Duplicate(ListOf(item), item.Id);
        if (copy is null) return;
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

    /// <summary>フォント設定を削除する。</summary>
    public void Delete(FontListItem item)
    {
        var list = ListOf(item);
        int index = _all.IndexOf(item);
        PushUndo(null);
        if (!N3FontLibrary.Remove(list, item.Id)) return;
        MarkDirty(item.IsSong);

        // 削除した次の行（無ければ前の行）を選ぶ
        var next = _all.Skip(index + 1).FirstOrDefault(i => Items.Contains(i)) ?? _all.Take(index).LastOrDefault(i => Items.Contains(i));
        Rebuild(next?.Id);
        SetStatus($"フォント設定「{item.Font.Name}」を削除しました（Ctrl+Z で元に戻せます）");
    }

    /// <summary>選択中のフォント設定を、同じ範囲（アプリ共通／曲専用）の中で上下に動かす。</summary>
    public void MoveSelected(int delta)
    {
        if (SelectedItem is not { } item) return;
        var list = ListOf(item);
        int i = list.IndexOf(item.Font);
        int to = i + delta;
        if (i < 0 || to < 0 || to >= list.Count) return;
        PushUndo(null);
        N3FontLibrary.Move(list, item.Id, to);
        MarkDirty(item.IsSong);
        Rebuild(item.Id);
    }

    /// <summary>追加・複製したフォント設定が見えるよう、検索と絞り込みを必要なら解除して選ぶ。</summary>
    private void ClearFilterFor(string id)
    {
        Rebuild(id);
        if (SelectedItem?.Id != id) Select(id);
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

    private string Signature() => System.Text.Json.JsonSerializer.Serialize(new[] { Common, Song });

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

    /// <summary>元に戻す の記録 1 回分（その操作の前の一覧と、行の参照を変えた名前の組）。</summary>
    private sealed class UndoEntry
    {
        public required List<N3FontSet> Common { get; init; }

        public required List<N3FontSet> Song { get; init; }

        /// <summary>この操作で行の手動指定の参照を変えた名前（変更前, 変更後）。</summary>
        public List<(string Old, string New)> Renames { get; } = new();
    }

    private UndoEntry Snapshot() => new()
    {
        Common = Common.Select(f => f.Clone()).ToList(),
        Song = Song.Select(f => f.Clone()).ToList(),
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

    /// <summary>記録した一覧に戻す（一覧の入れ物はそのまま、中身を入れ替える）。行の参照の名前も戻す・やり直す。</summary>
    private void Restore(UndoEntry entry, bool undo)
    {
        Common.Clear();
        Common.AddRange(entry.Common);
        Song.Clear();
        Song.AddRange(entry.Song);

        var renames = undo ? entry.Renames.AsEnumerable().Reverse().Select(r => (From: r.New, To: r.Old)) : entry.Renames.Select(r => (From: r.Old, To: r.New));
        foreach (var (from, to) in renames) _main.RenameFontReferences(from, to);

        _coalesceKey = null;
        UpdateUndoState();
        MarkDirty(song: false);
        MarkDirty(song: true);
        Rebuild(null);
    }
}
