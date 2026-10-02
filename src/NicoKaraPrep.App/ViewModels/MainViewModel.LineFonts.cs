using System.Text.Json;
using Microsoft.UI.Xaml.Media;
using NicoKaraPrep.App.Services;
using NicoKaraPrep.App.Services.Subtitles;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;
using NicoKaraPrep.Core.Validation;

namespace NicoKaraPrep.App.ViewModels;

/// <summary>
/// 行リストの「フォント設定」欄・歌詞の字幕の見た目・字幕のプレビュー: 各行に当たるフォント設定を、n3proj の書き出しと同じ決め方で出す
/// （ベースの n3proj のフォント設定に書き出すフォント設定を合わせた並び・全タブを順に通して引き継ぐ・既定のフォント設定）。
/// </summary>
public partial class MainViewModel
{
    /// <summary>書き出しのベースにする n3proj の中身（フォント設定・レイアウト設定・画面の大きさ）。</summary>
    private sealed record BaseProject(List<N3FontSet> FontSets, List<N3LayoutSettings> Layouts, int Width, int Height);

    private (string Path, DateTime Stamp, BaseProject Project)? _baseProjectCache;

    /// <summary>色見本のブラシ（同じ配色なら同じものを使い、チェックのたびに行の表示を作り直さないように）。</summary>
    private readonly Dictionary<string, Brush> _lineFontBrushes = new(StringComparer.Ordinal);

    /// <summary>フォント設定の中身（JSON）ごとの番号（行の配置の使い回しのキーに使う）。</summary>
    private readonly Dictionary<string, int> _fontContentIds = new(StringComparer.Ordinal);

    /// <summary>字幕の見た目で描く共通の材料（中身が同じあいだは同じものを使う）。</summary>
    private SubtitleContext? _subtitleContext;

    /// <summary>字幕のプレビューの材料（チェックのたびに作り直す。プレビューを切っていれば null）。</summary>
    public SubtitlePreviewModel? PreviewModel { get; private set; }

    /// <summary><see cref="PreviewModel"/> を作り直した。</summary>
    public event EventHandler? PreviewModelChanged;

    /// <summary>
    /// 表示中のタブの行の横幅の判定（行の番号ごと）。字幕のプレビューと同じ並べ方で測り、ページのレイアウト設定の左右余白で判定する。
    /// <see cref="UpdateLineFonts"/> で作り直す（チェックのたびに呼ばれる）。
    /// </summary>
    private Dictionary<int, LineWidthResult> _lineWidths = new();

    /// <summary>
    /// 書き出しのベースにする n3proj（<see cref="SuggestN3ProjBasePath"/>）の中身。ベースが無い・読めなければ null。
    /// ファイルのパスと更新日時が同じあいだは読み直さない。
    /// </summary>
    private BaseProject? GetBaseProject()
    {
        string? path = SuggestN3ProjBasePath();
        if (path is null) return null;
        try
        {
            var stamp = File.GetLastWriteTimeUtc(path);
            if (_baseProjectCache is { } c && c.Path == path && c.Stamp == stamp) return c.Project;
            var root = N3ProjFormat.ReadJsonObject(path);
            var (width, height) = N3LayoutReader.ScreenSize(root);
            var project = new BaseProject(N3ProjFormat.ReadFontSets(root), N3LayoutReader.Read(root, height), width, height);
            _baseProjectCache = (path, stamp, project);
            return project;
        }
        catch (Exception)
        {
            return null; // ベースが読めなくても表示は続ける
        }
    }

    /// <summary>書き出しのベースにする n3proj のフォント設定（LyricsFonts の順）。ベースが無い・読めなければ空。</summary>
    private List<N3FontSet> GetBaseFontSets() => GetBaseProject()?.FontSets ?? new();

    /// <summary>
    /// 書き出しで行のフォントを決めるときのフォント設定名の並び（<see cref="N3ProjWriter.ExportFontNames"/>）と既定のフォント設定名。
    /// 行リストの「フォント設定」欄とフォント設定ビューの使用状況は、これで書き出しと同じ結果を出す。
    /// </summary>
    public (List<string> Names, string? Default) GetExportFontNames() =>
        (N3ProjWriter.ExportFontNames(GetBaseFontSets().Select(f => f.Name), ExportFontSets, N3ProjSettings.MergeFontSets),
         N3ProjSettings.DefaultFontSetName is { Length: > 0 } d ? d : null);

    /// <summary>
    /// 行リストの各行に、当たるフォント設定と字幕の見た目で描く材料を出し、字幕のプレビューの材料を作り直す（チェックのたびに呼ぶ。
    /// 行のフォントの手動指定・書き出し設定を変えたときは、ステータスバーの知らせをチェック結果で消さないよう、これだけを呼ぶ）。
    /// </summary>
    public void UpdateLineFonts()
    {
        var tabs = GetAllTabs();
        int active = -1;
        for (int i = 0; i < tabs.Count; i++)
        {
            if (ReferenceEquals(tabs[i], _activeTab)) active = i;
        }
        if (active < 0)
        {
            _lineWidths = new();
            return;
        }

        var baseProject = GetBaseProject();
        var baseSets = baseProject?.FontSets ?? new List<N3FontSet>();
        var export = ExportFontSets;
        bool merge = N3ProjSettings.MergeFontSets;
        var names = N3ProjWriter.ExportFontNames(baseSets.Select(f => f.Name), export, merge);
        string? def = N3ProjSettings.DefaultFontSetName is { Length: > 0 } d ? d : null;
        var resolved = N3FontResolver.ResolveLines(tabs.Select(t => t.Document), names, def, continueAcrossLines: true);
        var lines = resolved[active];

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

        // 字幕の見た目で描くフォント設定（書き出しでベースと合わせた結果。名前ごとに 1 回だけ作る）
        bool styled = Settings.LineListStyledLyrics;
        var context = GetSubtitleContext();
        var effective = new N3FontSet?[names.Count];
        var fontIds = new int[names.Count];
        for (int k = 0; k < names.Count; k++)
        {
            own.TryGetValue(names[k], out var o);
            fromBase.TryGetValue(names[k], out var b);
            effective[k] = o is not null ? N3FontLibrary.MergeForPreview(o, b) : b ?? context.DefaultFont;
            fontIds[k] = FontContentId(effective[k]!);
        }

        LineRenderSource Source(LyricsLine line, N3FontResolver.LineFonts fonts, SubtitleSpacing spacing)
        {
            var unitFonts = fonts.Units.Select(u => effective[u]).ToArray();
            string key = $"{context.Key}|{TextEditModeFormat.WriteLyricLine(line)}|{string.Join(",", fonts.Units.Select(u => fontIds[u]))}";
            return new LineRenderSource(line, unitFonts, context, spacing, key);
        }

        for (int i = 0; i < Lines.Count; i++)
        {
            var fonts = i < lines.Count ? lines[i] : null;
            var line = Lines[i];
            line.AppliedFont = fonts is null || fonts.Runs.Count == 0
                ? LineFontDisplay.None
                : LineFontDisplay.Create(fonts.Runs.Select(RunOf).ToList(), fonts.Manual, line.Model.FontSetName, fonts.CharManual);

            if (!styled || fonts is null || fonts.Runs.Count == 0 || line.Model.GetDisplayText().Length == 0)
            {
                line.SetRenderSource(null);
                continue;
            }
            line.SetRenderSource(Source(line.Model, fonts, SubtitleSpacing.Default));
        }

        // 字幕のプレビュー（全タブ。書き出しと同じ表示時刻・ページ・レイアウト）と、行リストのレイアウト・横幅の表示
        var (screenWidth, screenHeight) = GetScreenSize();
        var layouts = GetEffectiveLayouts();
        var infos = layouts.Select(l => l.Info).ToList();
        var layoutTexts = new Dictionary<int, (string Text, string Tip)>();
        var previewLines = new List<PreviewLine>();
        var widths = new Dictionary<int, LineWidthResult>();
        static SubtitleSpacing SpacingOf(N3LayoutSettings layout) =>
            new((float)layout.LyricsIntervalPx, (float)layout.RubyIntervalPx, (float)layout.LyricsAndRubyIntervalPx, layout.RubyAlignment, layout.AllowBiting);
        for (int t = 0; t < tabs.Count; t++)
        {
            var doc = tabs[t].Document;
            var plans = N3ShowTimePlanner.Plan(doc, CreateShowTimeSettings(tabs[t].Name));
            if (plans.Count == 0 && t != active) continue;
            string? fixedLayout = N3ProjSettings.TabLayouts.GetValueOrDefault(tabs[t].Name) is { Length: > 0 } fl ? fl : null;
            var resolver = new N3ProjWriter.LayoutResolver(infos, fixedLayout, Nkm3Env?.LayoutSelectableBegin, Nkm3Env?.LayoutSelectableEnd, new List<string>(), tabs[t].Name);
            var pages = plans.GroupBy(p => p.Value.PageIndex).ToDictionary(g => g.Key, g =>
            {
                // ページの中で最初に手動指定のある行のレイアウト（無い名前なら行数から選ぶ。書き出しと同じ）
                string? manual = g.OrderBy(p => p.Key)
                    .Select(p => doc.Lines[p.Key].LayoutName)
                    .FirstOrDefault(n => n is { Length: > 0 } && resolver.FindIndex(n) is not null);
                int index = manual is not null ? resolver.FindIndex(manual)!.Value : resolver.Resolve(g.Count());
                return (Count: g.Count(), Rows: g.Max(p => p.Value.Row), Layout: Math.Clamp(index, 0, layouts.Count - 1), Manual: manual is not null);
            });
            foreach (var (index, plan) in plans.OrderBy(p => p.Key))
            {
                var line = doc.Lines[index];
                var page = pages[plan.PageIndex];
                var layout = layouts[page.Layout];
                if (t == active)
                {
                    string how = page.Manual ? "このページに手動で指定したレイアウト" : fixedLayout is not null && resolver.FindIndex(fixedLayout) is not null
                        ? "タブに固定したレイアウト（n3proj の書き出しの設定）"
                        : $"ページの行数（{page.Count} 行）から自動で選んだレイアウト";
                    layoutTexts[index] = ((page.Manual ? "✎" : "") + layout.Name, $"{how}: {layout.Name}");
                }
                if (line.IsEmpty || line.GetDisplayText().Length == 0 || index >= resolved[t].Count) continue;
                var fonts = resolved[t][index];
                if (fonts.Runs.Count == 0) continue;
                var source = Source(line, fonts, SpacingOf(layout));
                // 1 行のページを上の段へ上げる（PageRowMap）のは、レイアウトにその段があるときだけ（1 行のレイアウト「コーラス1行」などでは下の段のまま。
                // ニコカラメーカー3 の出力で確認）。行が多いページはレイアウトの行数を超えても積み上げる
                int maxRow = Math.Max(layout.LineCount, page.Count);
                previewLines.Add(new PreviewLine(
                    source, plan.BeginMs, plan.EndMs, t, plan.PageIndex, Math.Min(plan.Row, maxRow), Math.Min(Math.Max(page.Rows, page.Count), maxRow),
                    page.Count, layout, N3WipeTimeline.Groups(line)));
                if (t == active)
                {
                    widths[index] = LineWidthValidator.Evaluate(index, source.GetLayout().Width, screenWidth, layout.HorizontalMarginPx);
                }
            }

            if (t == active)
            {
                // 表示時刻の決まらない行（タイムタグの無い行など）も、手動指定のレイアウト（無ければ 1 行のページのレイアウト）で測る
                var single = layouts[Math.Clamp(resolver.Resolve(1), 0, layouts.Count - 1)];
                for (int i = 0; i < doc.Lines.Count && i < resolved[t].Count; i++)
                {
                    if (widths.ContainsKey(i)) continue;
                    var line = doc.Lines[i];
                    if (line.IsEmpty || line.GetDisplayText().Length == 0) continue;
                    var fonts = resolved[t][i];
                    if (fonts.Runs.Count == 0) continue;
                    var layout = line.LayoutName is { Length: > 0 } name && resolver.FindIndex(name) is int li
                        ? layouts[Math.Clamp(li, 0, layouts.Count - 1)]
                        : single;
                    widths[i] = LineWidthValidator.Evaluate(i, Source(line, fonts, SpacingOf(layout)).GetLayout().Width, screenWidth, layout.HorizontalMarginPx);
                }
            }
        }
        PreviewModel = new SubtitlePreviewModel(screenWidth, screenHeight, previewLines);
        PreviewModelChanged?.Invoke(this, EventArgs.Empty);

        _lineWidths = widths;
        for (int i = 0; i < Lines.Count; i++)
        {
            var (text, tip) = layoutTexts.TryGetValue(i, out var lt) ? lt : ("", "");
            Lines[i].LayoutText = text;
            Lines[i].LayoutToolTip = tip;
            Lines[i].SetWidthResult(widths.GetValueOrDefault(i));
        }
    }

    /// <summary>行リストで文字を選んでいる行（無ければ null）。選んでいる範囲は <see cref="LineViewModel.CharSelection"/>。</summary>
    public LineViewModel? CharSelectionLine { get; private set; }

    /// <summary>行リストで文字を選ぶ（<see cref="LyricsLine.Chars"/> の添字の範囲、両端を含む）。ほかの行の選択は外す。</summary>
    public void SetCharSelection(LineViewModel line, int startUnit, int endUnit)
    {
        if (CharSelectionLine is not null && !ReferenceEquals(CharSelectionLine, line)) CharSelectionLine.CharSelection = null;
        CharSelectionLine = line;
        line.CharSelection = (Math.Min(startUnit, endUnit), Math.Max(startUnit, endUnit));
    }

    /// <summary>行リストの文字の選択を外す。</summary>
    public void ClearCharSelection()
    {
        if (CharSelectionLine is null) return;
        CharSelectionLine.CharSelection = null;
        CharSelectionLine = null;
    }

    /// <summary>選んでいる文字の数（スペーサーを除く）。選んでいなければ 0。</summary>
    public int CharSelectionCount()
    {
        if (CharSelectionLine?.CharSelection is not (int start, int end)) return 0;
        var chars = CharSelectionLine.Model.Chars;
        int count = 0;
        for (int i = Math.Max(0, start); i <= end && i < chars.Count; i++)
        {
            if (!chars[i].IsSpacer) count++;
        }
        return count;
    }

    /// <summary>選んでいる文字の手動指定が全部同じ名前ならその名前（無い・ばらばらなら null）。</summary>
    public string? CharSelectionFontName()
    {
        if (CharSelectionLine?.CharSelection is not (int start, int end)) return null;
        var chars = CharSelectionLine.Model.Chars;
        string? name = null;
        bool first = true;
        for (int i = Math.Max(0, start); i <= end && i < chars.Count; i++)
        {
            if (chars[i].IsSpacer) continue;
            if (first)
            {
                name = chars[i].FontSetName;
                first = false;
            }
            else if (chars[i].FontSetName != name)
            {
                return null;
            }
        }
        return name;
    }

    /// <summary>字幕の見た目で描く共通の材料（絵文字の一覧・画像のフォルダ・既定のフォント設定）。中身が同じなら前と同じものを返す。</summary>
    public SubtitleContext GetSubtitleContext()
    {
        var emoji = GetEffectiveEmojiList();
        string? folder = (Tabs.FirstOrDefault(t => t.IsMain)?.FilePath ?? CurrentFilePath) is string path ? Path.GetDirectoryName(path) : null;
        var defaultFont = new N3FontSet
        {
            Name = "標準",
            FontFamily = Settings.FontFamily,
            FontFace = Settings.FontBold ? "Bold" : "",
            SizePx = Settings.FontSizePx,
            EdgePx = Settings.EdgeSizePx,
        };
        var matcher = CreateEmojiMatcher();
        string key = string.Join("\n", emoji.Select(e => e.ToTagValue()))
            + "|" + string.Join(",", matcher.Strings)
            + "|" + folder
            + "|" + JsonSerializer.Serialize(defaultFont.Detail);
        if (_subtitleContext is { } current && current.Key == key) return current;
        _subtitleContext = new SubtitleContext(emoji, matcher, folder, defaultFont, key);
        return _subtitleContext;
    }

    /// <summary>フォント設定の中身ごとの番号（同じ中身なら同じ番号）。</summary>
    private int FontContentId(N3FontSet font)
    {
        string json = JsonSerializer.Serialize(font.Detail);
        if (!_fontContentIds.TryGetValue(json, out int id))
        {
            id = _fontContentIds.Count + 1;
            _fontContentIds[json] = id;
        }
        return id;
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
