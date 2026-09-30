using NicoKaraPrep.Core.Model;
using NicoKaraPrep.Core.Validation;

namespace NicoKaraPrep.App.ViewModels;

/// <summary>
/// フォント設定ビューの配色パターン: 一覧（標準＋ユーザー）、フォント設定への当てはめ・色をそろえる・パターンを外す、
/// 役割ごとの色のコピー、新しいフォント設定の既定のパターン、検証（パターンの形と少し違う配色）。
/// パターンの識別子（<see cref="N3FontSet.ColorPatternId"/>）はにこぷれっぷの中だけで使い、書き出しには影響しない。
/// </summary>
public sealed partial class FontSettingsViewModel
{
    private List<N3ColorPattern>? _patterns;

    /// <summary>配色パターンの一覧（標準のあとにユーザーのパターン）。</summary>
    public IReadOnlyList<N3ColorPattern> Patterns => _patterns ??= N3ColorPatterns.All(_main.Settings.N3UserColorPatterns);

    /// <summary>ユーザーが作った配色パターン（編集画面に渡す）。</summary>
    public IReadOnlyList<N3ColorPattern> UserPatterns => _main.Settings.N3UserColorPatterns;

    /// <summary>新しいフォント設定に使う配色パターンの識別子。</summary>
    public string DefaultPatternId => _main.Settings.N3DefaultColorPatternId;

    /// <summary>フォント設定に当てはめる配色パターンと、形と違う箇所（パターンなしは null）。</summary>
    public N3PatternMatch? PatternOf(N3FontSet font) => N3ColorPatterns.Effective(font, Patterns);

    /// <summary>
    /// フォント設定の配色パターンを変える。パターンの形と違う箇所があれば、役割ごとに多いほうの色へそろえる
    /// （色が変わったときだけ編集扱いにする。パターンの選択だけならニコカラメーカー3 のテンプレートとの連動は外さない）。
    /// <see cref="N3ColorPatterns.NoneId"/> ならパターンを外す（色は変えない）。
    /// </summary>
    public void SetFontPattern(N3FontSet font, string patternId)
    {
        var pattern = N3ColorPatterns.Find(Patterns, patternId);
        bool align = pattern is not null && !N3ColorPatterns.Evaluate(font.Detail, pattern).IsExact;
        if (font.ColorPatternId == patternId && !align) return;

        PushUndo(null);
        font.ColorPatternId = patternId;
        int changed = 0;
        if (align)
        {
            changed = N3ColorPatterns.Align(font.Detail, pattern!);
            N3FontLibrary.MarkEdited(font);
        }
        OnFontContentChanged(font);
        if (ReferenceEquals(SelectedFont, font)) Editor.ReloadBrushes();
        SetStatus(pattern is null
            ? $"「{font.Name}」の配色パターンを外しました（8 箇所を別々に指定します。Ctrl+Z で元に戻せます）"
            : changed > 0
                ? $"「{font.Name}」の配色を「{pattern.Name}」の形にそろえました（{changed} 箇所を同じ役割の色にしました。Ctrl+Z で元に戻せます）"
                : $"「{font.Name}」に配色パターン「{pattern.Name}」を使います");
    }

    /// <summary>選択中のフォント設定の配色を、当てはめているパターンの形にそろえる（検証・パターンの注意から）。</summary>
    public void AlignSelectedToPattern()
    {
        if (SelectedFont is not { } font || PatternOf(font) is not { IsExact: false } match) return;
        SetFontPattern(font, match.Pattern.Id);
    }

    /// <summary>選択中のフォント設定の配色パターンを外し、今の配色のまま 8 箇所を別々に指定する。</summary>
    public void KeepIndividualColors()
    {
        if (SelectedFont is { } font) SetFontPattern(font, N3ColorPatterns.NoneId);
    }

    /// <summary>ほかのフォント設定の配色から、役割の色だけを選択中のフォント設定へ写す。</summary>
    public void CopyRoleFrom(FontListItem source, N3ColorPattern pattern, int role)
    {
        if (SelectedFont is not { } font || ReferenceEquals(source.Font, font)) return;
        EditFont(font, null, f =>
        {
            f.ColorPatternId ??= pattern.Id;
            N3ColorPatterns.CopyRole(source.Font.Detail, f.Detail, pattern, role);
        });
        Editor.ReloadBrushes();
        SetStatus($"「{source.Font.Name}」の{pattern.RoleName(role)}（{N3ColorPatterns.Describe(pattern.SlotsOf(role))}）を「{font.Name}」に写しました");
    }

    /// <summary>
    /// 配色パターンの編集画面で決めたユーザーのパターンと、新しいフォント設定に使うパターンを保存する
    /// （パターンの定義は元に戻す の対象にしない）。消したパターンを使っていたフォント設定は、配色から自動で当てはめる。
    /// </summary>
    public void SaveColorPatterns(List<N3ColorPattern> user, string defaultId)
    {
        _main.Settings.N3UserColorPatterns = user;
        _main.Settings.N3DefaultColorPatternId = defaultId;
        _patterns = null;

        var ids = new HashSet<string>(Patterns.Select(p => p.Id)) { N3ColorPatterns.NoneId };
        foreach (var f in Common.Concat(Song))
        {
            if (f.ColorPatternId is { } id && !ids.Contains(id)) f.ColorPatternId = null;
        }
        MarkDirty(song: false);
        if (Song.Count > 0) MarkDirty(song: true);

        Editor.ReloadPatternChoices();
        if (SelectedItem is not null) Editor.ReloadBrushes();
        RunAnalysis();
        SetStatus($"配色パターンを保存しました（標準 {N3ColorPatterns.BuiltIns.Count} 件・自作 {user.Count} 件）");
    }

    /// <summary>新しいフォント設定に、既定の配色パターンを当てはめて色をそろえる（既定が「パターンなし」なら何もしない）。</summary>
    private void ApplyDefaultPattern(N3FontSet font)
    {
        if (N3ColorPatterns.Find(Patterns, DefaultPatternId) is not { } pattern) return;
        font.ColorPatternId = pattern.Id;
        N3ColorPatterns.Align(font.Detail, pattern);
    }

    /// <summary>検証: 配色がパターンの形と少し違うフォント設定（入力の間違いの見込み）。</summary>
    private IEnumerable<N3FontIssue> PatternIssues(IEnumerable<N3FontSet> fonts)
    {
        foreach (var f in fonts)
        {
            if (PatternOf(f) is not { IsExact: false } m) continue;
            yield return new N3FontIssue(
                N3FontIssueKind.PatternMismatch,
                IssueSeverity.Warning,
                $"「{f.Name}」: 配色が「{m.Pattern.Name}」の形と {m.Deviations.Count} 箇所違います（{N3ColorPatterns.Describe(m.Deviations)}）",
                f.Id,
                f.Name,
                m.Deviations[0]);
        }
    }
}
