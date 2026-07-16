using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NicoKaraPrep.Core.Formats;

/// <summary>n3proj から取り出したフォント 1 件。</summary>
/// <param name="FontName">フォントファミリー名。</param>
/// <param name="FaceName">フェイス名（"ﾍﾋﾞｰ" "太字" など）。</param>
/// <param name="SizePx">画面高さ換算のフォントサイズ px。</param>
/// <param name="Index">FontInfos 内のインデックス（歌詞文字の FontIndex が参照）。</param>
/// <param name="SettingsName">ニコカラメーカー上の設定名（例: 歌詞／漢字）。</param>
public sealed record N3ProjFontInfo(string FontName, string? FaceName, double SizePx, int Index, string? SettingsName)
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
/// n3proj の字幕 1 行分の実表示区間（ニコカラメーカーが計算した値）。時刻はすべて ms。
/// </summary>
/// <param name="FirstCharBeginMs">行の最初の文字のワイプ開始時刻（ドキュメント行とのマッチング用）。</param>
/// <param name="ShowBeginMs">行の表示開始時刻。</param>
/// <param name="ShowEndMs">行の表示終了時刻。</param>
public sealed record N3ProjLineTime(int FirstCharBeginMs, int ShowBeginMs, int ShowEndMs);

/// <summary>n3proj から取り出した設定。</summary>
/// <param name="ScreenWidth">動画の横幅 px。</param>
/// <param name="ScreenHeight">動画の高さ px。</param>
/// <param name="MainFont">歌詞で最も多く使われているフォント。</param>
/// <param name="Fonts">定義されている全フォント。</param>
/// <param name="LineTimes">字幕行ごとの実表示区間。</param>
public sealed record N3ProjSettings(int ScreenWidth, int ScreenHeight, N3ProjFontInfo? MainFont, List<N3ProjFontInfo> Fonts, List<N3ProjLineTime> LineTimes);

/// <summary>
/// ニコカラメーカー3 のプロジェクトファイル（.n3proj = ZIP に JSON 1 エントリ）から
/// 画面サイズとフォント設定を読み出す。
/// </summary>
public static class N3ProjFormat
{
    public static N3ProjSettings Read(string path)
    {
        string json;
        using (var zip = ZipFile.OpenRead(path))
        {
            var entry = zip.Entries.FirstOrDefault()
                ?? throw new InvalidDataException("n3proj にエントリがありません");
            using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            json = reader.ReadToEnd();
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        int width = root.TryGetProperty("BackgroundWidth", out var bw) && bw.TryGetInt32(out int w) ? w : 1920;
        int height = root.TryGetProperty("BackgroundHeight", out var bh) && bh.TryGetInt32(out int h) ? h : 1080;

        var fonts = new List<N3ProjFontInfo>();
        if (FindProperty(root, "FontInfos") is { ValueKind: JsonValueKind.Array } fontInfos)
        {
            foreach (var fi in fontInfos.EnumerateArray())
            {
                string name = fi.TryGetProperty("FontName", out var fn) ? fn.GetString() ?? "" : "";
                string? face = fi.TryGetProperty("FontFaceName", out var ff) ? ff.GetString() : null;
                string? settingsName = fi.TryGetProperty("SettingsName", out var sn) ? sn.GetString() : null;
                int index = fi.TryGetProperty("Index", out var ix) && ix.TryGetInt32(out int i) ? i : fonts.Count;

                double sizePx = 0;
                if (fi.TryGetProperty("CharSize", out var cs))
                {
                    double ratio = cs.TryGetProperty("Ratio", out var r) ? r.GetDouble() : 0;
                    double size = cs.TryGetProperty("Size", out var sz) ? sz.GetDouble() : 0;
                    double reference = cs.TryGetProperty("Reference", out var rf) ? rf.GetDouble() : height;
                    sizePx = ratio > 0 ? ratio * height : (reference > 0 ? size * height / reference : size);
                }

                fonts.Add(new N3ProjFontInfo(name, face, sizePx, index, settingsName));
            }
        }

        // 歌詞文字が最も多く参照している FontIndex を主フォントとする
        N3ProjFontInfo? main = null;
        var usage = new Dictionary<int, int>();
        foreach (Match m in Regex.Matches(json, "\"FontIndex\":(\\d+)"))
        {
            int idx = int.Parse(m.Groups[1].Value);
            usage[idx] = usage.GetValueOrDefault(idx) + 1;
        }
        foreach (int idx in usage.OrderByDescending(kv => kv.Value).Select(kv => kv.Key))
        {
            main = fonts.FirstOrDefault(f => f.Index == idx && f.FontName.Length > 0 && f.SizePx > 0);
            if (main is not null) break;
        }
        main ??= fonts.FirstOrDefault(f => f.FontName.Length > 0 && f.SizePx > 0);

        // 字幕行ごとの実表示区間（ShowBeginTime / ShowEndTime）を収集
        var lineTimes = new List<N3ProjLineTime>();
        CollectLineTimes(root, lineTimes);

        return new N3ProjSettings(width, height, main, fonts, lineTimes);
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
