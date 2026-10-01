using Microsoft.UI.Dispatching;

namespace NicoKaraPrep.App;

/// <summary>
/// 行リストのときの右のパネル（<see cref="Views.LineSidePanel"/>）: フォント一覧で押したフォント設定を選んだ文字・行に指定し、
/// レイアウト設定の編集・適用を行リストの表示とプレビューへつなぐ。
/// </summary>
public sealed partial class MainWindow
{
    private DispatcherQueueTimer? _layoutRefreshTimer;

    private void InitializeLineSide()
    {
        LineSide.Initialize(ViewModel);
        LineSide.FontPicked += (_, name) => ApplyFontFromPanel(name);
        LineSide.LayoutApplyRequested += (_, name) => ApplyLineLayout(name);
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
