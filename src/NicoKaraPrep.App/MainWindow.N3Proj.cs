using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NicoKaraPrep.App.Services;
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
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var settings = dialog.Result;

        string? suggestedPath = ViewModel.SuggestN3ProjOutputPath();
        string? folder = Path.GetDirectoryName(suggestedPath ?? "") is { Length: > 0 } d ? d : ViewModel.GetDefaultSaveFolder();
        string suggested = Path.GetFileNameWithoutExtension(suggestedPath ?? "lyrics");
        string? path = SaveFileDialog.Show(Hwnd, folder, suggested, N3ProjFileTypes, "n3proj");
        if (path is null) return;

        TryRun(() => ViewModel.ExportN3Proj(path, settings));
        _n3FontNamesKey = null;
        RefreshN3LinePanel();
        // 書き出し設定（ベース・既定のフォント設定・合わせるか）で、行に当たるフォント設定が変わることがある（書き出しの知らせは残す）
        TryRun(ViewModel.UpdateLineFonts);
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

            // チェックはすぐに実行し、何を読み込んだかの表示がチェック結果で消えないよう、つなげて表示する
            _validateTimer.Stop();
            TryRun(ViewModel.RunValidation);
            RefreshInsertGutter();
            RefreshLineFontPlaceholder();
            ViewModel.StatusText = $"{summary}　／　{ViewModel.StatusText}";
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
            ViewModel.StatusText = n > 0 ? $"{n} 行の表示時刻・フォント・レイアウト・文字の大きさの指定を解除しました" : "手動指定のある行はありません";
        });
        foreach (var line in ViewModel.Lines) line.RaiseOverrideMark();
        RefreshN3LinePanel();
        ScheduleValidation();
    }

    // ------------------------------------------------------------ 行設定パネル

    /// <summary>選択行のニコカラメーカー用行設定（表示時刻・フォント）をパネルへ表示する。</summary>
    private void RefreshN3LinePanel()
    {
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
                PageFontSizeBox.Value = 0;
                UpdateSideTarget();
                RefreshLineFontPlaceholder();
                return;
            }

            var model = line.Model;
            var plans = ViewModel.PlanShowTimes();
            plans.TryGetValue(line.Index, out var plan);

            ShowBeginBox.Text = model.ShowBeginCs is int b ? FmtCs(b) : "";
            ShowEndBox.Text = model.ShowEndCs is int en ? FmtCs(en) : "";
            ShowBeginBox.PlaceholderText = plan is not null ? FmtCs(plan.BeginMs / 10) : "--:--:--";
            ShowEndBox.PlaceholderText = plan is not null ? FmtCs(plan.EndMs / 10) : "--:--:--";

            EnsureN3FontNames();
            LineFontBox.Text = model.FontSetName ?? "";
            RefreshLineFontForSelection();
            RefreshLineLayoutBox(line);
            PageFontSizeBox.Value = ViewModel.PageFontSizeDelta(line.Index);

            if (plan is null)
            {
                SetN3LineInfo("タイムタグが無いため表示時刻を計算できません");
            }
            else
            {
                string row = $"{(ViewModel.Settings.CollisionAlignFromTop ? "上" : "下")}から{plan.Row}行目";
                string adjusted = plan.Adjusted ? "・前後ページに合わせて調整" : "";
                string manual = plan.BeginIsManual || plan.EndIsManual ? "（手動指定あり）" : "";
                SetN3LineInfo($"自動: {FmtCs(plan.BeginMs / 10)} 〜 {FmtCs(plan.EndMs / 10)}　ページ{plan.PageIndex + 1}・{row}{adjusted}{manual}");
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

    /// <summary>選んだ行のページにレイアウトを指定する（null で自動に戻す）。</summary>
    private void ApplyLineLayout(string? name)
    {
        if (ViewModel.SelectedLine is not { } line || line.Model.IsEmpty) return;
        var indexes = SelectedIndexes;
        if (indexes.Count == 0) indexes = new List<int> { line.Index };
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
        RefreshLineLayoutBox(line);
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
    }

    private void OnShowTimeBoxLostFocus(object sender, RoutedEventArgs e) => ApplyShowTimeBoxes();

    private void ApplyShowTimeBoxes()
    {
        if (_n3PanelLoading || ViewModel.SelectedLine is not { } line || line.Model.IsEmpty) return;

        int? begin = ParseTimeText(ShowBeginBox.Text);
        int? end = ParseTimeText(ShowEndBox.Text);
        if (ShowBeginBox.Text.Trim().Length > 0 && begin is null)
        {
            ViewModel.StatusText = "表示開始は mm:ss:cc 形式で入力してください（空欄で自動）";
            return;
        }
        if (ShowEndBox.Text.Trim().Length > 0 && end is null)
        {
            ViewModel.StatusText = "表示終了は mm:ss:cc 形式で入力してください（空欄で自動）";
            return;
        }

        bool changed = false;
        TryRun(() => changed = ViewModel.SetLineShowTime(line.Index, begin, end));
        if (!changed) return;
        line.RaiseOverrideMark();
        ViewModel.StatusText = begin is null && end is null
            ? $"{line.Index + 1} 行目の表示時刻を自動に戻しました"
            : $"{line.Index + 1} 行目の表示時刻を指定しました（n3proj 書き出しとページ衝突チェックに反映）";
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
        if (LineFontBox.SelectedItem is string name) ApplyLineFont(name);
    }

    private void OnLineFontSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args)
    {
        ApplyLineFont(args.Text);
        args.Handled = true;
    }

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

    /// <summary>
    /// フォントの欄を空にして Enter を押したら、自動に戻す。編集できる ComboBox は、空の文字を確定しても TextSubmitted を出さず、
    /// 選んでいた名前に戻してしまうため、ComboBox が Enter を処理する前にここで受ける。
    /// </summary>
    private void OnLineFontPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
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
