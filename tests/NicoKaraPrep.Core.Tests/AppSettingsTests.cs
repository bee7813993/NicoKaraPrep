using NicoKaraPrep.Core.Model;
using NicoKaraPrep.Core.Project;

namespace NicoKaraPrep.Core.Tests;

public class AppSettingsTests
{
    /// <summary>一時フォルダを作って処理を実行し、終わったら消す。</summary>
    private static void InTempDir(Action<string> action)
    {
        string dir = Path.Combine(Path.GetTempPath(), "NicoKaraPrepTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            action(dir);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private const string BrokenJson = "{ \"FixedLineCount\": 3, \"GlobalEmojiList\": [ { \"ReplaceChar\": \"（花帆）\" ";

    // ------------------------------------------------------------ 読み込みの保護

    [Fact]
    public void 読み込み_壊れた設定ファイルは既定値で読み込み保存を止める()
    {
        InTempDir(dir =>
        {
            string path = Path.Combine(dir, "settings.json");
            File.WriteAllText(path, BrokenJson);

            var settings = AppSettings.Load(path);
            Assert.True(settings.LoadFailed);
            Assert.Equal(2, settings.FixedLineCount); // 既定値
            Assert.NotEqual("", settings.LoadError);

            // 壊れたファイルの写しが残る
            Assert.NotNull(settings.BrokenCopyPath);
            Assert.StartsWith("settings.json.broken-", Path.GetFileName(settings.BrokenCopyPath));
            Assert.Equal(BrokenJson, File.ReadAllText(settings.BrokenCopyPath!));
            Assert.Contains(settings.BrokenCopyPath!, settings.LoadFailureMessage);

            // 保存しても元のファイルは上書きされない
            settings.FixedLineCount = 5;
            settings.Save(path);
            Assert.Equal(BrokenJson, File.ReadAllText(path));
        });
    }

    [Fact]
    public void 読み込み_同じ内容の壊れたファイルは写しを増やさない()
    {
        InTempDir(dir =>
        {
            string path = Path.Combine(dir, "settings.json");
            File.WriteAllText(path, BrokenJson);

            var first = AppSettings.Load(path);
            var second = AppSettings.Load(path);
            Assert.Equal(first.BrokenCopyPath, second.BrokenCopyPath);
            Assert.Single(Directory.GetFiles(dir, "settings.json.broken-*"));
        });
    }

    [Fact]
    public void 読み込み_設定が入っていないファイルも読み込み失敗として扱う()
    {
        InTempDir(dir =>
        {
            string path = Path.Combine(dir, "settings.json");
            File.WriteAllText(path, "null");

            var settings = AppSettings.Load(path);
            Assert.True(settings.LoadFailed);
            settings.Save(path);
            Assert.Equal("null", File.ReadAllText(path));
        });
    }

    [Fact]
    public void 読み込み_正常な設定ファイルは今までどおり読み書きできる()
    {
        InTempDir(dir =>
        {
            string path = Path.Combine(dir, "settings.json");
            var original = new AppSettings { FixedLineCount = 3, PlaceholderChar = "◇" };
            original.GlobalEmojiList.Add(new EmojiEntry { ReplaceChar = "（花帆）", ImageBefore = "k.png" });
            original.N3FontSets.Add(new N3FontSet { Name = "（花帆）", SizePx = 72 });
            original.Save(path);

            // 読み込み状態は JSON に書かない
            Assert.DoesNotContain(nameof(AppSettings.LoadFailed), File.ReadAllText(path));

            var loaded = AppSettings.Load(path);
            Assert.False(loaded.LoadFailed);
            Assert.Null(loaded.BrokenCopyPath);
            Assert.Null(loaded.LoadFailureMessage);
            Assert.Equal(3, loaded.FixedLineCount);
            Assert.Equal("◇", loaded.PlaceholderChar);
            Assert.Equal("（花帆）", Assert.Single(loaded.GlobalEmojiList).ReplaceChar);
            Assert.Equal(72, Assert.Single(loaded.N3FontSets).SizePx);
            Assert.Empty(Directory.GetFiles(dir, "settings.json.broken-*"));

            // 読み込んだ設定は保存できる
            loaded.FixedLineCount = 4;
            loaded.Save(path);
            Assert.Equal(4, AppSettings.Load(path).FixedLineCount);
        });
    }

    [Fact]
    public void 厳密な読み込み_無いファイルと壊れたファイルを区別する()
    {
        InTempDir(dir =>
        {
            string missing = Path.Combine(dir, "無い.tttpl");
            Assert.Throws<FileNotFoundException>(() => AppSettings.LoadStrict(missing));

            string broken = Path.Combine(dir, "壊れた.tttpl");
            File.WriteAllText(broken, BrokenJson);
            var ex = Assert.Throws<InvalidDataException>(() => AppSettings.LoadStrict(broken));
            Assert.Contains("壊れた.tttpl", ex.Message);
            // 厳密な読み込みでは写しを作らない
            Assert.Empty(Directory.GetFiles(dir, "*.broken-*"));

            string ok = Path.Combine(dir, "正常.tttpl");
            new AppSettings { FixedLineCount = 3 }.Save(ok);
            Assert.Equal(3, AppSettings.LoadStrict(ok).FixedLineCount);
        });
    }

    // ------------------------------------------------------------ テンプレート

    [Fact]
    public void テンプレート_取り込みはフォント設定を変えない()
    {
        var settings = new AppSettings();
        settings.N3FontSets.Add(new N3FontSet { Name = "（花帆）", SizePx = 72 });
        var fonts = settings.N3FontSets;

        var template = new AppSettings { FixedLineCount = 3, FontSizePx = 60 };
        template.N3FontSets.Add(new N3FontSet { Name = "テンプレートのフォント" });
        settings.CopyFrom(template);

        Assert.Equal(3, settings.FixedLineCount);
        Assert.Equal(60, settings.FontSizePx);
        Assert.Same(fonts, settings.N3FontSets);
        Assert.Equal("（花帆）", Assert.Single(settings.N3FontSets).Name);
    }

    [Fact]
    public void テンプレート_写しにはフォント設定が入らず古いテンプレートのフォント設定は無視される()
    {
        InTempDir(dir =>
        {
            var settings = new AppSettings { FixedLineCount = 3 };
            settings.N3FontSets.Add(new N3FontSet { Name = "（花帆）" });

            // テンプレートの保存と同じ手順（新しい設定へ取り込む）で作った写しにはフォント設定が入らない
            var snapshot = new AppSettings();
            snapshot.CopyFrom(settings);
            Assert.Equal(3, snapshot.FixedLineCount);
            Assert.Empty(snapshot.N3FontSets);

            // フォント設定を含む古いテンプレートを適用しても、ライブラリは置き換わらない
            string path = Path.Combine(dir, "古い.tttpl");
            var old = new AppSettings { FixedLineCount = 4 };
            old.N3FontSets.Add(new N3FontSet { Name = "古いフォント" });
            old.Save(path);
            settings.CopyFrom(AppSettings.LoadStrict(path));
            Assert.Equal(4, settings.FixedLineCount);
            Assert.Equal("（花帆）", Assert.Single(settings.N3FontSets).Name);
        });
    }
}
