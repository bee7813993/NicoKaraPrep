using System.Text.Json;
using System.Text.Json.Serialization;
using NicoKaraPrep.Core.Model;
using NicoKaraPrep.Core.Validation;

namespace NicoKaraPrep.Core.Project;

/// <summary>メディア再生の置き場所（<see cref="AppSettings.PlayerPlacement"/>）。</summary>
public enum PlayerPlacementMode
{
    /// <summary>窓が低いとき（FHD 以下の高さ）は右の列、そうでなければ上の段。</summary>
    Auto,

    /// <summary>上の段の横いっぱい。</summary>
    Top,

    /// <summary>右の列（右のパネルの上）。</summary>
    Right,
}

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

    // ---- 字幕の既定（ベースの n3proj が無いときの画面の横幅、ニコカラメーカー3 のフォント設定が当たらない行のフォント） ----
    public int ScreenWidthPx { get; set; } = 1920;
    public string FontFamily { get; set; } = "メイリオ";
    public double FontSizePx { get; set; } = 80;
    public bool FontBold { get; set; } = true;
    public bool FontItalic { get; set; }

    /// <summary>
    /// 縁取りサイズ px（片側）。実機レンダリングの実測より、アイコンは
    /// 「文字セル下端＋縁取り」の位置に下端が揃うため、プレビューの縦位置に使用する
    /// （Zoom のサイズ基準には影響しない）。n3proj 読み込みで自動設定される。
    /// </summary>
    public double EdgeSizePx { get; set; }

    // ---- @Emoji ----
    /// <summary>絵文字挿入時の先行タグ秒数（開始時刻 = 直後の実文字の時刻 − この秒数）。</summary>
    public double EmojiLeadSeconds { get; set; } = 2.0;

    /// <summary>「0 秒」トグルを解除したときに戻す表示秒数（最後に使っていた 0 より大きい値）。</summary>
    public double EmojiLeadResumeSeconds { get; set; } = 2.0;

    /// <summary>true: 連続絵文字それぞれにタグを付与 / false: ブロックの先頭と末尾のみ。</summary>
    public bool EmojiTagPerEmoji { get; set; } = true;

    /// <summary>
    /// プレースホルダ文字（アイコンなしでアイコン同等のタイムタグを付けて挿入する文字）。
    /// 挿入ビューの Space キーで挿入される。
    /// </summary>
    public string PlaceholderChar { get; set; } = "＿";

    /// <summary>グローバルの @Emoji リスト（スロット 1–20）。</summary>
    public List<EmojiEntry> GlobalEmojiList { get; set; } = new();

    /// <summary>
    /// 挿入ビューの機能キー割り当て（機能 ID → キー ID）。
    /// 既定値・正規化はアプリ側（InsertViewKeyMap）が担当する。
    /// </summary>
    public Dictionary<string, string> InsertViewKeys { get; set; } = new();

    // ---- ニコカラメーカー3 プロジェクト書き出し ----
    /// <summary>ページ区切り／段落区切りの判定に使う「歌詞の表示間隔」（秒。ニコカラメーカーの IntervalTime）。</summary>
    public double N3IntervalSeconds { get; set; } = 0.3;

    /// <summary>
    /// 前ページの行を短縮するとき、最終タグの後に最低限残す秒数
    /// （0 = 自動: 表示前・表示後秒数の小さい方の半分。ニコカラメーカーの ProtectTime 相当）。
    /// </summary>
    public double N3ProtectSeconds { get; set; }

    /// <summary>上段の行をページの最終行が消えるまで表示する（ニコカラメーカーの「上段歌詞を長めに表示する」相当）。</summary>
    public bool N3TopLong { get; set; }

    /// <summary>
    /// 前後のページの同じ段の行を重ねてよい秒数（0 = 重ねない = ニコカラメーカー3 と同じ計算。NicoKaraPrep の機能）。
    /// 字幕アクションが「文字単位フェード」のときなど、少しの重なりなら見づらくない場合に使う
    /// （<see cref="Formats.N3ShowTimeSettings.OverlapMs"/>・ページ衝突チェックの <see cref="Validation.PageCollisionSettings.AllowedOverlapCs"/>）。
    /// </summary>
    public double N3OverlapSeconds { get; set; }

    /// <summary>
    /// 前のページの同じ段の行と重なるときは、絵文字（＿を含む）の先行の分だけ次の行の表示を遅らせる
    /// （ニコカラメーカー3 には無い NicoKaraPrep の機能。<see cref="Formats.N3ShowTimeSettings.EmojiLeadYield"/>）。
    /// 遅らせた行の絵文字の表示秒数は、書き出しで縮める。
    /// </summary>
    public bool N3EmojiLeadYield { get; set; } = true;

    /// <summary>
    /// 前後のページの同じ段の行でも、レイアウトで画面の上下の位置が違えば（字幕のプレビューで重ならなければ）、重なるものとして扱わない
    /// （表示時刻の計算・ページ衝突と表示時刻のチェック。ニコカラメーカー3 には無い NicoKaraPrep の機能。
    /// ニコカラメーカー3 は段だけで組にするので、上寄せ 5 行のページの次の下寄せ 2 行のページなども重なるとみなす）。
    /// </summary>
    public bool N3LayoutAwareRows { get; set; } = true;

    /// <summary>
    /// 歌詞ファイルを開いたとき、同じフォルダに n3proj が 1 つだけあれば
    /// 「字幕フォントと画面サイズ」「実際の表示区間」を自動で読み込む。
    /// </summary>
    public bool N3AutoImportNearby { get; set; } = true;

    /// <summary>最後に使ったベース n3proj（曲ごとの指定が無いときの既定）。</summary>
    public string N3LastBasePath { get; set; } = "";

    /// <summary>NicoKaraPrep 側で定義するニコカラメーカー3 のフォント設定（テンプレートには含めない。<see cref="CopyFrom"/> 参照）。</summary>
    public List<N3FontSet> N3FontSets { get; set; } = new();

    /// <summary>
    /// アプリ共通のフォント設定の階層（フォルダと並び。<see cref="N3FontTreeNode"/> の一覧 = 最上位）。
    /// null はまだ作っていない（フォント設定ビューを開いたときに、全部を最上位に今の順で並べて作る）。
    /// テンプレートには含めない（<see cref="CopyFrom"/> 参照）。
    /// </summary>
    public List<N3FontTreeNode>? N3FontHierarchy { get; set; }

    /// <summary>ユーザーが作った配色パターン（標準のパターンは <see cref="N3ColorPatterns.BuiltIns"/>。テンプレートには含めない）。</summary>
    public List<N3ColorPattern> N3UserColorPatterns { get; set; } = new();

    /// <summary>新しいフォント設定に使う配色パターンの識別子（<see cref="N3ColorPatterns.NoneId"/> は使わない）。</summary>
    public string N3DefaultColorPatternId { get; set; } = N3ColorPatterns.CharaInverseId;

    /// <summary>絵文字を 2 種類以上続けて入れたとき、その組み合わせのフォント設定（2 人用など）を自動で作るか。</summary>
    public bool N3AutoComposeFonts { get; set; } = true;

    /// <summary>組み合わせフォントの塗りの種類の既定（ミルフィーユかグラデーション）。</summary>
    public int N3ComposeBrushType { get; set; } = N3Brush.TypeMilleFeuille;

    /// <summary>組み合わせフォントの、端の帯の広さの既定（%。端の帯を中央の帯より何 % 広くするか）。</summary>
    public double N3ComposeEndWidenPercent { get; set; } = N3FontComposer.DefaultEndWidenPercent;

    /// <summary>
    /// フォント設定ビューの最近使った色（ColorPicker・16 進・不透明度で指定した色。新しい順。<see cref="N3RecentColors"/> の形式）。
    /// テンプレートには含めない。
    /// </summary>
    public List<string> N3RecentColorHistory { get; set; } = new();

    // ---- メディア再生 ----
    /// <summary>Z / X（および Ctrl+←/→）でシークする秒数。</summary>
    public double SeekSeconds { get; set; } = 3.0;

    /// <summary>
    /// メディアプレイヤーの表示高さ px（ドラッグハンドルで変更）。ウィンドウが低くて行リストが見えなくなるときは、
    /// 表示ではこれより縮める（この値は変えない）。
    /// </summary>
    public double PlayerHeightPx { get; set; } = 260;

    /// <summary>
    /// NicoKaraPrep で編集したニコカラメーカー3 のレイアウト設定（アプリ共通）。書き出しでは、ベースの n3proj の同じ名前のレイアウト設定に上書きし、
    /// 無い名前は足す。字幕のプレビューと行リストのレイアウトの表示も同じ合わせ方で使う。
    /// </summary>
    public List<N3Layout> N3Layouts { get; set; } = new();

    /// <summary>行リストの歌詞を字幕の見た目（フォント設定の書体・配色・ルビ・アイコン）で描くか。</summary>
    public bool LineListStyledLyrics { get; set; } = true;

    /// <summary>メディア再生パネルの動画の上に、字幕のプレビューを重ねるか。</summary>
    public bool PlayerSubtitlePreview { get; set; } = true;

    /// <summary>フォント設定ビューの左の一覧の幅 px（一覧と編集欄のあいだのつまみで変更）。</summary>
    public double FontListWidthPx { get; set; } = 300;

    /// <summary>
    /// 行リスト・絵文字挿入ビューの右のパネル（フォント・レイアウト、絵文字のパレット）の幅 px（左との境のつまみで変更）。
    /// ウィンドウが狭くて左が狭くなりすぎるときは、表示ではこれより狭める（この値は変えない）。
    /// </summary>
    public double SidePanelWidthPx { get; set; } = 300;

    /// <summary>行リスト・絵文字挿入ビューの右のパネルを出すか（表示メニューで切り替え）。</summary>
    public bool SidePanelVisible { get; set; } = true;

    /// <summary>
    /// メディア再生の置き場所（表示メニューで切り替え）。右の列（右のパネルの上）に置くと行リストが縦いっぱいになるので、
    /// FHD のような低い画面で行を多く見られる（プレイヤーの高さは列の幅から決める）。既定は自動（窓が低いときだけ右の列）。
    /// </summary>
    public PlayerPlacementMode PlayerPlacement { get; set; } = PlayerPlacementMode.Auto;

    /// <summary>メディア再生を右の列に置くときの、右の列の幅 px（<see cref="SidePanelWidthPx"/> とは別に覚える）。</summary>
    public double SidePlayerWidthPx { get; set; } = 560;

    /// <summary>前回ファイルを保存（エクスポート）したフォルダ。</summary>
    public string LastSaveFolder { get; set; } = "";

    /// <summary>最近使用したファイル（新しい順、最大 10 件）。</summary>
    public List<string> RecentFiles { get; set; } = new();

    /// <summary>最近使用したファイルの先頭に追加する（重複除去・最大 10 件）。</summary>
    public void AddRecentFile(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > 10)
        {
            RecentFiles.RemoveRange(10, RecentFiles.Count - 10);
        }
    }

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
        AllowedOverlapCs = Math.Max(0, (int)Math.Round(N3OverlapSeconds * 100)),
        AlignFromTop = CollisionAlignFromTop,
        ExcludeChar = excludeChar,
    };

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

    /// <summary>別の設定（テンプレート）の内容をこのインスタンスへ取り込む（ニコカラメーカー3 のフォント設定は除く）。</summary>
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
        EdgeSizePx = other.EdgeSizePx;
        EmojiLeadSeconds = other.EmojiLeadSeconds;
        EmojiLeadResumeSeconds = other.EmojiLeadResumeSeconds;
        EmojiTagPerEmoji = other.EmojiTagPerEmoji;
        PlaceholderChar = other.PlaceholderChar;
        SeekSeconds = other.SeekSeconds;
        GlobalEmojiList = other.GlobalEmojiList.Select(e => e.Clone()).ToList();
        N3IntervalSeconds = other.N3IntervalSeconds;
        N3ProtectSeconds = other.N3ProtectSeconds;
        N3TopLong = other.N3TopLong;
        N3OverlapSeconds = other.N3OverlapSeconds;
        N3EmojiLeadYield = other.N3EmojiLeadYield;
        N3LayoutAwareRows = other.N3LayoutAwareRows;
        // N3FontSets（ニコカラメーカー3 のフォント設定）とその階層 N3FontHierarchy・配色パターンはテンプレートとは独立したライブラリなので取り込まない
        // （テンプレートの適用でライブラリが置き換わらないように。古いテンプレートに入っていても無視する）
    }

    /// <summary>
    /// 設定ファイルを読み込めなかった（壊れていた）ため、既定値で動いている。
    /// このときは <see cref="Save"/> で上書きしない（元のファイルをそのまま残す）。
    /// </summary>
    [JsonIgnore]
    public bool LoadFailed { get; private set; }

    /// <summary>読み込めなかった設定ファイルを残した写しのパス（写しを作れなかったときは null）。</summary>
    [JsonIgnore]
    public string? BrokenCopyPath { get; private set; }

    /// <summary>読み込めなかった理由（例外のメッセージ）。</summary>
    [JsonIgnore]
    public string LoadError { get; private set; } = "";

    /// <summary>設定ファイルを読み込めなかったときにステータスバーへ出す説明。読み込めていれば null。</summary>
    [JsonIgnore]
    public string? LoadFailureMessage => !LoadFailed
        ? null
        : BrokenCopyPath is string copy
            ? $"設定ファイルを読み込めなかったため、設定は保存されません（{copy} を確認してください）"
            : $"設定ファイルを読み込めなかったため、設定は保存されません（{LoadError}）";

    /// <summary>
    /// 設定を読み込む。ファイルが無ければ既定値を返す。
    /// 壊れていて読めないときも既定値を返すが、<see cref="LoadFailed"/> を立てて保存を止め、
    /// 元のファイルの写しを「settings.json.broken-日時」として残す（全設定が既定値で上書きされて消えるのを防ぐ）。
    /// </summary>
    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            MigrateLegacySettings(path);
        }
        catch (Exception)
        {
            // 旧設定を引き継げなくても、新しい設定で起動する
        }
        if (!File.Exists(path)) return new AppSettings();

        try
        {
            return LoadStrict(path);
        }
        catch (Exception ex)
        {
            return new AppSettings
            {
                LoadFailed = true,
                LoadError = ex.Message,
                BrokenCopyPath = KeepBrokenCopy(path),
            };
        }
    }

    /// <summary>
    /// 設定（テンプレート）を読み込む。既定値に置き換えずに例外を投げるので、「ファイルが無い」と「壊れている」を区別できる。
    /// ファイルが無いときは <see cref="FileNotFoundException"/>、形式が正しくないときは <see cref="InvalidDataException"/>、
    /// 読み取れないときは <see cref="IOException"/> などをそのまま投げる。
    /// </summary>
    public static AppSettings LoadStrict(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"ファイルが見つかりません: {path}", path);
        string json = File.ReadAllText(path);
        AppSettings? settings;
        try
        {
            settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"ファイルの形式が正しくありません: {Path.GetFileName(path)}（{ex.Message}）", ex);
        }
        return settings ?? throw new InvalidDataException($"ファイルに設定が入っていません: {Path.GetFileName(path)}");
    }

    /// <summary>
    /// 読み込めなかった設定ファイルの写しを、同じフォルダに「元の名前.broken-yyyyMMdd-HHmmss」で残す。
    /// 同じ内容の写しが既にあればそれを返す（起動のたびに写しを増やさない）。写しを作れなければ null。
    /// </summary>
    private static string? KeepBrokenCopy(string path)
    {
        try
        {
            string full = Path.GetFullPath(path);
            string dir = Path.GetDirectoryName(full)!;
            string name = Path.GetFileName(full);
            byte[] content = File.ReadAllBytes(full);
            foreach (string existing in Directory.GetFiles(dir, name + ".broken-*").OrderByDescending(p => p, StringComparer.Ordinal))
            {
                if (File.ReadAllBytes(existing).AsSpan().SequenceEqual(content)) return existing;
            }

            string stem = Path.Combine(dir, $"{name}.broken-{DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture)}");
            string copy = stem;
            for (int n = 2; File.Exists(copy); n++)
            {
                copy = $"{stem}-{n}";
            }
            File.WriteAllBytes(copy, content);
            return copy;
        }
        catch (Exception)
        {
            return null;
        }
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

    /// <summary>
    /// 設定を保存する。読み込みに失敗した設定（<see cref="LoadFailed"/>）は何もしない
    /// （壊れた元のファイルを既定値で上書きしない）。
    /// </summary>
    public void Save(string? path = null)
    {
        if (LoadFailed) return;
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }
}
