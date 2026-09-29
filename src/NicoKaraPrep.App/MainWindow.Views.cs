using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NicoKaraPrep.App.ViewModels;
using NicoKaraPrep.App.Views;

namespace NicoKaraPrep.App;

/// <summary>
/// メイン画面のビュー（行リスト／絵文字挿入／フォント設定）の切り替え。
/// どのビューも同じウィンドウの中で表示（Visibility）を入れ替えるだけなので、
/// 行リストの選択・文書・再生状態は切り替えてもそのまま残る。
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>直前のビュー（フォント設定ビューから戻る先）。</summary>
    private MainViewMode _previousViewMode = MainViewMode.Lines;

    private bool _switchingView;

    /// <summary>切り替えの部品（SelectorBar・表示メニュー・右パネルのトグル）を今のビューに合わせている最中か。</summary>
    private bool _syncingViewSwitchers;

    private bool InsertViewActive => ViewModel.ViewMode == MainViewMode.EmojiInsert;

    /// <summary>フォント設定ビュー（初めて開くときに作る。以後は閉じても残し、表示だけを切り替える）。</summary>
    private FontSettingsView? _fontView;

    /// <summary>フォント設定ビューを返す。まだ無ければ作って FontViewHost に置く。</summary>
    private FontSettingsView EnsureFontView()
    {
        if (_fontView is null)
        {
            _fontView = new FontSettingsView();
            _fontView.BackRequested += OnFontViewBackRequested;
            FontViewHost.Child = _fontView;
        }
        return _fontView;
    }

    /// <summary>起動時に 1 回呼ぶ。切り替えの部品を今のビューに合わせ、以後は ViewMode の変更に追従させる。</summary>
    private void InitializeViewSwitching()
    {
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ViewMode)) SyncViewSwitchers();
        };
        SyncViewSwitchers();
    }

    /// <summary>ビューを切り替える。ビューの出入りはすべてここを通す。</summary>
    private void SwitchView(MainViewMode mode)
    {
        if (_switchingView) return;
        MainViewMode current = ViewModel.ViewMode;
        if (mode == current)
        {
            SyncViewSwitchers(); // 選び直された部品を今のビューへ戻す
            return;
        }

        _switchingView = true;
        try
        {
            _previousViewMode = current;
            ViewModel.ViewMode = mode;
            ApplyViewVisibility(mode);

            // 今までのビューを抜ける
            switch (current)
            {
                case MainViewMode.EmojiInsert:
                    ExitInsertView();
                    break;
                case MainViewMode.FontSettings:
                    ExitFontSettingsView();
                    break;
            }

            // 新しいビューに入る
            switch (mode)
            {
                case MainViewMode.EmojiInsert:
                    EnterInsertView();
                    break;
                case MainViewMode.FontSettings:
                    EnterFontSettingsView();
                    break;
                default:
                    ViewModel.StatusText = $"{MainViewModel.ViewModeName(current)}を終了し、行リストへ戻りました";
                    break;
            }
        }
        finally
        {
            _switchingView = false;
        }
    }

    /// <summary>
    /// F2・F3: そのビューを開く。すでに開いていれば戻る
    /// （絵文字挿入ビューは行リストへ、フォント設定ビューは直前のビューへ）。
    /// </summary>
    private void ToggleView(MainViewMode mode)
    {
        if (ViewModel.ViewMode != mode)
        {
            SwitchView(mode);
        }
        else if (mode == MainViewMode.FontSettings)
        {
            ReturnFromFontSettings();
        }
        else
        {
            SwitchView(MainViewMode.Lines);
        }
    }

    /// <summary>フォント設定ビューから直前のビューへ戻る。</summary>
    private void ReturnFromFontSettings()
    {
        if (ViewModel.ViewMode != MainViewMode.FontSettings) return;
        SwitchView(_previousViewMode == MainViewMode.FontSettings ? MainViewMode.Lines : _previousViewMode);
    }

    /// <summary>ビューに合わせて領域の表示を入れ替える（行リストなどはコントロールごと残る）。</summary>
    private void ApplyViewVisibility(MainViewMode mode)
    {
        bool font = mode == MainViewMode.FontSettings;
        if (font) EnsureFontView();

        // フォント設定ビューはメニューとステータスバー以外をすべて使う（メディア再生・メイン・チェック結果を隠す）
        var others = font ? Visibility.Collapsed : Visibility.Visible;
        MediaPanel.Visibility = others;
        MainArea.Visibility = others;
        IssuePanel.Visibility = others;
        FontViewHost.Visibility = font ? Visibility.Visible : Visibility.Collapsed;

        if (!font)
        {
            NormalView.Visibility = mode == MainViewMode.Lines ? Visibility.Visible : Visibility.Collapsed;
            InsertView.Visibility = mode == MainViewMode.EmojiInsert ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void EnterFontSettingsView()
    {
        string back = MainViewModel.ViewModeName(_previousViewMode);
        EnsureFontView().Enter(back);
        ViewModel.StatusText = $"フォント設定ビュー: Esc（または F3）で{back}へ戻ります";
    }

    /// <summary>フォント設定ビューを抜けたあと、行リスト側の表示（フォント設定名の候補・手動指定の印・チェック）を作り直す。</summary>
    private void ExitFontSettingsView()
    {
        _n3FontNamesKey = null;
        RefreshN3LinePanel();
        foreach (var line in ViewModel.Lines) line.RaiseOverrideMark();
        ScheduleValidation();
    }

    /// <summary>切り替えの部品（SelectorBar・表示メニュー・右パネルのトグル）を今のビューに合わせる。</summary>
    private void SyncViewSwitchers()
    {
        _syncingViewSwitchers = true;
        try
        {
            MainViewMode mode = ViewModel.ViewMode;
            ViewSelector.SelectedItem = mode switch
            {
                MainViewMode.EmojiInsert => EmojiViewTab,
                MainViewMode.FontSettings => FontViewTab,
                _ => LinesViewTab,
            };

            // ラジオ項目は選ぶ側を true にすると同じグループの他の項目が外れる（false を代入しても外れない）
            RadioMenuFlyoutItem item = mode switch
            {
                MainViewMode.EmojiInsert => EmojiViewMenuItem,
                MainViewMode.FontSettings => FontViewMenuItem,
                _ => LinesViewMenuItem,
            };
            item.IsChecked = true;

            EmojiModeToggle.IsChecked = mode == MainViewMode.EmojiInsert;
        }
        finally
        {
            _syncingViewSwitchers = false;
        }
    }

    // ------------------------------------------------------------ 切り替えの部品

    private void OnViewSelectorChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_syncingViewSwitchers) return;
        if (sender.SelectedItem is not { } item)
        {
            SyncViewSwitchers(); // 選択が外れたときは今のビューを選び直す
            return;
        }
        SwitchView(item == EmojiViewTab ? MainViewMode.EmojiInsert
            : item == FontViewTab ? MainViewMode.FontSettings
            : MainViewMode.Lines);
    }

    /// <summary>表示メニューの項目をクリックしたとき（そのビューを選ぶ）。</summary>
    private void OnViewMenuClick(object sender, RoutedEventArgs e)
    {
        SwitchView(ReferenceEquals(sender, EmojiViewMenuItem) ? MainViewMode.EmojiInsert
            : ReferenceEquals(sender, FontViewMenuItem) ? MainViewMode.FontSettings
            : MainViewMode.Lines);
    }

    /// <summary>F2・F3（表示メニューのアクセラレータ）。開いているビューのキーなら戻る。</summary>
    private void OnViewAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true; // メニュー項目のクリック（ラジオの選択）としては扱わない
        ToggleView(sender.Key == Windows.System.VirtualKey.F3 ? MainViewMode.FontSettings : MainViewMode.EmojiInsert);
    }

    /// <summary>右パネルの「絵文字挿入ビュー (F2)」トグル。</summary>
    private void OnEmojiModeChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingViewSwitchers) return;
        SwitchView(EmojiModeToggle.IsChecked == true ? MainViewMode.EmojiInsert : MainViewMode.Lines);
    }

    /// <summary>エクスポート > ニコカラメーカー3 のフォント設定を編集。</summary>
    private void OnFontSettingsViewClick(object sender, RoutedEventArgs e) => SwitchView(MainViewMode.FontSettings);

    private void OnFontViewBackRequested(object? sender, EventArgs e) => ReturnFromFontSettings();

    /// <summary>
    /// フォント設定ビューの外（メニューを使ったあとのメニューバーなど）にフォーカスがあるときの Esc。
    /// ビューの中の Esc は FontSettingsView が受けて処理済みにするので、ここへは処理されなかった Esc だけが届く。
    /// </summary>
    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled || e.Key != Windows.System.VirtualKey.Escape) return;
        if (ViewModel.ViewMode != MainViewMode.FontSettings) return;
        e.Handled = true;
        ReturnFromFontSettings();
    }

    // ------------------------------------------------------------ ビューごとに使えない操作

    /// <summary>
    /// 行エディタを対象にする操作（行分割・結合・適用・絵文字挿入）を止めるか。行リスト以外のビューでは止める
    /// （絵文字挿入ビューでは同じキーをエディタの PreviewKeyDown で処理する）。
    /// </summary>
    private bool LineEditorOperationBlocked()
    {
        if (ViewModel.ViewMode == MainViewMode.Lines) return false;
        if (ViewModel.ViewMode == MainViewMode.FontSettings) ShowLineOperationBlocked();
        return true;
    }

    /// <summary>
    /// 選択行を対象にする操作（行削除・空行挿入・タブ分離・エクスポート・済マークなど）を止めるか。
    /// 行が見えないフォント設定ビューでだけ止める（絵文字挿入ビューでは行情報欄で選んだ行に使える）。
    /// </summary>
    private bool LineOperationBlocked()
    {
        if (ViewModel.ViewMode != MainViewMode.FontSettings) return false;
        ShowLineOperationBlocked();
        return true;
    }

    private void ShowLineOperationBlocked() =>
        ViewModel.StatusText = $"フォント設定ビューでは行の操作は使えません（Esc で{MainViewModel.ViewModeName(_previousViewMode)}へ戻ります）";
}
