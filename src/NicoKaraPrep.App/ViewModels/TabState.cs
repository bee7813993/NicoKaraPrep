using CommunityToolkit.Mvvm.ComponentModel;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.ViewModels;

/// <summary>
/// ドキュメントタブ 1 つ分の状態。
/// メインタブ = 開いたファイル本体。分離タブ = 「選択行を新しいタブへ分離」で切り出した行の集まり。
/// </summary>
public partial class TabState : ObservableObject
{
    [ObservableProperty]
    private string name = "メイン";

    public LyricsDocument Document { get; set; } = new();

    /// <summary>このタブを保存したファイル（未保存の分離タブは null）。</summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// 「表示中のタブを別ファイルへ保存」した先（タブの上書き保存の対象）。
    /// タブへのファイル読み込み（差し替え）でも設定される。
    /// </summary>
    public string? CopyFilePath { get; set; }

    public DocumentFormat Format { get; set; } = DocumentFormat.Lrc;

    public bool IsModified { get; set; }

    public List<LyricsDocument> UndoStack { get; } = new();

    public List<LyricsDocument> RedoStack { get; } = new();

    /// <summary>メインタブかどうか（メインは閉じられない）。</summary>
    public bool IsMain { get; init; }

    /// <summary>
    /// 自分の歌詞ファイル（<see cref="CopyFilePath"/>）を持つタブか（ニコカラメーカー3 のプロジェクトの 2 つ目以降の歌詞設定の歌詞を開いたもの）。
    /// メインの歌詞ファイルの保存（タブ含む全行）には含めず、上書き保存で自分のファイルへ保存する。閉じてもメインへは戻さない。
    /// false は分離タブ（メインの歌詞ファイルの行を分けたもの）。
    /// </summary>
    public bool OwnFile { get; init; }

    public bool IsClosable => !IsMain;

    /// <summary>タブの見出しのツールチップ。</summary>
    public string ToolTipText => IsMain ? "メインの歌詞"
        : OwnFile ? $"別の歌詞ファイル: {System.IO.Path.GetFileName(CopyFilePath)}（上書き保存でこのファイルへ保存します。閉じてもメインへは戻しません）"
        : "分離タブ（上書き保存ではメインの歌詞ファイルにまとめて保存します。閉じるとメインへ戻します）";
}
