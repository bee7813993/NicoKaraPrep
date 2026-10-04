using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.ViewModels;

/// <summary>
/// フォント設定ビューの中央の編集欄（選択中のフォント設定 1 件）。基本・配色・フォントフェース・文字飾り。
/// 値の変更は <see cref="Edit"/> を通してフォント設定へ書き込む。フォント設定から値を読むのは <see cref="Load"/> と
/// <see cref="RefreshState"/>（名前・連動・継承の表示など、ほかの欄の変更で変わる表示）だけ。
/// </summary>
public sealed partial class FontEditorViewModel : ObservableObject
{
    private readonly FontSettingsViewModel _owner;
    private bool _loading;

    public FontEditorViewModel(FontSettingsViewModel owner)
    {
        _owner = owner;
        for (int i = 0; i < N3FontDetail.BrushCount; i++) BrushCells.Add(new BrushCellViewModel(i));
        AfterCells = BrushCells.Take(N3FontDetail.BeforeOffset).ToList();
        BeforeCells = BrushCells.Skip(N3FontDetail.BeforeOffset).ToList();
        BrushCells[0].IsSelected = true;
        for (int i = 0; i < N3FontDetail.FaceCount; i++) Faces.Add(new FaceRowViewModel(this, i));
        BrushEditor = new BrushEditorViewModel(this);
    }

    /// <summary>編集中のフォント設定（未選択なら null）。</summary>
    public N3FontSet? Font { get; private set; }

    /// <summary>編集中のフォント設定の一覧の行。</summary>
    public FontListItem? Item { get; private set; }

    /// <summary>フォント設定から値を読み込んでいる最中か（この間の値の変化は編集として扱わない）。</summary>
    internal bool IsLoading => _loading;

    [ObservableProperty]
    private bool hasFont;

    [ObservableProperty]
    private bool noFont = true;

    // ------------------------------------------------------------ 基本

    [ObservableProperty]
    private string nameText = "";

    /// <summary>範囲（0 アプリ共通 / 1 この曲専用）。表示だけで、変更はボタンで行う。</summary>
    [ObservableProperty]
    private int scopeIndex = -1;

    [ObservableProperty]
    private bool isCommon;

    [ObservableProperty]
    private bool isSong;

    [ObservableProperty]
    private string scopeNote = "";

    [ObservableProperty]
    private string importedFromText = "";

    [ObservableProperty]
    private bool isLinked;

    [ObservableProperty]
    private string linkText = "";

    /// <summary>連動していないが、ニコカラメーカー3 から取り込んだフォント設定か（連動を外した旨を出す）。</summary>
    [ObservableProperty]
    private bool hasLinkNote;

    // ------------------------------------------------------------ 配色

    /// <summary>配色の 8 箇所（添字 = <see cref="N3FontDetail.Brushes"/> の添字）。</summary>
    public ObservableCollection<BrushCellViewModel> BrushCells { get; } = new();

    /// <summary>ワイプ後の 4 箇所（文字・縁・縁 2・飾り）。</summary>
    public IReadOnlyList<BrushCellViewModel> AfterCells { get; }

    /// <summary>ワイプ前の 4 箇所。</summary>
    public IReadOnlyList<BrushCellViewModel> BeforeCells { get; }

    /// <summary>編集する配色の箇所（0–7）。</summary>
    [ObservableProperty]
    private int selectedBrushIndex;

    /// <summary>選択中の配色の箇所の編集欄。</summary>
    public BrushEditorViewModel BrushEditor { get; }

    // ------------------------------------------------------------ 配色パターン

    /// <summary>配色パターンの選択肢（先頭は「個別（パターンなし）」、続けて標準・ユーザーのパターン）。</summary>
    public ObservableCollection<PatternChoice> PatternChoices { get; } = new();

    /// <summary>当てはめている配色パターンの選択肢の位置（0 = 個別）。</summary>
    [ObservableProperty]
    private int selectedPatternIndex;

    /// <summary>当てはめている配色パターンの役割と箇所の説明。</summary>
    [ObservableProperty]
    private string patternNote = "";

    /// <summary>配色パターンの形と違う箇所があるか。</summary>
    [ObservableProperty]
    private bool hasPatternDeviation;

    [ObservableProperty]
    private string patternDeviationText = "";

    /// <summary>同じ役割の箇所をまとめて変えるか（既定はまとめる。外すと、選んだ 1 箇所だけを変える）。</summary>
    [ObservableProperty]
    private bool editRoleTogether = true;

    /// <summary>選んだ箇所に役割があるか（「まとめて変える」の切り替えを出す）。</summary>
    [ObservableProperty]
    private bool selectedSlotHasRole;

    [ObservableProperty]
    private string roleTogetherText = "";

    /// <summary>当てはめている配色パターンと、違う箇所（パターンなしは null）。</summary>
    private N3PatternMatch? _match;

    // ------------------------------------------------------------ 見づらい配色

    /// <summary>文字と縁の明るさが近い（見づらい）組があるか。</summary>
    [ObservableProperty]
    private bool hasContrastIssue;

    [ObservableProperty]
    private string contrastText = "";

    /// <summary>見づらい配色を直す色の候補。</summary>
    public ObservableCollection<ContrastSuggestionItem> ContrastSuggestions { get; } = new();

    /// <summary>
    /// 組み合わせのフォント設定（元のフォント設定と連動中）の多色の箇所が見づらいとき、直しに行く元のフォント設定（開くボタン）。
    /// </summary>
    public ObservableCollection<ContrastSourceItem> ContrastSources { get; } = new();

    /// <summary>色のドラッグ中か（そのあいだは配色の注意の欄を出し入れしない）。</summary>
    private bool _holdNotices;

    /// <summary>ドラッグ中に配色の注意の中身が変わり、まだ出し直していないか。</summary>
    private bool _noticesPending;

    // ------------------------------------------------------------ 組み合わせ

    /// <summary>組み合わせフォント（複数人で歌うパート用）か。</summary>
    [ObservableProperty]
    private bool isComposed;

    /// <summary>元のフォント設定の並び（上の帯から）。</summary>
    [ObservableProperty]
    private string compositionSourcesText = "";

    /// <summary>塗り方（0 ミルフィーユ / 1 グラデーション）。</summary>
    [ObservableProperty]
    private int compositionBrushIndex;

    /// <summary>端の帯の広さ（%）。</summary>
    [ObservableProperty]
    private double compositionWiden = N3FontComposer.DefaultEndWidenPercent;

    /// <summary>元のフォント設定の色が変わったら作り直すか。</summary>
    [ObservableProperty]
    private bool compositionLinked = true;

    [ObservableProperty]
    private string compositionNote = "";

    /// <summary>当てはめている配色パターン（パターンなしは null）。</summary>
    public N3ColorPattern? Pattern => _match?.Pattern;

    /// <summary>
    /// 編集したときにフォント設定に覚えさせる配色パターン（配色から自動で当てはめているときだけ。覚えさせたら 1 箇所だけ変えて
    /// 形が崩れても、パターンを見失わない）。
    /// </summary>
    internal string? PatternIdToKeep => Font?.ColorPatternId is null ? _match?.Pattern.Id : null;

    // ------------------------------------------------------------ フォントフェース

    /// <summary>文字種別フォント 6 行（添字 = <see cref="N3FontDetail.Faces"/> の添字）。</summary>
    public ObservableCollection<FaceRowViewModel> Faces { get; } = new();

    // ------------------------------------------------------------ 文字飾り

    /// <summary>文字飾りの種類（0 なし / 1 影 / 2 ブラー）。</summary>
    [ObservableProperty]
    private int decorKind;

    [ObservableProperty]
    private double decorSizeValue = 10;

    [ObservableProperty]
    private double blurLevel = 2;

    [ObservableProperty]
    private bool isDecor;

    [ObservableProperty]
    private bool isBlur;

    // ------------------------------------------------------------ 読み込み

    /// <summary>選択したフォント設定を編集欄に読み込む（null なら空にする）。</summary>
    public void Load(FontListItem? item)
    {
        _holdNotices = false;
        _noticesPending = false;
        _loading = true;
        try
        {
            Item = item;
            Font = item?.Font;
            HasFont = Font is not null;
            NoFont = !HasFont;
            if (Font is null)
            {
                NameText = "";
                ScopeIndex = -1;
                return;
            }

            IsSong = item!.IsSong;
            IsCommon = !IsSong;
            ScopeIndex = IsSong ? 1 : 0;
            ReloadBrushesCore();
            var d = Font.Detail;
            DecorKind = d.DecorKind is >= 0 and <= 2 ? d.DecorKind : -1;
            DecorSizeValue = d.DecorSizePx;
            BlurLevel = Math.Clamp(d.BlurLevel, 0, 2);
            IsDecor = d.DecorKind is 1 or 2;
            IsBlur = d.DecorKind == 2;
            foreach (var face in Faces) face.Load(Font);
            RefreshStateCore();
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>編集のあとに変わる表示（名前・範囲・連動・取り込み元・継承の実効値）を読み直す。</summary>
    public void RefreshState()
    {
        if (Font is null) return;
        _loading = true;
        try
        {
            RefreshStateCore();
        }
        finally
        {
            _loading = false;
        }
    }

    private void RefreshStateCore()
    {
        var f = Font!;
        NameText = f.Name;
        IsLinked = f.NkmSynchronize;
        LinkText = f.NkmSynchronize
            ? "ニコカラメーカー3 のテンプレートと連動しています。編集すると、書き出し時にこのフォントの連動が外れます"
            : f.NkmGuid is not null
                ? "ニコカラメーカー3 のテンプレートとは連動していません（取り込んだときに連動していなかったか、NicoKaraPrep で編集して連動を外しました）"
                : "";
        HasLinkNote = !f.NkmSynchronize && LinkText.Length > 0;
        ImportedFromText = f.ImportedFrom is { Length: > 0 } from
            ? $"{from}{(f.ImportedUtc is DateTime at ? $"（{at.ToLocalTime():yyyy/MM/dd HH:mm} に取り込み）" : "")}"
            : f.NkmGuid is not null ? "（記録なし）" : "（NicoKaraPrep で作成）";
        ScopeNote = IsSong
            ? "この曲専用のフォント設定は .tttproj（歌詞ファイルの隣）に保存され、書き出しでは同じ名前のアプリ共通より優先されます"
            : Item is { IsOverridden: true }
                ? "同じ名前の曲専用のフォント設定があるため、この曲の書き出しでは曲専用のほうが使われます"
                : "アプリ共通のフォント設定は settings.json に保存され、どの曲でも使えます";
        foreach (var face in Faces) face.RefreshPlaceholders(f);
        LoadComposition(f);
    }

    /// <summary>組み合わせの欄を読み直す（元のフォント設定の名前・塗り方・帯の広さ・連動）。</summary>
    private void LoadComposition(N3FontSet f)
    {
        var c = f.Composition;
        IsComposed = c is not null;
        if (c is null)
        {
            CompositionSourcesText = "";
            CompositionNote = "";
            return;
        }
        var names = c.SourceIds.Select(id => _owner.FindFont(id)?.Name ?? "（見つかりません）").ToList();
        CompositionSourcesText = string.Join(" → ", names);
        CompositionBrushIndex = c.BrushType == N3Brush.TypeGradient ? 1 : 0;
        CompositionWiden = c.EndWidenPercent;
        CompositionLinked = c.Linked;
        bool missing = c.SourceIds.Any(id => _owner.FindFont(id) is null);
        string role = _owner.PatternOf(f)?.Pattern.RoleName(N3FontComposer.MultiColorRole) is { Length: > 0 } r ? r : "メイン色";
        CompositionNote = missing
            ? "元のフォント設定が見つからないものがあるため、作り直せません（今の色のままです）"
            : c.Linked
                ? $"元のフォント設定の{role}を変えると、この組み合わせの色も自動で作り直します。{role}以外の箇所は、最初の元のフォント設定と同じです"
                : "元のフォント設定とは連動していません（色は今のまま変わりません）";
    }

    partial void OnCompositionBrushIndexChanged(int value)
    {
        if (_loading || Font?.Composition is null || value < 0) return;
        _owner.UpdateComposition(Font, "compose.type", c => c.BrushType = value == 1 ? N3Brush.TypeGradient : N3Brush.TypeMilleFeuille);
    }

    partial void OnCompositionWidenChanged(double value)
    {
        if (_loading || Font?.Composition is null || double.IsNaN(value)) return;
        _owner.UpdateComposition(Font, "compose.widen", c => c.EndWidenPercent = Math.Clamp(value, 0, 200));
    }

    partial void OnCompositionLinkedChanged(bool value)
    {
        if (_loading || Font?.Composition is null) return;
        _owner.UpdateComposition(Font, "compose.linked", c => c.Linked = value, recompose: value); // 外すときは今の色のまま
    }

    /// <summary>配色の 8 箇所と、選択中の箇所の編集欄を読み直す（一括の操作・色のコピーのあと）。</summary>
    public void ReloadBrushes()
    {
        if (Font is null) return;
        _loading = true;
        try
        {
            ReloadBrushesCore();
        }
        finally
        {
            _loading = false;
        }
    }

    private void ReloadBrushesCore()
    {
        for (int i = 0; i < N3FontDetail.BrushCount; i++) BrushCells[i].Load(Font!.Detail.Brushes[i]);
        if (SelectedBrushIndex is < 0 or >= N3FontDetail.BrushCount) SelectedBrushIndex = 0;
        RefreshPatternStateCore();
        LoadBrushEditor();
    }

    /// <summary>配色パターンの選択肢を作り直す（パターンを編集したあと）。</summary>
    public void ReloadPatternChoices()
    {
        bool was = _loading;
        _loading = true;
        try
        {
            PatternChoices.Clear();
            PatternChoices.Add(new PatternChoice(N3ColorPatterns.NoneId, "個別（パターンなし。8 箇所を別々に指定）"));
            foreach (var pattern in _owner.Patterns) PatternChoices.Add(new PatternChoice(pattern.Id, pattern.Name));
            if (Font is not null) RefreshPatternStateCore();
        }
        finally
        {
            _loading = was;
        }
    }

    /// <summary>配色を変えたあと（まとめて編集・コピー・交換など）に、配色パターンとの違いの表示を読み直す。</summary>
    internal void RefreshPatternState()
    {
        if (Font is null) return;
        bool was = _loading;
        _loading = true;
        try
        {
            var before = _match?.Pattern;
            RefreshPatternStateCore();

            // 当てはめるパターンが変わったら（自動で当てはめていて、配色の形が変わった）、まとめて変える箇所も変える
            if (!ReferenceEquals(before, _match?.Pattern)) LoadBrushEditor();
        }
        finally
        {
            _loading = was;
        }
    }

    private void RefreshPatternStateCore()
    {
        var font = Font!;
        if (PatternChoices.Count == 0)
        {
            PatternChoices.Add(new PatternChoice(N3ColorPatterns.NoneId, "個別（パターンなし。8 箇所を別々に指定）"));
            foreach (var pattern in _owner.Patterns) PatternChoices.Add(new PatternChoice(pattern.Id, pattern.Name));
        }
        _match = N3ColorPatterns.Effective(font, _owner.Patterns);
        var p = _match?.Pattern;
        int index = p is null ? 0 : PatternChoices.ToList().FindIndex(c => c.Id == p.Id);
        SelectedPatternIndex = Math.Max(0, index);

        for (int i = 0; i < N3FontDetail.BrushCount; i++)
        {
            int role = p?.RoleOf(i) ?? N3ColorPattern.Individual;
            BrushCells[i].RoleText = p?.RoleName(role) ?? "";
            BrushCells[i].IsDeviation = _match?.Deviations.Contains(i) ?? false;
        }

        if (p is null)
        {
            PatternNote = font.ColorPatternId == N3ColorPatterns.NoneId
                ? "8 箇所を別々に指定しています"
                : "どの配色パターンの形にも当てはまりません（8 箇所を別々に指定しています）。パターンを選ぶと、その形に色をそろえます";
        }
        else
        {
            var roles = Enumerable.Range(0, p.Roles.Count)
                .Select(r => (Name: p.RoleName(r), Slots: p.SlotsOf(r)))
                .Where(r => r.Slots.Count > 0)
                .Select(r => $"{r.Name}: {N3ColorPatterns.Describe(r.Slots)}");
            string auto = font.ColorPatternId is null ? "（配色から自動で当てはめています）" : "";
            PatternNote = $"{string.Join(" ／ ", roles)}{auto}";
        }

        RefreshNoticesCore(font);
    }

    /// <summary>
    /// 色の四角・つまみのドラッグを始めた・終えた。ドラッグ中は配色の注意の欄（パターンと違う・見づらい色）を出し入れしない
    /// （欄の高さが変わると下の色の四角が画面の上でずれ、ずれた先の色になってしまうため）。終えたら今の配色で出し直す。
    /// </summary>
    public void HoldNotices(bool hold)
    {
        _holdNotices = hold;
        if (hold || !_noticesPending || Font is null) return;
        _noticesPending = false;
        bool was = _loading;
        _loading = true;
        try
        {
            RefreshNoticesCore(Font);
        }
        finally
        {
            _loading = was;
        }
    }

    /// <summary>配色の注意の欄（パターンと違う・見づらい色）を読み直す（色のドラッグ中は、終わってから）。</summary>
    private void RefreshNoticesCore(N3FontSet font)
    {
        if (_holdNotices)
        {
            _noticesPending = true;
            return;
        }
        RefreshContrastCore(font);
        HasPatternDeviation = _match is { IsExact: false };
        PatternDeviationText = _match is { IsExact: false } m
            ? $"「{m.Pattern.Name}」の形と {m.Deviations.Count} 箇所違います（{N3ColorPatterns.Describe(m.Deviations)}）。入力の間違いなら「パターンに合わせる」で、同じ役割の多いほうの色にそろえます"
            : "";
    }

    /// <summary>
    /// 見づらい配色（文字と縁の明るさが近い組）の注意と、色の候補を読み直す。候補は、単色どうしの最初の組（ワイプ前が先）から作る。
    /// </summary>
    private void RefreshContrastCore(N3FontSet font)
    {
        ContrastSuggestions.Clear();
        ContrastSources.Clear();
        var issues = N3Contrast.Check(font);
        HasContrastIssue = issues.Count > 0;
        if (issues.Count == 0)
        {
            ContrastText = "";
            return;
        }
        var lines = issues.Select(i =>
            $"{N3FontDetail.BrushLabels[i.TextSlot]}（#{i.TextColor}）と{N3FontDetail.BrushLabels[i.EdgeSlot]}（#{i.EdgeColor}）の明るさが近く、見づらくなります（コントラスト比 {i.Ratio:0.0}）").ToList();
        var first = issues[0]; // ワイプ前が先

        // 元のフォント設定と連動している組み合わせの多色の箇所は、ここで直すと連動が外れるので、元のフォント設定を開いて直してもらう
        if (!first.BothSolid && font.Composition is { Linked: true } composition)
        {
            string role = _match?.Pattern.RoleName(N3FontComposer.MultiColorRole) is { Length: > 0 } r ? r : "メイン色";
            lines.Add($"多色の箇所の色は、元のフォント設定の{role}から作っています。下のボタンで元のフォント設定を開いて色を直すと、この組み合わせも作り直します（ここで色を変えると、元のフォント設定との連動が外れます）");
            foreach (var (id, name) in ContrastSourceFonts(font, composition, first)) ContrastSources.Add(new ContrastSourceItem($"「{name}」を開く", id));
            ContrastText = string.Join("\n", lines);
            return;
        }

        foreach (var s in N3Contrast.Suggest(first, font.Detail))
        {
            var slots = _owner.ContrastTargetSlots(font, s.Slot);
            int role = _match?.Pattern.RoleOf(s.Slot) ?? N3ColorPattern.Individual;
            string target = slots.Count > 1 && role >= 0
                ? $"{_match!.Pattern.RoleName(role)}（{slots.Count} 箇所）"
                : N3FontDetail.BrushLabels[s.Slot];
            string how = s.Kind switch
            {
                N3ContrastSuggestionKind.TextLightness => N3Contrast.Luminance(s.Color) < N3Contrast.Luminance(first.TextColor) ? "を暗くして" : "を明るくして",
                N3ContrastSuggestionKind.EdgeShade => "を文字の色合いの" + (N3Contrast.Luminance(s.Color) < N3Contrast.Luminance(first.TextColor) ? "濃い色" : "淡い色") + "にして",
                N3ContrastSuggestionKind.StopLightness =>
                    $"のマーカー {s.Stop + 1}（#{s.OriginalColor}）を" + (N3Contrast.Luminance(s.Color) < N3Contrast.Luminance(s.OriginalColor) ? "暗く" : "明るく") + "して",
                _ => "を" + (s.Color == "000000" ? "黒" : "白") + "にして",
            };
            ContrastSuggestions.Add(new ContrastSuggestionItem(s, first, $"{target}{how} #{s.Color} に"));
        }
        ContrastText = string.Join("\n", lines);
    }

    /// <summary>
    /// 組み合わせのフォント設定の見づらい多色の箇所を直しに行く、元のフォント設定（Id と名前）。見づらいマーカーの色を
    /// 多色の役割の色に持っているものを選ぶ（見つからなければ、元のフォント設定すべて）。
    /// </summary>
    private List<(string Id, string Name)> ContrastSourceFonts(N3FontSet font, N3FontComposition composition, N3ContrastIssue issue)
    {
        var sources = composition.SourceIds.Select(id => _owner.FindFont(id)).OfType<N3FontSet>().ToList();
        var bad = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (issue.TextStop >= 0) bad.Add(issue.TextColor);
        if (issue.EdgeStop >= 0) bad.Add(issue.EdgeColor);
        var hits = sources.Where(src =>
        {
            var pattern = _owner.PatternOf(src)?.Pattern ?? _match?.Pattern;
            return pattern is not null
                && N3FontComposer.RoleColors(src.Detail, pattern, N3FontComposer.MultiColorRole).Any(c => bad.Contains(N3FontSet.NormalizeWeb16(c.Color)));
        }).ToList();
        return (hits.Count > 0 ? hits : sources).Select(f => (f.Id, f.Name)).ToList();
    }

    /// <summary>選んだ箇所を編集欄に読み込む（同じ役割の箇所をまとめて変えるなら、その箇所も一緒に）。</summary>
    private void LoadBrushEditor()
    {
        int index = SelectedBrushIndex;
        var p = _match?.Pattern;
        int role = p?.RoleOf(index) ?? N3ColorPattern.Individual;
        var slots = role >= 0 ? p!.SlotsOf(role) : new List<int>();
        SelectedSlotHasRole = slots.Count > 1;
        RoleTogetherText = SelectedSlotHasRole
            ? $"同じ役割（{p!.RoleName(role)}）の {slots.Count} 箇所をまとめて変える"
            : "";
        var linked = SelectedSlotHasRole && EditRoleTogether ? slots : new List<int>();
        foreach (var cell in BrushCells) cell.IsLinked = cell.Index != index && linked.Contains(cell.Index);
        BrushEditor.Load(index, linked, p?.RoleName(role) ?? "");

        // 使っている色・最近使った色の一覧（ColorPicker の横）。編集中に一覧が動かないよう、箇所を選び直したときだけ作り直す
        if (Font is { } font)
        {
            var (groups, images) = _owner.UsedColors(font);
            BrushEditor.SetPalette(groups, images);
        }
        BrushEditor.SetRecentColors(_owner.RecentColors());
    }

    /// <summary>組み合わせのフォント設定の、端の帯の広さの既定（%。「端を広く配置」の最初の値）。</summary>
    internal double ComposeEndWidenPercent => _owner.ComposeEndWidenPercent;

    /// <summary>最近使った色に入れる（配色の編集欄から）。</summary>
    internal void AddRecentColor(string color, int alphaPercent) => _owner.AddRecentColor(color, alphaPercent);

    /// <summary>ステータスバーに知らせる（配色の編集欄から）。</summary>
    internal void SetStatus(string text) => _owner.Main.StatusText = text;

    partial void OnEditRoleTogetherChanged(bool value)
    {
        if (Font is null) return;
        bool was = _loading;
        _loading = true;
        try
        {
            LoadBrushEditor();
        }
        finally
        {
            _loading = was;
        }
    }

    partial void OnSelectedPatternIndexChanged(int value)
    {
        if (_loading || Font is null || value < 0 || value >= PatternChoices.Count) return;
        string id = PatternChoices[value].Id;
        if (id == (_match?.Pattern.Id ?? N3ColorPatterns.NoneId) && Font.ColorPatternId is not null) return;
        _owner.SetFontPattern(Font, id);
    }

    /// <summary>配色の 1 箇所の色見本を読み直す。</summary>
    internal void RefreshBrushCell(int index)
    {
        if (Font is not null) BrushCells[index].Load(Font.Detail.Brushes[index]);
    }

    // ------------------------------------------------------------ 編集

    /// <summary>フォント設定（アプリ共通・この曲専用）で使っているフォント名（フォントを選ぶ画面の先頭に出す）。</summary>
    public IReadOnlyCollection<string> UsedFontNames() => _owner.UsedFontNames();

    /// <summary>編集中のフォント設定を変える（読み込み中は何もしない）。</summary>
    internal void Edit(string? key, Action<N3FontSet> change)
    {
        if (_loading || Font is null) return;
        _owner.EditFont(Font, key, change);
    }

    partial void OnSelectedBrushIndexChanged(int value)
    {
        foreach (var cell in BrushCells) cell.IsSelected = cell.Index == value;
        if (value is < 0 or >= N3FontDetail.BrushCount || Font is null) return;
        bool was = _loading;
        _loading = true;
        try
        {
            LoadBrushEditor();
        }
        finally
        {
            _loading = was;
        }
    }

    /// <summary>配色パターンの選択肢 1 つ（識別子と表示名）。</summary>
    public sealed record PatternChoice(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    /// <summary>ワイプ前後の配色を交換する。</summary>
    public void SwapBeforeAfter()
    {
        Edit(null, f => N3FontLibrary.SwapBeforeAfter(f.Detail));
        ReloadBrushes();
    }

    /// <summary>ワイプ後の配色をワイプ前にコピーする。</summary>
    public void CopyAfterToBefore()
    {
        Edit(null, f => N3FontLibrary.CopyAfterToBefore(f.Detail));
        ReloadBrushes();
    }

    /// <summary>ワイプ前の配色をワイプ後にコピーする。</summary>
    public void CopyBeforeToAfter()
    {
        Edit(null, f => N3FontLibrary.CopyBeforeToAfter(f.Detail));
        ReloadBrushes();
    }

    partial void OnDecorKindChanged(int value)
    {
        IsDecor = value is 1 or 2;
        IsBlur = value == 2;
        if (value < 0) return;
        Edit("decor.kind", f => f.Detail.DecorKind = value);
    }

    partial void OnDecorSizeValueChanged(double value)
    {
        if (double.IsNaN(value)) return;
        Edit("decor.size", f => f.Detail.DecorSizePx = Math.Max(0, value));
    }

    partial void OnBlurLevelChanged(double value)
    {
        if (double.IsNaN(value)) return;
        Edit("decor.blur", f => f.Detail.BlurLevel = (int)Math.Round(Math.Clamp(value, 0, 2)));
    }
}
