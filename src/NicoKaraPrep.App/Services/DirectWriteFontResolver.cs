using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Text;
using Windows.UI.Text;

namespace NicoKaraPrep.App.Services;

/// <summary>
/// 描画に使うフォント（Win2D の <see cref="CanvasTextFormat"/> に渡すファミリー名・太さ・斜体・幅）。
/// </summary>
/// <param name="Family">CanvasTextFormat.FontFamily に渡す名前。</param>
/// <param name="Weight">太さ。</param>
/// <param name="Style">斜体。</param>
/// <param name="Stretch">幅。</param>
/// <param name="Found">指定のフォントがシステムにあったか（false なら代わりのフォント）。</param>
public readonly record struct ResolvedFont(string Family, FontWeight Weight, FontStyle Style, FontStretch Stretch, bool Found);

/// <summary>
/// ニコカラメーカー3 のフォント名（FontName）とフェイス名（FontFaceName。DirectWrite のフェイス名で、"ﾍﾋﾞｰ" のような半角カナ名もある）から、
/// 描画に使うフォントを決める。システムのフォントからファミリー名とフェイス名が一致するものを探し、その太さ・斜体・幅を使う。
/// 見つからないフェイス名は名前から太さ・斜体を推し量る。フォント名が空ならニコカラメーカー3 の既定（ＭＳ Ｐ明朝）に近いものを使う。
/// 結果はアプリの実行中は覚えておく（UI スレッドから呼ぶ）。
/// </summary>
public static class DirectWriteFontResolver
{
    /// <summary>フォント名が空・見つからないときに使う候補（ニコカラメーカー3 の既定はＭＳ Ｐ明朝。無ければ他の明朝体、最後はメイリオ）。</summary>
    private static readonly string[] DefaultFamilies = { "ＭＳ Ｐ明朝", "MS PMincho", "游明朝", "Yu Mincho", "メイリオ", "Meiryo", "Yu Gothic UI" };

    /// <summary>フォント名を探すときに見るファミリー名の種類（WWS ファミリー名、Win32 のファミリー名、typographic ファミリー名の順）。</summary>
    private static readonly CanvasFontPropertyIdentifier[] FamilyIdentifiers =
    {
        CanvasFontPropertyIdentifier.FamilyName,
        CanvasFontPropertyIdentifier.Win32FamilyName,
        CanvasFontPropertyIdentifier.PreferredFamilyName,
    };

    private static readonly Dictionary<(string Font, string Face), ResolvedFont> Cache = new();
    private static CanvasFontSet? _systemFonts;
    private static string? _defaultFamily;

    /// <summary>描画に使うフォントを決める。</summary>
    public static ResolvedFont Resolve(string? fontName, string? faceName)
    {
        string font = fontName ?? "";
        string face = faceName ?? "";
        if (Cache.TryGetValue((font, face), out var cached)) return cached;

        var result = ResolveCore(font.Trim(), face.Trim());
        Cache[(font, face)] = result;
        return result;
    }

    /// <summary>
    /// フェイス名から太さ・斜体・幅を推し量る（システムに同じ名前のフェイスが無いとき）。
    /// "Bold"・"太字"・"ﾎﾞｰﾙﾄﾞ" は Bold、"ﾍﾋﾞｰ"・"Heavy"・"ｴｸｽﾄﾗﾎﾞｰﾙﾄﾞ" は ExtraBold、"Black"・"ｳﾙﾄﾗ" は Black、"Light"・"細" は Light、
    /// "Italic"・"斜体" は斜体。それ以外は標準。
    /// </summary>
    public static (FontWeight Weight, FontStyle Style, FontStretch Stretch) GuessFromFaceName(string? faceName)
    {
        string f = faceName ?? "";
        bool Has(params string[] words) => words.Any(w => f.Contains(w, StringComparison.OrdinalIgnoreCase));

        FontWeight weight =
            Has("Black", "ｳﾙﾄﾗ", "ウルトラ") ? FontWeights.Black
            : Has("ExtraBold", "Extra Bold", "UltraBold", "Heavy", "ﾍﾋﾞｰ", "ヘビー", "ｴｸｽﾄﾗﾎﾞｰﾙﾄﾞ", "エクストラボールド") ? FontWeights.ExtraBold
            : Has("SemiBold", "Semi Bold", "DemiBold", "Demi Bold", "ﾃﾞﾐﾎﾞｰﾙﾄﾞ", "デミボールド", "ｾﾐﾎﾞｰﾙﾄﾞ", "セミボールド") ? FontWeights.SemiBold
            : Has("Bold", "太字", "ﾎﾞｰﾙﾄﾞ", "ボールド") ? FontWeights.Bold
            : Has("Medium", "ﾐﾃﾞｨｱﾑ", "ミディアム") ? FontWeights.Medium
            : Has("ExtraLight", "Extra Light", "UltraLight") ? FontWeights.ExtraLight
            : Has("Light", "細", "ﾗｲﾄ", "ライト") ? FontWeights.Light
            : Has("Thin") ? FontWeights.Thin
            : FontWeights.Normal;
        FontStyle style = Has("Italic", "斜体", "ｲﾀﾘｯｸ", "イタリック") ? FontStyle.Italic
            : Has("Oblique") ? FontStyle.Oblique
            : FontStyle.Normal;
        return (weight, style, FontStretch.Normal);
    }

    private static ResolvedFont ResolveCore(string font, string face)
    {
        var guessed = GuessFromFaceName(face);
        if (font.Length == 0)
        {
            // 空はニコカラメーカー3 の既定のフォント（継承の根）。見つからない扱いにはしない
            return new ResolvedFont(DefaultFamily(), guessed.Weight, guessed.Style, guessed.Stretch, true);
        }

        try
        {
            var fonts = SystemFonts();
            foreach (var id in FamilyIdentifiers)
            {
                using var family = fonts.GetMatchingFonts(new[] { Property(id, font) });
                if (family.Fonts.Count == 0) continue;

                // CanvasTextFormat は WWS ファミリー名で探すので、別の種類の名前で見つかったときは WWS の名前に直す
                string familyName = id == CanvasFontPropertyIdentifier.FamilyName ? font : WwsFamilyName(family) ?? font;
                if (face.Length > 0)
                {
                    using var faces = family.GetMatchingFonts(new[] { Property(CanvasFontPropertyIdentifier.FaceName, face) });
                    if (faces.Fonts.Count > 0)
                    {
                        using var fontFace = faces.Fonts[0];
                        return new ResolvedFont(familyName, fontFace.Weight, fontFace.Style, fontFace.Stretch, true);
                    }
                }
                return new ResolvedFont(familyName, guessed.Weight, guessed.Style, guessed.Stretch, true);
            }
        }
        catch (Exception)
        {
            // フォントの一覧を引けない環境でも、代わりのフォントで描く
        }
        return new ResolvedFont(DefaultFamily(), guessed.Weight, guessed.Style, guessed.Stretch, false);
    }

    /// <summary>フォント名が空のときに使うファミリー名（候補のうちシステムにある最初のもの）。</summary>
    private static string DefaultFamily()
    {
        if (_defaultFamily is not null) return _defaultFamily;
        string chosen = "Yu Gothic UI";
        try
        {
            var fonts = SystemFonts();
            foreach (string name in DefaultFamilies)
            {
                if (fonts.CountFontsMatchingProperty(Property(CanvasFontPropertyIdentifier.FamilyName, name)) > 0)
                {
                    chosen = name;
                    break;
                }
            }
        }
        catch (Exception)
        {
            // 一覧を引けなければ Yu Gothic UI（無ければ DirectWrite が代わりを選ぶ）
        }
        _defaultFamily = chosen;
        return chosen;
    }

    private static CanvasFontSet SystemFonts() => _systemFonts ??= CanvasFontSet.GetSystemFontSet();

    /// <summary>ロケールを空にすると、どの言語の名前とも照合する。</summary>
    private static CanvasFontProperty Property(CanvasFontPropertyIdentifier id, string value) =>
        new() { Identifier = id, Value = value, Locale = "" };

    private static string? WwsFamilyName(CanvasFontSet family)
    {
        var names = family.GetPropertyValues(0, CanvasFontPropertyIdentifier.FamilyName);
        if (names is null || names.Count == 0) return null;
        foreach (string locale in new[] { "ja-jp", "ja-JP", "en-us", "en-US" })
        {
            if (names.TryGetValue(locale, out string? name) && !string.IsNullOrEmpty(name)) return name;
        }
        return names.Values.FirstOrDefault(n => !string.IsNullOrEmpty(n));
    }
}
