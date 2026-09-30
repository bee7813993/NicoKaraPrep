using NicoKaraPrep.Core.Model;
using NicoKaraPrep.Core.Project;

namespace NicoKaraPrep.Core.Tests;

public class N3ColorPatternTests
{
    private static N3ColorPattern Chara => N3ColorPatterns.BuiltIns.Single(p => p.Id == N3ColorPatterns.CharaInverseId);

    private static N3ColorPattern NoWipe => N3ColorPatterns.BuiltIns.Single(p => p.Id == N3ColorPatterns.NoWipeId);

    private static N3Brush Solid(string color, int alpha = 100) => new() { Type = N3Brush.TypeSolid, Color = color, AlphaPercent = alpha };

    private static N3Brush Mille(params (double Position, string Color)[] stops) => new()
    {
        Type = N3Brush.TypeMilleFeuille,
        Stops = stops.Select(s => new N3GradientStop { Position = s.Position, Color = s.Color, AlphaPercent = 100 }).ToList(),
    };

    /// <summary>ユーザーのキャラ用フォント: キャラ色 → [1][4][7]、白 → [0][3][5]。縁 2 は [2]=黒・[6]=白（未使用）。</summary>
    private static N3FontDetail CharaFont(string chara)
    {
        var d = N3FontDetail.CreateDefault();
        d.Brushes[0] = Solid("FFFFFF");
        d.Brushes[1] = Solid(chara);
        d.Brushes[2] = Solid("000000");
        d.Brushes[3] = Solid("FFFFFF");
        d.Brushes[4] = Solid(chara);
        d.Brushes[5] = Solid("FFFFFF");
        d.Brushes[6] = Solid("FFFFFF");
        d.Brushes[7] = Solid(chara);
        return d;
    }

    private static string Colors(N3FontDetail d) =>
        string.Join(" ", d.Brushes.Select(b => b.Type == N3Brush.TypeSolid ? N3FontSet.NormalizeWeb16(b.Color) : $"t{b.Type}"));

    // ------------------------------------------------------------ 比べる

    [Fact]
    public void 比べる_キャラ用フォントはキャラ色の反転の形()
    {
        var d = CharaFont("F8B500");
        var m = N3ColorPatterns.Detect(d, N3ColorPatterns.BuiltIns);
        Assert.NotNull(m);
        Assert.Equal(N3ColorPatterns.CharaInverseId, m!.Pattern.Id);
        Assert.True(m.IsExact);
    }

    [Fact]
    public void 比べる_1箇所だけ違えば入力ミスとして見つける()
    {
        // （翔音花火ポルカ玲のりこ）: ワイプ前の文字だけ並びが違う
        var d = CharaFont("000000");
        d.Brushes[1] = Mille((0, "999999"), (0.2, "FF0000"), (1, "808080"));
        d.Brushes[4] = Mille((0, "C77BD9"), (0.2, "FF0000"), (1, "808080"));
        d.Brushes[7] = Mille((0, "999999"), (0.2, "FF0000"), (1, "808080"));
        var m = N3ColorPatterns.Detect(d, N3ColorPatterns.BuiltIns);
        Assert.Equal(N3ColorPatterns.CharaInverseId, m!.Pattern.Id);
        Assert.Equal(new[] { 4 }, m.Deviations);
    }

    [Fact]
    public void 比べる_マーカーの位置だけのずれも見つける()
    {
        var d = CharaFont("000000");
        d.Brushes[1] = Mille((0, "AAAAAA"), (0.33, "BBBBBB"), (0.66, "CCCCCC"), (1, "808080"));
        d.Brushes[4] = Mille((0, "AAAAAA"), (0.34, "BBBBBB"), (0.65, "CCCCCC"), (1, "808080"));
        d.Brushes[7] = Mille((0, "AAAAAA"), (0.34, "BBBBBB"), (0.65, "CCCCCC"), (1, "808080"));
        var m = N3ColorPatterns.Evaluate(d, Chara);
        Assert.Equal(new[] { 1 }, m.Deviations); // 3 つのうち 2 つがそろっているほうを正とする
    }

    [Fact]
    public void 比べる_ニコカラメーカーの既定配色はどのパターンにも当てはめない()
    {
        // 青配色: 6 組すべて別
        var d = N3FontDetail.CreateDefault();
        string[] colors = { "FFFFFF", "0000FF", "000000", "3333FF", "0000C0", "FFFFFF", "FFFFFF", "E0E0FF" };
        for (int i = 0; i < 8; i++) d.Brushes[i] = Solid(colors[i]);
        Assert.Null(N3ColorPatterns.Detect(d, N3ColorPatterns.BuiltIns));
    }

    [Fact]
    public void 比べる_コーラス配色は意図した配色なので当てはめない()
    {
        // ニコカラメーカー3 のコーラス配色を元にしたもの: 後の文字・前の縁・縁 2 がそれぞれ別の色
        var d = N3FontDetail.CreateDefault();
        string[] colors = { "FF9B00", "FFFFFF", "000000", "FFE19B", "FFFFFF", "3C2300", "FFFFFF", "FFE19B" };
        for (int i = 0; i < 8; i++) d.Brushes[i] = Solid(colors[i]);

        // ワイプ前後が同じ: 文字（2 箇所）と縁（2 箇所）が食い違っていて、どちらが正しいか分からない
        Assert.True(N3ColorPatterns.Evaluate(d, NoWipe).Ambiguous);
        // キャラ色の反転: ベース色の 3 箇所がすべて違う
        Assert.True(N3ColorPatterns.Evaluate(d, Chara).Ambiguous);
        Assert.Null(N3ColorPatterns.Detect(d, N3ColorPatterns.BuiltIns));
    }

    [Fact]
    public void 比べる_情報系はワイプ前後が同じ形()
    {
        var d = N3FontDetail.CreateDefault();
        string[] colors = { "FFFFFF", "404040", "000000", "808080", "FFFFFF", "404040", "FFFFFF", "808080" };
        for (int i = 0; i < 8; i++) d.Brushes[i] = Solid(colors[i]);
        var m = N3ColorPatterns.Detect(d, N3ColorPatterns.BuiltIns);
        Assert.Equal(N3ColorPatterns.NoWipeId, m!.Pattern.Id);
        Assert.True(m.IsExact);
    }

    [Fact]
    public void 比べる_種類ごとに使う値だけを比べる()
    {
        var a = Solid("ff0000");
        var b = Solid("FF0000");
        b.Stops = new List<N3GradientStop> { new() { Position = 0.5, Color = "123456" } }; // 単色では使わない値
        Assert.True(N3ColorPatterns.SameLook(a, b));
        Assert.False(N3ColorPatterns.SameLook(a, Solid("FF0000", 50)));
        Assert.False(N3ColorPatterns.SameLook(Solid(""), Solid("FFFFFF")));
        Assert.True(N3ColorPatterns.SameLook(Solid(""), Solid("")));
    }

    [Fact]
    public void 当てはめ_フォント設定で選んだパターン_個別_自動()
    {
        var patterns = N3ColorPatterns.All(null);
        var font = new N3FontSet { Name = "（梢）" };
        font.Detail = CharaFont("68BE8D");

        Assert.Equal(N3ColorPatterns.CharaInverseId, N3ColorPatterns.Effective(font, patterns)!.Pattern.Id);

        font.ColorPatternId = N3ColorPatterns.NoneId;
        Assert.Null(N3ColorPatterns.Effective(font, patterns));

        // 選んだパターンは、違う箇所がいくつあってもそのまま（直すまで知らせ続ける）
        font.ColorPatternId = N3ColorPatterns.NoWipeId;
        var m = N3ColorPatterns.Effective(font, patterns);
        Assert.Equal(N3ColorPatterns.NoWipeId, m!.Pattern.Id);
        Assert.NotEmpty(m.Deviations);
    }

    // ------------------------------------------------------------ 変える

    [Fact]
    public void 合わせる_役割ごとに多いほうの色へそろえる()
    {
        var d = CharaFont("F8B500");
        d.Brushes[4] = Solid("123456"); // 入力ミス
        Assert.Equal(1, N3ColorPatterns.Align(d, Chara));
        Assert.Equal("FFFFFF F8B500 000000 FFFFFF F8B500 FFFFFF FFFFFF F8B500", Colors(d));
        Assert.True(N3ColorPatterns.Evaluate(d, Chara).IsExact);
        Assert.NotSame(d.Brushes[1], d.Brushes[4]); // 写しは別のもの（あとで 1 箇所だけ直せるように）
    }

    [Fact]
    public void 合わせる_色がばらばらならワイプ前の文字の色を役割の色にする()
    {
        // 新しいフォント設定（ニコカラメーカーの既定: 後の文字 白・後の縁 黒・前の文字 青・前の縁 白、ほかは未指定）
        var d = N3FontDetail.CreateDefault();
        N3ColorPatterns.Align(d, Chara);
        Assert.Equal("FFFFFF 4DA3FF  FFFFFF 4DA3FF FFFFFF  4DA3FF", Colors(d));
    }

    [Fact]
    public void コピー_役割の色だけを写す()
    {
        var from = CharaFont("E4007F");
        var to = CharaFont("68BE8D");
        to.Brushes[0] = Solid("F0F0F0");
        N3ColorPatterns.CopyRole(from, to, Chara, 0);
        Assert.Equal("F0F0F0 E4007F 000000 FFFFFF E4007F FFFFFF FFFFFF E4007F", Colors(to)); // ベース色はそのまま
    }

    // ------------------------------------------------------------ 一覧と保存

    [Fact]
    public void 一覧_標準のあとにユーザーのパターン_重なる識別子は除く()
    {
        var user = new List<N3ColorPattern>
        {
            new() { Id = "u1", Name = "自作", Roles = new List<string> { "A" }, Slots = new[] { 0, 0, 0 } }, // 短い並びは個別で埋める
            new() { Id = "u1", Name = "重なり" },
            new() { Id = N3ColorPatterns.CharaInverseId, Name = "標準と同じ識別子" },
            new() { Id = N3ColorPatterns.NoneId, Name = "個別と同じ識別子" },
        };
        var all = N3ColorPatterns.All(user);
        Assert.Equal(new[] { N3ColorPatterns.CharaInverseId, N3ColorPatterns.NoWipeId, "u1" }, all.Select(p => p.Id));
        Assert.Equal(new[] { 0, 0, 0, -1, -1, -1, -1, -1 }, all[2].Slots);
        Assert.Equal(new[] { 0, 1, 2 }, all[2].SlotsOf(0));
        Assert.True(all[0].IsBuiltIn);
        Assert.False(all[2].IsBuiltIn);
    }

    [Fact]
    public void 保存_パターンの選択とユーザーのパターンを保存して読み戻せる()
    {
        var font = new N3FontSet { Name = "a" };
        var settings = new AppSettings
        {
            N3FontSets = { font, new N3FontSet { Name = "b", ColorPatternId = N3ColorPatterns.NoneId } },
            N3UserColorPatterns = { new N3ColorPattern { Id = "u1", Name = "自作", Roles = new List<string> { "A", "B" }, Slots = new[] { 0, 1, -1, 0, 1, 0, -1, 1 } } },
            N3DefaultColorPatternId = "u1",
        };
        string path = Path.Combine(Path.GetTempPath(), $"nkp-pattern-{Guid.NewGuid():N}.json");
        try
        {
            settings.Save(path);
            string json = File.ReadAllText(path);
            Assert.Equal(1, json.Split("\"ColorPatternId\"").Length - 1); // 自動（null）は書かない
            var loaded = AppSettings.Load(path);
            Assert.Null(loaded.N3FontSets[0].ColorPatternId);
            Assert.Equal(N3ColorPatterns.NoneId, loaded.N3FontSets[1].ColorPatternId);
            Assert.Equal("u1", loaded.N3DefaultColorPatternId);
            var p = Assert.Single(loaded.N3UserColorPatterns);
            Assert.Equal(new[] { 0, 1, -1, 0, 1, 0, -1, 1 }, p.Slots);
            Assert.Equal(new[] { "A", "B" }, p.Roles);
        }
        finally
        {
            File.Delete(path);
        }

        // テンプレート（.tttpl）には含めない
        var target = new AppSettings();
        target.CopyFrom(settings);
        Assert.Empty(target.N3UserColorPatterns);
        Assert.Equal(N3ColorPatterns.CharaInverseId, target.N3DefaultColorPatternId);
    }
}
