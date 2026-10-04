using System.Collections.ObjectModel;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.ViewModels;

/// <summary>パレットの定型文 1 件（番号と文字列）。</summary>
/// <param name="Number">番号（1 始まり。10 は「0」のキー）。</param>
/// <param name="KeyLabel">番号のキー（1–9・0。11 件目からは空）。</param>
/// <param name="Text">定型文（「{秒}」を含むことがある）。</param>
public sealed record PhraseItem(int Number, string KeyLabel, string Text)
{
    public string ToolTip => Text.Contains(PhraseTagger.SecondsToken)
        ? $"{Text}\n押すと、カーソルの位置に入れます（行頭・行末・空行なら定型文だけのページにして、前後のページと表示が重ならない時刻に。{PhraseTagger.SecondsToken} は前後の歌のあいだの秒数になります）"
        : $"{Text}\n押すと、カーソルの位置に入れます（行頭・行末・空行なら定型文だけのページにして、前後のページと表示が重ならない時刻に）";
}

/// <summary>
/// 定型文（「（前奏）」「（間奏 約{秒}秒）」「（後奏）」など）の挿入と削除。絵文字挿入ビューのパレットのクリックと、キー（N のあとに番号）で入れ、
/// BS / Del でまとめて消す。タイムタグは <see cref="PhraseTagger"/>（ワイプ前・ワイプ後は 表示時刻 のパラメーター）。
/// </summary>
public partial class MainViewModel
{
    /// <summary>パレットに並べる定型文（設定の順）。</summary>
    public ObservableCollection<PhraseItem> InsertPhraseItems { get; } = new();

    /// <summary>設定の定型文からパレットの一覧を作り直す。</summary>
    public void RefreshInsertPhrases()
    {
        InsertPhraseItems.Clear();
        var phrases = Settings.InsertPhrases ?? new List<string>();
        for (int i = 0; i < phrases.Count; i++)
        {
            string key = i < 9 ? (i + 1).ToString() : i == 9 ? "0" : "";
            InsertPhraseItems.Add(new PhraseItem(i + 1, key, phrases[i]));
        }
    }

    /// <summary>番号（1 始まり）の定型文（無ければ null）。</summary>
    public string? GetInsertPhrase(int number) =>
        Settings.InsertPhrases is { } list && number >= 1 && number <= list.Count ? list[number - 1] : null;

    /// <summary>定型文の時刻の決め方（表示時刻のワイプ前・ワイプ後。空行がページ区切りなら定型文だけのページにする）。</summary>
    private PhraseTiming PhraseTiming => new()
    {
        LeadCs = Settings.DisplayLeadCs,
        TailCs = Settings.DisplayTailCs,
        SeparatePages = Settings.PageMode == PageSplitMode.EmptyLine,
    };

    /// <summary>
    /// 挿入ビューのカーソル位置に定型文を入れ、タイムタグを付ける（Ctrl+Z・BS / Del で戻せる）。
    /// 絵文字の置き換え文字列の途中なら、その後ろへ入れる。入れた後ろのカーソル位置を返す。
    /// </summary>
    public int? InsertPhraseAtViewOffset(int offset, string template)
    {
        if (MapInsertViewOffset(offset) is not var (lineIndex, charOffset)) return null;
        var line = Document.Lines[lineIndex];
        int unitIndex = DisplayOffsetToUnitIndex(line, charOffset);
        foreach (var occ in CreateEmojiMatcher().FindOccurrences(line.Chars))
        {
            if (unitIndex > occ.Start && unitIndex < occ.EndExclusive)
            {
                unitIndex = occ.EndExclusive;
                break;
            }
        }

        PushUndo();
        var timing = PhraseTiming;
        var r = PhraseTagger.Insert(Document, lineIndex, unitIndex, template, timing);
        RebuildLinesPreservingMarks();
        MarkModified();

        string where = r.OwnLine ? "定型文だけの行として" : "";
        string time = r.StartCs is int s && r.EndCs is int e
            ? r.OwnLine && !r.Squeezed
                ? $"タグ {TimeTag.Format(s)} 〜 {TimeTag.Format(e)}、画面に出るのは {Plain(s - timing.LeadCs)} 〜 {Plain(e + timing.TailCs)}"
                : $"タグ {TimeTag.Format(s)} 〜 {TimeTag.Format(e)}"
            : "前後にタイムタグのある歌が無いため、時刻は付けていません";
        string squeezed = r.Squeezed ? "。前後の間が短いので、前後の歌に合わせました" : "";
        StatusText = $"定型文「{r.Text}」を{where}入れました（{time}{squeezed}。BS / Del・Ctrl+Z で消せます）";
        _noticeBeforeCheck = StatusText;
        return GetInsertViewLineStart(r.LineIndex) + r.DisplayOffset + r.Text.Length;

        static string Plain(int cs) => TimeTag.Format(Math.Max(0, cs)).Trim('[', ']');
    }

    /// <summary>
    /// 挿入ビューのカーソル位置の定型文を消す（Delete はカーソルの後ろ、Backspace は前の定型文）。定型文だけの行は、入れたときに足した空行ごと消す。
    /// 消したら消したあとのカーソル位置、カーソルの所に定型文が無ければ null を返す。
    /// </summary>
    public int? DeletePhraseAtViewOffset(int offset, bool forward)
    {
        if (MapInsertViewOffset(offset) is not var (lineIndex, charOffset)) return null;
        var templates = Settings.InsertPhrases ?? new List<string>();
        if (templates.Count == 0) return null;
        var line = Document.Lines[lineIndex];
        var target = PhraseTagger.FindOccurrences(line, templates).FirstOrDefault(o => forward
            ? charOffset >= o.DisplayStart && charOffset < o.DisplayStart + o.Value.Length
            : charOffset > o.DisplayStart && charOffset <= o.DisplayStart + o.Value.Length);
        if (target is null) return null;

        PushUndo();
        var (caretLine, caretOffset) = PhraseTagger.Delete(Document, lineIndex, target);
        RebuildLinesPreservingMarks();
        MarkModified();
        StatusText = $"定型文「{target.Value}」を消しました（Ctrl+Z で元に戻せます）";
        return GetInsertViewLineStart(caretLine) + caretOffset;
    }
}
