using NicoKaraPrep.Core.Formats;

namespace NicoKaraPrep.Core.Model;

/// <summary>
/// NicoKaraPrep で編集するニコカラメーカー3 のレイアウト設定 1 件（アプリ共通。px は書き出すプロジェクトの画面の高さでの値）。
/// 書き出しでは、ベースの n3proj の同じ名前のレイアウト設定に上書きし、無ければ足す。
/// 上下配置 0 = 上寄せ / 1 = 中央 / 2 = 下寄せ。行ごとの左右配置 0 = 左寄せ / 1 = 中央 / 2 = 右寄せ（上の行から）。
/// スマート水平配置 0 = 調整しない / 1 = 中心位置揃え / 2 = 左右余白揃え。ルビ配置 0 = 自動 / 1 = 中央 / 2 = 均等割り付け。
/// </summary>
public sealed class N3Layout
{
    private string _name = "";
    private List<int> _alignments = new() { 0, 2 };

    /// <summary>識別子（一覧の中で見分けるため。書き出しには使わない）。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name
    {
        get => _name;
        set => _name = value ?? "";
    }

    public int VerticalAlignment { get; set; } = 2;

    public double LineSpacePx { get; set; } = 60;

    public double VerticalMarginPx { get; set; } = 50;

    public double HorizontalMarginPx { get; set; } = 50;

    /// <summary>行ごとの左右配置（上の行から。1 件以上）。</summary>
    public List<int> HorizontalAlignments
    {
        get => _alignments;
        set => _alignments = value is { Count: > 0 } ? value : new List<int> { 1 };
    }

    public int SmartHorizon { get; set; } = 2;

    public double LyricsIntervalPx { get; set; }

    /// <summary>一部の文字の食い込みを許容する。</summary>
    public bool AllowBiting { get; set; }

    public double RubyIntervalPx { get; set; }

    public int RubyAlignment { get; set; }

    public double LyricsAndRubyIntervalPx { get; set; }

    public N3Layout Clone()
    {
        var c = (N3Layout)MemberwiseClone();
        c._alignments = new List<int>(_alignments);
        return c;
    }

    /// <summary>描画・書き出しのレイアウトの選び方に使う形（<paramref name="index"/> はレイアウト設定の並びの番号）。</summary>
    public N3LayoutSettings ToSettings(int index) => new(
        Name, index, VerticalAlignment, LineSpacePx, VerticalMarginPx, HorizontalMarginPx, HorizontalAlignments.ToList(),
        SmartHorizon, LyricsIntervalPx, RubyIntervalPx, LyricsAndRubyIntervalPx, RubyAlignment, AllowBiting);

    /// <summary>読み込んだレイアウト設定から作る（ベースのレイアウトを編集し始めるとき）。</summary>
    public static N3Layout FromSettings(N3LayoutSettings s) => new()
    {
        Name = s.Name,
        VerticalAlignment = s.VerticalAlignment,
        LineSpacePx = s.LineSpacePx,
        VerticalMarginPx = s.VerticalMarginPx,
        HorizontalMarginPx = s.HorizontalMarginPx,
        HorizontalAlignments = s.HorizontalAlignments.Count > 0 ? s.HorizontalAlignments.ToList() : new List<int> { 1 },
        SmartHorizon = s.SmartHorizon,
        LyricsIntervalPx = s.LyricsIntervalPx,
        AllowBiting = s.AllowBiting,
        RubyIntervalPx = s.RubyIntervalPx,
        RubyAlignment = s.RubyAlignment,
        LyricsAndRubyIntervalPx = s.LyricsAndRubyIntervalPx,
    };
}

/// <summary>レイアウト設定の一覧の計算（ベースと NicoKaraPrep で編集したものを合わせる）。</summary>
public static class N3LayoutLibrary
{
    /// <summary>
    /// 書き出すプロジェクトのレイアウト設定の並び（N3ProjWriter と同じ合わせ方）。ベースの順に、同じ名前の編集したものがあればその値にし、
    /// ベースに無い名前の編集したものを後ろへ足す（<paramref name="merge"/> が false ならベースのまま）。番号（Index）は並びの位置。
    /// </summary>
    public static List<N3LayoutSettings> Effective(IReadOnlyList<N3LayoutSettings> baseLayouts, IReadOnlyList<N3Layout> own, bool merge)
    {
        var result = new List<N3LayoutSettings>();
        var byName = new Dictionary<string, N3Layout>(StringComparer.Ordinal);
        if (merge)
        {
            foreach (var l in own)
            {
                if (!string.IsNullOrWhiteSpace(l.Name)) byName.TryAdd(l.Name, l);
            }
        }
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var b in baseLayouts)
        {
            if (byName.TryGetValue(b.Name, out var mine) && used.Add(b.Name))
            {
                result.Add(mine.ToSettings(result.Count));
            }
            else
            {
                result.Add(b with { Index = result.Count });
            }
        }
        foreach (var (name, mine) in byName)
        {
            if (result.Any(r => r.Name == name)) continue;
            result.Add(mine.ToSettings(result.Count));
        }
        return result;
    }
}
