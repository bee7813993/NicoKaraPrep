using System.Text.Json.Serialization;

namespace NicoKaraPrep.Core.Model;

/// <summary>
/// ニコカラメーカー3 のフォント設定（LyricsFontModel）1 件の全項目。
/// 配色 8 箇所（<see cref="Brushes"/>）・文字種別フォント 6 種（<see cref="Faces"/>）・文字飾りを持つ。
/// </summary>
public sealed class N3FontDetail
{
    /// <summary>配色の箇所の数（BrushInfos）。</summary>
    public const int BrushCount = 8;

    /// <summary>文字種別フォントの数（FontInfos）。</summary>
    public const int FaceCount = 6;

    /// <summary>ワイプ後とワイプ前の境目（Brushes[0..3] がワイプ後、[4..7] がワイプ前）。</summary>
    public const int BeforeOffset = 4;

    /// <summary>配色の箇所の表示名（添字 = BrushInfos の添字）。</summary>
    public static readonly IReadOnlyList<string> BrushLabels = new[]
    {
        "ワイプ後の文字", "ワイプ後の縁", "ワイプ後の縁 2", "ワイプ後の飾り",
        "ワイプ前の文字", "ワイプ前の縁", "ワイプ前の縁 2", "ワイプ前の飾り",
    };

    /// <summary>文字種別フォントの表示名（添字 = FontInfos の添字）。</summary>
    public static readonly IReadOnlyList<string> FaceLabels = new[]
    {
        "歌詞／漢字", "歌詞／かな", "歌詞／英数", "ルビ／漢字", "ルビ／かな", "ルビ／英数",
    };

    private N3Brush[] _brushes = NewBrushes();
    private N3FontFace[] _faces = NewFaces();

    /// <summary>
    /// 配色 8 箇所。添字: 0 ワイプ後の文字 / 1 後の縁 / 2 後の縁 2 / 3 後の飾り / 4 ワイプ前の文字 / 5 前の縁 / 6 前の縁 2 / 7 前の飾り。
    /// 常に 8 件（足りなければ未指定の箇所で補う）。
    /// </summary>
    public N3Brush[] Brushes
    {
        get => _brushes;
        set => _brushes = Normalize(value, BrushCount, () => new N3Brush());
    }

    /// <summary>
    /// 文字種別フォント 6 種。添字: 0 歌詞／漢字 / 1 歌詞／かな / 2 歌詞／英数 / 3 ルビ／漢字 / 4 ルビ／かな / 5 ルビ／英数。
    /// 常に 6 件（足りなければ継承の行で補う）。
    /// </summary>
    public N3FontFace[] Faces
    {
        get => _faces;
        set => _faces = Normalize(value, FaceCount, () => new N3FontFace());
    }

    /// <summary>文字飾り（0 = なし / 1 = 影 / 2 = ブラー）。</summary>
    public int DecorKind { get; set; }

    /// <summary>文字飾りのサイズ px（影ならずらす距離、ブラーなら広がり）。</summary>
    public double DecorSizePx { get; set; } = 10;

    /// <summary>ブラーの濃さ（0–2）。</summary>
    public int BlurLevel { get; set; } = 2;

    /// <summary>新規フォントの既定値（歌詞／漢字 = メイリオ Bold 80px・縁 8px、配色は従来の既定色）。</summary>
    public static N3FontDetail CreateDefault()
    {
        var d = new N3FontDetail();
        d.Faces[0] = new N3FontFace { FontName = "メイリオ", FaceName = "Bold", SizePx = 80, EdgePx = 8, UseEdge2 = false, Edge2Px = 4 };
        d.Brushes[0].Color = "FFFFFF";
        d.Brushes[1].Color = "000000";
        d.Brushes[4].Color = "4DA3FF";
        d.Brushes[5].Color = "FFFFFF";
        return d;
    }

    /// <summary>深いコピー。</summary>
    public N3FontDetail Clone() => new()
    {
        Brushes = _brushes.Select(b => b.Clone()).ToArray(),
        Faces = _faces.Select(f => f.Clone()).ToArray(),
        DecorKind = DecorKind,
        DecorSizePx = DecorSizePx,
        BlurLevel = BlurLevel,
    };

    private static N3Brush[] NewBrushes() => Enumerable.Range(0, BrushCount).Select(_ => new N3Brush()).ToArray();

    private static N3FontFace[] NewFaces() => Enumerable.Range(0, FaceCount).Select(_ => new N3FontFace()).ToArray();

    private static T[] Normalize<T>(T[]? items, int count, Func<T> create) where T : class
    {
        var result = new T[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = items is not null && i < items.Length && items[i] is T item ? item : create();
        }
        return result;
    }
}

/// <summary>配色 1 箇所（BrushInfoModel）。塗りの種類を切り替えても他の種類のデータは残す（ニコカラメーカーと同じ）。</summary>
public sealed class N3Brush
{
    /// <summary>単色塗りつぶし。</summary>
    public const int TypeSolid = 0;

    /// <summary>グラデーション（縦方向）。</summary>
    public const int TypeGradient = 1;

    /// <summary>ミルフィーユ（境目がくっきりした層）。</summary>
    public const int TypeMilleFeuille = 2;

    /// <summary>画像。</summary>
    public const int TypeBitmap = 3;

    private string _color = "";
    private List<N3GradientStop> _stops = new();
    private string _bitmapPath = "";

    /// <summary>塗りの種類（ニコカラメーカーの SelectedBrushTypeIndex。0 単色 / 1 グラデーション / 2 ミルフィーユ / 3 画像）。</summary>
    public int Type { get; set; }

    /// <summary>単色の色 "RRGGBB"。空は未指定（書き出し時: マージならベースを維持、新規なら既定色）。単色以外の箇所でも前の単色が残る。</summary>
    public string Color
    {
        get => _color;
        set => _color = value ?? "";
    }

    /// <summary>単色の不透明度 %（0–100。ニコカラメーカーの DxColor.A = AlphaPercent / 100）。</summary>
    public int AlphaPercent { get; set; } = 100;

    /// <summary>
    /// グラデーション・ミルフィーユのマーカー（位置 0 が上端、1 が下端）。
    /// 空は未指定（書き出し時: マージならベースを維持、新規ならニコカラメーカーの既定 3 点）。
    /// null は空の一覧に、一覧の中の null は取り除く（手で編集した JSON など）。
    /// </summary>
    public List<N3GradientStop> Stops
    {
        get => _stops;
        set
        {
            _stops = value ?? new();
            _stops.RemoveAll(s => s is null);
        }
    }

    /// <summary>画像ファイルのパス。空は未指定（塗りの種類が画像でなければ、マージではベースの画像の設定を維持）。</summary>
    public string BitmapPath
    {
        get => _bitmapPath;
        set => _bitmapPath = value ?? "";
    }

    /// <summary>画像の拡大率 %。</summary>
    public int BitmapScale { get; set; } = 100;

    /// <summary>何も指定していない箇所か（書き出しのマージでベースの値を維持する）。色が 16 進 6 桁でなければ未指定とみなす。</summary>
    [JsonIgnore]
    public bool IsUnset =>
        Type == TypeSolid && !N3FontSet.IsValidWeb16(Color) && Stops.Count == 0 && BitmapPath.Length == 0;

    /// <summary>ニコカラメーカーが新規の箇所に入れるマーカー（0 白 / 0.5 灰 / 1 灰）。</summary>
    public static List<N3GradientStop> DefaultStops() => new()
    {
        new N3GradientStop { Position = 0, Color = "FFFFFF" },
        new N3GradientStop { Position = 0.5, Color = "808080" },
        new N3GradientStop { Position = 1, Color = "808080" },
    };

    /// <summary>深いコピー。</summary>
    public N3Brush Clone()
    {
        var c = (N3Brush)MemberwiseClone();
        c.Stops = Stops.Select(s => s.Clone()).ToList();
        return c;
    }
}

/// <summary>グラデーション・ミルフィーユのマーカー 1 つ。</summary>
public sealed class N3GradientStop
{
    /// <summary>位置（0–1。0 が上端）。</summary>
    public double Position { get; set; }

    private string _color = "FFFFFF";

    /// <summary>色 "RRGGBB"。</summary>
    public string Color
    {
        get => _color;
        set => _color = value ?? "";
    }

    /// <summary>不透明度 %（0–100）。</summary>
    public int AlphaPercent { get; set; } = 100;

    public N3GradientStop Clone() => (N3GradientStop)MemberwiseClone();
}

/// <summary>
/// 文字種別フォント 1 種（FontFaceInfoModel）。
/// 空文字・0・null は「継承」（歌詞／かな・英数とルビ／漢字は歌詞／漢字を、ルビ／かな・英数はルビ／漢字を参照する）。
/// ただしルビ／漢字のサイズ・縁・縁 2 の 0 は、NicoKaraPrep では「歌詞の半分」として書き出す。
/// 全項目を持たないフォント（<see cref="N3FontSet.HasFullDetail"/> が false）を同名のフォント設定があるベースへマージするときは、
/// 歌詞／漢字の横倍率と、ルビ／漢字のフォント名・フェイス・横倍率・縁 2 の有無・縁 2 の幅は、継承ならベースの値を残す
/// （従来の項目しか持たないフォントを書き出してもベースの設定を消さないため）。
/// </summary>
public sealed class N3FontFace
{
    private string _fontName = "";
    private string _faceName = "";

    /// <summary>フォントファミリー名（空 = 継承）。</summary>
    public string FontName
    {
        get => _fontName;
        set => _fontName = value ?? "";
    }

    /// <summary>フェイス名（"Bold" "ﾍﾋﾞｰ" など。空 = 継承）。</summary>
    public string FaceName
    {
        get => _faceName;
        set => _faceName = value ?? "";
    }

    /// <summary>文字サイズ px（0 = 継承）。</summary>
    public double SizePx { get; set; }

    /// <summary>横方向の拡大率 %（0 = 継承）。</summary>
    public int XScale { get; set; }

    /// <summary>縁の幅 px（0 = 継承）。</summary>
    public double EdgePx { get; set; }

    /// <summary>縁 2（外側の縁）を付けるか（null = 継承）。</summary>
    public bool? UseEdge2 { get; set; }

    /// <summary>縁 2 の幅 px（0 = 継承）。</summary>
    public double Edge2Px { get; set; }

    /// <summary>すべての項目が継承か。</summary>
    [JsonIgnore]
    public bool IsInherited =>
        FontName.Length == 0 && FaceName.Length == 0 && SizePx == 0 && XScale == 0 && EdgePx == 0 && UseEdge2 is null && Edge2Px == 0;

    public N3FontFace Clone() => (N3FontFace)MemberwiseClone();
}
