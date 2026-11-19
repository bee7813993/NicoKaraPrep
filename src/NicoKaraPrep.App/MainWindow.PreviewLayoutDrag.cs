using NicoKaraPrep.App.Views;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App;

/// <summary>
/// メディア再生の字幕のプレビューで字幕の行をドラッグして、その行のページのレイアウト設定（上下余白・左右余白・行間）を変える。
/// レイアウト設定はアプリ共通なので、同じレイアウト設定を使うほかのページ・ほかの曲も変わる。
/// 直後なら Ctrl+Z で戻せる（歌詞の 元に戻す とは別に覚え、そのあとに歌詞を変えたり戻したりしたら、戻せるのは歌詞のほうになる）。
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>ドラッグで変えた 1 回分（前と後のレイアウト設定の一覧と、そのときの歌詞の履歴の数）。</summary>
    private sealed record PreviewLayoutChange(List<N3Layout> Before, List<N3Layout> After, string Name, string Change, long Serial);

    private readonly List<PreviewLayoutChange> _previewLayoutUndo = new();
    private readonly List<PreviewLayoutChange> _previewLayoutRedo = new();

    private void InitializePreviewLayoutDrag()
    {
        SubtitlePreview.LayoutDragEnabled = true;
        SubtitlePreview.LayoutDragStarting += OnPreviewLayoutDragStarting;
        SubtitlePreview.LayoutDragCompleted += OnPreviewLayoutDragCompleted;
    }

    private void OnPreviewLayoutDragStarting(object? sender, LayoutDragStartingEventArgs e)
    {
        if (!ViewModel.N3ProjSettings.MergeLayouts)
        {
            e.Cancel = true;
            ViewModel.StatusText = "この曲は、編集したレイアウト設定をベースへ反映しない設定のため、字幕のプレビューでは動かせません" +
                                   "（エクスポート(X) の n3proj の書き出し画面の「NicoKaraPrep で編集したレイアウト設定をベースへ反映する」を入れると動かせます）";
            return;
        }
        PauseMedia(); // 再生中は行が出たり消えたりするので止める
        string targets = N3LayoutDrag.Targets(e.Line.Layout, e.Line.Row, e.Line.RowsInPage);
        ViewModel.StatusText = targets.Length > 0
            ? $"レイアウト設定「{e.Line.Layout.Name}」の{targets}を変えます（離すと決まります。同じレイアウト設定を使うページすべてが変わります）"
            : $"この段は動かせません（レイアウト設定「{e.Line.Layout.Name}」の中央寄せの真ん中の段・中央の段）";
    }

    private void OnPreviewLayoutDragCompleted(object? sender, LayoutDragCompletedEventArgs e)
    {
        string change = N3LayoutDrag.DescribeChange(e.Before, e.After);
        var before = N3LayoutHistory.Copy(ViewModel.Settings.N3Layouts);
        if (change.Length == 0 || ViewModel.EnsureEditableLayout(e.After.Name) is not { } layout)
        {
            SubtitlePreview.CancelLayoutDrag();
            return;
        }
        layout.VerticalMarginPx = e.After.VerticalMarginPx;
        layout.HorizontalMarginPx = e.After.HorizontalMarginPx;
        layout.LineSpacePx = e.After.LineSpacePx;
        ViewModel.SaveLayouts(); // 行リストのレイアウトの表示・プレビューを作り直す
        _previewLayoutUndo.Add(new PreviewLayoutChange(before, N3LayoutHistory.Copy(ViewModel.Settings.N3Layouts), e.After.Name, change, ViewModel.UndoSerial));
        if (_previewLayoutUndo.Count > 50) _previewLayoutUndo.RemoveAt(0);
        _previewLayoutRedo.Clear();
        ViewModel.StatusText = $"レイアウト設定「{e.After.Name}」の{change}にしました（字幕のプレビューのドラッグ。続けて Ctrl+Z で戻せます。" +
                               "同じレイアウト設定を使うページすべてが変わります）";
    }

    /// <summary>
    /// 直前の操作が字幕のプレビューのドラッグなら、レイアウト設定を前の値に戻す（戻したら true）。
    /// そのあとに歌詞の履歴が動いた・ほかでレイアウト設定が変わったときは、覚えていた分を捨てて false（歌詞を戻す）。
    /// </summary>
    private bool TryUndoPreviewLayoutDrag()
    {
        if (_previewLayoutUndo.Count == 0) return false;
        var top = _previewLayoutUndo[^1];
        if (top.Serial != ViewModel.UndoSerial || !SameLayouts(top.After))
        {
            _previewLayoutUndo.Clear();
            _previewLayoutRedo.Clear();
            return false;
        }
        _previewLayoutUndo.RemoveAt(_previewLayoutUndo.Count - 1);
        RestoreLayouts(top.Before);
        _previewLayoutRedo.Add(top);
        ViewModel.StatusText = $"レイアウト設定「{top.Name}」の変更（{top.Change}）を元に戻しました（Ctrl+Y でやり直し）";
        return true;
    }

    /// <summary>直前に戻したのが字幕のプレビューのドラッグなら、やり直す（やり直したら true）。</summary>
    private bool TryRedoPreviewLayoutDrag()
    {
        if (_previewLayoutRedo.Count == 0) return false;
        var top = _previewLayoutRedo[^1];
        if (top.Serial != ViewModel.UndoSerial || !SameLayouts(top.Before))
        {
            _previewLayoutRedo.Clear();
            return false;
        }
        _previewLayoutRedo.RemoveAt(_previewLayoutRedo.Count - 1);
        RestoreLayouts(top.After);
        _previewLayoutUndo.Add(top);
        ViewModel.StatusText = $"レイアウト設定「{top.Name}」の変更（{top.Change}）をやり直しました";
        return true;
    }

    private bool SameLayouts(List<N3Layout> layouts) =>
        N3LayoutHistory.Signature(ViewModel.Settings.N3Layouts) == N3LayoutHistory.Signature(layouts);

    private void RestoreLayouts(List<N3Layout> layouts)
    {
        ViewModel.Settings.N3Layouts.Clear();
        ViewModel.Settings.N3Layouts.AddRange(N3LayoutHistory.Copy(layouts));
        ViewModel.SaveLayouts();
    }
}
