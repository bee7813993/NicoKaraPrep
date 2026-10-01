using System.Text.Json.Serialization;

namespace NicoKaraPrep.Core.Model;

/// <summary>
/// 配色パターン。配色の 8 箇所（<see cref="N3FontDetail.Brushes"/> の添字）それぞれを、役割（「メイン色」「ベース色」など）か
/// 個別（パターンに縛らない）に割り当てる。同じ役割の箇所は同じ色にそろえる。
/// 例: メイン色 = ワイプ後の縁・ワイプ前の文字・ワイプ前の飾り、ベース色 = ワイプ後の文字・ワイプ後の飾り・ワイプ前の縁。
/// </summary>
public sealed class N3ColorPattern
{
    /// <summary>役割の最大数。</summary>
    public const int MaxRoles = 4;

    /// <summary>「個別」（どの役割にも入れない箇所）の役割の番号。</summary>
    public const int Individual = -1;

    /// <summary>識別子（標準のパターンは "builtin:" で始まる）。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string Name { get; set; } = "";

    /// <summary>役割の名前（最大 <see cref="MaxRoles"/> 個。添字が役割の番号）。</summary>
    public List<string> Roles { get; set; } = new();

    /// <summary>箇所ごとの役割の番号（長さ 8。<see cref="Individual"/> は個別）。</summary>
    public int[] Slots { get; set; } = Enumerable.Repeat(Individual, N3FontDetail.BrushCount).ToArray();

    /// <summary>標準のパターン（編集・削除はできない。複製して使う）か。</summary>
    [JsonIgnore]
    public bool IsBuiltIn => Id.StartsWith(N3ColorPatterns.BuiltInPrefix, StringComparison.Ordinal);

    /// <summary>箇所の役割の番号（範囲外・壊れた値は個別）。</summary>
    public int RoleOf(int slot) =>
        Slots is not null && slot >= 0 && slot < Slots.Length && Slots[slot] >= 0 && Slots[slot] < Roles.Count ? Slots[slot] : Individual;

    /// <summary>役割に入っている箇所（添字の小さい順）。</summary>
    public List<int> SlotsOf(int role) =>
        Enumerable.Range(0, N3FontDetail.BrushCount).Where(i => RoleOf(i) == role).ToList();

    /// <summary>役割の名前（範囲外は空）。</summary>
    public string RoleName(int role) => role >= 0 && role < Roles.Count ? Roles[role] : "";

    /// <summary>深いコピー（識別子も同じ）。</summary>
    public N3ColorPattern Clone() => new()
    {
        Id = Id,
        Name = Name,
        Roles = Roles.ToList(),
        Slots = (Slots ?? Array.Empty<int>()).ToArray(),
    };
}

/// <summary>フォント設定の配色とパターンを比べた結果。</summary>
/// <param name="Pattern">パターン。</param>
/// <param name="Deviations">同じ役割のほかの箇所と色が違う箇所（役割の中で多いほうの色と違う箇所）。</param>
/// <param name="Ambiguous">
/// 色がそろっていない役割のどれかで、過半数の箇所がそろっていない（2 箇所の役割が食い違っているなど）。
/// どれが正しい色か分からないので、配色から自動で当てはめるときはこのパターンを選ばない。
/// </param>
public sealed record N3PatternMatch(N3ColorPattern Pattern, IReadOnlyList<int> Deviations, bool Ambiguous = false)
{
    /// <summary>パターンどおりか（違う箇所が無い）。</summary>
    public bool IsExact => Deviations.Count == 0;
}

/// <summary>配色パターンの一覧と、フォント設定の配色への当てはめ。</summary>
public static class N3ColorPatterns
{
    /// <summary>標準のパターンの識別子の頭。</summary>
    public const string BuiltInPrefix = "builtin:";

    /// <summary>フォント設定で「パターンを使わない（8 箇所を個別に指定）」を選んだときの識別子。</summary>
    public const string NoneId = "none";

    /// <summary>メイン色の反転（2 色）の識別子（新しいフォント設定の既定）。もとはキャラ色と呼んでいたので識別子は chara のまま（保存済みの設定のため変えない）。</summary>
    public const string CharaInverseId = BuiltInPrefix + "chara-inverse";

    /// <summary>ワイプ前後が同じ（3 色）の識別子。</summary>
    public const string NoWipeId = BuiltInPrefix + "no-wipe";

    /// <summary>
    /// 自動で当てはめるときに許す、違う箇所の数（これ以下ならそのパターンのつもりで入力を間違えたとみなし、検証で知らせる）。
    /// </summary>
    public const int MaxDeviations = 2;

    /// <summary>
    /// 役割の色を決めるときに、同じ数だけ使われている色があれば優先する箇所の順
    /// （ワイプ前の文字 → ワイプ後の文字 → ワイプ後の縁 → ワイプ前の縁 → ワイプ前の飾り → ワイプ後の飾り → 縁 2）。
    /// </summary>
    private static readonly int[] RepresentativeOrder = { 4, 0, 1, 5, 7, 3, 6, 2 };

    /// <summary>標準のパターン（実際のフォント設定から読み取ったもの）。</summary>
    public static IReadOnlyList<N3ColorPattern> BuiltIns { get; } = new[]
    {
        // ワイプ前の文字・飾りとワイプ後の縁 = メイン色（キャラの色など）、ワイプ前の縁とワイプ後の文字・飾り = ベース色（白など）。縁 2 は個別
        new N3ColorPattern
        {
            Id = CharaInverseId,
            Name = "メイン色の反転（2 色）",
            Roles = new List<string> { "メイン色", "ベース色" },
            Slots = new[] { 1, 0, N3ColorPattern.Individual, 1, 0, 1, N3ColorPattern.Individual, 0 },
        },
        // ワイプの前後で色が変わらない（情報などの字幕）。文字・縁・飾りをそれぞれ前後で同じにする。縁 2 は個別
        new N3ColorPattern
        {
            Id = NoWipeId,
            Name = "ワイプ前後が同じ（3 色）",
            Roles = new List<string> { "文字", "縁", "飾り" },
            Slots = new[] { 0, 1, N3ColorPattern.Individual, 2, 0, 1, N3ColorPattern.Individual, 2 },
        },
    };

    /// <summary>標準のパターンと、ユーザーが作ったパターン（識別子が重なるもの・壊れたものは除く）。</summary>
    public static List<N3ColorPattern> All(IEnumerable<N3ColorPattern>? user)
    {
        var result = BuiltIns.ToList();
        var ids = new HashSet<string>(result.Select(p => p.Id), StringComparer.Ordinal);
        foreach (var p in user ?? Enumerable.Empty<N3ColorPattern>())
        {
            if (p is null || string.IsNullOrEmpty(p.Id) || p.Id == NoneId || p.IsBuiltIn || !ids.Add(p.Id)) continue;
            p.Roles ??= new();
            p.Slots = Normalize(p.Slots);
            result.Add(p);
        }
        return result;
    }

    private static int[] Normalize(int[]? slots)
    {
        var result = Enumerable.Repeat(N3ColorPattern.Individual, N3FontDetail.BrushCount).ToArray();
        if (slots is null) return result;
        for (int i = 0; i < result.Length && i < slots.Length; i++) result[i] = slots[i];
        return result;
    }

    /// <summary>識別子でパターンを探す（無ければ null）。</summary>
    public static N3ColorPattern? Find(IEnumerable<N3ColorPattern> patterns, string? id) =>
        id is null ? null : patterns.FirstOrDefault(p => p.Id == id);

    // ------------------------------------------------------------ 比べる

    /// <summary>
    /// 2 つの箇所が同じ見た目か（塗りの種類と、その種類で使う値だけを比べる。単色は色と不透明度、グラデーション・ミルフィーユは
    /// マーカーの位置・色・不透明度、画像はパスと拡大率）。
    /// </summary>
    public static bool SameLook(N3Brush a, N3Brush b)
    {
        if (a.Type != b.Type) return false;
        switch (a.Type)
        {
            case N3Brush.TypeSolid:
                return string.Equals(N3FontSet.NormalizeWeb16(a.Color), N3FontSet.NormalizeWeb16(b.Color), StringComparison.Ordinal)
                    && (N3FontSet.IsValidWeb16(a.Color) == N3FontSet.IsValidWeb16(b.Color))
                    && a.AlphaPercent == b.AlphaPercent;
            case N3Brush.TypeGradient:
            case N3Brush.TypeMilleFeuille:
                if (a.Stops.Count != b.Stops.Count) return false;
                for (int i = 0; i < a.Stops.Count; i++)
                {
                    var x = a.Stops[i];
                    var y = b.Stops[i];
                    if (Math.Abs(x.Position - y.Position) > 1e-4
                        || !string.Equals(N3FontSet.NormalizeWeb16(x.Color), N3FontSet.NormalizeWeb16(y.Color), StringComparison.Ordinal)
                        || x.AlphaPercent != y.AlphaPercent)
                    {
                        return false;
                    }
                }
                return true;
            case N3Brush.TypeBitmap:
                return string.Equals(a.BitmapPath, b.BitmapPath, StringComparison.OrdinalIgnoreCase) && a.BitmapScale == b.BitmapScale;
            default:
                return true;
        }
    }

    /// <summary>
    /// 役割の色（その役割の箇所でいちばん多く使われている色）の箇所。同じ数なら <see cref="RepresentativeOrder"/> の順で先の箇所。
    /// 役割に箇所が無ければ -1。
    /// </summary>
    public static int RepresentativeSlot(N3FontDetail detail, N3ColorPattern pattern, int role)
    {
        var slots = pattern.SlotsOf(role);
        if (slots.Count == 0) return -1;
        int best = -1;
        int bestCount = -1;
        int bestRank = int.MaxValue;
        foreach (int s in slots)
        {
            int count = slots.Count(t => SameLook(detail.Brushes[s], detail.Brushes[t]));
            int rank = Array.IndexOf(RepresentativeOrder, s);
            if (count > bestCount || (count == bestCount && rank < bestRank))
            {
                best = s;
                bestCount = count;
                bestRank = rank;
            }
        }
        return best;
    }

    /// <summary>
    /// 配色をパターンと比べる（役割ごとに、役割の色と違う箇所を集める）。色がそろっていない役割で、役割の色が過半数の箇所に
    /// 使われていなければ（どれが正しいか分からなければ）<see cref="N3PatternMatch.Ambiguous"/> にする。
    /// </summary>
    public static N3PatternMatch Evaluate(N3FontDetail detail, N3ColorPattern pattern)
    {
        var deviations = new List<int>();
        bool ambiguous = false;
        for (int role = 0; role < pattern.Roles.Count; role++)
        {
            int rep = RepresentativeSlot(detail, pattern, role);
            if (rep < 0) continue;
            var slots = pattern.SlotsOf(role);
            var off = slots.Where(s => !SameLook(detail.Brushes[rep], detail.Brushes[s])).ToList();
            if (off.Count == 0) continue;
            deviations.AddRange(off);
            if ((slots.Count - off.Count) * 2 <= slots.Count) ambiguous = true;
        }
        deviations.Sort();
        return new N3PatternMatch(pattern, deviations, ambiguous);
    }

    /// <summary>
    /// 配色にいちばん合うパターンを探す（違う箇所がいちばん少ないもの。同じなら一覧の先のもの）。
    /// 違う箇所が <paramref name="maxDeviations"/> より多いパターンと、どれが正しい色か分からないパターン（<see cref="N3PatternMatch.Ambiguous"/>）は
    /// 選ばない。見つからなければ null。
    /// </summary>
    public static N3PatternMatch? Detect(N3FontDetail detail, IEnumerable<N3ColorPattern> patterns, int maxDeviations = MaxDeviations)
    {
        N3PatternMatch? best = null;
        foreach (var p in patterns)
        {
            if (p.Roles.Count == 0) continue;
            var m = Evaluate(detail, p);
            if (m.Deviations.Count > maxDeviations || m.Ambiguous) continue;
            if (best is null || m.Deviations.Count < best.Deviations.Count) best = m;
        }
        return best;
    }

    /// <summary>
    /// フォント設定に当てはめるパターンと、合っていない箇所。<see cref="N3FontSet.ColorPatternId"/> が
    /// <see cref="NoneId"/> なら null（個別）、ある識別子ならそのパターン（違う箇所がいくつあっても）、
    /// 未設定・見つからなければ配色から探す（<see cref="Detect"/>）。
    /// </summary>
    public static N3PatternMatch? Effective(N3FontSet font, IReadOnlyList<N3ColorPattern> patterns)
    {
        if (font.ColorPatternId == NoneId) return null;
        if (Find(patterns, font.ColorPatternId) is { } chosen) return Evaluate(font.Detail, chosen);
        return Detect(font.Detail, patterns);
    }

    // ------------------------------------------------------------ 変える

    /// <summary>
    /// 配色をパターンに合わせる: 役割ごとに、役割の色（いちばん多く使われている色）を、その役割のすべての箇所に写す。
    /// 変わった箇所の数を返す。
    /// </summary>
    public static int Align(N3FontDetail detail, N3ColorPattern pattern)
    {
        int changed = 0;
        for (int role = 0; role < pattern.Roles.Count; role++)
        {
            int rep = RepresentativeSlot(detail, pattern, role);
            if (rep < 0) continue;
            foreach (int s in pattern.SlotsOf(role))
            {
                if (s == rep || SameLook(detail.Brushes[rep], detail.Brushes[s])) continue;
                detail.Brushes[s] = detail.Brushes[rep].Clone();
                changed++;
            }
        }
        return changed;
    }

    /// <summary>
    /// 別のフォント設定の配色から、役割の色だけを写す（写し元でその役割の箇所にいちばん多く使われている色を、
    /// 写し先のその役割のすべての箇所へ）。
    /// </summary>
    public static void CopyRole(N3FontDetail from, N3FontDetail to, N3ColorPattern pattern, int role)
    {
        int rep = RepresentativeSlot(from, pattern, role);
        if (rep < 0) return;
        foreach (int s in pattern.SlotsOf(role)) to.Brushes[s] = from.Brushes[rep].Clone();
    }

    /// <summary>箇所の名前を並べた文字列（例:「ワイプ後の縁・ワイプ前の文字」）。</summary>
    public static string Describe(IEnumerable<int> slots) =>
        string.Join("・", slots.Where(s => s >= 0 && s < N3FontDetail.BrushCount).Select(s => N3FontDetail.BrushLabels[s]));
}
