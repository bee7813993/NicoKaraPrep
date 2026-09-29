using System.Text.Json.Nodes;

namespace NicoKaraPrep.Core.Tests;

/// <summary>フォント設定のテストで使う、ニコカラメーカーが保存した形の JSON。</summary>
internal static class N3FontSamples
{
    /// <summary>
    /// ニコカラメーカーが保存した「（麻衣）（のりこ）」（ミルフィーユ 3 箇所）の形の JSON。
    /// ワイプ後の飾りだけ半透明、歌詞／かなは XScale のキーが無い古い形。
    /// </summary>
    public static JsonObject NkmFont()
    {
        static JsonObject Color(double r, double g, double b, double a = 1) => new()
        {
            ["R"] = r, ["G"] = g, ["B"] = b, ["A"] = a, ["SumRGB"] = r + g + b, ["Average"] = (r + g + b) / 3, ["Luma"] = 0.299 * r + 0.587 * g + 0.114 * b,
        };
        static JsonArray DefaultStops() => new(
            new JsonObject { ["Position"] = 0, ["Color"] = Color(1, 1, 1) },
            new JsonObject { ["Position"] = 0.5, ["Color"] = Color(0.5019608, 0.5019608, 0.5019608) },
            new JsonObject { ["Position"] = 1, ["Color"] = Color(0.5019608, 0.5019608, 0.5019608) });
        static JsonObject Brush(int type, string web16, JsonObject dx, JsonArray stops) => new()
        {
            ["SelectedBrushTypeIndex"] = type,
            ["SolidColor"] = new JsonObject { ["DxColor"] = dx, ["Web16"] = web16, ["Guid"] = "bec83c4f-e3c5-4e8d-9d68-042878d48165" },
            ["GradientStops"] = stops,
            ["BitmapPath"] = "",
            ["BitmapScale"] = 100,
            ["SettingsName"] = "縁取り色",
            ["Guid"] = "5733235b-2b6d-4807-b4eb-7dce5f5aed03",
        };
        var mille = new JsonArray(
            new JsonObject { ["Position"] = 0, ["Color"] = Color(0.4, 0.77254903, 0.9254902) },
            new JsonObject { ["Position"] = 0.5, ["Color"] = Color(0.68235296, 0.38431373, 1) },
            new JsonObject { ["Position"] = 1, ["Color"] = Color(0.5019608, 0.5019608, 0.5019608) });
        var gray = Color(0.60784316, 0.60784316, 0.60784316);
        var brushes = new JsonArray
        {
            Brush(0, "FFFFFF", Color(1, 1, 1), DefaultStops()),
            Brush(2, "9B9B9B", gray.DeepClone().AsObject(), mille.DeepClone().AsArray()),
            Brush(0, "000000", Color(0, 0, 0), DefaultStops()),
            Brush(0, "FFFFFF", Color(1, 1, 1, 0.5), DefaultStops()),
            Brush(2, "9B9B9B", gray.DeepClone().AsObject(), mille.DeepClone().AsArray()),
            Brush(0, "FFFFFF", Color(1, 1, 1), DefaultStops()),
            Brush(0, "FFFFFF", Color(1, 1, 1), DefaultStops()),
            Brush(2, "9B9B9B", gray.DeepClone().AsObject(), mille.DeepClone().AsArray()),
        };
        static JsonObject Size(int size) => new() { ["Size"] = size, ["Reference"] = size == 0 ? 0 : 1080, ["Ratio"] = size / 1080.0 };
        static JsonObject Face(string name, string face, int size, int edge, bool? useEdge2, int edge2, bool xscale) =>
            xscale
                ? new JsonObject { ["FontName"] = name, ["FontFaceName"] = face, ["CharSize"] = Size(size), ["XScale"] = 0, ["EdgeSize"] = Size(edge), ["UseEdge2"] = useEdge2, ["EdgeSize2"] = Size(edge2), ["FallbackName"] = "歌詞／漢字", ["Guid"] = Guid.NewGuid().ToString() }
                : new JsonObject { ["FontName"] = name, ["FontFaceName"] = face, ["CharSize"] = Size(size), ["EdgeSize"] = Size(edge), ["UseEdge2"] = useEdge2, ["EdgeSize2"] = Size(edge2), ["FallbackName"] = "歌詞／漢字", ["Guid"] = Guid.NewGuid().ToString() };
        return new JsonObject
        {
            ["BrushInfos"] = brushes,
            ["FontInfos"] = new JsonArray(
                Face("HGS創英角ﾎﾟｯﾌﾟ体", "ﾍﾋﾞｰ", 80, 15, null, 5, true),
                Face("", "", 0, 0, null, 0, false),
                Face("", "", 0, 0, null, 0, true),
                Face("", "", 40, 10, false, 3, true),
                Face("", "", 0, 0, null, 0, true),
                Face("", "", 0, 0, null, 0, true)),
            ["DecorKind"] = 2,
            ["DecorSize"] = Size(10),
            ["BlurLevel"] = 0,
            ["SettingsName"] = "（麻衣）（のりこ）",
            ["Index"] = 20,
            ["Synchronize"] = true,
            ["SynchronizedTime"] = "2025-10-16T07:49:28.0429175Z",
            ["Guid"] = "4ad6be49-3d45-47e4-9aee-b4c14a0f6057",
            ["LastModified"] = "2026-09-22T05:57:46.3454048Z",
            ["CreateAppVer"] = "Ver 10.74",
            ["ModifyAppVer"] = "Ver 13.79",
        };
    }

    /// <summary>フォント設定だけを持つ n3proj の JSON（画面の高さ 1080）。</summary>
    public static JsonObject ProjectOf(params JsonObject[] fonts) => new()
    {
        ["SourceInfo"] = new JsonObject { ["BackgroundWidth"] = 1920, ["BackgroundHeight"] = 1080 },
        ["LyricsFonts"] = new JsonArray(fonts.Select(f => (JsonNode)f).ToArray()),
    };
}
