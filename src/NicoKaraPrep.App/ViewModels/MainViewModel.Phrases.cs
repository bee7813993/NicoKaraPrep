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
        ? $"{Text}\n押すと、カーソルの位置に前後の歌に合わせたタイムタグで入れます（{PhraseTagger.SecondsToken} は前後の歌のあいだの秒数になります）"
        : $"{Text}\n押すと、カーソルの位置に前後の歌に合わせたタイムタグで入れます";
}

/// <summary>
/// 定型文（「（後奏）」「（間奏 約{秒}秒）」など）の挿入。絵文字挿入ビューのパレットのクリックと、キー（N のあとに番号）で入れる。
/// タイムタグは前後の歌に合わせる（<see cref="PhraseTagger"/>）。
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

    /// <summary>
    /// 挿入ビューのカーソル位置に定型文を入れ、前後の歌に合わせてタイムタグを付ける（Ctrl+Z で戻せる）。
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
        int displayBefore = line.Chars.Take(unitIndex).Where(c => !c.IsSpacer).Sum(c => c.Text.Length);

        PushUndo();
        var result = PhraseTagger.Insert(Document, lineIndex, unitIndex, template, PhraseTagger.DefaultTailCs, Settings.EmojiLeadCs);
        if (lineIndex < Lines.Count) Lines[lineIndex].RaiseAllChanged();
        MarkModified();

        StatusText = result.StartCs is int s && result.EndCs is int e
            ? $"定型文「{result.Text}」を入れました（{TimeTag.Format(s)} 〜 {TimeTag.Format(e)}。Ctrl+Z で元に戻せます）"
            : $"定型文「{result.Text}」を入れました（前後にタイムタグのある歌が無いため、時刻は付けていません）";
        _noticeBeforeCheck = StatusText;
        return GetInsertViewLineStart(lineIndex) + displayBefore + result.Text.Length;
    }
}
