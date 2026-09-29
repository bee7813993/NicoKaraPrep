using System.Text.Json.Serialization;

namespace NicoKaraPrep.Core.Model;

/// <summary>
/// アプリ共通のフォント設定の階層（見つけやすくするための並べ方）の節 1 つ。フォルダ（フォント設定ではないまとまり。例:「蓮ノ空」）か、
/// フォント設定 1 件。フォント設定の節も下に節を持てる（例:「（102期生）」の下に「（梢）」「（綴理）」）。
/// フォント設定の中身は <see cref="Project.AppSettings.N3FontSets"/> にあり、節は <see cref="FontId"/>（<see cref="N3FontSet.Id"/>）で指す。
/// 階層はにこぷれっぷの中での整理用で、書き出しには並び順（上から、下の階層も順に）だけが効く。
/// </summary>
public sealed class N3FontTreeNode
{
    /// <summary>フォント設定の節なら、そのフォント設定の <see cref="N3FontSet.Id"/>（フォルダは null）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FontId { get; set; }

    /// <summary>フォルダの節の識別子（フォント設定の節は null）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FolderId { get; set; }

    /// <summary>フォルダの名前（フォント設定の節は null）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FolderName { get; set; }

    /// <summary>折りたたんでいるか（既定は開いている）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Collapsed { get; set; }

    /// <summary>下の節（常に non-null）。</summary>
    [JsonIgnore]
    public List<N3FontTreeNode> Children { get; set; } = new();

    /// <summary>JSON の下の節（空なら書かない）。</summary>
    [JsonPropertyName("Children")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<N3FontTreeNode>? ChildrenForJson
    {
        get => Children.Count > 0 ? Children : null;
        set => Children = value ?? new();
    }

    /// <summary>フォルダの節か（false はフォント設定の節）。</summary>
    [JsonIgnore]
    public bool IsFolder => FontId is null;

    /// <summary>節の識別子（フォント設定の節は FontId、フォルダは FolderId）。</summary>
    [JsonIgnore]
    public string Key => FontId ?? FolderId ?? "";

    /// <summary>フォント設定の節を作る。</summary>
    public static N3FontTreeNode ForFont(string fontId) => new() { FontId = fontId };

    /// <summary>フォルダの節を作る（識別子は新しく付ける）。</summary>
    public static N3FontTreeNode ForFolder(string name) => new() { FolderId = Guid.NewGuid().ToString(), FolderName = name };

    /// <summary>深いコピー（識別子も同じ）。</summary>
    public N3FontTreeNode Clone() => new()
    {
        FontId = FontId,
        FolderId = FolderId,
        FolderName = FolderName,
        Collapsed = Collapsed,
        Children = Children.Select(c => c.Clone()).ToList(),
    };
}

/// <summary>節の場所（<see cref="N3FontTree.Find"/> の結果）。</summary>
/// <param name="Node">節。</param>
/// <param name="Siblings">節が入っている一覧（最上位の一覧か、親の <see cref="N3FontTreeNode.Children"/>）。</param>
/// <param name="Index">一覧の中の位置。</param>
/// <param name="Parent">親（最上位なら null）。</param>
public sealed record N3FontTreeLocation(N3FontTreeNode Node, List<N3FontTreeNode> Siblings, int Index, N3FontTreeNode? Parent);

/// <summary>
/// フォント設定の階層（<see cref="N3FontTreeNode"/> の一覧 = 最上位）に対する操作。一覧を直接変更する。
/// 元に戻す の記録・保存・フォント設定の一覧の並べ替えの呼び出しは呼び出し側で行う。
/// </summary>
public static class N3FontTree
{
    /// <summary>新しいフォルダの名前。</summary>
    public const string NewFolderName = "新しいフォルダ";

    /// <summary>ニコカラメーカー3 のテンプレート（.tpl）から取り込んだフォント設定を入れるフォルダの名前。</summary>
    public const string TemplateFolderName = "ニコカラメーカー3 のテンプレート";

    /// <summary>名前の無いフォルダを読み込んだときの名前。</summary>
    public const string UnnamedFolderName = "フォルダ";

    // ------------------------------------------------------------ 整える

    /// <summary>
    /// 階層をフォント設定の一覧に合わせる。
    /// 一覧に無いフォント設定の節は外し（下の節はその位置へ繰り上げる）、同じフォント設定の節が 2 つ以上あれば後のものを同じように外す。
    /// フォルダの識別子が無い・重なるときは付け直し、名前が無ければ「フォルダ」にする。
    /// 階層に無いフォント設定は足す。<paramref name="groupImported"/> なら、取り込んだもの（<see cref="N3FontSet.ImportedFrom"/> がある）は
    /// 最上位の取り込み元ごとのフォルダ（n3proj はファイル名、テンプレートは「ニコカラメーカー3 のテンプレート」。同じ名前があればそこ）の末尾へ、
    /// それ以外は最上位の末尾へ。変わったら true。
    /// </summary>
    public static bool Normalize(List<N3FontTreeNode> roots, IReadOnlyList<N3FontSet> fonts, bool groupImported = true)
    {
        var ids = new HashSet<string>(fonts.Select(f => f.Id), StringComparer.OrdinalIgnoreCase);
        var seenFonts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool changed = NormalizeList(roots, ids, seenFonts, seenFolders);

        foreach (var f in fonts)
        {
            if (seenFonts.Contains(f.Id)) continue;
            seenFonts.Add(f.Id);
            var node = N3FontTreeNode.ForFont(f.Id);
            string? group = groupImported ? ImportGroupName(f) : null;
            if (group is null)
            {
                roots.Add(node);
            }
            else
            {
                EnsureRootFolder(roots, group).Children.Add(node);
            }
            changed = true;
        }
        return changed;
    }

    private static bool NormalizeList(List<N3FontTreeNode> list, HashSet<string> ids, HashSet<string> seenFonts, HashSet<string> seenFolders)
    {
        bool changed = false;
        for (int i = 0; i < list.Count; i++)
        {
            var node = list[i];
            if (node is null)
            {
                list.RemoveAt(i--);
                changed = true;
                continue;
            }
            node.Children ??= new();

            if (node.FontId is { } fontId)
            {
                if (node.FolderId is not null || node.FolderName is not null)
                {
                    node.FolderId = null;
                    node.FolderName = null;
                    changed = true;
                }
                if (!ids.Contains(fontId) || !seenFonts.Add(fontId))
                {
                    // 無いフォント設定・2 つ目の節: 外して、下の節をこの位置へ繰り上げる（繰り上げた節もこのあと調べる）
                    list.RemoveAt(i);
                    list.InsertRange(i, node.Children);
                    i--;
                    changed = true;
                    continue;
                }
            }
            else
            {
                if (string.IsNullOrEmpty(node.FolderId) || !seenFolders.Add(node.FolderId))
                {
                    node.FolderId = Guid.NewGuid().ToString();
                    seenFolders.Add(node.FolderId);
                    changed = true;
                }
                if (string.IsNullOrWhiteSpace(node.FolderName))
                {
                    node.FolderName = UnnamedFolderName;
                    changed = true;
                }
            }
            changed |= NormalizeList(node.Children, ids, seenFonts, seenFolders);
        }
        return changed;
    }

    /// <summary>取り込んだフォント設定を入れるフォルダの名前（取り込んだものでなければ null）。</summary>
    public static string? ImportGroupName(N3FontSet font)
    {
        if (string.IsNullOrWhiteSpace(font.ImportedFrom)) return null;
        string path = font.ImportedFrom;
        if (path.EndsWith(".tpl", StringComparison.OrdinalIgnoreCase)) return TemplateFolderName;
        string name;
        try
        {
            name = Path.GetFileNameWithoutExtension(path);
        }
        catch (ArgumentException)
        {
            return null;
        }
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <summary>最上位の同じ名前のフォルダを返す（無ければ最上位の末尾に作る）。</summary>
    public static N3FontTreeNode EnsureRootFolder(List<N3FontTreeNode> roots, string name)
    {
        var folder = roots.FirstOrDefault(n => n.IsFolder && n.FolderName == name);
        if (folder is null)
        {
            folder = N3FontTreeNode.ForFolder(name);
            roots.Add(folder);
        }
        return folder;
    }

    // ------------------------------------------------------------ 並び

    /// <summary>節を上から順に（下の階層も、その節のすぐ後に）たどる。Depth は最上位が 0。</summary>
    public static IEnumerable<(N3FontTreeNode Node, int Depth)> Walk(IEnumerable<N3FontTreeNode> roots)
    {
        var stack = new Stack<(IEnumerator<N3FontTreeNode> Items, int Depth)>();
        stack.Push((roots.GetEnumerator(), 0));
        while (stack.Count > 0)
        {
            var (items, depth) = stack.Peek();
            if (!items.MoveNext())
            {
                stack.Pop();
                continue;
            }
            var node = items.Current;
            yield return (node, depth);
            if (node.Children.Count > 0) stack.Push((node.Children.GetEnumerator(), depth + 1));
        }
    }

    /// <summary>フォント設定の Id を上から順に（下の階層も順に）並べたもの。</summary>
    public static List<string> FontOrder(IEnumerable<N3FontTreeNode> roots) =>
        Walk(roots).Where(x => x.Node.FontId is not null).Select(x => x.Node.FontId!).ToList();

    /// <summary>
    /// フォント設定の一覧を階層の順（上から、下の階層も順に）に並べ替える（階層に無いものは元の順で末尾）。
    /// 書き出しではこの順にニコカラメーカー3 のフォント設定が並ぶ。変わったら true。
    /// </summary>
    public static bool SortFonts(List<N3FontSet> fonts, IEnumerable<N3FontTreeNode> roots)
    {
        var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in FontOrder(roots)) rank.TryAdd(id, rank.Count);
        var sorted = fonts
            .Select((f, i) => (Font: f, Key: rank.TryGetValue(f.Id, out int r) ? r : rank.Count + i))
            .OrderBy(x => x.Key)
            .Select(x => x.Font)
            .ToList();
        if (sorted.SequenceEqual(fonts, ReferenceEqualityComparer.Instance)) return false;
        fonts.Clear();
        fonts.AddRange(sorted);
        return true;
    }

    // ------------------------------------------------------------ 探す

    /// <summary>節を探す（見つからなければ null）。</summary>
    public static N3FontTreeLocation? Find(List<N3FontTreeNode> roots, string key) => Find(roots, key, null);

    private static N3FontTreeLocation? Find(List<N3FontTreeNode> list, string key, N3FontTreeNode? parent)
    {
        for (int i = 0; i < list.Count; i++)
        {
            var node = list[i];
            if (string.Equals(node.Key, key, StringComparison.OrdinalIgnoreCase)) return new N3FontTreeLocation(node, list, i, parent);
            if (node.Children.Count > 0 && Find(node.Children, key, node) is { } found) return found;
        }
        return null;
    }

    /// <summary>節の上の階層の節（最上位から順に。節自身は含まない。見つからなければ空）。</summary>
    public static List<N3FontTreeNode> Ancestors(List<N3FontTreeNode> roots, string key)
    {
        var path = new List<N3FontTreeNode>();
        return FindPath(roots, key, path) ? path : new List<N3FontTreeNode>();
    }

    private static bool FindPath(List<N3FontTreeNode> list, string key, List<N3FontTreeNode> path)
    {
        foreach (var node in list)
        {
            if (string.Equals(node.Key, key, StringComparison.OrdinalIgnoreCase)) return true;
            path.Add(node);
            if (FindPath(node.Children, key, path)) return true;
            path.RemoveAt(path.Count - 1);
        }
        return false;
    }

    /// <summary><paramref name="node"/> 自身か、その下の階層に <paramref name="key"/> の節があるか。</summary>
    public static bool Contains(N3FontTreeNode node, string key) =>
        string.Equals(node.Key, key, StringComparison.OrdinalIgnoreCase) || node.Children.Any(c => Contains(c, key));

    /// <summary>節の下の階層にあるフォント設定の数（節自身は含まない）。</summary>
    public static int CountFonts(N3FontTreeNode node) => Walk(node.Children).Count(x => x.Node.FontId is not null);

    /// <summary>節の下の階層にあるフォント設定の Id（上から順に。節自身は含まない）。</summary>
    public static List<string> DescendantFontIds(N3FontTreeNode node) => FontOrder(node.Children);

    // ------------------------------------------------------------ 変える

    /// <summary>
    /// 節を外す。<paramref name="keepChildren"/> なら下の節をその位置へ繰り上げる（false なら下の節ごと外す）。
    /// 外した節を返す（見つからなければ null）。
    /// </summary>
    public static N3FontTreeNode? Remove(List<N3FontTreeNode> roots, string key, bool keepChildren)
    {
        if (Find(roots, key) is not { } loc) return null;
        loc.Siblings.RemoveAt(loc.Index);
        if (keepChildren)
        {
            loc.Siblings.InsertRange(loc.Index, loc.Node.Children);
            loc.Node.Children = new();
        }
        return loc.Node;
    }

    /// <summary>
    /// 節を <paramref name="parentKey"/> の節の下（null なら最上位）の <paramref name="index"/> の位置（null なら末尾）に入れる。
    /// 親が見つからなければ false（入れない）。
    /// </summary>
    public static bool Insert(List<N3FontTreeNode> roots, string? parentKey, N3FontTreeNode node, int? index = null)
    {
        List<N3FontTreeNode> list;
        if (parentKey is null)
        {
            list = roots;
        }
        else if (Find(roots, parentKey) is { } parent)
        {
            list = parent.Node.Children;
        }
        else
        {
            return false;
        }
        list.Insert(index is int i ? Math.Clamp(i, 0, list.Count) : list.Count, node);
        return true;
    }

    /// <summary>節を <paramref name="key"/> の節のすぐ後（同じ親の下）に入れる。見つからなければ false（入れない）。</summary>
    public static bool InsertAfter(List<N3FontTreeNode> roots, string key, N3FontTreeNode node)
    {
        if (Find(roots, key) is not { } loc) return false;
        loc.Siblings.Insert(loc.Index + 1, node);
        return true;
    }

    /// <summary>節を同じ親の下で <paramref name="delta"/> だけ動かす（端を越えるときは動かさない）。動いたら true。</summary>
    public static bool MoveWithinSiblings(List<N3FontTreeNode> roots, string key, int delta)
    {
        if (Find(roots, key) is not { } loc) return false;
        int to = loc.Index + delta;
        if (delta == 0 || to < 0 || to >= loc.Siblings.Count) return false;
        loc.Siblings.RemoveAt(loc.Index);
        loc.Siblings.Insert(to, loc.Node);
        return true;
    }

    /// <summary>
    /// 階層を 1 つ下げる: すぐ上の節（同じ親の下の 1 つ前）の下の末尾へ移し、その節を開く。先頭の節は下げられない（false）。
    /// </summary>
    public static bool Indent(List<N3FontTreeNode> roots, string key)
    {
        if (Find(roots, key) is not { } loc || loc.Index == 0) return false;
        var newParent = loc.Siblings[loc.Index - 1];
        loc.Siblings.RemoveAt(loc.Index);
        newParent.Children.Add(loc.Node);
        newParent.Collapsed = false;
        return true;
    }

    /// <summary>階層を 1 つ上げる: 親のすぐ後へ移す。最上位の節は上げられない（false）。</summary>
    public static bool Outdent(List<N3FontTreeNode> roots, string key)
    {
        if (Find(roots, key) is not { Parent: { } parent } loc) return false;
        if (Find(roots, parent.Key) is not { } parentLoc) return false;
        loc.Siblings.RemoveAt(loc.Index);
        parentLoc.Siblings.Insert(parentLoc.Index + 1, loc.Node);
        return true;
    }

    /// <summary>
    /// 節を <paramref name="newParentKey"/> の節の下（null なら最上位）の <paramref name="index"/> の位置（null なら末尾）へ移し、移し先を開く。
    /// 自分自身・自分の下の階層へは移せない。移したら true。
    /// </summary>
    public static bool MoveInto(List<N3FontTreeNode> roots, string key, string? newParentKey, int? index = null)
    {
        if (Find(roots, key) is not { } loc) return false;
        N3FontTreeNode? newParent = null;
        if (newParentKey is not null)
        {
            if (Contains(loc.Node, newParentKey)) return false;
            newParent = Find(roots, newParentKey)?.Node;
            if (newParent is null) return false;
        }
        var list = newParent?.Children ?? roots;
        loc.Siblings.RemoveAt(loc.Index);
        int at = index is int i ? i : list.Count;
        if (ReferenceEquals(list, loc.Siblings) && index is int j && j > loc.Index) at = j - 1; // 同じ一覧で後ろへ移すとき、外した分ずれる
        list.Insert(Math.Clamp(at, 0, list.Count), loc.Node);
        if (newParent is not null) newParent.Collapsed = false;
        return true;
    }

    /// <summary>フォント設定の節が指すフォント設定を付け替える（曲専用のフォント設定でアプリ共通のものを置き換えたとき）。付け替えたら true。</summary>
    public static bool ReplaceFontId(List<N3FontTreeNode> roots, string oldId, string newId)
    {
        if (Find(roots, oldId) is not { Node.FontId: not null } loc) return false;
        loc.Node.FontId = newId;
        return true;
    }

    /// <summary>深いコピー。</summary>
    public static List<N3FontTreeNode> Clone(IEnumerable<N3FontTreeNode> roots) => roots.Select(n => n.Clone()).ToList();

    /// <summary>節の並びと下の階層・フォルダの名前・折りたたみが同じか。</summary>
    public static bool SameShape(IReadOnlyList<N3FontTreeNode> a, IReadOnlyList<N3FontTreeNode> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            var x = a[i];
            var y = b[i];
            if (!string.Equals(x.Key, y.Key, StringComparison.OrdinalIgnoreCase) || x.IsFolder != y.IsFolder
                || x.FolderName != y.FolderName || x.Collapsed != y.Collapsed || !SameShape(x.Children, y.Children))
            {
                return false;
            }
        }
        return true;
    }
}
