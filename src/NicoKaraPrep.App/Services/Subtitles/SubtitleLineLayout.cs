using System.Numerics;
using Microsoft.Graphics.Canvas.Geometry;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;
using Windows.Foundation;

namespace NicoKaraPrep.App.Services.Subtitles;

/// <summary>
/// 文字の間隔・ルビの並べ方（ニコカラメーカー3 のレイアウト設定の文字間隔の項目。基準の画面の高さの px）。
/// AllowBiting は「一部の文字の食い込みを許容する」（false: 字面の左右に縁の分のすき間をとって、隣の文字と縁が重ならないように並べる）。
/// </summary>
public sealed record SubtitleSpacing(float LyricsInterval, float RubyInterval, float LyricsAndRubyInterval, int RubyAlignment, bool AllowBiting = false)
{
    /// <summary>ニコカラメーカー3 の新しいレイアウトの既定（間隔 0・ルビは自動配置・食い込みは許さない）。</summary>
    public static readonly SubtitleSpacing Default = new(0, 0, 0, 0);
}

/// <summary>
/// 行を字幕の見た目で並べるための共通の材料（絵文字の一覧・画像の場所・既定のフォント設定）。
/// <see cref="Key"/> は中身が変わると変わる（行の配置の使い回しのキーに含める）。
/// </summary>
public sealed class SubtitleContext
{
    private readonly Dictionary<string, (int W, int H)?> _imageSizes = new(StringComparer.OrdinalIgnoreCase);

    public SubtitleContext(IEnumerable<EmojiEntry> emoji, EmojiMatcher matcher, string? baseFolder, N3FontSet defaultFont, string key)
    {
        EmojiByString = emoji
            .Where(e => e.ReplaceChar.Length > 0)
            .GroupBy(e => e.ReplaceChar)
            .ToDictionary(g => g.Key, g => g.First());
        Matcher = matcher;
        BaseFolder = baseFolder;
        DefaultFont = defaultFont;
        Key = key;
    }

    /// <summary>置き換え文字列ごとの @Emoji（同じ文字列が複数あれば先のもの）。</summary>
    public IReadOnlyDictionary<string, EmojiEntry> EmojiByString { get; }

    /// <summary>行の中の絵文字（置き換え文字列）を探すもの。</summary>
    public EmojiMatcher Matcher { get; }

    /// <summary>相対パスの画像の基準のフォルダ（歌詞ファイルのフォルダ）。</summary>
    public string? BaseFolder { get; }

    /// <summary>フォント設定が決まらない文字に使うフォント設定（書き出しの「標準」と同じもの）。</summary>
    public N3FontSet DefaultFont { get; }

    public string Key { get; }

    /// <summary>画像のパス（相対パスは歌詞ファイルのフォルダから）。</summary>
    public string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathFullyQualified(path) || BaseFolder is null) return path;
        try
        {
            return Path.GetFullPath(Path.Combine(BaseFolder, path));
        }
        catch (Exception)
        {
            return path;
        }
    }

    /// <summary>画像の大きさ px（読めなければ null。覚えておく）。</summary>
    public (int W, int H)? ImageSize(string path)
    {
        if (_imageSizes.TryGetValue(path, out var size)) return size;
        size = ImageSizeReader.TryGetSize(path, out int w, out int h) && w > 0 && h > 0 ? (w, h) : null;
        _imageSizes[path] = size;
        return size;
    }
}

/// <summary>
/// 1 行を字幕の見た目で描くための配置。座標は基準の画面の高さの px で、行の左端・本文のベースラインを原点にする（上が負）。
/// 文字の輪郭は、同じフォント設定・同じ文字種別フォント（＝同じ縁の幅）ごとにまとめて持つ（描くときは「飾り → 縁 2 → 縁 → 本体」を行全体で順に描く）。
/// </summary>
internal sealed class SubtitleLineLayout : IDisposable
{
    /// <summary>表示の 1 単位（文字 1 つか絵文字 1 つ）。UnitStart〜UnitEnd は <see cref="LyricsLine.Chars"/> の添字（両端を含む）。</summary>
    public sealed record Token(int UnitStart, int UnitEnd, float X, float Width, bool IsEmoji, int Font);

    /// <summary>絵文字の画像（Rect は行の座標）。</summary>
    public sealed record Image(string Before, string? After, Rect Rect);

    /// <summary>同じフォント設定・同じ線の幅の輪郭のまとまり。</summary>
    public sealed class Group : IDisposable
    {
        private CanvasGeometry? _silhouette;

        public Group(int font, bool ruby, float edge, float edge2Outer, CanvasGeometry geometry)
        {
            Font = font;
            Ruby = ruby;
            Edge = edge;
            Edge2Outer = edge2Outer;
            Geometry = geometry;
        }

        public int Font { get; }

        /// <summary>ルビか（グラデーション・画像を当てる範囲がルビの字面になる）。</summary>
        public bool Ruby { get; }

        /// <summary>縁の線の幅。</summary>
        public float Edge { get; }

        /// <summary>縁 2 の線の幅（縁 + 縁 2。付けないなら 0）。</summary>
        public float Edge2Outer { get; }

        public CanvasGeometry Geometry { get; }

        /// <summary>いちばん外側の線の幅。</summary>
        public float Outer => Math.Max(Edge, Edge2Outer);

        /// <summary>本体と、いちばん外側の縁を合わせた形（ブラーの元）。</summary>
        public CanvasGeometry Silhouette(CanvasStrokeStyle style)
        {
            if (_silhouette is not null) return _silhouette;
            if (Outer <= 0) return Geometry;
            using var stroke = Geometry.Stroke(Outer, style);
            _silhouette = Geometry.CombineWith(stroke, Matrix3x2.Identity, CanvasGeometryCombine.Union);
            return _silhouette;
        }

        public void Dispose()
        {
            _silhouette?.Dispose();
            Geometry.Dispose();
        }
    }

    private SubtitleLineLayout(List<N3FontSet> fonts)
    {
        Fonts = fonts;
    }

    /// <summary>使うフォント設定（<see cref="Token.Font"/>・<see cref="Group.Font"/> の添字）。</summary>
    public IReadOnlyList<N3FontSet> Fonts { get; }

    public List<Token> Tokens { get; } = new();

    public List<Group> Groups { get; } = new();

    public List<Image> Images { get; } = new();

    /// <summary>行の幅（最初の文字の送りの左端から最後の文字の送りの右端まで）。行を左右の余白にそろえるときはこの幅を使う。</summary>
    public float Width { get; private set; }

    /// <summary>本文の文字の枠（フォントの上端〜下端）の上端・下端。</summary>
    public float TextTop { get; private set; }

    public float TextBottom { get; private set; }

    /// <summary>
    /// 本文の文字の枠に縁の幅の半分ずつを足した上端・下端（行を並べる位置の基準）。ニコカラメーカー3 は、この枠どうしの間を行間にし、
    /// 下寄せでは下端を下余白の位置に、上寄せでは上端を上余白の位置に置く（出力画像の実測）。
    /// </summary>
    public float BoxTop { get; private set; }

    public float BoxBottom { get; private set; }

    /// <summary>描く範囲（ルビ・縁・飾り・画像を含む）の上端・下端。</summary>
    public float Top { get; private set; }

    public float Bottom { get; private set; }

    /// <summary>本文・ルビのグラデーション・画像を当てる範囲（字面）。</summary>
    public Rect MainInk { get; private set; }

    public Rect RubyInk { get; private set; }

    /// <summary>本文のいちばん大きい文字のサイズ（行リストで縮める倍率を決めるため）。</summary>
    public float MainSize { get; private set; }

    /// <summary>見つからなかったフォントがあるか（代わりのフォントで描いた）。</summary>
    public bool MissingFont { get; private set; }

    /// <summary>x の位置の表示の単位の番号（左端より左は 0、右端より右は最後）。単位が無ければ -1。</summary>
    public int TokenAt(float x)
    {
        if (Tokens.Count == 0) return -1;
        for (int i = 0; i < Tokens.Count; i++)
        {
            if (x < Tokens[i].X + Tokens[i].Width) return i;
        }
        return Tokens.Count - 1;
    }

    public void Dispose()
    {
        foreach (var g in Groups) g.Dispose();
        Groups.Clear();
    }

    // ------------------------------------------------------------ 作る

    /// <summary>行の配置を作る。<paramref name="unitFonts"/> は CharUnit ごとのフォント設定（null・足りない分は既定のフォント設定）。</summary>
    public static SubtitleLineLayout Build(LyricsLine line, IReadOnlyList<N3FontSet?> unitFonts, SubtitleContext context, SubtitleSpacing spacing)
    {
        var fonts = new List<N3FontSet>();
        var fontIndex = new Dictionary<N3FontSet, int>(ReferenceEqualityComparer.Instance);
        int FontOf(int unit)
        {
            var f = unit < unitFonts.Count ? unitFonts[unit] : null;
            f ??= context.DefaultFont;
            if (!fontIndex.TryGetValue(f, out int idx))
            {
                idx = fonts.Count;
                fonts.Add(f);
                fontIndex[f] = idx;
            }
            return idx;
        }

        var layout = new SubtitleLineLayout(fonts);
        var device = SubtitleGlyphCache.Device;
        var mainParts = new Dictionary<(int Font, int Face), List<CanvasGeometry>>();
        var rubyParts = new Dictionary<(int Font, int Face), List<CanvasGeometry>>();
        var glyphTokens = new List<(int Token, SubtitleGlyph Glyph, int Face)>();

        Rect? mainInk = null;
        float ascent = 0, descent = 0, mainOuter = 0, mainSize = 0;
        float imageTop = 0, imageBottom = 0;
        float x = 0;
        bool any = false;

        var occurrences = context.Matcher.IsEmpty
            ? new List<EmojiMatcher.Occurrence>()
            : context.Matcher.FindOccurrences(line.Chars);
        int occ = 0;
        for (int i = 0; i < line.Chars.Count; i++)
        {
            var c = line.Chars[i];
            if (occ < occurrences.Count && occurrences[occ].Start == i)
            {
                var o = occurrences[occ++];
                if (context.EmojiByString.TryGetValue(o.Value, out var entry))
                {
                    if (any) x += spacing.LyricsInterval;
                    int font = FontOf(i);
                    var face = N3FontLibrary.RenderFace(fonts[font], 0);
                    var opts = entry.ParseOptions();
                    string before = context.ResolvePath(entry.ImageBefore);
                    string? after = string.IsNullOrEmpty(entry.ImageAfter) ? null : context.ResolvePath(entry.ImageAfter);
                    double box = face.SizePx * opts.ZoomPercent / 100.0;
                    double w = box, h = box;
                    if (context.ImageSize(before) is { } size)
                    {
                        if (opts.Fix)
                        {
                            w = size.W;
                            h = size.H;
                        }
                        else
                        {
                            w = box * size.W / size.H;
                        }
                    }
                    // 縦の位置: 画像の下端を、文字の枠に縁の幅の半分を足した下端（行を並べる枠の下端）にそろえ、下余白だけ上げる
                    // （ニコカラメーカー3 の出力画像の実測）
                    var reference = SubtitleGlyphCache.Get("あ", face);
                    float bottom = (float)(reference.Descent + OuterEdge(face) / 2 - opts.MarginBottom);
                    float start = x;
                    x += (float)Math.Max(0, opts.MarginLeft);
                    var rect = new Rect(x, bottom - h, w, h);
                    layout.Images.Add(new Image(before, after, rect));
                    x += (float)w + (float)Math.Max(0, opts.MarginRight);
                    layout.Tokens.Add(new Token(o.Start, o.EndExclusive - 1, start, x - start, true, font));
                    imageTop = Math.Min(imageTop, (float)rect.Top);
                    imageBottom = Math.Max(imageBottom, (float)rect.Bottom);
                    ascent = Math.Max(ascent, reference.Ascent);
                    descent = Math.Max(descent, reference.Descent);
                    mainSize = Math.Max(mainSize, (float)face.SizePx);
                    any = true;
                    i = o.EndExclusive - 1;
                    continue;
                }
                // 置き換え文字列の画像の指定が無い（プレースホルダなど）: 文字として描く
            }
            if (c.IsSpacer) continue;

            if (any) x += spacing.LyricsInterval;
            int f = FontOf(i);
            int faceIndex = N3FontLibrary.FaceIndexFor(c.Text, ruby: false);
            var faceValue = N3FontLibrary.RenderFace(fonts[f], faceIndex);
            var glyph = SubtitleGlyphCache.Get(c.Text, faceValue);
            if (!glyph.FontFound) layout.MissingFont = true;
            float outerEdge = OuterEdge(faceValue);
            var (pitch, offset) = Pitch(glyph, faceValue, outerEdge, spacing.AllowBiting);
            layout.Tokens.Add(new Token(i, i, x, pitch, false, f));
            glyphTokens.Add((layout.Tokens.Count - 1, glyph, faceIndex));
            if (glyph.Geometry is not null)
            {
                var moved = glyph.Geometry.Transform(Matrix3x2.CreateTranslation(x + offset, 0));
                Add(mainParts, (f, faceIndex), moved);
                var ink = glyph.Ink!.Value;
                var r = new Rect(ink.X + x + offset, ink.Y, ink.Width, ink.Height);
                mainInk = mainInk is Rect u ? Union(u, r) : r;
            }
            ascent = Math.Max(ascent, glyph.Ascent);
            descent = Math.Max(descent, glyph.Descent);
            mainOuter = Math.Max(mainOuter, outerEdge);
            mainSize = Math.Max(mainSize, (float)faceValue.SizePx);
            x += pitch;
            any = true;
        }
        layout.Width = x;
        layout.MainSize = mainSize;
        layout.TextTop = -ascent;
        layout.TextBottom = descent;
        layout.BoxTop = -ascent - mainOuter / 2;
        layout.BoxBottom = descent + mainOuter / 2;
        layout.MainInk = mainInk ?? new Rect(0, -ascent, Math.Max(1, x), Math.Max(1, ascent + descent));

        // ルビ
        float rubyOuter = 0;
        Rect? rubyInk = null;
        var rubyGlyphs = PlaceRuby(line, layout, glyphTokens, fonts, spacing, ref rubyOuter);
        if (rubyGlyphs.Count > 0)
        {
            // ルビの文字の枠（縁の幅の半分を足したもの）の下端を、本文の行の枠の上端に付け、歌詞とルビの間隔だけ離す（ニコカラメーカー3 の出力画像の実測）
            float rubyDescent = rubyGlyphs.Max(g => g.Glyph.Descent);
            float baseline = layout.BoxTop - spacing.LyricsAndRubyInterval - rubyOuter / 2 - rubyDescent;
            foreach (var (gx, glyph, font, face) in rubyGlyphs)
            {
                if (glyph.Geometry is null) continue;
                Add(rubyParts, (font, face), glyph.Geometry.Transform(Matrix3x2.CreateTranslation(gx, baseline)));
                var ink = glyph.Ink!.Value;
                var r = new Rect(ink.X + gx, ink.Y + baseline, ink.Width, ink.Height);
                rubyInk = rubyInk is Rect u ? Union(u, r) : r;
            }
        }
        layout.RubyInk = rubyInk ?? layout.MainInk;

        foreach (var ((font, face), parts) in mainParts) layout.Groups.Add(MakeGroup(device, fonts[font], font, face, false, parts));
        foreach (var ((font, face), parts) in rubyParts) layout.Groups.Add(MakeGroup(device, fonts[font], font, face, true, parts));

        // 描く範囲（縁・飾りの分を足す）
        float decor = 0;
        foreach (var fs in fonts)
        {
            double d = double.IsFinite(fs.Detail.DecorSizePx) ? Math.Clamp(fs.Detail.DecorSizePx, 0, 500) : 0;
            if (fs.Detail.DecorKind == 1) decor = Math.Max(decor, (float)d);
            if (fs.Detail.DecorKind == 2) decor = Math.Max(decor, (float)(d * 1.5));
        }
        float outer = Math.Max(mainOuter, rubyOuter) / 2;
        float top = Math.Min(layout.TextTop, imageTop);
        if (rubyInk is Rect ri) top = Math.Min(top, (float)ri.Top);
        layout.Top = top - outer - decor;
        layout.Bottom = Math.Max(layout.TextBottom, imageBottom) + outer + decor;
        return layout;
    }

    /// <summary>ルビの文字を並べる（横の位置だけ。縦の位置は本文の字面の上端が決まってから）。</summary>
    private static List<(float X, SubtitleGlyph Glyph, int Font, int Face)> PlaceRuby(
        LyricsLine line, SubtitleLineLayout layout, List<(int Token, SubtitleGlyph Glyph, int Face)> glyphTokens,
        List<N3FontSet> fonts, SubtitleSpacing spacing, ref float rubyOuter)
    {
        var result = new List<(float, SubtitleGlyph, int, int)>();

        // ルビのまとまり: ルビを持つ文字から、続き（ルビが空）の文字まで。「＋」で連結した文字（RubyJoinsNext）は 1 つのまとまりにする
        var groups = new List<(int First, int Last, string Ruby)>();
        (int First, int Last, string Ruby)? current = null;
        int previousUnit = -1;
        foreach (var (tokenIndex, _, _) in glyphTokens)
        {
            var token = layout.Tokens[tokenIndex];
            var c = line.Chars[token.UnitStart];
            bool adjacent = current is not null && previousUnit >= 0 && NoTokenBetween(layout, current.Value.Last, tokenIndex);
            if (c.Ruby is { Length: > 0 } ruby)
            {
                if (current is { } cur && adjacent && line.Chars[layout.Tokens[cur.Last].UnitStart].RubyJoinsNext)
                {
                    current = (cur.First, tokenIndex, cur.Ruby + ruby);
                }
                else
                {
                    if (current is { } done) groups.Add(done);
                    current = (tokenIndex, tokenIndex, ruby);
                }
            }
            else if (c.Ruby is { Length: 0 } && current is { } cur && adjacent)
            {
                current = (cur.First, tokenIndex, cur.Ruby);
            }
            else
            {
                if (current is { } done) groups.Add(done);
                current = null;
            }
            previousUnit = token.UnitStart;
        }
        if (current is { } last) groups.Add(last);

        foreach (var (first, lastToken, ruby) in groups)
        {
            var t0 = layout.Tokens[first];
            var t1 = layout.Tokens[lastToken];
            int font = t0.Font;
            float gx0 = t0.X, gx1 = t1.X + t1.Width;
            var chars = new List<(SubtitleGlyph Glyph, int Face, float Pitch, float Offset)>();
            foreach (string ch in TextElements(ruby))
            {
                int face = N3FontLibrary.FaceIndexFor(ch, ruby: true);
                var value = N3FontLibrary.RenderFace(fonts[font], face);
                var glyph = SubtitleGlyphCache.Get(ch, value);
                float outerEdge = OuterEdge(value);
                var (pitch, offset) = Pitch(glyph, value, outerEdge, spacing.AllowBiting);
                chars.Add((glyph, face, pitch, offset));
                rubyOuter = Math.Max(rubyOuter, outerEdge);
            }
            if (chars.Count == 0) continue;
            float sum = chars.Sum(c => c.Pitch);
            float width = gx1 - gx0;

            // 自動配置: 親文字が英数字だけなら中央、それ以外は均等割り付け
            bool parentAlnum = true;
            for (int k = first; k <= lastToken; k++)
            {
                var tk = layout.Tokens[k];
                if (tk.IsEmoji || N3FontLibrary.FaceIndexFor(line.Chars[tk.UnitStart].Text, false) != 2) parentAlnum = false;
            }
            bool equal = spacing.RubyAlignment switch
            {
                1 => false,
                2 => true,
                _ => !parentAlnum,
            };
            float gapBetween = spacing.RubyInterval;
            float x;
            if (equal && chars.Count > 0 && sum + gapBetween * (chars.Count - 1) < width)
            {
                // 均等割り付け: 親文字の幅にルビを広げる（両端は間隔の半分）
                gapBetween = Math.Max(spacing.RubyInterval, (width - sum) / chars.Count);
                x = gx0 + (width - (sum + gapBetween * (chars.Count - 1))) / 2;
            }
            else
            {
                x = (gx0 + gx1) / 2 - (sum + gapBetween * (chars.Count - 1)) / 2;
            }
            foreach (var (glyph, face, pitch, offset) in chars)
            {
                result.Add((x + offset, glyph, font, face));
                x += pitch + gapBetween;
            }
        }
        return result;
    }

    /// <summary>
    /// 文字の送り幅と、送りの左端から文字の原点までのずれ。食い込みを許さない（ニコカラメーカー3 の既定）ときは、字面の左右に
    /// 「サイドベアリング（文字サイズの 2 割までで打ち切る）の半分 ＋ 縁の幅の半分 ＋ 送り幅の 2% − 0.2」ずつのすき間をとって並べる。
    /// 字面の無い文字（空白）は「送り幅 × 0.54 ＋ 縁の幅 − 0.4」。
    /// ニコカラメーカー3 の出力画像（1920x1080）で文字の位置を測って求めた近似（英字・かな・漢字・小さい「っ」・全角「！」・空白・ルビで、
    /// 隣どうしの字面のすき間が平均 0.7px の差で合う）。食い込みを許すときはフォントの送り幅のまま（縁は重なる）。
    /// </summary>
    private static (float Pitch, float Offset) Pitch(SubtitleGlyph glyph, N3FontFace face, float outerEdge, bool allowBiting)
    {
        if (allowBiting) return (glyph.Advance, 0);
        float advance = glyph.Advance;
        if (glyph.Ink is not Rect ink)
        {
            float blank = Math.Max(0, advance * 0.54f + outerEdge - 0.4f);
            return (blank, (blank - advance) / 2);
        }
        float xScale = (face.XScale > 0 ? Math.Clamp(face.XScale, 10, 1000) : 100) / 100f;
        float size = (float)(double.IsFinite(face.SizePx) ? Math.Clamp(face.SizePx, 1, 2000) : 100) * xScale;
        float cap = size * 0.2f;
        float Pad(float bearing) => Math.Max(0, Math.Clamp(bearing, 0, cap) / 2 + outerEdge / 2 + advance * 0.02f - 0.2f);
        float left = Pad((float)ink.X);
        float right = Pad(advance - (float)ink.Right);
        return (left + (float)ink.Width + right, left - (float)ink.X);
    }

    /// <summary>2 つの表示の単位のあいだに絵文字が無いか（ルビのまとまりは絵文字をまたがない）。</summary>
    private static bool NoTokenBetween(SubtitleLineLayout layout, int a, int b)
    {
        for (int k = a + 1; k < b; k++)
        {
            if (layout.Tokens[k].IsEmoji) return false;
        }
        return true;
    }

    private static IEnumerable<string> TextElements(string text)
    {
        var e = System.Globalization.StringInfo.GetTextElementEnumerator(text);
        while (e.MoveNext()) yield return (string)e.Current;
    }

    private static Group MakeGroup(Microsoft.Graphics.Canvas.CanvasDevice device, N3FontSet fontSet, int font, int face, bool ruby, List<CanvasGeometry> parts)
    {
        var value = N3FontLibrary.RenderFace(fontSet, face);
        var geometry = CanvasGeometry.CreateGroup(device, parts.ToArray(), CanvasFilledRegionDetermination.Winding);
        foreach (var part in parts) part.Dispose(); // まとめた形が中身を持っているので、元の形は手放してよい
        float edge = (float)Finite(value.EdgePx);
        float edge2 = value.UseEdge2 == true ? (float)Finite(value.Edge2Px) : 0;
        return new Group(font, ruby, edge, edge2 > 0 ? edge + edge2 : 0, geometry);
    }

    private static float OuterEdge(N3FontFace face)
    {
        double edge = Finite(face.EdgePx);
        double edge2 = face.UseEdge2 == true ? Finite(face.Edge2Px) : 0;
        return (float)Math.Max(edge, edge2 > 0 ? edge + edge2 : 0);
    }

    private static double Finite(double v) => double.IsFinite(v) ? Math.Clamp(v, 0, 500) : 0;

    private static void Add(Dictionary<(int, int), List<CanvasGeometry>> parts, (int, int) key, CanvasGeometry geometry)
    {
        if (!parts.TryGetValue(key, out var list)) parts[key] = list = new List<CanvasGeometry>();
        list.Add(geometry);
    }

    private static Rect Union(Rect a, Rect b)
    {
        double left = Math.Min(a.Left, b.Left), top = Math.Min(a.Top, b.Top);
        double right = Math.Max(a.Right, b.Right), bottom = Math.Max(a.Bottom, b.Bottom);
        return new Rect(left, top, right - left, bottom - top);
    }
}
