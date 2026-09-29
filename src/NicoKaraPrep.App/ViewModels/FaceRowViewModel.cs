using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using NicoKaraPrep.App.Services;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.ViewModels;

/// <summary>
/// 文字種別フォント 1 行（フォントフェースの表。歌詞／漢字・かな・英数、ルビ／漢字・かな・英数）。
/// 空・0・継承の欄は値を空にし、PlaceholderText に継承した値（実効値）を薄く出す。
/// </summary>
public sealed partial class FaceRowViewModel : ObservableObject
{
    /// <summary>フォント名・フェイス名の選択肢の先頭（選ぶと継承に戻す）。</summary>
    public const string InheritChoice = "（継承に戻す）";

    private static readonly Lazy<IReadOnlyList<string>> SystemFonts = new(() =>
    {
        var fonts = new List<string> { InheritChoice };
        try
        {
            fonts.AddRange(DirectWriteTextMeasurer.GetSystemFontFamilies().Distinct().OrderBy(f => f, StringComparer.CurrentCulture));
        }
        catch (Exception)
        {
            // フォントの一覧が取れなくても、名前の直接入力はできる
        }
        return fonts;
    });

    private readonly FontEditorViewModel _editor;
    private bool _loading;

    public FaceRowViewModel(FontEditorViewModel editor, int index)
    {
        _editor = editor;
        Index = index;
        Label = N3FontDetail.FaceLabels[index];
    }

    /// <summary>文字種別（<see cref="N3FontDetail.Faces"/> の添字）。</summary>
    public int Index { get; }

    /// <summary>種別の表示（歌詞／漢字 など）。</summary>
    public string Label { get; }

    /// <summary>読み上げ・UI オートメーションでの欄の名前（「歌詞／漢字 サイズ」など）。</summary>
    public string NameOf(string column) => $"{Label} {column}";

    public string FontNameLabel => NameOf("フォント名");

    public string FaceNameLabel => NameOf("フェイス");

    public string SizeLabel => NameOf("サイズ");

    public string XScaleLabel => NameOf("横倍率");

    public string EdgeLabel => NameOf("縁");

    public string Edge2Label => NameOf("縁 2");

    public string Edge2WidthLabel => NameOf("縁 2 幅");

    /// <summary>ルビ／漢字の行か（サイズ・縁・縁 2 の 0 は「歌詞の半分」）。</summary>
    public bool IsRubyKanji => Index == 3;

    /// <summary>フォント名の選択肢（先頭は「継承に戻す」、以降はシステムのフォント）。初めて使うときに 1 回だけ取得する。</summary>
    public IReadOnlyList<string> FontChoices => SystemFonts.Value;

    /// <summary>フェイス名の選択肢。</summary>
    public IReadOnlyList<string> FaceChoices { get; } = new[] { InheritChoice, "Bold", "ﾍﾋﾞｰ", "ｴｸｽﾄﾗﾎﾞｰﾙﾄﾞ", "Regular", "Italic" };

    /// <summary>縁 2 の選択肢（添字 0 付ける / 1 付けない / 2 継承に戻す）。</summary>
    public IReadOnlyList<string> Edge2Choices { get; } = new[] { "付ける", "付けない", InheritChoice };

    [ObservableProperty]
    private string fontNameText = "";

    [ObservableProperty]
    private string fontNamePlaceholder = "";

    [ObservableProperty]
    private string faceNameText = "";

    [ObservableProperty]
    private string faceNamePlaceholder = "";

    [ObservableProperty]
    private double sizeValue = double.NaN;

    [ObservableProperty]
    private string sizePlaceholder = "";

    [ObservableProperty]
    private double xScaleValue = double.NaN;

    [ObservableProperty]
    private string xScalePlaceholder = "";

    [ObservableProperty]
    private double edgeValue = double.NaN;

    [ObservableProperty]
    private string edgePlaceholder = "";

    /// <summary>縁 2（-1 継承 / 0 付ける / 1 付けない）。</summary>
    [ObservableProperty]
    private int edge2Index = -1;

    [ObservableProperty]
    private string edge2Placeholder = "";

    [ObservableProperty]
    private double edge2Value = double.NaN;

    [ObservableProperty]
    private string edge2WidthPlaceholder = "";

    /// <summary>フォント設定から値を読む。</summary>
    public void Load(N3FontSet font)
    {
        _loading = true;
        try
        {
            var face = font.Detail.Faces[Index];
            FontNameText = face.FontName;
            FaceNameText = face.FaceName;
            SizeValue = face.SizePx > 0 ? face.SizePx : double.NaN;
            XScaleValue = face.XScale > 0 ? face.XScale : double.NaN;
            EdgeValue = face.EdgePx > 0 ? face.EdgePx : double.NaN;
            Edge2Index = face.UseEdge2 switch { true => 0, false => 1, null => -1 };
            Edge2Value = face.Edge2Px > 0 ? face.Edge2Px : double.NaN;
            RefreshPlaceholders(font);
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>継承の欄に出す実効値を読み直す（ほかの行を変えると変わる）。</summary>
    public void RefreshPlaceholders(N3FontSet font)
    {
        var eff = N3FontLibrary.EffectiveFace(font, Index);
        string inherit = IsRubyKanji ? "半分" : "継承";
        FontNamePlaceholder = eff.FontName.Length > 0 ? $"継承: {eff.FontName}" : "継承: （既定のフォント）";
        FaceNamePlaceholder = eff.FaceName.Length > 0 ? $"継承: {eff.FaceName}" : "継承: （標準）";
        SizePlaceholder = $"{inherit}: {Num(eff.SizePx)}";
        XScalePlaceholder = $"継承: {eff.XScale}";
        EdgePlaceholder = $"{inherit}: {Num(eff.EdgePx)}";
        Edge2Placeholder = $"継承: {(eff.UseEdge2 == true ? "付ける" : "付けない")}";
        Edge2WidthPlaceholder = $"{inherit}: {Num(eff.Edge2Px)}";
    }

    private static string Num(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    private void Edit(string what, Action<N3FontFace> change)
    {
        if (_loading) return;
        int index = Index;
        _editor.Edit($"face{index}.{what}", f => change(f.Detail.Faces[index]));
    }

    /// <summary>フォント名を確定する（選択肢の「継承に戻す」と空は継承）。</summary>
    public void CommitFontName(string? text)
    {
        string name = NormalizeChoice(text);
        if (_editor.Font is not { } font || font.Detail.Faces[Index].FontName == name) return;
        Edit("font", face => face.FontName = name);
        SetWithoutEdit(() => FontNameText = name);
    }

    /// <summary>フェイス名を確定する（選択肢の「継承に戻す」と空は継承）。</summary>
    public void CommitFaceName(string? text)
    {
        string name = NormalizeChoice(text);
        if (_editor.Font is not { } font || font.Detail.Faces[Index].FaceName == name) return;
        Edit("faceName", face => face.FaceName = name);
        SetWithoutEdit(() => FaceNameText = name);
    }

    private static string NormalizeChoice(string? text)
    {
        string t = (text ?? "").Trim();
        return t == InheritChoice ? "" : t;
    }

    private void SetWithoutEdit(Action set)
    {
        _loading = true;
        try
        {
            set();
        }
        finally
        {
            _loading = false;
        }
    }

    private static double Positive(double value) => double.IsNaN(value) || value <= 0 ? 0 : value;

    /// <summary>
    /// 数値の欄を確定したときの処理。0 以下は継承（0）として書き込み、欄を空に戻して PlaceholderText（継承した値）を見せる
    /// （NumberBox の確定の処理の途中で値を変えないよう、処理が終わってから空にする。
    /// それまでに別のフォント設定を選んでいたら、そのフォント設定の値を消さないよう何もしない）。
    /// </summary>
    private void EditNumber(string what, double value, Func<N3FontFace, double> get, Action<N3FontFace, double> set, Action clear)
    {
        if (_loading || _editor.Font is not { } font) return;
        double v = Positive(value);
        if (get(font.Detail.Faces[Index]) != v) Edit(what, face => set(face, v));
        if (v == 0 && !double.IsNaN(value))
        {
            Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() =>
            {
                if (ReferenceEquals(_editor.Font, font)) SetWithoutEdit(clear);
            });
        }
    }

    partial void OnSizeValueChanged(double value) =>
        EditNumber("size", value, face => face.SizePx, (face, v) => face.SizePx = v, () => SizeValue = double.NaN);

    partial void OnXScaleValueChanged(double value) =>
        EditNumber("xscale", Math.Round(value), face => face.XScale, (face, v) => face.XScale = (int)v, () => XScaleValue = double.NaN);

    partial void OnEdgeValueChanged(double value) =>
        EditNumber("edge", value, face => face.EdgePx, (face, v) => face.EdgePx = v, () => EdgeValue = double.NaN);

    partial void OnEdge2ValueChanged(double value) =>
        EditNumber("edge2", value, face => face.Edge2Px, (face, v) => face.Edge2Px = v, () => Edge2Value = double.NaN);

    partial void OnEdge2IndexChanged(int value)
    {
        if (_loading || value < 0) return;
        bool? use = value switch { 0 => true, 1 => false, _ => null };
        var font = _editor.Font;
        if (font is not null && font.Detail.Faces[Index].UseEdge2 != use) Edit("useEdge2", face => face.UseEdge2 = use);
        if (use is null)
        {
            // 「継承に戻す」を選んだら、選択を外して PlaceholderText（継承した値）を見せる。
            // ComboBox の選択の処理の途中で選択を変えないよう、処理が終わってから外す（それまでに別のフォント設定を選んでいたら何もしない）
            Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() =>
            {
                if (ReferenceEquals(_editor.Font, font)) SetWithoutEdit(() => Edge2Index = -1);
            });
        }
    }
}
