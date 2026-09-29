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
        BrushEditor.Load(SelectedBrushIndex);
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
            BrushEditor.Load(value);
        }
        finally
        {
            _loading = was;
        }
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
