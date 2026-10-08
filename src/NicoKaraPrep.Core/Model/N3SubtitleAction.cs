using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using NicoKaraPrep.Core.Formats;

namespace NicoKaraPrep.Core.Model;

/// <summary>
/// ニコカラメーカー3 の字幕アクション 1 件（行ごとに持つ。Id と設定値）。
/// 設定値は n3proj の SubtitleActionSettings と同じ形（<c>$type</c> 付き）で持ち、書き出しの直前に
/// <see cref="N3SubtitleActionCatalog.Normalize"/> で新しい書式にそろえる。知らない Id の設定値は中身をそのまま保つ。
/// </summary>
public sealed class N3SubtitleAction
{
    private string _id = "";
    private JsonObject _settings = new();

    public N3SubtitleAction()
    {
    }

    /// <param name="id">アクションの Id。</param>
    /// <param name="settings">設定値（そのまま持つ。写しが要るなら呼び出し側で写す）。null は空のオブジェクト。</param>
    public N3SubtitleAction(string id, JsonObject? settings)
    {
        Id = id;
        Settings = settings!;
    }

    /// <summary>アクションの Id（"SHINTA.CharFadeInFadeOut" など）。null を入れると空になる。</summary>
    public string Id
    {
        get => _id;
        set => _id = value ?? "";
    }

    /// <summary>設定値（n3proj の SubtitleActionSettings と同じ形。<c>$type</c> 付き）。null を入れると空のオブジェクトになる。</summary>
    public JsonObject Settings
    {
        get => _settings;
        set => _settings = value ?? new JsonObject();
    }

    /// <summary>深い写し（設定値も写す）。</summary>
    public N3SubtitleAction Clone() => new(Id, (JsonObject)Settings.DeepClone());

    /// <summary>
    /// 同じアクションか。Id と設定値の値を比べる（版数 CreateAppVer/ModifyAppVer と、旧書式の画面用の項目 *Visibility・*TimeTag などは無視。
    /// 知っている Id は新しい書式にそろえてから比べるので、旧書式と新書式の同じ値は同じとみなす）。
    /// </summary>
    public bool SameAs(N3SubtitleAction? other) =>
        other is not null && N3SubtitleActionCatalog.CompareKey(this) == N3SubtitleActionCatalog.CompareKey(other);

    /// <summary>2 つが同じアクションか（どちらも null なら同じ）。</summary>
    public static bool AreSame(N3SubtitleAction? a, N3SubtitleAction? b) => a is null ? b is null : a.SameAs(b);

    /// <summary>設定値の整数の項目（無い・数値でなければ null）。</summary>
    public int? GetInt(string key) => N3FontJson.Int(Settings[key]);

    /// <summary>設定値の真偽値の項目（無い・真偽値でなければ null）。</summary>
    public bool? GetBool(string key) => N3FontJson.Bool(Settings[key]);

    /// <summary>設定値の大きさ（px）の項目を、画面の高さ <paramref name="screenHeight"/> での px にしたもの（無い・読めなければ null）。</summary>
    public double? GetPixels(string key, int screenHeight) => N3SubtitleActionCatalog.PixelsAt(Settings[key], screenHeight);

    /// <summary>設定値の整数の項目を書く（ある項目は同じ位置で置き換える）。</summary>
    public void Set(string key, int value) => Settings[key] = value;

    /// <summary>設定値の真偽値の項目を書く（ある項目は同じ位置で置き換える）。</summary>
    public void Set(string key, bool value) => Settings[key] = value;

    /// <summary>設定値の大きさ（px）の項目を、画面の高さ <paramref name="screenHeight"/> での px として書く（{Size, Reference, Ratio} の形）。</summary>
    public void SetPixels(string key, double px, int screenHeight) => Settings[key] = N3SubtitleActionCatalog.PixelsNode(px, screenHeight);
}

/// <summary>字幕アクションの設定項目の種類。</summary>
public enum N3SubtitleActionFieldKind
{
    /// <summary>時間（ms、整数）。</summary>
    Milliseconds,

    /// <summary>する・しない。</summary>
    Bool,

    /// <summary>
    /// 大きさ（px。上下スライドのスライド量など）。n3proj では {Size, Reference, Ratio}（Reference の画面の高さでの px と、高さに対する比。
    /// レイアウト・フォントの大きさと同じ形）で持つ。マイナスもある。
    /// </summary>
    Pixels,
}

/// <summary>字幕アクションの設定項目 1 つ。</summary>
/// <param name="Key">設定値の JSON の項目名（"FadeInTime" など）。</param>
/// <param name="Label">画面の名前（ニコカラメーカー3 の設定画面に合わせる）。</param>
/// <param name="Kind">項目の種類。</param>
/// <param name="Default">
/// 既定値（<see cref="N3SubtitleActionFieldKind.Milliseconds"/> は int の ms、<see cref="N3SubtitleActionFieldKind.Bool"/> は bool、
/// <see cref="N3SubtitleActionFieldKind.Pixels"/> は <see cref="N3SubtitleActionCatalog.PixelsReference"/> の高さでの int の px）。
/// </param>
/// <param name="Visible">画面に出すか（ニコカラメーカー3 が書くが使わない項目は false）。</param>
/// <param name="TrueText">真偽値が既定値と違って true のときの短い説明（<see cref="N3SubtitleActionCatalog.Describe"/> 用）。</param>
/// <param name="FalseText">真偽値が既定値と違って false のときの短い説明。</param>
public sealed record N3SubtitleActionField(
    string Key,
    string Label,
    N3SubtitleActionFieldKind Kind,
    object Default,
    bool Visible = true,
    string TrueText = "",
    string FalseText = "")
{
    /// <summary>既定値の JSON の値。</summary>
    public JsonNode CreateDefaultNode() => Kind switch
    {
        N3SubtitleActionFieldKind.Bool => JsonValue.Create(Convert.ToBoolean(Default, CultureInfo.InvariantCulture)),
        N3SubtitleActionFieldKind.Pixels => N3SubtitleActionCatalog.PixelsNode(Convert.ToDouble(Default, CultureInfo.InvariantCulture), N3SubtitleActionCatalog.PixelsReference),
        _ => JsonValue.Create(Convert.ToInt32(Default, CultureInfo.InvariantCulture)),
    };

    /// <summary>source の値がこの項目の種類に合えば、その写し（無い・合わなければ null）。</summary>
    internal JsonNode? ValueFrom(JsonObject? source)
    {
        if (source is null || !source.TryGetPropertyValue(Key, out var node) || node is null) return null;
        return Kind switch
        {
            N3SubtitleActionFieldKind.Bool => N3FontJson.Bool(node) is bool b ? JsonValue.Create(b) : null,
            N3SubtitleActionFieldKind.Milliseconds => N3FontJson.Int(node) is int i ? JsonValue.Create(i) : null,
            N3SubtitleActionFieldKind.Pixels => PixelsFrom(node),
            _ => null,
        };
    }

    /// <summary>大きさ（px）の値。{Size, Reference, Ratio} は読めればそのまま写し、数だけなら基準の高さでの px とみなす。</summary>
    private static JsonNode? PixelsFrom(JsonNode node)
    {
        if (node is JsonObject) return N3SubtitleActionCatalog.PixelsAt(node, N3SubtitleActionCatalog.PixelsReference) is null ? null : node.DeepClone();
        return N3FontJson.Number(node) is double px ? N3SubtitleActionCatalog.PixelsNode(px, N3SubtitleActionCatalog.PixelsReference) : null;
    }
}

/// <summary>字幕アクションの種類 1 つ（ニコカラメーカー3 の字幕アクションの編集ウィンドウの選択肢）。</summary>
/// <param name="Id">SubtitleActionId。</param>
/// <param name="Name">表示名（ニコカラメーカー3 と同じ）。</param>
/// <param name="TypeName">設定値の <c>$type</c>（新しい書式のもの）。</param>
/// <param name="Fields">設定項目（画面に出す順）。</param>
public sealed record N3SubtitleActionKind(string Id, string Name, string TypeName, IReadOnlyList<N3SubtitleActionField> Fields)
{
    /// <summary>設定値に書く項目の並び（ニコカラメーカー3 が保存する順。<see cref="Fields"/> と同じ項目）。</summary>
    public IReadOnlyList<string> WriteOrder { get; init; } = Fields.Select(f => f.Key).ToList();

    /// <summary>画面に出す設定項目。</summary>
    public IEnumerable<N3SubtitleActionField> VisibleFields => Fields.Where(f => f.Visible);

    /// <summary>項目名から設定項目（無ければ null）。</summary>
    public N3SubtitleActionField? Field(string key) => Fields.FirstOrDefault(f => f.Key == key);
}

/// <summary>字幕アクションの既定（行ごとの指定が無い歌詞行に書くもの）を決めた出どころ。</summary>
public enum N3SubtitleActionSource
{
    /// <summary>曲の既定（N3ProjSongSettings.SubtitleAction）。</summary>
    Song,

    /// <summary>ベースの n3proj の歌詞行でいちばん多いアクション。</summary>
    Base,

    /// <summary>ニコカラメーカー3 の「すべて同じ字幕アクションにする」の Id。</summary>
    Nkm3,

    /// <summary>どれも無いときの文字単位フェード。</summary>
    Standard,
}

/// <summary>
/// ニコカラメーカー3 の字幕アクションの種類（Id・表示名・設定値の <c>$type</c>・設定項目と既定値）と、設定値の書式をそろえる処理。
/// 8 種類（ニコカラメーカー3 の選択肢すべて）。スピンフリップ・ユートピア・上下スライドの既定値は、ユーザーが作った見本の n3proj
/// （Ver 13.90 で保存）の値をそのまま使っている（ニコカラメーカー3 の既定値かは確認中）。
/// 知らない Id は「そのほか」として表示だけし、設定値は中身をそのまま保つ。
/// </summary>
public static class N3SubtitleActionCatalog
{
    public const string NoActionId = "SHINTA.NoAction";
    public const string LineFadeInId = "SHINTA.LineFadeIn";
    public const string LineFadeOutId = "SHINTA.LineFadeOut";
    public const string LineFadeInFadeOutId = "SHINTA.LineFadeInFadeOut";
    public const string CharFadeInFadeOutId = "SHINTA.CharFadeInFadeOut";
    public const string SpinFlipId = "SHINTA.SpinFlip";
    public const string UtopiaId = "SHINTA.Utopia";
    public const string SlideUpDownId = "SHINTA.SlideUpDown";

    /// <summary>ニコカラメーカー3 の「すべて同じ字幕アクションにする」の設定（AddOns の下のファイル名の Id）。</summary>
    public const string UnificationSelectorId = "SHINTA.UnificationSubtitleActionSelector";

    /// <summary>どの指定も無いときのアクション（文字単位フェード）。</summary>
    public const string StandardId = CharFadeInFadeOutId;

    /// <summary>「アクションしない」の新しい書式の <c>$type</c>（空行・区切り行の設定値と同じ）。</summary>
    public const string AddOnSettingsTypeName = "AddOnSettingsModel";

    /// <summary>大きさ（px）の既定値の基準の画面の高さ。</summary>
    public const int PixelsReference = 1080;

    private const string LineFadeTypeName = "SubtitleActionSettingsModel";
    private const string CharFadeTypeName = "CharFadeInFadeOutSettingsModel";

    private static N3SubtitleActionField FadeIn(bool visible = true) =>
        new("FadeInTime", "フェードイン時間", N3SubtitleActionFieldKind.Milliseconds, 250, visible);

    private static N3SubtitleActionField FadeOut(bool visible = true) =>
        new("FadeOutTime", "フェードアウト時間", N3SubtitleActionFieldKind.Milliseconds, 250, visible);

    private static N3SubtitleActionField IntroDelay() =>
        new("IntroDelay", "先頭と末尾の時間差", N3SubtitleActionFieldKind.Milliseconds, 350);

    private static N3SubtitleActionField TailDelay() =>
        new("TailDelay", "行末が消え始めるまで", N3SubtitleActionFieldKind.Milliseconds, 250);

    private static N3SubtitleActionField WholeFadeOut(bool byDefault) =>
        new("WholeFadeOut", "表示終了時刻を基準にフェードアウト", N3SubtitleActionFieldKind.Bool, byDefault,
            TrueText: "表示終了基準", FalseText: "表示終了基準にしない");

    private static N3SubtitleActionField DelayInlineGraphics(bool byDefault) =>
        new("DelayInlineGraphics", "インライングラフィックス（アイコン）のフェードアウトを遅らせる", N3SubtitleActionFieldKind.Bool, byDefault,
            TrueText: "アイコンを遅らせる", FalseText: "アイコンを遅らせない");

    /// <summary>文字単位フェードの型（文字単位フェード・スピンフリップ）の項目。</summary>
    private static N3SubtitleActionField[] CharFadeFields(bool wholeFadeOut, bool delayInlineGraphics) => new[]
    {
        IntroDelay(),
        TailDelay(),
        FadeIn(),
        FadeOut(),
        WholeFadeOut(wholeFadeOut),
        DelayInlineGraphics(delayInlineGraphics),
    };

    private static readonly string[] CharFadeWriteOrder = { "IntroDelay", "WholeFadeOut", "TailDelay", "DelayInlineGraphics", "FadeInTime", "FadeOutTime" };

    /// <summary>知っている字幕アクション（ニコカラメーカー3 の選択肢の順）。</summary>
    public static IReadOnlyList<N3SubtitleActionKind> Known { get; } = new List<N3SubtitleActionKind>
    {
        new(NoActionId, "アクションしない", AddOnSettingsTypeName, Array.Empty<N3SubtitleActionField>()),
        new(LineFadeInId, "フェードイン", LineFadeTypeName, new[] { FadeIn(), FadeOut(visible: false) }),
        new(LineFadeOutId, "フェードアウト", LineFadeTypeName, new[] { FadeOut(), FadeIn(visible: false) })
        {
            WriteOrder = new[] { "FadeInTime", "FadeOutTime" },
        },
        new(LineFadeInFadeOutId, "フェードイン/アウト", LineFadeTypeName, new[] { FadeIn(), FadeOut() }),
        // 既定値はニコカラメーカー3 の初期値（表示終了基準にする・アイコンを遅らせない）。ユーザーは「アイコンを遅らせる」を入れ、
        // 「表示終了基準」を外して使っている（AddOns\SHINTA.CharFadeInFadeOut.json）。実データでも、設定が初期状態に戻った時期（2024-10）の
        // n3proj だけがこの組み合わせで、同じ型のスピンフリップ（見本で設定を変えていない）も同じ値
        new(CharFadeInFadeOutId, "文字単位フェード", CharFadeTypeName, CharFadeFields(wholeFadeOut: true, delayInlineGraphics: false))
        {
            WriteOrder = CharFadeWriteOrder,
        },
        // 以下 3 種類の既定値は見本の n3proj の値（ユーザーは設定を変えていない。スピンフリップは文字単位フェードと同じ型・項目）
        new(SpinFlipId, "スピンフリップ", CharFadeTypeName, CharFadeFields(wholeFadeOut: true, delayInlineGraphics: false))
        {
            WriteOrder = CharFadeWriteOrder,
        },
        new(UtopiaId, "ユートピア", "UtopiaSettingsModel", new[] { TailDelay(), FadeIn(), FadeOut(), DelayInlineGraphics(false) })
        {
            WriteOrder = new[] { "TailDelay", "DelayInlineGraphics", "FadeInTime", "FadeOutTime" },
        },
        new(SlideUpDownId, "上下スライド", "SlideUpDownSettingsModel", new[]
        {
            new N3SubtitleActionField("SlideAmount", "上下スライド量（プラスで下・マイナスで上）", N3SubtitleActionFieldKind.Pixels, -50),
            new N3SubtitleActionField("FadeIn", "フェードインする", N3SubtitleActionFieldKind.Bool, false,
                TrueText: "フェードイン", FalseText: "フェードインしない"),
            FadeIn(),
            new N3SubtitleActionField("FadeOut", "フェードアウトする", N3SubtitleActionFieldKind.Bool, false,
                TrueText: "フェードアウト", FalseText: "フェードアウトしない"),
            FadeOut(),
        })
        {
            WriteOrder = new[] { "SlideAmount", "FadeIn", "FadeOut", "FadeInTime", "FadeOutTime" },
        },
    };

    /// <summary>Id の種類（知らない Id・空なら null）。</summary>
    public static N3SubtitleActionKind? Find(string? id) =>
        string.IsNullOrEmpty(id) ? null : Known.FirstOrDefault(k => k.Id == id);

    /// <summary>知っている Id か。</summary>
    public static bool IsKnown(string? id) => Find(id) is not null;

    /// <summary>
    /// Id の既定のアクション（新しい書式: <c>$type</c> を最初に、項目、CreateAppVer、ModifyAppVer = ""）。
    /// <paramref name="addOnDefaults"/> はニコカラメーカー3 の AddOns\&lt;Id&gt;.json（<c>$type</c> なし、値だけ）で、あればその値を使う。
    /// 大きさ（px）の項目は <see cref="PixelsReference"/> の高さで書く（書き出しで <see cref="Normalize"/> が画面の高さに合わせる）。
    /// 知らない Id は <c>$type</c> を「アクションしない」と同じ AddOnSettingsModel にし、<paramref name="addOnDefaults"/> の値だけを持たせる。
    /// </summary>
    public static N3SubtitleAction CreateDefault(string id, JsonObject? addOnDefaults = null, string createAppVer = "")
    {
        var kind = Find(id);
        var s = new JsonObject { ["$type"] = kind?.TypeName ?? AddOnSettingsTypeName };
        if (kind is not null)
        {
            foreach (string key in kind.WriteOrder)
            {
                var field = kind.Field(key)!;
                s[key] = field.ValueFrom(addOnDefaults) ?? field.CreateDefaultNode();
            }
        }
        CopyExtras(addOnDefaults, s);
        s["CreateAppVer"] = createAppVer ?? "";
        s["ModifyAppVer"] = "";
        return new N3SubtitleAction(id ?? "", s);
    }

    /// <summary>
    /// 書き出す形にそろえた写し。知っている Id は、旧書式の画面用の項目（*Visibility・*TimeTag・DelayInlineGraphicsIsEnabled など）を落とし、
    /// <c>$type</c> を最初に、項目を決まった順に（無い項目は既定値で）、CreateAppVer（無ければ <paramref name="ver"/>）・ModifyAppVer を最後に置く。
    /// <c>$type</c> が同じなら、知らない項目（新しい版のニコカラメーカー3 が足したものなど）も残す。
    /// <paramref name="screenHeight"/> が正なら、大きさ（px）の項目をその画面の高さの値に直す（高さに対する比は変えない。同じ高さならそのまま）。
    /// 知らない Id は中身をそのまま保つ（<c>$type</c> があれば最初へ動かすだけ）。元のアクションは変えない。
    /// </summary>
    public static N3SubtitleAction Normalize(N3SubtitleAction action, string ver, int screenHeight = 0)
    {
        var src = action.Settings;
        var kind = Find(action.Id);
        if (kind is null) return new N3SubtitleAction(action.Id, TypeFirst(src));

        var s = new JsonObject { ["$type"] = kind.TypeName };
        foreach (string key in kind.WriteOrder)
        {
            var field = kind.Field(key)!;
            var value = field.ValueFrom(src) ?? field.CreateDefaultNode();
            if (field.Kind == N3SubtitleActionFieldKind.Pixels && screenHeight > 0 && N3FontJson.Int((value as JsonObject)?["Reference"]) != screenHeight
                && PixelsAt(value, screenHeight) is double px)
            {
                value = PixelsNode(px, screenHeight);
            }
            s[key] = value;
        }
        if (Str(src["$type"]) == kind.TypeName) CopyExtras(src, s);
        s["CreateAppVer"] = Str(src["CreateAppVer"]) is { Length: > 0 } created ? created : ver ?? "";
        s["ModifyAppVer"] = Str(src["ModifyAppVer"]) ?? "";
        return new N3SubtitleAction(kind.Id, s);
    }

    /// <summary>表示名。知っている Id は表示名、空なら「（既定）」、知らない Id は「そのほか（Id の末尾）」。</summary>
    public static string DisplayName(string? id)
    {
        if (string.IsNullOrEmpty(id)) return "（既定）";
        if (Find(id) is { } kind) return kind.Name;
        int dot = id.LastIndexOf('.');
        string tail = dot >= 0 && dot < id.Length - 1 ? id[(dot + 1)..] : id;
        return $"そのほか（{tail}）";
    }

    /// <summary>
    /// 1 行の説明（一覧・ツールチップ用）。既定値のままなら表示名だけ（「文字単位フェード」）、時間を変えていれば画面に出す時間を並べ
    /// （「フェードイン/アウト（500/250ms）」）、大きさを変えていれば px（「上下スライド（-80px）」）、する・しないを変えていればその短い説明を足す
    /// （「文字単位フェード（表示終了基準・アイコンを遅らせない）」）。
    /// </summary>
    /// <param name="addOnSettings">
    /// ニコカラメーカー3 の設定（Id → AddOns の値。<see cref="Nkm3Environment.AddOnSettings"/>）。渡すと、その値（無い項目は既定値）と違う所だけを添える
    /// （ふだん使っている設定のままなら名前だけになる）。null なら既定値と比べる。
    /// </param>
    public static string Describe(N3SubtitleAction? action, IReadOnlyDictionary<string, JsonObject>? addOnSettings = null)
    {
        if (action is null) return DisplayName(null);
        var kind = Find(action.Id);
        if (kind is null) return DisplayName(action.Id);

        var n = Normalize(action, "");
        var baseline = CreateDefault(kind.Id, addOnSettings?.GetValueOrDefault(kind.Id));
        var parts = new List<string>();
        var times = kind.VisibleFields.Where(f => f.Kind == N3SubtitleActionFieldKind.Milliseconds).ToList();
        if (times.Any(f => n.GetInt(f.Key) != baseline.GetInt(f.Key)))
        {
            parts.Add(string.Join("/", times.Select(f => n.GetInt(f.Key))) + "ms");
        }
        foreach (var f in kind.VisibleFields)
        {
            if (f.Kind == N3SubtitleActionFieldKind.Pixels)
            {
                var node = n.Settings[f.Key];
                double defaultPx = PixelsAt(baseline.Settings[f.Key], PixelsReference) ?? Convert.ToDouble(f.Default, CultureInfo.InvariantCulture);
                if (PixelsAt(node, PixelsReference) is double atReference && Math.Abs(atReference - defaultPx) >= 0.5)
                {
                    double shown = N3FontJson.Number((node as JsonObject)?["Size"]) ?? atReference;
                    parts.Add(shown.ToString("0.#", CultureInfo.InvariantCulture) + "px");
                }
            }
            else if (f.Kind == N3SubtitleActionFieldKind.Bool && n.GetBool(f.Key) is bool value && value != (baseline.GetBool(f.Key) ?? Convert.ToBoolean(f.Default, CultureInfo.InvariantCulture)))
            {
                string text = value ? f.TrueText : f.FalseText;
                if (text.Length > 0) parts.Add(text);
            }
        }
        return parts.Count == 0 ? kind.Name : $"{kind.Name}（{string.Join("・", parts)}）";
    }

    /// <summary>
    /// いちばん多いアクション（<see cref="N3SubtitleAction.SameAs"/> で同じものをまとめて数える。同数なら最初に出たもの）の写し。
    /// null・Id が空のものは数えない。1 つも無ければ null。
    /// </summary>
    public static N3SubtitleAction? MostCommon(IEnumerable<N3SubtitleAction?> actions)
    {
        var counts = new Dictionary<string, (int Count, int First, N3SubtitleAction Action)>(StringComparer.Ordinal);
        int order = 0;
        foreach (var a in actions)
        {
            if (a is null || a.Id.Length == 0) continue;
            string key = CompareKey(a);
            counts[key] = counts.TryGetValue(key, out var c) ? (c.Count + 1, c.First, c.Action) : (1, order, a);
            order++;
        }
        if (counts.Count == 0) return null;
        var best = counts.Values.OrderByDescending(c => c.Count).ThenBy(c => c.First).First();
        return best.Action.Clone();
    }

    /// <summary>旧書式の画面用の項目か（*Visibility・*TimeTag・DelayInlineGraphicsIsEnabled など。書き出しでは落とす）。</summary>
    public static bool IsScreenItem(string key) =>
        key.EndsWith("Visibility", StringComparison.Ordinal) ||
        key.EndsWith("TimeTag", StringComparison.Ordinal) ||
        key.EndsWith("IsEnabled", StringComparison.Ordinal);

    /// <summary>大きさ（px）の値 {Size, Reference, Ratio}（Reference の画面の高さでの px と、高さに対する比。マイナスもそのまま）。</summary>
    public static JsonObject PixelsNode(double px, int reference) => new()
    {
        ["Size"] = (int)Math.Round(px, MidpointRounding.AwayFromZero),
        ["Reference"] = reference,
        ["Ratio"] = reference > 0 ? px / reference : 0.0,
    };

    /// <summary>
    /// 大きさ（px）の値を画面の高さ <paramref name="screenHeight"/> での px にする（比があればそれを、無ければ Size と Reference を使う。
    /// 数だけなら <see cref="PixelsReference"/> の高さでの px とみなす）。読めなければ null。
    /// </summary>
    public static double? PixelsAt(JsonNode? node, int screenHeight)
    {
        if (node is JsonObject o)
        {
            double? size = N3FontJson.Number(o["Size"]);
            double? ratio = N3FontJson.Number(o["Ratio"]);
            if (size is null && ratio is null) return null;
            if (ratio is double r && r != 0) return r * screenHeight;
            double reference = N3FontJson.Number(o["Reference"]) ?? 0;
            return reference > 0 ? (size ?? 0) * screenHeight / reference : size ?? 0;
        }
        return N3FontJson.Number(node) is double px ? px * screenHeight / PixelsReference : null;
    }

    /// <summary>比べるための文字列（Id と、そろえた設定値の版数・画面用の項目を除いた中身。項目の順は問わない）。</summary>
    internal static string CompareKey(N3SubtitleAction action)
    {
        var sb = new StringBuilder(action.Id).Append('\n');
        AppendCanonical(sb, Normalize(action, "").Settings, top: true);
        return sb.ToString();
    }

    private static void AppendCanonical(StringBuilder sb, JsonNode? node, bool top)
    {
        switch (node)
        {
            case JsonObject o:
                sb.Append('{');
                foreach (var (key, value) in o.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    if (top && (key is "CreateAppVer" or "ModifyAppVer" || IsScreenItem(key))) continue;
                    sb.Append('"').Append(key).Append("\":");
                    AppendCanonical(sb, value, top: false);
                    sb.Append(',');
                }
                sb.Append('}');
                break;
            case JsonArray a:
                sb.Append('[');
                foreach (var item in a)
                {
                    AppendCanonical(sb, item, top: false);
                    sb.Append(',');
                }
                sb.Append(']');
                break;
            case null:
                sb.Append("null");
                break;
            default:
                sb.Append(node.ToJsonString());
                break;
        }
    }

    /// <summary>source の項目のうち、dest にまだ無く、<c>$type</c>・版数・画面用の項目でないものを dest へ写す。</summary>
    private static void CopyExtras(JsonObject? source, JsonObject dest)
    {
        if (source is null) return;
        foreach (var (key, value) in source)
        {
            if (key is "$type" or "CreateAppVer" or "ModifyAppVer" || IsScreenItem(key) || dest.ContainsKey(key)) continue;
            dest[key] = value?.DeepClone();
        }
    }

    /// <summary>中身をそのまま写し、<c>$type</c> があれば最初に置く（ニコカラメーカー3 は型判別子が最初の項目でないと読めない）。</summary>
    private static JsonObject TypeFirst(JsonObject source)
    {
        var o = new JsonObject();
        if (source.TryGetPropertyValue("$type", out var type)) o["$type"] = type?.DeepClone();
        foreach (var (key, value) in source)
        {
            if (key == "$type") continue;
            o[key] = value?.DeepClone();
        }
        return o;
    }

    private static string? Str(JsonNode? node) => N3FontJson.Str(node);
}
