using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Formats;

/// <summary>n3proj から取り出したフォント 1 件（フォント設定タブの「歌詞／漢字」）。</summary>
/// <param name="FontName">フォントファミリー名。</param>
/// <param name="FaceName">フェイス名（"ﾍﾋﾞｰ" "太字" など）。</param>
/// <param name="SizePx">画面高さ換算のフォントサイズ px。</param>
/// <param name="Index">LyricsFonts 内のインデックス（歌詞文字の FontIndex が参照）。</param>
/// <param name="SettingsName">ニコカラメーカー上のフォント設定名（例: （花帆））。</param>
/// <param name="EdgeSizePx">縁取りサイズ px（@Emoji の Zoom 基準「字幕サイズ縁取り込み」の計算に使用）。</param>
public sealed record N3ProjFontInfo(string FontName, string? FaceName, double SizePx, int Index, string? SettingsName, double EdgeSizePx = 0)
{
    /// <summary>フェイス名から太字相当かどうかを推定する。</summary>
    public bool IsBoldLike =>
        FaceName is { } f &&
        (f.Contains('太') || f.Contains("Bold", StringComparison.OrdinalIgnoreCase) ||
         f.Contains("ボールド") || f.Contains("ﾎﾞｰﾙﾄﾞ") ||
         f.Contains("Heavy", StringComparison.OrdinalIgnoreCase) || f.Contains("ヘビー") || f.Contains("ﾍﾋﾞｰ") ||
         f.Contains("Black", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// n3proj の字幕 1 行分の実表示区間（ニコカラメーカーが計算した値）。時刻はすべて ms（タイムタグ時刻と同じ基準）。
/// </summary>
/// <param name="FirstCharBeginMs">行の最初の文字のワイプ開始時刻（ドキュメント行とのマッチング用）。</param>
/// <param name="ShowBeginMs">行の表示開始時刻。</param>
/// <param name="ShowEndMs">行の表示終了時刻。</param>
public sealed record N3ProjLineTime(int FirstCharBeginMs, int ShowBeginMs, int ShowEndMs);

/// <summary>n3proj のレイアウト設定 1 件。</summary>
/// <param name="Name">設定名。</param>
/// <param name="Index">LyricsLayouts 内のインデックス。</param>
/// <param name="LineCount">行数（HorizontalAlignments の数）。</param>
public sealed record N3ProjLayoutInfo(string Name, int Index, int LineCount);

/// <summary>n3proj から取り出した設定。</summary>
/// <param name="ScreenWidth">動画の横幅 px。</param>
/// <param name="ScreenHeight">動画の高さ px。</param>
/// <param name="MainFont">歌詞で最も多く使われているフォント。</param>
/// <param name="Fonts">定義されている全フォント設定（各タブの「歌詞／漢字」）。</param>
/// <param name="LineTimes">字幕行ごとの実表示区間。</param>
/// <param name="Layouts">定義されているレイアウト設定。</param>
/// <param name="AppVersion">最後に保存したニコカラメーカーのバージョン文字列（"Ver 13.79" など）。</param>
public sealed record N3ProjSettings(
    int ScreenWidth,
    int ScreenHeight,
    N3ProjFontInfo? MainFont,
    List<N3ProjFontInfo> Fonts,
    List<N3ProjLineTime> LineTimes,
    List<N3ProjLayoutInfo> Layouts,
    string? AppVersion)
{
    /// <summary>フォント設定名の一覧（LyricsFonts の順）。</summary>
    public List<string> FontSetNames => Fonts.OrderBy(f => f.Index).Select(f => f.SettingsName ?? "").ToList();

    /// <summary>
    /// 歌詞行でいちばん多い字幕アクション（同数なら最初に出たもの。歌詞行にアクションが無ければ null）。
    /// 書き出しの既定の字幕アクションを自動で決めるときの「ベースのまま」（<see cref="N3ProjWriter.ResolveDefaultAction(N3SubtitleAction, N3SubtitleAction, string, IReadOnlyDictionary{string, JsonObject}, out N3SubtitleActionSource)"/>）。
    /// </summary>
    public N3SubtitleAction? DefaultSubtitleAction { get; init; }
}

/// <summary>
/// ニコカラメーカー3 のプロジェクトファイル（.n3proj = ZIP に JSON 1 エントリ）から
/// 画面サイズとフォント設定を読み出す。
/// </summary>
public static class N3ProjFormat
{
    /// <summary>ZIP 内の JSON エントリ名（ニコカラメーカーの JsonManager と同じ）。</summary>
    public const string ZipEntryName = "0";

    /// <summary>n3proj の JSON 文字列を取り出す。</summary>
    public static string ReadJson(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry(ZipEntryName) ?? zip.Entries.FirstOrDefault()
            ?? throw new InvalidDataException("n3proj にエントリがありません");
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>n3proj の JSON をツリーとして読み込む（書き出し時のベースに使う）。</summary>
    public static JsonObject ReadJsonObject(string path)
    {
        var node = JsonNode.Parse(ReadJson(path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        return node as JsonObject ?? throw new InvalidDataException("n3proj の JSON がオブジェクトではありません");
    }

    public static N3ProjSettings Read(string path)
    {
        string json = ReadJson(path);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        int width = 1920, height = 1080;
        if (root.TryGetProperty("SourceInfo", out var si) && si.ValueKind == JsonValueKind.Object)
        {
            if (si.TryGetProperty("BackgroundWidth", out var bw) && bw.TryGetInt32(out int w)) width = w;
            if (si.TryGetProperty("BackgroundHeight", out var bh) && bh.TryGetInt32(out int h)) height = h;
        }
        else
        {
            // 古い構造（ルート直下）にも対応
            if (root.TryGetProperty("BackgroundWidth", out var bw) && bw.TryGetInt32(out int w)) width = w;
            if (root.TryGetProperty("BackgroundHeight", out var bh) && bh.TryGetInt32(out int h)) height = h;
        }

        string? appVersion = root.TryGetProperty("ModifyAppVer", out var mv) && mv.ValueKind == JsonValueKind.String ? mv.GetString() : null;
        if (string.IsNullOrEmpty(appVersion) && root.TryGetProperty("CreateAppVer", out var cv) && cv.ValueKind == JsonValueKind.String)
        {
            appVersion = cv.GetString();
        }

        // フォント設定タブ（LyricsFonts）ごとに「歌詞／漢字」のフォントを取り出す。
        // 歌詞文字の FontIndex は LyricsFonts のインデックスを指す。
        var fonts = new List<N3ProjFontInfo>();
        if (root.TryGetProperty("LyricsFonts", out var lyricsFonts) && lyricsFonts.ValueKind == JsonValueKind.Array)
        {
            int setIndex = 0;
            foreach (var set in lyricsFonts.EnumerateArray())
            {
                string? setName = set.TryGetProperty("SettingsName", out var sn) ? sn.GetString() : null;
                string name = "";
                string? face = null;
                double sizePx = 0, edgePx = 0;
                if (set.TryGetProperty("FontInfos", out var fontInfos) && fontInfos.ValueKind == JsonValueKind.Array)
                {
                    var main = fontInfos.EnumerateArray().FirstOrDefault();
                    if (main.ValueKind == JsonValueKind.Object)
                    {
                        name = main.TryGetProperty("FontName", out var fn) ? fn.GetString() ?? "" : "";
                        face = main.TryGetProperty("FontFaceName", out var ff) ? ff.GetString() : null;
                        sizePx = ReadSizePx(main, "CharSize", height);
                        edgePx = ReadSizePx(main, "EdgeSize", height);
                    }
                }
                fonts.Add(new N3ProjFontInfo(name, face, sizePx, setIndex, setName, edgePx));
                setIndex++;
            }
        }
        else if (FindProperty(root, "FontInfos") is { ValueKind: JsonValueKind.Array } fontInfosLegacy)
        {
            foreach (var fi in fontInfosLegacy.EnumerateArray())
            {
                string name = fi.TryGetProperty("FontName", out var fn) ? fn.GetString() ?? "" : "";
                string? face = fi.TryGetProperty("FontFaceName", out var ff) ? ff.GetString() : null;
                string? settingsName = fi.TryGetProperty("SettingsName", out var sn) ? sn.GetString() : null;
                int index = fi.TryGetProperty("Index", out var ix) && ix.TryGetInt32(out int i) ? i : fonts.Count;
                fonts.Add(new N3ProjFontInfo(name, face, ReadSizePx(fi, "CharSize", height), index, settingsName, ReadSizePx(fi, "EdgeSize", height)));
            }
        }

        // 歌詞文字が最も多く参照している FontIndex を主フォントとする
        N3ProjFontInfo? mainFont = null;
        var usage = new Dictionary<int, int>();
        foreach (Match m in Regex.Matches(json, "\"FontIndex\":(\\d+)"))
        {
            int idx = int.Parse(m.Groups[1].Value);
            usage[idx] = usage.GetValueOrDefault(idx) + 1;
        }
        foreach (int idx in usage.OrderByDescending(kv => kv.Value).Select(kv => kv.Key))
        {
            mainFont = fonts.FirstOrDefault(f => f.Index == idx && f.FontName.Length > 0 && f.SizePx > 0);
            if (mainFont is not null) break;
        }
        mainFont ??= fonts.FirstOrDefault(f => f.FontName.Length > 0 && f.SizePx > 0);

        // レイアウト設定（行数 = HorizontalAlignments の数）
        var layouts = new List<N3ProjLayoutInfo>();
        if (root.TryGetProperty("LyricsLayouts", out var lyricsLayouts) && lyricsLayouts.ValueKind == JsonValueKind.Array)
        {
            int li = 0;
            foreach (var layout in lyricsLayouts.EnumerateArray())
            {
                string name = layout.TryGetProperty("SettingsName", out var ln) ? ln.GetString() ?? "" : "";
                int count = layout.TryGetProperty("HorizontalAlignments", out var ha) && ha.ValueKind == JsonValueKind.Array
                    ? ha.GetArrayLength()
                    : 1;
                layouts.Add(new N3ProjLayoutInfo(name, li, count));
                li++;
            }
        }

        // 字幕行ごとの実表示区間（ShowBeginTime / ShowEndTime）を収集
        var lineTimes = new List<N3ProjLineTime>();
        if (root.TryGetProperty("SourceLyricsInfos", out var sources) && sources.ValueKind == JsonValueKind.Array)
        {
            foreach (var source in sources.EnumerateArray()) CollectLineTimes(source, lineTimes);
        }
        else
        {
            CollectLineTimes(root, lineTimes);
        }

        // 歌詞行の字幕アクションでいちばん多いもの（読み込んだ JSON の文書から切り離して持つ）
        var lineActions = new List<N3SubtitleAction?>();
        if (root.TryGetProperty("SourceLyricsInfos", out var actionSources) && actionSources.ValueKind == JsonValueKind.Array)
        {
            foreach (var source in actionSources.EnumerateArray())
            {
                if (source.ValueKind != JsonValueKind.Object || !source.TryGetProperty("LineInfos", out var lines) || lines.ValueKind != JsonValueKind.Array) continue;
                foreach (var line in lines.EnumerateArray())
                {
                    if (line.ValueKind != JsonValueKind.Object
                        || !line.TryGetProperty("Kind", out var kind) || !kind.TryGetInt32(out int k) || k != 1
                        || !line.TryGetProperty("SubtitleActionId", out var idElement) || idElement.ValueKind != JsonValueKind.String
                        || idElement.GetString() is not { Length: > 0 } id)
                    {
                        continue;
                    }
                    lineActions.Add(new N3SubtitleAction(id,
                        line.TryGetProperty("SubtitleActionSettings", out var st) && st.ValueKind == JsonValueKind.Object
                            ? JsonObject.Create(st)
                            : N3SubtitleActionCatalog.CreateDefault(id).Settings));
                }
            }
        }
        var defaultAction = N3SubtitleActionCatalog.MostCommon(lineActions);
        if (defaultAction is not null)
        {
            defaultAction = new N3SubtitleAction(defaultAction.Id, JsonNode.Parse(defaultAction.Settings.ToJsonString()) as JsonObject);
        }

        return new N3ProjSettings(width, height, mainFont, fonts, lineTimes, layouts, appVersion) { DefaultSubtitleAction = defaultAction };
    }

    /// <summary>
    /// n3proj の行（LineInfos の 1 件）の字幕アクション（SubtitleActionId が空・無ければ null）。
    /// 設定値（SubtitleActionSettings）は写して持つ（無ければ Id の既定値）。
    /// </summary>
    public static N3SubtitleAction? ReadLineSubtitleAction(JsonObject line) =>
        LineSubtitleActionView(line) is { } view ? view.Clone() : null;

    /// <summary>
    /// ベースの n3proj の歌詞設定（SourceLyricsInfos）の歌詞行（Kind 1）でいちばん多い字幕アクションの写し
    /// （<see cref="N3SubtitleAction.SameAs"/> で同じものをまとめて数え、同数なら最初に出たもの。無ければ null）。
    /// </summary>
    public static N3SubtitleAction? MostCommonSubtitleAction(JsonObject? root)
    {
        if (root?["SourceLyricsInfos"] is not JsonArray infos) return null;
        var actions = new List<N3SubtitleAction?>();
        foreach (var info in infos)
        {
            if (info?["LineInfos"] is not JsonArray lines) continue;
            foreach (var l in lines)
            {
                if (l is JsonObject line && N3FontJson.Int(line["Kind"]) == 1) actions.Add(LineSubtitleActionView(line));
            }
        }
        return N3SubtitleActionCatalog.MostCommon(actions);
    }

    /// <summary>行の字幕アクション（設定値は写さずにそのまま指す。読むだけに使う）。</summary>
    private static N3SubtitleAction? LineSubtitleActionView(JsonObject line)
    {
        if (line["SubtitleActionId"] is not JsonValue v || !v.TryGetValue(out string? id) || string.IsNullOrEmpty(id)) return null;
        return new N3SubtitleAction(id, line["SubtitleActionSettings"] as JsonObject ?? N3SubtitleActionCatalog.CreateDefault(id).Settings);
    }

    /// <summary>
    /// n3proj のフォント設定タブを NicoKaraPrep のフォント設定（<see cref="N3FontSet"/>）として全項目取り出す
    /// （配色 8 箇所の塗りの種類・単色・不透明度・マーカー・画像、文字種別フォント 6 種、文字飾り、Guid と連動の状態）。
    /// </summary>
    public static List<N3FontSet> ReadFontSets(string path)
    {
        var result = ReadFontSets(ReadJsonObject(path));
        string full = Path.GetFullPath(path);
        foreach (var f in result) f.ImportedFrom = full;
        return result;
    }

    /// <summary>読み込み済みの n3proj の JSON からフォント設定を取り出す。</summary>
    public static List<N3FontSet> ReadFontSets(JsonObject root)
    {
        var result = new List<N3FontSet>();
        int height = N3FontJson.Int(root["SourceInfo"]?["BackgroundHeight"]) ?? 1080;
        if (height <= 0) height = 1080;
        if (root["LyricsFonts"] is not JsonArray sets) return result;

        foreach (var node in sets)
        {
            if (node is JsonObject set) result.Add(ParseFontSet(set, height));
        }
        return result;
    }

    /// <summary>
    /// ニコカラメーカー3 のフォント設定（LyricsFontModel）1 件を読み取る。
    /// サイズは画面高さ <paramref name="height"/> 換算の px。Guid は <see cref="N3FontSet.NkmGuid"/>、
    /// テンプレート連動は <see cref="N3FontSet.NkmSynchronize"/> に入る。
    /// </summary>
    public static N3FontSet ParseFontSet(JsonObject set, int height) => N3FontJson.ParseFontSet(set, height);

    private static double ReadSizePx(JsonElement obj, string name, int height)
    {
        if (!obj.TryGetProperty(name, out var cs) || cs.ValueKind != JsonValueKind.Object) return 0;
        double ratio = cs.TryGetProperty("Ratio", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetDouble() : 0;
        double size = cs.TryGetProperty("Size", out var sz) && sz.ValueKind == JsonValueKind.Number ? sz.GetDouble() : 0;
        double reference = cs.TryGetProperty("Reference", out var rf) && rf.ValueKind == JsonValueKind.Number ? rf.GetDouble() : height;
        return ratio > 0 ? ratio * height : (reference > 0 ? size * height / reference : size);
    }

    /// <summary>
    /// JSON ツリーから ShowBeginTime / ShowEndTime を持つ字幕行オブジェクトを再帰的に収集する。
    /// 行オブジェクトは文字配列（各要素が BeginTime を持つ）も持っている。
    /// </summary>
    private static void CollectLineTimes(JsonElement element, List<N3ProjLineTime> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("ShowBeginTime", out var sb) && sb.TryGetInt32(out int showBegin) &&
                    element.TryGetProperty("ShowEndTime", out var se) && se.TryGetInt32(out int showEnd))
                {
                    // 最初の文字のワイプ開始時刻を探す（マッチング用のキー）
                    int? firstChar = null;
                    foreach (var prop in element.EnumerateObject())
                    {
                        if (prop.Value.ValueKind != JsonValueKind.Array) continue;
                        foreach (var item in prop.Value.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.Object &&
                                item.TryGetProperty("BeginTime", out var bt) && bt.TryGetInt32(out int begin) &&
                                begin >= 0)
                            {
                                firstChar = firstChar is int f ? Math.Min(f, begin) : begin;
                            }
                        }
                        if (firstChar is not null) break;
                    }
                    if (firstChar is int fc && showEnd > showBegin)
                    {
                        result.Add(new N3ProjLineTime(fc, showBegin, showEnd));
                    }
                }
                foreach (var prop in element.EnumerateObject())
                {
                    CollectLineTimes(prop.Value, result);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectLineTimes(item, result);
                }
                break;
        }
    }

    /// <summary>JSON ツリーを再帰的に探索して最初に見つかった指定名のプロパティを返す。</summary>
    private static JsonElement? FindProperty(JsonElement element, string name)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    if (prop.NameEquals(name)) return prop.Value;
                    if (FindProperty(prop.Value, name) is { } found) return found;
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (FindProperty(item, name) is { } found) return found;
                }
                break;
        }
        return null;
    }

    /// <summary>歌詞ファイルと同じフォルダにある n3proj を探す（1 つだけ見つかった場合にそのパスを返す）。</summary>
    public static string? FindNear(string lyricsPath)
    {
        try
        {
            string? dir = Path.GetDirectoryName(lyricsPath);
            if (dir is null || !Directory.Exists(dir)) return null;
            var candidates = Directory.GetFiles(dir, "*.n3proj");
            return candidates.Length == 1 ? candidates[0] : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
