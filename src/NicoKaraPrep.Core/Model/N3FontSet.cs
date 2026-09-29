using System.Globalization;
using System.Text.Json.Serialization;

namespace NicoKaraPrep.Core.Model;

/// <summary>
/// NicoKaraPrep 側で定義するニコカラメーカー3 の「フォント設定」1 件。
/// n3proj 書き出し時に LyricsFonts（フォント設定タブ）へ変換する。
/// 名称を歌詞中のパート記号（@Emoji の置き換え文字列など）と同じにしておくと、
/// ニコカラメーカーの「歌詞の文字と同じ名称のフォント設定を適用する」と同様に、
/// その記号以降の文字へ自動的に適用される。
/// 全項目は <see cref="Detail"/> に持つ。FontFamily・SizePx・8 色などの項目は Detail への窓口で、
/// 旧版のアプリでも読めるよう JSON には従来どおり書き出す（読み込み時は後に来る Detail が勝つ）。
/// 色は Web 形式の 16 進 6 桁（"FFFFFF"）。空文字はベース n3proj の値を維持する。
/// </summary>
public sealed class N3FontSet
{
    private string _id = "";
    private N3FontDetail? _detail;

    /// <summary>設定の名称（ニコカラメーカーのフォント設定タブ名）。</summary>
    public string Name { get; set; } = "";

    public string FontFamily
    {
        get => Detail.Faces[0].FontName;
        set => Detail.Faces[0].FontName = value ?? "";
    }

    /// <summary>フェイス名（"Bold" "太字" "ﾍﾋﾞｰ" など。空 = 標準）。</summary>
    public string FontFace
    {
        get => Detail.Faces[0].FaceName;
        set => Detail.Faces[0].FaceName = value ?? "";
    }

    /// <summary>フォントサイズ px（画面高さ 1080 基準）。</summary>
    public double SizePx
    {
        get => Detail.Faces[0].SizePx;
        set => Detail.Faces[0].SizePx = value;
    }

    /// <summary>縁の幅 px。</summary>
    public double EdgePx
    {
        get => Detail.Faces[0].EdgePx;
        set => Detail.Faces[0].EdgePx = value;
    }

    /// <summary>縁 2 を付けるか（歌詞／漢字。継承（null）は false として見せる）。</summary>
    public bool UseEdge2
    {
        get => Detail.Faces[0].UseEdge2 ?? false;
        set => Detail.Faces[0].UseEdge2 = value;
    }

    public double Edge2Px
    {
        get => Detail.Faces[0].Edge2Px;
        set => Detail.Faces[0].Edge2Px = value;
    }

    /// <summary>ルビのフォントサイズ px（0 = 歌詞の半分）。</summary>
    public double RubySizePx
    {
        get => Detail.Faces[3].SizePx;
        set => Detail.Faces[3].SizePx = value;
    }

    /// <summary>ルビの縁の幅 px（0 = 歌詞の半分）。</summary>
    public double RubyEdgePx
    {
        get => Detail.Faces[3].EdgePx;
        set => Detail.Faces[3].EdgePx = value;
    }

    // ワイプ後（歌唱済み）の配色。単色以外の箇所は ""（ベースを維持）として見せる。
    public string TextColorAfter { get => GetSolid(0); set => SetSolid(0, value); }
    public string EdgeColorAfter { get => GetSolid(1); set => SetSolid(1, value); }
    public string Edge2ColorAfter { get => GetSolid(2); set => SetSolid(2, value); }
    public string DecorColorAfter { get => GetSolid(3); set => SetSolid(3, value); }

    // ワイプ前（未歌唱）の配色
    public string TextColorBefore { get => GetSolid(4); set => SetSolid(4, value); }
    public string EdgeColorBefore { get => GetSolid(5); set => SetSolid(5, value); }
    public string Edge2ColorBefore { get => GetSolid(6); set => SetSolid(6, value); }
    public string DecorColorBefore { get => GetSolid(7); set => SetSolid(7, value); }

    /// <summary>文字飾り（0 = なし / 1 = 影 / 2 = ブラー）。</summary>
    public int DecorKind
    {
        get => Detail.DecorKind;
        set => Detail.DecorKind = value;
    }

    public double DecorSizePx
    {
        get => Detail.DecorSizePx;
        set => Detail.DecorSizePx = value;
    }

    /// <summary>ブラーの濃さ（0–2）。</summary>
    public int BlurLevel
    {
        get => Detail.BlurLevel;
        set => Detail.BlurLevel = value;
    }

    /// <summary>NicoKaraPrep 内の識別子（Guid 文字列）。保存されていなければ最初に参照したときに付与する。</summary>
    public string Id
    {
        get
        {
            if (string.IsNullOrEmpty(_id)) _id = Guid.NewGuid().ToString();
            return _id;
        }
        set => _id = value ?? "";
    }

    /// <summary>n3proj・テンプレート（.tpl）から取り込んだときのニコカラメーカーの Guid。</summary>
    public string? NkmGuid { get; set; }

    /// <summary>取り込み時にニコカラメーカーのテンプレートと連動していたか。NicoKaraPrep で編集したら false にする。</summary>
    public bool NkmSynchronize { get; set; }

    /// <summary>取り込んだ日時（UTC）。</summary>
    public DateTime? ImportedUtc { get; set; }

    /// <summary>取り込み元のファイル（n3proj・.tpl）。</summary>
    public string? ImportedFrom { get; set; }

    /// <summary>
    /// 全項目（<see cref="Detail"/>）が NicoKaraPrep 側で決めた値か。n3proj・テンプレート（.tpl）から取り込んだときと、
    /// NicoKaraPrep で編集したとき（<see cref="N3FontLibrary.MarkEdited"/>）に true にする。
    /// false のフォント（旧版の設定・従来の項目だけで作ったもの）は、同名のフォント設定があるベースへのマージで、
    /// 歌詞／漢字の横倍率とルビ／漢字のフォント名・フェイス・横倍率・縁 2 の有無・縁 2 の幅が継承ならベースの値を残す。
    /// true のフォントはそれらも継承として書き出す（継承に戻す編集を反映する）。
    /// </summary>
    public bool HasFullDetail { get; set; }

    /// <summary>全項目（常に non-null）。JSON では既存の項目の後に書く。</summary>
    [JsonPropertyOrder(1)]
    public N3FontDetail Detail
    {
        get => _detail ??= N3FontDetail.CreateDefault();
        set => _detail = value ?? N3FontDetail.CreateDefault();
    }

    /// <summary>深いコピー（Id も同じ）。</summary>
    public N3FontSet Clone()
    {
        var c = (N3FontSet)MemberwiseClone();
        c._id = Id;
        c._detail = _detail?.Clone();
        return c;
    }

    private string GetSolid(int index)
    {
        var b = Detail.Brushes[index];
        return b.Type == N3Brush.TypeSolid ? b.Color : "";
    }

    /// <summary>単色の色を設定する。有効な色なら単色に切り替え、空（未指定）なら単色の箇所だけ色を消す。</summary>
    private void SetSolid(int index, string? value)
    {
        var b = Detail.Brushes[index];
        if (IsValidWeb16(value))
        {
            b.Type = N3Brush.TypeSolid;
            b.Color = value!;
        }
        else if (b.Type == N3Brush.TypeSolid)
        {
            b.Color = value ?? "";
        }
    }

    /// <summary>"RRGGBB"（先頭の # は無視）を解析する。</summary>
    public static bool TryParseWeb16(string? text, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string s = text.Trim().TrimStart('#');
        if (s.Length != 6) return false;
        if (!byte.TryParse(s.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)) return false;
        if (!byte.TryParse(s.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g)) return false;
        if (!byte.TryParse(s.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b)) return false;
        return true;
    }

    /// <summary>16 進 6 桁として妥当な色文字列か（空は「未指定」として false）。</summary>
    public static bool IsValidWeb16(string? text) => TryParseWeb16(text, out _, out _, out _);

    /// <summary>色文字列を "RRGGBB"（大文字、# なし）にそろえる。妥当でなければ空。</summary>
    public static string NormalizeWeb16(string? text) =>
        TryParseWeb16(text, out byte r, out byte g, out byte b) ? $"{r:X2}{g:X2}{b:X2}" : "";
}
