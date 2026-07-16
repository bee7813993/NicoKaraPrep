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

        // 分割後の後半行は元の行と同じ位置キーを引き継ぐ（マージ時に元の行の直後へ並ぶ）
        var newLine = new LyricsLine { EndTimeCs = line.EndTimeCs, SplitOrderKey = line.SplitOrderKey };
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
    /// <param name="insertSpace">true なら結合位置（改行のあった場所）に半角スペースを挟む。</param>
    public static void JoinWithNextLine(LyricsDocument doc, int lineIndex, bool insertSpace = false)
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

        if (insertSpace && line.Chars.Count > 0 && next.Chars.Count > 0)
        {
            line.Chars.Add(new CharUnit { Text = " " });
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
        var line = new LyricsLine();
        if (index > 0)
        {
            line.SplitOrderKey = doc.Lines[index - 1].SplitOrderKey; // 直前の行に付いて並ぶ
        }
        doc.Lines.Insert(index, line);
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
    /// <summary>
    /// part の行を main へ挿入して戻す（タブ分離の解除・全行マージ用）。
    /// 元の行位置キー（SplitOrderKey）を持つ行はキー順に元の位置
    /// （ページ区切りとの前後関係を保った場所）へ、キーが無い行は
    /// 先頭タイムタグの時刻順に挿入する。
    /// </summary>
    public static void MergeLines(LyricsDocument main, LyricsDocument part)
    {
        int insertAfter = -1;
        foreach (var line in part.Lines)
        {
            int idx;
            if (line.SplitOrderKey is int key)
            {
                // メイン側でキーを持たない行（後から追加された行・空行）は
                // 直前のキーを引き継いだものとして扱い、その行に付いて並ばせる
                idx = 0;
                int lastEffectiveKey = int.MinValue;
                for (int i = 0; i < main.Lines.Count; i++)
                {
                    int effective = main.Lines[i].SplitOrderKey ?? lastEffectiveKey;
                    if (effective > key) break;
                    lastEffectiveKey = effective;
                    idx = i + 1;
                }
            }
            else if (line.GetFirstTimeCs() is int t)
            {
                idx = 0;
                while (idx < main.Lines.Count &&
                       (main.Lines[idx].GetFirstTimeCs() is not int mt || mt <= t))
                {
                    idx++;
                }
            }
            else
            {
                idx = insertAfter + 1; // キーもタグも無い行（空行など）は直前に挿入した行の次へ
            }
            main.Lines.Insert(idx, line);
            insertAfter = idx;
        }
    }

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
