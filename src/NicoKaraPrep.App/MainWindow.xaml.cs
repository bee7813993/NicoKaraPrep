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
        _validateTimer.Tick += (_, _) =>
        {
            TryRun(ViewModel.RunValidation);
            RefreshInsertGutter(); // 挿入ビュー表示中なら横幅などの再計算結果を行情報欄へ反映
        };

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
        RefreshRecentFilesMenu();
        LoadQuickEmojiSettings();
        RebuildInsertKeyBindings();

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

        // デバッグ用: 起動直後に絵文字挿入ビューへ切り替える
        if (args.Contains("--debug-insert-view"))
        {
            var timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(1500);
            timer.IsRepeating = false;
            timer.Tick += (_, _) =>
            {
                DebugLog("絵文字挿入ビューを自動オープンします");
                EmojiModeToggle.IsChecked = true;
            };
            timer.Start();
        }

        // デバッグ用: 起動直後にキー割り当てダイアログを開く
        if (args.Contains("--debug-keybindings"))
        {
            var timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(1500);
            timer.IsRepeating = false;
            timer.Tick += (_, _) => OnKeyBindingsClick(this, new RoutedEventArgs());
            timer.Start();
        }

        // デバッグ用: 再チェック発火の検証シナリオ
        // （挿入ビュー→＿挿入→復帰→行エディタ適用→タブ分離→解除 を 1.5 秒間隔で自動実行）
        if (args.Contains("--debug-recheck"))
        {
            int step = 0;
            var timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(1500);
            timer.IsRepeating = true;
            timer.Tick += (_, _) =>
            {
                step++;
                DebugLog($"debug-recheck step {step}");
                switch (step)
                {
                    case 1:
                        EmojiModeToggle.IsChecked = true;
                        break;
                    case 2:
                        InsertEmojiStringInView(ViewModel.Settings.PlaceholderChar);
                        break;
                    case 3:
                        EmojiModeToggle.IsChecked = false;
                        break;
                    case 4:
                        SelectLineAt(0);
                        LineEditor.Text = (ViewModel.SelectedLine?.RawText ?? "") + "ああああああああああ";
                        ApplyLineEditor();
                        break;
                    case 5:
                        TryRun(() =>
                        {
                            var tab = ViewModel.SplitSelectedToNewTab(new List<int> { 0, 1 });
                            if (tab is not null)
                            {
                                ViewModel.SwitchTab(ViewModel.Tabs.IndexOf(tab));
                                SyncTabSelection();
                                RefreshAfterTabChange();
                            }
                        });
                        break;
                    case 6:
                        TryRun(ViewModel.ResetAllTabs);
                        SyncTabSelection();
                        RefreshAfterTabChange();
                        timer.Stop();
                        break;
                }
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
        RefreshRecentFilesMenu();
        if (ViewModel.MediaPath is string mp && File.Exists(mp))
        {
            OpenMedia(mp);
        }
    }

    private IntPtr Hwnd => WinRT.Interop.WindowNative.GetWindowHandle(this);

    // ------------------------------------------------------------ ファイル

    private async void OnNewClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.HasUnsavedChanges)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = "新規作成",
                Content = "保存されていない変更があります。破棄して新規作成（ファイルを閉じる）しますか？",
                PrimaryButtonText = "破棄して新規作成",
                CloseButtonText = "キャンセル",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        }

        if (InsertViewActive) EmojiModeToggle.IsChecked = false;
        TryRun(ViewModel.NewDocument);
        CloseMedia();
        LineEditor.Text = "";
        RenderPreview();
        ScheduleValidation();
    }

    /// <summary>メディアを閉じる（新規作成・ファイルを閉じる時）。</summary>
    private void CloseMedia()
    {
        _mediaTimer?.Stop();
        Player.Source = null;
        MediaPanel.IsExpanded = false;
    }

    /// <summary>「最近使用したファイル」サブメニューを設定から作り直す。</summary>
    private void RefreshRecentFilesMenu()
    {
        RecentFilesMenu.Items.Clear();
        var files = ViewModel.Settings.RecentFiles.Where(File.Exists).ToList();
        if (files.Count == 0)
        {
            RecentFilesMenu.Items.Add(new MenuFlyoutItem { Text = "（なし）", IsEnabled = false });
            return;
        }
        foreach (string path in files)
        {
            var item = new MenuFlyoutItem { Text = Path.GetFileName(path) };
            ToolTipService.SetToolTip(item, path);
            string captured = path;
            item.Click += (_, _) => OpenRecentFile(captured);
            RecentFilesMenu.Items.Add(item);
        }
    }

    private void OpenRecentFile(string path)
    {
        if (!File.Exists(path))
        {
            ViewModel.Settings.RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            ViewModel.Settings.Save();
            RefreshRecentFilesMenu();
            ViewModel.StatusText = $"ファイルが見つかりません: {path}";
            return;
        }
        TryRun(() => ViewModel.OpenFile(path));
        AfterDocumentLoaded();
        RefreshRecentFilesMenu();
    }

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
            RefreshRecentFilesMenu();
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
        RefreshRecentFilesMenu();
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

    /// <summary>
    /// 表示中のタブの内容だけを別ファイルへ保存する。
    /// デフォルト形式はニコカラメーカー向けの lrc（rlf を開いていても）。
    /// 前回の保存先があれば、そのフォルダ・ファイル名・形式を初期値にする。
    /// </summary>
    private void OnSaveTabAsClick(object sender, RoutedEventArgs e)
    {
        string suggested = ViewModel.GetSuggestedFileBaseName();
        string? folder = ViewModel.GetDefaultSaveFolder();
        string defaultExt = "lrc";
        if (ViewModel.GetActiveTabCopyPath() is string prev)
        {
            suggested = Path.GetFileNameWithoutExtension(prev);
            if (Path.GetDirectoryName(prev) is string prevDir && Directory.Exists(prevDir)) folder = prevDir;
            defaultExt = Path.GetExtension(prev).TrimStart('.').ToLowerInvariant() is "rlf" ? "rlf" : "lrc";
        }
        var types = defaultExt == "rlf"
            ? new[] { LyricsFileTypes[1], LyricsFileTypes[0], LyricsFileTypes[2] }
            : LyricsFileTypes;

        string? path = SaveFileDialog.Show(Hwnd, folder, suggested, types, defaultExt);
        if (path is null) return;
        TryRun(() => ViewModel.SaveActiveTabCopyTo(path));
    }

    /// <summary>表示中のタブを前回の保存先へ上書き保存する（未保存なら保存ダイアログへ）。</summary>
    private void OnSaveTabOverwriteClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.GetActiveTabCopyPath() is string path)
        {
            TryRun(() => ViewModel.SaveActiveTabCopyTo(path));
        }
        else
        {
            OnSaveTabAsClick(sender, e);
        }
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

        // n3proj は設定として、動画・音声ファイルはメディアとして、それ以外は歌詞として開く
        if (Path.GetExtension(file.Path).Equals(".n3proj", StringComparison.OrdinalIgnoreCase))
        {
            TryRun(() => ViewModel.ApplyN3ProjSettings(file.Path));
            ScheduleValidation();
        }
        else if (MediaExtensions.Contains(Path.GetExtension(file.Path)))
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
        // （歌詞ファイルと n3proj だけはそれぞれの読み込みとして扱う）
        string ext = Path.GetExtension(file.Path);
        if (ext.Equals(".n3proj", StringComparison.OrdinalIgnoreCase))
        {
            TryRun(() => ViewModel.ApplyN3ProjSettings(file.Path));
            ScheduleValidation();
        }
        else if (ext.Equals(".rlf", StringComparison.OrdinalIgnoreCase) ||
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

        // 挿入ビュー表示中は行情報欄の選択ハイライトも追従させる
        QueueGutterRefresh();
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
        LoadQuickEmojiSettings();
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

    private async void OnKeyBindingsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new KeyBindingsDialog(_insertKeys) { XamlRoot = Content.XamlRoot };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            ViewModel.Settings.InsertViewKeys = InsertViewKeyMap.ToStored(dialog.Result);
            ViewModel.Settings.Save();
            RebuildInsertKeyBindings();
            ViewModel.StatusText = "キー割り当てを保存しました（挿入ビュー上部の凡例に反映）";
        }
    }

    private async void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(ViewModel.Settings) { XamlRoot = Content.XamlRoot };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            LoadQuickEmojiSettings();
            TryRun(ViewModel.RunValidation);
        }
    }

    // ------------------------------------------ パレットの絵文字挿入クイック設定

    // コンパイル済み XAML はプロパティ設定前にイベントを接続するため、
    // 初期値の流し込みが終わるまで変更ハンドラを無効にする
    private bool _quickEmojiReady;

    /// <summary>パレットのクイック設定欄へ現在の設定値を反映する。</summary>
    private void LoadQuickEmojiSettings()
    {
        _quickEmojiReady = false;
        QuickEmojiLeadBox.Value = ViewModel.Settings.EmojiLeadSeconds;
        QuickLeadZeroToggle.IsChecked = ViewModel.Settings.EmojiLeadSeconds == 0;
        QuickEmojiModeBox.SelectedIndex = ViewModel.Settings.EmojiTagPerEmoji ? 0 : 1;
        QuickPlaceholderBox.Text = ViewModel.Settings.PlaceholderChar;
        _quickEmojiReady = true;
    }

    private void OnQuickEmojiLeadChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_quickEmojiReady || double.IsNaN(args.NewValue)) return;
        double v = Math.Clamp(args.NewValue, 0, 30);
        ViewModel.Settings.EmojiLeadSeconds = v;
        if (v > 0) ViewModel.Settings.EmojiLeadResumeSeconds = v; // 0秒トグル解除時の復元値
        ViewModel.Settings.Save();

        _quickEmojiReady = false;
        QuickLeadZeroToggle.IsChecked = v == 0;
        _quickEmojiReady = true;

        ViewModel.StatusText =
            $"絵文字の表示秒数を {v:0.#} 秒にしました（既存の絵文字へは 絵文字 > 絵文字タイムタグ再計算 で適用）";
    }

    /// <summary>「0秒」トグル: ワイプしない絵文字用に表示秒数を 0 ⇔ 元の秒数で切り替える。</summary>
    private void OnQuickLeadZeroToggled(object sender, RoutedEventArgs e)
    {
        if (!_quickEmojiReady) return;
        bool on = QuickLeadZeroToggle.IsChecked == true;
        double resume = ViewModel.Settings.EmojiLeadResumeSeconds;
        double v = on ? 0 : (resume > 0 ? resume : 2.0);

        ViewModel.Settings.EmojiLeadSeconds = v;
        if (v > 0) ViewModel.Settings.EmojiLeadResumeSeconds = v;
        ViewModel.Settings.Save();

        _quickEmojiReady = false;
        QuickEmojiLeadBox.Value = v;
        _quickEmojiReady = true;

        ViewModel.StatusText = on
            ? "絵文字の表示秒数を 0 にしました（ワイプしない絵文字用。もう一度押すと元の秒数へ戻ります）"
            : $"絵文字の表示秒数を {v:0.#} 秒へ戻しました";
    }

    private void OnQuickEmojiModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_quickEmojiReady) return;
        ViewModel.Settings.EmojiTagPerEmoji = QuickEmojiModeBox.SelectedIndex == 0;
        ViewModel.Settings.Save();
    }

    private void OnQuickPlaceholderChanged(object sender, TextChangedEventArgs e)
    {
        if (!_quickEmojiReady) return;
        ViewModel.Settings.PlaceholderChar = QuickPlaceholderBox.Text.Trim();
        ViewModel.Settings.Save();
        ScheduleValidation(); // プレースホルダは絵文字扱い（チェック除外）の対象に含まれるため
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

        // 挿入ビュー表示中にタブ操作（分離・移動・解除・切り替え）をした場合はビューを作り直す
        if (InsertViewActive)
        {
            SetInsertEditorText(ViewModel.BuildInsertViewText());
            InsertEditor.SelectionStart = 0;
            RefreshInsertGutter();
            UpdateInsertCursorInfo();
        }
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

    private void OnJoinLineClick(object sender, RoutedEventArgs e) => JoinLineFromMenu(insertSpace: false);

    private void OnJoinLineWithSpaceClick(object sender, RoutedEventArgs e) => JoinLineFromMenu(insertSpace: true);

    private void JoinLineFromMenu(bool insertSpace)
    {
        if (InsertViewActive) return; // 挿入ビューでは Ctrl+J / Ctrl+Shift+J を PreviewKeyDown で処理
        if (ViewModel.SelectedLine is null) return;
        int index = ViewModel.SelectedLine.Index;
        TryRun(() =>
        {
            if (ViewModel.JoinSelectedWithNext(insertSpace)) SelectLineAt(index);
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
        QueueGutterRefresh(); // 済マーク表示を更新
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
        QueueGutterRefresh(); // 済マーク表示を更新
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
        QueueGutterRefresh(); // 済マーク表示を更新
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
        RefreshInsertGutter();
        UpdateInsertCursorInfo();
        ViewModel.StatusText = "絵文字挿入ビュー: 1–0 / Q–P で挿入。キー操作は上部の凡例参照（ファイル > キー割り当て で変更可）、Esc で終了";
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

    // ------------------------------------------------ 挿入ビューのキー割り当て

    private Dictionary<InsertViewAction, string> _insertKeys = new();
    private Dictionary<Windows.System.VirtualKey, InsertViewAction> _insertKeyLookup = new();

    /// <summary>設定からキー割り当てを再構築し、凡例表示も更新する。</summary>
    private void RebuildInsertKeyBindings()
    {
        _insertKeys = InsertViewKeyMap.Normalize(ViewModel.Settings.InsertViewKeys);
        _insertKeyLookup = new Dictionary<Windows.System.VirtualKey, InsertViewAction>();
        foreach (var (action, keyId) in _insertKeys)
        {
            foreach (var vk in InsertViewKeyMap.ToVirtualKeys(keyId))
            {
                _insertKeyLookup[vk] = action;
            }
        }
        UpdateInsertLegend();
        UpdateFollowIndicator();
    }

    /// <summary>現在のキー割り当てから挿入ビューの凡例テキストを組み立てる。</summary>
    private void UpdateInsertLegend()
    {
        string K(InsertViewAction a) => InsertViewKeyMap.KeyLabel(_insertKeys[a]);
        InsertLegend.Text =
            $"1–0 / Q–P: 絵文字挿入　{K(InsertViewAction.PlaceholderInsert)}: ＿挿入　{K(InsertViewAction.LeadTagInsert)}: 先行タグ挿入　" +
            $"{K(InsertViewAction.SpaceInsert)}: 空白挿入（Shift+{K(InsertViewAction.SpaceInsert)}: 全角）　BS / Del: 絵文字・空白削除　" +
            $"{K(InsertViewAction.PlayPause)}: 再生/一時停止　{K(InsertViewAction.PlayFromCursor)}: カーソル位置から再生　" +
            $"{K(InsertViewAction.Play)}: 再生　{K(InsertViewAction.Pause)}: 一時停止　" +
            $"{K(InsertViewAction.SeekBack)} / {K(InsertViewAction.SeekForward)}: 数秒戻る / 進む　{K(InsertViewAction.FollowToggle)}: 再生追従　" +
            $"{K(InsertViewAction.SplitLine)}: 行分割　{K(InsertViewAction.JoinLine)}: 行結合（Shift+{K(InsertViewAction.JoinLine)}: スペースを挟む）　" +
            "Ctrl+Z / Y: 元に戻す / やり直し　Esc: 終了（キーは ファイル > キー割り当て で変更可）　" +
            "左の行情報クリック: 行選択（Shift+クリック: 範囲、右クリック: 分離・エクスポート等）";
    }

    /// <summary>割り当てられた機能を実行する。</summary>
    private void ExecuteInsertViewAction(InsertViewAction action, bool shift)
    {
        switch (action)
        {
            case InsertViewAction.PlaceholderInsert:
                if (!string.IsNullOrEmpty(ViewModel.Settings.PlaceholderChar))
                {
                    InsertEmojiStringInView(ViewModel.Settings.PlaceholderChar);
                }
                break;
            case InsertViewAction.LeadTagInsert:
                TryRun(() =>
                {
                    int? caret = ViewModel.InsertLeadTagAtViewOffset(InsertEditor.SelectionStart);
                    if (caret is int c) RefreshInsertView(c);
                });
                ScheduleValidation();
                break;
            case InsertViewAction.SpaceInsert:
                TryRun(() =>
                {
                    int? caret = ViewModel.InsertSpaceAtViewOffset(InsertEditor.SelectionStart, fullWidth: shift);
                    if (caret is int c) RefreshInsertView(c);
                });
                ScheduleValidation();
                break;
            case InsertViewAction.PlayPause:
                TogglePlayPause();
                break;
            case InsertViewAction.PlayFromCursor:
                PlayFromInsertCursor();
                break;
            case InsertViewAction.Play:
                PlayMedia();
                break;
            case InsertViewAction.Pause:
                PauseMedia();
                break;
            case InsertViewAction.SeekBack:
                SeekBy(-ViewModel.Settings.SeekSeconds);
                break;
            case InsertViewAction.SeekForward:
                SeekBy(+ViewModel.Settings.SeekSeconds);
                break;
            case InsertViewAction.FollowToggle:
                _insertFollow = !_insertFollow;
                UpdateFollowIndicator();
                ViewModel.StatusText = _insertFollow
                    ? "再生追従カーソル ON: 再生中は次に歌われる文字の位置へカーソルが移動します"
                    : "再生追従カーソル OFF";
                break;
            case InsertViewAction.SplitLine:
                SplitLineInView();
                break;
            case InsertViewAction.JoinLine:
                JoinLineInView(insertSpace: shift);
                break;
        }
    }

    private void OnInsertEditorPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool ctrl = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                     & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        bool shift = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
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
                    SplitLineInView();
                    e.Handled = true;
                    return;
                case Windows.System.VirtualKey.J:
                    JoinLineInView(insertSpace: shift);
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
        }

        // 割り当て可能キー（ファイル > キー割り当て で変更）
        if (_insertKeyLookup.TryGetValue(e.Key, out var action))
        {
            ExecuteInsertViewAction(action, shift);
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

    private void SplitLineInView()
    {
        TryRun(() =>
        {
            int? caret = ViewModel.SplitLineAtViewOffset(InsertEditor.SelectionStart);
            if (caret is int c) RefreshInsertView(c);
        });
        ScheduleValidation();
    }

    private void JoinLineInView(bool insertSpace)
    {
        TryRun(() =>
        {
            int? caret = ViewModel.JoinLineAtViewOffset(InsertEditor.SelectionStart, insertSpace);
            if (caret is int c) RefreshInsertView(c);
        });
        ScheduleValidation();
    }

    /// <summary>挿入ビューのカーソル位置のタイムタグからメディアを再生する。</summary>
    private void PlayFromInsertCursor()
    {
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
        string key = _insertKeys.TryGetValue(InsertViewAction.FollowToggle, out string? k)
            ? InsertViewKeyMap.KeyLabel(k)
            : "F";
        FollowIndicator.Text = _insertFollow ? $"追従 ON ({key} で解除)" : $"追従 OFF ({key})";
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
        RefreshInsertGutter();
        UpdateInsertCursorInfo();

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
        UpdateInsertCursorInfo();
    }

    // ------------------------------------------------ 挿入ビューの行情報・カーソル情報

    private ScrollViewer? _insertEditorScroll;

    /// <summary>エディタ内部の ScrollViewer に行情報欄のスクロール同期を仕込む。</summary>
    private void HookInsertEditorScroll()
    {
        if (_insertEditorScroll is not null) return;
        _insertEditorScroll = FindDescendant<ScrollViewer>(InsertEditor);
        if (_insertEditorScroll is null) return;
        _insertEditorScroll.ViewChanged += (_, _) =>
            InsertGutterScroll.ChangeView(null, _insertEditorScroll.VerticalOffset, null, disableAnimation: true);
        // ウィンドウリサイズ後もスクロール位置の同期を取り直す
        InsertEditor.SizeChanged += (_, _) => ScheduleInsertGutterScrollSync();
    }

    /// <summary>チェック結果の重要度 → 行情報欄の文字色（null は既定色のまま）。</summary>
    private static Microsoft.UI.Xaml.Media.Brush? GutterBrushFor(Core.Validation.IssueSeverity? severity) => severity switch
    {
        Core.Validation.IssueSeverity.Error => ViewModels.LineViewModel.ErrorTimeBrush,
        Core.Validation.IssueSeverity.Warning => ViewModels.LineViewModel.WarningTimeBrush,
        _ => null,
    };

    // プローブで測った「行テキスト → 行の実高さ」キャッシュ（同じ行の再測定を避ける）
    private readonly Dictionary<string, double> _gutterLinePitchCache = new();
    private double _gutterProbeH2 = double.NaN; // 「あ\rあ」2 行のときのプローブ高さ
    private double _gutterProbeBasePitch = 27;

    /// <summary>
    /// プローブ TextBox の高さ = 定数 + Σ各行の実高さ、の定数部分を測定する。
    /// 行によって実高さが微妙に違う（全角記号や特定の漢字がフォールバックフォントを
    /// 引いて行が高くなる）ため、均一な行送りではなく行ごとの実測で積む。
    /// </summary>
    private void EnsureGutterProbeBaseline()
    {
        if (!double.IsNaN(_gutterProbeH2)) return;
        GutterPitchProbe.Text = "あ";
        GutterPitchProbe.UpdateLayout();
        double h1 = GutterPitchProbe.ActualHeight;
        GutterPitchProbe.Text = "あ\rあ";
        GutterPitchProbe.UpdateLayout();
        double h2 = GutterPitchProbe.ActualHeight;
        GutterPitchProbe.Text = "";
        if (h1 < 4 || h2 <= h1 + 4) return; // レイアウト未確定（次回の再構築で再試行）
        _gutterProbeH2 = h2;
        _gutterProbeBasePitch = h2 - h1;
    }

    /// <summary>1 行分のテキストをプローブに入れて、その行のエディタ上の実高さを測る。</summary>
    private double MeasureGutterLinePitch(string lineText)
    {
        if (_gutterLinePitchCache.TryGetValue(lineText, out double cached)) return cached;

        // 対象行を 8 回繰り返して「あ」2 行で挟み、基準（あ 2 行）との差分 ÷ 8 を取る。
        // 単独で測ると空行などで複数行テキスト中の実高さと一致せず、1 回だけの測定では
        // 高さの量子化誤差（±1px 弱）が同じ行の繰り返しで相関して累積するため。
        const int Repeat = 8;
        var sb = new System.Text.StringBuilder("あ");
        for (int i = 0; i < Repeat; i++)
        {
            sb.Append('\r').Append(lineText);
        }
        sb.Append('\r').Append('あ');
        GutterPitchProbe.Text = sb.ToString();
        GutterPitchProbe.UpdateLayout();
        double h = GutterPitchProbe.ActualHeight;
        GutterPitchProbe.Text = "";

        double pitch = (h - _gutterProbeH2) / Repeat;
        if (pitch < 4) return _gutterProbeBasePitch;
        _gutterLinePitchCache[lineText] = pitch;
        return pitch;
    }

    /// <summary>
    /// 行情報欄の幅を想定最大の情報文字列から一度だけ決めて固定する。
    /// Auto 幅のままだと編集のたびに内容の桁数で列幅が伸縮し、
    /// 情報と歌詞の境目が動いて見づらくなるため。
    /// </summary>
    private void EnsureGutterFixedWidth()
    {
        if (!double.IsNaN(InsertGutterScroll.Width)) return;
        var tb = new TextBlock { FontSize = 12, Text = "00:00:00→00:00:00 8888px 888%" };
        tb.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        InsertGutterScroll.Width = Math.Ceiling(tb.DesiredSize.Width) + 8;
    }

    /// <summary>行情報欄（開始→終了時刻・横幅）をドキュメントの現在内容から作り直す。</summary>
    private void RefreshInsertGutter()
    {
        if (!InsertViewActive) return;
        EnsureGutterFixedWidth();
        EnsureGutterProbeBaseline();

        var defaultBrush = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextFillColorSecondaryBrush"];
        var children = InsertGutterStack.Children;
        children.Clear();

        for (int index = 0; index < ViewModel.Lines.Count; index++)
        {
            var l = ViewModel.Lines[index];

            // エディタに実際に表示される文字列（行末空白の可視化込み）で高さを測る
            string lineText = l.Model.IsEmpty
                ? ""
                : ViewModels.MainViewModel.FormatInsertViewLine(l.Model.GetDisplayText());
            double pitch = double.IsNaN(_gutterProbeH2) ? _gutterProbeBasePitch : MeasureGutterLinePitch(lineText);

            var row = new TextBlock
            {
                FontSize = 12,
                Foreground = defaultBrush,
                TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis, // 固定幅からはみ出す場合は省略
                VerticalAlignment = VerticalAlignment.Top,
            };

            void AddRun(string text, Microsoft.UI.Xaml.Media.Brush? brush)
            {
                if (text.Length == 0) return;
                var run = new Microsoft.UI.Xaml.Documents.Run { Text = text };
                if (brush is not null) run.Foreground = brush;
                row.Inlines.Add(run);
            }

            if (!l.Model.IsEmpty)
            {
                if (l.Exported)
                {
                    AddRun("✓", null); // エクスポート済み
                }
                // 時間はページ衝突チェック、横幅は横幅チェックの重要度で色分け（通常ビューの列と同じ規則）
                if (l.TimeText.Length > 0 || l.EndTimeText.Length > 0)
                {
                    AddRun(l.TimeText, GutterBrushFor(l.StartTimeSeverity));
                    AddRun("→", null);
                    AddRun(l.EndTimeText, GutterBrushFor(l.EndTimeSeverity));
                }
                if (l.WidthText.Length > 0)
                {
                    // マージン不足・はみ出しは重要度の色、それ以外でも使用率 90% 超なら警告色で予告
                    var widthBrush = GutterBrushFor(l.WidthSeverity)
                        ?? (l.WidthUsagePercent > 90 ? ViewModels.LineViewModel.WarningTimeBrush : null);
                    AddRun(" ", null);
                    AddRun(l.WidthText, widthBrush);
                }
            }

            // クリックで行選択できるようにホストの Border で包む（透明背景はヒットテスト用）
            var host = new Border
            {
                Height = pitch,
                Background = l.IsSelected ? GutterSelectedBrush : GutterTransparentBrush,
                Child = row,
                Tag = index,
            };
            host.Tapped += OnGutterRowTapped;
            host.RightTapped += OnGutterRowRightTapped;
            children.Add(host);
        }
        // 末尾の余白はエディタ下端（横スクロールバー分）とのずれ吸収用
        children.Add(new Border { Height = 60 });

        ScheduleInsertGutterScrollSync();
    }

    // ------------------------------------------ 挿入ビューの行選択（行情報欄クリック）

    private static readonly Microsoft.UI.Xaml.Media.Brush GutterSelectedBrush =
        new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x30, 0x00, 0x99, 0xFF));

    private static readonly Microsoft.UI.Xaml.Media.Brush GutterTransparentBrush =
        new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);

    private int _gutterAnchorIndex = -1;
    private bool _gutterRefreshQueued;
    private MenuFlyout? _gutterContextMenu;

    /// <summary>行情報欄の再構築を 1 フレームにまとめて予約する（選択変更の連発対策）。</summary>
    private void QueueGutterRefresh()
    {
        if (!InsertViewActive || _gutterRefreshQueued) return;
        _gutterRefreshQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _gutterRefreshQueued = false;
            RefreshInsertGutter();
        });
    }

    /// <summary>行情報欄クリック: 行選択をトグル（Shift+クリックで範囲選択）。</summary>
    private void OnGutterRowTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not int index) return;
        if (index < 0 || index >= ViewModel.Lines.Count) return;

        bool shift = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
                      & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;

        if (shift && _gutterAnchorIndex >= 0 && _gutterAnchorIndex < ViewModel.Lines.Count)
        {
            int lo = Math.Min(_gutterAnchorIndex, index);
            int hi = Math.Max(_gutterAnchorIndex, index);
            for (int i = lo; i <= hi; i++)
            {
                var vm = ViewModel.Lines[i];
                if (!LineList.SelectedItems.Contains(vm)) LineList.SelectedItems.Add(vm);
            }
        }
        else
        {
            var vm = ViewModel.Lines[index];
            if (LineList.SelectedItems.Contains(vm))
            {
                LineList.SelectedItems.Remove(vm);
            }
            else
            {
                LineList.SelectedItems.Add(vm);
            }
            _gutterAnchorIndex = index;
        }

        int count = LineList.SelectedItems.Count;
        ViewModel.StatusText = count == 0
            ? "行の選択を解除しました"
            : $"{count} 行選択中（右クリックで分離・エクスポートなどの操作）";
        e.Handled = true;
    }

    /// <summary>行情報欄の右クリック: 未選択行ならその行だけを選択してから行操作メニューを出す。</summary>
    private void OnGutterRowRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not int index) return;
        if (index < 0 || index >= ViewModel.Lines.Count) return;

        var vm = ViewModel.Lines[index];
        if (!LineList.SelectedItems.Contains(vm))
        {
            LineList.SelectedItems.Clear();
            LineList.SelectedItems.Add(vm);
            _gutterAnchorIndex = index;
        }

        _gutterContextMenu ??= BuildGutterContextMenu();
        _gutterContextMenu.ShowAt((FrameworkElement)sender, e.GetPosition((UIElement)sender));
        e.Handled = true;
    }

    /// <summary>挿入ビュー用の行操作メニュー（通常ビューの右クリックメニュー相当）。</summary>
    private MenuFlyout BuildGutterContextMenu()
    {
        var menu = new MenuFlyout();

        var split = new MenuFlyoutItem { Text = "選択行を新しいタブへ分離" };
        split.Click += OnSplitToTabClick;
        menu.Items.Add(split);

        var moveSub = new MenuFlyoutSubItem { Text = "選択行を既存のタブへ移動" };
        menu.Items.Add(moveSub);
        menu.Opening += (_, _) => PopulateMoveToTabItems(moveSub.Items);

        menu.Items.Add(new MenuFlyoutSeparator());

        var export = new MenuFlyoutItem { Text = "選択行をエクスポート..." };
        export.Click += OnExportFileClick;
        menu.Items.Add(export);

        var exportClip = new MenuFlyoutItem { Text = "選択行をクリップボードへエクスポート" };
        exportClip.Click += OnExportClipboardClick;
        menu.Items.Add(exportClip);

        menu.Items.Add(new MenuFlyoutSeparator());

        var selectUnexported = new MenuFlyoutItem { Text = "未エクスポート行をすべて選択" };
        selectUnexported.Click += OnSelectUnexportedClick;
        menu.Items.Add(selectUnexported);

        var clearMarks = new MenuFlyoutItem { Text = "選択行の済マークを解除" };
        clearMarks.Click += OnClearMarksClick;
        menu.Items.Add(clearMarks);

        var clearSelection = new MenuFlyoutItem { Text = "選択をすべて解除" };
        clearSelection.Click += (_, _) => LineList.SelectedItems.Clear();
        menu.Items.Add(clearSelection);

        return menu;
    }

    /// <summary>行情報欄上のホイールはエディタ側をスクロールさせる（同期ずれ防止）。</summary>
    private void OnGutterWheel(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        HookInsertEditorScroll();
        if (_insertEditorScroll is { } sv)
        {
            int delta = e.GetCurrentPoint(InsertGutterScroll).Properties.MouseWheelDelta;
            sv.ChangeView(null, sv.VerticalOffset - delta, null, disableAnimation: true);
        }
        e.Handled = true;
    }

    private bool _gutterSyncPending;

    /// <summary>エディタのレイアウト確定後に行情報欄のスクロール位置を合わせる。</summary>
    private void ScheduleInsertGutterScrollSync()
    {
        if (_gutterSyncPending) return;
        _gutterSyncPending = true;

        void Handler(object? s, object e)
        {
            InsertEditor.LayoutUpdated -= Handler;
            _gutterSyncPending = false;
            HookInsertEditorScroll();
            if (_insertEditorScroll is { } sv)
            {
                InsertGutterScroll.ChangeView(null, sv.VerticalOffset, null, disableAnimation: true);
            }
        }
        InsertEditor.LayoutUpdated += Handler;
    }

    /// <summary>カーソル位置の行番号と直前・直後のタイムタグを下部バーに表示する。</summary>
    private void UpdateInsertCursorInfo()
    {
        int offset = InsertEditor.SelectionStart;
        var (prev, next) = ViewModel.GetTagTimesAroundViewOffset(offset);
        static string F(int? cs) => cs is int c ? TimeTag.Format(c).Trim('[', ']') : "なし";
        InsertCursorInfo.Text = ViewModel.MapInsertViewOffset(offset) is var (li, _)
            ? $"行 {li + 1}　カーソル位置のタグ ─ 直前: {F(prev)}　直後: {F(next)}"
            : "";
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
