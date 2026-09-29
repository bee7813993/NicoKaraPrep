using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NicoKaraPrep.App.ViewModels;
using NicoKaraPrep.App.Views.FontSettings;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.Views;

/// <summary>
/// ニコカラメーカー3 のフォント設定ビュー（F3）。左 = 一覧、中央 = 選択中のフォント設定の編集、右 = プレビュー・使用状況・検証。
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

    public FontSettingsView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
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
        _preview = new FontPreviewControl();
        PreviewHost.Child = _preview;
        ViewModel.PreviewTargetChanged += (_, font) => UpdatePreview(font);
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

    /// <summary>一覧の選択行にフォーカスを置く（一覧が空なら戻るボタン）。</summary>
    private void FocusInitial()
    {
        if (ViewModel?.SelectedItem is { } item && FontList.ContainerFromItem(item) is ListViewItem container
            && container.Focus(FocusState.Programmatic))
        {
            return;
        }
        if (FontList.Items.Count > 0 && FontList.Focus(FocusState.Programmatic)) return;
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
        if (XamlRoot is not null && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), NameBox)
            && ViewModel?.Editor.Font is { } font && NameBox.Text != font.Name)
        {
            NameBox.Text = font.Name;
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
        var now = DateTime.UtcNow;
        if ((now - _lastUndoRedoUtc).TotalMilliseconds < 150) return true;
        _lastUndoRedoUtc = now;
        if (redo) vm.Redo();
        else vm.Undo();
        return true;
    }

    private bool IsTextInputFocused() =>
        XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) is TextBox or RichEditBox or PasswordBox or AutoSuggestBox;

    private void OnUndoInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        args.Handled = TryUndo();

    private void OnRedoInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        args.Handled = TryRedo();

    // ------------------------------------------------------------ 一覧

    private void OnFontListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is not { } vm || vm.IsRebuilding) return;
        var item = FontList.SelectedItem as FontListItem;
        if (item is null && vm.SelectedItem is { } current && vm.Items.Contains(current))
        {
            // Ctrl+クリックなどで選択が外れたときは、編集中のフォント設定を選び直す（編集欄を空にしない）
            FontList.SelectedItem = current;
            return;
        }
        vm.SelectedItem = item;
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.AddNew();
        FocusNameBox();
    }

    private void OnDuplicateClick(object sender, RoutedEventArgs e) => ViewModel?.DuplicateSelected();

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { SelectedItem: { } item } vm) return;
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
