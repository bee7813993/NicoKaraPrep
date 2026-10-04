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
}
