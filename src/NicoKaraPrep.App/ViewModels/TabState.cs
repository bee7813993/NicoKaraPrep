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

    public bool IsClosable => !IsMain;
}
