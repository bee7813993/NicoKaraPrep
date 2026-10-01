using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.ViewModels;

/// <summary>
/// ニコカラメーカー3 のレイアウト設定: NicoKaraPrep で編集したもの（アプリ共通。<see cref="Core.Project.AppSettings.N3Layouts"/>）と、
/// ベースの n3proj（無ければ書き出しの既定）のものを合わせた一覧、行のページのレイアウトの手動指定。
/// </summary>
public partial class MainViewModel
{
    /// <summary>レイアウト設定の一覧（編集したもの）が変わった（行リストの表示・プレビューを作り直す）。</summary>
    public event EventHandler? LayoutsChanged;

    /// <summary>字幕の画面の幅・高さ（ベースの n3proj があればその背景素材の大きさ、無ければ設定の横幅の 16:9）。</summary>
    public (int Width, int Height) GetScreenSize()
    {
        if (GetBaseProject() is { } bp) return (bp.Width, bp.Height);
        int width = Settings.ScreenWidthPx > 0 ? Settings.ScreenWidthPx : 1920;
        return (width, width == 1920 ? 1080 : (int)Math.Round(width * 9.0 / 16));
    }

    /// <summary>ベースの n3proj のレイアウト設定（無い・読めなければ書き出しの既定のレイアウト）。編集したものは含まない。</summary>
    public List<N3LayoutSettings> GetBaseLayouts()
    {
        var (_, height) = GetScreenSize();
        return GetBaseProject() is { Layouts.Count: > 0 } bp ? bp.Layouts : N3LayoutReader.Defaults(height);
    }

    /// <summary>書き出すプロジェクトのレイアウト設定の並び（ベース＋編集したもの。書き出しと同じ合わせ方）。</summary>
    public List<N3LayoutSettings> GetEffectiveLayouts() =>
        N3LayoutLibrary.Effective(GetBaseLayouts(), Settings.N3Layouts, N3ProjSettings.MergeLayouts);

    /// <summary>編集したレイアウト設定（無ければ null）。</summary>
    public N3Layout? FindEditedLayout(string name) => Settings.N3Layouts.FirstOrDefault(l => l.Name == name);

    /// <summary>ベース（または既定）にある名前か。</summary>
    public bool IsBaseLayout(string name) => GetBaseLayouts().Any(l => l.Name == name);

    /// <summary>
    /// 名前のレイアウト設定を編集できる形で返す（まだ編集していないベースのものは、今の値を写して編集したものの一覧に足す）。
    /// 無い名前なら null。
    /// </summary>
    public N3Layout? EnsureEditableLayout(string name)
    {
        if (FindEditedLayout(name) is { } edited) return edited;
        var source = GetEffectiveLayouts().FirstOrDefault(l => l.Name == name);
        if (source is null) return null;
        var layout = N3Layout.FromSettings(source);
        Settings.N3Layouts.Add(layout);
        return layout;
    }

    /// <summary>編集したレイアウト設定を保存し、行リストの表示・プレビューを作り直すよう知らせる。</summary>
    public void SaveLayouts()
    {
        Settings.Save();
        LayoutsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>レイアウト設定を足す（名前が重なれば末尾に 2, 3… を付ける）。足したものを返す。</summary>
    public N3Layout AddLayout(N3Layout layout)
    {
        layout.Name = UniqueLayoutName(string.IsNullOrWhiteSpace(layout.Name) ? "新しいレイアウト" : layout.Name.Trim());
        Settings.N3Layouts.Add(layout);
        SaveLayouts();
        return layout;
    }

    /// <summary>
    /// 編集したレイアウト設定を消す。ベース（または既定）にある名前ならベースの値に戻り、足したものなら無くなる
    /// （行・タブの指定はそのまま。無い名前は行数から選ぶ）。
    /// </summary>
    public bool RemoveEditedLayout(string name)
    {
        int removed = Settings.N3Layouts.RemoveAll(l => l.Name == name);
        if (removed == 0) return false;
        SaveLayouts();
        return true;
    }

    /// <summary>
    /// 足したレイアウト設定の名前を変える（ベースにある名前は変えられない）。行（全タブ・元に戻すの履歴も）と、タブの固定レイアウトの指定も新しい名前にする。
    /// 変えた名前（重なれば 2, 3… 付き）を返す。変えられなければ null。
    /// </summary>
    public string? RenameLayout(string oldName, string newName)
    {
        newName = newName.Trim();
        if (newName.Length == 0 || newName == oldName || IsBaseLayout(oldName)) return null;
        if (FindEditedLayout(oldName) is not { } layout) return null;
        newName = UniqueLayoutName(newName);
        layout.Name = newName;
        foreach (var tab in GetAllTabs())
        {
            foreach (var doc in tab.UndoStack.Concat(tab.RedoStack).Append(tab.Document))
            {
                foreach (var line in doc.Lines)
                {
                    if (line.LayoutName == oldName) line.LayoutName = newName;
                }
            }
        }
        foreach (var key in N3ProjSettings.TabLayouts.Where(p => p.Value == oldName).Select(p => p.Key).ToList())
        {
            N3ProjSettings.TabLayouts[key] = newName;
        }
        SaveProject();
        SaveLayouts();
        return newName;
    }

    private string UniqueLayoutName(string name)
    {
        var names = new HashSet<string>(GetEffectiveLayouts().Select(l => l.Name), StringComparer.Ordinal);
        if (!names.Contains(name)) return name;
        for (int i = 2; ; i++)
        {
            string candidate = $"{name} {i}";
            if (!names.Contains(candidate)) return candidate;
        }
    }

    /// <summary>
    /// 行のページのレイアウトを手動指定する（null / 空 = 自動）。ニコカラメーカー3 と同じくページ単位なので、指定した行と同じページの行すべてにそろえる。
    /// 元に戻す（Ctrl+Z）は 1 回で戻る。変えた行の数を返す。
    /// </summary>
    public int SetLinesLayout(IReadOnlyList<int> indexes, string? name)
    {
        string? value = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        var show = CreateShowTimeSettings(_activeTab.Name);
        var pages = Document.GetPages(show.PageMode, show.FixedLineCount);
        var targets = new SortedSet<int>();
        foreach (int i in indexes)
        {
            if (i < 0 || i >= Document.Lines.Count || Document.Lines[i].IsEmpty) continue;
            var page = pages.FirstOrDefault(p => p.Contains(i));
            foreach (int k in page ?? new List<int> { i }) targets.Add(k);
        }
        var changed = targets.Where(i => Document.Lines[i].LayoutName != value).ToList();
        if (changed.Count == 0) return 0;
        PushUndo();
        foreach (int i in changed) Document.Lines[i].LayoutName = value;
        MarkModified();
        SaveProject();
        return changed.Count;
    }
}
