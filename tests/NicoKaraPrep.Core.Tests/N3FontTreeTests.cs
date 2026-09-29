using NicoKaraPrep.Core.Model;
using NicoKaraPrep.Core.Project;

namespace NicoKaraPrep.Core.Tests;

public class N3FontTreeTests
{
    private static List<N3FontSet> Fonts(params string[] names) => names.Select(n => new N3FontSet { Name = n }).ToList();

    private static string IdOf(List<N3FontSet> fonts, string name) => fonts.Single(f => f.Name == name).Id;

    /// <summary>階層を「名前(下の階層)」の形の文字列にする（フォルダは [名前]、折りたたみは末尾に -）。</summary>
    private static string Shape(IEnumerable<N3FontTreeNode> nodes, List<N3FontSet> fonts) =>
        string.Join(" ", nodes.Select(n =>
        {
            string name = n.IsFolder ? $"[{n.FolderName}]" : fonts.First(f => f.Id == n.FontId).Name;
            string children = n.Children.Count > 0 ? $"({Shape(n.Children, fonts)})" : "";
            return name + children + (n.Collapsed ? "-" : "");
        }));

    /// <summary>ユーザーの例: 標準配色・コーラス配色、フォルダ「蓮ノ空」の下に（102期生）とその下の 3 人、フォルダ「ユニット」。</summary>
    private static (List<N3FontSet> Fonts, List<N3FontTreeNode> Roots) Sample()
    {
        var fonts = Fonts("標準配色", "コーラス配色", "（102期生）", "（梢）", "（綴理）", "（慈）", "（スリーズブーケ）");
        var hasu = N3FontTreeNode.ForFolder("蓮ノ空");
        var grade = N3FontTreeNode.ForFont(IdOf(fonts, "（102期生）"));
        grade.Children.AddRange(new[] { "（梢）", "（綴理）", "（慈）" }.Select(n => N3FontTreeNode.ForFont(IdOf(fonts, n))));
        var unit = N3FontTreeNode.ForFolder("ユニット");
        unit.Children.Add(N3FontTreeNode.ForFont(IdOf(fonts, "（スリーズブーケ）")));
        hasu.Children.Add(grade);
        hasu.Children.Add(unit);
        var roots = new List<N3FontTreeNode>
        {
            N3FontTreeNode.ForFont(IdOf(fonts, "標準配色")),
            N3FontTreeNode.ForFont(IdOf(fonts, "コーラス配色")),
            hasu,
        };
        return (fonts, roots);
    }

    private static string FolderKey(List<N3FontTreeNode> roots, string name) =>
        N3FontTree.Walk(roots).First(x => x.Node.FolderName == name).Node.Key;

    // ------------------------------------------------------------ 整える

    [Fact]
    public void 整える_初めて作るときは全部を最上位に今の順で並べる()
    {
        var fonts = Fonts("A", "B", "C");
        fonts[1].ImportedFrom = @"C:\songs\曲.n3proj";
        var roots = new List<N3FontTreeNode>();
        Assert.True(N3FontTree.Normalize(roots, fonts, groupImported: false));
        Assert.Equal("A B C", Shape(roots, fonts));
        Assert.False(N3FontTree.Normalize(roots, fonts, groupImported: false));
    }

    [Fact]
    public void 整える_無いフォント設定の節は外して下の節を繰り上げる()
    {
        var (fonts, roots) = Sample();
        fonts.RemoveAll(f => f.Name == "（102期生）");
        Assert.True(N3FontTree.Normalize(roots, fonts));
        Assert.Equal("標準配色 コーラス配色 [蓮ノ空](（梢） （綴理） （慈） [ユニット](（スリーズブーケ）))", Shape(roots, fonts));
    }

    [Fact]
    public void 整える_同じフォント設定の2つ目の節は外す()
    {
        var (fonts, roots) = Sample();
        var dup = N3FontTreeNode.ForFont(IdOf(fonts, "（梢）"));
        dup.Children.Add(N3FontTreeNode.ForFont(IdOf(fonts, "標準配色")));
        roots.Add(dup);
        Assert.True(N3FontTree.Normalize(roots, fonts));
        Assert.Equal("標準配色 コーラス配色 [蓮ノ空](（102期生）(（梢） （綴理） （慈）) [ユニット](（スリーズブーケ）))", Shape(roots, fonts));
    }

    [Fact]
    public void 整える_階層に無いフォント設定は取り込み元ごとのフォルダか最上位の末尾へ()
    {
        var (fonts, roots) = Sample();
        var added = Fonts("（花帆）", "（さやか）", "テンプレ", "手で追加");
        added[0].ImportedFrom = @"D:\work\Dream Believers.n3proj";
        added[1].ImportedFrom = @"D:\work\Dream Believers.n3proj";
        added[2].ImportedFrom = @"C:\nkm3\TemplateFont\テンプレ.tpl";
        fonts.AddRange(added);
        roots.Add(N3FontTreeNode.ForFolder("Dream Believers")); // 同じ名前の最上位のフォルダがあればそこへ入れる

        Assert.True(N3FontTree.Normalize(roots, fonts));
        Assert.Equal(
            "標準配色 コーラス配色 [蓮ノ空](（102期生）(（梢） （綴理） （慈）) [ユニット](（スリーズブーケ）)) " +
            "[Dream Believers](（花帆） （さやか）) [ニコカラメーカー3 のテンプレート](テンプレ) 手で追加",
            Shape(roots, fonts));
    }

    [Fact]
    public void 整える_フォルダの識別子と名前を直す()
    {
        var fonts = Fonts("A");
        var roots = new List<N3FontTreeNode>
        {
            new() { FolderName = "X" },
            new() { FolderId = "same", FolderName = "Y" },
            new() { FolderId = "SAME", FolderName = " " },
            new() { FontId = fonts[0].Id, FolderName = "不要" },
        };
        Assert.True(N3FontTree.Normalize(roots, fonts));
        Assert.All(roots.Take(3), n => Assert.False(string.IsNullOrEmpty(n.FolderId)));
        Assert.Equal(3, roots.Take(3).Select(n => n.FolderId!.ToLowerInvariant()).Distinct().Count());
        Assert.Equal(N3FontTree.UnnamedFolderName, roots[2].FolderName);
        Assert.Null(roots[3].FolderName);
        Assert.False(roots[3].IsFolder);
    }

    // ------------------------------------------------------------ 並び

    [Fact]
    public void 並び_フォント設定の一覧を上から順に並べ替える()
    {
        var (fonts, roots) = Sample();
        fonts.Reverse();
        var extra = new N3FontSet { Name = "階層に無い" };
        fonts.Insert(2, extra);
        Assert.True(N3FontTree.SortFonts(fonts, roots));
        Assert.Equal(new[] { "標準配色", "コーラス配色", "（102期生）", "（梢）", "（綴理）", "（慈）", "（スリーズブーケ）", "階層に無い" },
            fonts.Select(f => f.Name));
        Assert.False(N3FontTree.SortFonts(fonts, roots));
    }

    [Fact]
    public void 探す_上の階層と下の数()
    {
        var (fonts, roots) = Sample();
        var path = N3FontTree.Ancestors(roots, IdOf(fonts, "（綴理）"));
        Assert.Equal(new[] { "蓮ノ空", null }, path.Select(n => n.FolderName));
        Assert.Equal(IdOf(fonts, "（102期生）"), path[1].FontId);
        Assert.Empty(N3FontTree.Ancestors(roots, IdOf(fonts, "標準配色")));
        Assert.Empty(N3FontTree.Ancestors(roots, "無い"));

        var hasu = N3FontTree.Find(roots, FolderKey(roots, "蓮ノ空"))!.Node;
        Assert.Equal(5, N3FontTree.CountFonts(hasu));
        Assert.True(N3FontTree.Contains(hasu, IdOf(fonts, "（慈）")));
        Assert.False(N3FontTree.Contains(hasu, IdOf(fonts, "標準配色")));
    }

    // ------------------------------------------------------------ 変える

    [Fact]
    public void 変える_同じ階層の中で上下に動かす()
    {
        var (fonts, roots) = Sample();
        string kozue = IdOf(fonts, "（梢）");
        Assert.False(N3FontTree.MoveWithinSiblings(roots, kozue, -1)); // 先頭は上へ動かない
        Assert.True(N3FontTree.MoveWithinSiblings(roots, kozue, +1));
        Assert.True(N3FontTree.MoveWithinSiblings(roots, kozue, +1));
        Assert.False(N3FontTree.MoveWithinSiblings(roots, kozue, +1)); // 末尾は下へ動かない（別の親へは移らない）
        Assert.Equal("標準配色 コーラス配色 [蓮ノ空](（102期生）(（綴理） （慈） （梢）) [ユニット](（スリーズブーケ）))", Shape(roots, fonts));
    }

    [Fact]
    public void 変える_階層を下げるとすぐ上の節の中の末尾へ入って開く()
    {
        var (fonts, roots) = Sample();
        var hasu = N3FontTree.Find(roots, FolderKey(roots, "蓮ノ空"))!.Node;
        hasu.Collapsed = true;
        Assert.False(N3FontTree.Indent(roots, IdOf(fonts, "標準配色"))); // 先頭は下げられない
        Assert.True(N3FontTree.Indent(roots, IdOf(fonts, "コーラス配色"))); // フォント設定の下にも入れられる
        Assert.True(N3FontTree.Indent(roots, hasu.Key));
        Assert.Equal("標準配色(コーラス配色 [蓮ノ空](（102期生）(（梢） （綴理） （慈）) [ユニット](（スリーズブーケ）))-)", Shape(roots, fonts));
        Assert.False(roots[0].Collapsed);
    }

    [Fact]
    public void 変える_階層を上げると親のすぐ後へ()
    {
        var (fonts, roots) = Sample();
        Assert.False(N3FontTree.Outdent(roots, IdOf(fonts, "標準配色"))); // 最上位は上げられない
        Assert.True(N3FontTree.Outdent(roots, IdOf(fonts, "（綴理）")));
        Assert.Equal("標準配色 コーラス配色 [蓮ノ空](（102期生）(（梢） （慈）) （綴理） [ユニット](（スリーズブーケ）))", Shape(roots, fonts));
        Assert.True(N3FontTree.Outdent(roots, IdOf(fonts, "（102期生）"))); // 下の節ごと上がる
        Assert.Equal("標準配色 コーラス配色 [蓮ノ空](（綴理） [ユニット](（スリーズブーケ）)) （102期生）(（梢） （慈）)", Shape(roots, fonts));
    }

    [Fact]
    public void 変える_別の節の中へ移す_自分の下へは移せない()
    {
        var (fonts, roots) = Sample();
        string hasu = FolderKey(roots, "蓮ノ空");
        string unit = FolderKey(roots, "ユニット");
        string grade = IdOf(fonts, "（102期生）");

        Assert.False(N3FontTree.MoveInto(roots, hasu, unit));    // 自分の下の階層へは移せない
        Assert.False(N3FontTree.MoveInto(roots, grade, grade));  // 自分自身へも移せない
        Assert.False(N3FontTree.MoveInto(roots, grade, "無い"));

        N3FontTree.Find(roots, unit)!.Node.Collapsed = true;
        Assert.True(N3FontTree.MoveInto(roots, IdOf(fonts, "標準配色"), unit));
        Assert.False(N3FontTree.Find(roots, unit)!.Node.Collapsed); // 移し先は開く
        Assert.True(N3FontTree.MoveInto(roots, grade, null, index: 0)); // 最上位の先頭へ（下の節ごと）
        Assert.Equal("（102期生）(（梢） （綴理） （慈）) コーラス配色 [蓮ノ空]([ユニット](（スリーズブーケ） 標準配色))", Shape(roots, fonts));

        // 同じ一覧の中で後ろへ移すときは、外した分を詰めた位置に入る
        Assert.True(N3FontTree.MoveInto(roots, grade, null, index: 2));
        Assert.Equal("コーラス配色 （102期生）(（梢） （綴理） （慈）) [蓮ノ空]([ユニット](（スリーズブーケ） 標準配色))", Shape(roots, fonts));
    }

    [Fact]
    public void 変える_外すときは下の節を繰り上げるか下の節ごと外す()
    {
        var (fonts, roots) = Sample();
        var removed = N3FontTree.Remove(roots, IdOf(fonts, "（102期生）"), keepChildren: true);
        Assert.NotNull(removed);
        Assert.Empty(removed!.Children);
        Assert.Equal("標準配色 コーラス配色 [蓮ノ空](（梢） （綴理） （慈） [ユニット](（スリーズブーケ）))", Shape(roots, fonts));

        var unit = N3FontTree.Remove(roots, FolderKey(roots, "ユニット"), keepChildren: false);
        Assert.Single(unit!.Children);
        Assert.Equal("標準配色 コーラス配色 [蓮ノ空](（梢） （綴理） （慈）)", Shape(roots, fonts));
        Assert.Null(N3FontTree.Remove(roots, "無い", keepChildren: true));
    }

    [Fact]
    public void 変える_入れる位置()
    {
        var (fonts, roots) = Sample();
        var a = new N3FontSet { Name = "（花帆）" };
        var b = new N3FontSet { Name = "（吟子）" };
        fonts.Add(a);
        fonts.Add(b);
        Assert.True(N3FontTree.InsertAfter(roots, IdOf(fonts, "（綴理）"), N3FontTreeNode.ForFont(a.Id)));
        Assert.True(N3FontTree.Insert(roots, FolderKey(roots, "ユニット"), N3FontTreeNode.ForFont(b.Id), index: 0));
        Assert.False(N3FontTree.Insert(roots, "無い", N3FontTreeNode.ForFolder("x")));
        Assert.False(N3FontTree.InsertAfter(roots, "無い", N3FontTreeNode.ForFolder("x")));
        Assert.Equal("標準配色 コーラス配色 [蓮ノ空](（102期生）(（梢） （綴理） （花帆） （慈）) [ユニット](（吟子） （スリーズブーケ）))", Shape(roots, fonts));
    }

    [Fact]
    public void 変える_曲専用で置き換えたフォント設定を同じ位置で指す()
    {
        var (fonts, roots) = Sample();
        string old = IdOf(fonts, "（梢）");
        Assert.True(N3FontTree.ReplaceFontId(roots, old, "new-id"));
        Assert.Equal("new-id", N3FontTree.Ancestors(roots, "new-id").Last().Children[0].FontId);
        Assert.False(N3FontTree.ReplaceFontId(roots, FolderKey(roots, "蓮ノ空"), "x")); // フォルダは付け替えない
    }

    [Fact]
    public void 比べる_同じ形か()
    {
        var (_, roots) = Sample();
        var copy = N3FontTree.Clone(roots);
        Assert.True(N3FontTree.SameShape(roots, copy));
        copy[2].Children[1].FolderName = "ユニット2";
        Assert.False(N3FontTree.SameShape(roots, copy));
        Assert.Equal("ユニット", roots[2].Children[1].FolderName); // 深いコピー
    }

    // ------------------------------------------------------------ 保存

    [Fact]
    public void 保存_階層を設定ファイルに保存して読み戻せる_空の下の階層と開いた状態は書かない()
    {
        var (fonts, roots) = Sample();
        roots[2].Collapsed = true;
        var settings = new AppSettings { N3FontSets = fonts, N3FontHierarchy = roots };
        string path = Path.Combine(Path.GetTempPath(), $"nkp-tree-{Guid.NewGuid():N}.json");
        try
        {
            settings.Save(path);
            string json = File.ReadAllText(path);
            Assert.Contains("\"N3FontHierarchy\"", json);
            Assert.Contains("\"FolderName\": \"蓮ノ空\"", json);
            Assert.DoesNotContain("\"Children\": []", json);
            Assert.Equal(1, json.Split("\"Collapsed\"").Length - 1);

            var loaded = AppSettings.Load(path);
            Assert.NotNull(loaded.N3FontHierarchy);
            Assert.True(N3FontTree.SameShape(roots, loaded.N3FontHierarchy!));
            Assert.False(N3FontTree.Normalize(loaded.N3FontHierarchy!, loaded.N3FontSets));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void 保存_階層の無い古い設定は未作成として読む_テンプレートには含めない()
    {
        string path = Path.Combine(Path.GetTempPath(), $"nkp-tree-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{\"N3FontSets\":[{\"Name\":\"A\"}]}");
            var loaded = AppSettings.Load(path);
            Assert.Null(loaded.N3FontHierarchy);
            Assert.Single(loaded.N3FontSets);
        }
        finally
        {
            File.Delete(path);
        }

        var (fonts, roots) = Sample();
        var template = new AppSettings { N3FontSets = fonts, N3FontHierarchy = roots };
        var target = new AppSettings();
        target.CopyFrom(template);
        Assert.Null(target.N3FontHierarchy);
        Assert.Empty(target.N3FontSets);
    }
}
