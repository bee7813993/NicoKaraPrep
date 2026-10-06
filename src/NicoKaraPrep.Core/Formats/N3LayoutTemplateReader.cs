using System.Text.Json;
using System.Text.Json.Nodes;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Formats;

/// <summary>
/// ニコカラメーカー3 のレイアウト設定テンプレート（TemplateLayout\{Guid}.tpl）を読み取る。
/// .tpl は n3proj と同じく ZIP のエントリ "0" に JSON（BOM 付き UTF-8）を 1 つ入れたもので、
/// JSON のルートがレイアウト設定（LyricsLayoutModel）1 件。項目は n3proj の LyricsLayouts の 1 件と同じで、
/// px は {Size, Reference, Ratio} の形（読み方は <see cref="N3LayoutReader.ParseLayout"/> と共通）。
/// 読み取り専用で、テンプレートを変更することはない。取り込んだレイアウトは NicoKaraPrep で編集するレイアウト（<see cref="N3Layout"/>）にし、
/// テンプレートとの連動（Guid・Synchronize）は引き継がない（書き出しでは NicoKaraPrep のレイアウトとして書く）。
/// </summary>
public static class N3LayoutTemplateReader
{
    /// <summary>テンプレートファイルの拡張子。</summary>
    public const string Extension = ".tpl";

    /// <summary>テンプレートの px を換算するときの既定の画面高さ（テンプレートは背景素材を持たないため FHD 基準。フォントのテンプレートと同じ）。</summary>
    public const int ReferenceHeight = N3FontTemplateReader.ReferenceHeight;

    /// <summary>
    /// テンプレートファイル 1 つを読む。名前は JSON の名前（SettingsName）で、無ければファイル名（拡張子を除く）。
    /// px は画面の高さ <paramref name="height"/> での値（書き出すプロジェクトの高さを渡す。0 以下なら <see cref="ReferenceHeight"/>）。
    /// </summary>
    public static N3Layout ReadTemplate(string path, int height = ReferenceHeight)
    {
        var layout = ParseTemplateJson(N3ProjFormat.ReadJson(path), height);
        if (string.IsNullOrWhiteSpace(layout.Name)) layout.Name = Path.GetFileNameWithoutExtension(path);
        return layout;
    }

    /// <summary>
    /// テンプレートの中身（ZIP から取り出した JSON）を読む。レイアウト設定の項目（上下配置・行ごとの左右配置）が
    /// どちらも無い JSON（フォント設定のテンプレートなど）はレイアウト設定ではないとして <see cref="InvalidDataException"/> にする。
    /// </summary>
    public static N3Layout ParseTemplateJson(string json, int height = ReferenceHeight)
    {
        var node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        if (node is not JsonObject root) throw new InvalidDataException("レイアウト設定テンプレートの JSON がオブジェクトではありません");
        if (!root.ContainsKey("SelectedVerticalAlignmentIndex") && !root.ContainsKey("HorizontalAlignments"))
        {
            throw new InvalidDataException("レイアウト設定テンプレートではありません（上下配置・行ごとの左右配置の項目がありません）");
        }
        return N3Layout.FromSettings(N3LayoutReader.ParseLayout(root, 0, height > 0 ? height : ReferenceHeight));
    }

    /// <summary>
    /// フォルダ内のテンプレート（*.tpl）をすべて読む（名前の順）。読めなかったファイルは飛ばし、
    /// <paramref name="errors"/> に「ファイル名: 理由」を追加する（壊れた ZIP・JSON でない中身など、1 ファイルの不備で一覧全体を止めない）。
    /// フォルダの中を列挙できなければ空の一覧を返し、<paramref name="errors"/> に「フォルダ: 理由」を追加する。
    /// px は画面の高さ <paramref name="height"/> での値。
    /// </summary>
    public static List<N3Layout> ReadTemplateFolder(string folder, ICollection<string>? errors = null, int height = ReferenceHeight)
    {
        var result = new List<N3Layout>();
        if (!Directory.Exists(folder)) return result;

        List<string> paths;
        try
        {
            paths = Directory.EnumerateFiles(folder, "*" + Extension).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            errors?.Add($"{folder}: {ex.Message}");
            return result;
        }

        foreach (string path in paths)
        {
            try
            {
                result.Add(ReadTemplate(path, height));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException
                or InvalidOperationException or NotSupportedException or ArgumentException or FormatException)
            {
                errors?.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }
        return result.OrderBy(l => l.Name, StringComparer.Ordinal).ToList();
    }
}
