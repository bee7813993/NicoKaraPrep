using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Validation;

/// <summary>
/// 0 秒より前（マイナス）のタイムタグを持つ行を知らせる（警告）。内部ではマイナスのまま持つが、保存・書き出しでは 0 秒になる
/// （<see cref="NegativeTimes"/>）。字幕全体の時刻を遅らせると、マイナスの値から計算して戻せる。
/// </summary>
public static class NegativeTimeValidator
{
    public const string Category = "0 秒より前";

    public static List<ValidationIssue> Validate(LyricsDocument doc)
    {
        var issues = new List<ValidationIssue>();
        for (int i = 0; i < doc.Lines.Count; i++)
        {
            var negatives = NegativeTimes.TagValues(doc.Lines[i]).Where(v => v < 0).ToList();
            if (negatives.Count == 0) continue;
            string count = negatives.Count > 1 ? $" {negatives.Count} 個" : "";
            issues.Add(new ValidationIssue(IssueSeverity.Warning, Category, i,
                $"{i + 1}行目: 0 秒より前の時刻があります{count}（{NegativeTimes.FormatSeconds(negatives.Min())}）。保存・書き出しでは 0 秒になります（字幕全体の時刻を遅らせると、この値から計算します）"));
        }
        return issues;
    }
}
