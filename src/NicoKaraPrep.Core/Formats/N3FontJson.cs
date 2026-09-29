using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Formats;

/// <summary>
/// ニコカラメーカー3 のフォント設定（LyricsFontModel の JSON）を NicoKaraPrep のモデルへ読み取る部品。
/// n3proj の読み込み・テンプレート（.tpl）の読み込み・書き出し時の比較で共通に使う。
/// 数値はファイルから読んだ値でもメモリ上で作った値（float・int）でも読めるようにしている。
/// </summary>
internal static class N3FontJson
{
    /// <summary>LyricsFontModel 1 件を読み取る。サイズは画面高さ <paramref name="height"/> 換算の px。</summary>
    public static N3FontSet ParseFontSet(JsonObject set, int height)
    {
        var d = new N3FontDetail();
        if (set["BrushInfos"] is JsonArray brushes)
        {
            for (int i = 0; i < N3FontDetail.BrushCount && i < brushes.Count; i++)
            {
                if (brushes[i] is JsonObject b) d.Brushes[i] = ParseBrush(b);
            }
        }
        if (set["FontInfos"] is JsonArray faces)
        {
            for (int i = 0; i < N3FontDetail.FaceCount && i < faces.Count; i++)
            {
                if (faces[i] is JsonObject f) d.Faces[i] = ParseFace(f, height);
            }
        }
        d.DecorKind = Int(set["DecorKind"]) ?? 0;
        d.DecorSizePx = SizePx(set["DecorSize"], height);
        d.BlurLevel = Int(set["BlurLevel"]) ?? 2;

        string guid = Str(set["Guid"]) ?? "";
        return new N3FontSet
        {
            Name = Str(set["SettingsName"]) ?? "",
            Detail = d,
            NkmGuid = guid.Length > 0 ? guid : null,
            NkmSynchronize = Bool(set["Synchronize"]) ?? false,
            ImportedUtc = DateTime.UtcNow,
        };
    }

    /// <summary>BrushInfoModel 1 件を読み取る（単色以外の箇所でも SolidColor に残っている色を読む）。</summary>
    public static N3Brush ParseBrush(JsonObject b)
    {
        var (color, alpha) = SolidColorOf(b["SolidColor"]);
        return new N3Brush
        {
            Type = Int(b["SelectedBrushTypeIndex"]) ?? 0,
            Color = color,
            AlphaPercent = alpha,
            Stops = ParseStops(b["GradientStops"]),
            BitmapPath = Str(b["BitmapPath"]) ?? "",
            BitmapScale = Int(b["BitmapScale"]) ?? 100,
        };
    }

    /// <summary>FontFaceInfoModel 1 件を読み取る（XScale が無い古いデータは 0 = 継承）。</summary>
    public static N3FontFace ParseFace(JsonObject f, int height) => new()
    {
        FontName = Str(f["FontName"]) ?? "",
        FaceName = Str(f["FontFaceName"]) ?? "",
        SizePx = SizePx(f["CharSize"], height),
        XScale = Int(f["XScale"]) ?? 0,
        EdgePx = SizePx(f["EdgeSize"], height),
        UseEdge2 = Bool(f["UseEdge2"]),
        Edge2Px = SizePx(f["EdgeSize2"], height),
    };

    /// <summary>GradientStops を読み取る。</summary>
    public static List<N3GradientStop> ParseStops(JsonNode? node)
    {
        var result = new List<N3GradientStop>();
        if (node is not JsonArray stops) return result;
        foreach (var s in stops)
        {
            if (s is not JsonObject o) continue;
            result.Add(new N3GradientStop
            {
                Position = Number(o["Position"]) ?? 0,
                Color = Web16Of(o["Color"]),
                AlphaPercent = AlphaOf(o["Color"]),
            });
        }
        return result;
    }

    /// <summary>ColorBindModel の色（Web16 を優先し、無ければ DxColor から）と不透明度 %。</summary>
    public static (string Color, int AlphaPercent) SolidColorOf(JsonNode? solidColor)
    {
        if (solidColor is not JsonObject sc) return ("", 100);
        string color = N3FontSet.NormalizeWeb16(Str(sc["Web16"]));
        if (color.Length == 0) color = Web16Of(sc["DxColor"]);
        return (color, AlphaOf(sc["DxColor"]));
    }

    /// <summary>Color4（R/G/B は 0–1）を "RRGGBB" にする。色が無ければ空。</summary>
    public static string Web16Of(JsonNode? color4)
    {
        if (color4 is not JsonObject c || Number(c["R"]) is not double r || Number(c["G"]) is not double g || Number(c["B"]) is not double b)
        {
            return "";
        }
        return $"{ToByte(r):X2}{ToByte(g):X2}{ToByte(b):X2}";
    }

    /// <summary>Color4 の A（0–1）を不透明度 %（0–100、四捨五入）にする。無ければ 100。</summary>
    public static int AlphaOf(JsonNode? color4)
    {
        if (color4 is not JsonObject c || Number(c["A"]) is not double a) return 100;
        return (int)Math.Clamp(Math.Round(a * 100, MidpointRounding.AwayFromZero), 0, 100);
    }

    /// <summary>SizeAndRatio を画面高さ換算の px にする（Ratio があればそれを、無ければ Size と Reference を使う。小数第 1 位に丸める）。</summary>
    public static double SizePx(JsonNode? sizeAndRatio, int height)
    {
        if (sizeAndRatio is not JsonObject o) return 0;
        double ratio = Number(o["Ratio"]) ?? 0;
        double size = Number(o["Size"]) ?? 0;
        double reference = Number(o["Reference"]) ?? height;
        double px = ratio > 0 ? ratio * height : (reference > 0 ? size * height / reference : size);
        return Math.Round(px, 1);
    }

    /// <summary>JSON の数値（ファイルから読んだ値でもメモリ上の float・int でも）。数値でなければ null。</summary>
    public static double? Number(JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        if (v.TryGetValue(out double d)) return d;
        if (v.TryGetValue(out JsonElement e)) return e.ValueKind == JsonValueKind.Number ? e.GetDouble() : null;
        if (v.GetValueKind() != JsonValueKind.Number) return null;
        return double.TryParse(v.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : null;
    }

    /// <summary>JSON の整数（小数なら四捨五入）。数値でなければ null。</summary>
    public static int? Int(JsonNode? node)
    {
        if (node is JsonValue v && v.TryGetValue(out int i)) return i;
        return Number(node) is double d ? (int)Math.Round(d, MidpointRounding.AwayFromZero) : null;
    }

    /// <summary>JSON の文字列。文字列でなければ null。</summary>
    public static string? Str(JsonNode? node) => node is JsonValue v && v.TryGetValue(out string? s) ? s : null;

    /// <summary>JSON の真偽値。null・欠落・真偽値以外は null。</summary>
    public static bool? Bool(JsonNode? node) => node is JsonValue v && v.TryGetValue(out bool b) ? b : null;

    private static byte ToByte(double x) => (byte)Math.Clamp(Math.Round(x * 255, MidpointRounding.AwayFromZero), 0, 255);
}
