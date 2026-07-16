namespace TimeTagTool.Core.Model;

/// <summary>ページ分割の方式。</summary>
public enum PageSplitMode
{
    /// <summary>空行がページ区切り（ニコカラメーカー3 の基本動作）。</summary>
    EmptyLine,

    /// <summary>固定行数でページ化（ニコカラメーカー側の設定行数ページ化に対応）。</summary>
    FixedLineCount,
}

/// <summary>
/// タイムタグ付き歌詞ドキュメント全体。
/// </summary>
public sealed class LyricsDocument
{
    /// <summary>@タグメタデータ（@Ruby / @Emoji 以外。原文順）。</summary>
    public List<MetadataTag> Metadata { get; } = new();

    /// <summary>曲内の @Emoji 定義。</summary>
    public List<EmojiEntry> EmojiEntries { get; } = new();

    /// <summary>lrc 読込時に歌詞へ投影できなかった @Ruby エントリ（書き出し時にそのまま再出力）。</summary>
    public List<RubyEntry> UnappliedRubyEntries { get; } = new();

    /// <summary>歌詞行（空行含む）。</summary>
    public List<LyricsLine> Lines { get; } = new();

    /// <summary>rlf 由来の付随データ（ラウンドトリップ用）。rlf 以外から読んだ場合は null。</summary>
    public RlfExtras? RlfExtras { get; set; }

    /// <summary>指定名の @タグ値を取得（大文字小文字無視）。無ければ null。</summary>
    public string? GetTag(string name) =>
        Metadata.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;

    /// <summary>指定名の @タグを設定。value=null で削除。</summary>
    public void SetTag(string name, string? value)
    {
        int i = Metadata.FindIndex(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (value is null)
        {
            if (i >= 0) Metadata.RemoveAt(i);
        }
        else if (i >= 0)
        {
            Metadata[i].Value = value;
        }
        else
        {
            Metadata.Add(new MetadataTag(name, value));
        }
    }

    /// <summary>
    /// ページ分割ビューを返す。各ページは元の行インデックスのリスト。
    /// EmptyLine モードでは空行はどのページにも属さない。
    /// </summary>
    public List<List<int>> GetPages(PageSplitMode mode, int fixedLineCount = 2)
    {
        var pages = new List<List<int>>();
        var current = new List<int>();

        if (mode == PageSplitMode.EmptyLine)
        {
            for (int i = 0; i < Lines.Count; i++)
            {
                if (Lines[i].IsEmpty)
                {
                    if (current.Count > 0) { pages.Add(current); current = new List<int>(); }
                }
                else
                {
                    current.Add(i);
                }
            }
            if (current.Count > 0) pages.Add(current);
        }
        else
        {
            if (fixedLineCount < 1) fixedLineCount = 1;
            for (int i = 0; i < Lines.Count; i++)
            {
                if (Lines[i].IsEmpty) continue; // 固定行数モードでは空行は無視
                current.Add(i);
                if (current.Count >= fixedLineCount)
                {
                    pages.Add(current);
                    current = new List<int>();
                }
            }
            if (current.Count > 0) pages.Add(current);
        }

        return pages;
    }

    public LyricsDocument Clone()
    {
        var d = new LyricsDocument();
        foreach (var m in Metadata) d.Metadata.Add(new MetadataTag(m.Name, m.Value));
        foreach (var e in EmojiEntries) d.EmojiEntries.Add(e.Clone());
        d.UnappliedRubyEntries.AddRange(UnappliedRubyEntries);
        foreach (var l in Lines) d.Lines.Add(l.Clone());
        d.RlfExtras = RlfExtras?.Clone();
        return d;
    }
}
