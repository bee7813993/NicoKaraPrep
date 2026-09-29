using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace NicoKaraPrep.App.Views;

/// <summary>
/// ニコカラメーカー3 のフォント設定ビュー（F3）。
/// メインウィンドウの中で表示を入れ替えるだけなので、閉じても行リストの選択・文書・再生状態はそのまま残る。
/// </summary>
public sealed partial class FontSettingsView : UserControl
{
    // 初めて表示するときはまだ画面に載っておらずフォーカスを置けないため、Loaded で置く
    private bool _focusOnLoaded;

    public FontSettingsView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (!_focusOnLoaded) return;
            _focusOnLoaded = false;
            BackButton.Focus(FocusState.Programmatic);
        };
    }

    /// <summary>「戻る」ボタンか Esc で、前のビューへ戻るよう求められたときに発生する。</summary>
    public event EventHandler? BackRequested;

    /// <summary>
    /// ビューに入ったときにメインウィンドウから呼ぶ。戻り先のビューの名前をボタンに出し、
    /// Esc を受けられるようにビューの中へフォーカスを置く。
    /// </summary>
    public void Enter(string backTargetName)
    {
        BackButton.Content = $"{backTargetName}へ戻る (Esc)";
        if (IsLoaded)
        {
            BackButton.Focus(FocusState.Programmatic);
        }
        else
        {
            _focusOnLoaded = true;
        }
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Esc で前のビューへ戻る。ComboBox のドロップダウンやフライアウトを閉じる Esc は
    /// そちらで処理済み（Handled）になり、ここへは届かない。
    /// </summary>
    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled || e.Key != Windows.System.VirtualKey.Escape) return;
        e.Handled = true;
        BackRequested?.Invoke(this, EventArgs.Empty);
    }
}
