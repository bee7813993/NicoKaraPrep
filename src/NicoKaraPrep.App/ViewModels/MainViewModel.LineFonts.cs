using Microsoft.UI.Xaml.Media;
using NicoKaraPrep.App.Services;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.ViewModels;

/// <summary>
/// 行リストの「フォント設定」欄: 各行に当たるフォント設定を、n3proj の書き出しと同じ決め方で出す
/// （ベースの n3proj のフォント設定に書き出すフォント設定を合わせた並び・全タブを順に通して引き継ぐ・既定のフォント設定）。
/// </summary>
public partial class MainViewModel
{
    private (string Path, DateTime Stamp, List<N3FontSet> Sets)? _baseFontSetsCache;

    /// <summary>色見本のブラシ（同じ配色なら同じものを使い、チェックのたびに行の表示を作り直さないように）。</summary>
    private readonly Dictionary<string, Brush> _lineFontBrushes = new(StringComparer.Ordinal);

    /// <summary>
    /// 書き出しのベースにする n3proj（<see cref="SuggestN3ProjBasePath"/>）のフォント設定（LyricsFonts の順）。ベースが無い・読めなければ空。
    /// ファイルのパスと更新日時が同じあいだは読み直さない。
    /// </summary>
    private List<N3FontSet> GetBaseFontSets()
    {
        string? path = SuggestN3ProjBasePath();
        if (path is null) return new();
        try
        {
            var stamp = File.GetLastWriteTimeUtc(path);
            if (_baseFontSetsCache is { } c && c.Path == path && c.Stamp == stamp) return c.Sets;
            var sets = N3ProjFormat.ReadFontSets(path);
            _baseFontSetsCache = (path, stamp, sets);
            return sets;
        }
        catch (Exception)
        {
            return new(); // ベースが読めなくても表示は続ける
        }
    }

    /// <summary>
    /// 書き出しで行のフォントを決めるときのフォント設定名の並び（<see cref="N3ProjWriter.ExportFontNames"/>）と既定のフォント設定名。
    /// 行リストの「フォント設定」欄とフォント設定ビューの使用状況は、これで書き出しと同じ結果を出す。
    /// </summary>
    public (List<string> Names, string? Default) GetExportFontNames() =>
        (N3ProjWriter.ExportFontNames(GetBaseFontSets().Select(f => f.Name), ExportFontSets, N3ProjSettings.MergeFontSets),
         N3ProjSettings.DefaultFontSetName is { Length: > 0 } d ? d : null);

    /// <summary>
    /// 行リストの各行に、当たるフォント設定を出す（チェックのたびに呼ぶ。行のフォントの手動指定・書き出し設定を変えたときは、
    /// ステータスバーの知らせをチェック結果で消さないよう、これだけを呼ぶ）。
    /// </summary>
    public void UpdateLineFonts()
    {
        var tabs = GetAllTabs();
        int active = -1;
        for (int i = 0; i < tabs.Count; i++)
        {
            if (ReferenceEquals(tabs[i], _activeTab)) active = i;
        }
        if (active < 0) return;

        var baseSets = GetBaseFontSets();
        var export = ExportFontSets;
        bool merge = N3ProjSettings.MergeFontSets;
        var names = N3ProjWriter.ExportFontNames(baseSets.Select(f => f.Name), export, merge);
        string? def = N3ProjSettings.DefaultFontSetName is { Length: > 0 } d ? d : null;
        var lines = N3FontResolver.ResolveLines(tabs.Select(t => t.Document), names, def, continueAcrossLines: true)[active];

        // 色見本は、書き出しで合わせるフォント設定（曲専用・アプリ共通）を先に使い、指定の無い箇所はベースのものを使う
        var own = new Dictionary<string, N3FontSet>(StringComparer.Ordinal);
        if (merge)
        {
            foreach (var f in export) own.TryAdd(f.Name, f);
        }
        var fromBase = new Dictionary<string, N3FontSet>(StringComparer.Ordinal);
        foreach (var f in baseSets) fromBase.TryAdd(f.Name, f);

        if (_lineFontBrushes.Count > 512) _lineFontBrushes.Clear();
        var runs = new Dictionary<int, LineFontRun>();
        LineFontRun RunOf(int index)
        {
            if (runs.TryGetValue(index, out var run)) return run;
            string name = names[index];
            own.TryGetValue(name, out var o);
            fromBase.TryGetValue(name, out var b);
            run = new LineFontRun(name.Length > 0 ? name : "（名前なし）", Swatch(o, b, 0), Swatch(o, b, N3FontDetail.BeforeOffset));
            runs[index] = run;
            return run;
        }

        for (int i = 0; i < Lines.Count; i++)
        {
            var fonts = i < lines.Count ? lines[i] : null;
            Lines[i].AppliedFont = fonts is null || fonts.Runs.Count == 0
                ? LineFontDisplay.None
                : LineFontDisplay.Create(fonts.Runs.Select(RunOf).ToList(), fonts.Manual, Lines[i].Model.FontSetName, fonts.CharManual);
        }
    }

    /// <summary>配色 1 箇所の色見本（合わせるフォント設定に指定が無ければベースのもの）。どちらにも無ければ null。</summary>
    private Brush? Swatch(N3FontSet? own, N3FontSet? fromBase, int slot)
    {
        var brush = own is not null && !own.Detail.Brushes[slot].IsUnset
            ? own.Detail.Brushes[slot]
            : fromBase?.Detail.Brushes[slot] ?? own?.Detail.Brushes[slot];
        if (brush is null) return null;
        string key = $"{brush.Type}|{brush.Color}|{brush.AlphaPercent}|{brush.BitmapPath}|{brush.BitmapScale}|"
            + string.Join(";", brush.Stops.Select(s => $"{s.Position:R},{s.Color},{s.AlphaPercent}"));
        if (!_lineFontBrushes.TryGetValue(key, out var result))
        {
            result = N3BrushPreview.Create(brush);
            _lineFontBrushes[key] = result;
        }
        return result;
    }
}
