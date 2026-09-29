using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NicoKaraPrep.App.ViewModels;
using NicoKaraPrep.App.Views.FontSettings;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.Views;

/// <summary>
/// ニコカラメーカー3 のフォント設定ビュー（F3）。左 = 一覧（フォルダと親子の階層のツリー。検索・絞り込みの間は平らな一覧）、
/// 中央 = 選択中のフォント設定の編集（フォルダを選んだときはフォルダの欄）、右 = プレビュー・使用状況・検証。
/// メインウィンドウの中で表示を入れ替えるだけなので、閉じても行リストの選択・文書・再生状態はそのまま残る。
/// 編集はすぐに保存する（300ms ごとにまとめる。アプリ共通は settings.json、曲専用は .tttproj）。
/// </summary>
public sealed partial class FontSettingsView : UserControl
{
    // 初めて表示するときはまだ画面に載っておらずフォーカスを置けないため、Loaded で置く
    private bool _focusOnLoaded;

    /// <summary>n3proj の読み込み確認画面を開く処理（メイン画面から受け取る）。</summary>
    private Func<Task>? _importN3Proj;

    /// <summary>ビューに入るときに選ぶフォント設定の名前（行リストの選択行の指定、または「編集...」で選んだ名前）。</summary>
    private string? _pendingSelectName;

    /// <summary>キーボードのアクセラレータとメニューの 元に戻す が同じキーで続けて来たときに 2 回戻さないため。</summary>
    private DateTime _lastUndoRedoUtc = DateTime.MinValue;

    /// <summary>右ペインのプレビュー（<see cref="PreviewHost"/> に置く。<see cref="Attach"/> で作る）。</summary>
    private FontPreviewControl? _preview;

    /// <summary>ツリーの節（節の識別子 → 節）。一覧を作り直すたびに作り直す。</summary>
    private readonly Dictionary<string, TreeViewNode> _treeNodes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>ツリーの節・選択・開閉をコードから変えている最中か（そのあいだのツリーのイベントは ViewModel へ返さない）。</summary>
    private bool _syncingTree;

    /// <summary>ドラッグしている節の識別子。</summary>
    private string? _draggedKey;

    /// <summary>ツリーで選ばれた節を ViewModel へ渡している最中か（その選択の変化はツリーへ写し返さない）。</summary>
    private bool _selectingFromTree;

    /// <summary>ツリーの中の一覧（選んだ節まで表示を送るのに使う）。</summary>
    private ListView? _treeList;

    /// <summary>左の一覧の幅の既定・最小（px）。</summary>
    private const double DefaultListWidth = 300;
    private const double MinListWidth = 220;

    /// <summary>編集欄に残す最小の幅と、右の列の幅（px。一覧を広げすぎないように）。</summary>
    private const double MinEditorWidth = 360;
    private const double RightColumnWidth = 300;

    /// <summary>つまみのドラッグを始めたときの一覧の幅。</summary>
    private double _listWidthAtDragStart;

    public FontSettingsView()
    {
        InitializeComponent();

        // 一覧と編集欄のあいだのつまみ: ドラッグ中は幅だけ変え、離したら保存する
        ListResizeGrip.DragStarted += (_, _) => _listWidthAtDragStart = ListColumn.Width.Value;
        ListResizeGrip.Dragging += (_, dx) => SetListWidth(_listWidthAtDragStart + dx, save: false);
        ListResizeGrip.DragCompleted += (_, _) => SetListWidth(ListColumn.Width.Value, save: true);
        ListResizeGrip.Stepped += (_, dx) => SetListWidth(ListColumn.Width.Value + dx, save: true);
        ListResizeGrip.ResetRequested += (_, _) => SetListWidth(DefaultListWidth, save: true);

        Loaded += (_, _) =>
        {
            // 画面に載る前に選んだ節は、ツリーの行ができてから選び直して見せる
            SyncTreeSelection(bringIntoView: true);
            if (!_focusOnLoaded) return;
            _focusOnLoaded = false;
            FocusInitial();
        };
    }

    /// <summary>ビューの ViewModel（<see cref="Attach"/> で作る）。</summary>
    public FontSettingsViewModel? ViewModel { get; private set; }

    /// <summary>「戻る」ボタンか Esc で、前のビューへ戻るよう求められたときに発生する。</summary>
    public event EventHandler? BackRequested;

    /// <summary>
    /// プレビューに出すフォント設定が変わったとき（選択が変わった・選択中のフォント設定を編集した・サンプル文字を変えた）。
    /// 引数は表示すべきフォント設定（未選択なら null）。PreviewHost に置いたプレビューはこれで描き直す。
    /// </summary>
    public event EventHandler<N3FontSet?>? PreviewTargetChanged;

    /// <summary>プレビューのサンプル文字（右ペインの「サンプル」の値。既定「永」）。</summary>
    public string PreviewSampleText
    {
        get => SampleBox.Text;
        set => SampleBox.Text = value ?? "";
    }

    /// <summary>プレビューのルビ（右ペインの「ルビ」の値。既定「えい」。空でもよい）。</summary>
    public string PreviewRubyText
    {
        get => RubyBox.Text;
        set => RubyBox.Text = value ?? "";
    }

    /// <summary>
    /// メイン画面の ViewModel と、n3proj の読み込み確認画面を開く処理を受け取る（作ったあと 1 回だけ呼ぶ）。
    /// </summary>
    public void Attach(MainViewModel main, Func<Task> importN3Proj)
    {
        _importN3Proj = importN3Proj;
        ViewModel = new FontSettingsViewModel(main);
        ViewModel.PreviewTargetChanged += (_, font) => PreviewTargetChanged?.Invoke(this, font);
        BrushEditorPart.SetViewModel(ViewModel.Editor.BrushEditor);
        FaceTablePart.SetViewModel(ViewModel.Editor);
        Bindings.Update();

        // プレビュー部品を差し込み口に置き、表示するフォント設定が変わるたび（選択・編集・元に戻す・サンプル文字）に描き直す
        // 枠の高さは 220px なので、画面の高さ 1080 基準のままでは文字が小さすぎる。1/4（270）を基準にして見やすくする
        // フォントを選ぶ画面をすぐ開けるよう、システムのフォントの一覧を裏で読み込んでおく
        _ = NicoKaraPrep.App.Services.SystemFontCatalog.LoadAsync();

        // 保存した一覧の幅（画面の広さに合わせた上限は、つまみを動かしたときにかける）
        ListColumn.Width = new GridLength(Math.Clamp(ViewModel.ListWidth, MinListWidth, 1600));

        _preview = new FontPreviewControl { ReferenceHeight = 270 };
        PreviewHost.Child = _preview;
        ViewModel.PreviewTargetChanged += (_, font) => UpdatePreview(font);

        // 左の一覧のツリー（ViewModel を作ったときの一覧はもうできているので、ここで最初の節を作る）
        ViewModel.TreeRebuilt += (_, _) => RebuildTreeNodes();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        RebuildTreeNodes();
    }

    /// <summary>プレビューに表示するフォント設定とサンプル文字を渡して描き直す（同じ参照の内容を編集したときも描き直すため Invalidate する）。</summary>
    private void UpdatePreview(N3FontSet? font)
    {
        if (_preview is null) return;
        _preview.SampleText = PreviewSampleText;
        _preview.RubyText = PreviewRubyText;
        _preview.FontSet = font;
        _preview.Invalidate();
    }

    /// <summary>
    /// ビューに入るときに、行リストで選ばれていた行（表示中のタブの行番号）と、選んでおくフォント設定の名前を受け取る
    /// （<see cref="Enter"/> の前に呼ぶ）。名前が無ければ前回の選択のまま。
    /// </summary>
    public void SetLineContext(IReadOnlyList<int> selectedLines, string? fontName)
    {
        ViewModel?.SetLineSelection(selectedLines);
        _pendingSelectName = string.IsNullOrEmpty(fontName) ? null : fontName;
    }

    /// <summary>
    /// ビューに入ったときにメインウィンドウから呼ぶ。一覧を作り直して選ぶフォント設定を選び、戻り先のビューの名前をボタンに出し、
    /// Esc を受けられるようにビューの中へフォーカスを置く。
    /// </summary>
    public void Enter(string backTargetName)
    {
        BackButton.Content = $"{backTargetName}へ戻る (Esc)";
        if (ViewModel is { } vm)
        {
            vm.OnEnter();

            // 名前が NicoKaraPrep のフォント設定に無いとき（書き出しのベースの n3proj にだけある名前など）は、前回の選択のまま
            if (_pendingSelectName is { } name) vm.SelectByName(name);
            _pendingSelectName = null;
            vm.RunAnalysis();
        }

        if (IsLoaded)
        {
            FocusInitial();
        }
        else
        {
            _focusOnLoaded = true;
        }
    }

    /// <summary>ビューを抜けるときにメインウィンドウから呼ぶ。保存待ちの編集を保存する。</summary>
    public void FlushPendingSave()
    {
        if (ViewModel is not { } vm) return;
        vm.FlushPendingSave();
        vm.OnExit();
    }

    /// <summary>一覧の選択中の節（行）にフォーカスを置く（一覧が空なら戻るボタン）。</summary>
    private void FocusInitial()
    {
        if (ViewModel is { IsTreeMode: true } vm)
        {
            if (vm.SelectedKey is { } key && _treeNodes.TryGetValue(key, out var node)
                && FontTree.ContainerFromNode(node) is TreeViewItem treeItem && treeItem.Focus(FocusState.Programmatic))
            {
                return;
            }
            if (FontTree.RootNodes.Count > 0 && FontTree.Focus(FocusState.Programmatic)) return;
        }
        else
        {
            if (ViewModel?.SelectedItem is { } item && FontList.ContainerFromItem(item) is ListViewItem container
                && container.Focus(FocusState.Programmatic))
            {
                return;
            }
            if (FontList.Items.Count > 0 && FontList.Focus(FocusState.Programmatic)) return;
        }
        BackButton.Focus(FocusState.Programmatic);
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Esc で前のビューへ戻る。ComboBox のドロップダウンやフライアウトを閉じる Esc は
    /// そちらで処理済み（Handled）になり、ここへは届かない。
    /// 名前の欄で確定前の入力があれば、それは取り消してから戻る（戻ったあとの LostFocus で名前が変わらないように）。
    /// </summary>
    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled || e.Key != Windows.System.VirtualKey.Escape) return;
        e.Handled = true;
        var focused = XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot);
        if (ReferenceEquals(focused, NameBox) && ViewModel?.Editor.Font is { } font && NameBox.Text != font.Name)
        {
            NameBox.Text = font.Name;
        }
        if (ReferenceEquals(focused, FolderNameBox) && ViewModel?.SelectedFolder is { } folder && FolderNameBox.Text != folder.Name)
        {
            FolderNameBox.Text = folder.Name;
        }
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------ 元に戻す・やり直し

    /// <summary>
    /// フォント設定ビューの中の操作を 1 つ戻す（メニューの 元に戻す と Ctrl+Z）。文字の入力欄にフォーカスがあるときは
    /// 入力欄の 元に戻す に任せて何もしない（false）。
    /// </summary>
    public bool TryUndo() => TryUndoRedo(redo: false);

    /// <summary>フォント設定ビューの中の操作をやり直す（メニューの やり直し と Ctrl+Y）。</summary>
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

    /// <summary>直前に実行した操作がやり直しだったか（<see cref="TryUndoRedo"/> の二重実行防止に使う）。</summary>
    private bool _lastUndoRedoWasRedo;

    private bool IsTextInputFocused() =>
        XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) is TextBox or RichEditBox or PasswordBox or AutoSuggestBox;

    private void OnUndoInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        args.Handled = TryUndo();

    private void OnRedoInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        args.Handled = TryRedo();

    // ------------------------------------------------------------ 一覧の幅

    /// <summary>
    /// 左の一覧の幅を変える（最小 220px。編集欄に 360px と右の列が残る幅まで）。save なら設定に保存する。
    /// </summary>
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

    // ------------------------------------------------------------ 一覧（ツリー）

    /// <summary>ViewModel の一覧から、ツリーの節を作り直して今の選択を選ぶ。</summary>
    private void RebuildTreeNodes()
    {
        if (ViewModel is not { } vm) return;
        _syncingTree = true;
        try
        {
            FontTree.RootNodes.Clear();
            _treeNodes.Clear();
            foreach (var item in vm.TreeRoots) FontTree.RootNodes.Add(CreateTreeNode(item, vm));
        }
        finally
        {
            _syncingTree = false;
        }
        SyncTreeSelection(bringIntoView: true);
    }

    private TreeViewNode CreateTreeNode(FontTreeItem item, FontSettingsViewModel vm)
    {
        var node = new TreeViewNode { Content = item };
        _treeNodes[item.Key] = node;
        foreach (var child in item.Children) node.Children.Add(CreateTreeNode(child, vm));
        node.IsExpanded = item.Children.Count > 0 && vm.IsExpanded(item);
        return node;
    }

    /// <summary>選択が変わったら（一覧の作り直しの最中はまとめて <see cref="RebuildTreeNodes"/> で）ツリーの選択を合わせる。</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ViewModel is not { } vm || vm.IsRebuildingTree || _selectingFromTree) return;
        switch (e.PropertyName)
        {
            case nameof(FontSettingsViewModel.SelectedItem):
            case nameof(FontSettingsViewModel.SelectedFolder):
                SyncTreeSelection(bringIntoView: true);
                break;
            case nameof(FontSettingsViewModel.IsTreeMode) when vm.IsTreeMode:
                // 検索を空にして階層の表示に戻ったら、選んでいるものまで開いて見せる
                SyncTreeSelection(bringIntoView: true);
                break;
        }
    }

    /// <summary>
    /// ViewModel の選択をツリーの選択に写す（閉じた節の中なら上の階層を開く）。検索・絞り込みの結果を並べているあいだは写さず、
    /// 階層の表示に戻ったときに、そのとき選んでいるものだけを開いて見せる。
    /// </summary>
    private void SyncTreeSelection(bool bringIntoView)
    {
        if (ViewModel is not { IsTreeMode: true } vm) return;
        TreeViewNode? node = vm.SelectedKey is { } key && _treeNodes.TryGetValue(key, out var n) ? n : null;
        _syncingTree = true;
        try
        {
            for (var parent = node?.Parent; parent?.Content is FontTreeItem parentItem; parent = parent.Parent)
            {
                if (parent.IsExpanded) continue;
                parent.IsExpanded = true;
                vm.SetExpanded(parentItem.Key, true);
            }
            if (node is not null)
            {
                if (!ReferenceEquals(FontTree.SelectedNode, node)) FontTree.SelectedNode = node;
            }
            else if (FontTree.SelectedNodes.Count > 0)
            {
                FontTree.SelectedNodes.Clear();
            }
        }
        finally
        {
            _syncingTree = false;
        }
        if (node is not null && bringIntoView && vm.IsTreeMode) BringIntoView(node);
    }

    /// <summary>節まで一覧の表示を送る（節の行ができてから）。</summary>
    private void BringIntoView(TreeViewNode node)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _treeList ??= FindDescendant<ListView>(FontTree);
            try
            {
                _treeList?.ScrollIntoView(node);
            }
            catch (Exception)
            {
                // 作り直しの途中で節が一覧から外れていたら何もしない
            }
        });
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found) return found;
            if (FindDescendant<T>(child) is { } deeper) return deeper;
        }
        return null;
    }

    /// <summary>
    /// ツリーで選んだ節を ViewModel へ渡す。選択の変更の中でツリーの選択を変えると、ツリーの選択の処理が入れ子になって
    /// 終わらなくなる（スタックオーバーフロー）ので、ここから始まった ViewModel の選択の変化はツリーへ写し返さず、
    /// 選択が外れたときの選び直しも、選択の処理が終わってから行う。
    /// </summary>
    private void OnFontTreeSelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        if (_syncingTree || ViewModel is not { } vm || vm.IsRebuildingTree) return;
        if (sender.SelectedNode?.Content is FontTreeItem item)
        {
            SelectFromTree(vm, item);
            return;
        }

        // 選択が外れた（Ctrl+クリックなど。別の節へ選び直す途中で一時的に空になることもある）:
        // 落ち着いてからまだ空なら、編集中のものを選び直す（編集欄を空にしない）
        DispatcherQueue.TryEnqueue(() =>
        {
            if (FontTree.SelectedNode is null && ViewModel?.SelectedKey is not null) SyncTreeSelection(bringIntoView: false);
        });
    }

    /// <summary>節を押したとき（選択の変更が届かない押し直しでも、その節を選ぶ）。</summary>
    private void OnFontTreeItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (_syncingTree || ViewModel is not { } vm) return;
        var item = (args.InvokedItem as TreeViewNode)?.Content as FontTreeItem ?? args.InvokedItem as FontTreeItem;
        if (item is not null) SelectFromTree(vm, item);
    }

    /// <summary>ツリーで選ばれた節を ViewModel で選ぶ（ツリーへは写し返さない）。</summary>
    private void SelectFromTree(FontSettingsViewModel vm, FontTreeItem item)
    {
        if (string.Equals(vm.SelectedKey, item.Key, StringComparison.OrdinalIgnoreCase)) return;
        _selectingFromTree = true;
        try
        {
            vm.SelectNode(item.Key);
        }
        finally
        {
            _selectingFromTree = false;
        }
    }

    private void OnFontTreeExpanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (_syncingTree || ViewModel is not { } vm) return;
        if (args.Node?.Content is FontTreeItem item) vm.SetExpanded(item.Key, true);
    }

    private void OnFontTreeCollapsed(TreeView sender, TreeViewCollapsedEventArgs args)
    {
        if (_syncingTree || ViewModel is not { } vm) return;
        if (args.Node?.Content is FontTreeItem item) vm.SetExpanded(item.Key, false);
    }

    private static FontTreeItem? DraggedItem(IEnumerable<object> items) =>
        items.FirstOrDefault() switch
        {
            TreeViewNode node => node.Content as FontTreeItem,
            FontTreeItem item => item,
            _ => null,
        };

    /// <summary>ドラッグの開始。「この曲専用」のまとまりそのものは動かせない。</summary>
    private void OnFontTreeDragItemsStarting(TreeView sender, TreeViewDragItemsStartingEventArgs args)
    {
        var item = DraggedItem(args.Items);
        if (item is null || item.IsSongGroup)
        {
            args.Cancel = true;
            return;
        }
        _draggedKey = item.Key;
    }

    /// <summary>
    /// ドラッグで並べ替えたあと。ツリーが動かした節の並びを読み取り、ViewModel へ渡して階層に反映する
    /// （ツリーの後始末が済んでから作り直すよう、少し遅らせる）。
    /// </summary>
    private void OnFontTreeDragItemsCompleted(TreeView sender, TreeViewDragItemsCompletedEventArgs args)
    {
        string? key = _draggedKey ?? DraggedItem(args.Items)?.Key;
        _draggedKey = null;
        if (ViewModel is not { } vm || args.DropResult == Windows.ApplicationModel.DataTransfer.DataPackageOperation.None) return;
        var dropped = ReadTreeNodes(FontTree.RootNodes);
        DispatcherQueue.TryEnqueue(() => vm.ApplyDroppedTree(dropped, key));
    }

    private static List<DroppedTreeNode> ReadTreeNodes(IList<TreeViewNode> nodes) =>
        nodes
            .Where(n => n.Content is FontTreeItem)
            .Select(n => new DroppedTreeNode(((FontTreeItem)n.Content).Key, ReadTreeNodes(n.Children)))
            .ToList();

    // ------------------------------------------------------------ 一覧（検索・絞り込みの結果）

    private void OnFontListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is not { } vm || vm.IsRebuilding || !vm.IsFlatMode) return;
        var item = FontList.SelectedItem as FontListItem;
        if (item is null && vm.SelectedItem is { } current && vm.Items.Contains(current))
        {
            // Ctrl+クリックなどで選択が外れたときは、編集中のフォント設定を選び直す（編集欄を空にしない）
            FontList.SelectedItem = current;
            return;
        }
        if (item is not null) vm.SelectedItem = item;
    }

    // ------------------------------------------------------------ 一覧の下のボタン

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.AddNew();
        FocusNameBox();
    }

    private void OnAddFolderClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.AddFolder();
        DispatcherQueue.TryEnqueue(() =>
        {
            FolderNameBox.Focus(FocusState.Programmatic);
            FolderNameBox.SelectAll();
        });
    }

    private void OnDuplicateClick(object sender, RoutedEventArgs e) => ViewModel?.DuplicateSelected();

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        if (vm.SelectedFolder is { IsFolder: true })
        {
            // フォルダだけを消す（中身は残るので確認しない。Ctrl+Z で戻せる）
            vm.DeleteSelectedFolder(withFonts: false);
            return;
        }
        if (vm.SelectedItem is not { } item) return;
        int lines = vm.CountLinesLosingFont(item);
        if (lines > 0)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "フォント設定の削除",
                Content = new TextBlock
                {
                    Text = $"フォント設定「{item.Font.Name}」は {lines} 行で使われています。削除しますか？\n" +
                           "（削除すると、書き出しではこれらの行に既定のフォント設定が使われます。Ctrl+Z で元に戻せます）",
                    TextWrapping = TextWrapping.Wrap,
                },
                PrimaryButtonText = "削除",
                CloseButtonText = "キャンセル",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary) return;
        }
        vm.Delete(item);
    }

    private void OnMoveUpClick(object sender, RoutedEventArgs e) => ViewModel?.MoveSelected(-1);

    private void OnMoveDownClick(object sender, RoutedEventArgs e) => ViewModel?.MoveSelected(+1);

    private void OnOutdentClick(object sender, RoutedEventArgs e) => ViewModel?.OutdentSelected();

    private void OnIndentClick(object sender, RoutedEventArgs e) => ViewModel?.IndentSelected();

    /// <summary>移動先を選ぶ画面を出し、選んだところの中の末尾へ移す。</summary>
    private async void OnMoveToClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { CanMoveTo: true } vm) return;
        string label = vm.SelectedItem?.Font.Name ?? vm.SelectedFolder?.Name ?? "";
        var dialog = new FontMoveDialog(label, vm.MoveTargets());
        await ShowDialogAsync(dialog);
        if (dialog.SelectedTarget is { } target) vm.MoveSelectedInto(target.Key);
    }

    // ------------------------------------------------------------ フォルダの欄

    private void OnFolderNameKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        e.Handled = true;
        CommitFolderName();
    }

    private void OnFolderNameLostFocus(object sender, RoutedEventArgs e) => CommitFolderName();

    private void CommitFolderName()
    {
        if (ViewModel is not { SelectedFolder: { IsFolder: true } folder } vm || FolderNameBox.Text == folder.Name) return;
        vm.RenameSelectedFolder(FolderNameBox.Text);
    }

    private void OnDeleteFolderClick(object sender, RoutedEventArgs e) => ViewModel?.DeleteSelectedFolder(withFonts: false);

    /// <summary>フォルダと中のフォント設定をまとめて削除する（確認する）。</summary>
    private async void OnDeleteFolderAllClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { SelectedFolder: { IsFolder: true } folder } vm) return;
        var fonts = vm.FontsIn(folder);
        int used = fonts.Count(f => vm.CountLinesLosingFont(f) > 0);
        string usedText = used > 0
            ? $"うち {used} 件は開いている歌詞で使われていて、削除すると書き出しではその行に既定のフォント設定が使われます。"
            : "";
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "フォルダと中身の削除",
            Content = new TextBlock
            {
                Text = $"フォルダ「{folder.Name}」と、中のフォント設定 {fonts.Count} 件（下の階層も含む）を削除しますか？\n{usedText}（Ctrl+Z で元に戻せます）",
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "削除",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary) return;
        vm.DeleteSelectedFolder(withFonts: true);
    }

    /// <summary>フォルダの欄の「中のフォント設定」を押したとき: そのフォント設定を開く。</summary>
    private void OnFolderFontClick(object sender, ItemClickEventArgs e)
    {
        if (ViewModel is { } vm && e.ClickedItem is FontListItem item) vm.Select(item.Id);
    }

    private async void OnImportN3ProjClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm || _importN3Proj is null) return;
        await vm.ImportN3ProjAsync(_importN3Proj);
    }

    /// <summary>ニコカラメーカー3 のテンプレート（TemplateFont\*.tpl）を読み、選んだものをアプリ共通に追加する。</summary>
    private async void OnImportTemplateClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        var folders = Nkm3Environment.FindTemplateFontFolders();
        if (folders.Count == 0)
        {
            vm.Main.StatusText = "ニコカラメーカー3 のフォント設定テンプレートのフォルダが見つかりませんでした";
            await ShowMessageAsync("テンプレートが見つかりません",
                "ニコカラメーカー3 のフォント設定テンプレートのフォルダ（設定フォルダの TemplateFont）が見つかりませんでした。\n" +
                "ニコカラメーカー3 でフォント設定をテンプレートとして保存してから、もう一度お試しください。");
            return;
        }

        vm.Main.StatusText = "ニコカラメーカー3 のテンプレートを読み込んでいます...";
        var errors = new List<string>();
        var templates = await Task.Run(() =>
        {
            var list = new List<N3FontSet>();
            foreach (string folder in folders) list.AddRange(N3FontTemplateReader.ReadTemplateFolder(folder, errors));
            return list;
        });
        string errorText = errors.Count > 0 ? $"（読めなかったファイル {errors.Count} 件: {string.Join(" / ", errors.Take(3))}{(errors.Count > 3 ? " ほか" : "")}）" : "";
        vm.Main.StatusText = $"ニコカラメーカー3 のテンプレート {templates.Count} 件を読み込みました{errorText}";
        if (templates.Count == 0)
        {
            await ShowMessageAsync("テンプレートがありません", $"{string.Join("\n", folders)}\n\nにフォント設定テンプレート（*.tpl）がありませんでした。{errorText}");
            return;
        }

        var existing = vm.AllItems.Where(i => !i.IsSong).Select(i => i.Font.Name).ToList();
        string folderText = $"読み込んだフォルダ（読み取りのみ。テンプレートは変更しません）: {string.Join(" ／ ", folders)}{errorText}";
        var dialog = new TemplateImportDialog(templates, existing, folderText);
        if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary) return;
        vm.AddTemplates(dialog.Selected);
    }

    // ------------------------------------------------------------ 基本

    private void FocusNameBox()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            NameBox.Focus(FocusState.Programmatic);
            NameBox.SelectAll();
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
        if (ViewModel is not { Editor.Font: { } font } vm || NameBox.Text == font.Name) return;
        vm.RenameSelected(NameBox.Text);
    }

    private void OnCopyToSongClick(object sender, RoutedEventArgs e) => ViewModel?.CopySelectedToSong();

    private async void OnMoveToCommonClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { SelectedItem: { } item } vm) return;
        if (vm.FindCommonNamed(item) is null)
        {
            vm.MoveSelectedToCommon(replace: false);
            return;
        }
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "アプリ共通へ移す",
            Content = new TextBlock
            {
                Text = $"アプリ共通に同じ名前のフォント設定「{item.Font.Name}」があります。この曲専用のもので置き換えますか？\n" +
                       "（置き換えると、ほかの曲の書き出しにもこの内容が使われます。Ctrl+Z で元に戻せます）",
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "置き換える",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary) return;
        vm.MoveSelectedToCommon(replace: true);
    }

    // ------------------------------------------------------------ 配色

    /// <summary>配色の 1 箇所のボタン: その箇所を下の編集欄で編集する。</summary>
    private void OnBrushCellClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && (sender as FrameworkElement)?.Tag is int index) vm.Editor.SelectedBrushIndex = index;
    }

    private void OnSwapClick(object sender, RoutedEventArgs e) => ViewModel?.Editor.SwapBeforeAfter();

    private void OnAfterToBeforeClick(object sender, RoutedEventArgs e) => ViewModel?.Editor.CopyAfterToBefore();

    private void OnBeforeToAfterClick(object sender, RoutedEventArgs e) => ViewModel?.Editor.CopyBeforeToAfter();

    private void OnCopyFlyoutOpening(object sender, object e)
    {
        CopySearchBox.Text = "";
        UpdateCopySources();
        int index = ViewModel?.Editor.SelectedBrushIndex ?? 0;
        if (index is >= 0 and < N3FontDetail.BrushCount && CopyScopeButtons.Items.Count > 1)
        {
            CopyScopeButtons.Items[1] = $"編集中の 1 箇所だけ（{N3FontDetail.BrushLabels[index]}）";
        }
    }

    private void OnCopySearchChanged(object sender, TextChangedEventArgs e) => UpdateCopySources();

    private void UpdateCopySources()
    {
        if (ViewModel is not { } vm) return;
        string q = CopySearchBox.Text.Trim();
        CopySourceList.ItemsSource = vm.AllItems
            .Where(i => !ReferenceEquals(i, vm.SelectedItem))
            .Where(i => q.Length == 0 || i.Font.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();
        CopyBrushesButton.IsEnabled = false;
    }

    private void OnCopySourceChanged(object sender, SelectionChangedEventArgs e) =>
        CopyBrushesButton.IsEnabled = CopySourceList.SelectedItem is FontListItem;

    private void OnCopyBrushesClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm || CopySourceList.SelectedItem is not FontListItem source) return;
        int[] indices = CopyScopeButtons.SelectedIndex == 1
            ? new[] { vm.Editor.SelectedBrushIndex }
            : Enumerable.Range(0, N3FontDetail.BrushCount).ToArray();
        vm.CopyBrushesFrom(source, indices);
        CopyFlyout.Hide();
    }

    // ------------------------------------------------------------ 右ペイン

    private void OnSampleTextChanged(object sender, TextChangedEventArgs e) => ViewModel?.RaisePreview();

    private void OnAssignClick(object sender, RoutedEventArgs e) => ViewModel?.AssignToSelectedLines();

    private void OnIssueClick(object sender, ItemClickEventArgs e)
    {
        if (ViewModel is not { } vm || e.ClickedItem is not FontIssueItem issue) return;
        if (issue.Issue.FontId is { } id && vm.Select(id))
        {
            if (issue.Issue.BrushIndex >= 0) vm.Editor.SelectedBrushIndex = issue.Issue.BrushIndex;
        }
        else if (issue.Issue.FontName is { } name)
        {
            vm.Main.StatusText = $"フォント設定「{name}」は一覧にありません。「追加」で同じ名前のフォント設定を作るか、行の指定を直してください";
        }
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
