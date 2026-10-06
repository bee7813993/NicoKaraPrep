using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NicoKaraPrep.App.ViewModels;
using NicoKaraPrep.App.Views;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App;

/// <summary>
/// メイン画面のビュー（行リスト／絵文字挿入／フォント設定／レイアウト設定）の切り替え。
/// どのビューも同じウィンドウの中で表示（Visibility）を入れ替えるだけなので、
/// 行リストの選択・文書・再生状態は切り替えてもそのまま残る。
/// フォント設定ビューとレイアウト設定ビューは、メニューとステータスバー以外の全体を使う「全画面ビュー」
/// （<see cref="MainViewModel.IsFullScreenView"/>）で、戻る先は最後にいた行リストか絵文字挿入ビュー。
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// 最後にいた行リストか絵文字挿入ビュー（全画面ビューから戻る先）。行リスト・絵文字挿入ビューを抜けるときだけ覚え直すので、
    /// 全画面ビューどうしを行き来しても（F3 → F4 → Esc）、全画面ビューに入る前のビューへ戻る。
    /// </summary>
    private MainViewMode _previousViewMode = MainViewMode.Lines;

    /// <summary>行リストを抜けたときにフォーカスがあった要素（行リストへ戻ったときにそこへ戻す）。</summary>
    private UIElement? _linesViewFocus;

    private bool _switchingView;

    /// <summary>切り替えの部品（SelectorBar・表示メニュー・右パネルのトグル）を今のビューに合わせている最中か。</summary>
    private bool _syncingViewSwitchers;

    private bool InsertViewActive => ViewModel.ViewMode == MainViewMode.EmojiInsert;

    /// <summary>全画面ビュー（フォント設定ビュー・レイアウト設定ビュー）を表示しているか。</summary>
    private bool FullScreenViewActive => MainViewModel.IsFullScreenView(ViewModel.ViewMode);

    /// <summary>
    /// 絵文字挿入ビューの状態（カーソルのあった行・その行の中の表示文字位置・再生追従カーソル）。
    /// LineIndex が -1 のときはカーソル位置を持たない（文書が空だったとき）。
    /// </summary>
    private sealed record InsertViewState(LyricsDocument Document, int LineIndex, int CharOffset, bool Follow);

    /// <summary>
    /// 絵文字挿入ビューから全画面ビューへ移ったときに覚えた挿入ビューの状態。全画面ビューどうしを行き来する間は持ち続け、
    /// 全画面ビューから絵文字挿入ビューへ戻ったら元に戻し、行リストへ戻ったら捨てる。
    /// </summary>
    private InsertViewState? _suspendedInsertView;

    /// <summary>フォント設定ビュー（初めて開くときに作る。以後は閉じても残し、表示だけを切り替える）。</summary>
    private FontSettingsView? _fontView;

    /// <summary>次にフォント設定ビューに入ったときに選ぶフォント設定の名前（行設定の「編集...」）。</summary>
    private string? _fontViewSelectName;

    /// <summary>レイアウト設定ビュー（初めて開くときに作る。以後は閉じても残し、表示だけを切り替える）。</summary>
    private LayoutView? _layoutView;

    /// <summary>次にレイアウト設定ビューに入ったときに選ぶレイアウト設定の名前（右パネルの「レイアウト設定を編集...」）。</summary>
    private string? _layoutViewSelectName;

    /// <summary>フォント設定ビューを返す。まだ無ければ作って FontViewHost に置く。</summary>
    private FontSettingsView EnsureFontView()
    {
        if (_fontView is null)
        {
            _fontView = new FontSettingsView();
            _fontView.BackRequested += OnFullScreenViewBackRequested;
            // n3proj からのフォント設定の取り込みは、メニューと同じ読み込み確認画面（フォント設定を選んだ状態）で行う
            _fontView.Attach(ViewModel, () => ImportN3ProjAsync(null, N3ProjImportFocus.FontSets));
            FontViewHost.Child = _fontView;
        }
        return _fontView;
    }

    /// <summary>レイアウト設定ビューを返す。まだ無ければ作って LayoutViewHost に置く。</summary>
    private LayoutView EnsureLayoutView()
    {
        if (_layoutView is null)
        {
            _layoutView = new LayoutView();
            _layoutView.BackRequested += OnFullScreenViewBackRequested;
            _layoutView.Attach(ViewModel);
            LayoutViewHost.Child = _layoutView;
        }
        return _layoutView;
    }

    /// <summary>起動時に 1 回呼ぶ。切り替えの部品を今のビューに合わせ、以後は ViewMode の変更に追従させる。</summary>
    private void InitializeViewSwitching()
    {
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ViewMode)) SyncViewSwitchers();
        };
        SyncViewSwitchers();

        // 閉じるときに、フォント設定ビュー・レイアウト設定ビューで保存を待っている編集を保存する（300ms ごとにまとめて保存しているため）
        Closed += (_, _) =>
        {
            _fontView?.FlushPendingSave();
            _layoutView?.FlushPendingSave();
        };
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

        bool fromFullScreen = MainViewModel.IsFullScreenView(current);
        bool toFullScreen = MainViewModel.IsFullScreenView(mode);
        _switchingView = true;
        try
        {
            // 行リストを隠す前に、フォーカスのある場所を覚えておく（隠すとフォーカスはメニューなどへ移ってしまう）
            if (current == MainViewMode.Lines)
            {
                _linesViewFocus = FocusManager.GetFocusedElement(Content.XamlRoot) as UIElement;
            }

            // 絵文字挿入ビューから全画面ビューへ移るときは、戻ったときに続きから使えるよう
            // 挿入ビューの状態を覚えておく（ExitInsertView が再生追従カーソルを OFF にするので、その前に）
            if (current == MainViewMode.EmojiInsert && toFullScreen)
            {
                _suspendedInsertView = CaptureInsertViewState();
            }

            // 戻る先は、全画面ビューに入る前にいた行リストか絵文字挿入ビュー（全画面ビューどうしの行き来では変えない）
            if (!fromFullScreen) _previousViewMode = current;
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
                case MainViewMode.Layout:
                    ExitLayoutView();
                    break;
            }

            // 全画面ビューから行リスト・絵文字挿入ビューへ戻るときに、覚えておいた挿入ビューの状態を取り出す（行リストへ戻るなら捨てる）
            InsertViewState? resumeInsertView = null;
            if (fromFullScreen && !toFullScreen)
            {
                resumeInsertView = _suspendedInsertView;
                _suspendedInsertView = null;
            }

            // 新しいビューに入る
            switch (mode)
            {
                case MainViewMode.EmojiInsert:
                    EnterInsertView();
                    if (resumeInsertView is not null) RestoreInsertViewState(resumeInsertView);
                    break;
                case MainViewMode.FontSettings:
                    EnterFontSettingsView();
                    break;
                case MainViewMode.Layout:
                    EnterLayoutView();
                    break;
                default:
                    RestoreLinesViewFocus();
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
    /// F2・F3・F4: そのビューを開く。すでに開いていれば戻る
    /// （絵文字挿入ビューは行リストへ、全画面ビューは最後にいた行リストか絵文字挿入ビューへ）。
    /// </summary>
    private void ToggleView(MainViewMode mode)
    {
        if (ViewModel.ViewMode != mode)
        {
            SwitchView(mode);
        }
        else if (MainViewModel.IsFullScreenView(mode))
        {
            ReturnFromFullScreenView();
        }
        else
        {
            SwitchView(MainViewMode.Lines);
        }
    }

    /// <summary>全画面ビューから戻る先（最後にいた行リストか絵文字挿入ビュー）。</summary>
    private MainViewMode FullScreenReturnTarget =>
        MainViewModel.IsFullScreenView(_previousViewMode) ? MainViewMode.Lines : _previousViewMode;

    /// <summary>全画面ビュー（フォント設定ビュー・レイアウト設定ビュー）から、最後にいた行リストか絵文字挿入ビューへ戻る。</summary>
    private void ReturnFromFullScreenView()
    {
        if (!FullScreenViewActive) return;
        SwitchView(FullScreenReturnTarget);
    }

    /// <summary>
    /// 今のビューを閉じて戻る（表示メニューの「…へ戻る (Esc)」）。Esc と同じく、
    /// 絵文字挿入ビューは行リストへ、全画面ビューは最後にいた行リストか絵文字挿入ビューへ戻る。
    /// </summary>
    private void ReturnFromCurrentView()
    {
        if (ViewModel.ViewMode == MainViewMode.EmojiInsert)
        {
            SwitchView(MainViewMode.Lines);
        }
        else if (FullScreenViewActive)
        {
            ReturnFromFullScreenView();
        }
    }

    /// <summary>
    /// 行リストへ戻ったとき、行リストを抜ける前にフォーカスがあった場所へフォーカスを戻す
    /// （F2・F3・F4・Esc で行き来したあとも、↓ や Enter がそのまま行の操作になるように）。
    /// </summary>
    private void RestoreLinesViewFocus()
    {
        UIElement? saved = _linesViewFocus;
        _linesViewFocus = null;

        // 行リストの外（行エディタ・行設定の欄・チェック結果など）にあったなら、そこへ戻す
        if (saved is FrameworkElement { IsLoaded: true } element && IsInLinesView(element) && !IsWithin(element, LineList)
            && element.Focus(FocusState.Programmatic))
        {
            return;
        }

        // 行リストの中にあったとき・記録が無いとき・戻せなかったときは選択行に置く。行の入れ物は使い回されるうえ、
        // 絵文字挿入ビューから戻ると選択はカーソルのあった行に変わるので、記録した入れ物ではなく今の選択行の入れ物を使う
        if (ViewModel.SelectedLine is { } line && LineList.ContainerFromItem(line) is ListViewItem item
            && item.Focus(FocusState.Programmatic))
        {
            return;
        }
        LineList.Focus(FocusState.Programmatic);
    }

    /// <summary>行リストのビューで見えている領域（メディア再生・メイン（絵文字挿入ビューを除く）・チェック結果）の中か。</summary>
    private bool IsInLinesView(DependencyObject element) =>
        (IsWithin(element, MediaPanel) || IsWithin(element, MainArea) || IsWithin(element, IssuePanel))
        && !IsWithin(element, InsertView);

    /// <summary>element が ancestor 自身か、その中（ビジュアルツリーの子孫）にあるか。</summary>
    private static bool IsWithin(DependencyObject? element, DependencyObject ancestor)
    {
        for (DependencyObject? e = element; e is not null; e = VisualTreeHelper.GetParent(e))
        {
            if (ReferenceEquals(e, ancestor)) return true;
        }
        return false;
    }

    /// <summary>ビューに合わせて領域の表示を入れ替える（行リストなどはコントロールごと残る）。</summary>
    private void ApplyViewVisibility(MainViewMode mode)
    {
        bool font = mode == MainViewMode.FontSettings;
        bool layout = mode == MainViewMode.Layout;
        bool fullScreen = MainViewModel.IsFullScreenView(mode);
        if (font) EnsureFontView();
        if (layout) EnsureLayoutView();

        // 全画面ビューはメニューとステータスバー以外をすべて使う（メディア再生・メイン・チェック結果を隠す）
        var others = fullScreen ? Visibility.Collapsed : Visibility.Visible;
        MediaPanel.Visibility = others;
        MainArea.Visibility = others;
        IssuePanel.Visibility = others;
        FontViewHost.Visibility = font ? Visibility.Visible : Visibility.Collapsed;
        LayoutViewHost.Visibility = layout ? Visibility.Visible : Visibility.Collapsed;

        if (!fullScreen)
        {
            NormalView.Visibility = mode == MainViewMode.Lines ? Visibility.Visible : Visibility.Collapsed;
            InsertView.Visibility = mode == MainViewMode.EmojiInsert ? Visibility.Visible : Visibility.Collapsed;
            // 右パネル: 行リストではフォント一覧・ページのレイアウト、絵文字挿入ビューでは絵文字のパレット（表示メニューで隠せる）
            ApplySidePanelVisibility(mode);
            // 行リストと絵文字挿入ビューでは、メディア再生と分け合う欄の下の欄が違うので、高さを合わせ直す
            FitPlayerHeightAfterLayout();
        }
    }

    private void EnterFontSettingsView()
    {
        string back = MainViewModel.ViewModeName(FullScreenReturnTarget);
        var view = EnsureFontView();

        // 選ぶフォント設定: 行設定の「編集...」で選んだ名前 → 選択行のフォント指定（無ければ前回の選択のまま）
        string? name = _fontViewSelectName ?? ViewModel.SelectedLine?.Model.FontSetName;
        _fontViewSelectName = null;
        view.SetLineContext(SelectedIndexes, name);
        view.Enter(back);
        ViewModel.StatusText = $"フォント設定ビュー: Esc（または F3）で{back}へ戻ります";
    }

    /// <summary>フォント設定ビューを抜けたあと、行リスト側の表示（フォント設定名の候補・手動指定の印・チェック）を作り直す。</summary>
    private void ExitFontSettingsView()
    {
        // 保存待ちのフォント設定の編集を、行リスト側の表示を作り直す前に保存する
        _fontView?.FlushPendingSave();
        _n3FontNamesKey = null;
        RefreshN3LinePanel();
        foreach (var line in ViewModel.Lines) line.RaiseOverrideMark();
        ScheduleValidation();
    }

    private void EnterLayoutView()
    {
        string back = MainViewModel.ViewModeName(FullScreenReturnTarget);
        var view = EnsureLayoutView();

        // 選ぶレイアウト設定: 右パネルの「レイアウト設定を編集...」で選んだ名前 → 選択行のページのレイアウトの手動指定（無ければビューに任せる）
        string? name = _layoutViewSelectName ?? (ViewModel.SelectedLine?.Model.LayoutName is { Length: > 0 } manual ? manual : null);
        _layoutViewSelectName = null;
        // 見本の材料（字幕のプレビューの行・当たるフォント）を今の歌詞・フォント設定で作り直す（全画面ビューではチェックのタイマーが
        // 作り直さないので、フォント設定ビューから直接移ったときや、編集の直後に入ったときに古いままにならないように）
        TryRun(ViewModel.UpdateLineFonts);
        view.SetLineContext(SelectedIndexes);
        view.Enter(back, name);
        ViewModel.StatusText = $"レイアウト設定ビュー: Esc（または F4）で{back}へ戻ります";
    }

    /// <summary>
    /// レイアウト設定ビューを抜けたあと、行リスト側の表示（行設定のレイアウトの候補・右パネル・手動指定の印・チェック）を作り直す
    /// （レイアウト設定の値・名前、ページの指定・字幕アクションが変わっていることがある）。
    /// </summary>
    private void ExitLayoutView()
    {
        // 保存待ちのレイアウト設定の編集を、行リスト側の表示を作り直す前に保存する
        _layoutView?.Exit();
        _n3LayoutNamesKey = null;
        RefreshN3LinePanel();
        LineSide.RefreshPagePane();
        foreach (var line in ViewModel.Lines) line.RaiseOverrideMark();
        ScheduleValidation();
    }

    /// <summary>
    /// 外で文書・タブ・ベースが変わったとき（MCP のタブの切り替え・書き出し画面の「適用」・n3proj の読み込み・ファイルを開くなど）、
    /// レイアウト設定ビューを開いていれば一覧を作り直す。全画面ビューではチェック（行の表示の作り直しを含む）を止めているので、
    /// 先に行の表示（ページのレイアウト・字幕のプレビューの材料）を今の文書で作り直してから知らせる。
    /// </summary>
    private void NotifyLayoutViewDocumentChanged()
    {
        if (ViewModel.ViewMode != MainViewMode.Layout || _layoutView is null) return;
        TryRun(ViewModel.UpdateLineFonts);
        TryRun(_layoutView.OnDocumentChanged);
    }

    /// <summary>今の絵文字挿入ビューの状態（カーソル位置と再生追従カーソル）を読み取る。</summary>
    private InsertViewState CaptureInsertViewState() =>
        ViewModel.MapInsertViewOffset(InsertEditor.SelectionStart) is var (lineIndex, charOffset)
            ? new InsertViewState(ViewModel.Document, lineIndex, charOffset, _insertFollow)
            : new InsertViewState(ViewModel.Document, -1, 0, _insertFollow);

    /// <summary>
    /// 覚えておいた絵文字挿入ビューの状態を戻す（EnterInsertView のあとに呼ぶ）。
    /// 文書が同じで行も残っていれば、カーソルをその行の元の位置へ戻す（行が短くなって位置が行に収まらなければ行頭）。
    /// 別の文書に替わっていたとき（ファイルを開いた・タブを切り替えたなど）は、EnterInsertView が置いた選択行の先頭のままにする。
    /// </summary>
    private void RestoreInsertViewState(InsertViewState state)
    {
        var lines = ViewModel.Document.Lines;
        if (ReferenceEquals(state.Document, ViewModel.Document) && state.LineIndex >= 0 && state.LineIndex < lines.Count)
        {
            int lineLength = lines[state.LineIndex].GetDisplayText().Length;
            int column = state.CharOffset <= lineLength ? state.CharOffset : 0;
            int caret = ViewModel.GetInsertViewLineStart(state.LineIndex) + column;
            InsertEditor.SelectionStart = Math.Min(caret, InsertEditor.Text.Length);
        }

        _insertFollow = state.Follow;
        UpdateFollowIndicator();
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
                MainViewMode.Layout => LayoutViewTab,
                _ => LinesViewTab,
            };

            // ラジオ項目は選ぶ側を true にすると同じグループの他の項目が外れる（false を代入しても外れない）
            RadioMenuFlyoutItem item = mode switch
            {
                MainViewMode.EmojiInsert => EmojiViewMenuItem,
                MainViewMode.FontSettings => FontViewMenuItem,
                MainViewMode.Layout => LayoutViewMenuItem,
                _ => LinesViewMenuItem,
            };
            item.IsChecked = true;

            // Esc の行き先（全画面ビューは行リストとは限らない）を項目名で示す
            BackViewMenuItem.Text = mode switch
            {
                MainViewMode.EmojiInsert => "行リストへ戻る",
                _ when MainViewModel.IsFullScreenView(mode) => $"{MainViewModel.ViewModeName(FullScreenReturnTarget)}へ戻る",
                _ => "前のビューへ戻る",
            };
            BackViewMenuItem.IsEnabled = mode != MainViewMode.Lines;

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
            : item == LayoutViewTab ? MainViewMode.Layout
            : MainViewMode.Lines);
    }

    /// <summary>表示メニューの項目をクリックしたとき（そのビューを選ぶ）。</summary>
    private void OnViewMenuClick(object sender, RoutedEventArgs e)
    {
        SwitchView(ReferenceEquals(sender, EmojiViewMenuItem) ? MainViewMode.EmojiInsert
            : ReferenceEquals(sender, FontViewMenuItem) ? MainViewMode.FontSettings
            : ReferenceEquals(sender, LayoutViewMenuItem) ? MainViewMode.Layout
            : MainViewMode.Lines);
    }

    /// <summary>表示メニューの「…へ戻る (Esc)」。</summary>
    private void OnBackViewMenuClick(object sender, RoutedEventArgs e) => ReturnFromCurrentView();

    /// <summary>F2・F3・F4（表示メニューのアクセラレータ）。開いているビューのキーなら戻る。</summary>
    private void OnViewAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true; // メニュー項目のクリック（ラジオの選択）としては扱わない
        switch (sender.Key)
        {
            case Windows.System.VirtualKey.F2:
                ToggleView(MainViewMode.EmojiInsert);
                break;
            case Windows.System.VirtualKey.F3:
                ToggleView(MainViewMode.FontSettings);
                break;
            case Windows.System.VirtualKey.F4:
                ToggleLayoutViewByKey();
                break;
        }
    }

    /// <summary>F4（レイアウト設定ビューの切り替え）を最後に処理した時刻（メニューのアクセラレータと根の PreviewKeyDown の二重発火を防ぐ）。</summary>
    private DateTime _lastLayoutViewKey = DateTime.MinValue;

    /// <summary>F4 でレイアウト設定ビューを切り替える（150ms 以内の 2 回目は同じキーの二重発火として捨てる）。</summary>
    private void ToggleLayoutViewByKey()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastLayoutViewKey).TotalMilliseconds < 150) return;
        _lastLayoutViewKey = now;
        ToggleView(MainViewMode.Layout);
    }

    /// <summary>
    /// 根の PreviewKeyDown（フォーカスのある部品より先に受ける）: 修飾キーなしの F4 でレイアウト設定ビューを切り替える。
    /// ComboBox は F4 でドロップダウンを開くので、右パネル・行設定・レイアウト設定ビューのコンボを選んだ直後（フォーカスがコンボに残る）に
    /// F4 を押すと、メニューのアクセラレータに届かずにドロップダウンが開いていた。開いているドロップダウン・ダイアログ・メニューの中のキーは
    /// 別のポップアップなのでここを通らない（ドロップダウンを閉じる F4 はそのまま効く）。
    /// </summary>
    private void OnRootPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled || e.Key != Windows.System.VirtualKey.F4) return;
        static bool Down(Windows.System.VirtualKey key) =>
            (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        if (Down(Windows.System.VirtualKey.Menu) || Down(Windows.System.VirtualKey.Control) || Down(Windows.System.VirtualKey.Shift)) return; // Alt+F4 などはそのまま
        e.Handled = true;
        ToggleLayoutViewByKey();
    }

    /// <summary>右パネルの「絵文字挿入ビュー (F2)」トグル。</summary>
    private void OnEmojiModeChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingViewSwitchers) return;
        SwitchView(EmojiModeToggle.IsChecked == true ? MainViewMode.EmojiInsert : MainViewMode.Lines);
    }

    /// <summary>エクスポート > ニコカラメーカー3 のフォント設定を編集。</summary>
    private void OnFontSettingsViewClick(object sender, RoutedEventArgs e) => SwitchView(MainViewMode.FontSettings);

    /// <summary>エクスポート > ニコカラメーカー3 のレイアウトと字幕アクションを編集。</summary>
    private void OnLayoutViewClick(object sender, RoutedEventArgs e) => SwitchView(MainViewMode.Layout);

    /// <summary>行設定のフォントの「編集...」: フォント設定ビューを開き、欄のフォント設定を選ぶ。</summary>
    private void OnEditLineFontClick(object sender, RoutedEventArgs e)
    {
        _fontViewSelectName = LineFontBox.Text is { Length: > 0 } text ? text : null;
        SwitchView(MainViewMode.FontSettings);
    }

    /// <summary>右パネルの「レイアウト設定を編集...」: レイアウト設定ビューを開き、選んだ行のページのレイアウトを選ぶ。</summary>
    private void OpenLayoutViewFor(string? layoutName)
    {
        _layoutViewSelectName = string.IsNullOrEmpty(layoutName) ? null : layoutName;
        SwitchView(MainViewMode.Layout);
    }

    /// <summary>
    /// 全画面ビュー（フォント設定ビュー・レイアウト設定ビュー）では、元に戻す・やり直し（メニューと Ctrl+Z / Ctrl+Y）をビューの中の操作に使う。
    /// 全画面ビューなら true（処理済み。文字の入力欄にフォーカスがあるときは何もしない。歌詞の 元に戻す はしない）。
    /// </summary>
    private bool FullScreenViewUndoRedo(bool redo)
    {
        switch (ViewModel.ViewMode)
        {
            case MainViewMode.FontSettings when _fontView is not null:
                if (redo) _fontView.TryRedo();
                else _fontView.TryUndo();
                return true;
            case MainViewMode.Layout when _layoutView is not null:
                if (redo) _layoutView.TryRedo();
                else _layoutView.TryUndo();
                return true;
            default:
                return false;
        }
    }

    private void OnFullScreenViewBackRequested(object? sender, EventArgs e) => ReturnFromFullScreenView();

    /// <summary>
    /// 全画面ビューの外（メニューを使ったあとのメニューバーなど）にフォーカスがあるときの Esc。
    /// ビューの中の Esc はビュー（FontSettingsView・LayoutView）が受けて処理済みにするので、ここへは処理されなかった Esc だけが届く。
    /// </summary>
    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled || e.Key != Windows.System.VirtualKey.Escape) return;
        if (ViewModel.ViewMode == MainViewMode.Lines && ViewModel.CharSelectionLine is not null)
        {
            // 行リストで選んでいた歌詞の文字の選択を外す（行の選択はそのまま）
            e.Handled = true;
            ViewModel.ClearCharSelection();
            RefreshLineFontForSelection();
            return;
        }
        if (!FullScreenViewActive) return;
        e.Handled = true;
        ReturnFromFullScreenView();
    }

    // ------------------------------------------------------------ ビューごとに使えない操作

    /// <summary>
    /// 行エディタを対象にする操作（行分割・結合・適用・絵文字挿入）を止めるか。行リスト以外のビューでは止める
    /// （絵文字挿入ビューでは同じキーをエディタの PreviewKeyDown で処理する）。
    /// </summary>
    private bool LineEditorOperationBlocked()
    {
        if (ViewModel.ViewMode == MainViewMode.Lines) return false;
        if (FullScreenViewActive) ShowLineOperationBlocked();
        return true;
    }

    /// <summary>
    /// 選択行を対象にする操作（行削除・空行挿入・タブ分離・エクスポート・済マークなど）を止めるか。
    /// 行が見えない全画面ビューでだけ止める（絵文字挿入ビューでは行情報欄で選んだ行に使える）。
    /// </summary>
    private bool LineOperationBlocked()
    {
        if (!FullScreenViewActive) return false;
        ShowLineOperationBlocked();
        return true;
    }

    private void ShowLineOperationBlocked() =>
        ViewModel.StatusText = $"{MainViewModel.ViewModeName(ViewModel.ViewMode)}では行の操作は使えません（Esc で{MainViewModel.ViewModeName(FullScreenReturnTarget)}へ戻ります）";

    /// <summary>
    /// 歌詞の 元に戻す・やり直し を止めるか。行が見えない全画面ビューでは止め、
    /// 押しても何も起きないように見えないよう、ステータスバーで知らせる。
    /// </summary>
    private bool LyricsUndoRedoBlocked()
    {
        if (!FullScreenViewActive) return false;
        ViewModel.StatusText =
            $"{MainViewModel.ViewModeName(ViewModel.ViewMode)}では歌詞の元に戻す・やり直しは使えません（Esc で{MainViewModel.ViewModeName(FullScreenReturnTarget)}へ戻ります）";
        return true;
    }

    /// <summary>
    /// 今すぐチェック（F5）を止めるか。行もチェック結果も見えない全画面ビューでは止め、
    /// ステータスバーの案内をチェック結果で消したり、隠れているチェック結果の開閉を変えたりしない
    /// （戻るときに ExitFontSettingsView・ExitLayoutView がチェックを予約し直す）。
    /// </summary>
    private bool ValidationBlocked()
    {
        if (!FullScreenViewActive) return false;
        ViewModel.StatusText =
            $"{MainViewModel.ViewModeName(ViewModel.ViewMode)}ではチェックは使えません（Esc で{MainViewModel.ViewModeName(FullScreenReturnTarget)}へ戻ると、チェックし直します）";
        return true;
    }
}
