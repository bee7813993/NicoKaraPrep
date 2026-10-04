using NicoKaraPrep.Core.Model;
using NicoKaraPrep.Core.Project;

namespace NicoKaraPrep.Core.Tests;

public class N3FontBackupTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "NicoKaraPrepTests", "FontBackups_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static N3FontBackup Sample()
    {
        var fonts = new List<N3FontSet> { new() { Name = "（花帆）" }, new() { Name = "（さやか）" } };
        var folder = N3FontTreeNode.ForFolder("蓮ノ空");
        folder.Children.Add(N3FontTreeNode.ForFont(fonts[0].Id));
        folder.Children.Add(N3FontTreeNode.ForFont(fonts[1].Id));
        return new N3FontBackup
        {
            CommonFonts = fonts,
            Hierarchy = new List<N3FontTreeNode> { folder },
            SongFonts = new List<N3FontSet> { new() { Name = "（コーラス）" } },
            SongPath = @"C:\曲\Dream Believers.rlf",
            UserColorPatterns = new List<N3ColorPattern> { new() { Name = "自作", Roles = new List<string> { "メイン色" } } },
            DefaultColorPatternId = "自作の id",
            RecentColors = new List<string> { "66C5EC", "88D66E@50" },
        };
    }

    [Fact]
    public void 保存して読む_持っている種類と階層がそのまま戻る()
    {
        var backup = Sample();
        string path = backup.Save(_folder);
        Assert.StartsWith("フォント設定の控え_", Path.GetFileName(path));

        var loaded = N3FontBackup.Load(path);
        Assert.NotNull(loaded);
        Assert.Equal(new[] { "（花帆）", "（さやか）" }, loaded!.CommonFonts!.Select(f => f.Name));
        Assert.Equal(backup.CommonFonts![1].Id, loaded.CommonFonts![1].Id);
        var root = Assert.Single(loaded.Hierarchy!);
        Assert.Equal("蓮ノ空", root.FolderName);
        Assert.Equal(backup.CommonFonts.Select(f => f.Id), root.Children.Select(c => c.FontId));
        Assert.Equal("（コーラス）", Assert.Single(loaded.SongFonts!).Name);
        Assert.Equal(@"C:\曲\Dream Believers.rlf", loaded.SongPath);
        Assert.Equal("自作", Assert.Single(loaded.UserColorPatterns!).Name);
        Assert.Equal("自作の id", loaded.DefaultColorPatternId);
        Assert.Equal(new[] { "66C5EC", "88D66E@50" }, loaded.RecentColors);
        Assert.Equal(backup.CreatedUtc, loaded.CreatedUtc);
    }

    [Fact]
    public void 説明_持っている種類と数_フォルダの数と曲の名前()
    {
        Assert.Equal("アプリ共通 2 件（フォルダ 1 個）・この曲専用 1 件（Dream Believers.rlf）・自作の配色パターン 1 件・最近使った色 2 色", Sample().Describe());
        Assert.Equal("最近使った色 0 色", new N3FontBackup { RecentColors = new List<string>() }.Describe());
        Assert.Equal("（空）", new N3FontBackup().Describe());
    }

    [Fact]
    public void 消していない種類はnull_空の控えは読まない()
    {
        var empty = new N3FontBackup();
        Assert.True(empty.IsEmpty);
        Assert.Null(N3FontBackup.Load(empty.Save(_folder)));

        // 消した種類が空でも（最近使った色が 0 色）、消した記録として読む
        var onlyRecent = new N3FontBackup { RecentColors = new List<string>() };
        Assert.False(onlyRecent.IsEmpty);
        var loaded = N3FontBackup.Load(onlyRecent.Save(_folder));
        Assert.NotNull(loaded);
        Assert.Null(loaded!.CommonFonts);
        Assert.Null(loaded.SongFonts);
        Assert.Null(loaded.UserColorPatterns);
        Assert.Empty(loaded.RecentColors!);
    }

    [Fact]
    public void 一覧_新しい順_同じ時刻でも別のファイル_読めないファイルは除く()
    {
        var at = new DateTime(2026, 10, 4, 6, 0, 0, DateTimeKind.Utc);
        var older = Sample();
        older.CreatedUtc = at;
        var newer = new N3FontBackup { CreatedUtc = at.AddMinutes(5), RecentColors = new List<string> { "FFFFFF" } };
        var same = new N3FontBackup { CreatedUtc = at, RecentColors = new List<string>() };
        string p1 = older.Save(_folder);
        string p2 = newer.Save(_folder);
        string p3 = same.Save(_folder);
        Assert.NotEqual(p1, p3);
        File.WriteAllText(Path.Combine(_folder, "壊れた.json"), "{ 壊れた");

        var list = N3FontBackup.List(_folder);
        Assert.Equal(3, list.Count);
        Assert.Equal(p2, list[0].Path);
        Assert.Contains(list, x => x.Path == p1);
        Assert.Contains(list, x => x.Path == p3);

        Assert.Empty(N3FontBackup.List(Path.Combine(_folder, "無いフォルダ")));
    }
}
