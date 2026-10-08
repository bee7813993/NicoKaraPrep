using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NicoKaraPrep.App.ViewModels;
using NicoKaraPrep.App.Views.Layout;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.Views;

/// <summary>
/// ニコカラメーカー3 のレイアウト設定ビュー（F4）。左 = レイアウト設定の一覧（書き出しの並び）、中央 = 選んだレイアウトの編集・使っているページ、
/// 右 = 見本（選んだページの歌詞を選んだレイアウトで並べる）・ページの一覧（全タブ）とページへのレイアウトの指定。
/// 字幕アクションはここでは扱わない（行リストの右のパネル「字幕アクション」。レイアウトとは別のもの）。
/// メインウィンドウの中で表示を入れ替えるだけなので、閉じても行リストの選択・文書・再生状態はそのまま残る。
/// レイアウトの値は 300ms ごとにまとめて保存する（アプリ共通。settings.json）。
/// </summary>
public sealed partial class LayoutView : UserControl
{
    // 初めて表示するときはまだ画面に載っておらずフォーカスを置けないため、Loaded で置く
    private bool _focusOnLoaded;

    /// <summary>編集欄に値を読み込んでいる最中か（そのあいだの欄の変化は編集として扱わない）。</summary>
    private bool _loading;

    /// <summary>ページの一覧の選択をコードから合わせている最中か（そのあいだの選択の変化は ViewModel へ返さない）。</summary>
    private bool _syncingPages;

    /// <summary>ページの一覧の選択の変化を処理している最中か（そのあいだは一覧の選択を変えない）。</summary>
    private bool _inPageSelectionChanged;

    /// <summary>キーボードのアクセラレータとメニューの 元に戻す が同じキーで続けて来たときに 2 回戻さないため。</summary>
    private DateTime _lastUndoRedoUtc = DateTime.MinValue;

    /// <summary>直前に実行した操作がやり直しだったか（<see cref="TryUndoRedo"/> の二重実行防止に使う）。</summary>
    private bool _lastUndoRedoWasRedo;

    /// <summary>編集欄を 2 列に並べているか（中央が狭いときは 1 列）。</summary>
    private bool? _editorWide;

    /// <summary>見本の枠の高さを合わせた幅（同じ幅では合わせ直さない）。</summary>
    private double _previewWidth;

    /// <summary>左の一覧の幅の既定・最小（px）。</summary>
    private const double DefaultListWidth = 300;
    private const double MinListWidth = 220;

    /// <summary>編集欄に残す最小の幅と、右の列の幅（px。一覧を広げすぎないように）。</summary>
    private const double MinEditorWidth = 360;
    private const double RightColumnWidth = 300;

    /// <summary>編集欄を 2 列に並べる中央の幅（px。これより狭いと 1 列）。</summary>
    private const double TwoColumnEditorWidth = 640;

    /// <summary>つまみのドラッグを始めたときの一覧の幅。</summary>
    private double _listWidthAtDragStart;

    public LayoutView()
    {
        InitializeComponent();

        // 一覧と編集欄のあいだのつまみ: ドラッグ中は幅だけ変え、離したら保存する
        LayoutListResizeGrip.DragStarted += (_, _) => _listWidthAtDragStart = ListColumn.Width.Value;
        LayoutListResizeGrip.Dragging += (_, dx) => SetListWidth(_listWidthAtDragStart + dx, save: false);
        LayoutListResizeGrip.DragCompleted += (_, _) => SetListWidth(ListColumn.Width.Value, save: true);
        LayoutListResizeGrip.Stepped += (_, dx) => SetListWidth(ListColumn.Width.Value + dx, save: true);
        LayoutListResizeGrip.ResetRequested += (_, _) => SetListWidth(DefaultListWidth, save: true);

        Loaded += (_, _) =>
        {
            // 画面に載る前に選んだページは、行ができてから見えるところまで送る
            if (ViewModel?.SelectedPages.FirstOrDefault() is { } page) ScrollPageIntoView(page);
            if (!_focusOnLoaded) return;
            _focusOnLoaded = false;
            FocusInitial();
        };
    }

    /// <summary>ビューの ViewModel（<see cref="Attach"/> で作る）。</summary>
    public LayoutViewModel? ViewModel { get; private set; }

    /// <summary>「戻る」ボタンか Esc で、前のビューへ戻るよう求められたときに発生する。</summary>
    public event EventHandler? BackRequested;

    /// <summary>x:Bind 用: 真偽を反転する（名前の欄を読み取り専用にするとき）。</summary>
    public static bool Not(bool value) => !value;

    /// <summary>メイン画面の ViewModel を受け取る（作ったあと 1 回だけ呼ぶ）。</summary>
    public void Attach(MainViewModel main)
    {
        ViewModel = new LayoutViewModel(main);
        // 編集欄の読み直しは、知らせのもとになった操作（一覧の選択の変更・欄のイベントなど）が終わってから行う（1 回にまとめる）
        ViewModel.EditorReloadRequested += (_, _) => QueueLoadFields();
        ViewModel.PreviewChanged += (_, _) => RefreshPreview();
        ViewModel.PagesSelectRequested += (_, pages) => ApplyPageSelection(pages);
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Bindings.Update();

        // 保存した一覧の幅（画面の広さに合わせた上限は、つまみを動かしたときにかける）
        ListColumn.Width = new GridLength(Math.Clamp(ViewModel.ListWidth, MinListWidth, 1600));

        LoadFields();
        RefreshPreview();
    }

    /// <summary>
    /// ビューに入るときに、行リストで選ばれていた行（表示中のタブの行番号）を受け取る（<see cref="Enter"/> の前に呼ぶ）。
    /// 入るときに、その行のページをページの一覧で選ぶ。
    /// </summary>
    public void SetLineContext(IReadOnlyList<int> selectedLines) => ViewModel?.SetLineContext(selectedLines);

    /// <summary>
    /// ビューに入ったときにメインウィンドウから呼ぶ。一覧を作り直し（ベースが替わっていれば反映）、名前があればそのレイアウトを選び、
    /// 戻り先のビューの名前をボタンに出し、Esc を受けられるようにビューの中へフォーカスを置く。
    /// </summary>
    public void Enter(string backTargetName, string? selectLayoutName)
    {
        LayoutBackButton.Content = $"{backTargetName}へ戻る (Esc)";
        ViewModel?.OnEnter(string.IsNullOrEmpty(selectLayoutName) ? null : selectLayoutName);

        if (IsLoaded)
        {
            FocusInitial();
        }
        else
        {
            _focusOnLoaded = true;
        }
    }

    /// <summary>ビューを抜けるときにメインウィンドウから呼ぶ。保存待ちの編集を保存する（チェックの予約はメインウィンドウがする）。</summary>
    public void Exit()
    {
        if (ViewModel is not { } vm) return;
        vm.FlushPendingSave();
        vm.OnExit();
    }

    /// <summary>保存待ちの編集をすぐに保存する（窓を閉じるときなど）。</summary>
    public void FlushPendingSave() => ViewModel?.FlushPendingSave();

    /// <summary>
    /// 外で文書・タブ・ベースが変わった（MCP のタブの切り替え・書き出し画面の「適用」・n3proj の読み込み）: 一覧とページの一覧を作り直す
    /// （選んでいるレイアウト・ページは保つ）。
    /// </summary>
    public void OnDocumentChanged() => ViewModel?.OnDocumentChanged();

    /// <summary>一覧の選択中の行にフォーカスを置く（一覧が空なら戻るボタン）。</summary>
    private void FocusInitial()
    {
        if (ViewModel?.SelectedLayout is { } item && LayoutList.ContainerFromItem(item) is ListViewItem container
            && container.Focus(FocusState.Programmatic))
        {
            return;
        }
        if (LayoutList.Items.Count > 0 && LayoutList.Focus(FocusState.Programmatic)) return;
        LayoutBackButton.Focus(FocusState.Programmatic);
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Esc で前のビューへ戻る。ComboBox のドロップダウンやフライアウトを閉じる Esc はそちらで処理済み（Handled）になり、ここへは届かない。
    /// 名前の欄で確定前の入力があれば、それは取り消してから戻る（戻ったあとの LostFocus で名前が変わらないように）。
    /// </summary>
    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled || e.Key != Windows.System.VirtualKey.Escape) return;
        e.Handled = true;
        var focused = XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot);
        if (ReferenceEquals(focused, LayoutEditNameBox) && ViewModel?.SelectedLayout is { } item && LayoutEditNameBox.Text != item.Name)
        {
            _loading = true;
            try
            {
                LayoutEditNameBox.Text = item.Name;
            }
            finally
            {
                _loading = false;
            }
        }
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------ 元に戻す・やり直し

    /// <summary>
    /// レイアウト設定ビューの中の操作を 1 つ戻す（メニューの 元に戻す と Ctrl+Z）。文字の入力欄にフォーカスがあるときは
    /// 入力欄の 元に戻す に任せて何もしない（false）。
    /// </summary>
    public bool TryUndo() => TryUndoRedo(redo: false);

    /// <summary>レイアウト設定ビューの中の操作をやり直す（メニューの やり直し と Ctrl+Y）。</summary>
    public bool TryRedo() => TryUndoRedo(redo: true);

    private bool TryUndoRedo(bool redo)
    {
        if (ViewModel is not { } vm || IsTextInputFocused()) return false;

        // 同じキーがアクセラレータとメニューの両方から続けて届いたときだけ 2 回目を捨てる
        // （元に戻す → やり直し のように種類が違うものは、続けて来ても両方実行する）
        var now = DateTime.UtcNow;
        if (redo == _lastUndoRedoWasRedo && (now - _lastUndoRedoUtc).TotalMilliseconds < 150) return true;
        _lastUndoRedoUtc = now;
        _lastUndoRedoWasRedo = redo;
        if (redo) vm.Redo();
        else vm.Undo();
        return true;
    }

    /// <summary>文字の入力欄（数値の欄の中の入力も含む）にフォーカスがあるか。</summary>
    private bool IsTextInputFocused() =>
        XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) is TextBox or RichEditBox or PasswordBox or AutoSuggestBox;

    private void OnUndoInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        args.Handled = TryUndo();

    private void OnRedoInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        args.Handled = TryRedo();

    // ------------------------------------------------------------ 一覧の幅・編集欄の並べ方・見本の高さ

    /// <summary>左の一覧の幅を変える（最小 220px。編集欄に 360px と右の列が残る幅まで）。save なら設定に保存する。</summary>
    private void SetListWidth(double width, bool save)
    {
        double available = RootGrid.ActualWidth - RootGrid.Padding.Left - RootGrid.Padding.Right
            - RootGrid.ColumnDefinitions[1].Width.Value - RootGrid.ColumnDefinitions[3].Width.Value
            - RightColumnWidth - MinEditorWidth;
        double max = Math.Max(MinListWidth, available);
        width = Math.Clamp(width, MinListWidth, max);
        ListColumn.Width = new GridLength(width);
        if (save) ViewModel?.SaveListWidth(width);
    }

    /// <summary>中央の幅に合わせて、編集欄のカードを 2 列（基本 | 行ごとの左右配置 / 文字 | ルビ）か 1 列に並べる。</summary>
    private void OnEditorScrollSizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool wide = e.NewSize.Width >= TwoColumnEditorWidth;
        if (_editorWide == wide) return;
        _editorWide = wide;
        LayoutEditorGrid.ColumnDefinitions[1].Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        LayoutEditorGrid.ColumnSpacing = wide ? 12 : 0;
        var cards = new FrameworkElement[] { LayoutBasicCard, LayoutRowsCard, LayoutTextCard, LayoutRubyCard };
        for (int i = 0; i < cards.Length; i++)
        {
            Grid.SetRow(cards[i], wide ? i / 2 : i);
            Grid.SetColumn(cards[i], wide ? i % 2 : 0);
        }
    }

    /// <summary>見本の枠の高さを幅から 16:9 にする。</summary>
    private void OnPreviewFrameSizeChanged(object sender, SizeChangedEventArgs e)
    {
        double width = e.NewSize.Width;
        if (width < 1 || Math.Abs(width - _previewWidth) < 0.5) return;
        _previewWidth = width;
        LayoutPreviewFrame.Height = Math.Round(width * 9 / 16);
    }

    // ------------------------------------------------------------ 一覧

    /// <summary>
    /// 一覧で選んだレイアウトを ViewModel へ渡す。選択が外れたとき（Ctrl+クリックなど）は、選択の処理が終わってから
    /// 編集中のものを選び直す（選択の変更の中では一覧の選択を変えない）。
    /// </summary>
    private void OnLayoutListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is not { } vm || vm.IsRebuilding) return;
        if (LayoutList.SelectedItem is LayoutListItem item)
        {
            vm.SelectedLayout = item;
            return;
        }
        DispatcherQueue.TryEnqueue(() =>
        {
            if (LayoutList.SelectedItem is null && ViewModel?.SelectedLayout is { } current && ViewModel.Layouts.Contains(current))
            {
                LayoutList.SelectedItem = current;
            }
        });
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.AddNew() is null) return;
        FocusNameBox();
    }

    private void OnDuplicateClick(object sender, RoutedEventArgs e) => ViewModel?.DuplicateSelected();

    /// <summary>削除（足したもの）・ベースの値に戻す（編集したもの）。確認してから。</summary>
    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm || vm.DescribeDelete() is not { } text) return;
        var dialog = new ContentDialog
        {
            Title = text.Title,
            Content = new TextBlock { Text = text.Message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = text.Primary,
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary) return;
        vm.DeleteSelected();
    }

    /// <summary>ニコカラメーカー3 のテンプレート（TemplateLayout\*.tpl）を読み、選んだものをアプリ共通のレイアウト設定に追加する。</summary>
    private async void OnImportTemplateClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        var folders = Nkm3Environment.FindTemplateLayoutFolders();
        if (folders.Count == 0)
        {
            vm.Main.StatusText = "ニコカラメーカー3 のレイアウト設定テンプレートのフォルダが見つかりませんでした";
            await ShowMessageAsync("テンプレートが見つかりません",
                "ニコカラメーカー3 のレイアウト設定テンプレートのフォルダ（設定フォルダの TemplateLayout）が見つかりませんでした。\n" +
                "ニコカラメーカー3 でレイアウト設定をテンプレートとして保存してから、もう一度お試しください。");
            return;
        }

        vm.Main.StatusText = "ニコカラメーカー3 のテンプレートを読み込んでいます...";
        int height = vm.ScreenHeight;
        var errors = new List<string>();
        List<N3Layout> templates;
        try
        {
            templates = await Task.Run(() =>
            {
                var list = new List<N3Layout>();
                foreach (string folder in folders) list.AddRange(N3LayoutTemplateReader.ReadTemplateFolder(folder, errors, height));
                return list;
            });
        }
        catch (Exception ex)
        {
            vm.Main.StatusText = $"エラー: ニコカラメーカー3 のテンプレートを読めませんでした（{ex.Message}）";
            return;
        }
        string errorText = errors.Count > 0 ? $"（読めなかったファイル {errors.Count} 件: {string.Join(" / ", errors.Take(3))}{(errors.Count > 3 ? " ほか" : "")}）" : "";
        vm.Main.StatusText = $"ニコカラメーカー3 のテンプレート {templates.Count} 件を読み込みました{errorText}";
        if (templates.Count == 0)
        {
            await ShowMessageAsync("テンプレートがありません", $"{string.Join("\n", folders)}\n\nにレイアウト設定テンプレート（*.tpl）がありませんでした。{errorText}");
            return;
        }

        var existing = vm.Layouts.Select(l => l.Name).Concat(vm.Main.Settings.N3Layouts.Select(l => l.Name)).Distinct().ToList();
        string folderText = $"読み込んだフォルダ（読み取りのみ。テンプレートは変更しません）: {string.Join(" ／ ", folders)}{errorText}\n" +
                            $"px は書き出すプロジェクトの画面の高さ（{height}）の値に直して取り込みます。";
        var dialog = new LayoutTemplateImportDialog(templates, existing, folderText);
        if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary) return;
        vm.AddTemplates(dialog.Selected);
    }

    // ------------------------------------------------------------ 編集欄

    /// <summary>ViewModel の状態の変化: ベースの値のままのレイアウトは編集欄を薄くする。</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LayoutViewModel.IsBaseSelected)) UpdateEditorOpacity();
    }

    private void UpdateEditorOpacity() => LayoutEditorGrid.Opacity = ViewModel?.IsBaseSelected == true ? 0.6 : 1.0;

    /// <summary>編集欄の読み直しを予約しているか。</summary>
    private bool _loadQueued;

    /// <summary>編集欄の読み直しを予約する（いま処理中のイベントが終わってから 1 回だけ読み直す）。</summary>
    private void QueueLoadFields()
    {
        if (_loadQueued) return;
        _loadQueued = true;
        if (!DispatcherQueue.TryEnqueue(() =>
            {
                _loadQueued = false;
                LoadFields();
            }))
        {
            _loadQueued = false;
        }
    }

    /// <summary>選んだレイアウトの値を編集欄に読み込む。</summary>
    private void LoadFields()
    {
        var s = ViewModel?.CurrentSettings;
        _loading = true;
        try
        {
            if (s is null)
            {
                LayoutEditNameBox.Text = "";
                LayoutAlignmentRows.Children.Clear();
                return;
            }
            LayoutEditNameBox.Text = s.Name;
            LayoutVerticalBox.SelectedIndex = Math.Clamp(s.VerticalAlignment, 0, 2);
            LayoutVerticalMarginLabel.Text = LayoutTexts.VerticalMarginLabel(s.VerticalAlignment);
            LayoutVerticalMarginBox.Value = s.VerticalMarginPx;
            LayoutHorizontalMarginBox.Value = s.HorizontalMarginPx;
            LayoutLineSpaceBox.Value = s.LineSpacePx;
            LayoutSmartHorizonBox.SelectedIndex = Math.Clamp(s.SmartHorizon, 0, 2);
            LayoutLyricsIntervalBox.Value = s.LyricsIntervalPx;
            LayoutAllowBitingCheck.IsChecked = s.AllowBiting;
            LayoutRubyIntervalBox.Value = s.RubyIntervalPx;
            LayoutRubyAlignmentBox.SelectedIndex = Math.Clamp(s.RubyAlignment, 0, 2);
            LayoutLyricsAndRubyIntervalBox.Value = s.LyricsAndRubyIntervalPx;
            BuildAlignmentRows(s.HorizontalAlignments);
        }
        finally
        {
            _loading = false;
        }
        UpdateEditorOpacity();
    }

    /// <summary>上下配置・スマート水平配置・ルビ配置を変えた。</summary>
    private void OnLayoutComboChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ViewModel is not { } vm || sender is not ComboBox box || box.SelectedIndex < 0) return;
        int value = box.SelectedIndex;
        if (ReferenceEquals(box, LayoutVerticalBox))
        {
            vm.Edit("上下配置", l => l.VerticalAlignment = value);
            LayoutVerticalMarginLabel.Text = LayoutTexts.VerticalMarginLabel(value);
        }
        else if (ReferenceEquals(box, LayoutSmartHorizonBox))
        {
            vm.Edit("スマート水平配置", l => l.SmartHorizon = value);
        }
        else if (ReferenceEquals(box, LayoutRubyAlignmentBox))
        {
            vm.Edit("ルビ配置", l => l.RubyAlignment = value);
        }
    }

    /// <summary>数値の欄を変えた（空にした欄は今の値に戻す）。</summary>
    private void OnLayoutNumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || ViewModel is not { } vm) return;
        if (!double.IsFinite(args.NewValue))
        {
            // 欄の処理が終わってから今の値に戻す
            DispatcherQueue.TryEnqueue(() => RestoreNumber(sender));
            return;
        }
        double v = args.NewValue;
        if (ReferenceEquals(sender, LayoutVerticalMarginBox)) vm.Edit("上下余白", l => l.VerticalMarginPx = v);
        else if (ReferenceEquals(sender, LayoutHorizontalMarginBox)) vm.Edit("左右余白", l => l.HorizontalMarginPx = v);
        else if (ReferenceEquals(sender, LayoutLineSpaceBox)) vm.Edit("行間", l => l.LineSpacePx = v);
        else if (ReferenceEquals(sender, LayoutLyricsIntervalBox)) vm.Edit("歌詞間隔", l => l.LyricsIntervalPx = v);
        else if (ReferenceEquals(sender, LayoutRubyIntervalBox)) vm.Edit("ルビ間隔", l => l.RubyIntervalPx = v);
        else if (ReferenceEquals(sender, LayoutLyricsAndRubyIntervalBox)) vm.Edit("歌詞とルビ", l => l.LyricsAndRubyIntervalPx = v);
    }

    /// <summary>空にした数値の欄を、選んでいるレイアウトの今の値に戻す。</summary>
    private void RestoreNumber(NumberBox box)
    {
        if (ViewModel?.CurrentSettings is not { } s) return;
        double value =
            ReferenceEquals(box, LayoutVerticalMarginBox) ? s.VerticalMarginPx
            : ReferenceEquals(box, LayoutHorizontalMarginBox) ? s.HorizontalMarginPx
            : ReferenceEquals(box, LayoutLineSpaceBox) ? s.LineSpacePx
            : ReferenceEquals(box, LayoutLyricsIntervalBox) ? s.LyricsIntervalPx
            : ReferenceEquals(box, LayoutRubyIntervalBox) ? s.RubyIntervalPx
            : ReferenceEquals(box, LayoutLyricsAndRubyIntervalBox) ? s.LyricsAndRubyIntervalPx
            : double.NaN;
        if (double.IsNaN(value)) return;
        _loading = true;
        try
        {
            box.Value = value;
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnAllowBitingClick(object sender, RoutedEventArgs e)
    {
        if (_loading || ViewModel is not { } vm) return;
        bool value = LayoutAllowBitingCheck.IsChecked == true;
        vm.Edit("食い込み", l => l.AllowBiting = value);
    }

    /// <summary>行ごとの左右配置の欄（行ごとに「n 行目 [左寄せ/中央/右寄せ] [×]」）。</summary>
    private void BuildAlignmentRows(IReadOnlyList<int> alignments)
    {
        LayoutAlignmentRows.Children.Clear();
        for (int i = 0; i < alignments.Count; i++)
        {
            int row = i;
            var grid = new Grid { ColumnSpacing = 8 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(new TextBlock { Text = $"{i + 1} 行目", VerticalAlignment = VerticalAlignment.Center });
            var box = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ItemsSource = LayoutTexts.HorizontalNames,
                SelectedIndex = Math.Clamp(alignments[i], 0, 2),
            };
            AutomationProperties.SetName(box, $"{i + 1} 行目の左右配置");
            ToolTipService.SetToolTip(box, $"上から {i + 1} 行目を、画面の左・中央・右のどこに寄せるか");
            box.SelectionChanged += (_, _) =>
            {
                if (_loading || box.SelectedIndex < 0 || ViewModel is not { } vm) return;
                int value = box.SelectedIndex;
                vm.Edit($"{row + 1} 行目の左右配置", l =>
                {
                    if (row < l.HorizontalAlignments.Count) l.HorizontalAlignments[row] = value;
                });
            };
            Grid.SetColumn(box, 1);
            grid.Children.Add(box);
            var remove = new Button
            {
                Content = new FontIcon { Glyph = "", FontSize = 12 },
                Padding = new Thickness(8, 6, 8, 6),
                IsEnabled = alignments.Count > 1,
            };
            AutomationProperties.SetName(remove, $"{i + 1} 行目を消す");
            ToolTipService.SetToolTip(remove, alignments.Count > 1
                ? $"{i + 1} 行目を消します（このレイアウトの行数が 1 つ減り、行数から自動で選ばれるページが変わります）"
                : "行が 1 つだけのときは消せません");
            remove.Click += (_, _) =>
            {
                ViewModel?.Edit("行を減らす", l =>
                {
                    if (l.HorizontalAlignments.Count > 1 && row < l.HorizontalAlignments.Count) l.HorizontalAlignments.RemoveAt(row);
                }, separate: true);
                // 押したボタンの処理が終わってから並べ直す
                DispatcherQueue.TryEnqueue(() => RebuildAlignmentRows());
            };
            Grid.SetColumn(remove, 2);
            grid.Children.Add(remove);
            LayoutAlignmentRows.Children.Add(grid);
        }
    }

    private void RebuildAlignmentRows()
    {
        if (ViewModel?.CurrentSettings is not { } s) return;
        _loading = true;
        try
        {
            BuildAlignmentRows(s.HorizontalAlignments);
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnAddAlignmentClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.Edit("行を増やす", l => l.HorizontalAlignments.Add(l.HorizontalAlignments.Count > 0 ? l.HorizontalAlignments[^1] : 1), separate: true);
        RebuildAlignmentRows();
    }

    // ------------------------------------------------------------ 名前

    private void FocusNameBox()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            LayoutEditNameBox.Focus(FocusState.Programmatic);
            LayoutEditNameBox.SelectAll();
        });
    }

    private void OnNameKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        e.Handled = true;
        CommitName();
    }

    private void OnNameLostFocus(object sender, RoutedEventArgs e) => CommitName();

    private void CommitName()
    {
        if (_loading || ViewModel is not { SelectedLayout: { } item } vm || LayoutEditNameBox.Text == item.Name) return;
        vm.RenameSelected(LayoutEditNameBox.Text);
    }

    // ------------------------------------------------------------ 使っているページ・ページの一覧

    /// <summary>「使っているページ」を押した: 右のページの一覧でそのページを選ぶ。</summary>
    private void OnUsagePageClick(object sender, ItemClickEventArgs e)
    {
        if (ViewModel is { } vm && e.ClickedItem is LayoutPageItem page) vm.SelectPages(new[] { page });
    }

    private void OnPageListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingPages || ViewModel is not { } vm || vm.IsRebuildingPages) return;
        _inPageSelectionChanged = true;
        try
        {
            vm.SetSelectedPages(LayoutPageList.SelectedItems.OfType<LayoutPageItem>().ToList());
        }
        finally
        {
            _inPageSelectionChanged = false;
        }
    }

    /// <summary>ページの一覧の選択を ViewModel の選んだページに合わせる（一覧の選択の変更の中なら、終わってから）。</summary>
    private void ApplyPageSelection(IReadOnlyList<LayoutPageItem> pages)
    {
        if (_inPageSelectionChanged)
        {
            DispatcherQueue.TryEnqueue(() => ApplyPageSelection(pages));
            return;
        }
        _syncingPages = true;
        try
        {
            LayoutPageList.SelectedItems.Clear();
            foreach (var page in pages)
            {
                if (ViewModel?.Pages.Contains(page) == true) LayoutPageList.SelectedItems.Add(page);
            }
        }
        catch (Exception)
        {
            // 一覧の作り直しの途中で行が外れていたら選ばない
        }
        finally
        {
            _syncingPages = false;
        }
        if (pages.FirstOrDefault() is { } first) ScrollPageIntoView(first);
    }

    /// <summary>ページの行まで一覧の表示を送る（行ができてから）。</summary>
    private void ScrollPageIntoView(LayoutPageItem page)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (ViewModel?.Pages.Contains(page) == true) LayoutPageList.ScrollIntoView(page);
            }
            catch (Exception)
            {
                // 作り直しの途中で行が一覧から外れていたら何もしない
            }
        });
    }

    private void OnApplyPageLayoutClick(object sender, RoutedEventArgs e) => ViewModel?.ApplyLayoutToSelectedPages();

    private void OnAutoPageLayoutClick(object sender, RoutedEventArgs e) => ViewModel?.ResetLayoutOfSelectedPages();

    // ------------------------------------------------------------ 見本

    /// <summary>見本を描き直す（選んだページの歌詞を、選んだレイアウトの編集中の値で並べる）。</summary>
    private void RefreshPreview()
    {
        if (ViewModel is not { } vm) return;
        var (model, note) = vm.BuildPreview();
        LayoutPreview.Model = model;
        LayoutPreview.TimeMs = 1;
        LayoutPreviewNoteText.Text = note;
    }

    // ------------------------------------------------------------ ダイアログ

    /// <summary>ダイアログを出す（ほかのダイアログが開いていて出せなければ None）。</summary>
    private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        dialog.XamlRoot = XamlRoot;
        try
        {
            return await dialog.ShowAsync();
        }
        catch (Exception)
        {
            return ContentDialogResult.None;
        }
    }

    private Task ShowMessageAsync(string title, string message) => ShowDialogAsync(new ContentDialog
    {
        Title = title,
        Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
        CloseButtonText = "OK",
        DefaultButton = ContentDialogButton.Close,
    });
}
