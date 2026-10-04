using System.Text.Json;
using System.Text.Json.Serialization;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Project;

/// <summary>
/// フォント設定をまとめて消す前の控え（設定フォルダの FontBackups\*.json）。消した種類だけを持ち、消していない種類は null。
/// 戻すときは、持っている種類を今の内容と置き換える（この曲専用は、控えを取った曲を開いているときだけ）。
/// </summary>
public sealed class N3FontBackup
{
    /// <summary>控えを入れるフォルダの名前（設定フォルダの中）。</summary>
    public const string FolderName = "FontBackups";

    /// <summary>控えを取った日時（UTC）。</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>アプリ共通のフォント設定（消していなければ null）。</summary>
    public List<N3FontSet>? CommonFonts { get; set; }

    /// <summary>アプリ共通のフォルダ分け（階層。<see cref="CommonFonts"/> と一緒に持つ）。</summary>
    public List<N3FontTreeNode>? Hierarchy { get; set; }

    /// <summary>この曲専用のフォント設定（消していなければ null）。</summary>
    public List<N3FontSet>? SongFonts { get; set; }

    /// <summary>この曲専用のフォント設定を持っていた曲の歌詞ファイル（未保存の曲なら null）。</summary>
    public string? SongPath { get; set; }

    /// <summary>自作の配色パターン（消していなければ null）。</summary>
    public List<N3ColorPattern>? UserColorPatterns { get; set; }

    /// <summary>新しいフォント設定に使っていた配色パターン（<see cref="UserColorPatterns"/> と一緒に持つ）。</summary>
    public string? DefaultColorPatternId { get; set; }

    /// <summary>最近使った色（消していなければ null。<see cref="AppSettings.N3RecentColorHistory"/> と同じ書き方）。</summary>
    public List<string>? RecentColors { get; set; }

    /// <summary>何も持っていないか（消すものが無かった）。</summary>
    [JsonIgnore]
    public bool IsEmpty => CommonFonts is null && SongFonts is null && UserColorPatterns is null && RecentColors is null;

    /// <summary>アプリ共通のフォルダの数。</summary>
    [JsonIgnore]
    public int FolderCount => Hierarchy is null ? 0 : N3FontTree.Walk(Hierarchy).Count(x => x.Node.IsFolder);

    /// <summary>持っている種類と数の説明（「アプリ共通 12 件（フォルダ 3 個）・この曲専用 2 件（曲.rlf）・…」）。</summary>
    public string Describe()
    {
        var parts = new List<string>();
        if (CommonFonts is not null)
        {
            parts.Add(FolderCount > 0 ? $"アプリ共通 {CommonFonts.Count} 件（フォルダ {FolderCount} 個）" : $"アプリ共通 {CommonFonts.Count} 件");
        }
        if (SongFonts is not null)
        {
            parts.Add(SongPath is { Length: > 0 } p ? $"この曲専用 {SongFonts.Count} 件（{Path.GetFileName(p)}）" : $"この曲専用 {SongFonts.Count} 件");
        }
        if (UserColorPatterns is not null) parts.Add($"自作の配色パターン {UserColorPatterns.Count} 件");
        if (RecentColors is not null) parts.Add($"最近使った色 {RecentColors.Count} 色");
        return parts.Count > 0 ? string.Join("・", parts) : "（空）";
    }

    // ------------------------------------------------------------ 保存・読み込み

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>控えを入れるフォルダ（設定フォルダの FontBackups）。</summary>
    public static string DefaultFolder => Path.Combine(AppSettings.DataFolder, FolderName);

    /// <summary>控えをフォルダへ保存し、保存したファイルのパスを返す（名前は日時。同じ名前があれば番号を付ける）。</summary>
    public string Save(string folder)
    {
        Directory.CreateDirectory(folder);
        string stem = $"フォント設定の控え_{CreatedUtc.ToLocalTime():yyyyMMdd_HHmmss}";
        string path = Path.Combine(folder, stem + ".json");
        for (int i = 2; File.Exists(path); i++) path = Path.Combine(folder, $"{stem}_{i}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
        return path;
    }

    /// <summary>控えを読む（読めなければ null）。</summary>
    public static N3FontBackup? Load(string path)
    {
        try
        {
            var backup = JsonSerializer.Deserialize<N3FontBackup>(File.ReadAllText(path), JsonOptions);
            return backup is null || backup.IsEmpty ? null : backup;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>フォルダの控えを新しい順に返す（読めないファイルは除く。フォルダが無ければ空）。</summary>
    public static List<(string Path, N3FontBackup Backup)> List(string folder)
    {
        var result = new List<(string Path, N3FontBackup Backup)>();
        if (!Directory.Exists(folder)) return result;
        foreach (string path in Directory.EnumerateFiles(folder, "*.json"))
        {
            if (Load(path) is { } backup) result.Add((path, backup));
        }
        result.Sort((a, b) => b.Backup.CreatedUtc.CompareTo(a.Backup.CreatedUtc));
        return result;
    }
}
