using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.ViewModels;

/// <summary>フォント設定ビューの右ペイン: 使用状況（全タブの文字数・行）・検証・選択中の行への割り当て。</summary>
public sealed partial class FontSettingsViewModel
{
    private DispatcherQueueTimer? _analysisTimer;

    /// <summary>ビューに入ったときの行リストの選択（表示中のタブの行番号）。「選択中の行に割り当て」の対象。</summary>
    private IReadOnlyList<int> _lineSelection = Array.Empty<int>();

    /// <summary>選択中のフォント設定を使っている行（タブ名・行番号・行の先頭）。</summary>
    public ObservableCollection<FontUsageLine> UsageLines { get; } = new();

    /// <summary>検証の結果（書き出しに使うフォント設定と、行が参照している名前）。</summary>
    public ObservableCollection<FontIssueItem> Issues { get; } = new();

    [ObservableProperty]
    private string usageSummary = "";

    [ObservableProperty]
    private string issueSummary = "";

    [ObservableProperty]
    private bool canAssign;

    [ObservableProperty]
    private string assignText = "選択中の行に割り当て";

    [ObservableProperty]
    private string assignNote = "";

    /// <summary>曲専用のフォント設定があるのに、歌詞ファイルが未保存で .tttproj に保存できないときの注意。</summary>
    [ObservableProperty]
    private string songSaveNote = "";

    [ObservableProperty]
    private bool hasSongSaveNote;

    /// <summary>ビューに入るときに、行リストの選択（表示中のタブの行番号）を受け取る。</summary>
    public void SetLineSelection(IReadOnlyList<int> indexes)
    {
        _lineSelection = indexes.ToList();
        UpdateAssignState();
    }

    /// <summary>使用状況と検証の計算を予約する（編集が続くあいだはまとめる）。</summary>
    private void ScheduleAnalysis()
    {
        if (_analysisTimer is null)
        {
            _analysisTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _analysisTimer.Interval = TimeSpan.FromMilliseconds(300);
            _analysisTimer.IsRepeating = false;
            _analysisTimer.Tick += (_, _) => RunAnalysis();
        }
        _analysisTimer.Stop();
        _analysisTimer.Start();
    }

    /// <summary>書き出しと同じ条件（全タブ・行をまたいで引き継ぐ・既定のフォント設定）で使われ方を計算する。</summary>
    private (List<LyricsDocument> Docs, List<N3FontSet> Export, List<string> Names, string? Default) UsageContext()
    {
        var docs = _main.GetAllTabs().Select(t => t.Document).ToList();
        var export = _main.ExportFontSets;
        var names = export.Select(f => f.Name).ToList();
        string? def = _main.N3ProjSettings.DefaultFontSetName is { Length: > 0 } d ? d : null;
        return (docs, export, names, def);
    }

    /// <summary>そのフォント設定名が 1 文字以上に適用される行（文書の番号・行の添字）。</summary>
    private IReadOnlyList<(int Document, int Line)> LinesUsingFont(string name)
    {
        var (docs, _, names, def) = UsageContext();
        return N3FontResolver.LinesUsing(docs, names, def, continueAcrossLines: true, name);
    }

    /// <summary>一覧の使用文字数・選択中のフォント設定の使用状況・検証を計算し直す。</summary>
    public void RunAnalysis()
    {
        _analysisTimer?.Stop();
        var (docs, export, names, def) = UsageContext();
        var first = UpdateItemUsage(docs, export, names, def);
        UpdateUsageLines(docs, names, def, first);
        UpdateIssues(export);
        UpdateAssignState();

        HasSongSaveNote = Song.Count > 0 && !_main.CanSaveSongFontSets;
        SongSaveNote = HasSongSaveNote
            ? "歌詞ファイルが保存されていないため、この曲専用のフォント設定はまだ保存されていません（歌詞ファイルを保存すると一緒に保存されます）"
            : "";
    }

    /// <summary>一覧の各行の使用文字数と「曲専用で上書き」を計算する。戻り値は名前ごとに書き出しで使われるフォント設定。</summary>
    private Dictionary<string, N3FontSet> UpdateItemUsage(List<LyricsDocument> docs, List<N3FontSet> export, List<string> names, string? def)
    {
        var usage = N3FontResolver.CountUsage(docs, names, def, continueAcrossLines: true);

        // 同じ名前が複数あるときは、書き出しで使われる先のものに数える
        var first = new Dictionary<string, N3FontSet>(StringComparer.Ordinal);
        foreach (var f in export) first.TryAdd(f.Name, f);
        var exported = new HashSet<N3FontSet>(export, ReferenceEqualityComparer.Instance);
        foreach (var item in _all)
        {
            item.IsOverridden = !item.IsSong && !exported.Contains(item.Font);
            item.Usage = first.TryGetValue(item.Font.Name, out var used) && ReferenceEquals(used, item.Font)
                ? usage.GetValueOrDefault(item.Font.Name)
                : 0;
        }
        return first;
    }

    /// <summary>一覧の各行の使用文字数だけを計算する（絞り込みの「未使用」の前に）。</summary>
    private void UpdateItemUsage()
    {
        var (docs, export, names, def) = UsageContext();
        UpdateItemUsage(docs, export, names, def);
    }

    private void UpdateUsageLines(List<LyricsDocument> docs, List<string> names, string? def, Dictionary<string, N3FontSet> first)
    {
        UsageLines.Clear();
        if (SelectedItem is not { } item)
        {
            UsageSummary = "";
            return;
        }
        if (!first.TryGetValue(item.Font.Name, out var used) || !ReferenceEquals(used, item.Font))
        {
            UsageSummary = item.IsOverridden
                ? $"同じ名前の曲専用のフォント設定「{item.Font.Name}」が優先されるため、この曲の書き出しでは使われません"
                : $"同じ名前のフォント設定「{item.Font.Name}」が先にあるため、書き出しではそちらが使われます";
            return;
        }

        var tabs = _main.GetAllTabs();
        var lines = N3FontResolver.LinesUsing(docs, names, def, continueAcrossLines: true, item.Font.Name);
        UsageSummary = $"使用: {item.Usage} 文字 / {lines.Count} 行";
        foreach (var (d, l) in lines)
        {
            var line = docs[d].Lines[l];
            string text = line.GetDisplayText();
            if (text.Length > 20) text = text[..20] + "…";
            string tab = d < tabs.Count ? tabs[d].Name : "";
            string manual = line.FontSetName == item.Font.Name ? "（手動指定）" : "";
            UsageLines.Add(new FontUsageLine($"{tab} {l + 1} 行目{manual}: {text}"));
        }
    }

    private void UpdateIssues(List<N3FontSet> export)
    {
        Issues.Clear();
        var issues = N3FontLibrary.Validate(export, _main.CollectReferencedFontNames());
        foreach (var issue in issues.OrderByDescending(i => i.Severity)) Issues.Add(new FontIssueItem(issue));
        IssueSummary = issues.Count == 0 ? "問題は見つかりませんでした" : $"{issues.Count} 件";
    }

    private void UpdateAssignState()
    {
        int n = _lineSelection.Count;
        string? name = SelectedItem?.Font.Name;
        CanAssign = n > 0 && !string.IsNullOrEmpty(name);
        AssignText = n > 0 ? $"選択中の {n} 行に割り当て" : "選択中の行に割り当て";
        AssignNote = n == 0 ? "行リストで行を選んでからこのビューを開くと、選んだ行にこのフォント設定を割り当てられます" : "";
    }

    /// <summary>選択中のフォント設定を、ビューに入ったときに選ばれていた行に手動指定する（歌詞の 元に戻す 1 回で戻る）。</summary>
    public void AssignToSelectedLines()
    {
        if (SelectedItem is not { } item || !CanAssign) return;
        int n = _main.SetLinesFontSet(_lineSelection, item.Font.Name);
        SetStatus(n > 0
            ? $"{n} 行にフォント設定「{item.Font.Name}」を指定しました（行リストへ戻って Ctrl+Z で元に戻せます）"
            : $"選択中の行はすでにフォント設定「{item.Font.Name}」を指定しています");
        RunAnalysis();
    }
}
