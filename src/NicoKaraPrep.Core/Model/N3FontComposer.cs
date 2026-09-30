namespace NicoKaraPrep.Core.Model;

/// <summary>
/// 組み合わせフォント（複数人で歌うパート用。例:「（梢）（吟子）」）の作り方。元のフォント設定の並び・塗り方・帯の幅を持つ。
/// 名前は元のフォント設定の名前を並びどおりにつなげたもの（歌詞の記号の並びと同じにすると、その部分に当たる）。
/// </summary>
public sealed class N3FontComposition
{
    /// <summary>元のフォント設定の Id（並びどおり。上の帯から順）。</summary>
    public List<string> SourceIds { get; set; } = new();

    /// <summary>塗りの種類（<see cref="N3Brush.TypeMilleFeuille"/> か <see cref="N3Brush.TypeGradient"/>）。</summary>
    public int BrushType { get; set; } = N3Brush.TypeMilleFeuille;

    /// <summary>端の帯を中央の帯より何 % 広くするか（0 で等分。文字の上下の端は面積が小さいため、既定で少し広くする）。</summary>
    public double EndWidenPercent { get; set; } = N3FontComposer.DefaultEndWidenPercent;

    /// <summary>元のフォント設定の色が変わったら作り直すか。</summary>
    public bool Linked { get; set; } = true;

    public N3FontComposition Clone() => new()
    {
        SourceIds = SourceIds.ToList(),
        BrushType = BrushType,
        EndWidenPercent = EndWidenPercent,
        Linked = Linked,
    };
}

/// <summary>
/// 組み合わせフォントを作る・作り直す。配色パターンの最初の役割（キャラ色の反転ならキャラ色）を多色（ミルフィーユかグラデーション）にし、
/// それ以外の箇所（ベース色・縁 2）と色以外の項目は最初の元フォント設定から写す。
/// </summary>
public static class N3FontComposer
{
    /// <summary>端の帯の広さの既定（%）。ユーザーの手作業の比率（3 人 0.34 / 0.65、4 人 0.26 / 0.74）とほぼ同じになる。</summary>
    public const double DefaultEndWidenPercent = 10;

    /// <summary>ミルフィーユの最後に置く、塗りに使われないマーカーの色（ニコカラメーカー3 の実データと同じ）。</summary>
    public const string MilleFeuilleTailColor = "808080";

    /// <summary>多色にする役割（配色パターンの最初の役割）。</summary>
    public const int MultiColorRole = 0;

    // ------------------------------------------------------------ 帯

    /// <summary>
    /// 帯の幅（上から順。合計 1）。端に近いほど広い: 帯 i の重みは 1 + 端の広さ × (中央からの距離 / 端までの距離)。
    /// 1 本なら [1]、2 本なら等分。
    /// </summary>
    public static double[] BandWidths(int count, double endWidenPercent)
    {
        if (count <= 0) return Array.Empty<double>();
        if (count == 1) return new[] { 1.0 };
        double k = Math.Max(0, endWidenPercent) / 100;
        double center = (count - 1) / 2.0;
        var w = Enumerable.Range(0, count).Select(i => 1 + k * Math.Abs(i - center) / center).ToArray();
        double sum = w.Sum();
        return w.Select(x => x / sum).ToArray();
    }

    /// <summary>
    /// 色の並びから、多色の塗りを作る。ミルフィーユは帯の上端にマーカーを置き、最後に 100% のマーカー（塗りには使われない）を足す。
    /// グラデーションは最初を 0%、最後を 100%、間を帯の中央に置く。位置は小数 4 桁に四捨五入する（上下の端の帯を同じ幅にするため）。
    /// </summary>
    public static N3Brush MultiColorBrush(IReadOnlyList<(string Color, int Alpha)> colors, int brushType, double endWidenPercent)
    {
        var widths = BandWidths(colors.Count, endWidenPercent);
        var stops = new List<N3GradientStop>();
        double top = 0;
        for (int i = 0; i < colors.Count; i++)
        {
            double position = brushType == N3Brush.TypeGradient
                ? (i == 0 ? 0 : i == colors.Count - 1 ? 1 : top + widths[i] / 2)
                : top;
            stops.Add(new N3GradientStop { Position = Math.Round(position, 4, MidpointRounding.AwayFromZero), Color = N3FontSet.NormalizeWeb16(colors[i].Color), AlphaPercent = colors[i].Alpha });
            top += widths[i];
        }
        if (brushType != N3Brush.TypeGradient) stops.Add(new N3GradientStop { Position = 1, Color = MilleFeuilleTailColor, AlphaPercent = 100 });
        return new N3Brush
        {
            Type = brushType == N3Brush.TypeGradient ? N3Brush.TypeGradient : N3Brush.TypeMilleFeuille,
            Color = colors.Count > 0 ? N3FontSet.NormalizeWeb16(colors[0].Color) : "",
            Stops = stops,
        };
    }

    /// <summary>
    /// 元のフォント設定の役割の色（役割の箇所でいちばん多く使われている塗り）。単色なら 1 色、多色ならマーカーの色を上から順に
    /// （ミルフィーユの最後のマーカーは除く）。画像や読めない色は白とみなす。
    /// </summary>
    public static List<(string Color, int Alpha)> RoleColors(N3FontDetail detail, N3ColorPattern pattern, int role)
    {
        int slot = N3ColorPatterns.RepresentativeSlot(detail, pattern, role);
        if (slot < 0) return new List<(string, int)> { ("FFFFFF", 100) };
        var b = detail.Brushes[slot];
        if (b.Type is N3Brush.TypeGradient or N3Brush.TypeMilleFeuille && b.Stops.Count > 0)
        {
            var stops = b.Stops.ToList();
            if (b.Type == N3Brush.TypeMilleFeuille && stops.Count > 1) stops.RemoveAt(stops.Count - 1);
            return stops.Select(s => (N3FontSet.IsValidWeb16(s.Color) ? s.Color : "FFFFFF", s.AlphaPercent)).ToList();
        }
        return new List<(string, int)> { (N3FontSet.IsValidWeb16(b.Color) ? b.Color : "FFFFFF", b.AlphaPercent) };
    }

    // ------------------------------------------------------------ 作る

    /// <summary>
    /// 組み合わせの配色を target に書く: パターンの多色にする役割の箇所には元のフォント設定の役割の色を並べた多色の塗りを、
    /// それ以外の箇所には最初の元フォント設定の塗りの写しを入れる。
    /// </summary>
    public static void ComposeColors(N3FontDetail target, IReadOnlyList<N3FontDetail> sources, N3ColorPattern pattern, int brushType, double endWidenPercent)
    {
        if (sources.Count == 0) return;
        var colors = sources.SelectMany(s => RoleColors(s, pattern, MultiColorRole)).ToList();
        var multi = MultiColorBrush(colors, brushType, endWidenPercent);
        var roleSlots = pattern.SlotsOf(MultiColorRole);
        for (int i = 0; i < N3FontDetail.BrushCount; i++)
        {
            target.Brushes[i] = roleSlots.Contains(i) ? multi.Clone() : sources[0].Brushes[i].Clone();
        }
    }

    /// <summary>
    /// 組み合わせフォントを作る（名前は元の名前を並びどおりにつなげたもの。色以外の項目は最初の元フォント設定の写し）。
    /// Id は新しく付け、配色パターンは pattern にする。一覧への追加（名前の重なりの確認）は呼び出し側で行う。
    /// </summary>
    public static N3FontSet Create(IReadOnlyList<N3FontSet> sources, N3ColorPattern pattern, int brushType, double endWidenPercent, bool linked = true)
    {
        if (sources.Count == 0) throw new ArgumentException("元のフォント設定がありません", nameof(sources));
        var first = sources[0];
        var font = first.Clone();
        font.Id = Guid.NewGuid().ToString();
        font.Name = string.Concat(sources.Select(s => s.Name));
        font.NkmGuid = null;
        font.NkmSynchronize = false;
        font.ImportedFrom = null;
        font.ImportedUtc = null;
        font.HasFullDetail = true;
        font.ColorPatternId = pattern.Id;
        font.Composition = new N3FontComposition
        {
            SourceIds = sources.Select(s => s.Id).ToList(),
            BrushType = brushType,
            EndWidenPercent = endWidenPercent,
            Linked = linked,
        };
        ComposeColors(font.Detail, sources.Select(s => s.Detail).ToList(), pattern, brushType, endWidenPercent);
        return font;
    }

    /// <summary>組み合わせに使うパターン（組み合わせフォントで選んだもの → 最初の元フォント設定に当てはまるもの → キャラ色の反転）。</summary>
    public static N3ColorPattern PatternFor(N3FontSet? composed, IReadOnlyList<N3FontSet> sources, IReadOnlyList<N3ColorPattern> patterns)
    {
        if (composed is not null && N3ColorPatterns.Find(patterns, composed.ColorPatternId) is { Roles.Count: > 0 } chosen) return chosen;
        if (sources.Count > 0 && N3ColorPatterns.Effective(sources[0], patterns) is { Pattern.Roles.Count: > 0 } m) return m.Pattern;
        return N3ColorPatterns.BuiltIns[0];
    }

    /// <summary>
    /// 組み合わせフォントの配色を、元のフォント設定の今の色で作り直す。元のフォント設定が 1 つでも見つからなければ何もしない（false）。
    /// </summary>
    public static bool Recompose(N3FontSet composed, IReadOnlyList<N3FontSet> all, IReadOnlyList<N3ColorPattern> patterns)
    {
        if (composed.Composition is not { } c || c.SourceIds.Count == 0) return false;
        var sources = new List<N3FontSet>();
        foreach (string id in c.SourceIds)
        {
            if (N3FontLibrary.Find(all, id) is not { } s || ReferenceEquals(s, composed)) return false;
            sources.Add(s);
        }
        var pattern = PatternFor(composed, sources, patterns);
        ComposeColors(composed.Detail, sources.Select(s => s.Detail).ToList(), pattern, c.BrushType, c.EndWidenPercent);
        return true;
    }

    /// <summary>作り直したときの配色と今の配色が同じか（組み合わせフォントの色を手で変えたかどうかの判定に使う）。</summary>
    public static bool MatchesSources(N3FontSet composed, IReadOnlyList<N3FontSet> all, IReadOnlyList<N3ColorPattern> patterns)
    {
        var trial = composed.Clone();
        if (!Recompose(trial, all, patterns)) return true;
        for (int i = 0; i < N3FontDetail.BrushCount; i++)
        {
            if (!N3ColorPatterns.SameLook(trial.Detail.Brushes[i], composed.Detail.Brushes[i])) return false;
        }
        return true;
    }

    // ------------------------------------------------------------ 歌詞から見つける

    /// <summary>
    /// 行の中で、続けて並んだ絵文字（2 連タグ用スペーサーだけを挟んでもよい）のうち、どれにも同じ名前のフォント設定があり、
    /// つなげた名前のフォント設定がまだ無い組（2 つ以上。同じ絵文字が 2 回入っている組は除く）を返す。各組は絵文字の名前の並び。
    /// </summary>
    public static List<IReadOnlyList<string>> FindMissingRuns(LyricsLine line, IEnumerable<string> emojiNames, IReadOnlySet<string> fontNames)
    {
        var result = new List<IReadOnlyList<string>>();
        var names = emojiNames.Where(n => !string.IsNullOrEmpty(n) && fontNames.Contains(n)).Distinct().ToList();
        if (names.Count < 2) return result;
        var compact = line.Chars.Where(c => !c.IsSpacer).ToList();
        var occurrences = new EmojiMatcher(names).FindOccurrences(compact).OrderBy(o => o.Start).ToList();

        var run = new List<EmojiMatcher.Occurrence>();
        void Flush()
        {
            if (run.Count >= 2)
            {
                var values = run.Select(o => o.Value).ToList();
                string name = string.Concat(values);
                if (values.Distinct().Count() == values.Count && !fontNames.Contains(name) && result.All(r => string.Concat(r) != name))
                {
                    result.Add(values);
                }
            }
            run.Clear();
        }
        foreach (var o in occurrences)
        {
            if (run.Count > 0 && run[^1].EndExclusive != o.Start) Flush();
            run.Add(o);
        }
        Flush();
        return result;
    }

    // ------------------------------------------------------------ 置き場所

    /// <summary>
    /// 名前が、ほかのフォント設定の名前を 2 つ以上つなげたものか（手作りの組み合わせフォントを見分ける。例:「（梢）（吟子）」）。
    /// </summary>
    public static bool IsCombinationName(string name, IReadOnlySet<string> singleNames)
    {
        if (name.Length == 0) return false;
        // parts[i] = 先頭から i 文字までを、いくつの名前で分けられるか（分けられなければ 0）
        var parts = new int[name.Length + 1];
        parts[0] = 0;
        for (int i = 1; i <= name.Length; i++) parts[i] = -1;
        for (int i = 0; i < name.Length; i++)
        {
            if (parts[i] < 0) continue;
            foreach (string n in singleNames)
            {
                if (n.Length == 0 || n == name || i + n.Length > name.Length || string.CompareOrdinal(name, i, n, 0, n.Length) != 0) continue;
                parts[i + n.Length] = Math.Max(parts[i + n.Length], parts[i] + 1);
            }
        }
        return parts[name.Length] >= 2;
    }

    /// <summary>
    /// 組み合わせフォントを階層に入れる: 同じ元フォントを含むほかの組み合わせフォント（手作りのものも名前で見分ける）が
    /// いちばん多く入っているフォルダの末尾へ。無ければ最初の元フォントと同じ親の末尾へ（階層に無ければ最上位の末尾）。
    /// </summary>
    public static void PlaceInTree(List<N3FontTreeNode> roots, IReadOnlyList<N3FontSet> fonts, N3FontSet composed, IReadOnlyList<N3FontSet> sources)
    {
        var node = N3FontTreeNode.ForFont(composed.Id);
        var sourceNames = new HashSet<string>(sources.Select(s => s.Name), StringComparer.Ordinal);
        var sourceIds = new HashSet<string>(sources.Select(s => s.Id), StringComparer.OrdinalIgnoreCase);
        var singleNames = new HashSet<string>(fonts.Where(f => f.Composition is null).Select(f => f.Name), StringComparer.Ordinal);

        var parents = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in fonts)
        {
            if (ReferenceEquals(f, composed)) continue;
            bool related = f.Composition is { } c
                ? c.SourceIds.Any(sourceIds.Contains)
                : sourceNames.Any(n => f.Name.Contains(n, StringComparison.Ordinal)) && IsCombinationName(f.Name, singleNames);
            if (!related || N3FontTree.Find(roots, f.Id) is not { } loc) continue;
            string key = loc.Parent?.Key ?? "";
            parents[key] = parents.GetValueOrDefault(key) + 1;
        }

        string? parentKey;
        if (parents.Count > 0)
        {
            string best = parents.OrderByDescending(p => p.Value).First().Key;
            parentKey = best.Length == 0 ? null : best;
        }
        else
        {
            parentKey = N3FontTree.Find(roots, sources[0].Id)?.Parent?.Key;
        }
        if (!N3FontTree.Insert(roots, parentKey, node)) roots.Add(node);
    }
}
