using System.Text.Json;
using System.Text.Json.Nodes;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Formats;

/// <summary>
/// ニコカラメーカー3 のフォント設定テンプレート（TemplateFont\{Guid}.tpl）を読み取る。
/// .tpl は n3proj と同じく ZIP のエントリ "0" に JSON（BOM 付き UTF-8）を 1 つ入れたもので、
/// JSON のルートがフォント設定（LyricsFontModel）1 件。読み取り専用で、テンプレートを変更することはない。
/// 取り込んだフォントはテンプレートと連動している扱い（<see cref="N3FontSet.NkmSynchronize"/> = true、
/// <see cref="N3FontSet.NkmGuid"/> = テンプレートの Guid）にする。
/// </summary>
public static class N3FontTemplateReader
{
    /// <summary>テンプレートファイルの拡張子。</summary>
    public const string Extension = ".tpl";

    /// <summary>テンプレートのサイズを px に換算するときの画面高さ（テンプレートは背景素材を持たないため FHD 基準）。</summary>
    public const int ReferenceHeight = 1080;

    /// <summary>テンプレートファイル 1 つを読む。</summary>
    public static N3FontSet ReadTemplate(string path)
    {
        var f = ParseTemplateJson(N3ProjFormat.ReadJson(path));
        f.NkmGuid ??= GuidFromFileName(path);
        f.ImportedFrom = Path.GetFullPath(path);
        return f;
    }

    /// <summary>テンプレートの中身（ZIP から取り出した JSON）を読む。</summary>
    public static N3FontSet ParseTemplateJson(string json)
    {
        var node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        if (node is not JsonObject root) throw new InvalidDataException("フォント設定テンプレートの JSON がオブジェクトではありません");
        var f = N3ProjFormat.ParseFontSet(root, ReferenceHeight);
        f.NkmSynchronize = true;
        return f;
    }

    /// <summary>
    /// フォルダ内のテンプレート（*.tpl）をすべて読む（名前の順）。読めなかったファイルは飛ばし、
    /// <paramref name="errors"/> に「ファイル名: 理由」を追加する（JSON のキーの重複など、1 ファイルの不備で一覧全体を止めない）。
    /// フォルダの中を列挙できなければ空の一覧を返し、<paramref name="errors"/> に「フォルダ: 理由」を追加する。
    /// </summary>
    public static List<N3FontSet> ReadTemplateFolder(string folder, ICollection<string>? errors = null)
    {
        var result = new List<N3FontSet>();
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
                result.Add(ReadTemplate(path));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException
                or InvalidOperationException or NotSupportedException or ArgumentException or FormatException)
            {
                errors?.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }
        return result.OrderBy(f => f.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>ファイル名（{Guid}.tpl）が Guid ならそれを返す。</summary>
    private static string? GuidFromFileName(string path) =>
        Guid.TryParse(Path.GetFileNameWithoutExtension(path), out var g) ? g.ToString() : null;
}
