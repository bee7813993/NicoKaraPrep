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

    /// <summary>
    /// 行の表示開始時刻（10ms 単位）。null は自動計算（ページの歌い出し − ワイプ前）。値の出どころは <see cref="ShowBeginOrigin"/>
    /// （手で指定した値・n3proj から読み込んだ値・自動調整を実行して決めた値）。値を持つ表示時刻は、書き出し・画面の表示でそのまま使う。
    /// </summary>
    public int? ShowBeginCs { get; set; }

    /// <summary>行の表示終了時刻（10ms 単位）。null は自動計算（最終タグ ＋ ワイプ後）。値の出どころは <see cref="ShowEndOrigin"/>。</summary>
    public int? ShowEndCs { get; set; }

    /// <summary><see cref="ShowBeginCs"/> の出どころ（値が null のときは意味を持たない）。</summary>
    public ShowTimeOrigin ShowBeginOrigin { get; set; }

    /// <summary><see cref="ShowEndCs"/> の出どころ（値が null のときは意味を持たない）。</summary>
    public ShowTimeOrigin ShowEndOrigin { get; set; }

    /// <summary>表示開始を手で指定しているか（自動調整を実行し直しても変えない値）。</summary>
    public bool HasManualShowBegin => ShowBeginCs is not null && ShowBeginOrigin == ShowTimeOrigin.Manual;

    /// <summary>表示終了を手で指定しているか（自動調整を実行し直しても変えない値）。</summary>
    public bool HasManualShowEnd => ShowEndCs is not null && ShowEndOrigin == ShowTimeOrigin.Manual;

    /// <summary>行に適用するニコカラメーカーのフォント設定名の手動指定。null は自動（パート記号で判定）。</summary>
    public string? FontSetName { get; set; }

    /// <summary>
    /// 行のページに適用するニコカラメーカーのレイアウト設定名の手動指定。null は自動（ページの行数から選ぶ）。
    /// ニコカラメーカー3 と同じくページ単位で効く（ページの中で最初に指定のある行のもの）。
    /// </summary>
    public string? LayoutName { get; set; }

    /// <summary>
    /// 行のページの文字の大きさの増減 px（0 = そのまま。ニコカラメーカー3 には無い NicoKaraPrep の機能）。ページ単位で効く（ページの中で最初に 0 以外を持つ行のもの）。
    /// 書き出しでは、ページの文字に当たるフォント設定を、文字の大きさだけを変えたもの（<see cref="N3PageFontSize"/>）に置き換える。
    /// </summary>
    public int FontSizeDelta { get; set; }

    /// <summary>
    /// 行の字幕アクション（ニコカラメーカー3 の SubtitleActionId と設定値）の手動指定。null は曲の既定（曲の設定、無ければベースの n3proj・
    /// ニコカラメーカー3 の設定から決めたもの。<c>N3ProjWriter.ResolveDefaultAction</c>）。
    /// ニコカラメーカー3 と同じく行ごとに効く（行の全文字に同じアクション）。
    /// </summary>
    public N3SubtitleAction? SubtitleAction { get; set; }

    /// <summary>文字単位のフォント設定名の手動指定（<see cref="CharUnit.FontSetName"/>）を 1 つでも持つか。</summary>
    public bool HasCharFonts => Chars.Any(c => c.FontSetName is not null);

    /// <summary>
    /// ニコカラメーカー3 書き出し用の行設定（表示時刻・行のフォント・文字のフォント・字幕アクションなど）を 1 つでも持つか（保存する行・解除できる行）。
    /// 表示時刻は出どころを問わない（読み込んだ値・自動調整の値も含む）。
    /// </summary>
    public bool HasN3Overrides => ShowBeginCs is not null || ShowEndCs is not null || FontSetName is not null || LayoutName is not null || FontSizeDelta != 0 || SubtitleAction is not null || HasCharFonts;

    /// <summary>
    /// 手で指定したニコカラメーカー3 書き出し用の行設定を 1 つでも持つか（行リストの ✎）。
    /// 表示時刻は手で指定したものだけを数える（読み込んだ値・自動調整の値は数えない）。
    /// </summary>
    public bool HasManualN3Overrides => HasManualShowBegin || HasManualShowEnd || FontSetName is not null || LayoutName is not null || FontSizeDelta != 0 || SubtitleAction is not null || HasCharFonts;

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

    /// <summary>
    /// 歌詞を書き換えて作り直した行（テキスト編集モードの書き方から読み直した行など）へ、元の行 <paramref name="from"/> の
    /// 歌詞に依らない行の設定（タブ分離の元の行位置キー・表示時刻・行のフォント・レイアウト・ページの文字の大きさ・字幕アクション）を写す。
    /// 文字ごとのフォントは <see cref="CharFontOperations.CopyCharFonts"/>、エクスポート済みの印は呼び出し側で扱う。
    /// 行の設定を足したら、ここと <see cref="Clone"/> にも足す（足し忘れると行エディタで直したときに消える）。
    /// </summary>
    public void CopyLineSettingsFrom(LyricsLine from)
    {
        SplitOrderKey = from.SplitOrderKey;
        ShowBeginCs = from.ShowBeginCs;
        ShowEndCs = from.ShowEndCs;
        ShowBeginOrigin = from.ShowBeginOrigin;
        ShowEndOrigin = from.ShowEndOrigin;
        FontSetName = from.FontSetName;
        LayoutName = from.LayoutName;
        FontSizeDelta = from.FontSizeDelta;
        SubtitleAction = from.SubtitleAction?.Clone();
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
            ShowBeginOrigin = ShowBeginOrigin,
            ShowEndOrigin = ShowEndOrigin,
            FontSetName = FontSetName,
            LayoutName = LayoutName,
            FontSizeDelta = FontSizeDelta,
            SubtitleAction = SubtitleAction?.Clone(),
        };
        foreach (var c in Chars) l.Chars.Add(c.Clone());
        return l;
    }
}
