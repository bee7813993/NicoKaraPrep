using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.Core.Tests;

/// <summary>レイアウト設定ビューの 元に戻す・やり直し の履歴（N3LayoutHistory）と、一覧の出どころ（N3LayoutLibrary.Entries）。</summary>
public class N3LayoutHistoryTests
{
    private static readonly DateTime T0 = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    private static List<N3Layout> Layouts(params string[] names) =>
        names.Select(n => new N3Layout { Name = n, HorizontalAlignments = new List<int> { 0, 2 } }).ToList();

    private static string Names(IEnumerable<N3Layout> layouts) => string.Join(",", layouts.Select(l => $"{l.Name}:{l.LineSpacePx}"));

    [Fact]
    public void 戻すとやり直すで一覧が行き来し_写しは別のもの()
    {
        var live = Layouts("a");
        var history = new N3LayoutHistory();
        history.Push(live, null, T0, out bool added);
        Assert.True(added);
        live[0].LineSpacePx = 10;
        live[0].HorizontalAlignments.Add(1);

        var undo = history.Undo(live);
        Assert.NotNull(undo);
        Assert.Equal("a:60", Names(undo!.Layouts));
        Assert.Equal(2, undo.Layouts[0].HorizontalAlignments.Count);
        Assert.Empty(undo.ReferenceChanges);
        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);

        // 戻した一覧を使っていても、履歴の中の記録は変わらない
        undo.Layouts[0].LineSpacePx = 99;
        var redo = history.Redo(undo.Layouts);
        Assert.Equal("a:10", Names(redo!.Layouts));
        Assert.Equal(3, redo.Layouts[0].HorizontalAlignments.Count);
        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Equal("a:99", Names(history.Undo(redo.Layouts)!.Layouts));
        Assert.Null(history.Undo(Layouts()));
    }

    [Fact]
    public void 同じ欄の続けての変更は_時間内だけ1回にまとめる()
    {
        var live = Layouts("a");
        var history = new N3LayoutHistory();
        var first = history.Push(live, "a|行間", T0, out _);
        live[0].LineSpacePx = 1;
        var same = history.Push(live, "a|行間", T0.AddSeconds(1), out bool added);
        Assert.False(added);
        Assert.Same(first, same);
        live[0].LineSpacePx = 2;
        // 前の変更から 1.5 秒以上たてば別の記録
        history.Push(live, "a|行間", T0.AddSeconds(3), out added);
        Assert.True(added);
        // 欄が違えば別の記録
        history.Push(live, "a|左右余白", T0.AddSeconds(3.1), out added);
        Assert.True(added);
        Assert.Equal(3, history.UndoCount);

        // まとめない指示のあとは、同じ欄でも別の記録
        history.BreakCoalescing();
        history.Push(live, "a|左右余白", T0.AddSeconds(3.2), out added);
        Assert.True(added);
        // key が null の操作は必ず別の記録
        history.Push(live, null, T0.AddSeconds(3.3), out added);
        Assert.True(added);
        history.Push(live, null, T0.AddSeconds(3.4), out added);
        Assert.True(added);
    }

    [Fact]
    public void 上限を超えると古いものから捨て_記録するとやり直しは消える()
    {
        var live = Layouts("a");
        var history = new N3LayoutHistory();
        for (int i = 0; i < N3LayoutHistory.MaxEntries + 5; i++)
        {
            history.Push(live, null, T0.AddSeconds(i), out _);
            live[0].LineSpacePx = i + 1;
        }
        Assert.Equal(N3LayoutHistory.MaxEntries, history.UndoCount);

        history.Undo(live);
        Assert.Equal(1, history.RedoCount);
        history.Push(live, null, T0.AddHours(1), out _);
        Assert.Equal(0, history.RedoCount);
    }

    [Fact]
    public void 何も変わらなかった操作の記録は取り消せる()
    {
        var live = Layouts("a");
        var history = new N3LayoutHistory();
        history.Push(live, null, T0, out _);
        var nothing = history.Push(live, "a|行間", T0.AddSeconds(1), out _);
        history.Drop(nothing);
        Assert.Equal(1, history.UndoCount);
        // 最後の記録でなければ取り消さない
        history.Drop(nothing);
        Assert.Equal(1, history.UndoCount);
        // 取り消したあとは、同じ欄でも別の記録
        history.Push(live, "a|行間", T0.AddSeconds(1.1), out bool added);
        Assert.True(added);
    }

    [Fact]
    public void 名前の変更は戻すとき逆向きに_やり直すとき同じ向きに付け替える()
    {
        var live = Layouts("新しいレイアウト");
        var history = new N3LayoutHistory();
        var entry = history.Push(live, null, T0, out _);
        live[0].Name = "コーラス";
        entry.Renames.Add(("新しいレイアウト", "コーラス"));
        var second = history.Push(live, null, T0.AddSeconds(1), out _);
        live[0].Name = "コーラス右";
        second.Renames.Add(("コーラス", "コーラス右"));

        var undo1 = history.Undo(live)!;
        Assert.Equal(new[] { ("コーラス右", "コーラス") }, undo1.ReferenceChanges);
        Assert.Equal("コーラス", undo1.Layouts[0].Name);
        var undo2 = history.Undo(undo1.Layouts)!;
        Assert.Equal(new[] { ("コーラス", "新しいレイアウト") }, undo2.ReferenceChanges);
        Assert.Equal("新しいレイアウト", undo2.Layouts[0].Name);

        var redo1 = history.Redo(undo2.Layouts)!;
        Assert.Equal(new[] { ("新しいレイアウト", "コーラス") }, redo1.ReferenceChanges);
        Assert.Equal("コーラス", redo1.Layouts[0].Name);
        var redo2 = history.Redo(redo1.Layouts)!;
        Assert.Equal(new[] { ("コーラス", "コーラス右") }, redo2.ReferenceChanges);
        Assert.Equal("コーラス右", redo2.Layouts[0].Name);
    }

    [Fact]
    public void 履歴を捨てられ_一覧の署名は中身で変わる()
    {
        var live = Layouts("a", "b");
        var history = new N3LayoutHistory();
        history.Push(live, null, T0, out _);
        history.Undo(live);
        history.Clear();
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);

        string before = N3LayoutHistory.Signature(live);
        Assert.Equal(before, N3LayoutHistory.Signature(N3LayoutHistory.Copy(live)));
        live[1].HorizontalAlignments[0] = 1;
        Assert.NotEqual(before, N3LayoutHistory.Signature(live));
    }

    [Fact]
    public void 一覧の出どころ_ベースのまま_編集_新規と書き出しの並び()
    {
        var baseLayouts = N3LayoutReader.Defaults(1080);
        var edited = N3Layout.FromSettings(baseLayouts.First(l => l.Name == "下寄せ2行"));
        edited.LineSpacePx = -10;
        var added = new N3Layout { Name = "コーラス右", VerticalAlignment = 0, HorizontalAlignments = new List<int> { 2 } };
        var own = new List<N3Layout> { added, edited };

        var entries = N3LayoutLibrary.Entries(baseLayouts, own, merge: true);
        Assert.Equal(N3LayoutLibrary.Effective(baseLayouts, own, merge: true).Select(l => l.Name), entries.Select(e => e.Name));
        Assert.Equal(Enumerable.Range(0, entries.Count), entries.Select(e => e.Settings.Index));
        var two = entries.Single(e => e.Name == "下寄せ2行");
        Assert.Equal(N3LayoutOrigin.Edited, two.Origin);
        Assert.Same(edited, two.Own);
        Assert.Equal(-10, two.Settings.LineSpacePx);
        var chorus = entries.Last();
        Assert.Equal(("コーラス右", N3LayoutOrigin.Added), (chorus.Name, chorus.Origin));
        Assert.All(entries.Where(e => e.Name is not ("下寄せ2行" or "コーラス右")), e =>
        {
            Assert.Equal(N3LayoutOrigin.Base, e.Origin);
            Assert.Null(e.Own);
        });

        // 合わせない曲: 並びと値はベースのまま。編集したものは出どころと編集画面の値にだけ出る
        var plain = N3LayoutLibrary.Entries(baseLayouts, own, merge: false);
        Assert.Equal(baseLayouts.Select(l => l.Name), plain.Select(e => e.Name));
        var plainTwo = plain.Single(e => e.Name == "下寄せ2行");
        Assert.Equal(N3LayoutOrigin.Edited, plainTwo.Origin);
        Assert.Equal(baseLayouts.First(l => l.Name == "下寄せ2行").LineSpacePx, plainTwo.Settings.LineSpacePx);
        Assert.Equal(-10, plainTwo.EditingSettings.LineSpacePx);
        Assert.Equal(plainTwo.Settings.Index, plainTwo.EditingSettings.Index);
    }
}
