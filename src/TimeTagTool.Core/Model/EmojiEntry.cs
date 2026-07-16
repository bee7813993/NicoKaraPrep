using System.Text;

namespace TimeTagTool.Core.Model;

/// <summary>
/// ニコカラメーカー3 の @Emoji タグ 1 件。
/// 書式: @Emoji=置き換える文字,ワイプ前画像[,ワイプ後画像][,オプション...]
/// オプション例: Zoom=150% / Fix / NoDecor / MarginLeft=10
/// </summary>
public sealed class EmojiEntry
{
    /// <summary>画像に置き換える文字列（「（花帆）」のような複数文字も可）。</summary>
    public string ReplaceChar { get; set; } = "";

    /// <summary>ワイプ前画像ファイル。</summary>
    public string ImageBefore { get; set; } = "";

    /// <summary>ワイプ後画像ファイル（null でワイプなし表現）。</summary>
    public string? ImageAfter { get; set; }

    /// <summary>オプション（"Zoom=150%,Fix" のような生文字列）。</summary>
    public string? Options { get; set; }

    /// <summary>パレットのスロット番号（1–20）。タグ由来で未割当なら null。</summary>
    public int? Slot { get; set; }

    /// <summary>@Emoji= の右辺（値部分）を生成する。</summary>
    public string ToTagValue()
    {
        var sb = new StringBuilder();
        sb.Append(ReplaceChar);
        sb.Append(',').Append(ImageBefore);
        if (!string.IsNullOrEmpty(ImageAfter))
        {
            sb.Append(',').Append(ImageAfter);
        }
        else if (!string.IsNullOrEmpty(Options))
        {
            sb.Append(',');
        }
        if (!string.IsNullOrEmpty(Options)) sb.Append(',').Append(Options);
        return sb.ToString();
    }

    /// <summary>@Emoji= の右辺（値部分）を解析する。</summary>
    public static EmojiEntry ParseTagValue(string value)
    {
        var parts = value.Split(',');
        var e = new EmojiEntry
        {
            ReplaceChar = parts.Length > 0 ? parts[0] : "",
            ImageBefore = parts.Length > 1 ? parts[1] : "",
        };
        if (parts.Length > 2)
        {
            string third = parts[2];
            if (LooksLikeOption(third))
            {
                e.ImageAfter = null;
                e.Options = string.Join(',', parts[2..]);
            }
            else
            {
                e.ImageAfter = string.IsNullOrEmpty(third) ? null : third;
                if (parts.Length > 3) e.Options = string.Join(',', parts[3..]);
            }
        }
        return e;
    }

    private static bool LooksLikeOption(string s) =>
        s.Contains('=') ||
        s.Equals("Fix", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("NoDecor", StringComparison.OrdinalIgnoreCase);

    /// <summary>解析済みの @Emoji オプション。</summary>
    /// <param name="ZoomPercent">Zoom=n[%]（デフォルト 100）。</param>
    /// <param name="Fix">元サイズ維持。</param>
    /// <param name="NoDecor">文字飾りなし。</param>
    /// <param name="MarginLeft">左余白 px。</param>
    /// <param name="MarginRight">右余白 px。</param>
    /// <param name="MarginBottom">下余白 px。</param>
    public sealed record EmojiOptions(double ZoomPercent, bool Fix, bool NoDecor, double MarginLeft, double MarginRight, double MarginBottom)
    {
        public static readonly EmojiOptions Default = new(100, false, false, 0, 0, 0);
    }

    /// <summary>オプション文字列（"NoDecor,Zoom=150,MarginRight=20" など）を解析する。</summary>
    public EmojiOptions ParseOptions()
    {
        double zoom = 100, marginL = 0, marginR = 0, marginB = 0;
        bool fix = false, noDecor = false;
        foreach (string part in (Options ?? "").Split(','))
        {
            string p = part.Trim();
            if (p.Length == 0) continue;
            if (p.Equals("Fix", StringComparison.OrdinalIgnoreCase)) { fix = true; continue; }
            if (p.Equals("NoDecor", StringComparison.OrdinalIgnoreCase)) { noDecor = true; continue; }
            int eq = p.IndexOf('=');
            if (eq <= 0) continue;
            string name = p[..eq].Trim();
            string value = p[(eq + 1)..].Trim().TrimEnd('%');
            if (!double.TryParse(value, out double v)) continue;
            if (name.Equals("Zoom", StringComparison.OrdinalIgnoreCase) && v > 0) zoom = v;
            else if (name.Equals("MarginLeft", StringComparison.OrdinalIgnoreCase)) marginL = v;
            else if (name.Equals("MarginRight", StringComparison.OrdinalIgnoreCase)) marginR = v;
            else if (name.Equals("MarginBottom", StringComparison.OrdinalIgnoreCase)) marginB = v;
        }
        return new EmojiOptions(zoom, fix, noDecor, marginL, marginR, marginB);
    }

    public EmojiEntry Clone() => new()
    {
        ReplaceChar = ReplaceChar,
        ImageBefore = ImageBefore,
        ImageAfter = ImageAfter,
        Options = Options,
        Slot = Slot,
    };
}
