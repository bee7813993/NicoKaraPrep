using System.Text.Json.Nodes;

namespace NicoKaraPrep.Core.Formats;

/// <summary>
/// このマシンにインストールされているニコカラメーカー3（Microsoft Store 版）の設定から、
/// 書き出しの既定値に使える情報を読み取る。見つからなければ null。
/// 読み取り専用で、ニコカラメーカーの設定を変更することはない。
/// </summary>
/// <param name="SettingsFolder">設定フォルダ。</param>
/// <param name="AppVersion">最後に起動したバージョン（"Ver 13.79" など）。</param>
/// <param name="PreTimeMs">「ワイプ前の表示時間」ms（表示時刻調整アドオンの設定）。</param>
/// <param name="PostTimeMs">「ワイプ後の表示時間」ms。</param>
/// <param name="IntervalMs">「歌詞の表示間隔」ms。</param>
/// <param name="LayoutSelectableBegin">レイアウト自動設定の適用対象範囲（先頭のレイアウト名）。</param>
/// <param name="LayoutSelectableEnd">レイアウト自動設定の適用対象範囲（末尾のレイアウト名）。</param>
/// <param name="CharFadeSettings">字幕アクション「文字単位フェード」の設定 JSON。</param>
public sealed record Nkm3Environment(
    string SettingsFolder,
    string? AppVersion,
    int? PreTimeMs,
    int? PostTimeMs,
    int? IntervalMs,
    string? LayoutSelectableBegin,
    string? LayoutSelectableEnd,
    JsonObject? CharFadeSettings)
{
    public static Nkm3Environment? Detect()
    {
        try
        {
            string packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
            if (!Directory.Exists(packages)) return null;
            string? folder = Directory.GetDirectories(packages, "22724SHINTA.NicokaraMaker3_*")
                .Select(d => Path.Combine(d, "Settings"))
                .FirstOrDefault(Directory.Exists);
            if (folder is null) return null;
            return Load(folder);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>設定フォルダから読み取る（テスト用に公開）。</summary>
    public static Nkm3Environment Load(string folder)
    {
        var settings = TryReadJson(Path.Combine(folder, "Nkm3Settings.json"));
        string? version = settings?["PrevLaunchVer"]?.GetValue<string>();
        string adjusterId = settings?["LastSelectedAddOns"]?["ShowTimeAdjusterId"]?.GetValue<string>() ?? "";
        if (string.IsNullOrEmpty(adjusterId)) adjusterId = "SHINTA.TopShortAdjuster";

        int? pre = null, post = null, interval = null;
        var adjuster = TryReadJson(Path.Combine(folder, "AddOns", adjusterId + ".json"))
            ?? TryReadJson(Path.Combine(folder, "AddOns", "SHINTA.TopShortAdjuster.json"));
        if (adjuster is not null)
        {
            pre = adjuster["PreTime2"]?.GetValue<int>();
            post = adjuster["PostTime2"]?.GetValue<int>();
            interval = adjuster["IntervalTime2"]?.GetValue<int>();
        }

        var layoutSelector = TryReadJson(Path.Combine(folder, "AddOns", "SHINTA.LinesLayoutSelector.json"));
        string? layoutBegin = layoutSelector?["SelectableBegin"]?["SettingsName"]?.GetValue<string>();
        string? layoutEnd = layoutSelector?["SelectableEnd"]?["SettingsName"]?.GetValue<string>();

        var fade = TryReadJson(Path.Combine(folder, "AddOns", "SHINTA.CharFadeInFadeOut.json"));

        return new Nkm3Environment(folder, version, pre, post, interval, layoutBegin, layoutEnd, fade);
    }

    private static JsonObject? TryReadJson(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
