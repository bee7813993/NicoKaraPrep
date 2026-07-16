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

    /// <summary>各行の元の行位置キー（分離解除で元の位置へ戻すため。Text の行と同順）。</summary>
    public List<int?> LineKeys { get; set; } = new();
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

    /// <summary>
    /// タブ分離中のメインの内容（テキスト編集モード形式）。
    /// 分離は歌詞ファイル自体を書き換えないため、分離後のメインをここに保存して復元する。
    /// 分離タブが無いときは空。
    /// </summary>
    public string MainText { get; set; } = "";

    /// <summary>メインの各行の元の行位置キー（MainText の行と同順）。</summary>
    public List<int?> MainLineKeys { get; set; } = new();

    /// <summary>保存時の歌詞ファイルのフィンガープリント（外部編集の検出用）。</summary>
    public string FileFingerprint { get; set; } = "";

    /// <summary>歌詞ファイルのフィンガープリント（サイズ＋更新時刻）を計算する。</summary>
    public static string ComputeFingerprint(string filePath)
    {
        try
        {
            var info = new FileInfo(filePath);
            return info.Exists ? $"{info.Length}:{info.LastWriteTimeUtc.Ticks}" : "";
        }
        catch (Exception)
        {
            return "";
        }
    }

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
