using CommunityToolkit.Mvvm.ComponentModel;

namespace NicoKaraPrep.App.ViewModels;

/// <summary>メイン画面のビュー（画面モード）。</summary>
public enum MainViewMode
{
    /// <summary>行リスト（ドキュメントタブ・行リスト・行エディタ・ニコカラメーカー用の行設定・プレビュー）。</summary>
    Lines,

    /// <summary>絵文字挿入ビュー（F2。歌詞全体をキーボードだけで編集する）。</summary>
    EmojiInsert,

    /// <summary>フォント設定ビュー（F3。メニューとステータスバー以外の全体を使う）。</summary>
    FontSettings,

    /// <summary>レイアウト設定ビュー（F4。ニコカラメーカー3 のレイアウト設定と字幕アクション。メニューとステータスバー以外の全体を使う）。</summary>
    Layout,
}

/// <summary>メイン画面のビューの状態（切り替えの処理は MainWindow.SwitchView）。</summary>
public partial class MainViewModel
{
    /// <summary>
    /// 今表示しているビュー。読み取り専用として扱い、変更は必ず MainWindow.SwitchView から行う
    /// （ここを直接書き換えると切り替えの部品が追従するだけで、表示の入れ替えもビューの出入りの処理も行われず、
    /// 見た目と状態が食い違う）。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewModeLabel))]
    private MainViewMode viewMode = MainViewMode.Lines;

    /// <summary>ステータスバーに常に出す、今のビューの表示。</summary>
    public string ViewModeLabel => $"ビュー: {ViewModeName(ViewMode)}";

    /// <summary>ビューの名前。</summary>
    public static string ViewModeName(MainViewMode mode) => mode switch
    {
        MainViewMode.EmojiInsert => "絵文字挿入ビュー",
        MainViewMode.FontSettings => "フォント設定ビュー",
        MainViewMode.Layout => "レイアウト設定ビュー",
        _ => "行リスト",
    };

    /// <summary>
    /// メニューとステータスバー以外の全体を使うビュー（フォント設定ビュー・レイアウト設定ビュー）か。
    /// 行リスト・チェック結果が見えないので、行の操作・歌詞の 元に戻す・チェックを止め、戻る先は最後にいた行リストか絵文字挿入ビューにする。
    /// </summary>
    public static bool IsFullScreenView(MainViewMode mode) => mode is MainViewMode.FontSettings or MainViewMode.Layout;
}
