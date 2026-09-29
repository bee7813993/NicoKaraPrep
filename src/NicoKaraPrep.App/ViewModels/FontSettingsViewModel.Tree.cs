using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.ViewModels;

/// <summary>
/// フォント設定ビューの左の一覧の階層: 節の作り直し・選択・開閉、フォルダの追加・名前・削除、
/// 階層の中での移動（上下・階層を上げる／下げる・移動...・ドラッグ）、中央のフォルダの欄。
/// 階層を変えたら、フォント設定の一覧を階層の順にそろえ（書き出しの並び）、保存を予約して一覧を作り直す。
/// </summary>
public sealed partial class FontSettingsViewModel
{
    /// <summary>「この曲専用」のまとまりを折りたたんでいるか（保存しない）。</summary>
    private bool _songGroupCollapsed;

    // ------------------------------------------------------------ ボタンの状態

    [ObservableProperty]
    private bool canMoveUp;

    [ObservableProperty]
    private bool canMoveDown;

    /// <summary>階層を下げられるか（すぐ上の節の中へ）。</summary>
    [ObservableProperty]
    private bool canIndent;

    /// <summary>階層を上げられるか（親の外へ）。</summary>
    [ObservableProperty]
    private bool canOutdent;

    /// <summary>「移動...」できるか（アプリ共通のフォント設定・フォルダを選んでいる）。</summary>
    [ObservableProperty]
    private bool canMoveTo;

    /// <summary>削除できるか（フォント設定かフォルダを選んでいる）。</summary>
    [ObservableProperty]
    private bool canDeleteSelection;

    /// <summary>複製できるか（フォント設定を選んでいる）。</summary>
    [ObservableProperty]
    private bool canDuplicate;

    // ------------------------------------------------------------ フォルダの欄

    /// <summary>フォルダの欄の見出し（「フォルダ」「この曲専用」）。</summary>
    [ObservableProperty]
    private string folderTitle = "";

    /// <summary>フォルダの名前の欄の値。</summary>
    [ObservableProperty]
    private string folderNameText = "";

    /// <summary>フォルダの中身の説明。</summary>
    [ObservableProperty]
    private string folderSummary = "";

    /// <summary>フォルダの名前を変えたり削除したりできるか（「この曲専用」のまとまりは false）。</summary>
    [ObservableProperty]
    private bool canEditFolder;

    /// <summary>選択中のフォルダ・まとまりの中のフォント設定（下の階層も含む。上から順）。</summary>
    public ObservableCollection<FontListItem> FolderFonts { get; } = new();

    // ------------------------------------------------------------ 節の作り直し

    /// <summary>一覧の行（<see cref="_all"/>）とアプリ共通の階層から、左の一覧の節を作り直す。検索の結果に出す場所（PathText）も付ける。</summary>
    private void BuildTreeItems()
    {
        _treeRoots.Clear();
        _treeItems.Clear();
        var common = new Dictionary<string, FontListItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _all)
        {
            if (!item.IsSong) common.TryAdd(item.Id, item);
        }

        var songs = _all.Where(i => i.IsSong).ToList();
        if (songs.Count > 0)
        {
            var group = FontTreeItem.ForSongGroup();
            foreach (var s in songs)
            {
                s.PathText = "";
                var child = FontTreeItem.ForFont(s, null);
                child.Parent = group;
                group.Children.Add(child);
                _treeItems.TryAdd(child.Key, child);
            }
            group.CountText = songs.Count.ToString();
            group.GroupToolTip = $"この曲専用のフォント設定 {songs.Count} 件（この曲の書き出しでは、アプリ共通の同じ名前のフォント設定より優先されます）";
            _treeItems[group.Key] = group;
            _treeRoots.Add(group);
        }

        foreach (var node in Tree)
        {
            if (CreateTreeItem(node, null, "", common) is { } item) _treeRoots.Add(item);
        }
    }

    private FontTreeItem? CreateTreeItem(N3FontTreeNode node, FontTreeItem? parent, string path, Dictionary<string, FontListItem> common)
    {
        FontTreeItem item;
        if (node.FontId is { } id)
        {
            if (!common.TryGetValue(id, out var font)) return null; // 整えたあとなので無いはずだが、念のため出さない
            font.PathText = path;
            item = FontTreeItem.ForFont(font, node);
        }
        else
        {
            item = FontTreeItem.ForFolder(node);
        }
        item.Parent = parent;
        _treeItems.TryAdd(item.Key, item);

        string childPath = path.Length == 0 ? item.Label : $"{path} › {item.Label}";
        foreach (var c in node.Children)
        {
            if (CreateTreeItem(c, item, childPath, common) is { } child) item.Children.Add(child);
        }
        if (item.IsFolder)
        {
            int n = N3FontTree.CountFonts(node);
            item.CountText = n.ToString();
            item.GroupToolTip = $"フォルダ「{item.Name}」: フォント設定 {n} 件（下の階層も含む）";
        }
        return item;
    }

    /// <summary>識別子で階層の節を探す（無ければ null）。</summary>
    public FontTreeItem? FindTreeItem(string key) => _treeItems.GetValueOrDefault(key);

    /// <summary>節の下の階層も含めて、上から順にたどる（節自身は含まない）。</summary>
    private static IEnumerable<FontTreeItem> Descendants(FontTreeItem item)
    {
        foreach (var c in item.Children)
        {
            yield return c;
            foreach (var d in Descendants(c)) yield return d;
        }
    }

    // ------------------------------------------------------------ 選択・開閉

    /// <summary>ツリーで節が選ばれたとき（フォント設定なら編集欄、フォルダ・まとまりならフォルダの欄を出す）。</summary>
    public void SelectNode(string key)
    {
        if (FindTreeItem(key) is not { } item) return;
        if (item.Font is { } font)
        {
            SelectedItem = font;
        }
        else
        {
            SelectedFolder = item;
        }
    }

    /// <summary>節が開いているか（ツリーの節を作るときに使う）。</summary>
    public bool IsExpanded(FontTreeItem item) => item.IsSongGroup ? !_songGroupCollapsed : item.Node is not { Collapsed: true };

    /// <summary>ツリーで節を開いた・閉じたとき（アプリ共通の階層は設定に保存する。元に戻す の対象にはしない）。</summary>
    public void SetExpanded(string key, bool expanded)
    {
        if (string.Equals(key, FontTreeItem.SongGroupKey, StringComparison.Ordinal))
        {
            _songGroupCollapsed = !expanded;
            return;
        }
        if (N3FontTree.Find(Tree, key) is not { } loc || loc.Node.Collapsed == !expanded) return;
        loc.Node.Collapsed = !expanded;
        MarkDirty(song: false);
    }

    // ------------------------------------------------------------ 変更の共通処理

    /// <summary>選択中のアプリ共通のフォント設定かフォルダの識別子（曲専用・まとまり・未選択なら null）。</summary>
    private string? CommonSelectionKey() =>
        SelectedItem is { IsSong: false } font ? font.Id
        : SelectedFolder is { IsFolder: true } folder ? folder.Key
        : null;

    /// <summary>階層を変えたあと: フォント設定の一覧を階層の順にそろえ、保存を予約し、一覧を作り直して選ぶ。</summary>
    private void AfterTreeChanged(string? selectKey, string? status = null)
    {
        N3FontTree.SortFonts(Common, Tree);
        MarkDirty(song: false);
        Rebuild(selectKey);
        if (status is not null) SetStatus(status);
    }

    /// <summary>節の名前（フォント設定は名前、フォルダはフォルダの名前）。</summary>
    private string LabelOf(N3FontTreeNode node) =>
        node.IsFolder ? node.FolderName ?? "" : N3FontLibrary.Find(Common, node.FontId!)?.Name ?? "";

    /// <summary>節の場所の説明（「フォルダ「蓮ノ空」の中」「「（102期生）」の中」「いちばん上の階層」）。</summary>
    private string WhereText(string key)
    {
        if (N3FontTree.Find(Tree, key) is not { Parent: { } parent }) return "いちばん上の階層";
        return parent.IsFolder ? $"フォルダ「{parent.FolderName}」の中" : $"「{LabelOf(parent)}」の中";
    }

    /// <summary>
    /// 新しい節をアプリ共通の階層に入れる: フォルダを選んでいればその中の末尾（フォルダを開く）、
    /// アプリ共通のフォント設定を選んでいればそのすぐ後、それ以外は最上位の末尾。入れた場所の説明を返す。
    /// </summary>
    private string PlaceNewCommonNode(N3FontTreeNode node)
    {
        if (SelectedFolder is { IsFolder: true, Node: { } folder } && N3FontTree.Insert(Tree, folder.Key, node))
        {
            folder.Collapsed = false;
        }
        else if (SelectedItem is not { IsSong: false } selected || !N3FontTree.InsertAfter(Tree, selected.Id, node))
        {
            Tree.Add(node);
        }
        return WhereText(node.Key);
    }

    /// <summary>節を外したあとに選ぶ節（下の節を繰り上げるならその先頭、次の節、前の節、親の順。無ければ null）。外す前に呼ぶ。</summary>
    private static string? NextKeyAfterRemoving(N3FontTreeLocation loc, bool keepChildren)
    {
        if (keepChildren && loc.Node.Children.Count > 0) return loc.Node.Children[0].Key;
        if (loc.Index + 1 < loc.Siblings.Count) return loc.Siblings[loc.Index + 1].Key;
        if (loc.Index > 0) return loc.Siblings[loc.Index - 1].Key;
        return loc.Parent?.Key;
    }

    // ------------------------------------------------------------ フォルダ

    /// <summary>
    /// フォルダを作る（選んでいるフォルダの中の末尾・選んでいるアプリ共通のフォント設定のすぐ後・最上位の末尾のどれか）。
    /// 作ったフォルダを選ぶ（ビューは名前の欄にフォーカスを移す）。
    /// </summary>
    public void AddFolder()
    {
        PushUndo(null);
        var node = N3FontTreeNode.ForFolder(N3FontTree.NewFolderName);
        string where = PlaceNewCommonNode(node);
        MarkDirty(song: false);
        ClearFilterFor(node.Key);
        SetStatus($"フォルダ「{node.FolderName}」を{where}に作りました（名前を入力して Enter で確定します）");
    }

    /// <summary>選択中のフォルダの名前を変える（前後の空白は除く。空なら変えない）。</summary>
    public void RenameSelectedFolder(string newName)
    {
        if (SelectedFolder is not { IsFolder: true, Node: { } node } folder) return;
        string name = (newName ?? "").Trim();
        string old = node.FolderName ?? "";
        if (name.Length == 0 || name == old)
        {
            // 空にしたときは元の名前を欄に戻す
            FolderNameText = "";
            FolderNameText = old;
            return;
        }
        PushUndo(null);
        node.FolderName = name;
        MarkDirty(song: false);

        // 一覧は作り直さない（名前の欄を離れたのが一覧の行を押したときでも、その操作の途中でツリーを作り直さないように）
        folder.Name = name;
        folder.GroupToolTip = $"フォルダ「{name}」: フォント設定 {N3FontTree.CountFonts(node)} 件（下の階層も含む）";
        RefreshPaths();
        LoadFolderPanel();
        SetStatus($"フォルダの名前を「{old}」から「{name}」に変えました");
    }

    /// <summary>
    /// フォント設定の名前を変えたあとの表示の更新（一覧は作り直さない。名前の欄を離れたのが一覧の行を押したときでも、
    /// その操作の途中でツリーを作り直さないように）。行の表示・下の階層の場所・編集欄・使用状況を更新する。
    /// </summary>
    private void RefreshAfterRename(FontListItem item)
    {
        item.Refresh();
        RefreshPaths();
        if (ReferenceEquals(SelectedItem, item)) Editor.RefreshState();
        RunAnalysis();
    }

    /// <summary>検索の結果に出す場所（上の階層の名前）を、今の階層の名前から付け直す。</summary>
    private void RefreshPaths()
    {
        void Walk(IEnumerable<FontTreeItem> items, string path)
        {
            foreach (var item in items)
            {
                if (item.IsSongGroup) continue;
                if (item.Font is { } font) font.PathText = path;
                Walk(item.Children, path.Length == 0 ? item.Label : $"{path} › {item.Label}");
            }
        }
        Walk(_treeRoots, "");
    }

    /// <summary>フォルダ・まとまりの中のフォント設定（下の階層も含む。上から順）。</summary>
    public List<FontListItem> FontsIn(FontTreeItem group) =>
        Descendants(group).Where(i => i.Font is not null).Select(i => i.Font!).ToList();

    /// <summary>
    /// 選択中のフォルダを削除する。withFonts が false なら中身（下の節）はフォルダの位置へ繰り上げ、
    /// true なら中のフォント設定もまとめて削除する（行の参照は残る。Ctrl+Z で戻せる）。
    /// </summary>
    public void DeleteSelectedFolder(bool withFonts)
    {
        if (SelectedFolder is not { IsFolder: true } folder || N3FontTree.Find(Tree, folder.Key) is not { } loc) return;
        string? next = NextKeyAfterRemoving(loc, keepChildren: !withFonts);
        string name = folder.Name;
        PushUndo(null);
        if (withFonts)
        {
            var ids = N3FontTree.DescendantFontIds(loc.Node);
            foreach (string id in ids) N3FontLibrary.Remove(Common, id);
            N3FontTree.Remove(Tree, folder.Key, keepChildren: false);
            AfterTreeChanged(next, $"フォルダ「{name}」と中のフォント設定 {ids.Count} 件を削除しました（Ctrl+Z で元に戻せます）");
        }
        else
        {
            int n = loc.Node.Children.Count;
            N3FontTree.Remove(Tree, folder.Key, keepChildren: true);
            string moved = n > 0 ? $"中の {n} 件は 1 つ上の階層へ移しました。" : "";
            AfterTreeChanged(next, $"フォルダ「{name}」を削除しました（{moved}Ctrl+Z で元に戻せます）");
        }
    }

    /// <summary>選択中のフォルダ・まとまりの欄を読み直す。</summary>
    private void LoadFolderPanel()
    {
        FolderFonts.Clear();
        if (SelectedFolder is not { } group)
        {
            FolderTitle = "";
            FolderNameText = "";
            FolderSummary = "";
            CanEditFolder = false;
            return;
        }
        CanEditFolder = group.IsFolder;
        FolderTitle = group.IsSongGroup ? "この曲専用" : "フォルダ";
        FolderNameText = group.Name;
        var fonts = FontsIn(group);
        foreach (var f in fonts) FolderFonts.Add(f);
        FolderSummary = group.IsSongGroup
            ? $"この曲専用のフォント設定 {fonts.Count} 件。この曲の書き出しでは、アプリ共通の同じ名前のフォント設定より優先されます。"
            : $"フォント設定 {fonts.Count} 件（下の階層も含む）。フォルダはにこぷれっぷの中で探しやすくするためのもので、書き出しには影響しません" +
              "（ニコカラメーカー3 のフォント設定は、左の一覧の上から順に並びます）。";
    }

    // ------------------------------------------------------------ 階層の中での移動

    /// <summary>選択中の節の階層を 1 つ下げる（すぐ上の節の中の末尾へ）。</summary>
    public void IndentSelected()
    {
        if (CommonSelectionKey() is not { } key || N3FontTree.Find(Tree, key) is not { Index: > 0 } loc) return;
        string target = LabelOf(loc.Siblings[loc.Index - 1]);
        PushUndo(null);
        N3FontTree.Indent(Tree, key);
        AfterTreeChanged(key, $"「{LabelOf(loc.Node)}」を「{target}」の中へ移しました");
    }

    /// <summary>選択中の節の階層を 1 つ上げる（親のすぐ後へ）。</summary>
    public void OutdentSelected()
    {
        if (CommonSelectionKey() is not { } key || N3FontTree.Find(Tree, key) is not { Parent: not null } loc) return;
        PushUndo(null);
        N3FontTree.Outdent(Tree, key);
        AfterTreeChanged(key, $"「{LabelOf(loc.Node)}」を{WhereText(key)}へ移しました");
    }

    /// <summary>
    /// 「移動...」の移し先（先頭に最上位、続けて階層の上から順のフォルダとフォント設定。選択中の節とその下の階層は除く）。
    /// </summary>
    public List<FontMoveTarget> MoveTargets()
    {
        var list = new List<FontMoveTarget> { new(null, "いちばん上の階層", 0, true, "") };
        if (CommonSelectionKey() is not { } key) return list;

        void Walk(List<N3FontTreeNode> nodes, int depth, string path)
        {
            foreach (var n in nodes)
            {
                if (string.Equals(n.Key, key, StringComparison.OrdinalIgnoreCase)) continue; // 自分と自分の下の階層へは移せない
                string label = LabelOf(n);
                list.Add(new FontMoveTarget(n.Key, label, depth, n.IsFolder, path));
                Walk(n.Children, depth + 1, path.Length == 0 ? label : $"{path} › {label}");
            }
        }
        Walk(Tree, 1, "");
        return list;
    }

    /// <summary>選択中の節を <paramref name="targetKey"/> の節の中の末尾（null なら最上位の末尾）へ移す。</summary>
    public void MoveSelectedInto(string? targetKey)
    {
        if (CommonSelectionKey() is not { } key || N3FontTree.Find(Tree, key) is not { } loc) return;

        // すでにそこの末尾にあるなら何もしない
        bool sameParent = targetKey is null ? loc.Parent is null : string.Equals(loc.Parent?.Key, targetKey, StringComparison.OrdinalIgnoreCase);
        if (sameParent && loc.Index == loc.Siblings.Count - 1)
        {
            SetStatus($"「{LabelOf(loc.Node)}」はすでに{WhereText(key)}の末尾にあります");
            return;
        }

        var entry = PushUndo(null);
        if (!N3FontTree.MoveInto(Tree, key, targetKey))
        {
            DropLastUndo(entry);
            SetStatus("自分自身や自分の下の階層へは移せません");
            return;
        }
        AfterTreeChanged(key, $"「{LabelOf(loc.Node)}」を{WhereText(key)}へ移しました");
    }

    /// <summary>
    /// ツリーでドラッグして並べ替えたあとの節の並びを、アプリ共通の階層（と曲専用の並び）に反映する。
    /// 曲専用のフォント設定とアプリ共通の節が入れ替わる（まとまりの外へ出す・中へ入れる）並びは受け付けず、元の表示に戻す。
    /// 反映したら true。
    /// </summary>
    public bool ApplyDroppedTree(IReadOnlyList<DroppedTreeNode> roots, string? movedKey)
    {
        var songIds = new HashSet<string>(Song.Select(f => f.Id), StringComparer.OrdinalIgnoreCase);
        var existing = new Dictionary<string, N3FontTreeNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var (node, _) in N3FontTree.Walk(Tree)) existing.TryAdd(node.Key, node);

        bool valid = true;
        List<string>? songOrder = null;
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        N3FontTreeNode? Convert(DroppedTreeNode d)
        {
            if (!existing.TryGetValue(d.Key, out var old) || !used.Add(d.Key))
            {
                valid = false; // 曲専用のフォント設定・まとまりがアプリ共通の階層に入った、または同じ節が 2 つ
                return null;
            }
            var n = old.IsFolder
                ? new N3FontTreeNode { FolderId = old.FolderId, FolderName = old.FolderName, Collapsed = old.Collapsed }
                : new N3FontTreeNode { FontId = old.FontId, Collapsed = old.Collapsed };
            foreach (var c in d.Children)
            {
                if (Convert(c) is { } child) n.Children.Add(child);
            }
            return n;
        }

        var newRoots = new List<N3FontTreeNode>();
        foreach (var r in roots)
        {
            if (string.Equals(r.Key, FontTreeItem.SongGroupKey, StringComparison.Ordinal))
            {
                songOrder = new List<string>();
                foreach (var c in r.Children)
                {
                    if (!songIds.Contains(c.Key) || c.Children.Count > 0) valid = false; // アプリ共通の節が入った・曲専用の下に入れた
                    songOrder.Add(c.Key);
                }
                continue;
            }
            if (Convert(r) is { } node) newRoots.Add(node);
        }
        if (used.Count != existing.Count) valid = false;
        if (Song.Count > 0 && (songOrder is null || songOrder.Count != Song.Count || !songOrder.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(songIds)))
        {
            valid = false;
        }

        if (!valid)
        {
            Rebuild(movedKey);
            SetStatus("この曲専用のフォント設定とアプリ共通のフォント設定のあいだは、ドラッグでは移せません（中央の「アプリ共通へ移す」「この曲専用にコピー」を使ってください）");
            return false;
        }

        // 移した節の移し先は開いておく（閉じたフォルダに落としたときに見失わないように）
        if (movedKey is not null && N3FontTree.Ancestors(newRoots, movedKey).LastOrDefault() is { } newParent) newParent.Collapsed = false;

        bool treeChanged = !N3FontTree.SameShape(Tree, newRoots);
        bool songChanged = songOrder is not null && !songOrder.SequenceEqual(Song.Select(f => f.Id), StringComparer.OrdinalIgnoreCase);
        if (!treeChanged && !songChanged)
        {
            Rebuild(movedKey);
            return true;
        }

        PushUndo(null);
        if (songChanged)
        {
            var byId = Song.ToDictionary(f => f.Id, StringComparer.OrdinalIgnoreCase);
            var reordered = songOrder!.Select(id => byId[id]).ToList();
            Song.Clear();
            Song.AddRange(reordered);
            MarkDirty(song: true);
        }
        if (treeChanged)
        {
            Tree.Clear();
            Tree.AddRange(newRoots);
        }
        string? label = movedKey is null ? null
            : N3FontTree.Find(Tree, movedKey) is { } moved ? LabelOf(moved.Node)
            : Song.FirstOrDefault(f => string.Equals(f.Id, movedKey, StringComparison.OrdinalIgnoreCase))?.Name;
        string where = movedKey is not null && N3FontTree.Find(Tree, movedKey) is not null ? WhereText(movedKey) : "この曲専用の中";
        AfterTreeChanged(movedKey, label is null ? "並びを変えました" : $"「{label}」を{where}へ移しました");
        return true;
    }

    // ------------------------------------------------------------ ボタンの状態

    /// <summary>選択と表示のしかたに合わせて、一覧の下のボタンの状態を更新する。上下・階層の変更は階層で見せているときだけ。</summary>
    private void UpdateCommandState()
    {
        bool tree = IsTreeMode;
        var loc = CommonSelectionKey() is { } key ? N3FontTree.Find(Tree, key) : null;
        if (SelectedItem is { IsSong: true } song)
        {
            int i = Song.IndexOf(song.Font);
            CanMoveUp = tree && i > 0;
            CanMoveDown = tree && i >= 0 && i < Song.Count - 1;
        }
        else
        {
            CanMoveUp = tree && loc is { Index: > 0 };
            CanMoveDown = tree && loc is not null && loc.Index < loc.Siblings.Count - 1;
        }
        CanIndent = tree && loc is { Index: > 0 };
        CanOutdent = tree && loc is { Parent: not null };
        CanMoveTo = loc is not null;
        CanDeleteSelection = SelectedItem is not null || SelectedFolder is { IsFolder: true };
        CanDuplicate = SelectedItem is not null;
    }
}
