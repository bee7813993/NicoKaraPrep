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
    /// それを置き換え（階層の位置はそのまま）、false なら何もしない（呼び出し側で確認してから true で呼ぶ）。
    /// 置き換えないときは、アプリ共通の階層の最上位の末尾に入れる。
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
            N3FontTree.ReplaceFontId(Tree, Common[i].Id, font.Id); // 置き換えたものと同じ場所に置く
            Common[i] = font;
        }
        else
        {
            Common.Add(font);
            Tree.Add(N3FontTreeNode.ForFont(font.Id)); // 取り込み元ごとのフォルダへは振り分けない
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
    /// テンプレートとの連動（NkmGuid・NkmSynchronize）はそのまま）。階層では最上位のフォルダ「ニコカラメーカー3 のテンプレート」
    /// （無ければ作る）の末尾に入れる。追加した数を返す。
    /// </summary>
    public int AddTemplates(IReadOnlyList<N3FontSet> templates)
    {
        if (templates.Count == 0) return 0;
        PushUndo(null);
        string? firstId = null;
        var renamed = new List<string>();
        var folder = N3FontTree.EnsureRootFolder(Tree, N3FontTree.TemplateFolderName);
        folder.Collapsed = false;
        foreach (var t in templates)
        {
            var copy = t.Clone();
            copy.Id = Guid.NewGuid().ToString();
            string original = copy.Name;
            N3FontLibrary.Add(Common, copy);
            folder.Children.Add(N3FontTreeNode.ForFont(copy.Id));
            if (copy.Name != original) renamed.Add($"{original}→{copy.Name}");
            firstId ??= copy.Id;
        }
        MarkDirty(song: false);
        ClearFilterFor(firstId!);
        string note = renamed.Count > 0 ? $"（同じ名前があったため名前を変えたもの: {string.Join("、", renamed.Take(5))}{(renamed.Count > 5 ? " ほか" : "")}）" : "";
        SetStatus($"ニコカラメーカー3 のテンプレートから {templates.Count} 件のフォント設定を、アプリ共通のフォルダ「{N3FontTree.TemplateFolderName}」に追加しました{note}");
        return templates.Count;
    }

    /// <summary>
    /// n3proj の読み込み確認画面（フォント設定を選んだ状態）でアプリ共通へ取り込む。取り込みの処理は <paramref name="import"/>（メイン画面）に任せ、
    /// 終わったら一覧を作り直す（取り込んだ行の指定で使用状況も変わるため）。フォント設定の入れ替えと 元に戻す の記録は、
    /// メニューからの読み込みと同じく <see cref="OnCommonFontSetsReplacing"/>・<see cref="OnCommonFontSetsReplaced"/> で行う。
    /// </summary>
    public async Task ImportN3ProjAsync(Func<Task> import)
    {
        FlushPendingSave();
        await import();
        Rebuild(null);
    }

    /// <summary>外での入れ替えの前の一覧（元に戻す の記録）と、その内容。入れ替えの最中だけ値がある。</summary>
    private (UndoEntry Entry, string Signature)? _externalBefore;

    /// <summary>
    /// アプリ共通のフォント設定が外（n3proj の読み込み。ビューの「取り込み」もメニューも同じ）で入れ替わる直前。
    /// 保存待ちの編集を保存し、元に戻す のために今の一覧を記録する。
    /// </summary>
    private void OnCommonFontSetsReplacing()
    {
        FlushPendingSave();
        _externalBefore = (Snapshot(), Signature());
    }

    /// <summary>
    /// アプリ共通のフォント設定が外で入れ替わったあと。入れ替わったフォント設定は別のオブジェクトになり、
    /// 一覧と編集欄が外れた古いオブジェクトを指したままだと編集が保存されないので、一覧を作り直す。
    /// 変わっていれば、元に戻す で入れ替えの前に戻せるよう記録する。
    /// </summary>
    private void OnCommonFontSetsReplaced()
    {
        var before = _externalBefore;
        _externalBefore = null;
        N3FontLibrary.EnsureIds(Common);
        string now = Signature();
        if (before is { } b && b.Signature != now)
        {
            _undo.Add(b.Entry);
            if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
            _redo.Clear();
            UpdateUndoState();

            // ビューの外での読み込みなら、戻ってきたときに「外で変わった」として履歴を捨てないよう、抜けたときの内容を今の内容にする
            // （抜けたあとにほかの変更もあったときは、そのまま捨てる）
            if (_exitSignature == b.Signature) _exitSignature = now;
        }
        _coalesceKey = null;
        RecomposeAllLinked(); // 取り込みで元のフォント設定が入れ替わっていれば、連動している組み合わせフォントを作り直す
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
