using System.Globalization;
using Microsoft.Graphics.Canvas.Text;
using Windows.UI.Text;

namespace NicoKaraPrep.App.Services;

/// <summary>システムのフォントの 1 フェイス（太字・斜体などのバリエーション）。</summary>
/// <param name="Name">表示・保存に使う名前（日本語の名前があればそれ。ニコカラメーカー3 の「詳細」と同じ "ﾍﾋﾞｰ" など）。</param>
/// <param name="AllNames">どの言語の名前でも照合できるよう、すべての言語の名前。</param>
public sealed record SystemFontFace(string Name, IReadOnlyList<string> AllNames, FontWeight Weight, FontStyle Style, FontStretch Stretch)
{
    /// <summary>名前のどれかが <paramref name="name"/> と同じか（大文字小文字は区別しない）。</summary>
    public bool Matches(string name) => AllNames.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>システムのフォントの 1 ファミリー。</summary>
/// <param name="Name">表示・保存に使う名前（日本語の名前があればそれ。"メイリオ" "HGS創英角ﾎﾟｯﾌﾟ体" など）。</param>
/// <param name="AllNames">どの言語の名前でも検索・照合できるよう、すべての言語の名前（"Meiryo" など）。</param>
/// <param name="SupportsJapanese">かな・漢字を含むか（分からないときは true）。</param>
public sealed record SystemFontFamily(string Name, IReadOnlyList<string> AllNames, IReadOnlyList<SystemFontFace> Faces, bool SupportsJapanese)
{
    /// <summary>名前のどれかが <paramref name="name"/> と同じか（大文字小文字は区別しない）。</summary>
    public bool Matches(string name) => AllNames.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>名前のどれかが <paramref name="query"/> を含むか（検索用）。</summary>
    public bool Contains(string query) => AllNames.Any(n => n.Contains(query, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 既定で選ぶフェイス。<paramref name="preferredName"/> と同じ名前があればそれ、無ければ標準（太さ 400・斜体なし・幅 標準）に
    /// いちばん近いもの。
    /// </summary>
    public SystemFontFace? DefaultFace(string? preferredName)
    {
        if (!string.IsNullOrEmpty(preferredName) && Faces.FirstOrDefault(f => f.Matches(preferredName)) is { } same) return same;
        return Faces
            .OrderBy(f => f.Style == FontStyle.Normal ? 0 : 1)
            .ThenBy(f => Math.Abs(f.Stretch - FontStretch.Normal))
            .ThenBy(f => Math.Abs(f.Weight.Weight - 400))
            .FirstOrDefault();
    }
}

/// <summary>
/// システムにあるフォントの一覧（ファミリーごとのフェイス、日本語の有無）。フォントを選ぶ画面で使う。
/// DirectWrite のシステムのフォントセットから名前・太さ・斜体・幅を読み、かなを持つかを調べる。
/// 1 回目は別スレッドで作り、アプリの実行中は覚えておく。
/// </summary>
public static class SystemFontCatalog
{
    /// <summary>名前を選ぶ言語の順（ニコカラメーカー3 は日本語の名前で保存する。無ければ英語）。</summary>
    private static readonly string[] PreferredLocales = { "ja-jp", "ja", "en-us", "en" };

    /// <summary>日本語のフォントとみなすために調べる文字（ひらがなの「あ」とカタカナの「ア」）。</summary>
    private static readonly uint[] JapaneseProbes = { 0x3042, 0x30A2 };

    private static readonly object Gate = new();
    private static Task<IReadOnlyList<SystemFontFamily>>? _loading;

    /// <summary>一覧を返す（まだ無ければ別スレッドで作り始める。何度呼んでも作るのは 1 回だけ）。</summary>
    public static Task<IReadOnlyList<SystemFontFamily>> LoadAsync()
    {
        lock (Gate)
        {
            return _loading ??= Task.Run(Build);
        }
    }

    private static IReadOnlyList<SystemFontFamily> Build()
    {
        try
        {
            return BuildCore();
        }
        catch (Exception)
        {
            // フォントの一覧を引けない環境でも、名前の入力でフォントは指定できる
            return Array.Empty<SystemFontFamily>();
        }
    }

    private static IReadOnlyList<SystemFontFamily> BuildCore()
    {
        // システムのフォントセットは Win2D の中で同じものが返ることがあり（DirectWriteFontResolver も使う）、閉じると共有先が壊れるので Dispose しない
        var set = CanvasFontSet.GetSystemFontSet();
        int count = set.Fonts.Count;
        var byFamily = new Dictionary<string, FamilyBuilder>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < count; i++)
        {
            uint index = (uint)i;
            var familyNames = Values(set, index, CanvasFontPropertyIdentifier.FamilyName);
            string? family = Pick(familyNames);
            if (string.IsNullOrEmpty(family)) continue;

            if (!byFamily.TryGetValue(family, out var builder))
            {
                builder = new FamilyBuilder(family, index);
                byFamily[family] = builder;
            }
            foreach (string n in familyNames.Values) builder.AddName(n);
            foreach (string n in Values(set, index, CanvasFontPropertyIdentifier.Win32FamilyName).Values) builder.AddName(n);

            var faceNames = Values(set, index, CanvasFontPropertyIdentifier.FaceName);
            string face = Pick(faceNames) ?? "";
            var weight = new FontWeight { Weight = (ushort)Math.Clamp(Number(set, index, CanvasFontPropertyIdentifier.Weight, 400), 1, 999) };
            var style = (FontStyle)Math.Clamp(Number(set, index, CanvasFontPropertyIdentifier.Style, 0), 0, 2);
            var stretch = (FontStretch)Math.Clamp(Number(set, index, CanvasFontPropertyIdentifier.Stretch, 5), 1, 9);
            builder.AddFace(face, faceNames.Values, weight, style, stretch);
        }

        return byFamily.Values
            .Select(b => b.Build(HasJapanese(set, b.FirstIndex)))
            .OrderBy(f => f.Name, StringComparer.Create(CultureInfo.GetCultureInfo("ja-JP"), ignoreCase: true))
            .ToList();
    }

    private static IReadOnlyDictionary<string, string> Values(CanvasFontSet set, uint index, CanvasFontPropertyIdentifier id)
    {
        try
        {
            return set.GetPropertyValues(index, id) ?? (IReadOnlyDictionary<string, string>)new Dictionary<string, string>();
        }
        catch (Exception)
        {
            return new Dictionary<string, string>();
        }
    }

    private static int Number(CanvasFontSet set, uint index, CanvasFontPropertyIdentifier id, int fallback)
    {
        foreach (string v in Values(set, index, id).Values)
        {
            if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) return n;
        }
        return fallback;
    }

    /// <summary>
    /// フォントがかなを持つか（ファミリーの最初のフェイスで調べる。フォントのファイルを読むので別スレッドで呼ぶ）。
    /// 調べられないときは、絞り込みで隠さないよう true にする。
    /// </summary>
    private static bool HasJapanese(CanvasFontSet set, uint index)
    {
        try
        {
            var face = set.Fonts[(int)index];
            return JapaneseProbes.Any(face.HasCharacter);
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <summary>言語ごとの名前から、日本語 → 英語 → そのほかの順に 1 つ選ぶ。</summary>
    private static string? Pick(IReadOnlyDictionary<string, string> names)
    {
        foreach (string locale in PreferredLocales)
        {
            foreach (var (key, value) in names)
            {
                if (string.Equals(key, locale, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value)) return value;
            }
        }
        return names.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
    }

    private sealed class FamilyBuilder
    {
        private readonly string _name;
        private readonly List<string> _names = new();
        private readonly List<SystemFontFace> _faces = new();

        public FamilyBuilder(string name, uint firstIndex)
        {
            _name = name;
            FirstIndex = firstIndex;
            AddName(name);
        }

        /// <summary>このファミリーの最初のフォント（フォントセットの中の番号。日本語の有無を調べるのに使う）。</summary>
        public uint FirstIndex { get; }

        public void AddName(string name)
        {
            if (!string.IsNullOrWhiteSpace(name) && !_names.Contains(name, StringComparer.OrdinalIgnoreCase)) _names.Add(name);
        }

        public void AddFace(string name, IEnumerable<string> allNames, FontWeight weight, FontStyle style, FontStretch stretch)
        {
            // 同じ名前のフェイスが複数のファイルにあるとき（利用者が同じフォントを入れ直したなど）は 1 つにまとめる
            if (_faces.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))) return;
            var names = allNames.Where(n => !string.IsNullOrWhiteSpace(n)).Prepend(name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            _faces.Add(new SystemFontFace(name, names, weight, style, stretch));
        }

        public SystemFontFamily Build(bool supportsJapanese) => new(
            _name,
            _names,
            _faces.OrderBy(f => f.Style).ThenBy(f => Math.Abs(f.Stretch - FontStretch.Normal)).ThenBy(f => f.Weight.Weight).ToList(),
            supportsJapanese);
    }
}
