using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.ViewModels;

/// <summary>フォント設定ビューの取り込み（n3proj・ニコカラメーカー3 のテンプレート）と、範囲（アプリ共通／この曲専用）の変更。</summary>
public sealed partial class FontSettingsViewModel
{
    // ------------------------------------------------------------ 範囲の変更

    /// <summary>選択中のアプリ共通のフォント設定と同じ名前の曲専用のフォント設定（無ければ null）。</summary>
    public FontListItem? FindSongOverride(FontListItem item) =>
        _all.FirstOrDefault(i => i.IsSong && i.Font.Name == item.Font.Name);

    /// <summary>
    /// 選択中のアプリ共通のフォント設定を、この曲専用に写す（名前はそのまま。書き出しでは曲専用が同じ名前の共通より優先される）。
    /// すでに同じ名前の曲専用があれば、写さずにそれを選ぶ。
    /// </summary>
    public void CopySelectedToSong()
    {
        if (SelectedItem is not { IsSong: false } item) return;
        if (FindSongOverride(item) is { } existing)
        {
            Select(existing.Id);
            SetStatus($"この曲専用のフォント設定「{item.Font.Name}」はすでにあります（それを選びました）");
            return;
        }

        PushUndo(null);
        var copy = item.Font.Clone();
        copy.Id = Guid.NewGuid().ToString();
        N3FontLibrary.Add(Song, copy);
        MarkDirty(song: true);
        ClearFilterFor(copy.Id);
        string save = _main.CanSaveSongFontSets ? "" : "（歌詞ファイルを保存すると .tttproj に保存されます）";
        SetStatus($"フォント設定「{copy.Name}」をこの曲専用に写しました。この曲の書き出しでは、アプリ共通の同じ名前より優先されます{save}");
    }

    /// <summary>選択中の曲専用のフォント設定と同じ名前のアプリ共通のフォント設定（無ければ null）。</summary>
    public N3FontSet? FindCommonNamed(FontListItem item) => Common.FirstOrDefault(f => f.Name == item.Font.Name);

    /// <summary>
    /// 選択中の曲専用のフォント設定をアプリ共通へ移す。同じ名前のアプリ共通があるときは、replace が true なら
    /// それを置き換え（位置はそのまま）、false なら何もしない（呼び出し側で確認してから true で呼ぶ）。
    /// </summary>
    public bool MoveSelectedToCommon(bool replace)
    {
        if (SelectedItem is not { IsSong: true } item) return false;
        var font = item.Font;
        int i = Common.FindIndex(f => f.Name == font.Name);
        if (i >= 0 && !replace) return false;

        PushUndo(null);
        Song.Remove(font);
        if (i >= 0)
        {
            Common[i] = font;
        }
        else
        {
            Common.Add(font);
        }
        N3FontLibrary.EnsureIds(Common);
        MarkDirty(song: true);
        MarkDirty(song: false);
        ClearFilterFor(font.Id);
        SetStatus(i >= 0
            ? $"曲専用のフォント設定「{font.Name}」で、アプリ共通の同じ名前のフォント設定を置き換えました"
            : $"曲専用のフォント設定「{font.Name}」をアプリ共通へ移しました");
        return true;
    }

    // ------------------------------------------------------------ 取り込み

    /// <summary>
    /// ニコカラメーカー3 のテンプレートから読んだフォント設定をアプリ共通に追加する（同じ名前があれば末尾に 2, 3… を付ける。
    /// テンプレートとの連動（NkmGuid・NkmSynchronize）はそのまま）。追加した数を返す。
    /// </summary>
    public int AddTemplates(IReadOnlyList<N3FontSet> templates)
    {
        if (templates.Count == 0) return 0;
        PushUndo(null);
        string? firstId = null;
        var renamed = new List<string>();
        foreach (var t in templates)
        {
            var copy = t.Clone();
            copy.Id = Guid.NewGuid().ToString();
            string original = copy.Name;
            N3FontLibrary.Add(Common, copy);
            if (copy.Name != original) renamed.Add($"{original}→{copy.Name}");
            firstId ??= copy.Id;
        }
        MarkDirty(song: false);
        ClearFilterFor(firstId!);
        string note = renamed.Count > 0 ? $"（同じ名前があったため名前を変えたもの: {string.Join("、", renamed.Take(5))}{(renamed.Count > 5 ? " ほか" : "")}）" : "";
        SetStatus($"ニコカラメーカー3 のテンプレートから {templates.Count} 件のフォント設定をアプリ共通に追加しました{note}");
        return templates.Count;
    }

    /// <summary>
    /// n3proj の読み込み確認画面（フォント設定を選んだ状態）でアプリ共通へ取り込む。取り込みの処理は <paramref name="import"/>（メイン画面）に任せ、
    /// 終わったら一覧を作り直す。取り込んだときは 元に戻す で取り込み前に戻せる。
    /// </summary>
    public async Task ImportN3ProjAsync(Func<Task> import)
    {
        FlushPendingSave();
        var before = Snapshot();
        string beforeJson = Signature();
        await import();

        if (Signature() != beforeJson)
        {
            _undo.Add(before);
            if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
            _redo.Clear();
            _coalesceKey = null;
            UpdateUndoState();
        }
        N3FontLibrary.EnsureIds(Common);
        Rebuild(null);
    }

    // ------------------------------------------------------------ 配色のコピー

    /// <summary>ほかのフォント設定の配色を、選択中のフォント設定へ写す（indices は配色の箇所）。</summary>
    public void CopyBrushesFrom(FontListItem source, IReadOnlyList<int> indices)
    {
        if (SelectedFont is not { } font || ReferenceEquals(source.Font, font) || indices.Count == 0) return;
        EditFont(font, null, f => N3FontLibrary.CopyBrushes(source.Font.Detail, f.Detail, indices));
        Editor.Load(SelectedItem);
        string where = indices.Count == N3FontDetail.BrushCount ? "8 箇所すべて" : N3FontDetail.BrushLabels[indices[0]];
        SetStatus($"「{source.Font.Name}」の配色（{where}）を「{font.Name}」に写しました");
    }
}
