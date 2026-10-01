using System.Text;

namespace NicoKaraPrep.Core.Model;

/// <summary>
/// 歌詞 1 行。空行（ページ区切り）も 1 行として保持する。
/// </summary>
public sealed class LyricsLine
{
    public List<CharUnit> Chars { get; } = new();

    /// <summary>行末タイムタグ（最後の文字の後ろに置かれるタグ）。</summary>
    public int? EndTimeCs { get; set; }

    /// <summary>エクスポート済みマーク。</summary>
    public bool Exported { get; set; }

    /// <summary>
    /// タブ分離時の元の行位置キー（ドキュメント読込時の行番号）。
    /// 分離解除・全行マージのとき、時刻ではなくこのキーで元の位置
    /// （ページ区切りとの前後関係）へ戻すために使う。ファイル形式には保存しない。
    /// </summary>
    public int? SplitOrderKey { get; set; }

    // ---- ニコカラメーカー3 プロジェクト書き出し用の行設定（歌詞ファイルには保存せず .tttproj に保存） ----

    /// <summary>行の表示開始時刻の手動指定（10ms 単位）。null は自動計算（先頭タグ − 表示前秒数）。</summary>
    public int? ShowBeginCs { get; set; }

    /// <summary>行の表示終了時刻の手動指定（10ms 単位）。null は自動計算（最終タグ ＋ 表示後秒数）。</summary>
    public int? ShowEndCs { get; set; }

    /// <summary>行に適用するニコカラメーカーのフォント設定名の手動指定。null は自動（パート記号で判定）。</summary>
    public string? FontSetName { get; set; }

    /// <summary>文字単位のフォント設定名の手動指定（<see cref="CharUnit.FontSetName"/>）を 1 つでも持つか。</summary>
    public bool HasCharFonts => Chars.Any(c => c.FontSetName is not null);

    /// <summary>ニコカラメーカー3 書き出し用の手動設定（表示時刻・行のフォント・文字のフォント）を 1 つでも持つか。</summary>
    public bool HasN3Overrides => ShowBeginCs is not null || ShowEndCs is not null || FontSetName is not null || HasCharFonts;

    /// <summary>空行（ページ区切り）かどうか。</summary>
    public bool IsEmpty => Chars.Count == 0 && EndTimeCs is null;

    /// <summary>表示文字列（スペーサー除外）。</summary>
    public string GetDisplayText()
    {
        var sb = new StringBuilder();
        foreach (var c in Chars)
        {
            if (!c.IsSpacer) sb.Append(c.Text);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 行の先頭タイムタグ（10ms 単位）。
    /// excludeChar が true を返す文字（例: 絵文字）のタグは無視する。
    /// </summary>
    public int? GetFirstTimeCs(Func<CharUnit, bool>? excludeChar = null)
    {
        foreach (var c in Chars)
        {
            if (excludeChar is not null && excludeChar(c)) continue;
            if (c.TimeCs is int t) return t;
        }
        return EndTimeCs;
    }

    /// <summary>
    /// 行の最終タイムタグ（10ms 単位）。
    /// excludeChar が true を返す文字（例: 絵文字）のタグは無視する。
    /// </summary>
    public int? GetLastTimeCs(Func<CharUnit, bool>? excludeChar = null)
    {
        if (EndTimeCs is int e) return e;
        for (int i = Chars.Count - 1; i >= 0; i--)
        {
            var c = Chars[i];
            if (excludeChar is not null && excludeChar(c)) continue;
            if (c.TimeCs is int t) return t;
        }
        return null;
    }

    public LyricsLine Clone()
    {
        var l = new LyricsLine
        {
            EndTimeCs = EndTimeCs,
            Exported = Exported,
            SplitOrderKey = SplitOrderKey,
            ShowBeginCs = ShowBeginCs,
            ShowEndCs = ShowEndCs,
            FontSetName = FontSetName,
        };
        foreach (var c in Chars) l.Chars.Add(c.Clone());
        return l;
    }
}
