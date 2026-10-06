using System.Text.Json;
using System.Text.Json.Nodes;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;
using NicoKaraPrep.Core.Project;

namespace NicoKaraPrep.Core.Tests;

/// <summary>ニコカラメーカー3 の字幕アクション（種類の一覧・既定値・書式のそろえ方・比べ方・保存）。</summary>
public class N3SubtitleActionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "NicoKaraPrepTests", Guid.NewGuid().ToString("N"));

    public N3SubtitleActionTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 後始末の失敗は無視 */ }
    }

    private static JsonObject Obj(string json) => JsonNode.Parse(json)!.AsObject();

    private static N3SubtitleAction Act(string id, string json) => new(id, Obj(json));

    /// <summary>旧書式（Ver 9〜10 で保存。画面用の項目入り）の文字単位フェード（実データ What is my LIFE.n3proj の写し）。</summary>
    private const string OldCharFade =
        """{"$type":"CharFadeInFadeOutSettingsModel","IntroDelay":350,"IntroDelayTimeTag":"[00:00:35]","WholeFadeOut":false,"DelayInlineGraphicsIsEnabled":true,"TailDelayVisibility":0,"TailDelay":250,"TailDelayTimeTag":"[00:00:25]","DelayInlineGraphics":true,"FadeInTimeVisibility":0,"FadeInTime":250,"FadeInTimeTag":"[00:00:25]","FadeOutTimeVisibility":0,"FadeOutTime":250,"FadeOutTimeTag":"[00:00:25]"}""";

    /// <summary>新しい書式の文字単位フェード（実データ Darling Wanted.n3proj の写し）。</summary>
    private const string NewCharFade =
        """{"$type":"CharFadeInFadeOutSettingsModel","IntroDelay":350,"WholeFadeOut":false,"TailDelay":250,"DelayInlineGraphics":true,"FadeInTime":250,"FadeOutTime":250,"CreateAppVer":"Ver 11.15","ModifyAppVer":""}""";

    // ユーザーが作った見本の n3proj（REGAIN AGAIN LLLLOVE_test.n3proj、ニコカラメーカー3 Ver 13.90 で保存）の残り 3 種類の写し
    private const string SampleSpinFlip =
        """{"$type":"CharFadeInFadeOutSettingsModel","IntroDelay":350,"WholeFadeOut":true,"TailDelay":250,"DelayInlineGraphics":false,"FadeInTime":250,"FadeOutTime":250,"CreateAppVer":"Ver 13.90","ModifyAppVer":""}""";

    private const string SampleUtopia =
        """{"$type":"UtopiaSettingsModel","TailDelay":250,"DelayInlineGraphics":false,"FadeInTime":250,"FadeOutTime":250,"CreateAppVer":"Ver 13.90","ModifyAppVer":""}""";

    private const string SampleSlideUpDown =
        """{"$type":"SlideUpDownSettingsModel","SlideAmount":{"Size":-50,"Reference":1080,"Ratio":-0.046296296296296294},"FadeIn":false,"FadeOut":false,"FadeInTime":250,"FadeOutTime":250,"CreateAppVer":"Ver 13.90","ModifyAppVer":""}""";

    // ------------------------------------------------------------ 種類の一覧

    [Fact]
    public void カタログ_ニコカラメーカー3の8種類の名前とIdと型()
    {
        Assert.Equal(
            new[] { "アクションしない", "フェードイン", "フェードアウト", "フェードイン/アウト", "文字単位フェード", "スピンフリップ", "ユートピア", "上下スライド" },
            N3SubtitleActionCatalog.Known.Select(k => k.Name));
        Assert.Equal(
            new[] { "SHINTA.NoAction", "SHINTA.LineFadeIn", "SHINTA.LineFadeOut", "SHINTA.LineFadeInFadeOut", "SHINTA.CharFadeInFadeOut", "SHINTA.SpinFlip", "SHINTA.Utopia", "SHINTA.SlideUpDown" },
            N3SubtitleActionCatalog.Known.Select(k => k.Id));
        Assert.Equal(
            new[] { "AddOnSettingsModel", "SubtitleActionSettingsModel", "SubtitleActionSettingsModel", "SubtitleActionSettingsModel", "CharFadeInFadeOutSettingsModel", "CharFadeInFadeOutSettingsModel", "UtopiaSettingsModel", "SlideUpDownSettingsModel" },
            N3SubtitleActionCatalog.Known.Select(k => k.TypeName));

        Assert.True(N3SubtitleActionCatalog.IsKnown("SHINTA.LineFadeOut"));
        Assert.True(N3SubtitleActionCatalog.IsKnown("SHINTA.Utopia"));
        Assert.False(N3SubtitleActionCatalog.IsKnown("SHINTA.Future"));
        Assert.False(N3SubtitleActionCatalog.IsKnown(""));
        Assert.False(N3SubtitleActionCatalog.IsKnown(null));

        // 画面に出す項目（フェードインはフェードアウト時間を書くが出さない）
        static string[] Visible(string id) => N3SubtitleActionCatalog.Find(id)!.VisibleFields.Select(f => f.Key).ToArray();
        Assert.Empty(Visible("SHINTA.NoAction"));
        Assert.Equal(new[] { "FadeInTime" }, Visible("SHINTA.LineFadeIn"));
        Assert.Equal(new[] { "FadeOutTime" }, Visible("SHINTA.LineFadeOut"));
        Assert.Equal(new[] { "FadeInTime", "FadeOutTime" }, Visible("SHINTA.LineFadeInFadeOut"));
        Assert.Equal(new[] { "IntroDelay", "TailDelay", "FadeInTime", "FadeOutTime", "WholeFadeOut", "DelayInlineGraphics" }, Visible("SHINTA.CharFadeInFadeOut"));
        Assert.Equal(Visible("SHINTA.CharFadeInFadeOut"), Visible("SHINTA.SpinFlip"));
        Assert.Equal(new[] { "TailDelay", "FadeInTime", "FadeOutTime", "DelayInlineGraphics" }, Visible("SHINTA.Utopia"));
        Assert.Equal(new[] { "SlideAmount", "FadeIn", "FadeInTime", "FadeOut", "FadeOutTime" }, Visible("SHINTA.SlideUpDown"));
        Assert.Equal(N3SubtitleActionFieldKind.Pixels, N3SubtitleActionCatalog.Find("SHINTA.SlideUpDown")!.Field("SlideAmount")!.Kind);
        var whole = N3SubtitleActionCatalog.Find("SHINTA.CharFadeInFadeOut")!.Field("WholeFadeOut")!;
        Assert.Equal(N3SubtitleActionFieldKind.Bool, whole.Kind);
        Assert.False((bool)whole.Default);
        Assert.Equal("表示終了時刻を基準にフェードアウト", whole.Label);
    }

    // ------------------------------------------------------------ 既定値

    [Fact]
    public void 既定値_新しい書式で型判別子が最初()
    {
        var a = N3SubtitleActionCatalog.CreateDefault("SHINTA.CharFadeInFadeOut", createAppVer: "Ver 13.79");
        Assert.Equal("SHINTA.CharFadeInFadeOut", a.Id);
        // ニコカラメーカー3 が保存するのと同じ並び（前の書き出しと同じ JSON）
        Assert.Equal(
            """{"$type":"CharFadeInFadeOutSettingsModel","IntroDelay":350,"WholeFadeOut":false,"TailDelay":250,"DelayInlineGraphics":true,"FadeInTime":250,"FadeOutTime":250,"CreateAppVer":"Ver 13.79","ModifyAppVer":""}""",
            a.Settings.ToJsonString());

        Assert.Equal("""{"$type":"AddOnSettingsModel","CreateAppVer":"","ModifyAppVer":""}""",
            N3SubtitleActionCatalog.CreateDefault("SHINTA.NoAction").Settings.ToJsonString());
        Assert.Equal("""{"$type":"SubtitleActionSettingsModel","FadeInTime":250,"FadeOutTime":250,"CreateAppVer":"","ModifyAppVer":""}""",
            N3SubtitleActionCatalog.CreateDefault("SHINTA.LineFadeOut").Settings.ToJsonString());
    }

    [Fact]
    public void 既定値_ニコカラメーカー3のアドオンの設定の値を使う()
    {
        // AddOns\SHINTA.CharFadeInFadeOut.json の形（$type なし、値と版数だけ）
        var addOn = Obj("""{"IntroDelay":400,"WholeFadeOut":true,"TailDelay":300,"DelayInlineGraphics":false,"FadeInTime":200,"FadeOutTime":100,"CreateAppVer":"Ver 11.15","ModifyAppVer":""}""");
        var a = N3SubtitleActionCatalog.CreateDefault("SHINTA.CharFadeInFadeOut", addOn, "Ver 13.90");
        Assert.Equal(
            """{"$type":"CharFadeInFadeOutSettingsModel","IntroDelay":400,"WholeFadeOut":true,"TailDelay":300,"DelayInlineGraphics":false,"FadeInTime":200,"FadeOutTime":100,"CreateAppVer":"Ver 13.90","ModifyAppVer":""}""",
            a.Settings.ToJsonString());

        // 項目が欠けていれば既定値、型の合わない値も既定値
        var partial = N3SubtitleActionCatalog.CreateDefault("SHINTA.LineFadeIn", Obj("""{"FadeInTime":"速く"}"""));
        Assert.Equal(250, partial.GetInt("FadeInTime"));
        Assert.Equal(250, partial.GetInt("FadeOutTime"));
        Assert.Equal(400, addOn["IntroDelay"]!.GetValue<int>()); // 元のアドオンの設定は変えない
    }

    // ------------------------------------------------------------ 書式をそろえる

    [Fact]
    public void 正規化_旧書式の画面用の項目を落として新しい書式にする()
    {
        var old = Act("SHINTA.CharFadeInFadeOut", OldCharFade);
        var n = N3SubtitleActionCatalog.Normalize(old, "Ver 13.79");
        Assert.Equal(
            """{"$type":"CharFadeInFadeOutSettingsModel","IntroDelay":350,"WholeFadeOut":false,"TailDelay":250,"DelayInlineGraphics":true,"FadeInTime":250,"FadeOutTime":250,"CreateAppVer":"Ver 13.79","ModifyAppVer":""}""",
            n.Settings.ToJsonString());
        Assert.Equal(OldCharFade, old.Settings.ToJsonString()); // 元は変えない

        // 旧書式の行フェード（実データ Public Style.n3proj の写し）。フェードインは使わないフェードアウトを隠していた
        var fadeIn = Act("SHINTA.LineFadeIn",
            """{"$type":"SubtitleActionSettingsModel","FadeInTimeVisibility":0,"FadeInTime":250,"FadeInTimeTag":"[00:00:25]","FadeOutTimeVisibility":1,"FadeOutTime":250,"FadeOutTimeTag":"[00:00:25]"}""");
        Assert.Equal("""{"$type":"SubtitleActionSettingsModel","FadeInTime":250,"FadeOutTime":250,"CreateAppVer":"Ver 13.79","ModifyAppVer":""}""",
            N3SubtitleActionCatalog.Normalize(fadeIn, "Ver 13.79").Settings.ToJsonString());

        // 旧書式の「アクションしない」は行フェードの型だった（新しい書式は AddOnSettingsModel で項目なし）
        var none = Act("SHINTA.NoAction",
            """{"$type":"SubtitleActionSettingsModel","FadeInTimeVisibility":1,"FadeInTime":250,"FadeInTimeTag":"[00:00:25]","FadeOutTimeVisibility":1,"FadeOutTime":250,"FadeOutTimeTag":"[00:00:25]"}""");
        Assert.Equal("""{"$type":"AddOnSettingsModel","CreateAppVer":"Ver 13.79","ModifyAppVer":""}""",
            N3SubtitleActionCatalog.Normalize(none, "Ver 13.79").Settings.ToJsonString());
    }

    [Fact]
    public void 正規化_新しい書式の値と版数はそのまま_欠けた項目は既定値_新しい項目は残す()
    {
        var changed = Act("SHINTA.CharFadeInFadeOut",
            """{"$type":"CharFadeInFadeOutSettingsModel","DelayInlineGraphics":false,"IntroDelay":350,"TailDelay":250,"FadeInTime":250,"CreateAppVer":"Ver 11.15","ModifyAppVer":"","NewOption":3}""");
        var n = N3SubtitleActionCatalog.Normalize(changed, "Ver 13.79");
        Assert.Equal(
            """{"$type":"CharFadeInFadeOutSettingsModel","IntroDelay":350,"WholeFadeOut":false,"TailDelay":250,"DelayInlineGraphics":false,"FadeInTime":250,"FadeOutTime":250,"NewOption":3,"CreateAppVer":"Ver 11.15","ModifyAppVer":""}""",
            n.Settings.ToJsonString());

        // 型判別子が違う（旧書式の「アクションしない」など）ときは、知らない項目を持ち込まない
        var mismatched = Act("SHINTA.LineFadeInFadeOut", """{"$type":"CharFadeInFadeOutSettingsModel","IntroDelay":350,"FadeInTime":500}""");
        Assert.Equal("""{"$type":"SubtitleActionSettingsModel","FadeInTime":500,"FadeOutTime":250,"CreateAppVer":"v","ModifyAppVer":""}""",
            N3SubtitleActionCatalog.Normalize(mismatched, "v").Settings.ToJsonString());
    }

    [Fact]
    public void 残り3種類_既定値は見本のn3projと同じ形と値()
    {
        // 既定値はニコカラメーカー3 に確かめ中のため、見本の値をそのまま既定にしている
        foreach (var (id, json) in new[] { ("SHINTA.SpinFlip", SampleSpinFlip), ("SHINTA.Utopia", SampleUtopia), ("SHINTA.SlideUpDown", SampleSlideUpDown) })
        {
            Assert.Equal(json, N3SubtitleActionCatalog.CreateDefault(id, createAppVer: "Ver 13.90").Settings.ToJsonString());
            // 見本をそろえても変わらない（1080 の画面でも）
            Assert.Equal(json, N3SubtitleActionCatalog.Normalize(Act(id, json), "Ver 13.79").Settings.ToJsonString());
            Assert.Equal(json, N3SubtitleActionCatalog.Normalize(Act(id, json), "Ver 13.79", screenHeight: 1080).Settings.ToJsonString());
        }
        Assert.True(Act("SHINTA.SpinFlip", SampleSpinFlip).SameAs(N3SubtitleActionCatalog.CreateDefault("SHINTA.SpinFlip")));
        Assert.False(Act("SHINTA.SpinFlip", SampleSpinFlip).SameAs(Act("SHINTA.CharFadeInFadeOut", SampleSpinFlip))); // 同じ型でも Id が違う
    }

    [Fact]
    public void 上下スライド量_画面の高さに合わせて書き_高さに対する比は変えない()
    {
        var slide = Act("SHINTA.SlideUpDown", SampleSlideUpDown);
        Assert.Equal(-50, slide.GetPixels("SlideAmount", 1080)!.Value, 6);
        Assert.Equal(-100, slide.GetPixels("SlideAmount", 2160)!.Value, 6);

        // 720 の画面のプロジェクトへ書くと、同じ比の 720 での px
        var n = N3SubtitleActionCatalog.Normalize(slide, "Ver 13.79", screenHeight: 720);
        var amount = n.Settings["SlideAmount"]!.AsObject();
        Assert.Equal(-33, amount["Size"]!.GetValue<int>());
        Assert.Equal(720, amount["Reference"]!.GetValue<int>());
        Assert.Equal(-50.0 / 1080, amount["Ratio"]!.GetValue<double>(), 12);

        // 画面の高さで px を書く（マイナスもそのまま）
        slide.SetPixels("SlideAmount", 30, 720);
        Assert.Equal("""{"Size":30,"Reference":720,"Ratio":0.041666666666666664}""", slide.Settings["SlideAmount"]!.ToJsonString());
        Assert.Equal(45, slide.GetPixels("SlideAmount", 1080)!.Value, 6);

        // 数だけの値（手で直した AddOns の設定など）は 1080 の画面での px とみなす
        var fromNumber = N3SubtitleActionCatalog.CreateDefault("SHINTA.SlideUpDown", Obj("""{"SlideAmount":-80}"""));
        Assert.Equal("""{"Size":-80,"Reference":1080,"Ratio":-0.07407407407407407}""", fromNumber.Settings["SlideAmount"]!.ToJsonString());
    }

    [Fact]
    public void 未知のId_中身をそのまま保ち型判別子だけ最初へ()
    {
        string future = """{"$type":"FutureSettingsModel","Amount":{"Size":-50,"Reference":1080,"Ratio":-0.046296296296296294},"FadeIn":false,"CreateAppVer":"Ver 14.00","ModifyAppVer":""}""";
        var n = N3SubtitleActionCatalog.Normalize(Act("SHINTA.Future", future), "Ver 13.79", screenHeight: 720);
        Assert.Equal("SHINTA.Future", n.Id);
        Assert.Equal(future, n.Settings.ToJsonString());

        // 型判別子が途中にあれば最初へ（ほかの並び・値・版数の無いことはそのまま）
        var odd = N3SubtitleActionCatalog.Normalize(Act("X.Some", """{"Amount":1,"$type":"SomeSettingsModel","Flag":true}"""), "Ver 13.79");
        Assert.Equal("""{"$type":"SomeSettingsModel","Amount":1,"Flag":true}""", odd.Settings.ToJsonString());
    }

    // ------------------------------------------------------------ 比べる

    [Fact]
    public void 同じアクションか_版数と画面用の項目は無視して値で比べる()
    {
        var oldFormat = Act("SHINTA.CharFadeInFadeOut", OldCharFade);
        var newFormat = Act("SHINTA.CharFadeInFadeOut", NewCharFade);
        Assert.True(oldFormat.SameAs(newFormat));
        Assert.True(newFormat.SameAs(N3SubtitleActionCatalog.CreateDefault("SHINTA.CharFadeInFadeOut", createAppVer: "Ver 13.90")));

        var noDelay = newFormat.Clone();
        noDelay.Set("DelayInlineGraphics", false);
        Assert.False(newFormat.SameAs(noDelay));
        Assert.False(newFormat.SameAs(Act("SHINTA.SpinFlip", NewCharFade))); // Id が違う
        Assert.False(newFormat.SameAs(null));

        // 旧書式と新しい書式の「アクションしない」
        Assert.True(Act("SHINTA.NoAction", """{"$type":"SubtitleActionSettingsModel","FadeInTime":250,"FadeOutTime":250,"CreateAppVer":"Ver 13.43","ModifyAppVer":""}""")
            .SameAs(Act("SHINTA.NoAction", """{"$type":"AddOnSettingsModel","CreateAppVer":"Ver 12.50","ModifyAppVer":""}""")));

        // 知らない Id: 版数・画面用の項目・並びは無視し、値は比べる
        var u1 = Act("SHINTA.Future", """{"$type":"FutureSettingsModel","TailDelay":250,"DelayInlineGraphics":false,"CreateAppVer":"Ver 14.00","ModifyAppVer":""}""");
        var u2 = Act("SHINTA.Future", """{"DelayInlineGraphics":false,"$type":"FutureSettingsModel","TailDelay":250,"TailDelayVisibility":0}""");
        var u3 = Act("SHINTA.Future", """{"$type":"FutureSettingsModel","TailDelay":300,"DelayInlineGraphics":false}""");
        Assert.True(u1.SameAs(u2));
        Assert.False(u1.SameAs(u3));

        Assert.True(N3SubtitleAction.AreSame(null, null));
        Assert.False(N3SubtitleAction.AreSame(null, u1));
    }

    [Fact]
    public void 写しは設定値も別のオブジェクト()
    {
        var a = Act("SHINTA.LineFadeIn", """{"$type":"SubtitleActionSettingsModel","FadeInTime":250,"FadeOutTime":250}""");
        var b = a.Clone();
        b.Set("FadeInTime", 500);
        Assert.Equal(250, a.GetInt("FadeInTime"));
        Assert.Equal(500, b.GetInt("FadeInTime"));
        // 書く値は同じ位置で置き換える（型判別子は最初のまま）
        Assert.Equal("""{"$type":"SubtitleActionSettingsModel","FadeInTime":500,"FadeOutTime":250}""", b.Settings.ToJsonString());
        Assert.Null(b.GetBool("FadeInTime"));
        Assert.Null(b.GetInt("無い項目"));
    }

    [Fact]
    public void いちばん多いアクション_同じものをまとめて数え同数なら最初に出たもの()
    {
        var fade = Act("SHINTA.LineFadeInFadeOut", """{"$type":"SubtitleActionSettingsModel","FadeInTime":250,"FadeOutTime":250}""");
        var charOld = Act("SHINTA.CharFadeInFadeOut", OldCharFade);
        var charNew = Act("SHINTA.CharFadeInFadeOut", NewCharFade);

        // 旧書式と新しい書式の文字単位フェードは同じものとして数える（2 対 1）
        var most = N3SubtitleActionCatalog.MostCommon(new[] { fade, null, charOld, charNew });
        Assert.Equal("SHINTA.CharFadeInFadeOut", most!.Id);
        Assert.NotSame(charOld, most); // 写しを返す
        Assert.Equal(OldCharFade, most.Settings.ToJsonString()); // 最初に出たものの写し（書式はそろえない）

        // 同数なら最初に出たもの
        Assert.Equal("SHINTA.LineFadeInFadeOut", N3SubtitleActionCatalog.MostCommon(new[] { fade, charNew })!.Id);
        Assert.Null(N3SubtitleActionCatalog.MostCommon(new N3SubtitleAction?[] { null, new N3SubtitleAction("", null) }));
    }

    // ------------------------------------------------------------ 表示

    [Fact]
    public void 表示名と説明()
    {
        Assert.Equal("（既定）", N3SubtitleActionCatalog.DisplayName(null));
        Assert.Equal("（既定）", N3SubtitleActionCatalog.DisplayName(""));
        Assert.Equal("フェードイン/アウト", N3SubtitleActionCatalog.DisplayName("SHINTA.LineFadeInFadeOut"));
        Assert.Equal("ユートピア", N3SubtitleActionCatalog.DisplayName("SHINTA.Utopia"));
        Assert.Equal("そのほか（Future）", N3SubtitleActionCatalog.DisplayName("SHINTA.Future"));
        Assert.Equal("そのほか（Plain）", N3SubtitleActionCatalog.DisplayName("Plain"));

        // 既定値のままなら名前だけ
        Assert.Equal("文字単位フェード", N3SubtitleActionCatalog.Describe(Act("SHINTA.CharFadeInFadeOut", OldCharFade)));
        Assert.Equal("アクションしない", N3SubtitleActionCatalog.Describe(N3SubtitleActionCatalog.CreateDefault("SHINTA.NoAction")));
        Assert.Equal("（既定）", N3SubtitleActionCatalog.Describe(null));
        Assert.Equal("そのほか（Future）", N3SubtitleActionCatalog.Describe(Act("SHINTA.Future", """{"$type":"FutureSettingsModel","Amount":3}""")));
        Assert.Equal("スピンフリップ", N3SubtitleActionCatalog.Describe(Act("SHINTA.SpinFlip", SampleSpinFlip)));
        Assert.Equal("上下スライド", N3SubtitleActionCatalog.Describe(Act("SHINTA.SlideUpDown", SampleSlideUpDown)));

        // 時間を変えたら画面に出す時間を並べる（出さない項目は数えない）
        var fade = N3SubtitleActionCatalog.CreateDefault("SHINTA.LineFadeInFadeOut");
        fade.Set("FadeInTime", 500);
        Assert.Equal("フェードイン/アウト（500/250ms）", N3SubtitleActionCatalog.Describe(fade));
        var fadeIn = N3SubtitleActionCatalog.CreateDefault("SHINTA.LineFadeIn");
        fadeIn.Set("FadeOutTime", 900);
        Assert.Equal("フェードイン", N3SubtitleActionCatalog.Describe(fadeIn));

        // する・しないを変えたら短い説明（実データ Proof.n3proj の形）
        var proof = N3SubtitleActionCatalog.CreateDefault("SHINTA.CharFadeInFadeOut");
        proof.Set("WholeFadeOut", true);
        proof.Set("DelayInlineGraphics", false);
        Assert.Equal("文字単位フェード（表示終了基準・アイコンを遅らせない）", N3SubtitleActionCatalog.Describe(proof));
        proof.Set("TailDelay", 400);
        Assert.Equal("文字単位フェード（350/400/250/250ms・表示終了基準・アイコンを遅らせない）", N3SubtitleActionCatalog.Describe(proof));

        // 既定値が逆のスピンフリップは、逆向きの説明
        var spin = N3SubtitleActionCatalog.CreateDefault("SHINTA.SpinFlip");
        spin.Set("WholeFadeOut", false);
        spin.Set("DelayInlineGraphics", true);
        Assert.Equal("スピンフリップ（表示終了基準にしない・アイコンを遅らせる）", N3SubtitleActionCatalog.Describe(spin));

        // 上下スライドは px と、フェードの する・しない
        var slide = N3SubtitleActionCatalog.CreateDefault("SHINTA.SlideUpDown");
        slide.SetPixels("SlideAmount", 80, 1080);
        slide.Set("FadeIn", true);
        Assert.Equal("上下スライド（80px・フェードイン）", N3SubtitleActionCatalog.Describe(slide));
    }

    // ------------------------------------------------------------ 保存（.tttproj）

    private static LyricsDocument Doc(params string[] lines)
    {
        var doc = new LyricsDocument();
        foreach (string l in lines) doc.Lines.Add(LrcFormat.ParseLyricLine(l));
        return doc;
    }

    [Fact]
    public void 行のアクションを保存して読み戻せる()
    {
        var doc = Doc("[00:01:00]あ[00:02:00]", "", "[00:03:00]い[00:04:00]", "[00:05:00]う[00:06:00]");
        doc.Lines[0].SubtitleAction = Act("SHINTA.CharFadeInFadeOut", OldCharFade);
        doc.Lines[3].SubtitleAction = Act("SHINTA.Utopia", """{"$type":"UtopiaSettingsModel","TailDelay":250,"DelayInlineGraphics":false}""");
        Assert.True(doc.Lines[3].HasN3Overrides); // アクションだけの行も保存する・解除できる
        Assert.True(doc.Lines[3].HasManualN3Overrides); // 行リストの ✎

        string song = Path.Combine(_dir, "song.lrc");
        var project = new SongProject { LineSettings = LineExportSettings.Collect(doc) };
        project.N3Proj.SubtitleAction = N3SubtitleActionCatalog.CreateDefault("SHINTA.LineFadeInFadeOut", createAppVer: "Ver 13.90");
        project.Save(song);

        // System.Text.Json で JsonObject をそのまま書く（$type も普通の項目として残る）
        string text = File.ReadAllText(SongProject.PathFor(song));
        Assert.Contains("\"$type\": \"UtopiaSettingsModel\"", text);

        var back = SongProject.TryLoad(song)!;
        Assert.Equal(2, back.LineSettings.Count);
        Assert.Equal("SHINTA.LineFadeInFadeOut", back.N3Proj.SubtitleAction!.Id);
        Assert.Equal("$type", back.N3Proj.SubtitleAction.Settings.First().Key);
        Assert.Equal("Ver 13.90", N3FontJson.Str(back.N3Proj.SubtitleAction.Settings["CreateAppVer"]));

        var reopened = Doc("[00:01:00]あ[00:02:00]", "", "[00:03:00]い[00:04:00]", "[00:05:00]う[00:06:00]");
        LineExportSettings.Apply(reopened, back.LineSettings);
        Assert.Equal(OldCharFade, reopened.Lines[0].SubtitleAction!.Settings.ToJsonString()); // 中身・並びはそのまま
        Assert.Null(reopened.Lines[2].SubtitleAction);
        Assert.True(doc.Lines[3].SubtitleAction!.SameAs(reopened.Lines[3].SubtitleAction));
        Assert.Equal("$type", reopened.Lines[3].SubtitleAction!.Settings.First().Key);

        // 曲の既定が無い（自動）なら書かずに null のまま読む
        var auto = new SongProject();
        auto.Save(song);
        Assert.Null(SongProject.TryLoad(song)!.N3Proj.SubtitleAction);
    }

    [Fact]
    public void 行のアクション_行を足し引きしても保存したときの文字の行へ当て直す()
    {
        var doc = Doc("[00:01:00]あ[00:02:00]", "[00:03:00]い[00:04:00]");
        doc.Lines[1].SubtitleAction = N3SubtitleActionCatalog.CreateDefault("SHINTA.NoAction");
        var saved = JsonSerializer.Deserialize<List<LineExportSettings>>(JsonSerializer.Serialize(LineExportSettings.Collect(doc)))!;

        // 先頭に 1 行足した文書へ（番号がずれても同じ文字の行へ）
        var shifted = Doc("", "[00:01:00]あ[00:02:00]", "[00:03:00]い[00:04:00]");
        LineExportSettings.Apply(shifted, saved);
        Assert.Null(shifted.Lines[1].SubtitleAction);
        Assert.Equal("SHINTA.NoAction", shifted.Lines[2].SubtitleAction!.Id);

        // 設定値の無い保存（手で直したファイルなど）は Id の既定値で読む
        LineExportSettings.Apply(doc, JsonSerializer.Deserialize<List<LineExportSettings>>("""[{"Index":0,"Text":"あ","SubtitleActionId":"SHINTA.LineFadeOut"}]"""));
        Assert.Equal(250, doc.Lines[0].SubtitleAction!.GetInt("FadeOutTime"));
        Assert.Equal("SubtitleActionSettingsModel", N3FontJson.Str(doc.Lines[0].SubtitleAction!.Settings["$type"]));
    }

    // ------------------------------------------------------------ ニコカラメーカー3 の設定

    private void WriteAddOn(string name, string json)
    {
        Directory.CreateDirectory(Path.Combine(_dir, "AddOns"));
        File.WriteAllText(Path.Combine(_dir, "AddOns", name + ".json"), json);
    }

    [Fact]
    public void 環境_すべて同じ字幕アクションのIdとアドオンの設定を読む()
    {
        WriteAddOn("SHINTA.UnificationSubtitleActionSelector", """{"SubtitleActionId":"SHINTA.LineFadeInFadeOut","CreateAppVer":"Ver 12.00","ModifyAppVer":""}""");
        WriteAddOn("SHINTA.CharFadeInFadeOut", """{"IntroDelay":350,"WholeFadeOut":false,"TailDelay":250,"DelayInlineGraphics":true,"FadeInTime":250,"FadeOutTime":250,"CreateAppVer":"Ver 11.15","ModifyAppVer":""}""");
        WriteAddOn("SHINTA.LineFadeInFadeOut", """{"FadeInTime":300,"FadeOutTime":400}""");

        var env = Nkm3Environment.Load(_dir);
        Assert.Equal("SHINTA.LineFadeInFadeOut", env.DefaultSubtitleActionId);
        Assert.Equal(new[] { "SHINTA.CharFadeInFadeOut", "SHINTA.LineFadeInFadeOut" }, env.AddOnSettings.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Same(env.AddOnSettings["SHINTA.CharFadeInFadeOut"], env.CharFadeSettings); // 前からの項目も同じもの
        Assert.Equal(Path.Combine(_dir, "TemplateLayout"), env.TemplateLayoutFolder);
        Assert.All(Nkm3Environment.FindTemplateLayoutFolders(), d => Assert.True(Directory.Exists(d)));
    }

    [Fact]
    public void 環境_設定が無い_壊れていても読める()
    {
        var empty = Nkm3Environment.Load(_dir);
        Assert.Null(empty.DefaultSubtitleActionId);
        Assert.Empty(empty.AddOnSettings);
        Assert.Null(empty.CharFadeSettings);

        WriteAddOn("SHINTA.UnificationSubtitleActionSelector", """{"SubtitleActionId":3}""");
        WriteAddOn("SHINTA.LineFadeIn", "{壊れた");
        var broken = Nkm3Environment.Load(_dir);
        Assert.Null(broken.DefaultSubtitleActionId);
        Assert.Empty(broken.AddOnSettings);
    }
}
