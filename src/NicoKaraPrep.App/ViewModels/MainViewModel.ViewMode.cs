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
        _ => "行リスト",
    };
}
