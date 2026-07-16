using System.Text.Json;
using System.Text.Json.Serialization;
using NicoKaraPrep.Core.Model;
using NicoKaraPrep.Core.Validation;

namespace NicoKaraPrep.Core.Project;

/// <summary>
/// アプリ全体の設定（%APPDATA%\NicoKaraPrep\settings.json に保存）。
/// </summary>
public sealed class AppSettings
{
    // ---- ページ衝突チェック ----
    public PageSplitMode PageMode { get; set; } = PageSplitMode.EmptyLine;
    public int FixedLineCount { get; set; } = 2;

    /// <summary>行の表示開始 = 先頭タグの何秒前か（ニコカラメーカー側の設定に合わせる）。</summary>
    public double DisplayLeadSeconds { get; set; } = 1.5;

    /// <summary>行の表示終了 = 最終タグの何秒後か。</summary>
    public double DisplayTailSeconds { get; set; } = 0.5;

    /// <summary>ページ衝突の行対応付け。true: 上から同じ行番号同士 / false: 下から（デフォルト）。</summary>
    public bool CollisionAlignFromTop { get; set; }

    /// <summary>この重なり秒数を超えたらエラー（以下は警告）。</summary>
    public double CollisionErrorThresholdSeconds { get; set; } = 1.0;

    // ---- 横幅チェック ----
    public int ScreenWidthPx { get; set; } = 1920;
    public string FontFamily { get; set; } = "メイリオ";
    public double FontSizePx { get; set; } = 80;
    public bool FontBold { get; set; } = true;
    public bool FontItalic { get; set; }
    public double SideMarginPercent { get; set; } = 5.0;

    // ---- @Emoji ----
    /// <summary>絵文字挿入時の先行タグ秒数（開始時刻 = 直後の実文字の時刻 − この秒数）。</summary>
    public double EmojiLeadSeconds { get; set; } = 2.0;

    /// <summary>true: 連続絵文字それぞれにタグを付与 / false: ブロックの先頭と末尾のみ。</summary>
    public bool EmojiTagPerEmoji { get; set; } = true;

    /// <summary>
    /// プレースホルダ文字（アイコンなしでアイコン同等のタイムタグを付けて挿入する文字）。
    /// 挿入ビューの Space キーで挿入される。
    /// </summary>
    public string PlaceholderChar { get; set; } = "＿";

    /// <summary>グローバルの @Emoji リスト（スロット 1–20）。</summary>
    public List<EmojiEntry> GlobalEmojiList { get; set; } = new();

    // ---- メディア再生 ----
    /// <summary>Z / X（および Ctrl+←/→）でシークする秒数。</summary>
    public double SeekSeconds { get; set; } = 3.0;

    /// <summary>メディアプレイヤーの表示高さ px（ドラッグハンドルで変更）。</summary>
    public double PlayerHeightPx { get; set; } = 260;

    /// <summary>前回ファイルを保存（エクスポート）したフォルダ。</summary>
    public string LastSaveFolder { get; set; } = "";

    // ---- ウィンドウ位置・サイズ（前回終了時の状態を復元） ----
    public int WindowX { get; set; }
    public int WindowY { get; set; }
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    // ------------------------------------------------------------ 変換

    [JsonIgnore]
    public int DisplayLeadCs => (int)Math.Round(DisplayLeadSeconds * 100);

    [JsonIgnore]
    public int DisplayTailCs => (int)Math.Round(DisplayTailSeconds * 100);

    [JsonIgnore]
    public int CollisionErrorThresholdCs => (int)Math.Round(CollisionErrorThresholdSeconds * 100);

    [JsonIgnore]
    public int EmojiLeadCs => (int)Math.Round(EmojiLeadSeconds * 100);

    public PageCollisionSettings ToCollisionSettings(Func<CharUnit, bool>? excludeChar) => new()
    {
        PageMode = PageMode,
        FixedLineCount = FixedLineCount,
        DisplayLeadCs = DisplayLeadCs,
        DisplayTailCs = DisplayTailCs,
        ErrorThresholdCs = CollisionErrorThresholdCs,
        AlignFromTop = CollisionAlignFromTop,
        ExcludeChar = excludeChar,
    };

    /// <param name="effectiveEmoji">実効 @Emoji リスト。</param>
    /// <param name="baseDir">相対画像パスの基準フォルダ（歌詞ファイルのフォルダ）。</param>
    public LineWidthSettings ToLineWidthSettings(IEnumerable<EmojiEntry> effectiveEmoji, string? baseDir = null)
    {
        var s = new LineWidthSettings
        {
            ScreenWidthPx = ScreenWidthPx,
            FontFamily = FontFamily,
            FontSizePx = FontSizePx,
            Bold = FontBold,
            Italic = FontItalic,
            SideMarginPercent = SideMarginPercent,
        };
        foreach (var e in effectiveEmoji)
        {
            if (string.IsNullOrEmpty(e.ReplaceChar)) continue;
            s.EmojiChars.Add(e.ReplaceChar);

            var opts = e.ParseOptions();
            s.EmojiZoomPercent[e.ReplaceChar] = opts.ZoomPercent;

            // アイコン表示幅を画像実寸から計算:
            //   通常: 高さ = フォントサイズ × Zoom%、幅 = 高さ × 画像の縦横比
            //   Fix : 画像のピクセルサイズをそのまま使用
            //   左右 Margin を加算
            string imagePath = e.ImageBefore;
            if (!string.IsNullOrEmpty(imagePath) && !Path.IsPathRooted(imagePath) && baseDir is not null)
            {
                imagePath = Path.Combine(baseDir, imagePath);
            }
            double width;
            if (Formats.ImageSizeReader.TryGetSize(imagePath, out int imgW, out int imgH) && imgH > 0)
            {
                width = opts.Fix
                    ? imgW
                    : FontSizePx * opts.ZoomPercent / 100.0 * imgW / imgH;
            }
            else
            {
                width = FontSizePx * opts.ZoomPercent / 100.0; // 画像が読めない場合は正方形近似
            }
            s.EmojiWidthPx[e.ReplaceChar] = width + Math.Max(0, opts.MarginLeft) + Math.Max(0, opts.MarginRight);
        }
        return s;
    }

    /// <summary>@Emoji オプション文字列から Zoom=n% を取り出す。</summary>
    public static bool TryParseZoom(string? options, out double zoom)
    {
        zoom = 100;
        if (string.IsNullOrEmpty(options)) return false;
        foreach (string part in options.Split(','))
        {
            string p = part.Trim();
            if (p.StartsWith("Zoom=", StringComparison.OrdinalIgnoreCase))
            {
                string v = p[5..].TrimEnd('%');
                if (double.TryParse(v, out double z) && z > 0)
                {
                    zoom = z;
                    return true;
                }
            }
        }
        return false;
    }

    // ------------------------------------------------------------ 永続化

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NicoKaraPrep", "settings.json");

    /// <summary>別の設定（テンプレート）の内容をこのインスタンスへ取り込む。</summary>
    public void CopyFrom(AppSettings other)
    {
        PageMode = other.PageMode;
        FixedLineCount = other.FixedLineCount;
        DisplayLeadSeconds = other.DisplayLeadSeconds;
        DisplayTailSeconds = other.DisplayTailSeconds;
        CollisionAlignFromTop = other.CollisionAlignFromTop;
        CollisionErrorThresholdSeconds = other.CollisionErrorThresholdSeconds;
        ScreenWidthPx = other.ScreenWidthPx;
        FontFamily = other.FontFamily;
        FontSizePx = other.FontSizePx;
        FontBold = other.FontBold;
        FontItalic = other.FontItalic;
        SideMarginPercent = other.SideMarginPercent;
        EmojiLeadSeconds = other.EmojiLeadSeconds;
        EmojiTagPerEmoji = other.EmojiTagPerEmoji;
        PlaceholderChar = other.PlaceholderChar;
        SeekSeconds = other.SeekSeconds;
        GlobalEmojiList = other.GlobalEmojiList.Select(e => e.Clone()).ToList();
    }

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            MigrateLegacySettings(path);
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception)
        {
            // 壊れた設定ファイルはデフォルトで起動する
        }
        return new AppSettings();
    }

    /// <summary>旧称（TimeTagTool）時代の設定フォルダから設定を引き継ぐ。</summary>
    private static void MigrateLegacySettings(string newPath)
    {
        if (File.Exists(newPath)) return;
        string legacy = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TimeTagTool", "settings.json");
        if (!File.Exists(legacy)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
        File.Copy(legacy, newPath);
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }
}
