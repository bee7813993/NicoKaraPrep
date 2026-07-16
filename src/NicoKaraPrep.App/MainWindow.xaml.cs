using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NicoKaraPrep.App.Services;
using NicoKaraPrep.App.ViewModels;
using NicoKaraPrep.App.Views;
using NicoKaraPrep.Core.Model;
using NicoKaraPrep.Core.Validation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace NicoKaraPrep.App;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; } = new();

    private readonly DispatcherQueueTimer _validateTimer;

    public MainWindow()
    {
        InitializeComponent();
        Title = ViewModel.WindowTitle;

        string icon = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        if (File.Exists(icon)) AppWindow.SetIcon(icon);

        ViewModel.TextMeasurer = new DirectWriteTextMeasurer();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.WindowTitle))
            {
                Title = ViewModel.WindowTitle;
            }
        };

        Player.Height = Math.Clamp(ViewModel.Settings.PlayerHeightPx, 120, 1200);

        RestoreWindowBounds();
        Closed += (_, _) => SaveWindowBounds();

        _validateTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _validateTimer.Interval = TimeSpan.FromMilliseconds(400);
        _validateTimer.IsRepeating = false;
        _validateTimer.Tick += (_, _) => TryRun(ViewModel.RunValidation);

        // パレットのドラッグ＆ドロップ並び替え → スロット番号を振り直す
        ViewModel.EmojiSlots.CollectionChanged += (_, e) =>
        {
            if (_suppressSlotReorder) return;
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (_suppressSlotReorder) return;
                    _suppressSlotReorder = true;
                    try
                    {
                        TryRun(ViewModel.ApplySlotOrderFromView);
                    }
                    finally
                    {
                        _suppressSlotReorder = false;
                    }
                });
            }
        };

        // コマンドライン引数のファイルを開く
        string[] args = Environment.GetCommandLineArgs();
        if (args.Length > 1 && File.Exists(args[1]))
        {
            TryRun(() => ViewModel.OpenFile(args[1]));
            AfterDocumentLoaded();
        }

        // デバッグ用: 起動直後に絵文字リスト編集を自動で開く
        if (args.Contains("--debug-emoji-dialog"))
        {
            var timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(1500);
            timer.IsRepeating = false;
            timer.Tick += (_, _) =>
            {
                DebugLog("絵文字リスト編集を自動オープンします");
                OnEmojiListClick(this, new RoutedEventArgs());
                DebugLog("OnEmojiListClick 呼び出し直後（ShowAsync 待機中）");
            };
            timer.Start();
        }
    }

    private static void DebugLog(string message)
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NicoKaraPrep");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
        }
        catch
        {
        }
    }

    // ------------------------------------------------ ウィンドウ位置・サイズの復元

    private void RestoreWindowBounds()
    {
        var s = ViewModel.Settings;
        if (s.WindowWidth < 400 || s.WindowHeight < 300) return; // 保存なし or 異常値

        var rect = new Windows.Graphics.RectInt32(s.WindowX, s.WindowY, s.WindowWidth, s.WindowHeight);

        // モニタ構成が変わっていても画面内に収まるように補正
        var area = Microsoft.UI.Windowing.DisplayArea.GetFromRect(rect, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest);
        if (area is not null)
        {
            var wa = area.WorkArea;
            rect.Width = Math.Min(rect.Width, wa.Width);
            rect.Height = Math.Min(rect.Height, wa.Height);
            rect.X = Math.Clamp(rect.X, wa.X, Math.Max(wa.X, wa.X + wa.Width - rect.Width));
            rect.Y = Math.Clamp(rect.Y, wa.Y, Math.Max(wa.Y, wa.Y + wa.Height - rect.Height));
        }

        AppWindow.MoveAndResize(rect);

        if (s.WindowMaximized && AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }
    }

    private void SaveWindowBounds()
    {
        try
        {
            var s = ViewModel.Settings;
            bool maximized = AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p &&
                             p.State == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized;
            s.WindowMaximized = maximized;
            if (!maximized)
            {
                s.WindowX = AppWindow.Position.X;
                s.WindowY = AppWindow.Position.Y;
                s.WindowWidth = AppWindow.Size.Width;
                s.WindowHeight = AppWindow.Size.Height;
            }
            s.Save();
        }
        catch (Exception)
        {
            // 終了時の保存失敗は無視（次回はデフォルト位置で起動）
        }
    }

    /// <summary>編集後のデバウンス付き再チェック。</summary>
    private void ScheduleValidation()
    {
        _validateTimer.Stop();
        _validateTimer.Start();
    }

    /// <summary>ドキュメント読込直後の共通処理（タブ同期・チェック実行・関連メディアのロード）。</summary>
    private void AfterDocumentLoaded()
    {
        SyncTabSelection();
        ScheduleValidation();
        if (ViewModel.MediaPath is string mp && File.Exists(mp))
        {
            OpenMedia(mp);
        }
    }

    private IntPtr Hwnd => WinRT.Interop.WindowNative.GetWindowHandle(this);

    // ------------------------------------------------------------ ファイル

    private async void OnOpenClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Hwnd);
        picker.FileTypeFilter.Add(".rlf");
        picker.FileTypeFilter.Add(".lrc");
        picker.FileTypeFilter.Add(".kra");
        picker.FileTypeFilter.Add(".txt");

        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null) return;
        TryRun(() => ViewModel.OpenFile(file.Path));
        AfterDocumentLoaded();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        // 上書き保存は常に「タブ含む全行」をメインのファイルへ（マスター保存）
        if (ViewModel.MainFilePath is string path)
        {
            TryRun(() => ViewModel.SaveFullTo(path));
        }
        else
        {
            OnSaveAsClick(sender, e);
        }
    }

    private void OnSaveAsClick(object sender, RoutedEventArgs e)
    {
        string? mainPath = ViewModel.MainFilePath;
        string suggested = mainPath is string mp ? Path.GetFileNameWithoutExtension(mp) : "lyrics";
        bool rlfFirst = mainPath is not null &&
                        Path.GetExtension(mainPath).Equals(".rlf", StringComparison.OrdinalIgnoreCase);

        var types = rlfFirst
            ? new[] { LyricsFileTypes[1], LyricsFileTypes[0], LyricsFileTypes[2] }
            : LyricsFileTypes;

        string? path = SaveFileDialog.Show(Hwnd, ViewModel.GetDefaultSaveFolder(), suggested, types, rlfFirst ? "rlf" : "lrc");
        if (path is null) return;
        TryRun(() => ViewModel.SaveFullTo(path));
    }

    /// <summary>表示中のタブへファイルを読み込んで差し替える。</summary>
    private async void OnReloadTabClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Hwnd);
        picker.FileTypeFilter.Add(".rlf");
        picker.FileTypeFilter.Add(".lrc");
        picker.FileTypeFilter.Add(".kra");
        picker.FileTypeFilter.Add(".txt");

        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null) return;
        TryRun(() => ViewModel.ReloadActiveTabFromFile(file.Path));
        RefreshAfterTabChange();
    }

    /// <summary>表示中のタブの内容だけを別ファイルへ保存する。</summary>
    private void OnSaveTabAsClick(object sender, RoutedEventArgs e)
    {
        string suggested = ViewModel.GetSuggestedFileBaseName();
        var types = ViewModel.CurrentFormat == ViewModels.DocumentFormat.Rlf
            ? new[] { LyricsFileTypes[1], LyricsFileTypes[0], LyricsFileTypes[2] }
            : LyricsFileTypes;
        string defaultExt = ViewModel.CurrentFormat == ViewModels.DocumentFormat.Rlf ? "rlf" : "lrc";

        string? path = SaveFileDialog.Show(Hwnd, ViewModel.GetDefaultSaveFolder(), suggested, types, defaultExt);
        if (path is null) return;
        TryRun(() => ViewModel.SaveActiveTabCopyTo(path));
    }

    // ------------------------------------------------------ クリップボード

    private async void OnPasteClick(object sender, RoutedEventArgs e)
    {
        var content = Clipboard.GetContent();
        if (!content.Contains(StandardDataFormats.Text)) return;
        string text = await content.GetTextAsync();
        TryRun(() => ViewModel.LoadFromText(text));
        ScheduleValidation();
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(ViewModel.GetTextEditModeText());
        Clipboard.SetContent(package);
        ViewModel.StatusText = "テキスト編集モード形式でコピーしました";
    }

    // -------------------------------------------------------- ドラッグ&ドロップ

    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".wmv", ".mov", ".webm",
        ".mp3", ".wav", ".ogg", ".m4a", ".flac", ".wma", ".aac", ".opus",
    };

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        if (items.FirstOrDefault(i => i is StorageFile) is not StorageFile file) return;

        // 動画・音声ファイルはメディアとして、それ以外は歌詞として開く
        if (MediaExtensions.Contains(Path.GetExtension(file.Path)))
        {
            OpenMedia(file.Path);
        }
        else
        {
            TryRun(() => ViewModel.OpenFile(file.Path));
            AfterDocumentLoaded();
        }
    }

    private void OnMediaDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            if (e.DragUIOverride is not null)
            {
                e.DragUIOverride.Caption = "メディアとして開く";
            }
        }
    }

    private async void OnMediaDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        if (items.FirstOrDefault(i => i is StorageFile) is not StorageFile file) return;

        // メディアパネルへのドロップは拡張子に関わらずメディアとして開く
        // （歌詞ファイルだけは歌詞として扱う）
        string ext = Path.GetExtension(file.Path);
        if (ext.Equals(".rlf", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".lrc", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".kra", StringComparison.OrdinalIgnoreCase))
        {
            TryRun(() => ViewModel.OpenFile(file.Path));
            AfterDocumentLoaded();
        }
        else
        {
            OpenMedia(file.Path);
        }
    }

    // ------------------------------------------------------------ 行編集

    private void OnLineSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.SelectedLine = LineList.SelectedItem as LineViewModel;
        LineEditor.Text = ViewModel.SelectedLine?.RawText ?? "";
        RenderPreview();

        // チェックボックス表示を ListView の選択と同期
        var selected = LineList.SelectedItems.Cast<LineViewModel>().ToHashSet();
        foreach (var line in ViewModel.Lines)
        {
            line.IsSelected = selected.Contains(line);
        }
    }

    /// <summary>行のチェックボックスで選択に追加 / 解除する（他の行の選択は維持）。</summary>
    private void OnRowCheckClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not LineViewModel vm) return;
        if (LineList.SelectedItems.Contains(vm))
        {
            LineList.SelectedItems.Remove(vm);
        }
        else
        {
            LineList.SelectedItems.Add(vm);
        }
    }

    private void OnApplyLineClick(object sender, RoutedEventArgs e)
    {
        ApplyLineEditor();
    }

    private void OnLineEditorKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            ApplyLineEditor();
            e.Handled = true;
        }
    }

    private void ApplyLineEditor()
    {
        if (ViewModel.SelectedLine is null) return;
        TryRun(() =>
        {
            ViewModel.ApplyRawTextToSelectedLine(LineEditor.Text);
            ViewModel.StatusText = "行を更新しました";
        });
        RenderPreview();
        ScheduleValidation();
    }

    // ------------------------------------------------------------ チェック

    private void OnValidateClick(object sender, RoutedEventArgs e)
    {
        TryRun(ViewModel.RunValidation);
        IssuePanel.IsExpanded = ViewModel.Issues.Count > 0;
    }

    // ------------------------------------------------------ テンプレート

    private async void OnSaveTemplateClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Hwnd);
        picker.FileTypeChoices.Add("NicoKaraPrep テンプレート", new List<string> { ".tttpl" });
        picker.SuggestedFileName = "template";
        StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null) return;
        TryRun(() => ViewModel.SaveTemplate(file.Path));
    }

    private async void OnLoadTemplateClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Hwnd);
        picker.FileTypeFilter.Add(".tttpl");
        picker.FileTypeFilter.Add(".json");
        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null) return;
        TryRun(() => ViewModel.LoadTemplate(file.Path));
        ScheduleValidation();
    }

    private async void OnImportN3ProjClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Hwnd);
        picker.FileTypeFilter.Add(".n3proj");
        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null) return;
        TryRun(() => ViewModel.ApplyN3ProjSettings(file.Path));
        ScheduleValidation();
    }

    private async void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(ViewModel.Settings) { XamlRoot = Content.XamlRoot };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            TryRun(ViewModel.RunValidation);
        }
    }

    private void OnIssueClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ValidationIssue issue) return;
        if (issue.LineIndex < 0 || issue.LineIndex >= ViewModel.Lines.Count) return;
        var line = ViewModel.Lines[issue.LineIndex];
        LineList.SelectedItem = line;
        LineList.ScrollIntoView(line);
    }

    // ------------------------------------------------------------ Undo / Redo

    private DateTime _lastUndoRedo = DateTime.MinValue;

    /// <summary>アクセラレータと PreviewKeyDown の二重発火を防ぐ。</summary>
    private bool DebounceUndoRedo()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastUndoRedo).TotalMilliseconds < 150) return false;
        _lastUndoRedo = now;
        return true;
    }

    private void PerformUndo()
    {
        if (!DebounceUndoRedo()) return;
        if (!ViewModel.Undo()) return;
        AfterUndoRedo();
    }

    private void PerformRedo()
    {
        if (!DebounceUndoRedo()) return;
        if (!ViewModel.Redo()) return;
        AfterUndoRedo();
    }

    private void AfterUndoRedo()
    {
        if (InsertViewActive)
        {
            RefreshInsertView(InsertEditor.SelectionStart);
        }
        LineEditor.Text = ViewModel.SelectedLine?.RawText ?? "";
        RenderPreview();
        ScheduleValidation();
    }

    private void OnUndoClick(object sender, RoutedEventArgs e) => PerformUndo();

    private void OnRedoClick(object sender, RoutedEventArgs e) => PerformRedo();

    // ------------------------------------------------------------ タブ

    private bool _suppressTabSelection;

    /// <summary>TabView の選択を ViewModel のアクティブタブに合わせる。</summary>
    private void SyncTabSelection()
    {
        _suppressTabSelection = true;
        try
        {
            DocTabs.SelectedIndex = ViewModel.ActiveTabIndex;
        }
        finally
        {
            _suppressTabSelection = false;
        }
    }

    /// <summary>タブ切替後の画面更新。</summary>
    private void RefreshAfterTabChange()
    {
        LineEditor.Text = ViewModel.SelectedLine?.RawText ?? "";
        RenderPreview();
        ScheduleValidation();
    }

    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTabSelection) return;
        int index = DocTabs.SelectedIndex;
        if (index < 0) return;
        TryRun(() => ViewModel.SwitchTab(index));
        RefreshAfterTabChange();
    }

    private void OnTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Item is not ViewModels.TabState tab || tab.IsMain) return;
        TryRun(() => ViewModel.CloseTab(tab));
        SyncTabSelection();
        RefreshAfterTabChange();
    }

    private void OnSplitToTabClick(object sender, RoutedEventArgs e)
    {
        if (InsertViewActive) return;
        var indexes = SelectedIndexes;
        if (indexes.Count == 0)
        {
            ViewModel.StatusText = "分離する行を選択してください";
            return;
        }
        TryRun(() =>
        {
            var tab = ViewModel.SplitSelectedToNewTab(indexes);
            if (tab is null) return;
            ViewModel.SwitchTab(ViewModel.Tabs.IndexOf(tab));
            SyncTabSelection();
            RefreshAfterTabChange();
        });
    }

    /// <summary>「選択行を移動」メニューに移動先タブの一覧を並べる。</summary>
    private void PopulateMoveToTabItems(IList<MenuFlyoutItemBase> items)
    {
        items.Clear();
        foreach (var tab in ViewModel.Tabs)
        {
            if (tab == ViewModel.ActiveTab) continue;
            var target = tab;
            var item = new MenuFlyoutItem { Text = tab.Name };
            item.Click += (_, _) => MoveSelectedToTab(target);
            items.Add(item);
        }
        if (items.Count == 0)
        {
            items.Add(new MenuFlyoutItem { Text = "（移動先のタブがありません。まず「選択行を分離」でタブを作成）", IsEnabled = false });
        }
    }

    private void OnMoveToTabFlyoutOpening(object sender, object e) => PopulateMoveToTabItems(MoveToTabFlyout.Items);

    private void OnLineContextMenuOpening(object sender, object e) => PopulateMoveToTabItems(MoveToTabSub.Items);

    /// <summary>右クリックした行が未選択なら、その行だけを選択してからメニューを出す。</summary>
    private void OnLineRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is LineViewModel line &&
            !LineList.SelectedItems.Contains(line))
        {
            LineList.SelectedItem = line;
        }
    }

    private void MoveSelectedToTab(ViewModels.TabState target)
    {
        var indexes = SelectedIndexes;
        if (indexes.Count == 0)
        {
            ViewModel.StatusText = "移動する行を選択してください";
            return;
        }
        TryRun(() =>
        {
            if (ViewModel.MoveSelectedToTab(indexes, target))
            {
                RefreshAfterTabChange();
            }
        });
    }

    private async void OnResetTabsClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Tabs.Count <= 1)
        {
            ViewModel.StatusText = "分離タブはありません";
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "タブ分離の解除",
            Content = $"分離タブ {ViewModel.Tabs.Count - 1} 個をすべて閉じて、行をメインへ時刻順に戻します。よろしいですか？",
            PrimaryButtonText = "戻す",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        TryRun(ViewModel.ResetAllTabs);
        SyncTabSelection();
        RefreshAfterTabChange();
    }

    private async void OnTabDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        var tab = ViewModel.ActiveTab;
        if (tab.IsMain) return;

        var box = new TextBox { Text = tab.Name, SelectionStart = tab.Name.Length };
        var dialog = new ContentDialog
        {
            Title = "タブ名の変更",
            Content = box,
            PrimaryButtonText = "OK",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(box.Text))
        {
            TryRun(() => ViewModel.RenameTab(tab, box.Text));
        }
    }

    // ------------------------------------------------------------ 行操作

    private List<int> SelectedIndexes =>
        LineList.SelectedItems.Cast<LineViewModel>().Select(l => l.Index).OrderBy(i => i).ToList();

    private void SelectLineAt(int index)
    {
        if (index >= 0 && index < ViewModel.Lines.Count)
        {
            LineList.SelectedItem = ViewModel.Lines[index];
            LineList.ScrollIntoView(ViewModel.Lines[index]);
        }
    }

    private void OnSplitLineClick(object sender, RoutedEventArgs e)
    {
        if (InsertViewActive) return; // 挿入ビューでは Ctrl+Enter を PreviewKeyDown で処理
        if (ViewModel.SelectedLine is null) return;
        TryRun(() =>
        {
            int? newIndex = ViewModel.SplitSelectedLine(LineEditor.Text, LineEditor.SelectionStart);
            if (newIndex is int i) SelectLineAt(i);
        });
        ScheduleValidation();
    }

    private void OnJoinLineClick(object sender, RoutedEventArgs e)
    {
        if (InsertViewActive) return; // 挿入ビューでは Ctrl+J を PreviewKeyDown で処理
        if (ViewModel.SelectedLine is null) return;
        int index = ViewModel.SelectedLine.Index;
        TryRun(() =>
        {
            if (ViewModel.JoinSelectedWithNext()) SelectLineAt(index);
        });
        ScheduleValidation();
    }

    private void OnInsertEmptyLineClick(object sender, RoutedEventArgs e)
    {
        TryRun(ViewModel.InsertEmptyLineBelowSelection);
        ScheduleValidation();
    }

    private void OnDeleteLinesClick(object sender, RoutedEventArgs e)
    {
        var indexes = SelectedIndexes;
        if (indexes.Count == 0) return;
        TryRun(() => ViewModel.DeleteLines(indexes));
        ScheduleValidation();
    }

    // ------------------------------------------------------------ エクスポート

    private static readonly (string Label, string Pattern)[] LyricsFileTypes =
    [
        ("歌詞ファイル (*.lrc)", "*.lrc"),
        ("RhythmicaLyrics 編集ファイル (*.rlf)", "*.rlf"),
        ("テキスト (*.txt)", "*.txt"),
    ];

    private void OnExportFileClick(object sender, RoutedEventArgs e)
    {
        var indexes = SelectedIndexes;
        if (indexes.Count == 0)
        {
            ViewModel.StatusText = "エクスポートする行を選択してください";
            return;
        }

        string suggested = ViewModel.GetSuggestedFileBaseName() + (ViewModel.ActiveTabIsMain ? "_part" : "");

        string? path = SaveFileDialog.Show(Hwnd, ViewModel.GetDefaultSaveFolder(), suggested, LyricsFileTypes, "lrc");
        if (path is null) return;
        TryRun(() => ViewModel.ExportLinesToFile(path, indexes));
    }

    private void OnExportClipboardClick(object sender, RoutedEventArgs e)
    {
        var indexes = SelectedIndexes;
        if (indexes.Count == 0)
        {
            ViewModel.StatusText = "エクスポートする行を選択してください";
            return;
        }
        TryRun(() =>
        {
            var package = new DataPackage();
            package.SetText(ViewModel.ExportLinesAsText(indexes));
            Clipboard.SetContent(package);
            ViewModel.MarkExported(indexes, true);
            ViewModel.StatusText = $"{indexes.Count} 行をクリップボードへエクスポートしました";
        });
    }

    private void OnSelectUnexportedClick(object sender, RoutedEventArgs e)
    {
        LineList.SelectedItems.Clear();
        foreach (var line in ViewModel.Lines)
        {
            if (!line.Exported && !line.Model.IsEmpty)
            {
                LineList.SelectedItems.Add(line);
            }
        }
        ViewModel.StatusText = $"未エクスポートの {LineList.SelectedItems.Count} 行を選択しました";
    }

    private void OnClearMarksClick(object sender, RoutedEventArgs e)
    {
        var indexes = SelectedIndexes;
        if (indexes.Count == 0) return;
        ViewModel.MarkExported(indexes, false);
        ViewModel.StatusText = "済マークを解除しました";
    }

    // ------------------------------------------------------------ メディア再生

    private DispatcherQueueTimer? _mediaTimer;
    private int _lastCurrentLine = -1;

    private async void OnOpenMediaClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Hwnd);
        foreach (string ext in new[] { ".mp4", ".mkv", ".avi", ".wmv", ".mov", ".mp3", ".wav", ".ogg", ".m4a", ".flac", ".wma" })
        {
            picker.FileTypeFilter.Add(ext);
        }
        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null) return;
        OpenMedia(file.Path);
    }

    private void OpenMedia(string path)
    {
        TryRun(() =>
        {
            Player.Source = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(path));
            ViewModel.MediaPath = path;
            ViewModel.SaveProject();
            MediaPanel.Visibility = Visibility.Visible;
            MediaPanel.IsExpanded = true;
            ViewModel.StatusText = $"メディアを開きました: {Path.GetFileName(path)}（Ctrl+Space で再生/一時停止）";

            if (_mediaTimer is null)
            {
                _mediaTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
                _mediaTimer.Interval = TimeSpan.FromMilliseconds(200);
                _mediaTimer.IsRepeating = true;
                _mediaTimer.Tick += (_, _) => UpdatePlaybackHighlight();
            }
            _mediaTimer.Start();
        });
    }

    private void UpdatePlaybackHighlight()
    {
        var session = Player.MediaPlayer?.PlaybackSession;
        if (session is null) return;
        if (session.PlaybackState != Windows.Media.Playback.MediaPlaybackState.Playing &&
            _lastCurrentLine >= 0)
        {
            return; // 停止中はハイライトを維持
        }

        int cs = ViewModel.MediaSecondsToTagCs(session.Position.TotalSeconds);
        int? index = ViewModel.UpdateCurrentLine(cs);
        if (index is int i && i != _lastCurrentLine)
        {
            _lastCurrentLine = i;
            if (FollowToggle.IsChecked && i < ViewModel.Lines.Count)
            {
                LineList.ScrollIntoView(ViewModel.Lines[i]);
            }
        }

        // 絵文字挿入ビューの再生追従カーソル
        if (InsertViewActive && _insertFollow &&
            ViewModel.FindInsertViewOffsetForTime(cs) is int offset &&
            offset != _lastInsertCaret)
        {
            _lastInsertCaret = offset;
            InsertEditor.SelectionStart = Math.Min(offset, InsertEditor.Text.Length);
        }
    }

    private Windows.Media.Playback.MediaPlayer? PlayerWithSource =>
        Player.MediaPlayer?.Source is null ? null : Player.MediaPlayer;

    private void PlayMedia()
    {
        if (PlayerWithSource is not { } player)
        {
            ViewModel.StatusText = "メディアが開かれていません（再生 > メディアを開く）";
            return;
        }
        player.Play();
    }

    private void PauseMedia() => PlayerWithSource?.Pause();

    private void TogglePlayPause()
    {
        if (PlayerWithSource is not { } player)
        {
            ViewModel.StatusText = "メディアが開かれていません（再生 > メディアを開く）";
            return;
        }
        if (player.PlaybackSession.PlaybackState == Windows.Media.Playback.MediaPlaybackState.Playing)
        {
            player.Pause();
        }
        else
        {
            player.Play();
        }
    }

    private void OnPlayPauseClick(object sender, RoutedEventArgs e) => TogglePlayPause();

    // ------------------------------------------------ プレイヤーの高さ変更

    private void OnPlayerResizeDelta(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        double current = double.IsNaN(Player.Height) ? Player.ActualHeight : Player.Height;
        Player.Height = Math.Clamp(current + e.Delta.Translation.Y, 120, 1200);
    }

    private void OnPlayerResizeCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
    {
        ViewModel.Settings.PlayerHeightPx = Player.Height;
        ViewModel.Settings.Save();
    }

    private void SeekBy(double seconds)
    {
        var session = PlayerWithSource?.PlaybackSession;
        if (session is null) return;
        var target = session.Position + TimeSpan.FromSeconds(seconds);
        if (target < TimeSpan.Zero) target = TimeSpan.Zero;
        session.Position = target;
    }

    private void OnSeekBackClick(object sender, RoutedEventArgs e) => SeekBy(-ViewModel.Settings.SeekSeconds);

    private void OnSeekForwardClick(object sender, RoutedEventArgs e) => SeekBy(+ViewModel.Settings.SeekSeconds);

    private void OnLineDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if (ViewModel.SelectedLine?.Model.GetFirstTimeCs() is not int cs) return;
        var session = Player.MediaPlayer?.PlaybackSession;
        if (session is null || Player.MediaPlayer?.Source is null) return;
        session.Position = TimeSpan.FromSeconds(ViewModel.TagCsToMediaSeconds(cs));
        ViewModel.StatusText = $"再生位置を {TimeTag.Format(cs)} へ移動しました";
    }

    // ------------------------------------------------ 絵文字挿入ビュー

    private bool InsertViewActive => InsertView.Visibility == Visibility.Visible;

    private bool _insertFollow;
    private int _lastInsertCaret = -1;

    private void OnEmojiModeChanged(object sender, RoutedEventArgs e)
    {
        if (EmojiModeToggle.IsChecked == true) EnterInsertView();
        else ExitInsertView();
    }

    private bool _allowInsertViewTextChange;
    private string _insertViewText = "";

    /// <summary>挿入ビューは表示専用（プログラムからの更新以外の文字入力をキャンセル）。</summary>
    private void OnInsertEditorBeforeTextChanging(TextBox sender, TextBoxBeforeTextChangingEventArgs args)
    {
        if (!_allowInsertViewTextChange) args.Cancel = true;
    }

    /// <summary>IME 変換確定など BeforeTextChanging をすり抜けた入力を復元する保険。</summary>
    private void OnInsertEditorTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_allowInsertViewTextChange) return;
        if (InsertEditor.Text == _insertViewText) return;

        // 最初に食い違う位置へカーソルを戻しつつ本来のテキストを復元
        string current = InsertEditor.Text;
        int diff = 0;
        int max = Math.Min(current.Length, _insertViewText.Length);
        while (diff < max && current[diff] == _insertViewText[diff]) diff++;

        SetInsertEditorText(_insertViewText);
        InsertEditor.SelectionStart = Math.Min(diff, InsertEditor.Text.Length);
    }

    private void SetInsertEditorText(string text)
    {
        _allowInsertViewTextChange = true;
        try
        {
            InsertEditor.Text = text;
            _insertViewText = InsertEditor.Text; // TextBox 内部の改行正規化後の値を保持
        }
        finally
        {
            _allowInsertViewTextChange = false;
        }
    }

    private void EnterInsertView()
    {
        SetInsertEditorText(ViewModel.BuildInsertViewText());
        NormalView.Visibility = Visibility.Collapsed;
        InsertView.Visibility = Visibility.Visible;

        int caret = ViewModel.SelectedLine is { } sel ? ViewModel.GetInsertViewLineStart(sel.Index) : 0;
        InsertEditor.Focus(FocusState.Programmatic);
        InsertEditor.SelectionStart = Math.Min(caret, InsertEditor.Text.Length);
        ViewModel.StatusText = "絵文字挿入ビュー: 1–0 / Q–P で挿入、BS/Del で絵文字削除、A/S/D/Z/X で再生操作、F で再生追従、Esc で終了";
    }

    private void ExitInsertView()
    {
        if (!InsertViewActive) return;

        // カーソルのあった行を行リストの選択に引き継ぐ
        int caretLine = ViewModel.MapInsertViewOffset(InsertEditor.SelectionStart) is var (li, _) ? li : -1;

        InsertView.Visibility = Visibility.Collapsed;
        NormalView.Visibility = Visibility.Visible;
        _insertFollow = false;
        UpdateFollowIndicator();
        if (caretLine >= 0) SelectLineAt(caretLine);
        LineEditor.Text = ViewModel.SelectedLine?.RawText ?? "";
        RenderPreview();
        ScheduleValidation();
        ViewModel.StatusText = "絵文字挿入ビューを終了しました";
    }

    private void OnInsertEditorPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool ctrl = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                     & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;

        if (ctrl)
        {
            switch (e.Key)
            {
                case Windows.System.VirtualKey.Z:
                    PerformUndo();
                    e.Handled = true;
                    return;
                case Windows.System.VirtualKey.Y:
                    PerformRedo();
                    e.Handled = true;
                    return;
                case Windows.System.VirtualKey.Enter:
                    TryRun(() =>
                    {
                        int? caret = ViewModel.SplitLineAtViewOffset(InsertEditor.SelectionStart);
                        if (caret is int c) RefreshInsertView(c);
                    });
                    ScheduleValidation();
                    e.Handled = true;
                    return;
                case Windows.System.VirtualKey.J:
                    TryRun(() =>
                    {
                        int? caret = ViewModel.JoinLineAtViewOffset(InsertEditor.SelectionStart);
                        if (caret is int c) RefreshInsertView(c);
                    });
                    ScheduleValidation();
                    e.Handled = true;
                    return;
            }
            return; // その他の Ctrl 組み合わせは既定処理へ
        }

        switch (e.Key)
        {
            case Windows.System.VirtualKey.Escape:
                EmojiModeToggle.IsChecked = false;
                e.Handled = true;
                return;
            case Windows.System.VirtualKey.Space:
                // プレースホルダ（＿）をアイコン同等のタイムタグ付きで挿入
                if (!string.IsNullOrEmpty(ViewModel.Settings.PlaceholderChar))
                {
                    InsertEmojiStringInView(ViewModel.Settings.PlaceholderChar);
                }
                e.Handled = true;
                return;
            case Windows.System.VirtualKey.Enter:
                // カーソル位置から再生
                TryRun(() =>
                {
                    if (ViewModel.FindTimeAtViewOffset(InsertEditor.SelectionStart) is not int cs)
                    {
                        ViewModel.StatusText = "カーソル位置以降にタイムタグがありません";
                        return;
                    }
                    var session = PlayerWithSource?.PlaybackSession;
                    if (session is null)
                    {
                        ViewModel.StatusText = "メディアが開かれていません（再生 > メディアを開く）";
                        return;
                    }
                    session.Position = TimeSpan.FromSeconds(ViewModel.TagCsToMediaSeconds(cs));
                    PlayerWithSource?.Play();
                    ViewModel.StatusText = $"カーソル位置 {TimeTag.Format(cs)} から再生します";
                });
                e.Handled = true;
                return;
            case Windows.System.VirtualKey.Back:
            case Windows.System.VirtualKey.Delete:
                TryRun(() =>
                {
                    int? caret = ViewModel.DeleteEmojiAtViewOffset(
                        InsertEditor.SelectionStart,
                        forward: e.Key == Windows.System.VirtualKey.Delete);
                    if (caret is int c) RefreshInsertView(c);
                });
                ScheduleValidation();
                e.Handled = true;
                return;
            case Windows.System.VirtualKey.F:
                _insertFollow = !_insertFollow;
                UpdateFollowIndicator();
                ViewModel.StatusText = _insertFollow
                    ? "再生追従カーソル ON: 再生中は次に歌われる文字の位置へカーソルが移動します"
                    : "再生追従カーソル OFF";
                e.Handled = true;
                return;
            case Windows.System.VirtualKey.A:
                PlayMedia();
                e.Handled = true;
                return;
            case Windows.System.VirtualKey.S:
                PauseMedia();
                e.Handled = true;
                return;
            case Windows.System.VirtualKey.D:
                TogglePlayPause();
                e.Handled = true;
                return;
            case Windows.System.VirtualKey.Z:
                SeekBy(-ViewModel.Settings.SeekSeconds);
                e.Handled = true;
                return;
            case Windows.System.VirtualKey.X:
                SeekBy(+ViewModel.Settings.SeekSeconds);
                e.Handled = true;
                return;
        }

        int slot = SlotFromKey(e.Key);
        if (slot > 0)
        {
            InsertEmojiInView(slot);
            e.Handled = true;
        }
    }

    private void InsertEmojiInView(int slot)
    {
        if (ViewModel.GetSlotEmojiString(slot) is not string emoji)
        {
            ViewModel.StatusText = $"スロット {ViewModels.EmojiSlotViewModel.KeyLabels[slot - 1]} は未設定です（絵文字 > 絵文字リスト編集）";
            return;
        }
        InsertEmojiStringInView(emoji);
    }

    private void UpdateFollowIndicator()
    {
        FollowIndicator.Text = _insertFollow ? "追従 ON (F で解除)" : "追従 OFF (F)";
        FollowIndicator.Foreground = _insertFollow
            ? (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["AccentTextFillColorPrimaryBrush"]
            : (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextFillColorSecondaryBrush"];
    }

    private void InsertEmojiStringInView(string emoji)
    {
        TryRun(() =>
        {
            int? caret = ViewModel.InsertEmojiAtViewOffset(InsertEditor.SelectionStart, emoji);
            if (caret is int c)
            {
                RefreshInsertView(c);
                ScheduleValidation();
            }
        });
    }

    /// <summary>
    /// 挿入ビューのテキストを組み立て直してカーソルを設定する。
    /// テキスト再構築で TextBox が勝手にスクロールするため、元のスクロール位置を復元する。
    /// </summary>
    private void RefreshInsertView(int caret)
    {
        var sv = FindDescendant<ScrollViewer>(InsertEditor);
        double? vOffset = sv?.VerticalOffset;
        double? hOffset = sv?.HorizontalOffset;

        SetInsertEditorText(ViewModel.BuildInsertViewText());
        InsertEditor.SelectionStart = Math.Min(Math.Max(0, caret), InsertEditor.Text.Length);

        if (sv is not null)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                sv.ChangeView(hOffset, vOffset, null, disableAnimation: true);
                EnsureInsertCaretMarginVisible();
            });
        }
    }

    /// <summary>カーソル行の下（上）に 2 行分の余白が見えるようにスクロールする。</summary>
    private void EnsureInsertCaretMarginVisible()
    {
        var sv = FindDescendant<ScrollViewer>(InsertEditor);
        if (sv is null || _insertViewText.Length == 0) return;

        int totalLines = 1;
        foreach (char c in _insertViewText)
        {
            if (c == '\r') totalLines++;
        }

        int caretPos = Math.Min(InsertEditor.SelectionStart, _insertViewText.Length);
        int caretLine = 0;
        for (int i = 0; i < caretPos; i++)
        {
            if (_insertViewText[i] == '\r') caretLine++;
        }

        double lineHeight = sv.ExtentHeight / totalLines;
        if (lineHeight <= 0 || sv.ViewportHeight <= 0) return;

        const int MarginLines = 2;
        double topNeeded = Math.Max(0, (caretLine - MarginLines) * lineHeight);
        double bottomNeeded = Math.Min(sv.ExtentHeight, (caretLine + 1 + MarginLines) * lineHeight);

        if (bottomNeeded > sv.VerticalOffset + sv.ViewportHeight)
        {
            sv.ChangeView(null, bottomNeeded - sv.ViewportHeight, null, disableAnimation: true);
        }
        else if (topNeeded < sv.VerticalOffset)
        {
            sv.ChangeView(null, topNeeded, null, disableAnimation: true);
        }
    }

    private void OnInsertEditorSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (!InsertViewActive) return;
        EnsureInsertCaretMarginVisible();
    }

    private static T? FindDescendant<T>(Microsoft.UI.Xaml.DependencyObject root) where T : Microsoft.UI.Xaml.DependencyObject
    {
        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T t) return t;
            if (FindDescendant<T>(child) is T found) return found;
        }
        return null;
    }

    // ------------------------------------------------------------ 絵文字

    private void OnEmojiSlotClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ViewModels.EmojiSlotViewModel slot) return;
        if (InsertViewActive)
        {
            InsertEmojiInView(slot.Slot);
            InsertEditor.Focus(FocusState.Programmatic);
        }
        else
        {
            InsertEmojiSlot(slot.Slot);
        }
    }

    private bool _suppressSlotReorder;

    private void OnPromoteEmojiClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is EmojiEntry entry)
        {
            TryRun(() => ViewModel.PromoteEmojiToSlot(entry));
        }
    }

    private void OnUnslottedEmojiClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not EmojiEntry entry || entry.ReplaceChar.Length == 0) return;
        if (InsertViewActive)
        {
            InsertEmojiStringInView(entry.ReplaceChar);
            InsertEditor.Focus(FocusState.Programmatic);
        }
        else
        {
            InsertEmojiStringIntoLineEditor(entry.ReplaceChar);
        }
    }

    private async void OnEmojiListClick(object sender, RoutedEventArgs e)
    {
        var dialog = new EmojiListDialog(ViewModel.Settings, ViewModel.Document) { XamlRoot = Content.XamlRoot };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            ViewModel.RefreshEmojiSlots();
            if (dialog.SongListChanged) ViewModel.MarkModified();
            ScheduleValidation();
        }
    }

    private void OnEmojiRetagClick(object sender, RoutedEventArgs e)
    {
        TryRun(ViewModel.RetagAllEmoji);
        LineEditor.Text = ViewModel.SelectedLine?.RawText ?? "";
        RenderPreview();
        ScheduleValidation();
    }

    /// <summary>キー → スロット番号（1–0 = 1..10, Q–P = 11..20）。0 = 該当なし。</summary>
    private static int SlotFromKey(Windows.System.VirtualKey key) => key switch
    {
        >= Windows.System.VirtualKey.Number1 and <= Windows.System.VirtualKey.Number9 => key - Windows.System.VirtualKey.Number0,
        Windows.System.VirtualKey.Number0 => 10,
        >= Windows.System.VirtualKey.NumberPad1 and <= Windows.System.VirtualKey.NumberPad9 => key - Windows.System.VirtualKey.NumberPad0,
        Windows.System.VirtualKey.NumberPad0 => 10,
        Windows.System.VirtualKey.Q => 11,
        Windows.System.VirtualKey.W => 12,
        Windows.System.VirtualKey.E => 13,
        Windows.System.VirtualKey.R => 14,
        Windows.System.VirtualKey.T => 15,
        Windows.System.VirtualKey.Y => 16,
        Windows.System.VirtualKey.U => 17,
        Windows.System.VirtualKey.I => 18,
        Windows.System.VirtualKey.O => 19,
        Windows.System.VirtualKey.P => 20,
        _ => 0,
    };

    private void OnLineEditorPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool ctrl = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                     & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;

        int slot = SlotFromKey(e.Key);
        if (slot == 0) return;

        // 行エディタでは Ctrl+数字（1–0）で挿入（素押しの挿入は絵文字挿入ビューで）
        if (ctrl && slot <= 10)
        {
            InsertEmojiSlot(slot);
            e.Handled = true;
        }
    }

    private void InsertEmojiSlot(int slot)
    {
        if (ViewModel.GetSlotEmojiString(slot) is not string emoji)
        {
            ViewModel.StatusText = $"スロット {ViewModels.EmojiSlotViewModel.KeyLabels[slot - 1]} は未設定です（絵文字 > 絵文字リスト編集）";
            return;
        }
        InsertEmojiStringIntoLineEditor(emoji);
    }

    private void InsertEmojiStringIntoLineEditor(string emoji)
    {
        if (ViewModel.SelectedLine is null)
        {
            ViewModel.StatusText = "行を選択してください";
            return;
        }

        TryRun(() =>
        {
            var result = ViewModel.InsertEmojiIntoRaw(LineEditor.Text, LineEditor.SelectionStart, emoji);
            if (result is not { } r) return;
            LineEditor.Text = r.NewRaw;
            LineEditor.SelectionStart = r.NewCursor;
            RenderPreview();
            ScheduleValidation();
        });
    }

    // ------------------------------------------------------------ プレビュー

    /// <summary>選択行のルビ・絵文字画像付きプレビューを描画する（適用中のフォントを使用）。</summary>
    private void RenderPreview()
    {
        PreviewPanel.Children.Clear();
        var line = ViewModel.SelectedLine?.Model;
        if (line is null) return;

        var previewFont = new Microsoft.UI.Xaml.Media.FontFamily(ViewModel.Settings.FontFamily);
        var previewWeight = ViewModel.Settings.FontBold
            ? Microsoft.UI.Text.FontWeights.Bold
            : Microsoft.UI.Text.FontWeights.Normal;

        var entryByString = ViewModel.GetEffectiveEmojiList()
            .Where(x => x.ReplaceChar.Length > 0)
            .GroupBy(x => x.ReplaceChar)
            .ToDictionary(g => g.Key, g => g.First());
        var matcher = ViewModel.CreateEmojiMatcher();
        var occurrences = matcher.FindOccurrences(line.Chars);
        int occIdx = 0;

        for (int i = 0; i < line.Chars.Count; i++)
        {
            // 絵文字の出現（複数文字の置き換え文字列）は 1 つの画像として描画
            if (occIdx < occurrences.Count && occurrences[occIdx].Start == i)
            {
                var occ = occurrences[occIdx];
                occIdx++;
                i = occ.EndExclusive - 1;

                if (entryByString.TryGetValue(occ.Value, out var emoji) && File.Exists(emoji.ImageBefore))
                {
                    // 絵文字リスト編集のプレビューと同じ式で実機同等のサイズ感にする:
                    // 高さ = フォントサイズ × Zoom%（プレビューの文字サイズ基準）、幅は縦横比、
                    // Fix は実寸相当、縁取り分だけ文字下端より下げる
                    const double previewFontPx = 20;
                    double pscale = previewFontPx / Math.Max(8, ViewModel.Settings.FontSizePx);
                    double edgePx = Math.Max(0, ViewModel.Settings.EdgeSizePx);
                    var opts = emoji.ParseOptions();
                    double iconW, iconH;
                    if (Core.Formats.ImageSizeReader.TryGetSize(emoji.ImageBefore, out int imgW, out int imgH) && imgW > 0 && imgH > 0)
                    {
                        if (opts.Fix)
                        {
                            iconW = imgW * pscale;
                            iconH = imgH * pscale;
                        }
                        else
                        {
                            iconH = previewFontPx * opts.ZoomPercent / 100.0;
                            iconW = iconH * imgW / imgH;
                        }
                    }
                    else
                    {
                        iconW = iconH = previewFontPx * opts.ZoomPercent / 100.0;
                    }
                    PreviewPanel.Children.Add(new Image
                    {
                        Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(emoji.ImageBefore)),
                        Width = iconW,
                        Height = iconH,
                        Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Margin = new Thickness(
                            Math.Max(0, opts.MarginLeft) * pscale, 0,
                            Math.Max(0, opts.MarginRight) * pscale,
                            (opts.MarginBottom - edgePx) * pscale),
                    });
                }
                else
                {
                    PreviewPanel.Children.Add(new TextBlock
                    {
                        Text = occ.Value,
                        FontSize = 20,
                        FontFamily = previewFont,
                        FontWeight = previewWeight,
                        VerticalAlignment = VerticalAlignment.Bottom,
                    });
                }
                continue;
            }

            var c = line.Chars[i];
            if (c.IsSpacer) continue;

            var cell = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Bottom };
            cell.Children.Add(new TextBlock
            {
                Text = c.Ruby ?? "",
                FontSize = 10,
                FontFamily = previewFont,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextFillColorSecondaryBrush"],
            });
            cell.Children.Add(new TextBlock
            {
                Text = c.Text,
                FontSize = 20,
                FontFamily = previewFont,
                FontWeight = previewWeight,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            PreviewPanel.Children.Add(cell);
        }
    }

    // ------------------------------------------------------------ 共通

    private void TryRun(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            ViewModel.StatusText = $"エラー: {ex.Message}";
        }
    }
}
