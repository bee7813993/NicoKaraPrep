using System.Text.Json;
using System.Text.Json.Nodes;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Project;

/// <summary>
/// 行ごとのニコカラメーカー3 書き出し設定（表示時刻の手動指定・フォント設定名・文字単位のフォント設定名・字幕アクション）。行インデックスと、
/// 保存したときの行の表示文字列・行の数で保存する。
/// 読み直すときは、同じ番号の行が同じ文字ならそこへ、違えば同じ文字の行のうち近いものへ当てる（歌詞ファイルを保存しないまま
/// 行を足し引きしたあとに開き直すと、番号だけでは別の行へ付くため。例: 定型文の行のフォント設定が 2 行下の歌詞の行に付いた）。
/// 同じ文字の行が無ければ、行の数が保存したときと同じ（外で歌詞を直しただけ）ときだけ同じ番号の行へ当て、違えば当てない。
/// 文字単位の指定は、保存したときの行の表示文字列と今の行が同じときだけ当てる（歌詞を外で直したときに別の文字へ付かないように）。
/// </summary>
public sealed class LineExportSettings
{
    public int Index { get; set; }

    /// <summary>表示開始時刻（10ms 単位）。null は自動。</summary>
    public int? ShowBeginCs { get; set; }

    /// <summary>表示終了時刻（10ms 単位）。null は自動。</summary>
    public int? ShowEndCs { get; set; }

    /// <summary>表示開始の出どころ（以前の版のファイルには無く、手動指定として読む）。</summary>
    public ShowTimeOrigin ShowBeginOrigin { get; set; }

    /// <summary>表示終了の出どころ（以前の版のファイルには無く、手動指定として読む）。</summary>
    public ShowTimeOrigin ShowEndOrigin { get; set; }

    /// <summary>フォント設定名の手動指定。null は自動。</summary>
    public string? FontSetName { get; set; }

    /// <summary>行のページのレイアウト設定名の手動指定。null は自動。</summary>
    public string? LayoutName { get; set; }

    /// <summary>行のページの文字の大きさの増減 px（0 = そのまま）。</summary>
    public int FontSizeDelta { get; set; }

    /// <summary>行の字幕アクションの Id の手動指定。null・空は曲の既定。</summary>
    public string? SubtitleActionId { get; set; }

    /// <summary>行の字幕アクションの設定値（n3proj の SubtitleActionSettings と同じ形。<c>$type</c> 付き）。無ければ Id の既定値で読む。</summary>
    public JsonObject? SubtitleActionSettings { get; set; }

    /// <summary>文字単位のフォント設定名の手動指定（表示文字の位置の範囲）。無ければ null。</summary>
    public List<CharFontRange>? CharFonts { get; set; }

    /// <summary><see cref="CharFonts"/> を保存したときの行の表示文字列（スペーサーを除く）。</summary>
    public string? CharText { get; set; }

    /// <summary>保存したときの行の表示文字列（以前の版のファイルには無く、そのときは番号だけで当てる）。</summary>
    public string? Text { get; set; }

    /// <summary>保存したときの文書の行の数（以前の版のファイルには無い）。</summary>
    public int? LineCount { get; set; }

    /// <summary>ドキュメントの行から手動設定を持つ行だけを集める。</summary>
    public static List<LineExportSettings> Collect(LyricsDocument doc)
    {
        var result = new List<LineExportSettings>();
        for (int i = 0; i < doc.Lines.Count; i++)
        {
            var l = doc.Lines[i];
            if (!l.HasN3Overrides) continue;
            var ranges = CharFontOperations.Ranges(l);
            result.Add(new LineExportSettings
            {
                Index = i,
                ShowBeginCs = l.ShowBeginCs,
                ShowEndCs = l.ShowEndCs,
                ShowBeginOrigin = l.ShowBeginOrigin,
                ShowEndOrigin = l.ShowEndOrigin,
                FontSetName = l.FontSetName,
                LayoutName = l.LayoutName,
                FontSizeDelta = l.FontSizeDelta,
                SubtitleActionId = l.SubtitleAction?.Id,
                SubtitleActionSettings = l.SubtitleAction is { } action ? (JsonObject)action.Settings.DeepClone() : null,
                CharFonts = ranges.Count > 0 ? ranges : null,
                CharText = ranges.Count > 0 ? l.GetDisplayText() : null,
                Text = l.GetDisplayText(),
                LineCount = doc.Lines.Count,
            });
        }
        return result;
    }

    /// <summary>保存した手動設定をドキュメントの行へ適用する（当てる行は <see cref="Locate"/>）。</summary>
    public static void Apply(LyricsDocument doc, IEnumerable<LineExportSettings>? settings)
    {
        if (settings is null) return;
        var list = settings.ToList();
        var used = new HashSet<int>();
        var target = new int[list.Count];
        // 番号も文字も合う行を先に決めてから、ずれた行を近い同じ文字の行へ（ずれた設定が、合っている行を取らないように）
        for (int k = 0; k < list.Count; k++)
        {
            var s = list[k];
            target[k] = s.Text is not null && s.Index >= 0 && s.Index < doc.Lines.Count && doc.Lines[s.Index].GetDisplayText() == s.Text && used.Add(s.Index) ? s.Index : -1;
        }
        for (int k = 0; k < list.Count; k++)
        {
            if (target[k] < 0) target[k] = Locate(doc, list[k], used);
        }
        for (int k = 0; k < list.Count; k++)
        {
            if (target[k] < 0) continue;
            var s = list[k];
            var l = doc.Lines[target[k]];
            l.ShowBeginCs = s.ShowBeginCs;
            l.ShowEndCs = s.ShowEndCs;
            l.ShowBeginOrigin = s.ShowBeginOrigin;
            l.ShowEndOrigin = s.ShowEndOrigin;
            l.FontSetName = string.IsNullOrEmpty(s.FontSetName) ? null : s.FontSetName;
            l.LayoutName = string.IsNullOrEmpty(s.LayoutName) ? null : s.LayoutName;
            l.FontSizeDelta = s.FontSizeDelta;
            l.SubtitleAction = s.SubtitleActionId is { Length: > 0 } actionId
                ? new N3SubtitleAction(actionId, s.SubtitleActionSettings?.DeepClone() as JsonObject ?? N3SubtitleActionCatalog.CreateDefault(actionId).Settings)
                : null;
            if (s.CharFonts is { Count: > 0 } && s.CharText == l.GetDisplayText()) CharFontOperations.ApplyRanges(l, s.CharFonts);
        }
    }

    /// <summary>
    /// 番号か文字が合わなかった設定を当てる行（無ければ -1）。同じ文字のまだ使っていない行のうち番号が近いもの（行の数の増減だけずらして測る）、
    /// 無ければ行の数が保存したときと同じなら同じ番号の行。以前の版のファイル（文字を持たない）は番号のまま。
    /// </summary>
    private static int Locate(LyricsDocument doc, LineExportSettings s, HashSet<int> used)
    {
        bool inRange = s.Index >= 0 && s.Index < doc.Lines.Count;
        if (s.Text is null) return inRange && used.Add(s.Index) ? s.Index : -1;

        // 近さは、行の数の増減だけずらした番号から測る（足し引きした場所より後ろの行は、その分ずれているはず）。同じなら元の番号に近いほう
        int expected = s.LineCount is int count ? s.Index + (doc.Lines.Count - count) : s.Index;
        int best = -1;
        for (int i = 0; i < doc.Lines.Count; i++)
        {
            if (used.Contains(i) || doc.Lines[i].GetDisplayText() != s.Text) continue;
            if (best < 0) best = i;
            else
            {
                int d = Math.Abs(i - expected), bd = Math.Abs(best - expected);
                if (d < bd || (d == bd && Math.Abs(i - s.Index) < Math.Abs(best - s.Index))) best = i;
            }
        }
        if (best >= 0)
        {
            used.Add(best);
            return best;
        }
        // 同じ文字の行が無い: 行の数が同じなら、外で歌詞を直しただけとみて同じ番号の行へ。行の数が違えば（行の足し引き）当てない
        return inRange && s.LineCount == doc.Lines.Count && used.Add(s.Index) ? s.Index : -1;
    }
}

/// <summary>曲ごとのニコカラメーカー3 プロジェクト書き出し設定。</summary>
public sealed class N3ProjSongSettings
{
    /// <summary>ベースにする既存の n3proj（フォント・レイアウト・タイトル等をここから引き継ぐ）。null = 標準設定で生成。</summary>
    public string? BasePath { get; set; }

    /// <summary>前回の書き出し先。</summary>
    public string? OutputPath { get; set; }

    /// <summary>プロジェクト名（ニコカラメーカーの「設定の名称」）。空 = 歌詞ファイル名。</summary>
    public string ProjectName { get; set; } = "";

    /// <summary>タブ名 → レイアウト名（空 = 行数に応じて自動）。</summary>
    public Dictionary<string, string> TabLayouts { get; set; } = new();

    /// <summary>タブ名 → 上段の行を長めに表示するか（ニコカラメーカーの TopLong 相当）。</summary>
    public Dictionary<string, bool> TabTopLong { get; set; } = new();

    /// <summary>
    /// タブ名 → タブの最初の行から使うフォント設定名（パート記号が出るまで。空・無し = 自動: メインのタブは既定のフォント設定、
    /// 2 つ目以降のタブは名前に「コーラス」を含むフォント設定）。ニコカラメーカー3 はタブをまたいでフォントを引き継がない。
    /// </summary>
    public Dictionary<string, string> TabFontSetNames { get; set; } = new();

    /// <summary>行の既定フォント設定名（パート記号が現れるまで適用）。空 = 先頭の設定。</summary>
    public string DefaultFontSetName { get; set; } = "";

    /// <summary>NicoKaraPrep 側のフォント設定をベースへマージするか。</summary>
    public bool MergeFontSets { get; set; } = true;

    /// <summary>NicoKaraPrep で編集したレイアウト設定を、ベースの同じ名前のレイアウト設定に上書き・無い名前は追加するか。</summary>
    public bool MergeLayouts { get; set; } = true;

    /// <summary>
    /// 曲の既定の字幕アクション（行ごとの指定が無い歌詞行に書くもの）。null = 自動（ベースの n3proj の歌詞行でいちばん多いアクション、
    /// 無ければニコカラメーカー3 の「すべて同じ字幕アクションにする」の Id、無ければ文字単位フェード。<c>N3ProjWriter.ResolveDefaultAction</c>）。
    /// </summary>
    public N3SubtitleAction? SubtitleAction { get; set; }
}

/// <summary>分離タブ 1 つ分の保存データ。</summary>
public sealed class SongProjectTab
{
    public string Name { get; set; } = "";

    /// <summary>タブの内容（テキスト編集モード形式。チェック数・ルビ込みでロスレス）。</summary>
    public string Text { get; set; } = "";

    /// <summary>エクスポート済みマークの付いた行インデックス。</summary>
    public List<int> ExportedLines { get; set; } = new();

    /// <summary>各行の元の行位置キー（分離解除で元の位置へ戻すため。Text の行と同順）。</summary>
    public List<int?> LineKeys { get; set; } = new();

    /// <summary>このタブを別ファイルへ保存した先（タブの上書き保存の対象）。</summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// 自分の歌詞ファイル（<see cref="FilePath"/>）を持つタブか（ニコカラメーカー3 のプロジェクトの 2 つ目以降の歌詞設定の歌詞など）。
    /// メインの歌詞ファイルには含めず、開き直したときは <see cref="FilePath"/> から読み込む。false は分離タブ。
    /// </summary>
    public bool OwnFile { get; set; }

    /// <summary>行ごとのニコカラメーカー3 書き出し設定。</summary>
    public List<LineExportSettings> LineSettings { get; set; } = new();
}

/// <summary>
/// 曲ごとの作業状態（.tttproj、歌詞ファイルの隣に保存）。
/// 済マーク・メディアファイルパスなど、歌詞ファイル自体を汚さない情報を保持する。
/// </summary>
public sealed class SongProject
{
    /// <summary>関連付けたメディアファイル（動画/音源）。</summary>
    public string? MediaPath { get; set; }

    /// <summary>エクスポート済みマークの付いた行インデックス（0 始まり）。</summary>
    public List<int> ExportedLines { get; set; } = new();

    /// <summary>曲内 @Emoji のスロット割り当て（置き換え文字列 → スロット番号 1–20）。並び替えの保存用。</summary>
    public Dictionary<string, int> EmojiSlots { get; set; } = new();

    /// <summary>分離タブ（メイン以外）。次回開いたとき復元する。</summary>
    public List<SongProjectTab> Tabs { get; set; } = new();

    /// <summary>
    /// タブ分離中のメインの内容（テキスト編集モード形式）。
    /// 分離は歌詞ファイル自体を書き換えないため、分離後のメインをここに保存して復元する。
    /// 分離タブが無いときは空。
    /// </summary>
    public string MainText { get; set; } = "";

    /// <summary>メインの各行の元の行位置キー（MainText の行と同順）。</summary>
    public List<int?> MainLineKeys { get; set; } = new();

    /// <summary>保存時の歌詞ファイルのフィンガープリント（外部編集の検出用）。</summary>
    public string FileFingerprint { get; set; } = "";

    /// <summary>メインの行ごとのニコカラメーカー3 書き出し設定（表示時刻の手動指定など）。</summary>
    public List<LineExportSettings> LineSettings { get; set; } = new();

    /// <summary>ニコカラメーカー3 プロジェクト書き出しの設定。</summary>
    public N3ProjSongSettings N3Proj { get; set; } = new();

    private List<N3FontSet> _fontSets = new();

    /// <summary>
    /// この曲専用のフォント設定。書き出しでは同じ名前のアプリ共通のフォント設定より優先する
    /// （<see cref="N3FontLibrary.ResolveForExport"/>）。
    /// null は空の一覧に、一覧の中の null は取り除く（手で編集したファイルなど）。
    /// </summary>
    public List<N3FontSet> FontSets
    {
        get => _fontSets;
        set
        {
            _fontSets = value ?? new();
            _fontSets.RemoveAll(f => f is null);
        }
    }

    /// <summary>歌詞ファイルのフィンガープリント（サイズ＋更新時刻）を計算する。</summary>
    public static string ComputeFingerprint(string filePath)
    {
        try
        {
            var info = new FileInfo(filePath);
            return info.Exists ? $"{info.Length}:{info.LastWriteTimeUtc.Ticks}" : "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>歌詞ファイルパスに対応するプロジェクトファイルのパス。</summary>
    public static string PathFor(string documentPath) => documentPath + ".tttproj";

    public static SongProject? TryLoad(string documentPath)
    {
        string path = PathFor(documentPath);
        try
        {
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<SongProject>(File.ReadAllText(path), JsonOptions);
            }
        }
        catch (Exception)
        {
            // 壊れたプロジェクトファイルは無視
        }
        return null;
    }

    public void Save(string documentPath)
    {
        File.WriteAllText(PathFor(documentPath), JsonSerializer.Serialize(this, JsonOptions));
    }
}
