using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.ViewModels;

/// <summary>
/// 字幕全体の時刻をずらす（再生パネルの「字幕全体の時刻をずらす」の欄。MainWindow.TimeShift.cs）。
/// 押すたびに全タブの時刻（<see cref="TimeShiftOperations"/>）を書き換えるので、再生を続けたまま字幕のプレビューで確かめられる。
/// 欄を開いてから閉じるまでを 1 まとまりとし、そのあいだのずらしは各タブの元に戻す履歴に 1 つだけ積む（Ctrl+Z 1 回で欄を開く前に戻る。タブごと）。
/// </summary>
public partial class MainViewModel
{
    /// <summary>欄を開いているか（ずらしのまとまりの中か）。</summary>
    public bool TimeShiftSessionActive { get; private set; }

    /// <summary>欄を開いてからずらした量の合計（10ms 単位。正なら遅らせた）。</summary>
    public int TimeShiftSessionCs { get; private set; }

    /// <summary>
    /// このまとまりで各タブの履歴に積んだ写し。タブの履歴のいちばん上がこれのままなら、あいだにほかの編集・元に戻すが無いので、
    /// 次のずらしは履歴に積まずに同じ 1 回にまとめる。
    /// </summary>
    private readonly Dictionary<TabState, LyricsDocument> _timeShiftUndoMarks = new();

    /// <summary>ずらした量を数え直した（元に戻す・やり直し、別のファイルを開いたなど。欄の表示を 0 にする）。</summary>
    public event EventHandler? TimeShiftSessionReset;

    /// <summary>欄を開いた（まとまりを始める）。</summary>
    public void BeginTimeShiftSession()
    {
        TimeShiftSessionActive = true;
        TimeShiftSessionCs = 0;
        _timeShiftUndoMarks.Clear();
    }

    /// <summary>欄を閉じた（まとまりを終える。ずらした時刻はそのまま）。</summary>
    public void EndTimeShiftSession()
    {
        TimeShiftSessionActive = false;
        TimeShiftSessionCs = 0;
        _timeShiftUndoMarks.Clear();
    }

    /// <summary>
    /// 欄を開いたまま、ずらした量を 0 から数え直す（元に戻す・やり直しでタブの一部だけが戻ったとき・別のファイルを開いたとき。
    /// そのあとの「0 秒に戻す」が、戻っていないタブとずれないように）。
    /// </summary>
    internal void RestartTimeShiftSession()
    {
        if (!TimeShiftSessionActive) return;
        bool changed = TimeShiftSessionCs != 0 || _timeShiftUndoMarks.Count > 0;
        TimeShiftSessionCs = 0;
        _timeShiftUndoMarks.Clear();
        if (changed) TimeShiftSessionReset?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>全タブの時刻を、いまからずらせる量の範囲（10ms 単位。<see cref="TimeShiftOperations.ShiftLimits"/>）。</summary>
    public (int Min, int Max) GetTimeShiftLimits() => TimeShiftOperations.ShiftLimits(GetAllTabs().Select(t => t.Document));

    /// <summary>
    /// 全タブの時刻（タイムタグ・行の表示時刻・時刻付きのルビ）を <paramref name="deltaCs"/>（10ms 単位。負なら早める）だけずらす。
    /// ずらせる範囲（−99:59.99〜99:59.99。0 秒より前はマイナスのまま持つ）に収めて、実際にずらした量を返す（0 = 動かせなかった）。
    /// 行リストの表示を直し、.tttproj（行の表示時刻）を保存する。チェック・字幕のプレビューの作り直しは呼び出し側で行う。
    /// </summary>
    public int ShiftAllTimes(int deltaCs)
    {
        var tabs = GetAllTabs();
        var (min, max) = TimeShiftOperations.ShiftLimits(tabs.Select(t => t.Document));
        int delta = Math.Clamp(deltaCs, min, max);
        if (delta == 0) return 0;

        foreach (var tab in tabs)
        {
            if (!HasTimes(tab.Document)) continue;
            bool sameRun = TimeShiftSessionActive &&
                           _timeShiftUndoMarks.TryGetValue(tab, out var mark) &&
                           tab.UndoStack.Count > 0 && ReferenceEquals(tab.UndoStack[^1], mark);
            if (!sameRun)
            {
                var snapshot = tab.Document.Clone();
                tab.UndoStack.Add(snapshot);
                if (tab.UndoStack.Count > MaxUndo) tab.UndoStack.RemoveAt(0);
                if (TimeShiftSessionActive) _timeShiftUndoMarks[tab] = snapshot;
            }
            tab.RedoStack.Clear();
            TimeShiftOperations.Shift(tab.Document, delta);
            tab.IsModified = true;
        }

        if (TimeShiftSessionActive) TimeShiftSessionCs += delta;
        foreach (var line in Lines)
        {
            line.RaiseAllChanged(); // 行リストの時刻（Document は表示中のタブの文書そのものなので、行はその場で書き換わっている）
            line.RaiseOverrideMark(); // 手で指定した表示時刻の説明
        }
        IsModified = true;
        UpdateTitle();
        SaveProject();
        return delta;
    }

    /// <summary>ずらす時刻を 1 つでも持つ文書か（タイムタグ・行の表示時刻・時刻付きのルビ）。</summary>
    private static bool HasTimes(LyricsDocument doc) =>
        TimeShiftOperations.TagRange(doc) is not null ||
        doc.Lines.Any(l => l.ShowBeginCs is not null || l.ShowEndCs is not null) ||
        doc.UnappliedRubyEntries.Any(r => r.StartCs is not null || r.EndCs is not null);

    /// <summary>ずらした量の表示（「+0.25 秒」「−1.00 秒」「0.00 秒」）。</summary>
    public static string FormatTimeShift(int cs)
    {
        string sign = cs > 0 ? "+" : cs < 0 ? "−" : "";
        int abs = Math.Abs(cs);
        return $"{sign}{abs / 100}.{abs % 100:00} 秒";
    }
}
