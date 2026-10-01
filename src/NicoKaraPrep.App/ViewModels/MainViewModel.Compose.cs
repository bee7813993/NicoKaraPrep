using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.ViewModels;

/// <summary>
/// 絵文字を続けて入れたときの組み合わせフォント（2 人用など）の自動作成。
/// 絵文字を 2 種類以上続けて並べた行（一緒に歌うところ）で、どの絵文字にも同じ名前のフォント設定があり、つなげた名前のフォント設定が
/// まだ無ければ、アプリ共通に作る（確認は出さず、ステータスバーで知らせる。設定で止められる）。
/// フォント設定ビューが開いていれば、外での入れ替えとして一覧を作り直し、ビューの 元に戻す で消せる。
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// 行の中の、続けて並んだ絵文字の組み合わせのフォント設定が無ければ作る。作ったフォント設定の名前を返す（作らなければ空）。
    /// 色は既定の塗り方（ミルフィーユなど）と端の帯の広さで、元のフォント設定のメイン色を並べる。
    /// </summary>
    public List<string> AutoComposeFontsFor(LyricsLine line)
    {
        var created = new List<string>();
        if (!Settings.N3AutoComposeFonts) return created;
        var export = ExportFontSets;
        var fontNames = new HashSet<string>(export.Select(f => f.Name), StringComparer.Ordinal);
        var runs = N3FontComposer.FindMissingRuns(line, GetEffectiveEmojiList().Select(e => e.ReplaceChar), fontNames);
        if (runs.Count == 0) return created;

        var patterns = N3ColorPatterns.All(Settings.N3UserColorPatterns);
        ReplaceCommonFontSets(() =>
        {
            // 階層をまだ作っていなければ（フォント設定ビューを開いたことが無い）、今の一覧を最上位に並べて作ってから入れる
            var tree = Settings.N3FontHierarchy;
            if (tree is null)
            {
                tree = new List<N3FontTreeNode>();
                N3FontTree.Normalize(tree, Settings.N3FontSets, groupImported: false);
                Settings.N3FontHierarchy = tree;
            }
            foreach (var run in runs)
            {
                var sources = run.Select(n => export.First(f => f.Name == n)).ToList();
                var pattern = N3FontComposer.PatternFor(null, sources, patterns);
                var font = N3FontComposer.Create(sources, pattern, Settings.N3ComposeBrushType, Settings.N3ComposeEndWidenPercent);
                N3FontLibrary.Add(Settings.N3FontSets, font);
                N3FontComposer.PlaceInTree(tree, Settings.N3FontSets, font, sources);
                created.Add(font.Name);
            }
            N3FontTree.SortFonts(Settings.N3FontSets, tree);
        });
        Settings.Save();
        return created;
    }

    /// <summary>
    /// 絵文字を入れたあとのステータスバーの表示に、組み合わせフォントを作ったことを足す。挿入のあとのチェックの結果で消えないよう、
    /// 次のチェックの結果の前にも出す（<see cref="_noticeBeforeCheck"/>）。
    /// </summary>
    private void NoteComposedFonts(IReadOnlyList<string> created)
    {
        if (created.Count == 0) return;
        string note = $"組み合わせのフォント設定「{string.Join("」「", created)}」を作りました（フォント設定ビュー（F3）で色の分け方を変えられます）";
        StatusText += $"　／　{note}";
        _noticeBeforeCheck = note;
    }

    /// <summary>次のチェックの結果の前に出すお知らせ（チェックの結果で上書きされて見えなくならないように）。</summary>
    private string? _noticeBeforeCheck;
}
