using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NicoKaraPrep.App.ViewModels;

namespace NicoKaraPrep.App.Views;

/// <summary>
/// ニコカラメーカー3 のレイアウト設定ビュー（F4）の仮の中身。メイン画面の骨組み（切り替え・戻る・元に戻すの受け渡し）を確かめるためのもので、
/// 本物（レイアウトの一覧・編集・見本・ページの一覧・字幕アクション）は同じファイル名で置き換える。公開するメンバーは本物と同じ。
/// </summary>
public sealed partial class LayoutView : UserControl
{
    // 初めて表示するときはまだ画面に載っておらずフォーカスを置けないため、Loaded で置く
    private bool _focusOnLoaded;

    private MainViewModel? _main;

    public LayoutView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (!_focusOnLoaded) return;
            _focusOnLoaded = false;
            LayoutBackButton.Focus(FocusState.Programmatic);
        };
    }

    /// <summary>「戻る」ボタンか Esc で、前のビューへ戻るよう求められたときに発生する。</summary>
    public event EventHandler? BackRequested;

    /// <summary>メイン画面の ViewModel を受け取る（作ったあと 1 回だけ呼ぶ）。</summary>
    public void Attach(MainViewModel main)
    {
        _main = main;
    }

    /// <summary>ビューに入るときに、行リストで選ばれていた行（表示中のタブの行番号）を受け取る（<see cref="Enter"/> の前に呼ぶ）。</summary>
    public void SetLineContext(IReadOnlyList<int> selectedLines)
    {
    }

    /// <summary>
    /// ビューに入ったときにメインウィンドウから呼ぶ。戻り先のビューの名前をボタンに出し、Esc を受けられるようにビューの中へフォーカスを置く。
    /// </summary>
    public void Enter(string backTargetName, string? selectLayoutName)
    {
        LayoutBackButton.Content = $"{backTargetName}へ戻る (Esc)";
        if (IsLoaded)
        {
            LayoutBackButton.Focus(FocusState.Programmatic);
        }
        else
        {
            _focusOnLoaded = true;
        }
    }

    /// <summary>ビューを抜けるときにメインウィンドウから呼ぶ（保存待ちの編集を保存する）。</summary>
    public void Exit() => FlushPendingSave();

    /// <summary>保存待ちの編集を保存する（ビューを抜けるとき・ウィンドウを閉じるとき）。</summary>
    public void FlushPendingSave()
    {
    }

    /// <summary>ビューの中の操作を 1 つ戻す（入力欄にフォーカスがあるときは何もしないで false）。</summary>
    public bool TryUndo() => false;

    /// <summary>ビューの中の操作をやり直す（入力欄にフォーカスがあるときは何もしないで false）。</summary>
    public bool TryRedo() => false;

    /// <summary>外で文書・タブ・ベースが変わった（一覧を作り直す）。</summary>
    public void OnDocumentChanged()
    {
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Esc で前のビューへ戻る。</summary>
    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled || e.Key != Windows.System.VirtualKey.Escape) return;
        e.Handled = true;
        BackRequested?.Invoke(this, EventArgs.Empty);
    }
}
