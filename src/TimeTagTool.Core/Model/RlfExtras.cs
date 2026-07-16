namespace TimeTagTool.Core.Model;

/// <summary>
/// rlf ファイル由来の、歌詞本体以外の付随データ。RhythmicaLyrics へ書き戻すときに使用する。
/// </summary>
public sealed class RlfExtras
{
    /// <summary>RhythmicaLyrics の SaveMojiCode（保存時文字コード設定）。</summary>
    public int SaveMojiCode { get; set; }

    /// <summary>RhythmicaLyrics の YomiMojiCode（読込時文字コード設定）。</summary>
    public int YomiMojiCode { get; set; }

    /// <summary>sakura_surface(0,0) に格納される FLELE サーフェス名。</summary>
    public string Surface00 { get; set; } = "";

    public RlfExtras Clone() => new()
    {
        SaveMojiCode = SaveMojiCode,
        YomiMojiCode = YomiMojiCode,
        Surface00 = Surface00,
    };
}
