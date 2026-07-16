using System.Text.Json;

namespace NicoKaraPrep.Core.Project;

/// <summary>分離タブ 1 つ分の保存データ。</summary>
public sealed class SongProjectTab
{
    public string Name { get; set; } = "";

    /// <summary>タブの内容（テキスト編集モード形式。チェック数・ルビ込みでロスレス）。</summary>
    public string Text { get; set; } = "";

    /// <summary>エクスポート済みマークの付いた行インデックス。</summary>
    public List<int> ExportedLines { get; set; } = new();
}

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

    /// <summary>分離タブ（メイン以外）。次回開いたとき復元する。</summary>
    public List<SongProjectTab> Tabs { get; set; } = new();

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
