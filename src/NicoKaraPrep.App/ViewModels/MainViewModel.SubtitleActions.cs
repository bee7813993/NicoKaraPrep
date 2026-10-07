using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.ViewModels;

/// <summary>
/// 字幕アクション（ニコカラメーカー3 の行のアクション）の指定。行ごとの指定（<see cref="LyricsLine.SubtitleAction"/>、null = 曲の既定）と、
/// 曲の既定（<see cref="Core.Project.N3ProjSongSettings.SubtitleAction"/>、null = 自動: ベース → ニコカラメーカー3 の設定 → 文字単位フェード）。
/// 右パネルの「字幕アクション」タブ・行設定の欄・曲の既定のポップアップ・MCP から使う。
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// 行に字幕アクションを手で指定する（null = 曲の既定に戻す）。<paramref name="wholePage"/> なら、ニコカラメーカー3 のページ単位の使い方に合わせて
    /// 指定した行と同じページの行すべてにそろえる（ページの数え方は書き出しと同じ）。元に戻す（Ctrl+Z）は 1 回で戻る。変えた行の数を返す。
    /// </summary>
    public int SetLinesSubtitleAction(IReadOnlyList<int> indexes, N3SubtitleAction? action, bool wholePage = true)
    {
        var targets = new SortedSet<int>();
        if (wholePage)
        {
            var show = CreateShowTimeSettings(_activeTab.Name);
            var pages = Document.GetPages(show.PageMode, show.FixedLineCount);
            foreach (int i in indexes)
            {
                if (i < 0 || i >= Document.Lines.Count || Document.Lines[i].IsEmpty) continue;
                var page = pages.FirstOrDefault(p => p.Contains(i));
                foreach (int k in page ?? new List<int> { i }) targets.Add(k);
            }
        }
        else
        {
            foreach (int i in indexes)
            {
                if (i >= 0 && i < Document.Lines.Count && !Document.Lines[i].IsEmpty) targets.Add(i);
            }
        }
        var changed = targets.Where(i => !N3SubtitleAction.AreSame(Document.Lines[i].SubtitleAction, action)).ToList();
        if (changed.Count == 0) return 0;
        PushUndo();
        foreach (int i in changed) Document.Lines[i].SubtitleAction = action?.Clone();
        MarkModified();
        SaveProject();
        UpdateLineActions();
        return changed.Count;
    }

    /// <summary>行リストの「字幕アクション」の列（<see cref="LineViewModel.ActionText"/>）を、行の指定と曲の既定で作り直したとき。</summary>
    public event EventHandler? LineActionsUpdated;

    /// <summary>
    /// 行リストの「字幕アクション」の列を、今の行の指定と曲の既定で作り直す（手で指定した行は「✎フェードイン」、指定の無い行は曲の既定の名前）。
    /// 行の一覧・フォントの表示の作り直し、字幕アクションの指定・曲の既定・行の設定の解除のあとに呼ぶ。
    /// </summary>
    public void UpdateLineActions()
    {
        var def = ResolveCurrentDefaultSubtitleAction(out var source);
        string defName = N3SubtitleActionCatalog.DisplayName(def.Id);
        string defTip = $"曲の既定の字幕アクション: {DescribeSubtitleAction(def)}（{DefaultSubtitleActionSourceLabel(source)}）";
        foreach (var line in Lines)
        {
            var model = line.Model;
            if (model.IsEmpty)
            {
                line.ActionText = "";
                line.ActionToolTip = "";
                line.IsManualAction = false;
            }
            else if (model.SubtitleAction is { Id.Length: > 0 } action)
            {
                line.ActionText = $"✎{N3SubtitleActionCatalog.DisplayName(action.Id)}";
                line.ActionToolTip = $"この行に手で指定した字幕アクション: {DescribeSubtitleAction(action)}\n{defTip}\n" +
                                     "右のパネル「字幕アクション」・行設定の欄で変えられます（「（既定）」で曲の既定に戻ります）";
                line.IsManualAction = true;
            }
            else
            {
                line.ActionText = defName;
                line.ActionToolTip = $"{defTip}\nこの行は手で指定していないので、曲の既定を使います（右のパネル「字幕アクション」・行設定の欄で行ごとに指定できます）";
                line.IsManualAction = false;
            }
        }
        LineActionsUpdated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 字幕アクションの短い説明（「文字単位フェード」「フェードイン/アウト（500/250ms）」など）。設定値は、ニコカラメーカー3 の設定
    /// （AddOns。無い項目は既定値）と違う所だけを添える（ふだん使っている設定のままなら名前だけ）。画面・MCP の説明はこれを使う。
    /// </summary>
    public string DescribeSubtitleAction(N3SubtitleAction? action) => N3SubtitleActionCatalog.Describe(action, Nkm3Env?.AddOnSettings);

    /// <summary>
    /// ページ（行）に指定する字幕アクションの値。曲の既定（自動を含む。<see cref="ResolveCurrentDefaultSubtitleAction"/>）と同じ種類なら
    /// その値の写し（既定のままのページと同じ動きになる）、違う種類ならニコカラメーカー3 の設定（AddOns）の値、無ければ既定値。
    /// 右パネル・レイアウト設定ビュー・MCP の set_line_action で同じものを使う（どこで選んでも同じ値を書くように）。
    /// </summary>
    public N3SubtitleAction CreatePageSubtitleAction(string id)
    {
        var song = ResolveCurrentDefaultSubtitleAction(out _);
        return song.Id == id ? song : N3SubtitleActionCatalog.CreateDefault(id, Nkm3Env?.AddOnSettings.GetValueOrDefault(id));
    }

    /// <summary>
    /// 曲の既定の字幕アクションを変える（null = 自動）。.tttproj に保存する（歌詞の元に戻すの対象にはしない）。変わったら true。
    /// </summary>
    public bool SetSongDefaultSubtitleAction(N3SubtitleAction? action)
    {
        if (N3SubtitleAction.AreSame(N3ProjSettings.SubtitleAction, action)) return false;
        N3ProjSettings.SubtitleAction = action?.Clone();
        SaveProject();
        UpdateLineActions();
        SongDefaultSubtitleActionChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>曲の既定の字幕アクションが変わった（レイアウト設定ビューと右パネルの表示を合わせる）。</summary>
    public event EventHandler? SongDefaultSubtitleActionChanged;
}
