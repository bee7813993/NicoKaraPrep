using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;
using NicoKaraPrep.Core.Validation;

namespace NicoKaraPrep.Core.Tests;

public class PageRowCollisionValidatorTests
{
    private static LyricsDocument Doc(params string[] lines)
    {
        var doc = new LyricsDocument();
        foreach (string l in lines) doc.Lines.Add(LrcFormat.ParseLyricLine(l));
        return doc;
    }

    [Fact]
    public void 衝突なし()
    {
        // ページ1 は 10 秒で終了、ページ2 は 20 秒開始 → 余裕あり
        var doc = Doc(
            "[00:05:00]あ[00:10:00]",
            "",
            "[00:20:00]い[00:25:00]");
        var issues = PageRowCollisionValidator.Validate(doc, new PageCollisionSettings());
        Assert.Empty(issues);
    }

    [Fact]
    public void 下から同位置の行が重なるとエラー()
    {
        // 前ページ下段の表示終了 = 10:00 + 1秒 = 11:00
        // 次ページ下段の表示開始 = 11:50 - 2秒 = 09:50 → 重なり 1.1 秒 > 閾値 1 秒 → エラー
        var doc = Doc(
            "[00:01:00]前の上段[00:02:00]",
            "[00:05:00]あ[00:10:00]",
            "",
            "[00:30:00]次の上段[00:35:00]",
            "[00:11:50]い[00:20:00]");
        var settings = new PageCollisionSettings { DisplayLeadCs = 200, DisplayTailCs = 100, ErrorThresholdCs = 100 };
        var issues = PageRowCollisionValidator.Validate(doc, settings);
        Assert.Single(issues);
        Assert.Equal(IssueSeverity.Error, issues[0].Severity);
        Assert.Equal(4, issues[0].LineIndex);
    }

    [Fact]
    public void 重なりが閾値以下なら警告()
    {
        // 下段: 表示終了 11:00 vs 表示開始 10:50 → 重なり 0.5 秒 <= 閾値 1 秒 → 警告
        var doc = Doc(
            "[00:01:00]前の上段[00:02:00]",
            "[00:05:00]あ[00:10:00]",
            "",
            "[00:30:00]次の上段[00:35:00]",
            "[00:12:50]い[00:20:00]");
        var settings = new PageCollisionSettings { DisplayLeadCs = 200, DisplayTailCs = 100, ErrorThresholdCs = 100 };
        var issues = PageRowCollisionValidator.Validate(doc, settings);
        Assert.Single(issues);
        Assert.Equal(IssueSeverity.Warning, issues[0].Severity);
    }

    [Fact]
    public void 上からの位置合わせ_同じ行番号同士だけ比較()
    {
        // 2行目は「次ページの1行目」の表示開始と重なっていても正常。
        // 「次ページの2行目」の表示開始までに消えていればよい。
        var doc = Doc(
            "[00:01:00]1行目[00:05:00]",
            "[00:06:00]2行目[00:14:00]",   // 表示終了 14:00+0.5s = 14:50
            "",
            "[00:15:00]次の1行目[00:20:00]",  // 表示開始 15:00-1.5s = 13:50 ← 2行目と重なるが比較しない
            "[00:21:00]次の2行目[00:25:00]"); // 表示開始 21:00-1.5s = 19:50 ← こちらと比較して OK
        var settings = new PageCollisionSettings
        {
            AlignFromTop = true,
            DisplayLeadCs = 150,
            DisplayTailCs = 50,
            ErrorThresholdCs = 0,
        };
        var issues = PageRowCollisionValidator.Validate(doc, settings);
        Assert.Empty(issues);

        // 次の2行目が早すぎる場合は検出される
        var doc2 = Doc(
            "[00:01:00]1行目[00:05:00]",
            "[00:06:00]2行目[00:14:00]",
            "",
            "[00:15:00]次の1行目[00:20:00]",
            "[00:15:50]次の2行目[00:25:00]"); // 表示開始 14:00 < 2行目の表示終了 14:50
        var issues2 = PageRowCollisionValidator.Validate(doc2, settings);
        Assert.Single(issues2);
        Assert.Contains("上から2行目", issues2[0].Message);
        Assert.Equal(4, issues2[0].LineIndex);
        Assert.Equal(1, issues2[0].RelatedLineIndex);
    }

    [Fact]
    public void 下からの位置合わせ_設定で切替()
    {
        // 2行ページ同士: 下から対応付けでは下段同士・上段同士を比較
        var doc = Doc(
            "[00:01:00]上の行はずっと前[00:02:00]",
            "[00:05:00]下の行[00:10:00]",
            "",
            "[00:11:00]次の上の行[00:20:00]",
            "[00:21:00]次の下の行[00:25:00]");
        var settings = new PageCollisionSettings { AlignFromTop = false, DisplayLeadCs = 200, DisplayTailCs = 100, ErrorThresholdCs = 0 };
        var issues = PageRowCollisionValidator.Validate(doc, settings);
        // 上段: 02:00+1s=03:00 vs 11:00-2s=09:00 → OK
        // 下段: 10:00+1s=11:00 vs 21:00-2s=19:00 → OK … にならないよう下段を詰める
        Assert.Empty(issues);

        var doc2 = Doc(
            "[00:01:00]上の行はずっと前[00:02:00]",
            "[00:05:00]下の行[00:10:00]",
            "",
            "[00:11:00]次の上の行[00:20:00]",
            "[00:12:00]次の下の行[00:25:00]"); // 下段開始 12:00-2s=10:00 < 下の行の表示終了 11:00
        var issues2 = PageRowCollisionValidator.Validate(doc2, settings);
        Assert.Single(issues2);
        Assert.Contains("下から1行目", issues2[0].Message);
    }

    [Fact]
    public void 一行ページ_十分時間があれば下から2行目に昇格()
    {
        // ページ2 は 1 行のみ。次ページの上段の表示開始まで十分時間がある → 下から2行目扱い。
        // そのため前ページの「上段」（ずっと表示が残っている）と衝突として検出される。
        var doc = Doc(
            "[00:01:00]前の上段ずっと残る[00:14:00]",   // 表示終了 14:00+1s=15:00
            "[00:02:00]前の下段[00:05:00]",
            "",
            "[00:15:50]ソロの一行[00:20:00]",           // 表示開始 15:50-2s=13:50 ← 上段15:00と重なる
            "",
            "[00:40:00]次の上段[00:45:00]",             // ソロ行の表示終了 21:00 ≪ 38:00 → 昇格
            "[00:46:00]次の下段[00:50:00]");
        var settings = new PageCollisionSettings { AlignFromTop = false, DisplayLeadCs = 200, DisplayTailCs = 100, ErrorThresholdCs = 0 };
        var issues = PageRowCollisionValidator.Validate(doc, settings);
        Assert.Single(issues);
        Assert.Contains("下から2行目", issues[0].Message);
        Assert.Equal(3, issues[0].LineIndex);      // ソロの一行
        Assert.Equal(0, issues[0].RelatedLineIndex); // 前の上段

        // 前ページの「下段」とは比較されない（下段はソロ行の直前に終わっていても問題なし）
        Assert.DoesNotContain(issues, i => i.RelatedLineIndex == 1);
    }

    [Fact]
    public void 一行ページ_時間が足りなければ下から1行目のまま()
    {
        // ソロ行の表示終了と次ページ上段の表示開始が重なる → 昇格せず下から1行目扱い。
        // 前ページの「下段」との衝突が検出される。
        var doc = Doc(
            "[00:01:00]前の上段[00:03:00]",
            "[00:05:00]前の下段ずっと残る[00:14:00]",   // 表示終了 15:00
            "",
            "[00:15:50]ソロの一行[00:20:00]",           // 表示開始 13:50 ← 下段15:00と重なる / 表示終了 21:00
            "",
            "[00:22:00]次の上段[00:30:00]",             // 表示開始 20:00 < ソロ表示終了 21:00 → 昇格しない
            "[00:31:00]次の下段[00:35:00]");
        var settings = new PageCollisionSettings { AlignFromTop = false, DisplayLeadCs = 200, DisplayTailCs = 100, ErrorThresholdCs = 0 };
        var issues = PageRowCollisionValidator.Validate(doc, settings);
        Assert.Single(issues);
        Assert.Contains("下から1行目", issues[0].Message);
        Assert.Equal(3, issues[0].LineIndex);
        Assert.Equal(1, issues[0].RelatedLineIndex); // 前の下段
    }

    [Fact]
    public void 絵文字の先行タグは除外される()
    {
        // ★ = 絵文字。次ページ下段の ★ に付いた先行タグ [00:09:00] は無視され、
        // 実文字の [00:30:00] が表示開始の基準になる
        var doc = Doc(
            "[00:01:00]前の上段[00:02:00]",
            "[00:05:00]あ[00:10:00]",
            "",
            "[00:40:00]次の上段[00:45:00]",
            "[00:09:00]★[00:30:00]い[00:35:00]");
        var settings = new PageCollisionSettings
        {
            ExcludeChar = c => c.Text == "★",
        };
        var issues = PageRowCollisionValidator.Validate(doc, settings);
        Assert.Empty(issues);

        // 除外しなければ衝突する
        var issues2 = PageRowCollisionValidator.Validate(doc, new PageCollisionSettings());
        Assert.NotEmpty(issues2);
    }

    [Fact]
    public void 固定行数モード()
    {
        // 2行ごとにページ化（空行なし）
        var doc = Doc(
            "[00:01:00]1行目[00:02:00]",
            "[00:03:00]2行目[00:04:00]",
            "[00:04:50]3行目[00:06:00]",   // ページ2 の上段 ← 1行目(下から2番目)と比較
            "[00:07:00]4行目[00:08:00]");
        var settings = new PageCollisionSettings
        {
            PageMode = PageSplitMode.FixedLineCount,
            FixedLineCount = 2,
            DisplayLeadCs = 200,
            DisplayTailCs = 100,
            ErrorThresholdCs = 0,
        };
        var issues = PageRowCollisionValidator.Validate(doc, settings);
        // 1行目表示終了 02:00+1s=03:00 vs 3行目表示開始 04:50-2s=02:50 → 重なり0.1秒
        // 2行目表示終了 04:00+1s=05:00 vs 4行目表示開始 07:00-2s=05:00 → 重なりなし(0)
        Assert.Single(issues);
        Assert.Equal(2, issues[0].LineIndex);
    }
}

public class OverlapInfoDetectorTests
{
    [Fact]
    public void 行内逆行を検出()
    {
        var doc = new LyricsDocument();
        doc.Lines.Add(LrcFormat.ParseLyricLine("[00:10:00]あ[00:05:00]い[00:20:00]"));
        doc.Lines.Add(LrcFormat.ParseLyricLine("[00:30:00]う[00:40:00]"));
        var marked = OverlapInfoDetector.Detect(doc);
        Assert.Contains(0, marked);
        Assert.DoesNotContain(1, marked);
    }

    [Fact]
    public void 隣接行の重なりを検出()
    {
        var doc = new LyricsDocument();
        doc.Lines.Add(LrcFormat.ParseLyricLine("[00:10:00]パート1[00:20:00]"));
        doc.Lines.Add(LrcFormat.ParseLyricLine("[00:15:00]パート2[00:25:00]"));
        var marked = OverlapInfoDetector.Detect(doc);
        Assert.Contains(0, marked);
        Assert.Contains(1, marked);
    }
}

public class LineWidthValidatorTests
{
    /// <summary>1 文字 = フォントサイズ幅として計測するフェイク。</summary>
    private sealed class FakeMeasurer : ITextMeasurer
    {
        public double MeasureWidth(string text, string fontFamily, double fontSize, bool bold, bool italic)
        {
            int count = 0;
            for (int i = 0; i < text.Length; i++)
            {
                count++;
                if (char.IsHighSurrogate(text[i])) i++;
            }
            return count * fontSize;
        }
    }

    private static LyricsDocument Doc(string text)
    {
        var doc = new LyricsDocument();
        doc.Lines.Add(LrcFormat.ParseLyricLine(text));
        return doc;
    }

    [Fact]
    public void 収まる場合は問題なし()
    {
        // 10 文字 × 80px = 800px、有効幅 = 1920 × 0.9 = 1728px
        var doc = Doc("[00:01:00]あいうえおかきくけこ");
        var settings = new LineWidthSettings { ScreenWidthPx = 1920, FontSizePx = 80 };
        var results = LineWidthValidator.Measure(doc, settings, new FakeMeasurer());
        Assert.Null(results[0].Severity);
        Assert.Equal(800, results[0].WidthPx);
    }

    [Fact]
    public void マージン不足で警告()
    {
        // 22 文字 × 80px = 1760px > 有効幅 1728px、画面 1920px 以内 → 警告
        var doc = Doc("[00:01:00]" + new string('あ', 22));
        var settings = new LineWidthSettings { ScreenWidthPx = 1920, FontSizePx = 80, SideMarginPercent = 5 };
        var results = LineWidthValidator.Measure(doc, settings, new FakeMeasurer());
        Assert.Equal(IssueSeverity.Warning, results[0].Severity);
    }

    [Fact]
    public void はみ出しでエラー()
    {
        // 25 文字 × 80px = 2000px > 画面 1920px → エラー
        var doc = Doc("[00:01:00]" + new string('あ', 25));
        var settings = new LineWidthSettings { ScreenWidthPx = 1920, FontSizePx = 80 };
        var results = LineWidthValidator.Measure(doc, settings, new FakeMeasurer());
        Assert.Equal(IssueSeverity.Error, results[0].Severity);
    }

    [Fact]
    public void 絵文字はZoom付き正方形として加算()
    {
        var doc = Doc("[00:01:00]あ★い");
        var settings = new LineWidthSettings { ScreenWidthPx = 1920, FontSizePx = 100 };
        settings.EmojiChars.Add("★");
        settings.EmojiZoomPercent["★"] = 150;
        var results = LineWidthValidator.Measure(doc, settings, new FakeMeasurer());
        // あ+い = 200px、★ = 100 × 150% = 150px → 350px
        Assert.Equal(350, results[0].WidthPx);
    }

    [Fact]
    public void 絵文字の実寸幅が登録されていればそれを使う()
    {
        var doc = Doc("[00:01:00]あ（花帆）い");
        var settings = new LineWidthSettings { ScreenWidthPx = 1920, FontSizePx = 100 };
        settings.EmojiChars.Add("（花帆）");
        settings.EmojiWidthPx["（花帆）"] = 260; // 横長画像 + Margin の計算値
        var results = LineWidthValidator.Measure(doc, settings, new FakeMeasurer());
        // あ+い = 200px + アイコン 260px → 460px
        Assert.Equal(460, results[0].WidthPx);
    }
}

public class EmojiOptionsTests
{
    [Fact]
    public void オプション解析()
    {
        var e = new NicoKaraPrep.Core.Model.EmojiEntry
        {
            ReplaceChar = "（花帆）",
            ImageBefore = "a.png",
            Options = "NoDecor,Zoom=150,MarginRight=20,MarginBottom=-10",
        };
        var o = e.ParseOptions();
        Assert.Equal(150, o.ZoomPercent);
        Assert.True(o.NoDecor);
        Assert.False(o.Fix);
        Assert.Equal(20, o.MarginRight);
        Assert.Equal(-10, o.MarginBottom);
        Assert.Equal(0, o.MarginLeft);
    }

    [Fact]
    public void アイコン実寸から幅を計算()
    {
        // 200×100 の PNG（縦横比 2:1）を作る
        string dir = Path.Combine(Path.GetTempPath(), "ttt_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string png = Path.Combine(dir, "wide.png");
        WriteMinimalPng(png, 200, 100);

        try
        {
            Assert.True(NicoKaraPrep.Core.Formats.ImageSizeReader.TryGetSize(png, out int w, out int h));
            Assert.Equal(200, w);
            Assert.Equal(100, h);

            var settings = new NicoKaraPrep.Core.Project.AppSettings { FontSizePx = 80, EdgeSizePx = 10 };
            var emoji = new[]
            {
                new NicoKaraPrep.Core.Model.EmojiEntry
                {
                    ReplaceChar = "（幅広）",
                    ImageBefore = png,
                    Options = "Zoom=150,MarginRight=20",
                },
            };
            var s = settings.ToLineWidthSettings(emoji);
            // 高さ = フォントサイズ 80 × 150% = 120px（縁取りは Zoom 基準に含まれない）。
            // 幅 = 120 × (200/100) = 240px、+MarginRight 20 → 260px
            Assert.Equal(260, s.EmojiWidthPx["（幅広）"], 3);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>IHDR だけ正しい最小限の PNG ヘッダを書き出す（サイズ読み取りテスト用）。</summary>
    private static void WriteMinimalPng(string path, int width, int height)
    {
        using var fs = File.Create(path);
        fs.Write(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A });
        fs.Write(new byte[] { 0, 0, 0, 13 });                       // IHDR 長
        fs.Write("IHDR"u8);
        fs.Write(new byte[] { (byte)(width >> 24), (byte)(width >> 16), (byte)(width >> 8), (byte)width });
        fs.Write(new byte[] { (byte)(height >> 24), (byte)(height >> 16), (byte)(height >> 8), (byte)height });
        fs.Write(new byte[] { 8, 6, 0, 0, 0 });                     // bit depth ほか
        fs.Write(new byte[] { 0, 0, 0, 0 });                        // CRC（読み取りには不要）
    }
}
