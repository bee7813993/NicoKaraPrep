using System.Text.Json;
using NicoKaraPrep.Core.Model;
using NicoKaraPrep.Core.Project;
using NicoKaraPrep.Core.Validation;

namespace NicoKaraPrep.Core.Tests;

public class N3FontLibraryTests
{
    private static List<N3FontSet> Library(params string[] names) => names.Select(n => new N3FontSet { Name = n }).ToList();

    private static List<string> Names(IEnumerable<N3FontSet> list) => list.Select(f => f.Name).ToList();

    // ------------------------------------------------------------ 一覧の操作

    [Fact]
    public void 追加_名前が重なれば末尾に2から順に付ける()
    {
        var list = Library("（花帆）", "（花帆）2", "情報2");
        N3FontLibrary.Add(list, new N3FontSet { Name = "（花帆）" });
        N3FontLibrary.Add(list, new N3FontSet { Name = "情報2" });
        N3FontLibrary.Add(list, new N3FontSet { Name = "  " });
        N3FontLibrary.Add(list, new N3FontSet { Name = "新規" });
        N3FontLibrary.Add(list, new N3FontSet { Name = "（梢）" }, index: 0);
        Assert.Equal(new[] { "（梢）", "（花帆）", "（花帆）2", "情報2", "（花帆）3", "情報3", "新規", "新規2" }, Names(list));
    }

    [Fact]
    public void 追加と名前変更_前後の空白は消さない()
    {
        // ニコカラメーカー側の名前（" X" など）とマージで一致させるため
        var list = Library("B");
        Assert.Equal(" B ", N3FontLibrary.Add(list, new N3FontSet { Name = " B " }).Name);
        Assert.Equal(" B 2", N3FontLibrary.Add(list, new N3FontSet { Name = " B " }).Name);
        Assert.Equal("B", N3FontLibrary.Rename(list, list[0].Id, " X"));
        Assert.Equal(new[] { " X", " B ", " B 2" }, Names(list));

        // 空白だけの名前は書き出されないので「新規」にする
        Assert.Equal("新規", N3FontLibrary.Add(list, new N3FontSet { Name = " \t" }).Name);
    }

    [Fact]
    public void 追加_Idが重なれば新しいIdにする()
    {
        var list = Library("a");
        var copy = list[0].Clone();
        copy.Name = "b";
        N3FontLibrary.Add(list, copy);
        Assert.NotEqual(list[0].Id, list[1].Id);
    }

    [Fact]
    public void Id_重複しているIdを付け直す()
    {
        var list = Library("a", "b", "c");
        list[1].Id = list[0].Id;
        list[2].Id = list[0].Id.ToUpperInvariant();
        string first = list[0].Id;
        N3FontLibrary.EnsureIds(list);
        Assert.Equal(first, list[0].Id);
        Assert.Equal(3, list.Select(f => f.Id.ToLowerInvariant()).Distinct().Count());
    }

    [Fact]
    public void 複製_元の直後に入り連動と取り込み元は引き継がない()
    {
        var list = Library("（花帆）", "（梢）");
        var src = list[0];
        src.NkmGuid = "0f8fad5b-d9cb-469f-a165-70867728950e";
        src.NkmSynchronize = true;
        src.ImportedFrom = @"C:\a.n3proj";
        src.Detail.Brushes[4].Stops.Add(new N3GradientStop { Color = "123456" });

        var copy = N3FontLibrary.Duplicate(list, src.Id)!;
        Assert.Equal(new[] { "（花帆）", "（花帆）2", "（梢）" }, Names(list));
        Assert.NotEqual(src.Id, copy.Id);
        Assert.Null(copy.NkmGuid);
        Assert.False(copy.NkmSynchronize);
        Assert.Null(copy.ImportedFrom);
        Assert.Equal(JsonSerializer.Serialize(src.Detail), JsonSerializer.Serialize(copy.Detail));
        copy.Detail.Brushes[4].Stops[0].Color = "FFFFFF";
        Assert.Equal("123456", src.Detail.Brushes[4].Stops[0].Color);
        Assert.Null(N3FontLibrary.Duplicate(list, "無いId"));
    }

    [Fact]
    public void 削除と移動_Idで指定する()
    {
        var list = Library("a", "b", "c", "d");
        Assert.True(N3FontLibrary.Move(list, list[0].Id, 2));
        Assert.Equal(new[] { "b", "c", "a", "d" }, Names(list));
        Assert.True(N3FontLibrary.Move(list, list[3].Id, -5));
        Assert.Equal(new[] { "d", "b", "c", "a" }, Names(list));
        Assert.True(N3FontLibrary.Move(list, list[0].Id, 99));
        Assert.Equal(new[] { "b", "c", "a", "d" }, Names(list));

        Assert.True(N3FontLibrary.Remove(list, list[1].Id));
        Assert.Equal(new[] { "b", "a", "d" }, Names(list));
        Assert.False(N3FontLibrary.Remove(list, "無いId"));
        Assert.False(N3FontLibrary.Move(list, "無いId", 0));
    }

    [Fact]
    public void 名前変更_変更前の名前を返し連動を外す()
    {
        var list = Library("（花帆）", "（梢）");
        list[0].NkmSynchronize = true;
        list[1].NkmSynchronize = true;

        Assert.Equal("（花帆）", N3FontLibrary.Rename(list, list[0].Id, "（花帆・小）"));
        Assert.Equal("（花帆・小）", list[0].Name);
        Assert.False(list[0].NkmSynchronize);

        // 他と重なる名前は番号を付ける。同じ名前なら何もしない
        Assert.Equal("（梢）", N3FontLibrary.Rename(list, list[1].Id, "（花帆・小）"));
        Assert.Equal("（花帆・小）2", list[1].Name);
        list[1].NkmSynchronize = true;
        Assert.Equal("（花帆・小）2", N3FontLibrary.Rename(list, list[1].Id, "（花帆・小）2"));
        Assert.True(list[1].NkmSynchronize);
        Assert.Null(N3FontLibrary.Rename(list, "無いId", "x"));
    }

    [Fact]
    public void 編集_連動を外し全項目を持つフォントにする()
    {
        var f = new N3FontSet { NkmSynchronize = true, NkmGuid = "0f8fad5b-d9cb-469f-a165-70867728950e" };
        N3FontLibrary.MarkEdited(f);
        Assert.False(f.NkmSynchronize);
        Assert.True(f.HasFullDetail);
        Assert.Equal("0f8fad5b-d9cb-469f-a165-70867728950e", f.NkmGuid);
    }

    [Fact]
    public void 書き出し用_曲専用が同名の共通を置き換え曲専用だけの名前は末尾()
    {
        var library = Library("標準", "（花帆）", "（梢）");
        var song = Library("（梢）", "（曲だけ）", "（花帆）");
        var result = N3FontLibrary.ResolveForExport(library, song);
        Assert.Equal(new[] { "標準", "（花帆）", "（梢）", "（曲だけ）" }, Names(result));
        Assert.Same(library[0], result[0]);
        Assert.Same(song[2], result[1]);
        Assert.Same(song[0], result[2]);
        Assert.Same(song[1], result[3]);

        Assert.Equal(Names(library), Names(N3FontLibrary.ResolveForExport(library, Array.Empty<N3FontSet>())));
        Assert.Equal(Names(song), Names(N3FontLibrary.ResolveForExport(Array.Empty<N3FontSet>(), song)));
    }

    [Fact]
    public void 書き出し用_同じ名前が重なっても曲専用は1件にし重なる曲専用は後のものを使う()
    {
        // 置き換える共通が 2 件あっても曲専用は最初の位置に 1 件だけ入る
        var library = Library("（花帆）", "標準", "（花帆）");
        var song = Library("（花帆）");
        var result = N3FontLibrary.ResolveForExport(library, song);
        Assert.Equal(new[] { "（花帆）", "標準" }, Names(result));
        Assert.Same(song[0], result[0]);

        // 同じ名前の曲専用が 2 件あれば後のものを 1 件だけ使う（置き換えでも末尾でも）
        var dupSong = Library("（花帆）", "（曲だけ）", "（花帆）", "（曲だけ）");
        var resolved = N3FontLibrary.ResolveForExport(Library("標準", "（花帆）"), dupSong);
        Assert.Equal(new[] { "標準", "（花帆）", "（曲だけ）" }, Names(resolved));
        Assert.Same(dupSong[2], resolved[1]);
        Assert.Same(dupSong[3], resolved[2]);

        // 置き換えない共通どうしの重複はそのまま（一覧の検証で報告する）
        Assert.Equal(new[] { "標準", "標準" }, Names(N3FontLibrary.ResolveForExport(Library("標準", "標準"), new List<N3FontSet>())));
    }

    [Fact]
    public void 書き出し用_一覧や要素や名前がnullでも落ちない()
    {
        var library = Library("標準");
        Assert.Equal(new[] { "標準" }, Names(N3FontLibrary.ResolveForExport(library, null)));
        Assert.Equal(new[] { "標準" }, Names(N3FontLibrary.ResolveForExport(null, library)));
        Assert.Empty(N3FontLibrary.ResolveForExport(null, null));

        var withNull = new List<N3FontSet> { null!, new() { Name = null! }, new() { Name = "標準" } };
        var result = N3FontLibrary.ResolveForExport(withNull, new List<N3FontSet> { null! });
        Assert.Equal(2, result.Count);
        Assert.Same(withNull[1], result[0]);
    }

    [Fact]
    public void 曲専用_旧形式の曲プロジェクトは曲専用フォントが空で読め保存すると残る()
    {
        string dir = Path.Combine(Path.GetTempPath(), "NicoKaraPrepTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string song = Path.Combine(dir, "song.lrc");
            File.WriteAllText(SongProject.PathFor(song), """{"MediaPath":"a.mp4","ExportedLines":[1],"N3Proj":{"DefaultFontSetName":"（花帆）"}}""");
            var old = SongProject.TryLoad(song)!;
            Assert.Equal("a.mp4", old.MediaPath);
            Assert.Empty(old.FontSets);

            old.FontSets.Add(new N3FontSet { Name = "（花帆）", EdgeColorAfter = "F8B500" });
            old.FontSets[0].Detail.Brushes[4].Type = N3Brush.TypeMilleFeuille;
            old.Save(song);
            var back = SongProject.TryLoad(song)!;
            var f = Assert.Single(back.FontSets);
            Assert.Equal("（花帆）", f.Name);
            Assert.Equal(old.FontSets[0].Id, f.Id);
            Assert.Equal("F8B500", f.EdgeColorAfter);
            Assert.Equal(N3Brush.TypeMilleFeuille, f.Detail.Brushes[4].Type);
            Assert.Equal("（花帆）", back.N3Proj.DefaultFontSetName);

            // 手で編集して null にしたファイルも空の一覧として読み、書き出し用の一覧を作れる
            File.WriteAllText(SongProject.PathFor(song), """{"MediaPath":"a.mp4","FontSets":null}""");
            var nulled = SongProject.TryLoad(song)!;
            Assert.Empty(nulled.FontSets);
            Assert.Single(N3FontLibrary.ResolveForExport(Library("標準"), nulled.FontSets));
            File.WriteAllText(SongProject.PathFor(song), """{"MediaPath":"a.mp4","FontSets":[null,{"Name":"（花帆）"}]}""");
            Assert.Equal(new[] { "（花帆）" }, Names(SongProject.TryLoad(song)!.FontSets));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ------------------------------------------------------------ 実効値

    [Fact]
    public void 実効値_継承をたどりルビの0は歌詞の半分()
    {
        var f = new N3FontSet();
        var faces = f.Detail.Faces;
        faces[0] = new N3FontFace { FontName = "HGS創英角ﾎﾟｯﾌﾟ体", SizePx = 80, EdgePx = 15, Edge2Px = 6 };
        faces[2] = new N3FontFace { FontName = "Arial", XScale = 90 };
        faces[3] = new N3FontFace { SizePx = 40, UseEdge2 = true };
        faces[5] = new N3FontFace { FaceName = "Regular", EdgePx = 1 };

        var lyric = N3FontLibrary.EffectiveFace(f, 0);
        Assert.Equal("HGS創英角ﾎﾟｯﾌﾟ体", lyric.FontName);
        Assert.Equal("Bold", lyric.FaceName);      // デフォルトフォント
        Assert.Equal(100, lyric.XScale);
        Assert.False(lyric.UseEdge2);

        var kana = N3FontLibrary.EffectiveFace(f, 1);
        Assert.Equal(("HGS創英角ﾎﾟｯﾌﾟ体", 80.0, 15.0, 100), (kana.FontName, kana.SizePx, kana.EdgePx, kana.XScale));
        var alnum = N3FontLibrary.EffectiveFace(f, 2);
        Assert.Equal(("Arial", 90, 80.0), (alnum.FontName, alnum.XScale, alnum.SizePx));

        var ruby = N3FontLibrary.EffectiveFace(f, 3);
        Assert.Equal((40.0, 7.5, 3.0, true), (ruby.SizePx, ruby.EdgePx, ruby.Edge2Px, ruby.UseEdge2));
        Assert.Equal("HGS創英角ﾎﾟｯﾌﾟ体", ruby.FontName);

        var rubyKana = N3FontLibrary.EffectiveFace(f, 4);
        Assert.Equal((40.0, 7.5, true), (rubyKana.SizePx, rubyKana.EdgePx, rubyKana.UseEdge2));
        var rubyAlnum = N3FontLibrary.EffectiveFace(f, 5);
        Assert.Equal(("Regular", 1.0, 40.0), (rubyAlnum.FaceName, rubyAlnum.EdgePx, rubyAlnum.SizePx));

        // 歌詞／漢字も継承ならデフォルトフォント（フォント名は空のまま）
        var blank = new N3FontSet { Detail = new N3FontDetail() };
        var d = N3FontLibrary.EffectiveFace(blank, 0);
        Assert.Equal(("", "Bold", 100.0, 100, 5.0, false, 5.0), (d.FontName, d.FaceName, d.SizePx, d.XScale, d.EdgePx, d.UseEdge2, d.Edge2Px));
        Assert.Equal(50, N3FontLibrary.EffectiveFace(blank, 3).SizePx);
        Assert.Throws<ArgumentOutOfRangeException>(() => N3FontLibrary.EffectiveFace(blank, 6));
    }

    // ------------------------------------------------------------ 検証

    [Fact]
    public void 検証_正しい一覧は問題なし()
    {
        var list = Library("標準", "（花帆）");
        Assert.Empty(N3FontLibrary.Validate(list, new[] { "（花帆）", "標準", "" }));
    }

    [Fact]
    public void 検証_名前の重複と空と参照切れ()
    {
        var list = Library("（花帆）", "", "（花帆）", "（花帆）");
        var issues = N3FontLibrary.Validate(list, new[] { "（花帆）", "（梢）", "（梢）" });
        Assert.Single(issues, i => i.Kind == N3FontIssueKind.EmptyName);
        var dup = Assert.Single(issues, i => i.Kind == N3FontIssueKind.DuplicateName);
        Assert.Equal(list[2].Id, dup.FontId);
        var missing = Assert.Single(issues, i => i.Kind == N3FontIssueKind.MissingName);
        Assert.Equal("（梢）", missing.FontName);
        Assert.Null(missing.FontId);
        Assert.Equal(3, issues.Count);
    }

    [Fact]
    public void 検証_画像とマーカーと値の範囲()
    {
        var f = new N3FontSet { Name = "a" };
        var b = f.Detail.Brushes;
        b[0].Type = N3Brush.TypeBitmap;
        b[1].Type = N3Brush.TypeBitmap;
        b[1].BitmapPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
        b[2].Type = N3Brush.TypeMilleFeuille;
        b[2].Stops = new List<N3GradientStop> { new() { Position = 0 }, new() { Position = 0 }, new() { Position = 1.2 } };
        b[3].Type = N3Brush.TypeGradient;
        b[3].Stops = new List<N3GradientStop> { new() { Position = 0 }, new() { Position = 1, AlphaPercent = 101 }, new() { Position = 1 } };
        b[4].Type = 7;
        b[5].AlphaPercent = -1;
        // 単色の箇所のマーカーは使われないので調べない
        b[6].Stops = new List<N3GradientStop> { new() { Position = -1 } };
        f.Detail.BlurLevel = 3;
        f.Detail.DecorKind = -1;

        var issues = N3FontLibrary.Validate(new[] { f }, Array.Empty<string>());
        Assert.Equal(2, issues.Count(i => i.Kind == N3FontIssueKind.MissingBitmap));
        Assert.Single(issues, i => i.Kind == N3FontIssueKind.StopOutOfRange && i.BrushIndex == 2);
        Assert.Single(issues, i => i.Kind == N3FontIssueKind.StopOverlap && i.BrushIndex == 2 && i.Severity == IssueSeverity.Info);
        Assert.Single(issues, i => i.Kind == N3FontIssueKind.StopOverlap && i.BrushIndex == 3);
        Assert.Single(issues, i => i.Kind == N3FontIssueKind.ValueOutOfRange && i.BrushIndex == 3);
        Assert.Single(issues, i => i.Kind == N3FontIssueKind.ValueOutOfRange && i.BrushIndex == 4);
        Assert.Single(issues, i => i.Kind == N3FontIssueKind.ValueOutOfRange && i.BrushIndex == 5);
        Assert.Equal(2, issues.Count(i => i.Kind == N3FontIssueKind.ValueOutOfRange && i.BrushIndex < 0));
        Assert.DoesNotContain(issues, i => i.BrushIndex == 6);
        Assert.All(issues, i => Assert.Equal(f.Id, i.FontId));
        Assert.Contains(issues, i => i.Message.Contains("ワイプ後の文字の画像が指定されていません"));
    }

    [Fact]
    public void 検証_マーカーの位置が数値でなければ範囲外()
    {
        foreach (double position in new[] { double.NaN, double.PositiveInfinity })
        {
            var f = new N3FontSet { Name = "a" };
            f.Detail.Brushes[1].Type = N3Brush.TypeGradient;
            f.Detail.Brushes[1].Stops = new List<N3GradientStop> { new() { Position = 0 }, new() { Position = position } };
            var issue = Assert.Single(N3FontLibrary.Validate(new[] { f }, Array.Empty<string>()));
            Assert.Equal((N3FontIssueKind.StopOutOfRange, 1), (issue.Kind, issue.BrushIndex));
        }
    }

    // ------------------------------------------------------------ 配色の一括操作

    private static N3FontDetail Numbered()
    {
        var d = new N3FontDetail();
        for (int i = 0; i < N3FontDetail.BrushCount; i++) d.Brushes[i].Color = $"0000{i:X2}";
        return d;
    }

    private static string Colors(N3FontDetail d) => string.Join(",", d.Brushes.Select(b => b.Color[^1..]));

    [Fact]
    public void 一括操作_ワイプ前後の交換とコピー()
    {
        var d = Numbered();
        N3FontLibrary.SwapBeforeAfter(d);
        Assert.Equal("4,5,6,7,0,1,2,3", Colors(d));

        d = Numbered();
        N3FontLibrary.CopyAfterToBefore(d);
        Assert.Equal("0,1,2,3,0,1,2,3", Colors(d));
        Assert.NotSame(d.Brushes[0], d.Brushes[4]);

        d = Numbered();
        N3FontLibrary.CopyBeforeToAfter(d);
        Assert.Equal("4,5,6,7,4,5,6,7", Colors(d));
        Assert.NotSame(d.Brushes[0], d.Brushes[4]);
    }

    [Fact]
    public void 一括操作_他のフォントから指定した箇所の配色を写す()
    {
        var from = Numbered();
        from.Brushes[1].Type = N3Brush.TypeMilleFeuille;
        from.Brushes[1].Stops.Add(new N3GradientStop { Color = "ABCDEF" });
        var to = new N3FontDetail();

        N3FontLibrary.CopyBrushes(from, to, new[] { 1, 4, 9, -1 });
        Assert.Equal(N3Brush.TypeMilleFeuille, to.Brushes[1].Type);
        Assert.Equal("ABCDEF", to.Brushes[1].Stops[0].Color);
        Assert.Equal("000004", to.Brushes[4].Color);
        Assert.True(to.Brushes[0].IsUnset);
        Assert.NotSame(from.Brushes[1], to.Brushes[1]);

        N3FontLibrary.CopyBrushes(from, to, Enumerable.Range(0, N3FontDetail.BrushCount));
        Assert.Equal(Colors(from), Colors(to));
    }
}
