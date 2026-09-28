using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NicoKaraPrep.App.Services;
using NicoKaraPrep.App.Views;
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
    }

    private async void OnN3FontSetsClick(object sender, RoutedEventArgs e)
    {
        while (true)
        {
            var dialog = new N3FontSetDialog(ViewModel.Settings) { XamlRoot = Content.XamlRoot };
            var result = await dialog.ShowAsync();
            if (dialog.ImportRequested)
            {
                // 取り込みは読み込み確認画面（フォント設定を選択した状態）で行い、終わったら編集画面へ戻る
                await ImportN3ProjAsync(null, N3ProjImportFocus.FontSets);
                continue;
            }
            if (result == ContentDialogResult.Primary)
            {
                _n3FontNamesKey = null;
                ViewModel.StatusText = $"ニコカラメーカー3 のフォント設定を保存しました（{ViewModel.Settings.N3FontSets.Count} 件）";
                RefreshN3LinePanel();
            }
            break;
        }
    }

    /// <summary>
    /// ニコカラメーカー3 プロジェクトの読み込み（メニュー・ドラッグ＆ドロップ・フォント設定の取り込みの共通入口）。
    /// 内容を調べて確認画面を出し、選んだ項目だけを取り込む。path が null ならファイルを選ばせる。
    /// </summary>
    private async Task ImportN3ProjAsync(string? path, N3ProjImportFocus focus = N3ProjImportFocus.Default)
    {
        try
        {
            if (path is null)
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                WinRT.Interop.InitializeWithWindow.Initialize(picker, Hwnd);
                picker.FileTypeFilter.Add(".n3proj");
                var file = await picker.PickSingleFileAsync();
                if (file is null)
                {
                    // ファイルを選んだのに何も起きない場合と区別できるよう、選ばれなかったことを表示する
                    ViewModel.StatusText = "ファイルが選ばれなかったため、ニコカラメーカー3 プロジェクトは読み込みませんでした";
                    return;
                }
                if (string.IsNullOrEmpty(file.Path))
                {
                    await ShowMessageAsync("ニコカラメーカー3 プロジェクトを読み込めませんでした",
                        $"{file.Name}\n\n選んだファイルの場所（パス）を取得できませんでした。エクスプローラーでファイルのあるフォルダを開き、画面へドラッグ＆ドロップしてください。");
                    return;
                }
                path = file.Path;
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
                ViewModel.StatusText = $"エラー: {ex.Message}";
                await ShowMessageAsync("ニコカラメーカー3 プロジェクトを読み込めませんでした", $"{Path.GetFileName(path)}\n\n{ex.Message}");
                return;
            }

            var dialog = new N3ProjImportDialog(ViewModel, preview, focus) { XamlRoot = Content.XamlRoot };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            var choices = dialog.Result;
            TryRun(() => ViewModel.ApplyN3ProjImport(preview, choices));
            string summary = ViewModel.StatusText;
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
            ViewModel.StatusText = $"{summary}　／　{ViewModel.StatusText}";
        }
        catch (Exception ex)
        {
            DebugLog($"n3proj の読み込みで例外: {ex}");
            ViewModel.StatusText = $"エラー: {ex.Message}";
            await ShowMessageAsync("ニコカラメーカー3 プロジェクトの読み込み中にエラーが発生しました",
                $"{(path is null ? "" : Path.GetFileName(path) + "\n\n")}{ex.GetType().Name}: {ex.Message}");
        }
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
        var indexes = SelectedIndexes;
        if (indexes.Count == 0)
        {
            ViewModel.StatusText = "行を選択してください";
            return;
        }
        TryRun(() =>
        {
            int n = ViewModel.ClearLineOverrides(indexes);
            ViewModel.StatusText = n > 0 ? $"{n} 行の表示時刻・フォント指定を解除しました" : "手動指定のある行はありません";
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
                N3LineInfo.Text = "";
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

            if (plan is null)
            {
                N3LineInfo.Text = "タイムタグが無いため表示時刻を計算できません";
            }
            else
            {
                string row = $"{(ViewModel.Settings.CollisionAlignFromTop ? "上" : "下")}から{plan.Row}行目";
                string adjusted = plan.Adjusted ? "・前後ページに合わせて調整" : "";
                string manual = plan.BeginIsManual || plan.EndIsManual ? "（手動指定あり）" : "";
                N3LineInfo.Text = $"自動: {FmtCs(plan.BeginMs / 10)} 〜 {FmtCs(plan.EndMs / 10)}　ページ{plan.PageIndex + 1}・{row}{adjusted}{manual}";
            }
        }
        finally
        {
            _n3PanelLoading = false;
        }
    }

    private static string FmtCs(int cs) => TimeTag.Format(cs).Trim('[', ']');

    /// <summary>フォント設定名の候補（ベース n3proj のフォント設定 ＋ NicoKaraPrep のフォント設定）を作る。</summary>
    private void EnsureN3FontNames()
    {
        string? basePath = ViewModel.SuggestN3ProjBasePath();
        string key = (basePath ?? "") + "|" + string.Join(",", ViewModel.Settings.N3FontSets.Select(f => f.Name));
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
        names.AddRange(ViewModel.Settings.N3FontSets.Select(f => f.Name));

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
        bool changed = false;
        TryRun(() => changed = ViewModel.SetLineFontSet(line.Index, name));
        if (!changed) return;
        line.RaiseOverrideMark();
        ViewModel.StatusText = string.IsNullOrWhiteSpace(name)
            ? $"{line.Index + 1} 行目のフォント指定を自動に戻しました"
            : $"{line.Index + 1} 行目にフォント設定「{name.Trim()}」を指定しました";
    }
}
