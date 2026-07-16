namespace NicoKaraPrep.Core.Model;

/// <summary>行の分割・結合・空行操作。</summary>
public static class LineOperations
{
    /// <summary>
    /// 行 lineIndex を charIndex（CharUnit 単位）で 2 行に分割する。
    /// 前半行の行末タグが無い場合は、後半行の先頭タグを行末タグとして補う
    /// （分割前のワイプ終了時刻を保つため）。
    /// </summary>
    public static void SplitLine(LyricsDocument doc, int lineIndex, int charIndex)
    {
        var line = doc.Lines[lineIndex];
        charIndex = Math.Clamp(charIndex, 0, line.Chars.Count);

        var newLine = new LyricsLine { EndTimeCs = line.EndTimeCs };
        for (int i = charIndex; i < line.Chars.Count; i++)
        {
            newLine.Chars.Add(line.Chars[i]);
        }
        line.Chars.RemoveRange(charIndex, line.Chars.Count - charIndex);
        line.EndTimeCs = null;

        // 分割位置をまたぐルビ連結は切り離す
        if (line.Chars.Count > 0)
        {
            line.Chars[^1].RubyJoinsNext = false;
        }

        // 前半行のワイプ終了時刻を補完（後半行の最初のタグ）
        if (line.Chars.Count > 0 && newLine.GetFirstTimeCs() is int nextStart)
        {
            bool lastHasTag = line.Chars[^1].TimeCs is not null && line.Chars[^1].IsSpacer;
            if (!lastHasTag && line.EndTimeCs is null)
            {
                line.EndTimeCs = nextStart;
            }
        }

        doc.Lines.Insert(lineIndex + 1, newLine);
    }

    /// <summary>
    /// 行 lineIndex に次の行を結合する。
    /// 前半行の行末タグは、次行の先頭タグと同時刻なら捨て、異なるならスペーサー（2連タグ）として残す。
    /// </summary>
    public static void JoinWithNextLine(LyricsDocument doc, int lineIndex)
    {
        if (lineIndex + 1 >= doc.Lines.Count) return;
        var line = doc.Lines[lineIndex];
        var next = doc.Lines[lineIndex + 1];

        if (line.EndTimeCs is int endCs)
        {
            if (next.GetFirstTimeCs() != endCs)
            {
                line.Chars.Add(new CharUnit { Text = CharUnit.Spacer, TimeCs = endCs });
            }
            line.EndTimeCs = null;
        }

        line.Chars.AddRange(next.Chars);
        line.EndTimeCs = next.EndTimeCs;
        line.Exported = line.Exported && next.Exported;
        doc.Lines.RemoveAt(lineIndex + 1);
    }

    /// <summary>index の位置に空行（ページ区切り）を挿入する。</summary>
    public static void InsertEmptyLine(LyricsDocument doc, int index)
    {
        index = Math.Clamp(index, 0, doc.Lines.Count);
        doc.Lines.Insert(index, new LyricsLine());
    }

    /// <summary>行を削除する。</summary>
    public static void DeleteLine(LyricsDocument doc, int index)
    {
        if (index >= 0 && index < doc.Lines.Count)
        {
            doc.Lines.RemoveAt(index);
        }
    }

    /// <summary>
    /// 選択行だけを含む新しいドキュメントを作る（エクスポート用）。
    /// メタデータと実効絵文字リストを引き継ぐ。
    /// </summary>
    public static LyricsDocument ExtractLines(LyricsDocument doc, IEnumerable<int> lineIndexes, IEnumerable<EmojiEntry>? effectiveEmoji = null)
    {
        var result = new LyricsDocument();
        foreach (var m in doc.Metadata)
        {
            result.Metadata.Add(new MetadataTag(m.Name, m.Value));
        }
        foreach (var e in effectiveEmoji ?? doc.EmojiEntries)
        {
            result.EmojiEntries.Add(e.Clone());
        }
        result.UnappliedRubyEntries.AddRange(doc.UnappliedRubyEntries);

        foreach (int i in lineIndexes.Distinct().OrderBy(x => x))
        {
            if (i >= 0 && i < doc.Lines.Count)
            {
                var clone = doc.Lines[i].Clone();
                clone.Exported = false;
                result.Lines.Add(clone);
            }
        }
        return result;
    }
}
