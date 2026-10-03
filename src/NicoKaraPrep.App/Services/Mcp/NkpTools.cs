using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Runtime.CompilerServices;

namespace NicoKaraPrep.App.Services.Mcp;

/// <summary>道具の引数・結果の JSON の決まり（日本語をそのまま、既定値は省ける）。</summary>
internal static class McpSerializer
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        return options;
    }
}

/// <summary>
/// MCP で公開する道具（Claude などから呼ぶ操作）。引数の説明は属性に書き、処理はすべて <see cref="MainWindow"/>（McpInvokeAsync）で
/// UI スレッドで 1 件ずつ行う。window が無いとき（橋渡しの一覧作り）は呼ばれない。
/// </summary>
[McpServerToolType]
internal sealed class NkpTools
{
    private readonly MainWindow? _window;

    public NkpTools(MainWindow? window)
    {
        _window = window;
    }

    private Task<CallToolResult> Invoke(MainWindow.McpAccess access, Func<MainWindow, JsonNode?> body, CancellationToken ct,
        [CallerMemberName] string tool = "") =>
        _window is null
            ? Task.FromResult(McpResults.Error(McpInfo.NotRunningMessage))
            : _window.McpInvokeAsync(access, () => body(_window), ToolName(tool), ct);

    /// <summary>メソッド名（GetStatus）→ 道具の名前（get_status）。</summary>
    private static readonly Dictionary<string, string> ToolNames = typeof(NkpTools)
        .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
        .Select(m => (m.Name, Tool: System.Reflection.CustomAttributeExtensions.GetCustomAttribute<McpServerToolAttribute>(m)?.Name))
        .Where(x => x.Tool is not null)
        .ToDictionary(x => x.Name, x => x.Tool!);

    private static string ToolName(string method) => ToolNames.GetValueOrDefault(method, method);

    // ------------------------------------------------------------ 読む

    [McpServerTool(Name = "get_status", Title = "本体の状態", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    [Description("開いているファイル・タブ・ビュー・選択行・再生位置・チェックの件数・表示時刻のパラメーター・ステータスバーの文など、本体の今の状態を返す。")]
    public Task<CallToolResult> GetStatus(CancellationToken ct) => Invoke(MainWindow.McpAccess.Read, w => w.McpGetStatus(), ct);

    [McpServerTool(Name = "get_lines", Title = "行の一覧", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    [Description("行リストの行を返す。line（行番号）・text（歌詞）・singStart / singEnd（歌い出し・歌い終わり）・showBegin / showEnd（表示開始・表示終了。ニコカラメーカー3 で行が出る・消える時刻）・showBeginFrom / showEndFrom（表示時刻の出どころ: computed = 値を持たず毎回計算 / loaded = n3proj から読み込んだ値 / adjusted = 自動調整の値 / manual = 手で指定した値）・page / row（ページと下からの段）・font（当たるフォント設定。途中で変わるときは →）・fontManual / charFonts（手で指定したフォント）・layout / layoutManual・fontSizeDelta・width（横幅 px と使用率と判定）・check（チェックの印 error / warning）・overlap（同時歌唱）・exported（済マーク）。空行は pageBreak。長い曲は from と count で分けて読む。")]
    public Task<CallToolResult> GetLines(
        [Description("タブの名前（省くと表示中のタブ。指定すると画面もそのタブに切り替わる）")] string? tab = null,
        [Description("最初の行番号（1 始まり）")] int from = 1,
        [Description("返す行数（既定 200・最大 2000）")] int count = 200,
        [Description("タグ付きの原文（テキスト編集モード形式の行）も返す")] bool raw = false,
        CancellationToken ct = default) => Invoke(MainWindow.McpAccess.Read, w => w.McpGetLines(tab, from, count, raw), ct);

    [McpServerTool(Name = "get_text", Title = "歌詞の全文", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    [Description("タブの歌詞の全文を、RhythmicaLyrics のテキスト編集モード形式（[mm:ss:cc] のタイムタグ・{親|ルビ} 付き）で返す。")]
    public Task<CallToolResult> GetText(
        [Description("タブの名前（省くと表示中のタブ）")] string? tab = null,
        CancellationToken ct = default) => Invoke(MainWindow.McpAccess.Read, w => w.McpGetText(tab), ct);

    [McpServerTool(Name = "run_check", Title = "チェックを実行", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    [Description("チェック（ページ間の行衝突・表示時刻・横幅）を今すぐ実行し、結果の一覧（重要度・種別・行番号・関係する行・メッセージ）を返す。")]
    public Task<CallToolResult> RunCheck(CancellationToken ct) => Invoke(MainWindow.McpAccess.Read, w => w.McpRunCheck(), ct);

    [McpServerTool(Name = "render_preview", Title = "字幕のプレビュー画像", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    [Description("指定した時刻（または行の表示中のある時点）の字幕のプレビューを PNG 画像で返す（書き出しと同じ表示時刻・ページ・レイアウト・フォント設定。動画は含めない）。そのとき出ている行の一覧も返す。")]
    public Task<CallToolResult> RenderPreview(
        [Description("時刻（mm:ss:cc か秒）。line を指定するときは省く")] string? time = null,
        [Description("行番号。その行が表示されている時点を描く（at で位置を選ぶ）")] int? line = null,
        [Description("line のどの時点か: middle（表示の中ほど。既定）/ begin（表示開始の直後）/ end（表示終了の直前）/ wipe（歌の中ほど）")] string at = "middle",
        [Description("画像の幅 px（既定 960。字幕の画面の縦横比で描く）")] int width = 960,
        [Description("タブの名前（省くと表示中のタブ）")] string? tab = null,
        CancellationToken ct = default)
    {
        if (_window is null) return Task.FromResult(McpResults.Error(McpInfo.NotRunningMessage));
        var window = _window;
        return window.McpInvokeResultAsync(MainWindow.McpAccess.Read, async () =>
        {
            var (info, png) = await window.McpRenderPreviewAsync(tab, time, line, at, width);
            return McpResults.OkWithImage(info, png);
        }, "render_preview", ct);
    }

    [McpServerTool(Name = "get_show_time_settings", Title = "表示時刻のパラメーター", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    [Description("右のパネル「表示時刻」のパラメーター（ワイプ前・ワイプ後・表示間隔・最短表示後・重ねてよい・上段を長めに・絵文字の分だけ遅らせる・レイアウトで位置が違う行）と、表示時刻の出どころごとの行数を返す。")]
    public Task<CallToolResult> GetShowTimeSettings(CancellationToken ct) => Invoke(MainWindow.McpAccess.Read, w => w.McpShowTimeSettingsJson(withSummary: true), ct);

    // ------------------------------------------------------------ ファイル

    [McpServerTool(Name = "open_file", Title = "ファイルを開く", ReadOnly = false, Idempotent = false, OpenWorld = false, Destructive = true)]
    [Description("歌詞ファイル（.rlf / .lrc / .kra / .txt）を開く（作業状態 .tttproj も復元）。.n3proj を渡すとニコカラメーカー3 プロジェクトの読み込み（import_n3proj の既定の項目）になる。保存していない変更があるときは force: true が要る。")]
    public Task<CallToolResult> OpenFile(
        [Description("ファイルのフルパス")] string path,
        [Description("保存していない変更を捨てて開く")] bool force = false,
        CancellationToken ct = default) => Invoke(MainWindow.McpAccess.WriteDocument, w => w.McpOpenFile(path, force), ct);

    [McpServerTool(Name = "import_n3proj", Title = "ニコカラメーカー3 プロジェクトを読み込む", ReadOnly = false, Idempotent = false, OpenWorld = false, Destructive = true)]
    [Description("ニコカラメーカー3 のプロジェクト（.n3proj）を読み込む。メニューの読み込み確認画面と同じ項目を引数で選ぶ（省いた項目は確認画面の既定と同じ）。プロジェクトの歌詞ファイルも開き（別の歌詞を開いているときは force: true が要る）、2 つ目以降の歌詞設定の歌詞はタブで開く。")]
    public Task<CallToolResult> ImportN3Proj(
        [Description(".n3proj のフルパス")] string path,
        [Description("プロジェクトの歌詞ファイルも開く（false: 今開いている歌詞に設定だけ読み込む）")] bool openLyrics = true,
        [Description("別の歌詞を開いていても、保存していない変更を捨ててプロジェクトの歌詞を開く")] bool force = false,
        [Description("字幕フォントと画面サイズを取り込む（横幅チェック・プレビュー用）")] bool checkFont = true,
        [Description("ニコカラメーカーが計算した実際の表示区間をページ衝突チェックに使う")] bool lineTimes = true,
        [Description("表示時刻の設定値（ワイプ前・後・表示間隔・上段）をプロジェクトから推定して取り込む")] bool timing = false,
        [Description("行ごとの表示時刻を読み込んだ値として持たせる（省くと、歌詞が一致する行があれば持たせる）")] bool? lineShowTimes = null,
        [Description("ページのレイアウトを、自動で選ぶものと違うページだけ手動指定として取り込む")] bool pageLayouts = true,
        [Description("このプロジェクトを n3proj 書き出しのベースにする（省くと、ベースが未設定なら ベースにする）")] bool? exportBase = null,
        [Description("NicoKaraPrep のフォント設定として取り込むフォント設定の名前（[\"*\"] で全部。省くと取り込まない）")] string[]? fontSets = null,
        [Description("アイコン（@Emoji）を取り込む（省くと、追加・置き換えになるものだけ取り込む）")] bool? icons = null,
        [Description("アイコンをアプリ共通に取り込む（false: この曲専用）")] bool iconsGlobal = false,
        [Description("背景素材の動画をメディア再生に使う（省くと、メディアを開いていなければ使う）")] bool? media = null,
        CancellationToken ct = default) =>
        Invoke(MainWindow.McpAccess.WriteDocument,
            w => w.McpImportN3Proj(path, openLyrics, force, checkFont, lineTimes, timing, lineShowTimes, pageLayouts, exportBase, fontSets, icons, iconsGlobal, media), ct);

    [McpServerTool(Name = "save", Title = "保存", ReadOnly = false, Idempotent = true, OpenWorld = false, Destructive = true)]
    [Description("歌詞ファイルを保存する。scope が all（既定）はメニューの「上書き保存（タブ含む全行）」と同じ（分離タブをまとめてメインのファイルへ、別ファイルのタブはそれぞれのファイルへ）。tab は「表示中のタブを上書き保存」。path を指定すると、その場所へ名前を付けて保存する（.lrc か .rlf）。")]
    public Task<CallToolResult> Save(
        [Description("保存先のフルパス（省くと今のファイルへ上書き）")] string? path = null,
        [Description("all（タブ含む全行）か tab（表示中のタブだけ）")] string scope = "all",
        CancellationToken ct = default) => Invoke(MainWindow.McpAccess.Write, w => w.McpSave(path, scope), ct);

    [McpServerTool(Name = "export_n3proj", Title = "ニコカラメーカー3 プロジェクトを書き出す", ReadOnly = false, Idempotent = true, OpenWorld = false, Destructive = true)]
    [Description("ニコカラメーカー3 のプロジェクト（.n3proj）と歌詞ファイル（lrc）を書き出す。省いた設定は書き出し画面の今の設定（前回の書き出し・ベースの候補）を使う。警告があれば返す。")]
    public Task<CallToolResult> ExportN3Proj(
        [Description("書き出す .n3proj のフルパス（省くと前回の書き出し先か歌詞ファイルのフォルダ）")] string? path = null,
        [Description("ベースにする既存の n3proj のフルパス（\"\" でベースなし。省くと今の設定）")] string? basePath = null,
        [Description("プロジェクト名（ニコカラメーカーの設定の名称。空はファイル名）")] string? projectName = null,
        [Description("行の既定のフォント設定名（パート記号が出るまで）")] string? defaultFont = null,
        [Description("NicoKaraPrep のフォント設定をベースへ反映する")] bool? mergeFontSets = null,
        [Description("NicoKaraPrep で編集したレイアウト設定をベースへ反映する")] bool? mergeLayouts = null,
        [Description("タブ名 → レイアウト名（タブに固定するレイアウト。\"\" で自動）")] Dictionary<string, string>? tabLayouts = null,
        [Description("タブ名 → 上段の行を長めに表示するか")] Dictionary<string, bool>? tabTopLong = null,
        [Description("タブ名 → タブの最初の行から使うフォント設定名（\"\" で自動）")] Dictionary<string, string>? tabFonts = null,
        CancellationToken ct = default) =>
        Invoke(MainWindow.McpAccess.Write,
            w => w.McpExportN3Proj(path, basePath, projectName, defaultFont, mergeFontSets, mergeLayouts, tabLayouts, tabTopLong, tabFonts), ct);

    // ------------------------------------------------------------ 表示時刻

    [McpServerTool(Name = "set_show_time_settings", Title = "表示時刻のパラメーターを変える", ReadOnly = false, Idempotent = true, OpenWorld = false, Destructive = false)]
    [Description("右のパネル「表示時刻」のパラメーターを変える（指定した項目だけ。秒は小数可）。表示時刻を持たない行はすぐ新しい値で計算され、持たせた値は run_show_time_adjust を実行するまで変わらない。")]
    public Task<CallToolResult> SetShowTimeSettings(
        [Description("ワイプ前の表示時間（秒）")] double? lead = null,
        [Description("ワイプ後の表示時間（秒）")] double? tail = null,
        [Description("歌詞の表示間隔（秒）")] double? interval = null,
        [Description("最短表示後（秒。0 = 自動）")] double? protect = null,
        [Description("同じ段の行を重ねてよい時間（秒。0 = 重ねない）")] double? overlap = null,
        [Description("上段の行を長めに表示する")] bool? topLong = null,
        [Description("絵文字の分だけ行の表示を遅らせる")] bool? emojiYield = null,
        [Description("レイアウトで画面の位置が違う行は重ねない")] bool? layoutAware = null,
        CancellationToken ct = default) =>
        Invoke(MainWindow.McpAccess.Write, w => w.McpSetShowTimeSettings(lead, tail, interval, protect, overlap, topLong, emojiYield, layoutAware), ct);

    [McpServerTool(Name = "run_show_time_adjust", Title = "表示時刻の自動調整を実行", ReadOnly = false, Idempotent = true, OpenWorld = false, Destructive = false)]
    [Description("右のパネル「表示時刻」の「自動調整を実行」と同じ。全タブの行に、今のパラメーターで計算した表示時刻を持たせる（手で指定した行はそのまま。読み込んだ値・前回の自動調整の値は計算し直す）。Ctrl+Z で戻せる。")]
    public Task<CallToolResult> RunShowTimeAdjust(CancellationToken ct) => Invoke(MainWindow.McpAccess.WriteDocument, w => w.McpRunShowTimeAdjust(), ct);

    [McpServerTool(Name = "set_line_show_time", Title = "行の表示時刻を指定", ReadOnly = false, Idempotent = true, OpenWorld = false, Destructive = false)]
    [Description("行の表示開始・表示終了を手で指定する（行設定の欄と同じ。自動調整を実行し直しても変わらない）。指定しない側は今のまま。\"auto\" で手動の値を外して自動に戻す。")]
    public Task<CallToolResult> SetLineShowTime(
        [Description("行番号")] int line,
        [Description("表示開始（mm:ss:cc か秒。\"auto\" で自動）")] string? begin = null,
        [Description("表示終了（mm:ss:cc か秒。\"auto\" で自動）")] string? end = null,
        [Description("タブの名前（省くと表示中のタブ）")] string? tab = null,
        CancellationToken ct = default) => Invoke(MainWindow.McpAccess.WriteDocument, w => w.McpSetLineShowTime(tab, line, begin, end), ct);

    // ------------------------------------------------------------ 行設定（フォント・レイアウト・文字の大きさ）

    [McpServerTool(Name = "set_line_font", Title = "行のフォント設定を指定", ReadOnly = false, Idempotent = true, OpenWorld = false, Destructive = false)]
    [Description("行全体に当てるニコカラメーカー3 のフォント設定を手で指定する（文字ごとの指定は消える）。font を省くと自動（パート記号で決める）に戻す。")]
    public Task<CallToolResult> SetLineFont(
        [Description("行番号の一覧")] int[] lines,
        [Description("フォント設定名（書き出すフォント設定にある名前。省くと自動）")] string? font = null,
        [Description("タブの名前（省くと表示中のタブ）")] string? tab = null,
        CancellationToken ct = default) => Invoke(MainWindow.McpAccess.WriteDocument, w => w.McpSetLineFont(tab, lines, font), ct);

    [McpServerTool(Name = "set_char_font", Title = "文字のフォント設定を指定", ReadOnly = false, Idempotent = true, OpenWorld = false, Destructive = false)]
    [Description("行の中の文字の範囲だけにフォント設定を指定する（行リストで文字を選んで指定するのと同じ）。範囲は表示文字列の位置（0 始まり、end は含まない）。font を省くとその文字を自動に戻す。")]
    public Task<CallToolResult> SetCharFont(
        [Description("行番号")] int line,
        [Description("最初の文字の位置（0 始まり）")] int start,
        [Description("終わりの位置（この位置の文字は含まない）")] int end,
        [Description("フォント設定名（省くと自動）")] string? font = null,
        [Description("タブの名前（省くと表示中のタブ）")] string? tab = null,
        CancellationToken ct = default) => Invoke(MainWindow.McpAccess.WriteDocument, w => w.McpSetCharFont(tab, line, start, end, font), ct);

    [McpServerTool(Name = "set_page_layout", Title = "ページのレイアウトを指定", ReadOnly = false, Idempotent = true, OpenWorld = false, Destructive = false)]
    [Description("行のページ（空行で区切ったまとまり）にニコカラメーカー3 のレイアウト設定を指定する。layout を省くと自動（行数から選ぶ）に戻す。")]
    public Task<CallToolResult> SetPageLayout(
        [Description("行番号の一覧（それぞれの行のページに指定）")] int[] lines,
        [Description("レイアウト設定名（省くと自動）")] string? layout = null,
        [Description("タブの名前（省くと表示中のタブ）")] string? tab = null,
        CancellationToken ct = default) => Invoke(MainWindow.McpAccess.WriteDocument, w => w.McpSetPageLayout(tab, lines, layout), ct);

    [McpServerTool(Name = "set_page_font_size", Title = "ページの文字の大きさを変える", ReadOnly = false, Idempotent = true, OpenWorld = false, Destructive = false)]
    [Description("行のページの文字の大きさの増減（px。0 でそのまま）を指定する。書き出しでは、文字の大きさだけを変えたフォント設定（「（麻衣）+4」など）を作ってそのページに当てる。")]
    public Task<CallToolResult> SetPageFontSize(
        [Description("行番号の一覧（それぞれの行のページに指定）")] int[] lines,
        [Description("増減 px（例 -8、+4。0 で元に戻す）")] int delta = 0,
        [Description("タブの名前（省くと表示中のタブ）")] string? tab = null,
        CancellationToken ct = default) => Invoke(MainWindow.McpAccess.WriteDocument, w => w.McpSetPageFontSize(tab, lines, delta), ct);

    [McpServerTool(Name = "clear_line_overrides", Title = "行の指定を解除", ReadOnly = false, Idempotent = true, OpenWorld = false, Destructive = false)]
    [Description("行の表示時刻・フォント設定・レイアウト・文字の大きさの指定（読み込んだ値・自動調整の値も含む）をすべて解除して自動に戻す。")]
    public Task<CallToolResult> ClearLineOverrides(
        [Description("行番号の一覧")] int[] lines,
        [Description("タブの名前（省くと表示中のタブ）")] string? tab = null,
        CancellationToken ct = default) => Invoke(MainWindow.McpAccess.WriteDocument, w => w.McpClearLineOverrides(tab, lines), ct);

    // ------------------------------------------------------------ 元に戻す・画面

    [McpServerTool(Name = "undo", Title = "元に戻す", ReadOnly = false, Idempotent = false, OpenWorld = false, Destructive = false)]
    [Description("歌詞の変更を 1 つ元に戻す（Ctrl+Z。表示中のタブ。MCP の 1 回の書き込みは 1 回で戻る）。")]
    public Task<CallToolResult> Undo(CancellationToken ct) => Invoke(MainWindow.McpAccess.WriteDocument, w => w.McpUndoRedo(redo: false), ct);

    [McpServerTool(Name = "redo", Title = "やり直し", ReadOnly = false, Idempotent = false, OpenWorld = false, Destructive = false)]
    [Description("元に戻した変更をやり直す（Ctrl+Y。表示中のタブ）。")]
    public Task<CallToolResult> Redo(CancellationToken ct) => Invoke(MainWindow.McpAccess.WriteDocument, w => w.McpUndoRedo(redo: true), ct);

    [McpServerTool(Name = "select_lines", Title = "行を選ぶ", ReadOnly = false, Idempotent = true, OpenWorld = false, Destructive = false)]
    [Description("行リストで行を選び、見えるところまでスクロールする（利用者に行を示すため。歌詞は変えない）。")]
    public Task<CallToolResult> SelectLines(
        [Description("行番号の一覧（空にすると選択を外す）")] int[] lines,
        [Description("タブの名前（省くと表示中のタブ）")] string? tab = null,
        CancellationToken ct = default) => Invoke(MainWindow.McpAccess.Write, w => w.McpSelectLines(tab, lines), ct);
}
