using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.Services.Subtitles;

/// <summary>
/// 行を字幕の見た目で描くための材料（行・CharUnit ごとのフォント設定・共通の材料・文字の間隔）。
/// <see cref="Key"/> は描く内容が変わると変わり、同じキーの行の配置は使い回す。
/// </summary>
public sealed class LineRenderSource
{
    /// <param name="contentKey">文字の間隔のほかに描く内容を表すキー（共通の材料・行の内容・文字ごとのフォント設定）。</param>
    public LineRenderSource(LyricsLine line, IReadOnlyList<N3FontSet?> unitFonts, SubtitleContext context, SubtitleSpacing spacing, string contentKey)
    {
        Line = line;
        UnitFonts = unitFonts;
        Context = context;
        Spacing = spacing;
        ContentKey = contentKey;
        Key = $"{spacing}|{contentKey}";
    }

    /// <summary>文字の間隔だけを変えた材料（レイアウトの画面の見本で、選んだレイアウトの文字間隔で描くため）。</summary>
    public LineRenderSource WithSpacing(SubtitleSpacing spacing) =>
        spacing == Spacing ? this : new LineRenderSource(Line, UnitFonts, Context, spacing, ContentKey);

    public string ContentKey { get; }

    public LyricsLine Line { get; }

    public IReadOnlyList<N3FontSet?> UnitFonts { get; }

    public SubtitleContext Context { get; }

    public SubtitleSpacing Spacing { get; }

    public string Key { get; }

    /// <summary>行の配置（使い回し。描くたびに取り直し、持ち続けない）。</summary>
    internal SubtitleLineLayout GetLayout() => SubtitleLayoutCache.Get(Key, () => SubtitleLineLayout.Build(Line, UnitFonts, Context, Spacing));
}

/// <summary>行の配置の使い回し（新しく使った順に一定数まで）。デバイスを失ったら捨てる。UI スレッドから使う。</summary>
internal static class SubtitleLayoutCache
{
    private const int Capacity = 600;
    private static readonly Dictionary<string, LinkedListNode<(string Key, SubtitleLineLayout Layout)>> Map = new(StringComparer.Ordinal);
    private static readonly LinkedList<(string Key, SubtitleLineLayout Layout)> Order = new();

    static SubtitleLayoutCache()
    {
        SubtitleGlyphCache.Reset += (_, _) => Clear();
    }

    public static SubtitleLineLayout Get(string key, Func<SubtitleLineLayout> build)
    {
        if (Map.TryGetValue(key, out var node))
        {
            Order.Remove(node);
            Order.AddFirst(node);
            return node.Value.Layout;
        }
        var layout = build();
        Map[key] = Order.AddFirst((key, layout));
        while (Order.Count > Capacity)
        {
            var last = Order.Last!;
            Order.RemoveLast();
            Map.Remove(last.Value.Key);
            last.Value.Layout.Dispose();
        }
        return layout;
    }

    public static void Clear()
    {
        foreach (var (_, layout) in Order) layout.Dispose();
        Order.Clear();
        Map.Clear();
    }
}
