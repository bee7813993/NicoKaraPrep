using Microsoft.Graphics.Canvas;
using Windows.UI;

namespace NicoKaraPrep.App.Services.Subtitles;

/// <summary>
/// 字幕を描くときの画像（絵文字の画像・画像ブラシ）の使い回し。共有のデバイスで読み、読み終わったら <see cref="Loaded"/> で知らせる
/// （描いている側は描き直す）。読めない画像は null のまま覚える。UI スレッドから使う。
/// </summary>
internal static class SubtitleBitmapCache
{
    private static readonly Dictionary<string, CanvasBitmap?> Bitmaps = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Loading = new(StringComparer.OrdinalIgnoreCase);
    private static CanvasRenderTarget? _hatch;
    private static int _generation;

    static SubtitleBitmapCache()
    {
        SubtitleGlyphCache.Reset += (_, _) => Clear();
    }

    /// <summary>画像を 1 つ読み終えた（読めなかったときも）。</summary>
    public static event EventHandler? Loaded;

    /// <summary>覚えている画像をすべて捨てる（デバイスを失ったとき・画像を差し替えたとき）。</summary>
    public static void Clear()
    {
        _generation++;
        foreach (var b in Bitmaps.Values) b?.Dispose();
        Bitmaps.Clear();
        Loading.Clear();
        _hatch?.Dispose();
        _hatch = null;
    }

    /// <summary>画像を返す。まだ読んでいなければ読み始めて null を返す（<paramref name="loading"/> が true）。読めない画像も null。</summary>
    public static CanvasBitmap? Get(string path, out bool loading)
    {
        loading = false;
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (Bitmaps.TryGetValue(path, out var bitmap)) return bitmap;
        loading = true;
        if (Loading.Add(path)) _ = LoadAsync(path);
        return null;
    }

    private static async Task LoadAsync(string path)
    {
        int generation = _generation;
        CanvasBitmap? bitmap = null;
        try
        {
            if (Path.IsPathFullyQualified(path) && File.Exists(path))
            {
                bitmap = await CanvasBitmap.LoadAsync(SubtitleGlyphCache.Device, path);
            }
        }
        catch (Exception)
        {
            bitmap = null; // 壊れた画像・対応していない形式
        }
        if (generation != _generation)
        {
            bitmap?.Dispose();
            return;
        }
        Loading.Remove(path);
        Bitmaps[path] = bitmap;
        Loaded?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>画像が無い・読めないときに使う灰色の斜線（12px 四方を敷き詰める）。</summary>
    public static CanvasRenderTarget Hatch
    {
        get
        {
            if (_hatch is not null) return _hatch;
            const float size = 12;
            var target = new CanvasRenderTarget(SubtitleGlyphCache.Device, size, size, 96);
            using (var ds = target.CreateDrawingSession())
            {
                ds.Clear(Color.FromArgb(255, 0x80, 0x80, 0x80));
                var line = Color.FromArgb(255, 0x58, 0x58, 0x58);
                ds.DrawLine(-1, size + 1, size + 1, -1, line, 2.5f);
                ds.DrawLine(-1, 1, 1, -1, line, 2.5f);
                ds.DrawLine(size - 1, size + 1, size + 1, size - 1, line, 2.5f);
            }
            _hatch = target;
            return target;
        }
    }
}
