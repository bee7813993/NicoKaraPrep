using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.ViewModels;

/// <summary>
/// レイアウト設定ビュー（F4）の状態。左 = レイアウト設定の一覧（書き出しの並び）、中央 = 選んだレイアウトの編集と曲の既定の字幕アクション、
/// 右 = 見本とページの一覧（全タブ）。
/// レイアウトの値はアプリ共通（<see cref="Core.Project.AppSettings.N3Layouts"/>）で、変えると 300ms ごとにまとめて保存する
/// （<see cref="MainViewModel.SaveLayouts"/>。行リスト・字幕のプレビューの作り直しはメイン画面が受け持つ）。
/// レイアウトの値・追加・削除・名前の変更・取り込みは、このビューの 元に戻す（<see cref="N3LayoutHistory"/>）で戻す。
/// ページへの指定（レイアウト・字幕アクション）は歌詞の変更なので、行リストへ戻って Ctrl+Z で戻す。曲の既定の字幕アクションは 元に戻す の対象にしない。
/// </summary>
public sealed partial class LayoutViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly DispatcherQueueTimer _saveTimer;
    private readonly N3LayoutHistory _history = new();

    /// <summary>レイアウトの値を保存していない（保存の予約中）。</summary>
    private bool _layoutsDirty;

    /// <summary>一覧の幅など、レイアウト以外の設定を保存していない。</summary>
    private bool _settingsDirty;

    /// <summary>
    /// メイン画面のレイアウトの操作（保存・追加・削除・名前の変更）を呼んでいる最中の深さ。そのあいだに届く
    /// <see cref="MainViewModel.LayoutsChanged"/> は自分の変更なので、一覧を作り直さない（呼んだ側で作り直す）。
    /// </summary>
    private int _ownChange;

    /// <summary>ビューを出しているか（隠れているあいだはメイン画面の知らせで作り直さず、入るときにまとめて作り直す）。</summary>
    private bool _active;

    /// <summary>ビューを抜けたときのレイアウトの一覧の署名（戻ってきたときに、外で変わっていないかを調べる）。</summary>
    private string? _exitSignature;

    public LayoutViewModel(MainViewModel main)
    {
        _main = main;

        var queue = DispatcherQueue.GetForCurrentThread();
        _saveTimer = queue.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(300);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += (_, _) => FlushPendingSave();

        if (_main.Settings.LoadFailed) SaveNote = NotSavedNote;
        _main.DocumentReplacing += (_, _) => FlushPendingSave();
        _main.LayoutsChanged += OnMainLayoutsChanged;
        _main.SongDefaultSubtitleActionChanged += (_, _) => OnSongDefaultActionChanged();
        _main.PreviewModelChanged += (_, _) =>
        {
            if (_active) PreviewChanged?.Invoke(this, EventArgs.Empty);
        };
        PageActionChoices = new[] { new LayoutActionChoice(null, "（既定）") }
            .Concat(N3SubtitleActionCatalog.Known.Select(k => new LayoutActionChoice(k.Id, k.Name)))
            .ToList();
        Refresh(null);
        RefreshSongAction(rebuildFields: true);
    }

    /// <summary>メイン画面の ViewModel（行・タブ・設定）。</summary>
    public MainViewModel Main => _main;

    /// <summary>NicoKaraPrep のレイアウト設定（アプリ共通の一覧そのもの）。</summary>
    private List<N3Layout> Own => _main.Settings.N3Layouts;

    private void SetStatus(string text) => _main.StatusText = text;

    // ------------------------------------------------------------ ビューへの知らせ

    /// <summary>選んだレイアウトが変わった・値が外から変わった（元に戻すなど）ので、中央の編集欄を読み直す。</summary>
    public event EventHandler? EditorReloadRequested;

    /// <summary>見本を描き直す（選んだレイアウト・ページ・値・歌詞が変わった）。</summary>
    public event EventHandler? PreviewChanged;

    /// <summary>ページの一覧でこれらのページを選ぶ（一覧を作り直した・行リストで選んでいた行のページ・「使っているページ」を押した）。</summary>
    public event EventHandler<IReadOnlyList<LayoutPageItem>>? PagesSelectRequested;

    // ------------------------------------------------------------ 一覧

    /// <summary>左の一覧（書き出しの並び。<see cref="N3LayoutLibrary.Effective"/> の順）。</summary>
    public ObservableCollection<LayoutListItem> Layouts { get; } = new();

    /// <summary>一覧を作り直している最中か（ListView の選択の変化を選択の操作として扱わない）。</summary>
    public bool IsRebuilding { get; private set; }

    [ObservableProperty]
    private LayoutListItem? selectedLayout;

    /// <summary>レイアウトを選んでいるか。</summary>
    [ObservableProperty]
    private bool hasSelection;

    /// <summary>何も選んでいないか（中央に案内を出す）。</summary>
    [ObservableProperty]
    private bool hasNoSelection = true;

    /// <summary>選んだレイアウトがベースの値のままか（編集欄を薄く出す）。</summary>
    [ObservableProperty]
    private bool isBaseSelected;

    /// <summary>名前を変えられるか（NicoKaraPrep で足したものだけ）。</summary>
    [ObservableProperty]
    private bool canRename;

    [ObservableProperty]
    private bool canDelete;

    /// <summary>削除のボタンの文字（足したものは「削除」、編集したベースのものは「ベースの値に戻す」）。</summary>
    [ObservableProperty]
    private string deleteText = "削除";

    [ObservableProperty]
    private string deleteToolTip = "";

    /// <summary>選んだレイアウトの出どころの説明。</summary>
    [ObservableProperty]
    private string stateNote = "";

    /// <summary>名前の欄の説明（ツールチップ）。</summary>
    [ObservableProperty]
    private string nameToolTip = "";

    [ObservableProperty]
    private string listSummary = "";

    /// <summary>この曲が、編集したレイアウト設定をベースへ反映しない設定か（中央の上に注意を出す）。</summary>
    [ObservableProperty]
    private bool isMergeOff;

    [ObservableProperty]
    private bool canUndo;

    [ObservableProperty]
    private bool canRedo;

    /// <summary>選んだレイアウトの編集画面の値（NicoKaraPrep の値があればその値、無ければ書き出しの並びの値。未選択なら null）。</summary>
    public N3LayoutSettings? CurrentSettings => SelectedLayout?.Entry.EditingSettings;

    /// <summary>選んだレイアウトの名前。</summary>
    public string? SelectedName => SelectedLayout?.Name;

    partial void OnSelectedLayoutChanged(LayoutListItem? value)
    {
        _history.BreakCoalescing();
        UpdateSelectionState();
        UpdateUsage();
        if (IsRebuilding) return; // 作り直しの終わりにまとめて知らせる
        EditorReloadRequested?.Invoke(this, EventArgs.Empty);
        PreviewChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>名前でレイアウトを選ぶ（無ければ今の選択のまま）。選べたら true。</summary>
    public bool SelectByName(string? name)
    {
        if (name is null || Layouts.FirstOrDefault(l => l.Name == name) is not { } item) return false;
        SelectedLayout = item;
        return true;
    }

    /// <summary>
    /// 一覧とページの一覧を今のレイアウト設定・歌詞から作り直す。並びの名前が同じなら行を使い回し（選択・スクロールを保つ）、違えば作り直して
    /// <paramref name="selectName"/>（null なら今の選択の名前。無ければ同じ位置の行）を選ぶ。
    /// </summary>
    private void Refresh(string? selectName)
    {
        bool merge = _main.N3ProjSettings.MergeLayouts;
        IsMergeOff = !merge;
        var pages = _main.GetLayoutPages();
        // 一覧は、ベースへ反映しない曲でも NicoKaraPrep のレイアウト（足したもの・編集したもの）を並べる（アプリ共通なので、ここで
        // 名前の変更・削除ができるように。反映しない曲では、この曲の書き出しに使われないことを一覧の説明と各行のツールチップで知らせる）
        var entries = N3LayoutLibrary.Entries(_main.GetBaseLayouts(), Own, merge: true);
        var counts = pages.GroupBy(p => p.LayoutName).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        string? keep = selectName ?? SelectedLayout?.Name;
        int keepIndex = SelectedLayout is { } current ? Layouts.IndexOf(current) : -1;
        if (Layouts.Select(l => l.Name).SequenceEqual(entries.Select(e => e.Name)))
        {
            for (int i = 0; i < entries.Count; i++) Layouts[i].Update(entries[i], counts.GetValueOrDefault(entries[i].Name), merge);
            if (selectName is not null) SelectByName(selectName);
        }
        else
        {
            IsRebuilding = true;
            try
            {
                Layouts.Clear();
                foreach (var e in entries) Layouts.Add(new LayoutListItem(e, counts.GetValueOrDefault(e.Name), merge));
                SelectedLayout = Layouts.FirstOrDefault(l => l.Name == keep)
                    ?? (keepIndex >= 0 && Layouts.Count > 0 ? Layouts[Math.Min(keepIndex, Layouts.Count - 1)] : Layouts.FirstOrDefault());
            }
            finally
            {
                IsRebuilding = false;
            }
            EditorReloadRequested?.Invoke(this, EventArgs.Empty);
        }

        int edited = entries.Count(e => e.Origin == N3LayoutOrigin.Edited);
        int added = entries.Count(e => e.Origin == N3LayoutOrigin.Added);
        var parts = new List<string>();
        if (edited > 0) parts.Add($"編集 {edited}");
        if (added > 0) parts.Add($"新規 {added}");
        ListSummary = $"レイアウト設定 {entries.Count} 件{(parts.Count > 0 ? $"（{string.Join("・", parts)}）" : "")}。上からの並びが書き出しの並び（行数から自動で選ぶときの優先順）です" +
                      (merge || parts.Count == 0 ? "" : "（この曲はベースへ反映しない設定なので、「編集」はベースの値で、「新規」は入れずに書き出します）");

        RefreshPages(pages);
        UpdateSelectionState();
        UpdateUsage();
        PreviewChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>選んだレイアウトに合わせて、ボタン・説明の状態を作り直す。</summary>
    private void UpdateSelectionState()
    {
        var item = SelectedLayout;
        HasSelection = item is not null;
        HasNoSelection = item is null;
        IsBaseSelected = item?.Origin == N3LayoutOrigin.Base;
        CanRename = item?.Origin == N3LayoutOrigin.Added;
        CanDelete = item is not null && item.Origin != N3LayoutOrigin.Base;
        DeleteText = item?.Origin == N3LayoutOrigin.Edited ? "ベースの値に戻す" : "削除";
        DeleteToolTip = item?.Origin switch
        {
            N3LayoutOrigin.Edited => "編集した値を消して、ベースの n3proj（無ければ書き出しの既定）の値に戻します（確認します。Ctrl+Z で戻せます）",
            N3LayoutOrigin.Added => "NicoKaraPrep で足したレイアウト設定を削除します（確認します。この名前を指定したページは行数から自動で選ぶようになります。Ctrl+Z で戻せます）",
            _ => "ベースの値のままのレイアウト設定は削除できません（ベースの n3proj にあるため）",
        };
        StateNote = item is null ? "" : LayoutTexts.OriginNote(item.Origin);
        NameToolTip = item?.Origin == N3LayoutOrigin.Added
            ? "名前（Enter か欄を離れたときに変えます。このレイアウトを指定したページ・タブの指定も新しい名前にします）"
            : "ベースにある名前は変えられません（変えると別のレイアウトになるため）。名前を変えたいときは「複製」してから変えてください";
    }

    // ------------------------------------------------------------ 編集

    /// <summary>
    /// 選んだレイアウトを編集する（まだ編集していないベースのものは、今の値を写して NicoKaraPrep のレイアウトにしてから）。
    /// <paramref name="field"/> は欄の名前（同じ欄の続けての変更は 1 回の 元に戻す にまとめる）。<paramref name="separate"/> なら必ず 1 回分にする
    /// （行の追加・削除など）。値が変わらなければ何もしない。変えたら保存を予約し、一覧・ページの一覧・見本を作り直す。
    /// </summary>
    public void Edit(string field, Action<N3Layout> change, bool separate = false)
    {
        if (SelectedLayout is not { } item) return;
        string name = item.Name;
        var entry = _history.Push(Own, separate ? null : $"{name}\n{field}", DateTime.UtcNow, out bool added);
        bool created = _main.FindEditedLayout(name) is null;
        var layout = _main.EnsureEditableLayout(name);
        if (layout is null)
        {
            if (added) _history.Drop(entry);
            return;
        }
        string before = JsonSerializer.Serialize(layout);
        change(layout);
        if (JsonSerializer.Serialize(layout) == before)
        {
            if (created) Own.Remove(layout); // 何も変えていないベースのものは、写したものを消す（ベースの値のままに戻す）
            if (added) _history.Drop(entry);
            return;
        }
        UpdateUndoState();
        MarkDirty(layouts: true);
        Refresh(name);
    }

    /// <summary>「追加」: 選んでいるレイアウトの値を写して「新しいレイアウト」（重なれば 2, 3…）を足し、それを選ぶ。</summary>
    public string? AddNew()
    {
        var source = CurrentSettings;
        _history.Push(Own, null, DateTime.UtcNow, out _);
        var layout = source is null ? new N3Layout() : N3Layout.FromSettings(source);
        layout.Name = "新しいレイアウト";
        AddOwn(layout);
        string from = source is null ? "" : $"「{source.Name}」の値を写しました。";
        SetStatus($"レイアウト設定「{layout.Name}」を追加しました（{from}名前は中央の欄で変えられます。Ctrl+Z で戻せます）{MergeOffNote()}");
        return layout.Name;
    }

    /// <summary>「複製」: 選んでいるレイアウトを写して、同じ名前の末尾に 2, 3… を付けて足し、それを選ぶ。</summary>
    public void DuplicateSelected()
    {
        if (CurrentSettings is not { } source) return;
        _history.Push(Own, null, DateTime.UtcNow, out _);
        var layout = N3Layout.FromSettings(source);
        AddOwn(layout);
        SetStatus($"レイアウト設定「{source.Name}」を複製して「{layout.Name}」を作りました（Ctrl+Z で戻せます）{MergeOffNote()}");
    }

    private void AddOwn(N3Layout layout)
    {
        AsOwnChange(() => _main.AddLayout(layout));
        UpdateUndoState();
        Refresh(layout.Name);
    }

    /// <summary>編集したレイアウト設定をベースへ反映しない曲のときに、ステータスバーの知らせに添える注意。</summary>
    private string MergeOffNote() => _main.N3ProjSettings.MergeLayouts
        ? ""
        : "　⚠ この曲は、編集したレイアウト設定をベースへ反映しない設定です（n3proj の書き出し画面のチェック）";

    /// <summary>削除の確認の画面の題と本文、ボタンの文字（削除できなければ null）。</summary>
    public (string Title, string Message, string Primary)? DescribeDelete()
    {
        if (SelectedLayout is not { } item || item.Origin == N3LayoutOrigin.Base) return null;
        if (item.Origin == N3LayoutOrigin.Edited)
        {
            return ($"レイアウト設定「{item.Name}」をベースの値に戻しますか？",
                "編集した値を消して、ベースの n3proj（無ければ書き出しの既定）の値に戻します。\n（このビューの中で Ctrl+Z を押すと、編集した値に戻せます）",
                "ベースの値に戻す");
        }
        string used = item.PageCount > 0 ? $"このレイアウトを使っている {item.PageCount} ページは、ページの行数から自動で選ぶようになります（指定した名前は行に残ります）。\n" : "";
        return ($"レイアウト設定「{item.Name}」を削除しますか？",
            $"{used}（このビューの中で Ctrl+Z を押すと元に戻せます）",
            "削除");
    }

    /// <summary>選んだレイアウトの NicoKaraPrep の値を消す（足したものは無くなり、編集したベースのものはベースの値に戻る）。</summary>
    public void DeleteSelected()
    {
        if (SelectedLayout is not { } item || item.Origin == N3LayoutOrigin.Base) return;
        int index = Layouts.IndexOf(item);
        var entry = _history.Push(Own, null, DateTime.UtcNow, out _);
        bool removed = false;
        AsOwnChange(() => removed = _main.RemoveEditedLayout(item.Name));
        if (!removed)
        {
            _history.Drop(entry);
            UpdateUndoState();
            return;
        }
        UpdateUndoState();
        bool wasAdded = item.Origin == N3LayoutOrigin.Added;
        var rest = Layouts.Where(l => !ReferenceEquals(l, item)).ToList();
        string? next = !wasAdded ? item.Name : rest.Count == 0 ? null : rest[Math.Clamp(index, 0, rest.Count - 1)].Name;
        Refresh(next);
        EditorReloadRequested?.Invoke(this, EventArgs.Empty);
        SetStatus(wasAdded
            ? $"レイアウト設定「{item.Name}」を削除しました（Ctrl+Z で戻せます）"
            : $"レイアウト設定「{item.Name}」をベースの値に戻しました（Ctrl+Z で戻せます）");
    }

    /// <summary>
    /// 選んだレイアウトの名前を変える（NicoKaraPrep で足したものだけ。重なれば末尾に 2, 3…）。このレイアウトを指定した行（全タブ・
    /// 元に戻すの履歴も）とタブの固定の指定も新しい名前にする。変えたら true（変えられなければ欄を元に戻すよう知らせる）。
    /// </summary>
    public bool RenameSelected(string text)
    {
        if (SelectedLayout is not { } item) return false;
        text = text.Trim();
        if (text.Length == 0 || text == item.Name)
        {
            EditorReloadRequested?.Invoke(this, EventArgs.Empty);
            return false;
        }
        if (item.Origin != N3LayoutOrigin.Added)
        {
            SetStatus($"レイアウト設定「{item.Name}」はベースにある名前なので変えられません（「複製」してから名前を変えてください）");
            EditorReloadRequested?.Invoke(this, EventArgs.Empty);
            return false;
        }
        string old = item.Name;
        int lines = _main.GetAllTabs().Sum(t => t.Document.Lines.Count(l => l.LayoutName == old));
        var entry = _history.Push(Own, null, DateTime.UtcNow, out _);
        string? renamed = null;
        AsOwnChange(() => renamed = _main.RenameLayout(old, text));
        if (renamed is null)
        {
            _history.Drop(entry);
            UpdateUndoState();
            EditorReloadRequested?.Invoke(this, EventArgs.Empty);
            return false;
        }
        entry.Renames.Add((old, renamed));
        UpdateUndoState();
        Refresh(renamed);
        string unique = renamed != text ? $"（「{text}」はほかのレイアウト設定か、ページ・タブの指定で使われているため「{renamed}」にしました）" : "";
        string refs = lines > 0 ? $"。{lines} 行のレイアウトの指定も新しい名前にしました" : "";
        SetStatus($"レイアウト設定の名前を「{old}」から「{renamed}」に変えました{unique}{refs}（Ctrl+Z で戻せます）");
        return true;
    }

    /// <summary>ニコカラメーカー3 のテンプレートから選んだレイアウトを足す（名前が重なれば末尾に 2, 3…）。最初に足したものを選ぶ。</summary>
    public void AddTemplates(IReadOnlyList<N3Layout> templates)
    {
        if (templates.Count == 0) return;
        _history.Push(Own, null, DateTime.UtcNow, out _);
        string? first = null;
        var renamed = new List<string>();
        foreach (var template in templates)
        {
            var copy = template.Clone();
            copy.Id = Guid.NewGuid().ToString("N");
            string wanted = string.IsNullOrWhiteSpace(copy.Name) ? "新しいレイアウト" : copy.Name.Trim();
            copy.Name = _main.UniqueLayoutName(wanted);
            if (copy.Name != wanted) renamed.Add($"{wanted} → {copy.Name}");
            Own.Add(copy);
            first ??= copy.Name;
        }
        AsOwnChange(_main.SaveLayouts);
        UpdateUndoState();
        Refresh(first);
        string renamedText = renamed.Count > 0 ? $"（同じ名前があったため名前を変えたもの: {string.Join("・", renamed.Take(3))}{(renamed.Count > 3 ? " ほか" : "")}）" : "";
        SetStatus($"ニコカラメーカー3 のテンプレートからレイアウト設定 {templates.Count} 件を追加しました{renamedText}（Ctrl+Z で戻せます）{MergeOffNote()}");
    }

    /// <summary>メイン画面のレイアウトの操作を呼ぶ（そのあいだに届く <see cref="MainViewModel.LayoutsChanged"/> は自分の変更として扱う）。</summary>
    private void AsOwnChange(Action action)
    {
        _ownChange++;
        try
        {
            action();
        }
        finally
        {
            _ownChange--;
        }
    }

    /// <summary>ほかの所（n3proj の読み込み・右パネルなど）でレイアウト設定が変わった: 出しているあいだは一覧と編集欄を作り直す。</summary>
    private void OnMainLayoutsChanged(object? sender, EventArgs e)
    {
        if (_ownChange > 0 || !_active) return;
        Refresh(null);
        EditorReloadRequested?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------ ビューの出入り

    /// <summary>行リストで選んでいた行（表示中のタブの行番号。ビューに入るときにそのページを選ぶ）。</summary>
    private IReadOnlyList<int> _pendingLines = Array.Empty<int>();

    /// <summary>ビューに入る前に、行リストで選んでいた行（表示中のタブの行番号）を受け取る。</summary>
    public void SetLineContext(IReadOnlyList<int> selectedLines) => _pendingLines = selectedLines.ToList();

    /// <summary>
    /// ビューに入るとき。一覧とページの一覧を作り直し（ベースが替わっていれば反映）、<paramref name="selectLayoutName"/> があればそれを選ぶ。
    /// 行リストで選んでいた行があれば、そのページをページの一覧で選び、名前の指定が無ければそのページのレイアウトを選ぶ。
    /// 抜けている間にレイアウト設定が変わっていたら、元に戻す の履歴は捨てる（戻すと外での変更まで消えるため）。
    /// </summary>
    public void OnEnter(string? selectLayoutName)
    {
        _active = true;
        if (_exitSignature is not null && _exitSignature != N3LayoutHistory.Signature(Own))
        {
            _history.Clear();
        }
        _exitSignature = null;
        _history.BreakCoalescing();
        UpdateUndoState();
        Refresh(selectLayoutName);

        var lines = _pendingLines;
        _pendingLines = Array.Empty<int>();
        var pages = lines.Count == 0
            ? new List<LayoutPageItem>()
            : Pages.Where(p => p.Info.IsActiveTab && p.Info.Lines.Any(lines.Contains)).ToList();
        if (pages.Count > 0)
        {
            SelectPages(pages);
            if (selectLayoutName is null) SelectByName(pages[0].Info.LayoutName);
        }
        else if (selectLayoutName is not null)
        {
            SelectByName(selectLayoutName);
        }
        RefreshSongAction(rebuildFields: true);
        EditorReloadRequested?.Invoke(this, EventArgs.Empty);
        PreviewChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>ビューを抜けるとき（保存待ちを保存してから呼ぶ）。</summary>
    public void OnExit()
    {
        _exitSignature = N3LayoutHistory.Signature(Own);
        _active = false;
    }

    /// <summary>
    /// 外で文書・タブ・ベースが変わった（MCP のタブの切り替え・書き出し画面の「適用」・n3proj の読み込み）: 出しているあいだは一覧と
    /// ページの一覧・曲の既定の字幕アクションを作り直す（選択は名前・ページで保つ）。
    /// </summary>
    public void OnDocumentChanged()
    {
        if (!_active) return;
        Refresh(null);
        RefreshSongAction(rebuildFields: true);
        EditorReloadRequested?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------ 保存

    /// <summary>設定ファイルを読み込めず、保存を止めているときの一覧の下の表示。</summary>
    private const string NotSavedNote = "設定ファイルを読み込めなかったため、レイアウト設定は保存されません（起動したときのステータスバーの説明を見てください）";

    /// <summary>一覧の下に出す保存の状態。</summary>
    [ObservableProperty]
    private string saveNote = "レイアウト設定は、変更するとすぐに自動で保存されます";

    /// <summary>保存の状態の説明（ツールチップ）。</summary>
    public string SaveNoteToolTip =>
        "レイアウト設定は、変更するとすぐに設定ファイル（%APPDATA%\\NicoKaraPrep\\settings.json）へ自動で保存されます（アプリ共通。保存の操作はいりません）。" +
        "すべての曲の n3proj の書き出しで、ベースの同じ名前のレイアウト設定に上書きします（無い名前は足します）。\n" +
        "ページへのレイアウト・字幕アクションの指定と曲の既定の字幕アクションは、歌詞ファイルの隣の .tttproj に保存されます。";

    /// <summary>左の一覧の幅（px。設定に保存したもの）。</summary>
    public double ListWidth => _main.Settings.LayoutListWidthPx;

    /// <summary>左の一覧の幅を設定に保存する（まとめて保存する）。</summary>
    public void SaveListWidth(double width)
    {
        if (Math.Abs(_main.Settings.LayoutListWidthPx - width) < 0.5) return;
        _main.Settings.LayoutListWidthPx = width;
        MarkDirty(layouts: false);
    }

    /// <summary>保存を予約する（300ms 以内の変更はまとめて保存する）。</summary>
    private void MarkDirty(bool layouts)
    {
        if (layouts) _layoutsDirty = true;
        else _settingsDirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>予約中の保存をすぐに行う（ビューを抜けるとき・文書を入れ替える前・窓を閉じるとき）。</summary>
    public void FlushPendingSave()
    {
        _saveTimer.Stop();
        if (!_layoutsDirty && !_settingsDirty) return;
        bool layouts = _layoutsDirty;
        _layoutsDirty = false;
        _settingsDirty = false;
        try
        {
            // レイアウトの値を変えたときは、行リスト・字幕のプレビューの作り直しも知らせる（SaveLayouts）。一覧の幅だけなら設定の保存だけ
            if (layouts) AsOwnChange(_main.SaveLayouts);
            else _main.Settings.Save();
            if (layouts)
            {
                SaveNote = _main.Settings.LoadFailed ? NotSavedNote : $"レイアウト設定を自動で保存しました（{DateTime.Now:HH:mm:ss}）";
            }
        }
        catch (Exception ex)
        {
            SetStatus($"エラー: レイアウト設定を保存できませんでした（{ex.Message}）");
        }
    }

    // ------------------------------------------------------------ 元に戻す・やり直し

    private void UpdateUndoState()
    {
        CanUndo = _history.CanUndo;
        CanRedo = _history.CanRedo;
        // ビューを抜けたあとに、このビューの操作（数値・名前の欄を離れたときの確定）で変えたとき: 変更は元に戻すの履歴に入っているので、
        // 抜けたときの署名を取り直す（外での変更とみなして、次に入ったときに履歴を捨てないように）
        if (!_active && _exitSignature is not null) _exitSignature = N3LayoutHistory.Signature(Own);
    }

    /// <summary>レイアウト設定ビューの中の操作を 1 つ戻す。</summary>
    public bool Undo()
    {
        var change = _history.Undo(Own);
        if (change is null)
        {
            SetStatus("レイアウト設定で元に戻す操作はありません");
            return false;
        }
        Restore(change);
        SetStatus("レイアウト設定の変更を元に戻しました（Ctrl+Y でやり直し）");
        return true;
    }

    /// <summary>元に戻した操作をやり直す。</summary>
    public bool Redo()
    {
        var change = _history.Redo(Own);
        if (change is null)
        {
            SetStatus("レイアウト設定でやり直す操作はありません");
            return false;
        }
        Restore(change);
        SetStatus("レイアウト設定の変更をやり直しました");
        return true;
    }

    /// <summary>記録した一覧に戻す（入れ物はそのまま、中身を入れ替える）。名前の変更で付け替えた行・タブの参照も付け替え直す。</summary>
    private void Restore(N3LayoutHistory.Change change)
    {
        string? select = SelectedLayout?.Name;
        Own.Clear();
        Own.AddRange(change.Layouts);
        foreach (var (from, to) in change.ReferenceChanges)
        {
            _main.RenameLayoutReferences(from, to);
            if (select == from) select = to;
        }
        UpdateUndoState();
        MarkDirty(layouts: true);
        Refresh(select);
        EditorReloadRequested?.Invoke(this, EventArgs.Empty);
    }
}
