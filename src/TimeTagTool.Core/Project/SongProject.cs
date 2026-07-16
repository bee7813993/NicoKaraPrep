using System.Text.Json;

namespace TimeTagTool.Core.Project;

/// <summary>
/// 曲ごとの作業状態（.tttproj、歌詞ファイルの隣に保存）。
/// 済マーク・メディアファイルパスなど、歌詞ファイル自体を汚さない情報を保持する。
/// </summary>
public sealed class SongProject
{
    /// <summary>関連付けたメディアファイル（動画/音源）。</summary>
    public string? MediaPath { get; set; }

    /// <summary>エクスポート済みマークの付いた行インデックス（0 始まり）。</summary>
    public List<int> ExportedLines { get; set; } = new();

    /// <summary>曲内 @Emoji のスロット割り当て（置き換え文字列 → スロット番号 1–20）。並び替えの保存用。</summary>
    public Dictionary<string, int> EmojiSlots { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>歌詞ファイルパスに対応するプロジェクトファイルのパス。</summary>
    public static string PathFor(string documentPath) => documentPath + ".tttproj";

    public static SongProject? TryLoad(string documentPath)
    {
        string path = PathFor(documentPath);
        try
        {
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<SongProject>(File.ReadAllText(path), JsonOptions);
            }
        }
        catch (Exception)
        {
            // 壊れたプロジェクトファイルは無視
        }
        return null;
    }

    public void Save(string documentPath)
    {
        File.WriteAllText(PathFor(documentPath), JsonSerializer.Serialize(this, JsonOptions));
    }
}
