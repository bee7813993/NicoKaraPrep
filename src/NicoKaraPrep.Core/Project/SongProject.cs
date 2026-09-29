using System.Text.Json;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Project;

/// <summary>行ごとのニコカラメーカー3 書き出し設定（表示時刻の手動指定・フォント設定名）。行インデックスで保存する。</summary>
public sealed class LineExportSettings
{
    public int Index { get; set; }

    /// <summary>表示開始時刻の手動指定（10ms 単位）。null は自動。</summary>
    public int? ShowBeginCs { get; set; }

    /// <summary>表示終了時刻の手動指定（10ms 単位）。null は自動。</summary>
    public int? ShowEndCs { get; set; }

    /// <summary>フォント設定名の手動指定。null は自動。</summary>
    public string? FontSetName { get; set; }

    /// <summary>ドキュメントの行から手動設定を持つ行だけを集める。</summary>
    public static List<LineExportSettings> Collect(LyricsDocument doc)
    {
        var result = new List<LineExportSettings>();
        for (int i = 0; i < doc.Lines.Count; i++)
        {
            var l = doc.Lines[i];
            if (!l.HasN3Overrides) continue;
            result.Add(new LineExportSettings
            {
                Index = i,
                ShowBeginCs = l.ShowBeginCs,
                ShowEndCs = l.ShowEndCs,
                FontSetName = l.FontSetName,
            });
        }
        return result;
    }

    /// <summary>保存した手動設定をドキュメントの行へ適用する（行数が合う範囲で）。</summary>
    public static void Apply(LyricsDocument doc, IEnumerable<LineExportSettings>? settings)
    {
        if (settings is null) return;
        foreach (var s in settings)
        {
            if (s.Index < 0 || s.Index >= doc.Lines.Count) continue;
            var l = doc.Lines[s.Index];
            l.ShowBeginCs = s.ShowBeginCs;
            l.ShowEndCs = s.ShowEndCs;
            l.FontSetName = string.IsNullOrEmpty(s.FontSetName) ? null : s.FontSetName;
        }
    }
}

/// <summary>曲ごとのニコカラメーカー3 プロジェクト書き出し設定。</summary>
public sealed class N3ProjSongSettings
{
    /// <summary>ベースにする既存の n3proj（フォント・レイアウト・タイトル等をここから引き継ぐ）。null = 標準設定で生成。</summary>
    public string? BasePath { get; set; }

    /// <summary>前回の書き出し先。</summary>
    public string? OutputPath { get; set; }

    /// <summary>プロジェクト名（ニコカラメーカーの「設定の名称」）。空 = 歌詞ファイル名。</summary>
    public string ProjectName { get; set; } = "";

    /// <summary>タブ名 → レイアウト名（空 = 行数に応じて自動）。</summary>
    public Dictionary<string, string> TabLayouts { get; set; } = new();

    /// <summary>タブ名 → 上段の行を長めに表示するか（ニコカラメーカーの TopLong 相当）。</summary>
    public Dictionary<string, bool> TabTopLong { get; set; } = new();

    /// <summary>行の既定フォント設定名（パート記号が現れるまで適用）。空 = 先頭の設定。</summary>
    public string DefaultFontSetName { get; set; } = "";

    /// <summary>NicoKaraPrep 側のフォント設定をベースへマージするか。</summary>
    public bool MergeFontSets { get; set; } = true;
}

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

    /// <summary>このタブを別ファイルへ保存した先（タブの上書き保存の対象）。</summary>
    public string? FilePath { get; set; }

    /// <summary>行ごとのニコカラメーカー3 書き出し設定。</summary>
    public List<LineExportSettings> LineSettings { get; set; } = new();
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

    /// <summary>メインの行ごとのニコカラメーカー3 書き出し設定（表示時刻の手動指定など）。</summary>
    public List<LineExportSettings> LineSettings { get; set; } = new();

    /// <summary>ニコカラメーカー3 プロジェクト書き出しの設定。</summary>
    public N3ProjSongSettings N3Proj { get; set; } = new();

    private List<N3FontSet> _fontSets = new();

    /// <summary>
    /// この曲専用のフォント設定。書き出しでは同じ名前のアプリ共通のフォント設定より優先する
    /// （<see cref="N3FontLibrary.ResolveForExport"/>）。
    /// null は空の一覧に、一覧の中の null は取り除く（手で編集したファイルなど）。
    /// </summary>
    public List<N3FontSet> FontSets
    {
        get => _fontSets;
        set
        {
            _fontSets = value ?? new();
            _fontSets.RemoveAll(f => f is null);
        }
    }

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
