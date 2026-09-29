using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.ViewModels;

/// <summary>
/// ニコカラメーカー3 のフォント設定のうち、曲ごとに持つもの（曲専用のフォント設定）と、
/// フォント設定ビューが使う全タブの参照の集計・更新。
/// アプリ共通のフォント設定は <see cref="NicoKaraPrep.Core.Project.AppSettings.N3FontSets"/>。
/// </summary>
public partial class MainViewModel
{
    private List<N3FontSet> _songFontSets = new();

    /// <summary>
    /// この曲専用のフォント設定（.tttproj の FontSets）。書き出しでは同じ名前のアプリ共通のフォント設定より優先する。
    /// ファイルを開く・新規作成で一覧ごと入れ替わる（そのとき PropertyChanged を出す）。中身の編集は一覧をそのまま変更する。
    /// </summary>
    public List<N3FontSet> SongFontSets
    {
        get => _songFontSets;
        private set
        {
            _songFontSets = value ?? new();
            N3FontLibrary.EnsureIds(_songFontSets);
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 表示中の文書を別の文書に入れ替える直前（ファイルを開く・新規作成・貼り付けで取り込む）に発生する。
    /// フォント設定ビューは、保存待ちの曲専用のフォント設定をここで前の曲の .tttproj へ保存する。
    /// </summary>
    public event EventHandler? DocumentReplacing;

    /// <summary>
    /// アプリ共通のフォント設定を、フォント設定ビューの外（n3proj の読み込み）で入れ替える直前に発生する。
    /// フォント設定ビューは、保存待ちの編集をここで保存し、元に戻す のために今の一覧を記録する。
    /// </summary>
    public event EventHandler? CommonFontSetsReplacing;

    /// <summary>
    /// アプリ共通のフォント設定を入れ替えたあと（<see cref="CommonFontSetsReplacing"/> と対で必ず発生する）。
    /// 入れ替えたフォント設定は別のオブジェクトになるので、フォント設定ビューはここで一覧を作り直す。
    /// </summary>
    public event EventHandler? CommonFontSetsReplaced;

    /// <summary>アプリ共通のフォント設定を入れ替える（前後に <see cref="CommonFontSetsReplacing"/>・<see cref="CommonFontSetsReplaced"/> を出す）。</summary>
    private void ReplaceCommonFontSets(Action replace)
    {
        CommonFontSetsReplacing?.Invoke(this, EventArgs.Empty);
        try
        {
            replace();
        }
        finally
        {
            CommonFontSetsReplaced?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>n3proj の書き出しに使うフォント設定（アプリ共通を曲専用で置き換えたもの）。</summary>
    public List<N3FontSet> ExportFontSets => N3FontLibrary.ResolveForExport(Settings.N3FontSets, SongFontSets);

    /// <summary>曲専用のフォント設定を .tttproj に保存できるか（メインの歌詞ファイルが保存されているか）。</summary>
    public bool CanSaveSongFontSets => MainFilePath is not null;

    /// <summary>すべてのタブ（メインが先頭。表示中のタブの内容を書き戻してから返す）。</summary>
    public IReadOnlyList<TabState> GetAllTabs()
    {
        StoreActiveTab();
        return Tabs.ToList();
    }

    /// <summary>行（全タブ）と n3proj 書き出し設定の既定フォントが参照しているフォント設定名。</summary>
    public HashSet<string> CollectReferencedFontNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tab in GetAllTabs())
        {
            foreach (var line in tab.Document.Lines)
            {
                if (line.FontSetName is { Length: > 0 } n) names.Add(n);
            }
        }
        if (N3ProjSettings.DefaultFontSetName is { Length: > 0 } d) names.Add(d);
        return names;
    }

    private (string Path, DateTime Stamp, HashSet<string> Names)? _baseFontNamesCache;

    /// <summary>
    /// 書き出しのベースにする n3proj（<see cref="SuggestN3ProjBasePath"/>）にあるフォント設定名。ベースが無い・読めなければ空。
    /// 行の手動指定はベースのフォント設定名も選べるので、参照切れの検証ではこれも「ある名前」として扱う。
    /// ファイルのパスと更新日時が同じあいだは読み直さない。
    /// </summary>
    public IReadOnlySet<string> GetBaseFontNames()
    {
        string? path = SuggestN3ProjBasePath();
        if (path is null) return new HashSet<string>();
        try
        {
            var stamp = File.GetLastWriteTimeUtc(path);
            if (_baseFontNamesCache is { } c && c.Path == path && c.Stamp == stamp) return c.Names;
            var names = new HashSet<string>(N3ProjFormat.Read(path).FontSetNames, StringComparer.Ordinal);
            _baseFontNamesCache = (path, stamp, names);
            return names;
        }
        catch (Exception)
        {
            return new HashSet<string>(); // ベースが読めなくても検証は続ける
        }
    }

    /// <summary>
    /// フォント設定名の変更に合わせて、行（全タブ）の手動指定と n3proj 書き出し設定の既定フォントの参照を新しい名前にする。
    /// 歌詞の 元に戻す・やり直し の履歴の中の行も新しい名前にする（名前の変更は歌詞の編集ではないので、
    /// 行リストで Ctrl+Z をしても、もう無い古い名前に戻って参照が切れることが無いように）。
    /// 変わった行の数を返す（既定フォントだけが変わったときは 0。履歴の中の行は数えない）。
    /// </summary>
    public int RenameFontReferences(string oldName, string newName)
    {
        if (oldName.Length == 0 || oldName == newName) return 0;
        int count = 0;
        foreach (var tab in GetAllTabs())
        {
            foreach (var doc in tab.UndoStack.Concat(tab.RedoStack)) RenameFontReferences(doc, oldName, newName);
            int changed = RenameFontReferences(tab.Document, oldName, newName);
            if (changed == 0) continue;
            tab.IsModified = true;
            if (tab == _activeTab) MarkModified();
            count += changed;
        }

        bool defaultChanged = N3ProjSettings.DefaultFontSetName == oldName;
        if (defaultChanged) N3ProjSettings.DefaultFontSetName = newName;
        if (count > 0 || defaultChanged) SaveProject();
        return count;
    }

    /// <summary>1 つの文書の行の手動指定を新しい名前にし、変わった行の数を返す。</summary>
    private static int RenameFontReferences(LyricsDocument doc, string oldName, string newName)
    {
        int changed = 0;
        foreach (var line in doc.Lines)
        {
            if (line.FontSetName != oldName) continue;
            line.FontSetName = newName.Length > 0 ? newName : null;
            changed++;
        }
        return changed;
    }

    /// <summary>文書の入れ替えを知らせ、曲専用のフォント設定を空にする（LoadDocument の最初に呼ぶ）。</summary>
    private void ResetSongFontSets()
    {
        DocumentReplacing?.Invoke(this, EventArgs.Empty);
        SongFontSets = new();
    }
}
