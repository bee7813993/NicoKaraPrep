using NicoKaraPrep.Core.Model;
using NicoKaraPrep.Core.Project;

namespace NicoKaraPrep.App.ViewModels;

/// <summary>まとめて消す種類（フォント設定ビューの「まとめて消す...」で選ぶ）。</summary>
/// <param name="Common">アプリ共通のフォント設定とフォルダ分け。</param>
/// <param name="Song">この曲専用のフォント設定。</param>
/// <param name="Patterns">自作の配色パターン（新しいフォント設定に使うパターンが自作なら標準に戻す）。</param>
/// <param name="RecentColors">最近使った色。</param>
public sealed record FontClearChoice(bool Common, bool Song, bool Patterns, bool RecentColors)
{
    public bool Any => Common || Song || Patterns || RecentColors;
}

/// <summary>
/// フォント設定ビューの「まとめて消す」と「消す前の控えから戻す」（説明の動画を撮るときなどに、まっさらにして、あとで戻す）。
/// 消す前に、消す種類の内容を控え（設定フォルダの FontBackups）に保存する。控えを保存できなければ消さない。
/// フォント設定とフォルダ分けは ビューの 元に戻す でも戻せる（配色パターンと最近使った色は控えから戻す）。
/// </summary>
public sealed partial class FontSettingsViewModel
{
    /// <summary>アプリ共通のフォント設定の数。</summary>
    public int CommonFontCount => Common.Count;

    /// <summary>アプリ共通のフォルダの数。</summary>
    public int CommonFolderCount => N3FontTree.Walk(Tree).Count(x => x.Node.IsFolder);

    /// <summary>この曲専用のフォント設定の数。</summary>
    public int SongFontCount => Song.Count;

    /// <summary>自作の配色パターンの数。</summary>
    public int UserPatternCount => _main.Settings.N3UserColorPatterns.Count;

    /// <summary>最近使った色の数。</summary>
    public int RecentColorCount => RecentColors().Count;

    /// <summary>
    /// 選んだ種類をまとめて消す。消す前に控えを保存し、そのパスを返す（消すものが無ければ null）。
    /// 控えを保存できなければ例外を投げ、何も消さない。
    /// </summary>
    public string? ClearFonts(FontClearChoice choice)
    {
        FlushPendingSave();
        var backup = new N3FontBackup();
        if (choice.Common && (Common.Count > 0 || Tree.Count > 0))
        {
            backup.CommonFonts = Common.Select(f => f.Clone()).ToList();
            backup.Hierarchy = N3FontTree.Clone(Tree);
        }
        if (choice.Song && Song.Count > 0)
        {
            backup.SongFonts = Song.Select(f => f.Clone()).ToList();
            backup.SongPath = _main.MainFilePath;
        }
        if (choice.Patterns && UserPatternCount > 0)
        {
            backup.UserColorPatterns = _main.Settings.N3UserColorPatterns.ToList();
            backup.DefaultColorPatternId = DefaultPatternId;
        }
        if (choice.RecentColors && RecentColorCount > 0)
        {
            backup.RecentColors = (_main.Settings.N3RecentColorHistory ?? new List<string>()).ToList();
        }
        if (backup.IsEmpty)
        {
            SetStatus("消すものはありませんでした");
            return null;
        }

        string path = backup.Save(N3FontBackup.DefaultFolder);

        if (backup.CommonFonts is not null || backup.SongFonts is not null) PushUndo(null);
        if (backup.CommonFonts is not null)
        {
            Common.Clear();
            Tree.Clear();
            MarkDirty(song: false);
        }
        if (backup.SongFonts is not null)
        {
            Song.Clear();
            MarkDirty(song: true);
        }
        if (backup.UserColorPatterns is not null)
        {
            // 新しいフォント設定に使うパターンが自作のものだったら、標準（メイン色の反転）に戻す
            string def = N3ColorPatterns.BuiltIns.Any(p => p.Id == DefaultPatternId) || DefaultPatternId == N3ColorPatterns.NoneId
                ? DefaultPatternId
                : N3ColorPatterns.CharaInverseId;
            SaveColorPatterns(new List<N3ColorPattern>(), def);
        }
        if (backup.RecentColors is not null)
        {
            _main.Settings.N3RecentColorHistory = new List<string>();
            MarkDirty(song: false);
        }
        Rebuild(null);
        FlushPendingSave();

        bool undoable = backup.CommonFonts is not null || backup.SongFonts is not null;
        SetStatus($"{backup.Describe()} を消しました（控え: {Path.GetFileName(path)}。一覧の下の「控えから戻す...」で戻せます{(undoable ? "。フォント設定は Ctrl+Z でも戻せます" : "")}）");
        return path;
    }

    /// <summary>消す前の控えの一覧（新しい順）。</summary>
    public List<(string Path, N3FontBackup Backup)> ListBackups() => N3FontBackup.List(N3FontBackup.DefaultFolder);

    /// <summary>控えのこの曲専用のフォント設定を、今開いている曲へ戻せるか（控えを取った曲を開いているか）。</summary>
    public bool CanRestoreSongFonts(N3FontBackup backup) =>
        backup.SongFonts is not null &&
        backup.SongPath is { Length: > 0 } p &&
        _main.MainFilePath is { } current &&
        string.Equals(Path.GetFullPath(p), Path.GetFullPath(current), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 控えの内容に戻す（控えが持っている種類を、今の内容と置き換える）。この曲専用のフォント設定は、控えを取った曲を開いているときだけ戻す。
    /// フォント設定とフォルダ分けは ビューの 元に戻す で戻す前に戻せる。
    /// </summary>
    public void RestoreBackup(N3FontBackup backup)
    {
        FlushPendingSave();
        bool song = CanRestoreSongFonts(backup);
        if (backup.CommonFonts is not null || song) PushUndo(null);
        if (backup.CommonFonts is not null)
        {
            Common.Clear();
            Common.AddRange(backup.CommonFonts.Select(f => f.Clone()));
            N3FontLibrary.EnsureIds(Common);
            Tree.Clear();
            Tree.AddRange(N3FontTree.Clone(backup.Hierarchy ?? new List<N3FontTreeNode>()));
            MarkDirty(song: false);
        }
        if (song)
        {
            Song.Clear();
            Song.AddRange(backup.SongFonts!.Select(f => f.Clone()));
            N3FontLibrary.EnsureIds(Song);
            MarkDirty(song: true);
        }
        if (backup.UserColorPatterns is not null)
        {
            SaveColorPatterns(backup.UserColorPatterns.ToList(), backup.DefaultColorPatternId ?? DefaultPatternId);
        }
        if (backup.RecentColors is not null)
        {
            _main.Settings.N3RecentColorHistory = backup.RecentColors.ToList();
            MarkDirty(song: false);
        }
        Rebuild(null);
        FlushPendingSave();

        string skipped = backup.SongFonts is not null && !song
            ? $"。この曲専用のフォント設定は、控えを取った曲（{(backup.SongPath is { Length: > 0 } p ? Path.GetFileName(p) : "保存していない曲")}）を開いていないので戻していません"
            : "";
        string undo = backup.CommonFonts is not null || song ? "（フォント設定は Ctrl+Z で戻す前に戻せます）" : "";
        SetStatus($"控え（{backup.CreatedUtc.ToLocalTime():yyyy/MM/dd HH:mm}）から戻しました{skipped}{undo}");
    }
}
