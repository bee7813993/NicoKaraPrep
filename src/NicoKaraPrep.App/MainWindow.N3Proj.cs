using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NicoKaraPrep.App.Services;
using NicoKaraPrep.App.ViewModels;
using NicoKaraPrep.App.Views;
using NicoKaraPrep.Core;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App;

/// <summary>ニコカラメーカー3 プロジェクト書き出しと、行ごとの表示時刻・フォント指定パネル。</summary>
public sealed partial class MainWindow
{
    private static readonly (string Label, string Pattern)[] N3ProjFileTypes =
    [
        ("ニコカラメーカー3 プロジェクト (*.n3proj)", "*.n3proj"),
    ];

    private bool _n3PanelLoading;
    private string? _n3FontNamesKey;
    private string? _n3LayoutNamesKey;

    /// <summary>行設定のレイアウトの欄の「自動」の項目。</summary>
    private const string AutoLayoutItem = "（自動）";

    // ------------------------------------------------------------ メニュー

    private async void OnExportN3ProjClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.GetN3ProjExportTabs().Count == 0)
        {
            ViewModel.StatusText = "書き出す歌詞がありません";
            return;
        }

        var dialog = new N3ProjExportDialog(ViewModel) { XamlRoot = Content.XamlRoot };
        var choice = await dialog.ShowAsync();
        if (choice == ContentDialogResult.None) return;
        var settings = dialog.Result;

        if (choice == ContentDialogResult.Secondary)
        {
            // 「適用」: 書き出さずに設定（ベース・タブごとの設定など）だけ保存し、プレビュー・行設定パネル・チェックを作り直す
            TryRun(() => ViewModel.ApplyN3ProjSongSettings(settings));
            ViewModel.StatusText = "書き出しの設定を適用しました（n3proj は書き出していません）";
        }
        else
        {
            // 保存先を選ばずにやめたときは何も保存しない（書き出し画面の設定を残すときは「適用」）
            string? suggestedPath = ViewModel.SuggestN3ProjOutputPath();
            string? folder = Path.GetDirectoryName(suggestedPath ?? "") is { Length: > 0 } d ? d : ViewModel.GetDefaultSaveFolder();
            string suggested = Path.GetFileNameWithoutExtension(suggestedPath ?? "lyrics");
            string? path = SaveFileDialog.Show(Hwnd, folder, suggested, N3ProjFileTypes, "n3proj");
            if (path is null)
            {
                ViewModel.StatusText = "n3proj は書き出しませんでした";
            }
            else
            {
                TryRun(() => ViewModel.ExportN3Proj(path, settings));
            }
        }
        _n3FontNamesKey = null;
        RefreshN3LinePanel();
        // 表示時刻の設定で字幕のプレビュー・チェックの結果が、書き出し設定（ベース・既定のフォント設定・合わせるか）で行に当たるフォント設定が変わることがある。
        // チェックはすぐに実行し、書き出しの知らせがチェック結果で消えないよう、つなげて表示する
        // （全画面ビュー（フォント設定・レイアウト設定）ではチェックしない。戻るときにチェックし直す。
        // レイアウト設定ビューは、ベース・タブの固定レイアウトが替わるとレイアウトの並びが変わるので一覧を作り直す）
        if (FullScreenViewActive)
        {
            TryRun(ViewModel.UpdateLineFonts);
            NotifyLayoutViewDocumentChanged();
        }
        else
        {
            string summary = ViewModel.StatusText;
            _validateTimer.Stop();
            TryRun(ViewModel.RunValidation);
            RefreshInsertGutter();
            ViewModel.StatusText = $"{summary}　／　{ViewModel.StatusText}";
        }
        RefreshLineFontPlaceholder();
    }

    /// <summary>
    /// ニコカラメーカー3 プロジェクトの読み込み（メニュー・ドラッグ＆ドロップ・フォント設定ビューの取り込みの共通入口）。
    /// 内容を調べて確認画面を出し、選んだ項目だけを取り込む。path が null ならファイルを選ばせる。
    /// フォント設定ビューの取り込み以外では、確認画面の前にプロジェクトの歌詞ファイルも開く（<see cref="OpenProjectLyricsAsync"/>）。
    /// </summary>
    private async Task ImportN3ProjAsync(string? path, N3ProjImportFocus focus = N3ProjImportFocus.Default)
    {
        try
        {
            if (path is null)
            {
                // ファイルを選んだのに何も起きない場合と区別できるよう、選ばれなかったことも表示する
                path = await PickFileAsync([".n3proj"], "ファイルが選ばれなかったため、ニコカラメーカー3 プロジェクトは読み込みませんでした");
                if (path is null) return;
            }
            DebugLog($"n3proj の読み込み: {path}");

            N3ProjImportPreview preview;
            try
            {
                preview = ViewModel.PrepareN3ProjImport(path);
            }
            catch (Exception ex)
            {
                // 読めなかったときは何も起きないように見えないよう、画面で知らせる
                string detail = ErrorText.Describe(ex);
                ViewModel.StatusText = $"エラー: {detail}";
                await ShowMessageAsync("ニコカラメーカー3 プロジェクトを読み込めませんでした", $"{Path.GetFileName(path)}\n\n{detail}");
                return;
            }

            // 過去にニコカラメーカー3 で作ったプロジェクトを開いたときは、そのプロジェクトの歌詞も開く
            // （開いた歌詞と行を照らし合わせるので、行ごとの表示時刻もそのまま取り込める）
            string? lyricsNote = null;
            if (focus == N3ProjImportFocus.Default)
            {
                var (proceed, note) = await OpenProjectLyricsAsync(preview);
                if (!proceed) return;
                lyricsNote = note;
            }

            var dialog = new N3ProjImportDialog(ViewModel, preview, focus) { XamlRoot = Content.XamlRoot };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                if (lyricsNote is not null) ViewModel.StatusText = $"{lyricsNote}（プロジェクトの設定は読み込みませんでした）";
                return;
            }

            var choices = dialog.Result;
            TryRun(() => ViewModel.ApplyN3ProjImport(preview, choices));
            AfterN3ProjImported(path, choices, lyricsNote, addRecent: focus == N3ProjImportFocus.Default);
        }
        catch (Exception ex)
        {
            DebugLog($"n3proj の読み込みで例外: {ex}");
            string detail = ErrorText.Describe(ex);
            ViewModel.StatusText = $"エラー: {detail}";
            await ShowMessageAsync("ニコカラメーカー3 プロジェクトの読み込み中にエラーが発生しました",
                $"{(path is null ? "" : Path.GetFileName(path) + "\n\n")}{detail}");
        }
    }

    /// <summary>
    /// n3proj の読み込み（選んだ項目の取り込み）のあとの画面の作り直し（メニューの読み込み確認画面と MCP の import_n3proj で共通）。
    /// addRecent なら最近使用したファイルに入れる。チェックはすぐに実行し、何を読み込んだかの表示がチェック結果で消えないよう、つなげて表示する。
    /// </summary>
    private void AfterN3ProjImported(string path, N3ProjImportChoices choices, string? lyricsNote, bool addRecent)
    {
        if (addRecent)
        {
            // 最近使用したファイルにも入れる（開いたプロジェクトの歌詞より上に。選ぶとこの読み込みをもう一度行う）
            ViewModel.Settings.AddRecentFile(path);
            ViewModel.Settings.Save();
            RefreshRecentFilesMenu();
        }
        string summary = lyricsNote is null ? ViewModel.StatusText : $"{lyricsNote}　／　{ViewModel.StatusText}";
        if (choices.Media && ViewModel.MediaPath is string media && File.Exists(media))
        {
            OpenMedia(media);
        }
        foreach (var line in ViewModel.Lines) line.RaiseOverrideMark();
        _n3FontNamesKey = null;
        LoadQuickEmojiSettings();
        RenderPreview();
        RefreshN3LinePanel();

        _validateTimer.Stop();
        TryRun(ViewModel.RunValidation);
        RefreshInsertGutter();
        RefreshLineFontPlaceholder();
        ViewModel.StatusText = $"{summary}　／　{ViewModel.StatusText}";
        NotifyLayoutViewDocumentChanged(); // レイアウト設定ビューを開いたまま読み込んだとき（ベース・ページの指定・字幕アクションが変わる）
    }

    /// <summary>
    /// n3proj を開いたとき、そのプロジェクトのメインの歌詞ファイル（最初の歌詞設定タブの歌詞ファイル）も開く。
    /// 歌詞を開いていなければそのまま開き、別の歌詞を開いていれば開くか尋ねる。同じ曲の歌詞（同じ名前の rlf なども）を
    /// 開いていればそのまま使う。尋ねた画面でキャンセルされたら Proceed = false（読み込みをやめる）。
    /// コーラスなど 2 つ目以降の歌詞設定の歌詞は、それぞれ自分のファイルを持つタブとして開く
    /// （上書き保存でそれぞれのファイルへ保存し、メインの歌詞ファイルにはまとめない）。「設定だけ読み込む」では開かない。
    /// Note は歌詞を開いた・開けなかったことの説明（ステータスバーに出す）。
    /// </summary>
    private async Task<(bool Proceed, string? Note)> OpenProjectLyricsAsync(N3ProjImportPreview preview)
    {
        if (preview.Tabs.FirstOrDefault() is not { } main) return (true, null); // 歌詞の無いプロジェクト
        string? lyrics = N3ProjImport.FindLyricsFile(preview.Path, main);
        string? current = ViewModel.MainFilePath;
        bool blank = ViewModel.IsDocumentBlank;
        if (lyrics is null)
        {
            // 歌詞を開いていないときだけ知らせる（開いている歌詞に設定を読み込むときは、今までどおり）
            string name = Path.GetFileName(main.LyricsRelativePath ?? main.LyricsPath ?? "");
            return (true, blank ? $"このプロジェクトの歌詞ファイル{(name.Length > 0 ? $"（{name}）" : "")}が見つからないため、歌詞は開きませんでした" : null);
        }
        if (current is not null && N3ProjImport.IsSameLyrics(current, lyrics)) return (true, OpenProjectExtraTabs(preview));

        if (!blank)
        {
            string currentName = current is null ? "（無題）" : Path.GetFileName(current);
            bool unsaved = ViewModel.HasUnsavedChanges;
            var ask = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = "プロジェクトの歌詞も開きますか？",
                Content = new TextBlock
                {
                    Text = $"このプロジェクトの歌詞ファイル「{Path.GetFileName(lyrics)}」も開きますか？今開いている「{currentName}」は閉じます。" +
                           (unsaved ? "\n開いている歌詞には保存していない変更があります。歌詞も開くと、その変更は失われます。" : "") +
                           "\n\n「設定だけ読み込む」では、今開いている歌詞にプロジェクトの設定を読み込みます。",
                    TextWrapping = TextWrapping.Wrap,
                },
                PrimaryButtonText = "歌詞も開く",
                SecondaryButtonText = "設定だけ読み込む",
                CloseButtonText = "キャンセル",
                DefaultButton = unsaved ? ContentDialogButton.Secondary : ContentDialogButton.Primary,
            };
            var answer = await ask.ShowAsync();
            if (answer == ContentDialogResult.None) return (false, null);
            if (answer == ContentDialogResult.Secondary) return (true, null);
        }

        try
        {
            ViewModel.OpenFile(lyrics, autoImportNearby: false);
        }
        catch (Exception ex)
        {
            DebugLog($"プロジェクトの歌詞を開けませんでした: {lyrics}: {ex}");
            string detail = ErrorText.Describe(ex);
            ViewModel.StatusText = $"エラー: {detail}";
            await ShowMessageAsync("プロジェクトの歌詞を開けませんでした", $"{lyrics}\n\n{detail}");
            return (true, $"プロジェクトの歌詞 {Path.GetFileName(lyrics)} を開けませんでした");
        }
        AfterDocumentLoaded();

        string opened = $"プロジェクトの歌詞 {Path.GetFileName(lyrics)} を開きました（{ViewModel.Lines.Count} 行）";
        return (true, OpenProjectExtraTabs(preview) is string extra ? $"{opened}　{extra}" : opened);
    }

    /// <summary>
    /// n3proj の 2 つ目以降の歌詞設定の歌詞をタブで開き、開いた・開けなかったことの説明を返す（何もなければ null）。
    /// </summary>
    private string? OpenProjectExtraTabs(N3ProjImportPreview preview)
    {
        var (opened, missing) = ViewModel.OpenProjectExtraTabs(preview);
        if (opened.Count > 0)
        {
            SyncTabSelection();
            ScheduleValidation(); // プレビュー・行リストに新しいタブの行を出す
        }
        var parts = new List<string>();
        if (opened.Count > 0) parts.Add($"{string.Join("・", opened)} の歌詞をタブで開きました（上書き保存でそれぞれのファイルへ保存します）");
        if (missing.Count > 0) parts.Add($"{string.Join("・", missing)} の歌詞ファイルが見つからないため開いていません");
        return parts.Count > 0 ? string.Join("　", parts) : null;
    }

    /// <summary>
    /// ファイル選択画面で 1 つ選ばせ、そのパスを返す（選ばなかったときは null。cancelStatus があればステータスバーに出す）。
    /// 選んだファイルを受け取れなかったときは、理由をメッセージで知らせて null を返す。
    /// </summary>
    private async Task<string?> PickFileAsync(IReadOnlyList<string> extensions, string? cancelStatus = null)
    {
        Windows.Storage.StorageFile? file;
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, Hwnd);
            foreach (string ext in extensions) picker.FileTypeFilter.Add(ext);
            file = await picker.PickSingleFileAsync();
        }
        catch (Exception ex)
        {
            // 一時フォルダの中など一部のフォルダのファイルは、選んでも受け取れず COMException（メッセージなし）になる
            DebugLog($"ファイル選択画面で例外: {ex}");
            string detail = ErrorText.Describe(ex);
            ViewModel.StatusText = $"エラー: 選んだファイルを受け取れませんでした。{detail}";
            await ShowMessageAsync("選んだファイルを開けませんでした",
                "ファイル選択画面から、選んだファイルを受け取れませんでした。\n" +
                "一時フォルダ（Temp）の中など、一部のフォルダにあるファイルで起きることがあります。" +
                "ファイルを別のフォルダ（ドキュメントなど）にコピーしてから開いてください。\n\n" +
                $"詳細: {detail}");
            return null;
        }
        if (file is null)
        {
            if (cancelStatus is not null) ViewModel.StatusText = cancelStatus;
            return null;
        }
        if (string.IsNullOrEmpty(file.Path))
        {
            ViewModel.StatusText = $"エラー: {file.Name} の場所（パス）を取得できませんでした";
            await ShowMessageAsync("選んだファイルを開けませんでした",
                $"{file.Name}\n\n選んだファイルの場所（パス）を取得できませんでした。ファイルを別のフォルダ（ドキュメントなど）にコピーしてから開いてください。");
            return null;
        }
        return file.Path;
    }

    /// <summary>OK だけのメッセージ画面を出す（出せない場合はステータスバーのみ）。</summary>
    private async Task ShowMessageAsync(string title, string message)
    {
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = title,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                CloseButtonText = "OK",
                DefaultButton = ContentDialogButton.Close,
            };
            await dialog.ShowAsync();
        }
        catch (Exception)
        {
            // ほかのダイアログが開いているなど。ステータスバーの表示だけにする
        }
    }

    private void OnClearN3OverridesClick(object sender, RoutedEventArgs e)
    {
        if (LineOperationBlocked()) return;
        var indexes = SelectedIndexes;
        if (indexes.Count == 0)
        {
            ViewModel.StatusText = "行を選択してください";
            return;
        }
        TryRun(() =>
        {
            int n = ViewModel.ClearLineOverrides(indexes);
            ViewModel.StatusText = n > 0 ? $"{n} 行の表示時刻・フォント・レイアウト・文字の大きさ・字幕アクションの指定を解除しました" : "手動指定のある行はありません";
        });
        foreach (var line in ViewModel.Lines) line.RaiseOverrideMark();
        RefreshN3LinePanel();
        LineSide.RefreshPagePane();
        ScheduleValidation();
    }

    // ------------------------------------------------------------ 行設定パネル

    /// <summary>選択行のニコカラメーカー用行設定（表示時刻・フォント）をパネルへ表示する。</summary>
    private void RefreshN3LinePanel()
    {
        _showBeginPrefill = null;
        _showEndPrefill = null;
        _n3PanelLoading = true;
        try
        {
            var line = ViewModel.SelectedLine;
            bool enabled = line is not null && !line.Model.IsEmpty;
            N3LinePanel.IsHitTestVisible = enabled;
            N3LinePanel.Opacity = enabled ? 1 : 0.5;
            if (line is null || !enabled)
            {
                ShowBeginBox.Text = "";
                ShowEndBox.Text = "";
                ShowBeginBox.PlaceholderText = "--:--:--";
                ShowEndBox.PlaceholderText = "--:--:--";
                LineFontBox.Text = "";
                SetN3LineInfo("");
                LineFontLabel.Text = "フォント";
                LineLayoutBox.SelectedIndex = -1;
                LineLayoutBox.PlaceholderText = "（自動）";
                EnsureLineActionItems();
                LineActionBox.SelectedIndex = -1;
                LineActionBox.PlaceholderText = DefaultLineActionItem;
                PageFontSizeBox.Value = 0;
                UpdateSideTarget();
                RefreshLineFontPlaceholder();
                return;
            }

            var model = line.Model;
            var plans = ViewModel.PlanShowTimes();
            plans.TryGetValue(line.Index, out var plan);
            // いま自動調整を実行したら決まる表示時刻（読み込んだ値・自動調整の値を持つ行の説明に使う）
            N3LinePlan? freshPlan = null;
            ViewModel.PlanShowTimesFresh()?.TryGetValue(line.Index, out freshPlan);

            // 欄の文字は手で指定した値だけ。読み込んだ値・自動調整の値・自動計算は薄字で出す
            ShowBeginBox.Text = model.HasManualShowBegin ? FmtCs(model.ShowBeginCs!.Value) : "";
            ShowEndBox.Text = model.HasManualShowEnd ? FmtCs(model.ShowEndCs!.Value) : "";
            ShowBeginBox.PlaceholderText = plan is not null ? FmtMs(plan.BeginMs) : "--:--:--";
            ShowEndBox.PlaceholderText = plan is not null ? FmtMs(plan.EndMs) : "--:--:--";

            EnsureN3FontNames();
            LineFontBox.Text = model.FontSetName ?? "";
            RefreshLineFontForSelection();
            RefreshLineLayoutBox(line);
            RefreshLineActionBox();
            PageFontSizeBox.Value = ViewModel.PageFontSizeDelta(line.Index);

            if (plan is null)
            {
                SetN3LineInfo("タイムタグが無いため表示時刻を計算できません");
            }
            else
            {
                string row = $"{(ViewModel.Settings.CollisionAlignFromTop ? "上" : "下")}から{plan.Row}行目";
                // 読み込んだ値・自動調整の値を持つ行は、いま自動調整を実行し直しても同じなら、規則の説明を実行したときの計算で書く。
                // 違うなら「実行し直すと …」を添える
                bool recomputable = (model.ShowBeginCs is not null && model.ShowBeginOrigin != ShowTimeOrigin.Manual) ||
                                    (model.ShowEndCs is not null && model.ShowEndOrigin != ShowTimeOrigin.Manual);
                bool same = freshPlan is not null && Math.Abs(freshPlan.BeginMs - plan.BeginMs) <= 5 && Math.Abs(freshPlan.EndMs - plan.EndMs) <= 5;
                var shown = recomputable && same ? freshPlan! : plan;
                string adjusted = shown.Adjusted ? "・前後ページに合わせて調整" : "";
                string yielded = ViewModel.DescribeEmojiLeadYield(shown, plans, byRule: !recomputable || same); // 絵文字の分だけ遅らせた行・絵文字を縮めた行
                string outdated = recomputable && freshPlan is not null && !same
                    ? $"・実行し直すと {FmtMs(freshPlan.BeginMs)} 〜 {FmtMs(freshPlan.EndMs)}"
                    : "";
                string label = ViewModels.MainViewModel.ShowTimeOriginLabel(model); // 自動・読み込み・自動調整・手動
                SetN3LineInfo($"{label}: {FmtMs(plan.BeginMs)} 〜 {FmtMs(plan.EndMs)}　ページ{plan.PageIndex + 1}・{row}{adjusted}{yielded}{outdated}");
            }
        }
        finally
        {
            _n3PanelLoading = false;
        }
    }

    /// <summary>行設定の右の説明（自動の表示時刻など）。欄が狭いと … で切れるので、全文をツールチップにも出す。</summary>
    private void SetN3LineInfo(string text)
    {
        N3LineInfo.Text = text;
        ToolTipService.SetToolTip(N3LineInfo, text.Length > 0 ? text : null);
    }

    /// <summary>
    /// 行設定のフォントの欄の薄字（手動指定が無いときに当たるフォント設定）を、選択行の今のチェック結果にする。
    /// 手動指定のある行は、その名前が欄に入っているので「（自動）」のまま。
    /// </summary>
    private void RefreshLineFontPlaceholder()
    {
        if (CharSelectionActive())
        {
            LineFontBox.PlaceholderText = "（自動）";
            return;
        }
        var font = ViewModel.SelectedLine is { Model.IsEmpty: false } line ? line.AppliedFont : ViewModels.LineFontDisplay.None;
        LineFontBox.PlaceholderText = font.IsVisible && !font.IsManual ? $"自動: {font.Summary}" : "（自動）";
    }

    /// <summary>選択行の中で文字を選んでいるか（行設定のフォントの欄は、選んだ文字だけに指定する）。</summary>
    private bool CharSelectionActive() =>
        ViewModel.SelectedLine is { } line && ReferenceEquals(ViewModel.CharSelectionLine, line) && line.CharSelection is not null;

    /// <summary>行設定のフォントの欄の見出しと中身を、文字の選択に合わせる（選んでいれば、選んだ文字の指定）。</summary>
    private void RefreshLineFontForSelection()
    {
        bool chars = CharSelectionActive();
        bool loading = _n3PanelLoading;
        _n3PanelLoading = true;
        try
        {
            if (chars)
            {
                LineFontLabel.Text = $"フォント（選んだ {ViewModel.CharSelectionCount()} 文字）";
                LineFontBox.Text = ViewModel.CharSelectionFontName() ?? "";
            }
            else
            {
                LineFontLabel.Text = "フォント";
                LineFontBox.Text = ViewModel.SelectedLine?.Model.FontSetName ?? "";
            }
        }
        finally
        {
            _n3PanelLoading = loading;
        }
        RefreshLineFontPlaceholder();
        UpdateSideTarget();
    }

    /// <summary>行リストの歌詞の文字を選んだ（押した・ドラッグした）。その行だけを選び、文字の範囲を覚える。</summary>
    private void OnLyricCharSelecting(object? sender, Views.LyricCharSelectEventArgs e)
    {
        if (e.StartUnit < 0)
        {
            ViewModel.ClearCharSelection(); // 文字の無いところを押した
            RefreshLineFontForSelection();
            return;
        }
        if (e.Started && (LineList.SelectedItems.Count != 1 || !ReferenceEquals(LineList.SelectedItem, e.Line)))
        {
            LineList.SelectedItem = e.Line;
        }
        ViewModel.SetCharSelection(e.Line, e.StartUnit, e.EndUnit);
        RefreshLineFontForSelection();
    }

    /// <summary>行設定のレイアウトの欄（候補はベース＋編集したレイアウト。手動指定が無ければ薄字に自動で選ぶレイアウト）。</summary>
    private void RefreshLineLayoutBox(ViewModels.LineViewModel line)
    {
        bool loading = _n3PanelLoading;
        _n3PanelLoading = true;
        try
        {
            var names = ViewModel.GetEffectiveLayouts().Select(l => l.Name).Where(n => n.Length > 0).Distinct().ToList();
            string key = string.Join("\n", names);
            if (_n3LayoutNamesKey != key)
            {
                _n3LayoutNamesKey = key;
                LineLayoutBox.Items.Clear();
                LineLayoutBox.Items.Add(AutoLayoutItem);
                foreach (string n in names) LineLayoutBox.Items.Add(n);
            }
            string? manual = line.Model.LayoutName;
            int index = manual is { Length: > 0 } ? LineLayoutBox.Items.IndexOf(manual) : -1;
            LineLayoutBox.SelectedIndex = index;
            string auto = line.LayoutText.TrimStart('✎');
            LineLayoutBox.PlaceholderText = index < 0 && auto.Length > 0 && !line.LayoutText.StartsWith('✎') ? $"自動: {auto}" : "（自動）";
        }
        finally
        {
            _n3PanelLoading = loading;
        }
    }

    /// <summary>行設定のレイアウトの欄で選んだ（選んだ行のページすべてに指定。「（自動）」で戻す）。</summary>
    private void OnLineLayoutChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_n3PanelLoading || ViewModel.SelectedLine is not { } line || line.Model.IsEmpty) return;
        if (LineLayoutBox.SelectedItem is not string choice) return;
        ApplyLineLayout(choice == AutoLayoutItem ? null : choice);
    }

    /// <summary>
    /// 右パネル「レイアウト」と行設定の指定の対象: 選んだ行のうち空行でない行（無ければ行リストの選択の行。空行なら空）。
    /// 空行を押してから Shift で歌詞の行まで選んだときも、右パネルの表示（選んだ行のうち空行でない行）と同じ行に指定する。
    /// </summary>
    private List<int> SelectedNonEmptyIndexes()
    {
        var indexes = SelectedIndexes.Where(i => i >= 0 && i < ViewModel.Lines.Count && !ViewModel.Lines[i].Model.IsEmpty).ToList();
        if (indexes.Count == 0 && ViewModel.SelectedLine is { } line && !line.Model.IsEmpty) indexes.Add(line.Index);
        return indexes;
    }

    /// <summary>選んだ行のページにレイアウトを指定する（null で自動に戻す）。</summary>
    private void ApplyLineLayout(string? name)
    {
        var indexes = SelectedNonEmptyIndexes();
        if (indexes.Count == 0) return;
        int n = 0;
        TryRun(() => n = ViewModel.SetLinesLayout(indexes, name));
        if (n > 0)
        {
            foreach (var l in ViewModel.Lines) l.RaiseOverrideMark();
            ViewModel.StatusText = name is null
                ? $"選んだ行のページ（{n} 行）のレイアウト指定を自動に戻しました"
                : $"選んだ行のページ（{n} 行）にレイアウト設定「{name}」を指定しました";
        }
        // 行リストのレイアウトの表示とプレビューを作り直す（チェックはしないので、上の知らせは消えない）
        TryRun(ViewModel.UpdateLineFonts);
        if (ViewModel.SelectedLine is { } line && !line.Model.IsEmpty) RefreshLineLayoutBox(line);
        LineSide.RefreshPagePane();
    }

    // ------------------------------------------------------------ 行の字幕アクション（行設定。選んだ行だけ）

    private const string DefaultLineActionItem = "（既定）";

    /// <summary>行設定の字幕アクションの欄の候補（「（既定）」と 8 種類）を入れる（初めの 1 回）。</summary>
    private void EnsureLineActionItems()
    {
        if (LineActionBox.Items.Count > 0) return;
        LineActionBox.Items.Add(DefaultLineActionItem);
        foreach (var kind in N3SubtitleActionCatalog.Known) LineActionBox.Items.Add(kind.Name);
    }

    /// <summary>
    /// 行設定の字幕アクションの欄を、選んだ行（空行は除く）に合わせる。指定が無ければ「（既定）」の薄字、全部同じならその種類、
    /// 行によって違えば「（混在）」。_n3PanelLoading の中で呼ぶ（選択を合わせても指定はしない）。
    /// </summary>
    private void RefreshLineActionBox()
    {
        EnsureLineActionItems();
        var actions = SelectedNonEmptyIndexes().Select(i => ViewModel.Document.Lines[i].SubtitleAction).ToList();
        var def = ViewModel.ResolveCurrentDefaultSubtitleAction(out var source);
        string defText = $"曲の既定: {ViewModel.DescribeSubtitleAction(def)}（{ViewModels.MainViewModel.DefaultSubtitleActionSourceLabel(source)}）";
        string state;
        if (actions.All(a => a is null))
        {
            LineActionBox.SelectedIndex = -1;
            LineActionBox.PlaceholderText = DefaultLineActionItem;
            state = $"この行は曲の既定の字幕アクションです（{defText}）";
        }
        else if (actions[0] is { } a0 && actions.All(a => a0.SameAs(a)))
        {
            int kind = N3SubtitleActionCatalog.Known.ToList().FindIndex(k => k.Id == a0.Id);
            LineActionBox.SelectedIndex = kind >= 0 ? kind + 1 : -1;
            LineActionBox.PlaceholderText = kind >= 0 ? DefaultLineActionItem : N3SubtitleActionCatalog.DisplayName(a0.Id);
            state = $"手で指定した字幕アクション: {ViewModel.DescribeSubtitleAction(a0)}（{defText}）";
        }
        else
        {
            LineActionBox.SelectedIndex = -1;
            LineActionBox.PlaceholderText = "（混在）";
            state = $"選んだ行によって字幕アクションが違います（{defText}）";
        }
        ToolTipService.SetToolTip(LineActionBox,
            $"{state}\n選ぶと、選んだ行だけに指定します（複数の行を選んでいれば、その行すべて。ページのほかの行はそのまま）。「（既定）」で曲の既定に戻します。" +
            "ページの行すべてにそろえるときは、右のパネル「レイアウト」かレイアウト設定ビュー（F4）で指定します");
    }

    /// <summary>行設定の字幕アクションの欄で選んだ（選んだ行だけに指定。「（既定）」で外す）。</summary>
    private void OnLineActionChanged(object sender, SelectionChangedEventArgs e)
    {
        int index = LineActionBox.SelectedIndex;
        if (_n3PanelLoading || index < 0) return;
        ApplyLineActionToSelectedLines(index == 0 ? null : N3SubtitleActionCatalog.Known[index - 1].Id);
    }

    /// <summary>
    /// 選んだ行（空行は除く）だけに字幕アクションを指定する（null で指定を外して曲の既定に戻す）。ページのほかの行は変えない
    /// （ページの行すべてにそろえるのは右パネル・レイアウト設定ビュー）。設定値は <see cref="MainViewModel.CreatePageSubtitleAction"/>。
    /// 元に戻す（Ctrl+Z）は 1 回で戻る。
    /// </summary>
    private void ApplyLineActionToSelectedLines(string? actionId)
    {
        var indexes = SelectedNonEmptyIndexes();
        if (indexes.Count == 0) return;
        N3SubtitleAction? action = string.IsNullOrEmpty(actionId) ? null : ViewModel.CreatePageSubtitleAction(actionId);
        int n = 0;
        TryRun(() => n = ViewModel.SetLinesSubtitleAction(indexes, action, wholePage: false));
        if (n > 0)
        {
            foreach (var l in ViewModel.Lines) l.RaiseOverrideMark();
            ViewModel.StatusText = action is null
                ? $"{n} 行の字幕アクションの指定を外しました（曲の既定に戻します。Ctrl+Z で戻せます）"
                : $"{n} 行に字幕アクション「{N3SubtitleActionCatalog.DisplayName(action.Id)}」を指定しました（選んだ行だけ。ページのほかの行はそのまま。Ctrl+Z で戻せます）";
        }
        else
        {
            ViewModel.StatusText = "字幕アクションの指定は変わりませんでした";
        }
        // 欄の作り直しは、SelectionChanged を抜けてから（選択の変更の中で選択を変えない）
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _n3PanelLoading = true;
            try
            {
                RefreshLineActionBox();
            }
            finally
            {
                _n3PanelLoading = false;
            }
            LineSide.RefreshPagePane();
        });
    }

    /// <summary>
    /// 選んだ行のページに字幕アクションを指定する（null で指定を外して曲の既定に戻す。右パネル「レイアウト」の字幕アクションの欄）。
    /// 設定値は <see cref="MainViewModel.CreatePageSubtitleAction"/>（曲の既定と同じ種類ならその値、違えばその種類の既定値。
    /// レイアウト設定ビュー・MCP と同じ）。元に戻す（Ctrl+Z）は 1 回で戻る。
    /// </summary>
    private void ApplyLineAction(string? actionId)
    {
        var indexes = SelectedNonEmptyIndexes();
        if (indexes.Count == 0)
        {
            ViewModel.StatusText = "行リストで行を選んでから、字幕アクションを選んでください";
            return;
        }
        N3SubtitleAction? action = string.IsNullOrEmpty(actionId) ? null : ViewModel.CreatePageSubtitleAction(actionId);
        int n = 0;
        TryRun(() => n = ViewModel.SetLinesSubtitleAction(indexes, action));
        if (n > 0)
        {
            foreach (var l in ViewModel.Lines) l.RaiseOverrideMark();
            ViewModel.StatusText = action is null
                ? $"選んだ行のページ（{n} 行）の字幕アクションの指定を外しました（曲の既定に戻します）"
                : $"選んだ行のページ（{n} 行）に字幕アクション「{N3SubtitleActionCatalog.DisplayName(action.Id)}」を指定しました";
        }
        else
        {
            ViewModel.StatusText = "字幕アクションの指定は変わりませんでした";
        }
        // 字幕アクションは行リストの表示・プレビュー・チェックに出ないので、右パネルの欄だけを合わせる
        LineSide.RefreshPagePane();
    }

    /// <summary>行設定の文字の大きさの欄（選んだ行のページすべてに、文字の大きさの増減 px を指定する。0 でそのまま）。</summary>
    private void OnPageFontSizeChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_n3PanelLoading || ViewModel.SelectedLine is not { } line || line.Model.IsEmpty) return;
        double value = double.IsNaN(args.NewValue) ? 0 : args.NewValue;
        int delta = (int)Math.Round(Math.Clamp(value, N3PageFontSize.MinDelta, N3PageFontSize.MaxDelta), MidpointRounding.AwayFromZero);
        var indexes = SelectedIndexes;
        if (indexes.Count == 0) indexes = new List<int> { line.Index };
        int n = 0;
        TryRun(() => n = ViewModel.SetLinesFontSizeDelta(indexes, delta));
        if (n > 0)
        {
            foreach (var l in ViewModel.Lines) l.RaiseOverrideMark();
            ViewModel.StatusText = delta == 0
                ? $"選んだ行のページ（{n} 行）の文字の大きさを元に戻しました"
                : $"選んだ行のページ（{n} 行）の文字の大きさを {N3PageFontSize.Signed(delta)} px にしました（n3proj の書き出しで、文字の大きさだけを変えたフォント設定を作って当てます）";
        }
        // 行リスト・字幕のプレビュー・横幅を作り直す（チェックはしないので、上の知らせは消えない）。欄は整数・範囲内の値に直す
        TryRun(ViewModel.UpdateLineFonts);
        bool loading = _n3PanelLoading;
        _n3PanelLoading = true;
        try
        {
            sender.Value = ViewModel.PageFontSizeDelta(line.Index);
        }
        finally
        {
            _n3PanelLoading = loading;
        }
    }

    private static string FmtCs(int cs) => TimeTag.Format(cs).Trim('[', ']');

    /// <summary>ms の時刻を 10ms 単位で表示する（自動調整で行に持たせる値・チェックの文と同じく四捨五入）。</summary>
    private static string FmtMs(int ms) => FmtCs(N3ShowTimeAdjuster.ToCs(ms));

    /// <summary>フォント設定名の候補（ベース n3proj のフォント設定 ＋ NicoKaraPrep のフォント設定（アプリ共通・この曲専用））を作る。</summary>
    private void EnsureN3FontNames()
    {
        string? basePath = ViewModel.SuggestN3ProjBasePath();
        var own = ViewModel.ExportFontSets;
        string key = (basePath ?? "") + "|" + string.Join(",", own.Select(f => f.Name));
        if (_n3FontNamesKey == key) return;
        _n3FontNamesKey = key;

        var names = new List<string>();
        if (basePath is not null)
        {
            try
            {
                names.AddRange(N3ProjFormat.Read(basePath).FontSetNames);
            }
            catch (Exception)
            {
                // ベースが読めなくても候補無しで続行
            }
        }
        names.AddRange(own.Select(f => f.Name));

        string text = LineFontBox.Text;
        LineFontBox.Items.Clear();
        foreach (string n in names.Where(n => n.Length > 0).Distinct())
        {
            LineFontBox.Items.Add(n);
        }
        LineFontBox.Text = text;
    }

    private void OnShowTimeBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            ApplyShowTimeBoxes();
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Escape)
        {
            // 直すのをやめる: 欄を行に持たせた値へ戻して、行リストの選択行へ戻る（↑↓ で行を選び続けられるように）
            RefreshN3LinePanel();
            if (!(ViewModel.SelectedLine is { } line && LineList.ContainerFromItem(line) is ListViewItem item && item.Focus(FocusState.Programmatic)))
            {
                LineList.Focus(FocusState.Programmatic);
            }
            ViewModel.StatusText = "表示時刻は直しませんでした";
            e.Handled = true;
        }
    }

    /// <summary>
    /// 行リストの時刻をダブルクリックしたときに表示開始・終了の欄へ入れた、今の表示時刻（欄の文字）。
    /// 変えずに確定しても手で指定したことにしない（行設定パネルを作り直すと消す）。
    /// </summary>
    private string? _showBeginPrefill, _showEndPrefill;

    /// <summary>行リストの歌い出し・表示開始の時刻をダブルクリック: その行の表示開始の欄へカーソルを移す（行のダブルクリックの再生位置の移動もする）。</summary>
    private void OnLineStartTimeDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => BeginEditShowTime(sender, begin: true);

    /// <summary>行リストの歌い終わり・表示終了の時刻をダブルクリック: その行の表示終了の欄へカーソルを移す。</summary>
    private void OnLineEndTimeDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => BeginEditShowTime(sender, begin: false);

    /// <summary>行リストの上でボタン（マウス・指・ペン）が押されているか（<see cref="OnLineListPointerPressed"/>）。</summary>
    private bool _lineListPointerDown;

    /// <summary>
    /// ボタンを離したら移る表示時刻の欄（行リストの時刻のダブルクリック）。ダブルクリックは 2 回目のボタンを押したところで届き、
    /// 離したときに行の項目がフォーカスを取るので、先に欄へ移っても行リストへ戻されてしまう（画面確認で確かめた）。
    /// </summary>
    private (ViewModels.LineViewModel Line, bool Begin)? _showTimeEditOnRelease;

    /// <summary>行リストのボタンの押し・離しを見張る（行の項目が処理済みにしたものも受ける。処理は行の項目のあとになる）。</summary>
    private void InitializeShowTimeEditing()
    {
        LineList.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnLineListPointerPressed), handledEventsToo: true);
        var released = new PointerEventHandler(OnLineListPointerReleased);
        LineList.AddHandler(UIElement.PointerReleasedEvent, released, handledEventsToo: true);
        LineList.AddHandler(UIElement.PointerCaptureLostEvent, released, handledEventsToo: true);
        LineList.AddHandler(UIElement.PointerCanceledEvent, released, handledEventsToo: true);
    }

    private void OnLineListPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _lineListPointerDown = true;
        _showTimeEditOnRelease = null; // 前のダブルクリックで離したのが届かなかったときの残りは捨てる
    }

    /// <summary>行リストの上でボタンを離した・つかみが外れた: 待っていた表示時刻の欄へ移る。</summary>
    private void OnLineListPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _lineListPointerDown = false;
        if (_showTimeEditOnRelease is not { } pending) return;
        _showTimeEditOnRelease = null;
        FocusShowTimeBox(pending.Line, pending.Begin);
    }

    private void BeginEditShowTime(object sender, bool begin)
    {
        if ((sender as FrameworkElement)?.DataContext is not ViewModels.LineViewModel line || line.Model.IsEmpty) return;
        if (!ReferenceEquals(ViewModel.SelectedLine, line)) LineList.SelectedItem = line;
        if (_lineListPointerDown)
        {
            _showTimeEditOnRelease = (line, begin); // ボタンを離してから（行の項目がフォーカスを取ったあとで）移る
            return;
        }
        FocusShowTimeBox(line, begin);
    }

    /// <summary>行の表示開始・表示終了の欄へカーソルを移す（手で指定していなければ今の表示時刻を入れて選ぶ）。</summary>
    private void FocusShowTimeBox(ViewModels.LineViewModel line, bool begin)
    {
        // 選んだ行の行設定パネルができてから（行の選択の処理・行のダブルクリックの処理・行の項目がフォーカスを取る処理のあとで）移る
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (!ReferenceEquals(ViewModel.SelectedLine, line)) return;
            var box = begin ? ShowBeginBox : ShowEndBox;
            if (box.Text.Trim().Length == 0 && box.PlaceholderText is { Length: > 0 } shown && shown != "--:--:--")
            {
                // 手で指定していない行は、今の表示時刻（薄字）を入れて選ぶ（そのまま打ち直せる。変えずに確定しても手動にしない）
                _n3PanelLoading = true;
                box.Text = shown;
                _n3PanelLoading = false;
                if (begin) _showBeginPrefill = shown;
                else _showEndPrefill = shown;
            }
            box.Focus(FocusState.Programmatic);
            box.SelectAll();
            ViewModel.StatusText = $"{line.Index + 1} 行目の表示{(begin ? "開始" : "終了")}を直せます（mm:ss:cc で入れて Enter。Esc でやめる）";
        });
    }

    /// <summary>
    /// 表示開始・終了の欄に入った: 手で指定していなければ今の表示時刻（薄字）を入れて選ぶ（そこから直せる・コピーできる。
    /// 変えずに離れたら空欄に戻し、手で指定したことにしない。行リストの時刻のダブルクリックと同じ）。
    /// </summary>
    private void OnShowTimeBoxGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box || box.Text.Trim().Length > 0) return;
        if (PlaceholderPrefill.ValueOf(box.PlaceholderText) is not string shown) return;
        _n3PanelLoading = true;
        box.Text = shown;
        _n3PanelLoading = false;
        if (ReferenceEquals(box, ShowBeginBox)) _showBeginPrefill = shown;
        else _showEndPrefill = shown;
        // 部品のメソッド（box.SelectAll）をそのまま渡すと、WinRT へ渡すときに InvalidCastException で落ちるのでラムダで包む
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => box.SelectAll());
    }

    private void OnShowTimeBoxLostFocus(object sender, RoutedEventArgs e)
    {
        ApplyShowTimeBoxes();
        DropShowTimePrefill(sender);
    }

    /// <summary>
    /// 行リストの時刻のダブルクリックで欄へ入れた今の表示時刻を、変えずに欄を離れたら外して、欄を空欄（薄字の表示）へ戻す
    /// （手で指定した値に見えないように）。
    /// </summary>
    private void DropShowTimePrefill(object sender)
    {
        if (sender is not TextBox box) return;
        bool isBegin = ReferenceEquals(box, ShowBeginBox);
        string? prefill = isBegin ? _showBeginPrefill : _showEndPrefill;
        if (prefill is null) return;
        if (isBegin) _showBeginPrefill = null;
        else _showEndPrefill = null;
        if (box.Text.Trim() != prefill) return;
        _n3PanelLoading = true;
        box.Text = "";
        _n3PanelLoading = false;
    }

    private void ApplyShowTimeBoxes()
    {
        if (_n3PanelLoading || ViewModel.SelectedLine is not { } line || line.Model.IsEmpty) return;

        // 行リストの時刻のダブルクリックで入れた今の表示時刻のままなら、手で指定していない（空欄と同じ）
        string beginText = ShowBeginBox.Text.Trim() == _showBeginPrefill ? "" : ShowBeginBox.Text;
        string endText = ShowEndBox.Text.Trim() == _showEndPrefill ? "" : ShowEndBox.Text;
        int? begin = ParseTimeText(beginText);
        int? end = ParseTimeText(endText);
        if (beginText.Trim().Length > 0 && begin is null)
        {
            ViewModel.StatusText = "表示開始は mm:ss:cc 形式で入力してください（空欄にすると、手で指定した値を外します）";
            return;
        }
        if (endText.Trim().Length > 0 && end is null)
        {
            ViewModel.StatusText = "表示終了は mm:ss:cc 形式で入力してください（空欄にすると、手で指定した値を外します）";
            return;
        }

        bool changed = false;
        TryRun(() => changed = ViewModel.SetLineShowTimeFromBoxes(line.Index, begin, end));
        if (!changed) return;
        line.RaiseOverrideMark();
        ViewModel.StatusText = begin is null && end is null
            ? $"{line.Index + 1} 行目の手で指定した表示時刻を外しました"
            : $"{line.Index + 1} 行目の表示時刻を手で指定しました（自動調整を実行し直しても変わりません）";
        RefreshN3LinePanel();
        ScheduleValidation();
    }

    private static int? ParseTimeText(string text)
    {
        string t = text.Trim();
        if (t.Length == 0) return null;
        return TimeTag.TryParse(t, out int cs) ? cs : null;
    }

    private void OnResetShowTimeClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedLine is not { } line || line.Model.IsEmpty) return;
        bool changed = false;
        TryRun(() => changed = ViewModel.SetLineShowTime(line.Index, null, null));
        if (changed)
        {
            line.RaiseOverrideMark();
            ViewModel.StatusText = $"{line.Index + 1} 行目の表示時刻を自動に戻しました";
        }
        RefreshN3LinePanel();
        ScheduleValidation();
    }

    private void OnLineFontChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_n3PanelLoading) return;
        if (LineFontBox.SelectedItem is string name && !IsPrefilledFont(name)) ApplyLineFont(name);
    }

    private void OnLineFontSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args)
    {
        if (!IsPrefilledFont(args.Text)) ApplyLineFont(args.Text);
        args.Handled = true;
    }

    /// <summary>
    /// 欄に入ったときに入れた、自動で当たっているフォント設定の名前か（編集できる ComboBox は、入れた名前を欄を離れるときなどに
    /// 自分で確定してしまうので、その名前は手で指定したことにしない。もう自動で当たっている名前なので、指定しなくても同じ）。
    /// </summary>
    private bool IsPrefilledFont(string? name) => _lineFontPrefill is string p && (name ?? "").Trim() == p;

    private void ApplyLineFont(string? name)
    {
        if (_n3PanelLoading || ViewModel.SelectedLine is not { } line || line.Model.IsEmpty) return;
        string label = string.IsNullOrWhiteSpace(name) ? "" : name.Trim();
        var indexes = SelectedIndexes;
        if (CharSelectionActive() && line.CharSelection is (int start, int end))
        {
            // 選んだ文字だけ
            int count = ViewModel.CharSelectionCount();
            int changedChars = 0;
            TryRun(() => changedChars = ViewModel.SetCharFontSet(line.Index, start, end, name));
            if (changedChars == 0) return;
            line.RaiseOverrideMark();
            ViewModel.StatusText = label.Length == 0
                ? $"{line.Index + 1} 行目の選んだ {count} 文字のフォント指定を自動に戻しました"
                : $"{line.Index + 1} 行目の選んだ {count} 文字にフォント設定「{label}」を指定しました";
        }
        else if (indexes.Count > 1)
        {
            // 選んだ行すべて（ニコカラメーカー3 でチェックした行に指定するのと同じ）
            int n = 0;
            TryRun(() => n = ViewModel.SetLinesFontSet(indexes, name));
            if (n == 0) return;
            foreach (var l in ViewModel.Lines) l.RaiseOverrideMark();
            ViewModel.StatusText = label.Length == 0
                ? $"選んだ {n} 行のフォント指定を自動に戻しました"
                : $"選んだ {n} 行にフォント設定「{label}」を指定しました";
        }
        else
        {
            bool changed = false;
            TryRun(() => changed = ViewModel.SetLineFontSet(line.Index, name));
            if (!changed) return;
            line.RaiseOverrideMark();
            ViewModel.StatusText = label.Length == 0
                ? $"{line.Index + 1} 行目のフォント指定を自動に戻しました"
                : $"{line.Index + 1} 行目にフォント設定「{label}」を指定しました";
        }
        // 行リストのフォント設定の欄（この行と、引き継ぐ後ろの行）を作り直す（チェックはしないので、上の知らせは消えない）
        TryRun(ViewModel.UpdateLineFonts);
        RefreshLineFontPlaceholder();
    }

    /// <summary>フォントの欄に入れた、自動で当たるフォント設定の名前（変えずに離れたら空欄に戻す）。</summary>
    private string? _lineFontPrefill;

    /// <summary>
    /// フォントの欄に入った: 手で指定していなければ、自動で当たるフォント設定の名前（薄字）を入れて選ぶ（そこから直せる・コピーできる）。
    /// 名前を入れても、変えずに離れる・Enter なら手で指定したことにしない。
    /// </summary>
    private void OnLineFontGotFocus(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(LineFontBox.Text))
        {
            // 手で指定した名前が入っている（または入れた名前のまま、欄の中で動いただけ）
            if (!IsPrefilledFont(LineFontBox.Text)) _lineFontPrefill = null;
            return;
        }
        _lineFontPrefill = null;
        if (PlaceholderPrefill.ValueOf(LineFontBox.PlaceholderText) is not string name) return;
        _n3PanelLoading = true;
        try
        {
            LineFontBox.Text = name;
        }
        finally
        {
            _n3PanelLoading = false;
        }
        _lineFontPrefill = name;
        if (e.OriginalSource is TextBox box) DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => box.SelectAll()); // ラムダで包む（上と同じ）
    }

    /// <summary>
    /// フォントの欄を離れる: 入れた名前のままなら空欄（自動）へ戻す。一覧を開いて選ぶとき（欄の中へ移る）はそのまま。
    /// 編集できる ComboBox は、離れるときに自分で中の文字を確定し直すので、離れ終わってから（優先度を下げて）空にする。
    /// </summary>
    private void OnLineFontLosingFocus(UIElement sender, LosingFocusEventArgs args)
    {
        if (_lineFontPrefill is not string name) return;
        if (args.NewFocusedElement is DependencyObject next && IsInsideOf(next, LineFontBox)) return;
        if (args.NewFocusedElement is ComboBoxItem) return; // 一覧の項目を選ぼうとしている
        // _lineFontPrefill は残す（このあと ComboBox が入れた名前を確定してきても、手で指定したことにしないため。次に欄に入ったときに入れ直す）
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (LineFontBox.FocusState != FocusState.Unfocused) return;
            var inner = FindInnerTextBox(LineFontBox);
            string shown = (inner?.Text ?? LineFontBox.Text ?? "").Trim();
            if (shown != name && (LineFontBox.Text ?? "").Trim() != name) return;
            _n3PanelLoading = true;
            try
            {
                LineFontBox.SelectedIndex = -1;
                LineFontBox.Text = "";
                if (inner is not null) inner.Text = "";
            }
            finally
            {
                _n3PanelLoading = false;
            }
        });
    }

    private static TextBox? FindInnerTextBox(DependencyObject root)
    {
        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is TextBox t) return t;
            if (FindInnerTextBox(child) is { } found) return found;
        }
        return null;
    }

    private static bool IsInsideOf(DependencyObject element, DependencyObject ancestor)
    {
        for (var e = element; e is not null; e = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(e))
        {
            if (ReferenceEquals(e, ancestor)) return true;
        }
        return false;
    }

    /// <summary>
    /// フォントの欄を空にして Enter を押したら、自動に戻す。編集できる ComboBox は、空の文字を確定しても TextSubmitted を出さず、
    /// 選んでいた名前に戻してしまうため、ComboBox が Enter を処理する前にここで受ける。
    /// </summary>
    private void OnLineFontPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // 欄に入ったときに入れた自動のフォント設定の名前のまま Enter: 手で指定したことにしない
        if (e.Key == Windows.System.VirtualKey.Enter && _lineFontPrefill is string prefill && e.OriginalSource is TextBox current && current.Text.Trim() == prefill)
        {
            _n3PanelLoading = true;
            try
            {
                LineFontBox.SelectedIndex = -1;
                LineFontBox.Text = "";
            }
            finally
            {
                _n3PanelLoading = false;
            }
            e.Handled = true;
            return;
        }
        if (e.Key != Windows.System.VirtualKey.Enter || e.OriginalSource is not TextBox box || !string.IsNullOrWhiteSpace(box.Text)) return;
        _n3PanelLoading = true;
        try
        {
            LineFontBox.SelectedIndex = -1; // このあとの ComboBox の確定で、前の名前に戻さないように
        }
        finally
        {
            _n3PanelLoading = false;
        }
        ApplyLineFont(null);
    }
}
