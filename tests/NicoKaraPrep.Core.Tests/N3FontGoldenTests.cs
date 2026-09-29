using System.Text.Json.Nodes;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Tests;

/// <summary>
/// 実データ（ニコカラメーカー3 が保存した n3proj・フォント設定テンプレート）を使うフォント設定の往復テスト。
/// 環境変数が無いときは何もしない。
/// </summary>
public class N3FontGoldenTests
{
    private const double Tolerance = 1e-6;

    private static N3ProjWriter.TabSource DummyTab()
    {
        var doc = LrcFormat.Parse("[00:01:00]あ[00:02:00]\r\n");
        const string path = @"C:\v\golden.lrc";
        return new N3ProjWriter.TabSource(new N3ProjExportTab { Name = "メイン", Document = doc, LyricsPath = path }, path, LrcFormat.Write(doc), DateTime.Now);
    }

    /// <summary>
    /// n3proj のフォント設定を読み、同じ n3proj をベースにしてマージ書き出しすると、LyricsFonts が元と一致する
    /// （LastModified・ModifyAppVer を除く。数値は 1e-6 の許容）。値を壊したベースへマージしても同じになることも確かめる。
    /// あわせて、ベース無しで新規に書き出した LyricsFonts も、識別用の項目（下位の Guid・日時・版）を除いて元と一致することを確かめる。
    /// 環境変数 TTT_N3PROJ_SAMPLE に n3proj のパスを設定して実行する。
    /// </summary>
    [Fact]
    public void ゴールデン_読み込んだフォント設定を同じn3projへマージすると元と一致する()
    {
        string? sample = Environment.GetEnvironmentVariable("TTT_N3PROJ_SAMPLE");
        if (string.IsNullOrEmpty(sample) || !File.Exists(sample)) return;

        var original = N3ProjFormat.ReadJsonObject(sample);
        var expected = original["LyricsFonts"]!.AsArray();
        var fontSets = N3ProjFormat.ReadFontSets(sample);
        Assert.Equal(expected.Count, fontSets.Count);

        // マージ（同じファイルをベースにする）
        var baseRoot = (JsonObject)original.DeepClone();
        var merged = N3ProjWriter.BuildProjectJson(@"C:\v\golden.n3proj", new[] { DummyTab() },
            new N3ProjExportOptions { BaseProject = baseRoot, FontSets = fontSets }, baseRoot, new List<string>(), out _, out int mergedCount);
        Assert.Equal(expected.Count, mergedCount);
        var mergeDiffs = new List<string>();
        Compare(expected, merged["LyricsFonts"], "LyricsFonts", mergeDiffs, strict: true);

        // 値を壊したベースへマージしても元に戻る（同じ値を書かずに残す処理に頼らず、書き込みの正しさを確かめる）
        var brokenRoot = (JsonObject)original.DeepClone();
        Break(brokenRoot["LyricsFonts"]!.AsArray(), fontSets);
        var repaired = N3ProjWriter.BuildProjectJson(@"C:\v\golden.n3proj", new[] { DummyTab() },
            new N3ProjExportOptions { BaseProject = brokenRoot, FontSets = fontSets }, brokenRoot, new List<string>(), out _, out _);
        Compare(expected, repaired["LyricsFonts"], "LyricsFonts(壊したベース)", mergeDiffs, strict: true);

        // 新規（ベース無し）
        int height = BackgroundHeight(original);
        var created = N3ProjWriter.BuildProjectJson(@"C:\v\golden.n3proj", new[] { DummyTab() },
            new N3ProjExportOptions { FontSets = fontSets, ScreenHeight = height }, null, new List<string>(), out _, out int createdCount);
        Assert.Equal(expected.Count, createdCount);
        var createDiffs = new List<string>();
        Compare(expected, created["LyricsFonts"], "LyricsFonts", createDiffs, strict: false);
        for (int i = 0; i < expected.Count; i++)
        {
            string? g1 = expected[i]!["Guid"]?.GetValue<string>();
            string? g2 = created["LyricsFonts"]![i]!["Guid"]?.GetValue<string>();
            if (!string.Equals(g1, g2, StringComparison.OrdinalIgnoreCase)) createDiffs.Add($"LyricsFonts[{i}]/Guid: {g1} ≠ {g2}");
        }

        Assert.True(mergeDiffs.Count == 0 && createDiffs.Count == 0,
            $"マージの不一致 {mergeDiffs.Count} 件:\n{string.Join("\n", mergeDiffs.Take(15))}\n" +
            $"新規の不一致 {createDiffs.Count} 件:\n{string.Join("\n", createDiffs.Take(15))}");
    }

    /// <summary>
    /// ニコカラメーカー3 のフォント設定テンプレート（*.tpl）をすべて読めることを確かめる。
    /// 展開済みの JSON（同じ名前の *.json）が同じフォルダにあれば、.tpl から読んだ結果と一致することも確かめる。
    /// 環境変数 TTT_N3TPL_DIR にフォルダを設定して実行する。
    /// </summary>
    [Fact]
    public void ゴールデン_フォント設定テンプレートを読める()
    {
        string? dir = Environment.GetEnvironmentVariable("TTT_N3TPL_DIR");
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

        var errors = new List<string>();
        var templates = N3FontTemplateReader.ReadTemplateFolder(dir, errors);
        Assert.True(errors.Count == 0, string.Join("\n", errors));
        Assert.Equal(Directory.GetFiles(dir, "*.tpl").Length, templates.Count);

        var diffs = new List<string>();
        foreach (var t in templates)
        {
            Assert.True(t.NkmSynchronize);
            Assert.Equal(Path.GetFileNameWithoutExtension(t.ImportedFrom), t.NkmGuid, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(N3FontDetail.BrushCount, t.Detail.Brushes.Length);
            Assert.Equal(N3FontDetail.FaceCount, t.Detail.Faces.Length);

            string json = Path.ChangeExtension(t.ImportedFrom!, ".json");
            if (!File.Exists(json)) continue;
            var fromJson = N3FontTemplateReader.ParseTemplateJson(File.ReadAllText(json));
            string a = System.Text.Json.JsonSerializer.Serialize(t.Detail);
            string b = System.Text.Json.JsonSerializer.Serialize(fromJson.Detail);
            if (a != b || t.Name != fromJson.Name) diffs.Add($"{Path.GetFileName(json)}: .tpl と .json の読み取り結果が違います");

            // 新規に書き出して読み戻しても同じ（テンプレートの FontInfos には XScale の無い古い形式がある）。
            // ただしルビ／漢字のサイズ・縁・縁 2 の 0 は NicoKaraPrep の仕様で歌詞の半分として書き出すので、期待値もそうする
            var written = N3ProjWriter.NewFontSet(t, N3FontTemplateReader.ReferenceHeight, "Ver 13.79");
            var back = N3ProjFormat.ParseFontSet(written, N3FontTemplateReader.ReferenceHeight);
            var expectedDetail = t.Detail.Clone();
            var ruby = N3FontLibrary.EffectiveFace(t, 3);
            if (expectedDetail.Faces[3].SizePx <= 0) expectedDetail.Faces[3].SizePx = ruby.SizePx;
            if (expectedDetail.Faces[3].EdgePx <= 0) expectedDetail.Faces[3].EdgePx = ruby.EdgePx;
            if (expectedDetail.Faces[3].Edge2Px <= 0) expectedDetail.Faces[3].Edge2Px = ruby.Edge2Px;
            string c = System.Text.Json.JsonSerializer.Serialize(expectedDetail);
            string d = System.Text.Json.JsonSerializer.Serialize(back.Detail);
            if (c != d) diffs.Add($"{Path.GetFileName(json)}: 書き出して読み戻すと変わります\n  期待 {c}\n  実際 {d}");
        }
        Assert.True(diffs.Count == 0, string.Join("\n", diffs.Take(15)));
    }

    /// <summary>
    /// マージで書き戻されるはずの値（配色 8 箇所、歌詞／漢字・ルビ／漢字と継承でないかな・英数、文字飾り、連動）を別の値に変える。
    /// キーの有無と並びは変えない。
    /// </summary>
    private static void Break(JsonArray fonts, List<N3FontSet> models)
    {
        static JsonObject Size(double px) => new() { ["Size"] = (int)px, ["Reference"] = 1080, ["Ratio"] = px / 1080 };
        static void Replace(JsonObject o, string key, JsonNode? value)
        {
            if (o.ContainsKey(key)) o[key] = value;
        }

        for (int f = 0; f < fonts.Count; f++)
        {
            var set = fonts[f]!.AsObject();
            foreach (var node in set["BrushInfos"]!.AsArray())
            {
                var b = node!.AsObject();
                Replace(b, "SelectedBrushTypeIndex", (b["SelectedBrushTypeIndex"]!.GetValue<int>() + 1) % 4);
                var sc = b["SolidColor"]!.AsObject();
                Replace(sc, "DxColor", new JsonObject { ["R"] = 0.1f, ["G"] = 0.2f, ["B"] = 0.3f, ["A"] = 0.5f, ["SumRGB"] = 0.6f, ["Average"] = 0.2f, ["Luma"] = 0.2f });
                Replace(sc, "Web16", "1A334D");
                Replace(b, "GradientStops", new JsonArray());
                Replace(b, "BitmapPath", "壊した.png");
                Replace(b, "BitmapScale", 55);
            }
            var faces = set["FontInfos"]!.AsArray();
            for (int i = 0; i < faces.Count; i++)
            {
                var model = models[f].Detail.Faces[i];
                bool kanaOrAlnum = i != 0 && i != 3;
                if (kanaOrAlnum && model.IsInherited) continue;

                // 歌詞／漢字・ルビ／漢字の従来の項目以外は、継承ならマージでベースのまま残すので壊さない
                bool lyric = i == 0;
                bool Breaks(bool legacy, bool inherited) => kanaOrAlnum || legacy || !inherited;
                var face = faces[i]!.AsObject();
                if (Breaks(lyric, model.FontName.Length == 0)) Replace(face, "FontName", "壊したフォント");
                if (Breaks(lyric, model.FaceName.Length == 0)) Replace(face, "FontFaceName", "壊したフェイス");
                Replace(face, "CharSize", Size(1));
                if (Breaks(false, model.XScale == 0)) Replace(face, "XScale", 7);
                Replace(face, "EdgeSize", Size(2));
                if (Breaks(lyric, model.UseEdge2 is null)) Replace(face, "UseEdge2", face["UseEdge2"] is null ? true : null);
                if (Breaks(lyric, model.Edge2Px <= 0)) Replace(face, "EdgeSize2", Size(3));
            }
            Replace(set, "DecorKind", (set["DecorKind"]!.GetValue<int>() + 1) % 3);
            Replace(set, "DecorSize", Size(4));
            Replace(set, "BlurLevel", (set["BlurLevel"]!.GetValue<int>() + 1) % 3);
        }
    }

    private static int BackgroundHeight(JsonObject root)
    {
        var h = root["SourceInfo"]?["BackgroundHeight"];
        return h is JsonValue v && v.TryGetValue(out int height) && height > 0 ? height : 1080;
    }

    private static readonly HashSet<string> AlwaysIgnored = new(StringComparer.Ordinal) { "LastModified", "ModifyAppVer" };

    private static readonly HashSet<string> IdentityKeys = new(StringComparer.Ordinal) { "Guid", "CreateAppVer", "SynchronizedTime", "LastModified", "ModifyAppVer" };

    /// <summary>
    /// JSON を比べる。strict = true はキーの並びまで比べ、LastModified・ModifyAppVer だけを除く。
    /// strict = false は識別用の項目を除き、キーの並びは問わず、XScale の欠落は 0、Size と Ratio が 0 のサイズは同じ（継承）とみなす。
    /// </summary>
    private static void Compare(JsonNode? expected, JsonNode? actual, string path, List<string> diffs, bool strict)
    {
        if (diffs.Count > 200) return;
        switch (expected)
        {
            case null:
                if (actual is not null) diffs.Add($"{path}: null ≠ {actual.ToJsonString()}");
                return;
            case JsonObject eo:
                if (actual is not JsonObject ao)
                {
                    diffs.Add($"{path}: オブジェクトではありません（{actual?.ToJsonString()}）");
                    return;
                }
                if (!strict && IsBlankSize(eo) && IsBlankSize(ao)) return;
                var ignored = strict ? AlwaysIgnored : IdentityKeys;
                var ek = eo.Select(kv => kv.Key).Where(k => !ignored.Contains(k)).ToList();
                var ak = ao.Select(kv => kv.Key).Where(k => !ignored.Contains(k)).ToList();
                if (!strict)
                {
                    if (!ek.Contains("XScale") && ak.Contains("XScale") && ao["XScale"] is JsonValue xs && xs.ToJsonString() == "0") ak.Remove("XScale");
                    ek.Sort(StringComparer.Ordinal);
                    ak.Sort(StringComparer.Ordinal);
                }
                if (!ek.SequenceEqual(ak))
                {
                    diffs.Add($"{path}: キーが違います\n  期待 {string.Join(",", ek)}\n  実際 {string.Join(",", ak)}");
                    return;
                }
                foreach (string k in ek) Compare(eo[k], ao[k], $"{path}/{k}", diffs, strict);
                return;
            case JsonArray ea:
                if (actual is not JsonArray aa || aa.Count != ea.Count)
                {
                    diffs.Add($"{path}: 配列の要素数が違います（{ea.Count} ≠ {(actual as JsonArray)?.Count}）");
                    return;
                }
                for (int i = 0; i < ea.Count; i++) Compare(ea[i], aa[i], $"{path}[{i}]", diffs, strict);
                return;
            default:
                if (actual is not JsonValue av)
                {
                    diffs.Add($"{path}: {expected.ToJsonString()} ≠ {actual?.ToJsonString()}");
                    return;
                }
                if (TryNumber(expected, out double en) && TryNumber(av, out double an))
                {
                    if (Math.Abs(en - an) > Tolerance) diffs.Add($"{path}: {expected.ToJsonString()} ≠ {av.ToJsonString()}");
                    return;
                }
                if (expected.ToJsonString() != av.ToJsonString()) diffs.Add($"{path}: {expected.ToJsonString()} ≠ {av.ToJsonString()}");
                return;
        }
    }

    private static bool IsBlankSize(JsonObject o) =>
        o.ContainsKey("Size") && o.ContainsKey("Ratio") && o.Count <= 3 &&
        TryNumber(o["Size"], out double s) && s == 0 && TryNumber(o["Ratio"], out double r) && r == 0;

    private static bool TryNumber(JsonNode? node, out double value)
    {
        value = 0;
        if (node is not JsonValue v || v.GetValueKind() != System.Text.Json.JsonValueKind.Number) return false;
        return double.TryParse(v.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
    }
}
