using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NicoKaraPrep.App;

/// <summary>
/// 行リストのときの右のパネル（<see cref="Views.LineSidePanel"/>）: フォント一覧で押したフォント設定を選んだ文字・行に指定し、
/// 選んだ行のページへのレイアウト・字幕アクションの指定と、レイアウト設定ビュー（F4）を開くボタンを行リストの表示とプレビューへつなぐ。
/// レイアウト設定の編集（レイアウト設定ビュー）のあとの行リストの表示・プレビューの作り直しもここ。
/// </summary>
public sealed partial class MainWindow
{
    private DispatcherQueueTimer? _layoutRefreshTimer;

    private void InitializeLineSide()
    {
        LineSide.Initialize(ViewModel, () => SelectedIndexes);
        LineSide.FontPicked += (_, name) => ApplyFontFromPanel(name);
        LineSide.PageLayoutPicked += (_, name) => ApplyLineLayout(name);
        LineSide.PageActionPicked += (_, pick) => ApplyLineAction(pick.Id, pick.WholePage);
        LineSide.EditLayoutsRequested += (_, name) => OpenLayoutViewFor(name);
        LineSide.SongActionRequested += (_, _) => _ = OpenSongActionDialogAsync();
        ViewModel.LayoutsChanged += (_, _) => ScheduleLayoutRefresh();
        UpdateSideTarget();
    }

    /// <summary>レイアウト設定を編集した: 行リストのレイアウトの表示とプレビューを作り直す（続けて変えたときはまとめる）。</summary>
    private void ScheduleLayoutRefresh()
    {
        if (_layoutRefreshTimer is null)
        {
            _layoutRefreshTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _layoutRefreshTimer.Interval = TimeSpan.FromMilliseconds(250);
            _layoutRefreshTimer.IsRepeating = false;
            _layoutRefreshTimer.Tick += (_, _) =>
            {
                TryRun(ViewModel.UpdateLineFonts);
                _n3LayoutNamesKey = null;
                if (ViewModel.SelectedLine is { } line && !line.Model.IsEmpty) RefreshLineLayoutBox(line);
            };
        }
        _layoutRefreshTimer.Stop();
        _layoutRefreshTimer.Start();
    }

    /// <summary>開いている「表示時刻の自動調整」のポップアップ（無ければ null。MCP でパラメーターを変えたとき・自動調整のあとに中身を合わせる）。</summary>
    private Views.ShowTimeDialog? _showTimeDialog;

    /// <summary>行設定の「自動調整...」・エクスポート(X) の「表示時刻の自動調整...」。</summary>
    private void OnShowTimeDialogClick(object sender, RoutedEventArgs e) => _ = OpenShowTimeDialogAsync();

    /// <summary>「表示時刻の自動調整」のポップアップを開く（パラメーターは変えるとすぐ保存。閉じるボタンだけ）。</summary>
    private async Task OpenShowTimeDialogAsync()
    {
        if (_showTimeDialog is not null) return;
        var dialog = new Views.ShowTimeDialog(ViewModel) { XamlRoot = Content.XamlRoot };
        dialog.ShowTimeSettingsChanged += (_, _) => OnShowTimeSettingsChanged();
        dialog.AutoShowTimeRequested += (_, _) => RunAutoShowTimes();
        _showTimeDialog = dialog;
        try
        {
            await dialog.ShowAsync();
        }
        finally
        {
            _showTimeDialog = null;
        }
    }

    /// <summary>字幕アクションのタブの「曲の既定を設定...」・エクスポート(X) の「字幕アクションの曲の既定...」。</summary>
    private void OnSongActionDialogClick(object sender, RoutedEventArgs e) => _ = OpenSongActionDialogAsync();

    /// <summary>字幕アクションの曲の既定のポップアップを開き、OK なら曲の既定にする（.tttproj に保存。元に戻すの対象にはしない）。</summary>
    private async Task OpenSongActionDialogAsync()
    {
        var dialog = new Views.SongActionDialog(ViewModel) { XamlRoot = Content.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        bool changed = false;
        TryRun(() => changed = ViewModel.SetSongDefaultSubtitleAction(dialog.Result));
        ViewModel.StatusText = !changed
            ? "曲の既定の字幕アクションは変わりませんでした"
            : dialog.Result is null
                ? "曲の既定の字幕アクションを自動にしました（.tttproj に保存します）"
                : $"曲の既定の字幕アクションを「{ViewModel.DescribeSubtitleAction(dialog.Result)}」にしました（.tttproj に保存します）";
        LineSide.RefreshPagePane();
    }

    /// <summary>
    /// 「表示時刻の自動調整」のパラメーターを変えた: 表示時刻を持たない行（未設定）はすぐ新しいパラメーターで計算されるので、
    /// プレビュー・行設定・チェックを作り直す（行に持たせた値は「自動調整を実行」するまで変わらない）。
    /// </summary>
    private void OnShowTimeSettingsChanged()
    {
        RefreshN3LinePanel();
        ScheduleValidation();
        _showTimeDialog?.RefreshSummary();
    }

    /// <summary>
    /// 「表示時刻の自動調整」の「自動調整を実行」: 全タブの表示時刻を決め直し、行リストの印・行設定・プレビュー・チェックを作り直す。
    /// 結果の行数を返す（実行できなかったときは null。MCP からも呼ぶ）。
    /// </summary>
    private Core.Formats.N3ShowTimeAdjustResult? RunAutoShowTimes()
    {
        Core.Formats.N3ShowTimeAdjustResult? result = null;
        TryRun(() => result = ViewModel.RunAutoShowTimes());
        foreach (var line in ViewModel.Lines) line.RaiseOverrideMark();
        RefreshN3LinePanel();
        // チェックはすぐに実行し、自動調整の知らせがチェック結果で消えないよう、つなげて表示する
        string summary = ViewModel.StatusText;
        _validateTimer.Stop();
        TryRun(ViewModel.RunValidation);
        RefreshInsertGutter();
        ViewModel.StatusText = $"{summary}　／　{ViewModel.StatusText}";
        _showTimeDialog?.RefreshSummary();
        return result;
    }

    /// <summary>右のフォント一覧で押した（null は「自動に戻す」）: 選んだ文字（文字を選んでいなければ選んだ行）に指定する。</summary>
    private void ApplyFontFromPanel(string? name)
    {
        if (ViewModel.SelectedLine is not { } line || line.Model.IsEmpty)
        {
            ViewModel.StatusText = "行リストで行か文字を選んでから、フォント設定を押してください";
            return;
        }
        ApplyLineFont(name);
        RefreshLineFontForSelection();
    }

    /// <summary>右のフォント一覧の上の「どこに指定するか」の説明を、今の選択に合わせる。</summary>
    private void UpdateSideTarget()
    {
        string text;
        if (CharSelectionActive() && ViewModel.SelectedLine is { } charLine)
        {
            text = $"{charLine.Index + 1} 行目の選んだ {ViewModel.CharSelectionCount()} 文字に指定します（Esc で文字の選択をやめる）";
        }
        else if (SelectedIndexes.Count > 1)
        {
            text = $"選んだ {SelectedIndexes.Count} 行（行全体）に指定します";
        }
        else if (ViewModel.SelectedLine is { Model.IsEmpty: false } line)
        {
            text = $"{line.Index + 1} 行目（行全体）に指定します。歌詞の文字をクリック・ドラッグで選ぶと、その文字だけに指定します";
        }
        else
        {
            text = "行リストで行か文字を選んでから、フォント設定を押してください";
        }
        LineSide.SetTarget(text);
    }
}
