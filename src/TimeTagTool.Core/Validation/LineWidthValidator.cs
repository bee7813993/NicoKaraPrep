using System.Text;
using TimeTagTool.Core.Model;

namespace TimeTagTool.Core.Validation;

/// <summary>テキスト描画幅の実測を抽象化する（App 側で DirectWrite 実装、テストではフェイク）。</summary>
public interface ITextMeasurer
{
    /// <summary>指定フォントで text を 1 行描画したときの幅（px）を返す。</summary>
    double MeasureWidth(string text, string fontFamily, double fontSize, bool bold, bool italic);
}

/// <summary>横幅チェックの設定。</summary>
public sealed class LineWidthSettings
{
    /// <summary>画面（動画）の横幅 px。</summary>
    public int ScreenWidthPx { get; set; } = 1920;

    /// <summary>字幕フォントファミリー。</summary>
    public string FontFamily { get; set; } = "メイリオ";

    /// <summary>字幕フォントサイズ px。</summary>
    public double FontSizePx { get; set; } = 80;

    public bool Bold { get; set; } = true;

    public bool Italic { get; set; }

    /// <summary>左右それぞれに確保したいマージン（画面幅に対する %）。</summary>
    public double SideMarginPercent { get; set; } = 5.0;

    /// <summary>絵文字（置き換え文字列）→ Zoom%。EmojiWidthPx 未登録時のフォールバック（正方形近似）。</summary>
    public Dictionary<string, double> EmojiZoomPercent { get; } = new();

    /// <summary>
    /// 絵文字（置き換え文字列）→ アイコン表示幅 px（画像の縦横比・Zoom・Fix・左右 Margin 込みの実寸計算値）。
    /// </summary>
    public Dictionary<string, double> EmojiWidthPx { get; } = new();

    /// <summary>絵文字（置き換え文字列、複数文字可）の集合。幅計算で画像幅に置き換える。</summary>
    public HashSet<string> EmojiChars { get; } = new();

    /// <summary>マージンを除いた使用可能幅 px。</summary>
    public double UsableWidthPx => ScreenWidthPx * (1 - SideMarginPercent * 2 / 100.0);
}

/// <summary>行ごとの幅計測結果。</summary>
/// <param name="LineIndex">行インデックス。</param>
/// <param name="WidthPx">描画幅 px。</param>
/// <param name="UsagePercent">使用可能幅（マージン除き）に対する使用率 %。</param>
/// <param name="Severity">null = 問題なし / Warning = マージン不足 / Error = 画面はみ出し。</param>
public sealed record LineWidthResult(int LineIndex, double WidthPx, double UsagePercent, IssueSeverity? Severity);

/// <summary>
/// 1 行の描画幅をフォント実測でチェックする。
/// エラー: 画面からはみ出す / 警告: 収まるが左右マージンを確保できない。
/// </summary>
public static class LineWidthValidator
{
    public static List<LineWidthResult> Measure(LyricsDocument doc, LineWidthSettings settings, ITextMeasurer measurer)
    {
        var results = new List<LineWidthResult>(doc.Lines.Count);

        for (int i = 0; i < doc.Lines.Count; i++)
        {
            var line = doc.Lines[i];
            if (line.IsEmpty)
            {
                results.Add(new LineWidthResult(i, 0, 0, null));
                continue;
            }

            double width = MeasureLine(line, settings, measurer);
            double usable = settings.UsableWidthPx;
            double usage = usable <= 0 ? 0 : width / usable * 100.0;

            IssueSeverity? severity = null;
            if (width > settings.ScreenWidthPx) severity = IssueSeverity.Error;
            else if (width > usable) severity = IssueSeverity.Warning;

            results.Add(new LineWidthResult(i, width, usage, severity));
        }

        return results;
    }

    public static List<ValidationIssue> ToIssues(IEnumerable<LineWidthResult> results, LineWidthSettings settings)
    {
        var issues = new List<ValidationIssue>();
        foreach (var r in results)
        {
            if (r.Severity is not IssueSeverity s) continue;
            string message = s == IssueSeverity.Error
                ? $"{r.LineIndex + 1}行目: 画面からはみ出します（{r.WidthPx:F0}px > 画面 {settings.ScreenWidthPx}px）"
                : $"{r.LineIndex + 1}行目: 左右マージン{settings.SideMarginPercent:F0}%を確保できません（{r.WidthPx:F0}px > 有効幅 {settings.UsableWidthPx:F0}px）";
            issues.Add(new ValidationIssue(s, "横幅", r.LineIndex, message));
        }
        return issues;
    }

    private static double MeasureLine(LyricsLine line, LineWidthSettings settings, ITextMeasurer measurer)
    {
        // 絵文字（複数文字の置き換え文字列を含む）は画像に置き換わるため、
        // テキストから除いて正方形近似で加算する
        var matcher = new EmojiMatcher(settings.EmojiChars);
        var occurrences = matcher.FindOccurrences(line.Chars);

        var sb = new StringBuilder();
        double emojiWidth = 0;
        int occIdx = 0;

        for (int i = 0; i < line.Chars.Count; i++)
        {
            if (occIdx < occurrences.Count && occurrences[occIdx].Start == i)
            {
                var occ = occurrences[occIdx];
                if (settings.EmojiWidthPx.TryGetValue(occ.Value, out double px))
                {
                    emojiWidth += px; // 画像実寸ベースの計算値
                }
                else
                {
                    double zoom = settings.EmojiZoomPercent.TryGetValue(occ.Value, out double z) ? z : 100.0;
                    emojiWidth += settings.FontSizePx * zoom / 100.0; // 正方形近似
                }
                i = occ.EndExclusive - 1;
                occIdx++;
                continue;
            }
            if (!line.Chars[i].IsSpacer)
            {
                sb.Append(line.Chars[i].Text);
            }
        }

        double textWidth = sb.Length == 0
            ? 0
            : measurer.MeasureWidth(sb.ToString(), settings.FontFamily, settings.FontSizePx, settings.Bold, settings.Italic);
        return textWidth + emojiWidth;
    }
}
