using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NicoKaraPrep.App.ViewModels;
using Windows.Globalization.NumberFormatting;

namespace NicoKaraPrep.App;

/// <summary>
/// 字幕全体の時刻をずらす欄（動画の下。再生 > 字幕全体の時刻をずらす、再生の操作の「時刻ずらし」で出し入れ）。
/// 押すたびに全タブの時刻を書き換え（<see cref="MainViewModel.ShiftAllTimes"/>）、字幕のプレビューをすぐ作り直すので、
/// 再生を続けたまま見え方を確かめて微調整できる。
/// </summary>
public sealed partial class MainWindow
{
    private bool _timeShiftBoxUpdating;

    /// <summary>
    /// 欄の部品ができて、値の変化を受けてよいか（XAML で値を入れると、画面を作る途中で値の変化が来て、まだできていない部品を触って落ちる。
    /// 欄の値は XAML では入れず、ここで入れる）。
    /// </summary>
    private bool _timeShiftReady;

    /// <summary>起動時に、ずらした量の欄の書式（小数 2 桁）と初めの値、数え直しの知らせをつなぐ。</summary>
    private void InitializeTimeShiftBar()
    {
        var formatter = new DecimalFormatter
        {
            IntegerDigits = 1,
            FractionDigits = 2,
            NumberRounder = new IncrementNumberRounder { Increment = 0.01, RoundingAlgorithm = RoundingAlgorithm.RoundHalfAwayFromZero },
        };
        TimeShiftBox.NumberFormatter = formatter;
        ViewModel.TimeShiftSessionReset += (_, _) => DispatcherQueue.TryEnqueue(UpdateTimeShiftAmount);
        _timeShiftReady = true;
        UpdateTimeShiftAmount();
    }

    private bool TimeShiftBarOpen => TimeShiftBar.Visibility == Visibility.Visible;

    private void OnTimeShiftMenuClick(object sender, RoutedEventArgs e)
    {
        if (TimeShiftBarOpen) TimeShiftBox.Focus(FocusState.Programmatic);
        else OpenTimeShiftBar();
    }

    // ボタンの入り切り（押したときも、開く・閉じるの中で IsChecked を合わせたときも来るので、欄の状態と違うときだけ動く）
    private void OnTimeShiftToggleChecked(object sender, RoutedEventArgs e)
    {
        if (!TimeShiftBarOpen) OpenTimeShiftBar();
    }

    private void OnTimeShiftToggleUnchecked(object sender, RoutedEventArgs e)
    {
        if (TimeShiftBarOpen) CloseTimeShiftBar();
    }

    private void OnTimeShiftCloseClick(object sender, RoutedEventArgs e) => CloseTimeShiftBar();

    /// <summary>欄を開く（メディア再生が畳まれていれば広げる）。</summary>
    private void OpenTimeShiftBar()
    {
        if (FullScreenViewActive)
        {
            TimeShiftToggle.IsChecked = false;
            ViewModel.StatusText = "字幕全体の時刻をずらす欄は、行リストか絵文字挿入ビューで使えます";
            return;
        }
        ViewModel.BeginTimeShiftSession();
        MediaPanel.IsExpanded = true;
        TimeShiftBar.Visibility = Visibility.Visible;
        TimeShiftToggle.IsChecked = true;
        UpdateTimeShiftAmount();
        FitPlayerHeightAfterLayout(); // 欄の分だけ、行リストの高さが残るよう動画を縮める
        ViewModel.StatusText = PlayerWithSource is null
            ? "字幕全体の時刻をずらす: メディアを開くと、再生しながら字幕プレビューで確かめられます（再生 > メディアを開く）"
            : "字幕全体の時刻をずらす: 再生しながらボタンを押すと、すぐ字幕プレビューに出ます";
    }

    /// <summary>欄を閉じる（ずらした時刻はそのまま）。</summary>
    private void CloseTimeShiftBar()
    {
        int total = ViewModel.TimeShiftSessionCs;
        ViewModel.EndTimeShiftSession();
        TimeShiftBar.Visibility = Visibility.Collapsed;
        TimeShiftToggle.IsChecked = false;
        FitPlayerHeightAfterLayout();
        if (total != 0)
        {
            ViewModel.StatusText = $"字幕全体の時刻を {MainViewModel.FormatTimeShift(total)} ずらしました（Ctrl+Z で、タブごとにずらす前へ戻せます）";
        }
    }

    private void OnTimeShiftStepClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && int.TryParse(tag, out int cs)) ShiftAllTimesBy(cs);
    }

    private void OnTimeShiftResetClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.TimeShiftSessionCs == 0)
        {
            ViewModel.StatusText = "この欄ではまだずらしていません";
            return;
        }
        ShiftAllTimesBy(-ViewModel.TimeShiftSessionCs);
    }

    /// <summary>ずらした量の欄に入れた値（入力・▲▼・↑↓キー）まで、全体をずらす。</summary>
    private void OnTimeShiftBoxValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_timeShiftBoxUpdating || !_timeShiftReady) return;
        if (double.IsNaN(args.NewValue))
        {
            UpdateTimeShiftAmount(); // 空にしたときは今の量に戻す
            return;
        }
        int target = (int)Math.Round(args.NewValue * 100, MidpointRounding.AwayFromZero);
        int delta = target - ViewModel.TimeShiftSessionCs;
        if (delta != 0) ShiftAllTimesBy(delta);
        else UpdateTimeShiftAmount();
    }

    /// <summary>全タブの時刻を deltaCs（10ms 単位）ずらし、行の編集欄・行設定・字幕のプレビューを新しい時刻にする。</summary>
    private void ShiftAllTimesBy(int deltaCs)
    {
        int moved = 0;
        TryRun(() => moved = ViewModel.ShiftAllTimes(deltaCs));
        UpdateTimeShiftAmount();
        if (moved == 0)
        {
            var (min, max) = ViewModel.GetTimeShiftLimits();
            ViewModel.StatusText = min == 0 && max == 0 ? "ずらすタイムタグがありません"
                : deltaCs < 0 ? "これ以上は早められません（最初のタイムタグが −99:59.99 です）"
                : "これ以上は遅らせられません（最後のタイムタグが 99:59.99 です）";
            return;
        }

        LineEditor.Text = ViewModel.SelectedLine?.RawText ?? "";
        RenderPreview();
        RefreshN3LinePanel();
        if (InsertViewActive) RefreshInsertView(InsertEditor.SelectionStart);
        TryRun(ViewModel.UpdateLineFonts); // 字幕のプレビューの材料を今の時刻で作り直す（チェックを待たずに）
        UpdatePlayerTime(); // 止めているときも、今の位置の字幕を新しい時刻で出す

        string limited = moved != deltaCs ? "（ずらせる範囲の端で止めました）" : "";
        ViewModel.StatusText = ViewModel.TimeShiftSessionCs == 0
            ? $"この欄でずらした分を全タブまとめて戻しました（{MainViewModel.FormatTimeShift(moved)}）"
            : $"字幕全体の時刻を {MainViewModel.FormatTimeShift(moved)} ずらしました{limited}。この欄でずらした量は {MainViewModel.FormatTimeShift(ViewModel.TimeShiftSessionCs)}";
        ViewModel.KeepNoticeBeforeCheck(); // チェックの結果で知らせを消さない
        ScheduleValidation();
    }

    /// <summary>ずらした量の欄を今の合計にする。</summary>
    private void UpdateTimeShiftAmount()
    {
        _timeShiftBoxUpdating = true;
        try
        {
            TimeShiftBox.Value = ViewModel.TimeShiftSessionCs / 100.0;
        }
        finally
        {
            _timeShiftBoxUpdating = false;
        }
        TimeShiftResetButton.IsEnabled = ViewModel.TimeShiftSessionCs != 0;
    }

    /// <summary>
    /// 欄の中の並べ方。上の段（横に広い）: 1 段目に見出し・ずらした量・閉じる、2 段目に早める・遅らせる・説明。
    /// 右の列（狭い）: 見出しと閉じる、ずらした量、早める、遅らせる、説明を縦に。
    /// </summary>
    private void ApplyTimeShiftPlacement(bool right)
    {
        static void Place(FrameworkElement element, int row, int column, int span)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            Grid.SetColumnSpan(element, span);
        }
        if (right)
        {
            Place(TimeShiftTitle, 0, 0, 3);
            Place(TimeShiftCloseButton, 0, 3, 1);
            Place(TimeShiftAmountGroup, 1, 0, 4);
            Place(TimeShiftEarlierGroup, 2, 0, 4);
            Place(TimeShiftLaterGroup, 3, 0, 4);
            Place(TimeShiftHint, 4, 0, 4);
        }
        else
        {
            Place(TimeShiftTitle, 0, 0, 1);
            Place(TimeShiftAmountGroup, 0, 1, 2);
            Place(TimeShiftCloseButton, 0, 3, 1);
            Place(TimeShiftEarlierGroup, 1, 0, 1);
            Place(TimeShiftLaterGroup, 1, 1, 1);
            Place(TimeShiftHint, 1, 2, 2);
        }
    }
}
