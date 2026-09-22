using System.Globalization;

namespace NicoKaraPrep.Core.Model;

/// <summary>
/// NicoKaraPrep 側で定義するニコカラメーカー3 の「フォント設定」1 件。
/// n3proj 書き出し時に LyricsFonts（フォント設定タブ）へ変換する。
/// 名称を歌詞中のパート記号（@Emoji の置き換え文字列など）と同じにしておくと、
/// ニコカラメーカーの「歌詞の文字と同じ名称のフォント設定を適用する」と同様に、
/// その記号以降の文字へ自動的に適用される。
/// 色は Web 形式の 16 進 6 桁（"FFFFFF"）。空文字はベース n3proj の値を維持する。
/// </summary>
public sealed class N3FontSet
{
    /// <summary>設定の名称（ニコカラメーカーのフォント設定タブ名）。</summary>
    public string Name { get; set; } = "";

    public string FontFamily { get; set; } = "メイリオ";

    /// <summary>フェイス名（"Bold" "太字" "ﾍﾋﾞｰ" など。空 = 標準）。</summary>
    public string FontFace { get; set; } = "Bold";

    /// <summary>フォントサイズ px（画面高さ 1080 基準）。</summary>
    public double SizePx { get; set; } = 80;

    /// <summary>縁の幅 px。</summary>
    public double EdgePx { get; set; } = 8;

    public bool UseEdge2 { get; set; }

    public double Edge2Px { get; set; } = 4;

    /// <summary>ルビのフォントサイズ px（0 = 歌詞の半分）。</summary>
    public double RubySizePx { get; set; }

    /// <summary>ルビの縁の幅 px（0 = 歌詞の半分）。</summary>
    public double RubyEdgePx { get; set; }

    // ワイプ後（歌唱済み）の配色
    public string TextColorAfter { get; set; } = "FFFFFF";
    public string EdgeColorAfter { get; set; } = "000000";
    public string Edge2ColorAfter { get; set; } = "";
    public string DecorColorAfter { get; set; } = "";

    // ワイプ前（未歌唱）の配色
    public string TextColorBefore { get; set; } = "4DA3FF";
    public string EdgeColorBefore { get; set; } = "FFFFFF";
    public string Edge2ColorBefore { get; set; } = "";
    public string DecorColorBefore { get; set; } = "";

    /// <summary>文字飾り（0 = なし / 1 = 影 / 2 = ブラー）。</summary>
    public int DecorKind { get; set; }

    public double DecorSizePx { get; set; } = 10;

    /// <summary>ブラーの濃さ（0–2）。</summary>
    public int BlurLevel { get; set; } = 2;

    public N3FontSet Clone() => (N3FontSet)MemberwiseClone();

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
}
