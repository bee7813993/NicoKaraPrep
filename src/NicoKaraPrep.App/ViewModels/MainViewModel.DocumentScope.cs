using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.ViewModels;

/// <summary>
/// 新規作成・ほかの曲を開くときに、新しい文書に出なくなるもの（この曲だけのもの）と、そのまま使えるもの（アプリ共通）。
/// </summary>
public sealed class DocumentScopeSummary
{
    /// <summary>この曲だけのもの（.tttproj・歌詞ファイルに保存。保存した分は、その曲を開き直すと戻る）。説明の文の並び。</summary>
    public List<string> SongItems { get; } = new();

    /// <summary>この曲だけのもののうち、数えられるものの短い名前（ステータスバーの知らせ用。「曲専用のフォント設定 2 件」など）。</summary>
    public List<string> SongShort { get; } = new();

    /// <summary>アプリ共通のもの（settings.json に保存。どの曲でも使える）。説明の文の並び。</summary>
    public List<string> CommonItems { get; } = new();

    /// <summary>今の歌詞をファイルに保存しているか（保存していなければ、この曲だけのものは開き直しても戻らない）。</summary>
    public bool HasFile { get; init; }
}

/// <summary>新規作成・ほかの曲を開くときの「何が残るか」の説明。</summary>
public partial class MainViewModel
{
    /// <summary>今の文書について、新しい文書に出なくなるもの・そのまま使えるものを数える。</summary>
    public DocumentScopeSummary DescribeDocumentScope()
    {
        StoreActiveTab();
        var s = new DocumentScopeSummary { HasFile = Tabs.FirstOrDefault(t => t.IsMain)?.FilePath is not null };
        void Song(string item, string? shortName = null)
        {
            s.SongItems.Add(item);
            if (shortName is not null) s.SongShort.Add(shortName);
        }

        if (SongFontSets.Count > 0) Song($"この曲専用のフォント設定 {SongFontSets.Count} 件", $"曲専用のフォント設定 {SongFontSets.Count} 件");
        int songEmoji = Tabs.SelectMany(t => t.Document.EmojiEntries)
            .Where(e => e.ReplaceChar.Length > 0).Select(e => e.ReplaceChar).Distinct(StringComparer.Ordinal).Count();
        if (songEmoji > 0) Song($"この曲の絵文字 {songEmoji} 件（パレットの「曲」の印のもの）", $"曲の絵文字 {songEmoji} 件");
        int manual = Tabs.SelectMany(t => t.Document.Lines).Count(l => l.HasManualN3Overrides);
        if (manual > 0) Song($"行ごとの手動指定（表示時刻・フォント・レイアウトなど）{manual} 行", $"行ごとの手動指定 {manual} 行");
        int split = Tabs.Count(t => !t.IsMain);
        if (split > 0) Song($"分けたタブ {split} 個", $"分けたタブ {split} 個");
        Song("書き出しの設定（書き出しのベースの n3proj・字幕アクションの曲の既定など）");
        if (MediaPath is { Length: > 0 } media) Song($"動画・音声（{Path.GetFileName(media)}）", "動画・音声");

        int fonts = Settings.N3FontSets.Count;
        int folders = N3FontTree.Walk(Settings.N3FontHierarchy ?? new List<N3FontTreeNode>()).Count(x => x.Node.IsFolder);
        s.CommonItems.Add(folders > 0 ? $"フォント設定 {fonts} 件（フォルダ {folders} 個）" : $"フォント設定 {fonts} 件");
        s.CommonItems.Add($"絵文字 {Settings.GlobalEmojiList.Count(e => e.ReplaceChar.Length > 0)} 件（パレットのキーの位置も）");
        if (Settings.N3Layouts.Count > 0) s.CommonItems.Add($"にこぷれっぷで編集したレイアウト設定 {Settings.N3Layouts.Count} 件");
        s.CommonItems.Add($"定型文 {Settings.InsertPhrases.Count} 件");
        s.CommonItems.Add("表示時刻の自動調整のパラメーター・ページ区切り・画面の横幅などの設定");
        return s;
    }

    /// <summary>新規作成のあとのステータスバーの知らせ（そのまま使えるもの・前の曲から出なくなったもの）。</summary>
    public static string NewDocumentNotice(DocumentScopeSummary before)
    {
        const string kept = "アプリ共通のフォント設定・絵文字・レイアウト設定・定型文は、そのまま使えます";
        if (before.SongShort.Count == 0) return $"新規作成しました。{kept}";
        string back = before.HasFile ? "。保存した分は、その曲を開き直すと戻ります" : "";
        return $"新規作成しました。{kept}。前の曲だけのもの（{string.Join("・", before.SongShort)}）は出ていません{back}";
    }
}
