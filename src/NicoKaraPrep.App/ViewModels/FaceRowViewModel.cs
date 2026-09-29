using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.ViewModels;

/// <summary>
/// 文字種別フォント 1 行（フォントフェースの表。歌詞／漢字・かな・英数、ルビ／漢字・かな・英数）。
/// フォント名とフェイス名はボタンに出し、押すとフォントを選ぶ画面を開く（<see cref="CommitFont"/> で確定）。
/// 数値の欄は空・0 が継承で、PlaceholderText に継承した値（実効値）を薄く出す。縁 2 は 3 状態（付ける／付けない／継承）。
/// </summary>
public sealed partial class FaceRowViewModel : ObservableObject
{
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

    public string FontLabel => NameOf("フォント");

    public string SizeLabel => NameOf("サイズ");

    public string XScaleLabel => NameOf("横倍率");

    public string EdgeLabel => NameOf("縁");

    public string Edge2Label => NameOf("縁 2");

    public string Edge2WidthLabel => NameOf("縁 2 幅");

    /// <summary>ルビ／漢字の行か（サイズ・縁・縁 2 の 0 は「歌詞の半分」）。</summary>
    public bool IsRubyKanji => Index == 3;

    /// <summary>英数の行か（フォントを選ぶ画面で「日本語のあるフォントだけ」を最初は外す）。</summary>
    public bool IsAlphanumeric => Index is 2 or 5;

    /// <summary>フォントのボタンの 1 行目（フォント名。継承なら「継承: …」）。</summary>
    [ObservableProperty]
    private string fontLine = "";

    /// <summary>フォントのボタンの 2 行目（フェイス名。継承なら「継承: …」）。</summary>
    [ObservableProperty]
    private string faceLine = "";

    /// <summary>フォント名が継承のときはボタンの 1 行目を薄くする。</summary>
    [ObservableProperty]
    private double fontLineOpacity = 1;

    [ObservableProperty]
    private string fontToolTip = "";

    [ObservableProperty]
    private double sizeValue = double.NaN;

    [ObservableProperty]
    private string sizePlaceholder = "";

    [ObservableProperty]
    private string sizeToolTip = "";

    [ObservableProperty]
    private double xScaleValue = double.NaN;

    [ObservableProperty]
    private string xScalePlaceholder = "";

    [ObservableProperty]
    private string xScaleToolTip = "";

    [ObservableProperty]
    private double edgeValue = double.NaN;

    [ObservableProperty]
    private string edgePlaceholder = "";

    [ObservableProperty]
    private string edgeToolTip = "";

    /// <summary>縁 2（true 付ける / false 付けない / null 継承）。</summary>
    [ObservableProperty]
    private bool? edge2State;

    [ObservableProperty]
    private string edge2ToolTip = "";

    [ObservableProperty]
    private double edge2Value = double.NaN;

    [ObservableProperty]
    private string edge2WidthPlaceholder = "";

    [ObservableProperty]
    private string edge2WidthToolTip = "";

    /// <summary>フォント設定から値を読む。</summary>
    public void Load(N3FontSet font)
    {
        _loading = true;
        try
        {
            var face = font.Detail.Faces[Index];
            SizeValue = face.SizePx > 0 ? face.SizePx : double.NaN;
            XScaleValue = face.XScale > 0 ? face.XScale : double.NaN;
            EdgeValue = face.EdgePx > 0 ? face.EdgePx : double.NaN;
            Edge2State = face.UseEdge2;
            Edge2Value = face.Edge2Px > 0 ? face.Edge2Px : double.NaN;
            RefreshPlaceholders(font);
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>フォントのボタンの表示と、継承の欄に出す実効値を読み直す（ほかの行を変えると変わる）。</summary>
    public void RefreshPlaceholders(N3FontSet font)
    {
        var own = font.Detail.Faces[Index];
        var eff = N3FontLibrary.EffectiveFace(font, Index);
        var (inheritedFont, inheritedFace) = InheritedFont(font);

        FontLine = own.FontName.Length > 0 ? own.FontName : $"継承: {DescribeFont(inheritedFont)}";
        FaceLine = own.FaceName.Length > 0 ? own.FaceName
            : eff.FaceName.Length > 0 ? $"継承: {eff.FaceName}"
            : "（標準）";
        FontLineOpacity = own.FontName.Length > 0 ? 1 : 0.6;
        FontToolTip =
            $"{Label} のフォント: {DescribeFont(eff.FontName)}（{(eff.FaceName.Length > 0 ? eff.FaceName : "標準")}）\n" +
            $"押すと、見本を見ながらフォントを選べます（継承に戻すと {DescribeFont(inheritedFont)}{(inheritedFace.Length > 0 ? $"（{inheritedFace}）" : "")}）";

        string blank = IsRubyKanji ? "歌詞の半分" : "継承";
        SizePlaceholder = Num(eff.SizePx);
        SizeToolTip = $"{Label} の文字サイズ（px、画面の高さ 1080 基準）。空欄は{blank}: {Num(eff.SizePx)}";
        XScalePlaceholder = eff.XScale.ToString(CultureInfo.InvariantCulture);
        XScaleToolTip = $"{Label} の横倍率（%）。空欄は継承: {eff.XScale}";
        EdgePlaceholder = Num(eff.EdgePx);
        EdgeToolTip = $"{Label} の縁の幅（px）。空欄は{blank}: {Num(eff.EdgePx)}";
        Edge2ToolTip =
            $"{Label} の縁 2: {(own.UseEdge2 switch { true => "付ける", false => "付けない", null => $"継承（{(eff.UseEdge2 == true ? "付ける" : "付けない")}）" })}\n" +
            "押すたびに 付けない（空欄）→ 付ける（✓）→ 継承（━）の順に変わります";
        Edge2WidthPlaceholder = Num(eff.Edge2Px);
        Edge2WidthToolTip = $"{Label} の縁 2 の幅（px）。空欄は{blank}: {Num(eff.Edge2Px)}";
    }

    /// <summary>
    /// フォント名・フェイス名を継承に戻したときに使われるフォント（歌詞／漢字はニコカラメーカー3 の既定のフォント = 空、
    /// 歌詞／かな・英数とルビ／漢字は歌詞／漢字、ルビ／かな・英数はルビ／漢字）。
    /// </summary>
    public (string FontName, string FaceName) InheritedFont(N3FontSet font)
    {
        if (Index == 0) return ("", "");
        var parent = N3FontLibrary.EffectiveFace(font, Index is 4 or 5 ? 3 : 0);
        return (parent.FontName, parent.FaceName);
    }

    private static string DescribeFont(string name) => name.Length > 0 ? name : "既定のフォント";

    private static string Num(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    private void Edit(string what, Action<N3FontFace> change)
    {
        if (_loading) return;
        int index = Index;
        _editor.Edit($"face{index}.{what}", f => change(f.Detail.Faces[index]));
    }

    /// <summary>フォントを選ぶ画面の結果でフォント名とフェイス名を確定する（両方とも空なら継承。1 回の編集として元に戻せる）。</summary>
    public void CommitFont(string fontName, string faceName)
    {
        string name = (fontName ?? "").Trim();
        string face = (faceName ?? "").Trim();
        if (_editor.Font is not { } font) return;
        var current = font.Detail.Faces[Index];
        if (current.FontName == name && current.FaceName == face) return;
        Edit("font", f =>
        {
            f.FontName = name;
            f.FaceName = face;
        });
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

    partial void OnEdge2StateChanged(bool? value)
    {
        if (_loading || _editor.Font is not { } font || font.Detail.Faces[Index].UseEdge2 == value) return;
        Edit("useEdge2", face => face.UseEdge2 = value);
    }
}
