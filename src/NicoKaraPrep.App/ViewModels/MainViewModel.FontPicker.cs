using Microsoft.UI.Xaml.Media;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.ViewModels;

/// <summary>行リストの右のフォント一覧の 1 件（名前と、ワイプ後・前の文字の色見本）。</summary>
public sealed record FontPickItem(string Name, Brush? After, Brush? Before, string ToolTip);

/// <summary>行リストの右のフォント一覧のまとまり（フォルダ・この曲専用・ベースにしか無いもの）。</summary>
public sealed class FontPickGroup : List<FontPickItem>
{
    public FontPickGroup(string key)
    {
        Key = key;
    }

    public string Key { get; }
}

/// <summary>行リストの右のフォント一覧（押すと、選んだ文字・行にそのフォント設定を指定する）。</summary>
public partial class MainViewModel
{
    /// <summary>
    /// 書き出すフォント設定（書き出しの名前の並びにあるもの）を、この曲専用 → アプリ共通のフォルダごと（フォント設定ビューの階層の順）→
    /// フォルダに入っていないもの → ベースの n3proj にしか無いもの、の順にまとめる。<paramref name="filter"/> は名前の絞り込み（空なら全部）。
    /// <paramref name="allOwnFonts"/> は名前を選ぶだけのとき（絵文字のリストの「文字」・定型文のフォント設定。書き出しでまとめない設定でも、
    /// にこぷれっぷのフォント設定を全部出し、フォント設定が無いときの仮の名前「標準」は出さない）。<paramref name="clickHint"/> は項目の説明の 2 行目。
    /// </summary>
    public List<FontPickGroup> BuildFontPickGroups(string? filter, bool allOwnFonts = false, string? clickHint = null)
    {
        var (names, _) = GetExportFontNames();
        var available = new HashSet<string>(names.Where(n => n.Length > 0), StringComparer.Ordinal);
        var own = new Dictionary<string, N3FontSet>(StringComparer.Ordinal);
        if (N3ProjSettings.MergeFontSets || allOwnFonts)
        {
            foreach (var f in ExportFontSets) own.TryAdd(f.Name, f);
        }
        if (allOwnFonts) available.UnionWith(own.Keys.Where(n => n.Length > 0));
        string hint = clickHint ?? "押すと、選んだ文字（文字を選んでいなければ選んだ行）にこのフォント設定を指定します";
        var fromBase = new Dictionary<string, N3FontSet>(StringComparer.Ordinal);
        foreach (var f in GetBaseFontSets()) fromBase.TryAdd(f.Name, f);

        string query = (filter ?? "").Trim();
        var groups = new List<FontPickGroup>();
        var byKey = new Dictionary<string, FontPickGroup>(StringComparer.Ordinal);
        var placed = new HashSet<string>(StringComparer.Ordinal);
        void Add(string group, string name, string where)
        {
            if (!available.Contains(name) || !placed.Add(name)) return;
            if (query.Length > 0 && !name.Contains(query, StringComparison.OrdinalIgnoreCase)) return;
            if (!byKey.TryGetValue(group, out var g))
            {
                g = new FontPickGroup(group);
                byKey[group] = g;
                groups.Add(g);
            }
            own.TryGetValue(name, out var o);
            fromBase.TryGetValue(name, out var b);
            g.Add(new FontPickItem(name, Swatch(o, b, 0), Swatch(o, b, N3FontDetail.BeforeOffset), $"{name}（{where}）\n{hint}"));
        }

        foreach (var f in SongFontSets) Add("この曲専用", f.Name, "この曲専用");

        const string noFolder = "（フォルダなし）";
        var byId = new Dictionary<string, N3FontSet>(StringComparer.Ordinal);
        foreach (var f in Settings.N3FontSets) byId.TryAdd(f.Id, f);
        string current = noFolder;
        foreach (var (node, depth) in N3FontTree.Walk(Settings.N3FontHierarchy ?? new List<N3FontTreeNode>()))
        {
            if (depth == 0) current = node.FolderName ?? noFolder;
            if (node.FontId is { } id && byId.TryGetValue(id, out var font)) Add(current, font.Name, "アプリ共通");
        }
        foreach (var f in Settings.N3FontSets) Add(noFolder, f.Name, "アプリ共通");
        foreach (string n in names)
        {
            // 名前を選ぶだけのときは、フォント設定が無いときの仮の名前（「標準」）は出さない
            if (!allOwnFonts || fromBase.ContainsKey(n)) Add("ベースの n3proj だけにあるもの", n, "ベースの n3proj");
        }
        return groups;
    }

    /// <summary>「この曲専用」のまとまりの <see cref="FontPickRow.Key"/>。</summary>
    public const string SongFontGroupKey = "song";

    /// <summary>「ベースの n3proj だけにあるもの」のまとまりの <see cref="FontPickRow.Key"/>。</summary>
    public const string BaseFontGroupKey = "base";

    /// <summary>
    /// 行リストの右のフォント一覧の行（<see cref="BuildFontPickGroups"/> と同じフォント設定を、フォント設定ビュー（F3）と同じ入れ子の階層で並べる）。
    /// この曲専用のまとまり → アプリ共通の階層（フォルダと、下にフォント設定を持つフォント設定は畳める）→ 階層に無いもの → ベースの n3proj だけにあるもの、の順。
    /// <paramref name="collapsed"/> は畳んでいるまとまりの Key（右のパネルだけで覚える。F3 の折りたたみとは別）。名前で絞り込んでいるあいだは、
    /// 畳んだまとまりも広げて、当てはまるフォント設定とそれを含むまとまりだけを出す。
    /// </summary>
    public List<FontPickRow> BuildFontPickRows(string? filter, IReadOnlyCollection<string> collapsed)
    {
        var (names, _) = GetExportFontNames();
        var available = new HashSet<string>(names.Where(n => n.Length > 0), StringComparer.Ordinal);
        var own = new Dictionary<string, N3FontSet>(StringComparer.Ordinal);
        if (N3ProjSettings.MergeFontSets)
        {
            foreach (var f in ExportFontSets) own.TryAdd(f.Name, f);
        }
        const string hint = "押すと、選んだ文字（文字を選んでいなければ選んだ行）にこのフォント設定を指定します";
        var fromBase = new Dictionary<string, N3FontSet>(StringComparer.Ordinal);
        foreach (var f in GetBaseFontSets()) fromBase.TryAdd(f.Name, f);

        string query = (filter ?? "").Trim();
        bool searching = query.Length > 0;
        var placed = new HashSet<string>(StringComparer.Ordinal);

        // 名前を一覧に出すフォント設定の行（書き出しに無い・前に出した・絞り込みに当てはまらないものは null）
        FontPickRow? FontRow(string name, string where, int depth, string key)
        {
            if (!available.Contains(name) || !placed.Add(name)) return null;
            if (searching && !name.Contains(query, StringComparison.OrdinalIgnoreCase)) return null;
            own.TryGetValue(name, out var o);
            fromBase.TryGetValue(name, out var b);
            var item = new FontPickItem(name, Swatch(o, b, 0), Swatch(o, b, N3FontDetail.BeforeOffset), $"{name}（{where}）\n{hint}");
            return FontPickRow.ForFont(key, item, depth);
        }

        // まとまりの見出しと中身（中に出すフォント設定が無ければ何も出さない。畳んでいれば見出しだけ）。中のフォント設定の数（畳んだものも数える）を返す
        int AddGroup(List<FontPickRow> rows, string key, string name, int depth, Built children, string? toolTip = null)
        {
            if (children.Fonts == 0) return 0;
            bool isCollapsed = !searching && collapsed.Contains(key);
            rows.Add(FontPickRow.ForGroup(key, name, depth, children.Fonts, isCollapsed, searching, toolTip));
            if (!isCollapsed) rows.AddRange(children.Rows);
            return children.Fonts;
        }

        static Built Join(IEnumerable<Built> parts)
        {
            var rows = new List<FontPickRow>();
            int fonts = 0;
            foreach (var p in parts)
            {
                rows.AddRange(p.Rows);
                fonts += p.Fonts;
            }
            return new Built(rows, fonts);
        }

        var result = new List<FontPickRow>();

        // この曲専用
        var song = new List<FontPickRow>();
        foreach (var f in SongFontSets)
        {
            if (FontRow(f.Name, "この曲専用", 1, "song:" + f.Name) is { } row) song.Add(row);
        }
        AddGroup(result, SongFontGroupKey, "この曲専用", 0, new Built(song, song.Count),
            "この曲専用のフォント設定（この曲の .tttproj に保存。新規作成・ほかの曲を開くと出なくなります）");

        // アプリ共通の階層（フォルダ・下にフォント設定を持つフォント設定は畳める）
        var byId = new Dictionary<string, N3FontSet>(StringComparer.Ordinal);
        foreach (var f in Settings.N3FontSets) byId.TryAdd(f.Id, f);
        Built Node(N3FontTreeNode node, int depth)
        {
            var rows = new List<FontPickRow>();
            if (node.IsFolder)
            {
                var children = Join(node.Children.Select(c => Node(c, depth + 1)).ToList());
                int n = AddGroup(rows, node.FolderId ?? "", node.FolderName ?? N3FontTree.UnnamedFolderName, depth, children);
                return new Built(rows, n);
            }
            var self = node.FontId is { } id && byId.TryGetValue(id, out var font) ? FontRow(font.Name, "アプリ共通", depth, id) : null;
            if (self is null)
            {
                // 一覧に出さないフォント設定の下にあるものは、その位置に繰り上げて出す
                return Join(node.Children.Select(c => Node(c, depth)).ToList());
            }
            var below = Join(node.Children.Select(c => Node(c, depth + 1)).ToList());
            if (below.Fonts == 0)
            {
                rows.Add(self);
                return new Built(rows, 1);
            }
            bool isCollapsed = !searching && collapsed.Contains(self.Key);
            rows.Add(self.WithChildren(below.Fonts, isCollapsed, searching));
            if (!isCollapsed) rows.AddRange(below.Rows);
            return new Built(rows, 1 + below.Fonts);
        }
        foreach (var node in Settings.N3FontHierarchy ?? new List<N3FontTreeNode>()) result.AddRange(Node(node, 0).Rows);
        foreach (var f in Settings.N3FontSets)
        {
            if (FontRow(f.Name, "アプリ共通", 0, f.Id) is { } row) result.Add(row);
        }

        // ベースの n3proj だけにあるもの
        var baseOnly = new List<FontPickRow>();
        foreach (string n in names)
        {
            if (FontRow(n, "ベースの n3proj", 1, "base:" + n) is { } row) baseOnly.Add(row);
        }
        AddGroup(result, BaseFontGroupKey, "ベースの n3proj だけにあるもの", 0, new Built(baseOnly, baseOnly.Count),
            "書き出しのベースの n3proj にだけあるフォント設定（にこぷれっぷでは編集していないもの）");
        return result;
    }

    /// <summary>階層の一部を並べた行と、その中のフォント設定の数（畳んで行に出していないものも数える）。</summary>
    private sealed record Built(List<FontPickRow> Rows, int Fonts);
}

/// <summary>
/// 行リストの右のフォント一覧の 1 行。まとまりの見出し（フォルダ・この曲専用・ベースだけ）か、フォント設定（下にフォント設定を持つものは畳める）。
/// </summary>
public sealed class FontPickRow
{
    /// <summary>字下げの 1 段の幅 px。</summary>
    private const double IndentStep = 14;

    private FontPickRow(string key, bool isHeader, string name, int depth)
    {
        Key = key;
        IsHeader = isHeader;
        Name = name;
        Depth = depth;
    }

    /// <summary>行を見分ける値（フォルダの Id・フォント設定の Id・<c>song</c>・<c>base</c> など。畳んだまとまりを覚えるのにも使う）。</summary>
    public string Key { get; }

    /// <summary>まとまりの見出しか（false はフォント設定）。</summary>
    public bool IsHeader { get; }

    public bool IsFont => !IsHeader;

    public string Name { get; }

    /// <summary>階層の深さ（0 が最上位）。</summary>
    public int Depth { get; }

    /// <summary>フォント設定の行の中身（見出しは null）。</summary>
    public FontPickItem? Font { get; private init; }

    /// <summary>畳める（見出しか、下にフォント設定を持つフォント設定）。</summary>
    public bool CanToggle { get; private init; }

    public bool IsCollapsed { get; private init; }

    /// <summary>名前で絞り込み中（畳めない）。</summary>
    public bool IsSearching { get; private init; }

    /// <summary>中のフォント設定の数（見出しと、下にフォント設定を持つフォント設定）。</summary>
    public int Count { get; private init; }

    private string? _groupToolTip;

    public Microsoft.UI.Xaml.Thickness IndentMargin => new(Depth * IndentStep, 0, 0, 0);

    /// <summary>畳む印（▼ 広げている・▶ 畳んでいる）。</summary>
    public string ChevronGlyph => IsCollapsed ? "" : "";

    public string CountText => Count > 0 ? $"{Count}" : "";

    public Microsoft.UI.Xaml.Media.Brush? After => Font?.After;

    public Microsoft.UI.Xaml.Media.Brush? Before => Font?.Before;

    public string ToolTip => IsHeader
        ? $"{Name}（{Count} 件）\n{(_groupToolTip is { Length: > 0 } t ? t + "\n" : "")}" +
          (IsSearching ? "名前で絞り込んでいるあいだは、畳まずに出します" : IsCollapsed ? "押すと広げます" : "押すと畳みます（畳んだまとまりは覚えます。フォント設定ビュー（F3）の折りたたみとは別）")
        : Font?.ToolTip ?? Name;

    /// <summary>畳む印のボタンの説明（下にフォント設定を持つフォント設定）。</summary>
    public string ToggleToolTip => IsSearching ? "名前で絞り込んでいるあいだは、畳まずに出します"
        : IsCollapsed ? $"下の {Count} 件を広げます" : $"下の {Count} 件を畳みます";

    /// <summary>一覧の作り直しを省くための中身の目印（見た目に関わる値と色見本のオブジェクト）。</summary>
    public string Signature => $"{Key}|{IsHeader}|{Name}|{Depth}|{CanToggle}|{IsCollapsed}|{IsSearching}|{Count}|" +
        $"{(Font?.After is { } a ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(a) : 0)}|" +
        $"{(Font?.Before is { } b ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(b) : 0)}|{Font?.ToolTip}";

    public static FontPickRow ForFont(string key, FontPickItem item, int depth) => new(key, false, item.Name, depth) { Font = item };

    public static FontPickRow ForGroup(string key, string name, int depth, int count, bool collapsed, bool searching, string? toolTip) =>
        new(key, true, name, depth) { CanToggle = true, IsCollapsed = collapsed, IsSearching = searching, Count = count, _groupToolTip = toolTip };

    /// <summary>下にフォント設定を持つフォント設定の行（畳める）。</summary>
    public FontPickRow WithChildren(int count, bool collapsed, bool searching) =>
        new(Key, false, Name, Depth) { Font = Font, CanToggle = true, IsCollapsed = collapsed, IsSearching = searching, Count = count };

    /// <summary>読み上げ・UI オートメーションでの名前（見出しは畳んだ状態と件数を付ける）。</summary>
    public override string ToString() => IsHeader ? $"{(IsCollapsed ? "▶" : "▼")} {Name}（{Count}）" : Name;
}
