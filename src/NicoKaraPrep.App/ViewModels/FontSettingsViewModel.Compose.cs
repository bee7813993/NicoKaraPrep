using Microsoft.UI.Dispatching;
using NicoKaraPrep.Core.Model;
using NicoKaraPrep.Core.Validation;

namespace NicoKaraPrep.App.ViewModels;

/// <summary>
/// フォント設定ビューの組み合わせフォント（複数人で歌うパート用）と、見づらい配色（文字と縁の明るさが近い）の候補。
/// 組み合わせフォントは元のフォント設定の色が変わると作り直す（連動）。色を手で変えたら連動を外す。
/// </summary>
public sealed partial class FontSettingsViewModel
{
    /// <summary>組み合わせの設定を変えている最中か（そのあいだの編集では、手で色を変えたとみなして連動を外さない）。</summary>
    private bool _composing;

    /// <summary>アプリ共通・この曲専用のすべてのフォント設定（組み合わせの元を探すのに使う）。</summary>
    private List<N3FontSet> AllFonts => Common.Concat(Song).ToList();

    /// <summary>組み合わせフォントの塗りの種類の既定。</summary>
    public int ComposeBrushType => _main.Settings.N3ComposeBrushType;

    /// <summary>組み合わせフォントの端の帯の広さの既定（%）。</summary>
    public double ComposeEndWidenPercent => _main.Settings.N3ComposeEndWidenPercent;

    /// <summary>絵文字を続けて入れたときに組み合わせフォントを自動で作るか。</summary>
    public bool AutoComposeFonts => _main.Settings.N3AutoComposeFonts;

    /// <summary>Id でフォント設定を探す（アプリ共通・この曲専用。無ければ null）。</summary>
    public N3FontSet? FindFont(string id) => N3FontLibrary.Find(AllFonts, id);

    // ------------------------------------------------------------ 作る・変える

    /// <summary>
    /// 元のフォント設定を並べて組み合わせフォントを作り、選ぶ（アプリ共通。名前は元の名前を並びどおりにつなげたもの）。
    /// saveDefaults なら塗り方と帯の広さを、絵文字から自動で作るときの既定にする。
    /// </summary>
    public void CreateComposedFont(IReadOnlyList<N3FontSet> sources, int brushType, double endWidenPercent, bool saveDefaults, bool autoCompose)
    {
        if (sources.Count < 2) return;
        if (saveDefaults)
        {
            _main.Settings.N3ComposeBrushType = brushType;
            _main.Settings.N3ComposeEndWidenPercent = endWidenPercent;
        }
        _main.Settings.N3AutoComposeFonts = autoCompose;

        PushUndo(null);
        var pattern = N3FontComposer.PatternFor(null, sources, Patterns);
        var font = N3FontComposer.Create(sources, pattern, brushType, endWidenPercent);
        string wanted = font.Name;
        N3FontLibrary.Add(Common, font);
        N3FontComposer.PlaceInTree(Tree, Common, font, sources);
        MarkDirty(song: false);
        ClearFilterFor(font.Id);
        string renamed = font.Name != wanted ? $"（「{wanted}」は使われているため「{font.Name}」にしました。歌詞の記号と同じ名前にしないと当たりません）" : "";
        SetStatus($"組み合わせのフォント設定「{font.Name}」を作りました{renamed}");
    }

    /// <summary>組み合わせの設定（塗り方・帯の広さ・連動）を変えて、recompose なら配色を作り直す。</summary>
    public void UpdateComposition(N3FontSet font, string key, Action<N3FontComposition> change, bool recompose = true)
    {
        if (font.Composition is null) return;
        _composing = true;
        try
        {
            EditFont(font, key, f =>
            {
                change(f.Composition!);
                if (recompose) N3FontComposer.Recompose(f, AllFonts, Patterns);
            });
        }
        finally
        {
            _composing = false;
        }
        if (ReferenceEquals(SelectedFont, font)) Editor.ReloadBrushes();
    }

    /// <summary>選択中の組み合わせフォントを、ふつうのフォント設定にする（今の色のまま。元のフォント設定とのつながりを切る）。</summary>
    public void DetachComposition()
    {
        if (SelectedFont is not { Composition: not null } font) return;
        _composing = true;
        try
        {
            EditFont(font, null, f => f.Composition = null);
        }
        finally
        {
            _composing = false;
        }
        Editor.RefreshState();
        SetStatus($"「{font.Name}」を元のフォント設定から切り離しました（今の色のまま、ふつうのフォント設定になりました。Ctrl+Z で戻せます）");
    }

    /// <summary>
    /// フォント設定を編集したあと（<see cref="EditFont"/> から）: 組み合わせフォントの色を手で変えたら連動を外し、
    /// 元のフォント設定の色が変わったら、連動している組み合わせフォントを作り直す。
    /// </summary>
    private void AfterFontEdited(N3FontSet font)
    {
        if (_composing) return;
        var all = AllFonts;
        if (font.Composition is { Linked: true } c && !N3FontComposer.MatchesSources(font, all, Patterns))
        {
            c.Linked = false;
            SetStatus($"「{font.Name}」の色を手で変えたので、元のフォント設定との連動を外しました（組み合わせの欄で連動に戻せます）");
        }
        int n = RecomposeDependents(font, all, new HashSet<N3FontSet>(ReferenceEqualityComparer.Instance) { font });
        if (n > 0) SetStatus($"「{font.Name}」を使っている組み合わせのフォント設定 {n} 件の色も作り直しました");
    }

    /// <summary>font を元にしている、連動した組み合わせフォントを作り直す（組み合わせの組み合わせもたどる）。作り直した数を返す。</summary>
    private int RecomposeDependents(N3FontSet font, List<N3FontSet> all, HashSet<N3FontSet> visited)
    {
        int count = 0;
        foreach (var f in all)
        {
            if (visited.Contains(f) || f.Composition is not { Linked: true } c) continue;
            if (!c.SourceIds.Contains(font.Id, StringComparer.OrdinalIgnoreCase)) continue;
            if (N3FontComposer.MatchesSources(f, all, Patterns)) continue;
            if (!N3FontComposer.Recompose(f, all, Patterns)) continue;
            visited.Add(f);
            N3FontLibrary.MarkEdited(f);
            _all.FirstOrDefault(i => ReferenceEquals(i.Font, f))?.Refresh();
            MarkDirty(song: Song.Contains(f));
            count++;
            count += RecomposeDependents(f, all, visited);
        }
        return count;
    }

    /// <summary>
    /// 連動しているすべての組み合わせフォントを、元のフォント設定の今の色で作り直す（外での取り込みで元が入れ替わったとき）。
    /// </summary>
    private void RecomposeAllLinked()
    {
        var all = AllFonts;
        foreach (var f in all)
        {
            if (f.Composition is not { Linked: true } || N3FontComposer.MatchesSources(f, all, Patterns)) continue;
            if (N3FontComposer.Recompose(f, all, Patterns))
            {
                N3FontLibrary.MarkEdited(f);
                MarkDirty(song: Song.Contains(f));
            }
        }
    }

    // ------------------------------------------------------------ 見づらい配色

    /// <summary>
    /// 見づらい配色を直す色の候補を当てる。候補の箇所が配色パターンの役割に入っていて、まとめて変える設定なら、
    /// 同じ役割の箇所もまとめて同じ色にする（不透明度はそのまま）。
    /// </summary>
    public void ApplyContrastSuggestion(N3ContrastSuggestion suggestion)
    {
        if (SelectedFont is not { } font) return;
        var slots = ContrastTargetSlots(font, suggestion.Slot);
        string? pin = font.ColorPatternId is null ? PatternOf(font)?.Pattern.Id : null;
        EditFont(font, null, f =>
        {
            foreach (int s in slots)
            {
                var b = f.Detail.Brushes[s];
                f.Detail.Brushes[s] = new N3Brush { Type = N3Brush.TypeSolid, Color = suggestion.Color, AlphaPercent = b.Type == N3Brush.TypeSolid ? b.AlphaPercent : 100 };
            }
            if (pin is not null) f.ColorPatternId ??= pin;
        });
        Editor.ReloadBrushes();
        SetStatus($"{N3ColorPatterns.Describe(slots)}を #{suggestion.Color} にしました（コントラスト比 {suggestion.Ratio:0.0}。Ctrl+Z で戻せます）");
    }

    /// <summary>見づらい組（文字と縁の明るさが近い）の文字の箇所（編集の前後で比べ、新しくできた組を知らせるのに使う）。</summary>
    private static HashSet<int> LowContrastSlots(N3FontSet font) => N3Contrast.Check(font).Select(i => i.TextSlot).ToHashSet();

    /// <summary>
    /// 編集で見づらい組が新しくできたら、ステータスバーで知らせる（色の編集欄を下へ送っていて、配色の注意の欄が見えていなくても
    /// 気づけるように）。編集のあとで呼び出し側がステータスを書き換えても消えないよう、処理が終わってから出す
    /// （そのあいだにステータスが変わっていれば、後ろに足す）。
    /// </summary>
    private void NoteNewLowContrast(N3FontSet font, HashSet<int> before, string statusBefore)
    {
        var added = N3Contrast.Check(font).Where(i => !before.Contains(i.TextSlot)).ToList();
        if (added.Count == 0) return;
        string pairs = string.Join("・", added.Select(i => $"{N3FontDetail.BrushLabels[i.TextSlot]}と縁"));
        string note = $"「{font.Name}」の{pairs}の明るさが近く、見づらくなります（配色の欄の注意から、直す色を選べます）";
        DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() =>
        {
            string now = _main.StatusText;
            if (now.Contains(note, StringComparison.Ordinal)) return;
            _main.StatusText = now != statusBefore && now.Length > 0 ? $"{now}　／　{note}" : note;
        });
    }

    /// <summary>候補の色を入れる箇所（配色パターンの役割でまとめて変えるなら、その役割の箇所すべて）。</summary>
    public List<int> ContrastTargetSlots(N3FontSet font, int slot)
    {
        if (Editor.EditRoleTogether && PatternOf(font) is { } m && m.Pattern.RoleOf(slot) is var role && role >= 0)
        {
            return m.Pattern.SlotsOf(role);
        }
        return new List<int> { slot };
    }

    /// <summary>検証: 文字と縁の明るさが近く、見づらいフォント設定。</summary>
    private static IEnumerable<N3FontIssue> ContrastIssues(IEnumerable<N3FontSet> fonts)
    {
        foreach (var f in fonts)
        {
            foreach (var i in N3Contrast.Check(f))
            {
                yield return new N3FontIssue(
                    N3FontIssueKind.LowContrast,
                    IssueSeverity.Warning,
                    $"「{f.Name}」: {N3FontDetail.BrushLabels[i.TextSlot]}（#{i.TextColor}）と縁（#{i.EdgeColor}）の明るさが近く、見づらくなります（コントラスト比 {i.Ratio:0.0}）",
                    f.Id,
                    f.Name,
                    i.TextSlot);
            }
        }
    }
}
