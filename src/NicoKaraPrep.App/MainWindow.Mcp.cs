using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ModelContextProtocol.Protocol;
using NicoKaraPrep.App.Services;
using NicoKaraPrep.App.Services.Mcp;
using NicoKaraPrep.App.Services.Subtitles;
using NicoKaraPrep.App.ViewModels;
using NicoKaraPrep.App.Views;
using NicoKaraPrep.Core;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;
using NicoKaraPrep.Core.Project;
using NicoKaraPrep.Core.Validation;

namespace NicoKaraPrep.App;

/// <summary>
/// MCP（Claude などからの操作）。道具（<see cref="NkpTools"/>）の処理はすべてここで、UI スレッドで 1 件ずつ行う。
/// 書き込みは画面の操作と同じ処理を通し（画面にすぐ出る・Ctrl+Z で戻せる）、1 回の呼び出しは Ctrl+Z 1 回で戻る。
/// 確認画面などのダイアログを開いているあいだは書き込みを断り、歌詞を変える操作は全画面ビュー（フォント設定ビュー・レイアウト設定ビュー）を
/// 開いていれば元のビューへ戻ってから行う。
/// 行番号は 1 から、時刻は mm:ss:cc（行リストと同じ）でやり取りする。
/// あとから起動したインスタンスの引数（開くファイル）の受け取りもここ（単一インスタンス。Program.cs）。
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>道具が何をするか（断る・ビューを戻すの判断に使う）。</summary>
    internal enum McpAccess
    {
        /// <summary>読むだけ（チェックの実行・画像の作成を含む）。</summary>
        Read,

        /// <summary>設定・保存・選択など、歌詞の行は変えない書き込み。</summary>
        Write,

        /// <summary>歌詞（行・行設定）を変える書き込み。</summary>
        WriteDocument,
    }

    private McpHost? _mcpHost;
    private readonly SemaphoreSlim _mcpGate = new(1, 1);
    private int _mcpClients;
    private string? _mcpLastTool;
    private DateTime _mcpLastTime;

    // ------------------------------------------------------------ 待ち受けと状態の表示

    private void InitializeMcp()
    {
        Closed += (_, _) => _mcpHost?.Stop();
        ApplyMcpEnabled();
    }

    /// <summary>設定（ファイル > 設定 の MCP）に合わせて、待ち受けを始める・やめる。</summary>
    private void ApplyMcpEnabled()
    {
        if (ViewModel.Settings.McpEnabled)
        {
            if (_mcpHost is null)
            {
                _mcpHost = new McpHost(this);
                _mcpHost.ClientCountChanged += count => DispatcherQueue.TryEnqueue(() =>
                {
                    _mcpClients = count;
                    UpdateMcpStatus();
                });
            }
            _mcpHost.Start();
        }
        else
        {
            _mcpHost?.Stop();
            _mcpClients = 0;
        }
        UpdateMcpStatus();
    }

    /// <summary>ステータスバーの右の MCP の印（待ち受け中・接続中）と、その説明（最後の操作）。</summary>
    private void UpdateMcpStatus()
    {
        bool on = _mcpHost?.IsRunning == true;
        McpStatusText.Text = !on ? "" : _mcpClients > 0 ? "MCP: 接続中" : "MCP: 待ち受け中";
        if (!on)
        {
            ToolTipService.SetToolTip(McpStatusText, null);
            return;
        }
        string tip = _mcpClients > 0
            ? $"Claude などの MCP クライアントがつながっています（{_mcpClients} 件）。MCP からの操作はステータスバーに「MCP:」を付けて出し、Ctrl+Z で戻せます"
            : "Claude などの MCP クライアントからの接続を待っています（Claude への登録は ファイル > Claude と連携... から。ファイル > 設定 で止められます）";
        if (_mcpLastTool is not null) tip += $"\n最後の操作: {_mcpLastTool}（{_mcpLastTime:HH:mm:ss}）";
        ToolTipService.SetToolTip(McpStatusText, tip);
    }

    // ------------------------------------------------------------ Claude と連携（登録の画面）

    private async void OnClaudeLinkClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => await ShowClaudeLinkDialogAsync();

    /// <summary>「Claude と連携」の画面（Claude Desktop・Claude Code への登録をボタン 1 つで行う）を開く。</summary>
    private async Task ShowClaudeLinkDialogAsync()
    {
        if (IsModalDialogOpen()) return;
        var dialog = new ClaudeLinkDialog(
            () => ViewModel.Settings.McpEnabled,
            () =>
            {
                ViewModel.Settings.McpEnabled = true;
                ViewModel.Settings.Save();
                ApplyMcpEnabled();
            })
        {
            XamlRoot = Content.XamlRoot,
        };
        try
        {
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            ViewModel.StatusText = $"Claude と連携の画面を開けません: {ErrorText.Describe(ex)}";
        }
    }

    // ------------------------------------------------------------ 道具の処理の入口

    /// <summary>道具の処理（JSON を返すもの）を UI スレッドで 1 件ずつ行う。</summary>
    internal Task<CallToolResult> McpInvokeAsync(McpAccess access, Func<JsonNode?> body, string tool, CancellationToken ct) =>
        McpInvokeResultAsync(access, () => Task.FromResult(McpResults.Ok(body())), tool, ct);

    /// <summary>道具の処理（結果をそのまま作るもの。画像など）を UI スレッドで 1 件ずつ行う。</summary>
    internal async Task<CallToolResult> McpInvokeResultAsync(McpAccess access, Func<Task<CallToolResult>> body, string tool, CancellationToken ct)
    {
        await _mcpGate.WaitAsync(ct);
        try
        {
            var done = new TaskCompletionSource<CallToolResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool queued = DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    done.TrySetResult(await RunMcpToolAsync(access, body, tool));
                }
                catch (Exception ex)
                {
                    done.TrySetResult(McpResults.Error($"エラー: {ErrorText.Describe(ex)}"));
                }
            });
            if (!queued) return McpResults.Error("にこぷれっぷが終了しようとしているため、操作できません");
            return await done.Task.WaitAsync(ct);
        }
        finally
        {
            _mcpGate.Release();
        }
    }

    private async Task<CallToolResult> RunMcpToolAsync(McpAccess access, Func<Task<CallToolResult>> body, string tool)
    {
        _mcpLastTool = tool;
        _mcpLastTime = DateTime.Now;
        UpdateMcpStatus();
        if (access != McpAccess.Read)
        {
            if (IsModalDialogOpen())
            {
                return McpResults.Error("にこぷれっぷで確認画面などのダイアログが開いているため、操作できません。ダイアログを閉じてから、もう一度お願いします");
            }
            if (access == McpAccess.WriteDocument && FullScreenViewActive) ReturnFromFullScreenView();
        }

        string statusBefore = ViewModel.StatusText;
        var undoTops = access == McpAccess.WriteDocument ? ViewModel.SnapshotUndoTops() : null;
        try
        {
            return await body();
        }
        catch (McpToolException ex)
        {
            return McpResults.Error(ex.Message);
        }
        catch (Exception ex)
        {
            DebugLog($"MCP の {tool} で例外: {ex}");
            return McpResults.Error($"エラー: {ErrorText.Describe(ex)}");
        }
        finally
        {
            if (undoTops is not null) ViewModel.CollapseUndoSince(undoTops); // 1 回の呼び出しは Ctrl+Z 1 回で戻る
            if (access != McpAccess.Read && ViewModel.StatusText != statusBefore && !ViewModel.StatusText.StartsWith("MCP: ", StringComparison.Ordinal))
            {
                ViewModel.StatusText = "MCP: " + ViewModel.StatusText;
                if (_validateTimer.IsRunning) ViewModel.KeepNoticeBeforeCheck();
            }
        }
    }

    /// <summary>
    /// 確認画面などのダイアログ（ContentDialog・保存ダイアログ）を開いているか。開いているあいだは MCP の書き込みを断る
    /// （ダイアログはリストを丸ごと差し替えて保存するものがあり、その間の変更が消えるため）。
    /// </summary>
    private bool IsModalDialogOpen()
    {
        if (SaveFileDialog.IsShowing) return true;
        try
        {
            if (Content?.XamlRoot is { } root)
            {
                foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
                {
                    if (popup.Child is ContentDialog) return true;
                }
            }
        }
        catch (Exception)
        {
            // 調べられないときは開いていないものとする
        }
        return false;
    }

    /// <summary>
    /// 読む前に、編集のあとで待っているチェックを今すぐ行い、行の表示（当たるフォント・横幅・表示時刻）とプレビューの材料を今の歌詞にする。
    /// 全画面ビュー（フォント設定・レイアウト設定）ではチェックはせず（ステータスバーの案内を消さない）、行の表示だけを作り直す。
    /// </summary>
    private void McpRefreshForRead()
    {
        if (FullScreenViewActive)
        {
            TryRun(ViewModel.UpdateLineFonts);
            return;
        }
        if (!_validateTimer.IsRunning) return;
        _validateTimer.Stop();
        TryRun(ViewModel.RunValidation);
        RefreshInsertGutter();
        RefreshLineFontPlaceholder();
    }

    /// <summary>
    /// 今すぐチェックし、今のステータスバーの文（操作の知らせ）の後ろにチェックの結果をつなげて出す（書き込みのあと。結果の件数を返すため）。
    /// 全画面ビュー（フォント設定・レイアウト設定）ではチェックせず、行の表示だけを作り直す。
    /// </summary>
    private void ValidateNowKeepingStatus()
    {
        _validateTimer.Stop();
        if (FullScreenViewActive)
        {
            TryRun(ViewModel.UpdateLineFonts);
            return;
        }
        string summary = ViewModel.StatusText;
        TryRun(ViewModel.RunValidation);
        RefreshInsertGutter();
        RefreshLineFontPlaceholder();
        ViewModel.StatusText = $"{summary}　／　{ViewModel.StatusText}";
    }

    // ------------------------------------------------------------ 指定の読み取り（タブ・行・パス）

    /// <summary>道具のタブの指定（名前）。省いたときは表示中のタブ、違うタブなら画面もそのタブに切り替える。</summary>
    private TabState McpUseTab(string? tab)
    {
        if (string.IsNullOrWhiteSpace(tab)) return ViewModel.ActiveTab;
        string name = tab.Trim();
        var target = ViewModel.Tabs.FirstOrDefault(t => t.Name == name)
            ?? throw new McpToolException($"タブ「{name}」はありません（今のタブ: {string.Join("・", ViewModel.Tabs.Select(t => t.Name))}）");
        if (!ReferenceEquals(target, ViewModel.ActiveTab))
        {
            ViewModel.SwitchTab(ViewModel.Tabs.IndexOf(target));
            SyncTabSelection();
            RefreshAfterTabChange();
        }
        return target;
    }

    /// <summary>行番号（1 から）→ 行の添字。</summary>
    private int McpLineIndex(int line)
    {
        int count = ViewModel.Lines.Count;
        if (count == 0) throw new McpToolException("歌詞の行がありません（open_file などで歌詞を開いてください）");
        if (line < 1 || line > count) throw new McpToolException($"行番号 {line} は範囲の外です（1〜{count}）");
        return line - 1;
    }

    /// <summary>行番号の一覧 → 行の添字（重複を除いて小さい順）。</summary>
    private List<int> McpLineIndexes(int[]? lines)
    {
        if (lines is null || lines.Length == 0) throw new McpToolException("行番号を 1 つ以上指定してください");
        return lines.Select(McpLineIndex).Distinct().OrderBy(i => i).ToList();
    }

    private static string McpFullPath(string? path, string what)
    {
        string p = (path ?? "").Trim().Trim('"');
        if (p.Length == 0) throw new McpToolException($"{what}のパスを指定してください");
        if (!Path.IsPathFullyQualified(p)) throw new McpToolException($"{what}はフルパス（例 J:\\work\\曲\\歌詞.rlf）で指定してください: {path}");
        return Path.GetFullPath(p);
    }

    private static string McpExistingFile(string? path, string what)
    {
        string full = McpFullPath(path, what);
        if (!File.Exists(full)) throw new McpToolException($"{what}が見つかりません: {full}");
        return full;
    }

    /// <summary>保存・書き出しの先（フルパスで、フォルダがあり、拡張子が決まったもの）。</summary>
    private static string McpOutputPath(string? path, string what, params string[] extensions)
    {
        string full = McpFullPath(path, what);
        string ext = Path.GetExtension(full).ToLowerInvariant();
        if (!extensions.Contains(ext)) throw new McpToolException($"{what}の拡張子は {string.Join(" / ", extensions)} のどれかにしてください: {full}");
        string? dir = Path.GetDirectoryName(full);
        if (dir is null || !Directory.Exists(dir)) throw new McpToolException($"{what}のフォルダがありません: {dir}");
        return full;
    }

    private static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>書き出すフォント設定の名前（省く・auto は自動 = null。無い名前は断る）。</summary>
    private string? McpFontName(string? font)
    {
        if (string.IsNullOrWhiteSpace(font) || font.Trim() is "auto" or "自動") return null;
        string name = font.Trim();
        var (names, _) = ViewModel.GetExportFontNames();
        if (!names.Contains(name))
        {
            throw new McpToolException($"フォント設定「{name}」はありません。使える名前: {string.Join("・", names.Where(n => n.Length > 0).Distinct().Take(80))}");
        }
        return name;
    }

    private string UnsavedMessage()
    {
        var names = ViewModel.GetAllTabs().Where(ViewModel.IsTabModified).Select(t => t.Name);
        return $"保存していない変更があります（タブ: {string.Join("・", names)}）。save で保存するか、捨ててよければ force: true で呼んでください";
    }

    // ------------------------------------------------------------ 結果の JSON

    private static string ViewKey(MainViewMode mode) => mode switch
    {
        MainViewMode.EmojiInsert => "emojiInsert",
        MainViewMode.FontSettings => "fontSettings",
        MainViewMode.Layout => "layout",
        _ => "lines",
    };

    /// <summary>既定の字幕アクションの出どころ（song: 曲の既定 / base: ベースの n3proj / nicoKaraMaker3: ニコカラメーカー3 の設定 / standard: 文字単位フェード）。</summary>
    private static string ActionSourceKey(N3SubtitleActionSource source) => source switch
    {
        N3SubtitleActionSource.Song => "song",
        N3SubtitleActionSource.Base => "base",
        N3SubtitleActionSource.Nkm3 => "nicoKaraMaker3",
        _ => "standard",
    };

    private static string SeverityKey(IssueSeverity s) => s switch
    {
        IssueSeverity.Error => "error",
        IssueSeverity.Warning => "warning",
        _ => "info",
    };

    /// <summary>表示時刻の出どころ（computed: 値を持たず毎回計算 / loaded: n3proj から読み込んだ値 / adjusted: 自動調整の値 / manual: 手で指定した値）。</summary>
    private static string OriginKey(int? cs, ShowTimeOrigin origin) => cs is null ? "computed" : origin switch
    {
        ShowTimeOrigin.Loaded => "loaded",
        ShowTimeOrigin.Auto => "adjusted",
        _ => "manual",
    };

    private JsonObject McpChecksJson() => new()
    {
        ["errors"] = ViewModel.Issues.Count(i => i.Severity == IssueSeverity.Error),
        ["warnings"] = ViewModel.Issues.Count(i => i.Severity == IssueSeverity.Warning),
        ["infos"] = ViewModel.Issues.Count(i => i.Severity == IssueSeverity.Info),
    };

    /// <summary>行 1 つ分の JSON（行リストに出ているものと同じ値）。</summary>
    private JsonObject McpLineJson(int index, IReadOnlyDictionary<int, N3LinePlan> plans, bool withRaw)
    {
        var vm = ViewModel.Lines[index];
        var line = vm.Model;
        var o = new JsonObject { ["line"] = index + 1 };
        if (line.IsEmpty)
        {
            o["pageBreak"] = true;
            return o;
        }
        o["text"] = line.GetDisplayText();
        if (withRaw) o["raw"] = vm.RawText;
        if (line.GetFirstTimeCs() is int first) o["singStart"] = McpTime.Format(first);
        if (line.GetLastTimeCs() is int last) o["singEnd"] = McpTime.Format(last);
        if (plans.TryGetValue(index, out var plan))
        {
            o["showBegin"] = McpTime.FormatMs(plan.BeginMs);
            o["showEnd"] = McpTime.FormatMs(plan.EndMs);
            o["page"] = plan.PageIndex + 1;
            o["row"] = plan.Row;
        }
        o["showBeginFrom"] = OriginKey(line.ShowBeginCs, line.ShowBeginOrigin);
        o["showEndFrom"] = OriginKey(line.ShowEndCs, line.ShowEndOrigin);
        if (vm.AppliedFont.IsVisible) o["font"] = vm.AppliedFont.Summary;
        if (line.FontSetName is { } manualFont) o["fontManual"] = manualFont;
        var ranges = CharFontOperations.Ranges(line);
        if (ranges.Count > 0)
        {
            var units = CharFontOperations.DisplayUnits(line);
            int Offset(int k) => units.Take(k).Sum(c => c.Text.Length);
            o["charFonts"] = new JsonArray(ranges.Select(r => (JsonNode?)new JsonObject
            {
                ["start"] = Offset(r.Start),
                ["end"] = Offset(r.Start + r.Length),
                ["font"] = r.Name,
            }).ToArray());
        }
        string layout = vm.LayoutText.TrimStart('✎');
        int delta = ViewModel.PageFontSizeDelta(index);
        if (delta != 0)
        {
            string suffix = $"（文字 {N3PageFontSize.Signed(delta)}）";
            if (layout.EndsWith(suffix, StringComparison.Ordinal)) layout = layout[..^suffix.Length];
            o["fontSizeDelta"] = delta;
        }
        if (layout.Length > 0) o["layout"] = layout;
        if (line.LayoutName is { Length: > 0 } manualLayout) o["layoutManual"] = manualLayout;
        // 字幕アクション（行ごとの指定の表示名。指定が無ければ「（既定）」= 曲の既定に従う）
        string actionName = N3SubtitleActionCatalog.DisplayName(line.SubtitleAction?.Id);
        o["action"] = actionName;
        if (line.SubtitleAction is { } action)
        {
            o["actionId"] = action.Id;
            string detail = ViewModel.DescribeSubtitleAction(action); // 設定値をニコカラメーカー3 の設定から変えていれば「フェードイン/アウト（500/250ms）」など
            if (detail != actionName) o["actionDetail"] = detail;
        }
        if (vm.WidthResult is { } w)
        {
            o["width"] = new JsonObject
            {
                ["px"] = Math.Round(w.WidthPx),
                ["percent"] = Math.Round(w.UsagePercent),
                ["result"] = w.Severity is { } ws ? SeverityKey(ws) : "ok",
            };
        }
        if (vm.RowSeverity is { } severity) o["check"] = SeverityKey(severity);
        if (vm.OverlapGlyph.Length > 0) o["overlap"] = true;
        if (vm.Exported) o["exported"] = true;
        return o;
    }

    /// <summary>行を変えた道具の結果（変えた行数・指定した行の今の状態・チェックの件数・ステータスバーの文）。</summary>
    private JsonObject McpChangedLinesJson(IReadOnlyList<int> indexes, int changed)
    {
        var plans = ViewModel.PlanShowTimes();
        return new JsonObject
        {
            ["changedLines"] = changed,
            ["lines"] = new JsonArray(indexes.Take(50).Select(i => (JsonNode?)McpLineJson(i, plans, withRaw: false)).ToArray()),
            ["checks"] = McpChecksJson(),
            ["statusText"] = ViewModel.StatusText,
        };
    }

    internal JsonObject McpShowTimeSettingsJson(bool withSummary)
    {
        var s = ViewModel.Settings;
        var o = new JsonObject
        {
            ["lead"] = s.DisplayLeadSeconds,
            ["tail"] = s.DisplayTailSeconds,
            ["interval"] = s.N3IntervalSeconds,
            ["protect"] = s.N3ProtectSeconds,
            ["overlap"] = s.N3OverlapSeconds,
            ["topLong"] = s.N3TopLong,
            ["emojiYield"] = s.N3EmojiLeadYield,
            ["layoutAware"] = s.N3LayoutAwareRows,
        };
        if (withSummary)
        {
            var (c, outdated) = ViewModel.ShowTimeSummary();
            o["activeTabLines"] = new JsonObject
            {
                ["manual"] = c.Manual,
                ["loaded"] = c.Loaded,
                ["adjusted"] = c.Auto,
                ["computed"] = c.Live,
                ["changeIfAdjustedAgain"] = outdated,
            };
        }
        return o;
    }

    // ------------------------------------------------------------ 読む

    internal JsonNode McpGetStatus()
    {
        McpRefreshForRead();
        var tabs = new JsonArray();
        foreach (var t in ViewModel.GetAllTabs())
        {
            tabs.Add(new JsonObject
            {
                ["name"] = t.Name,
                ["kind"] = t.IsMain ? "main" : t.OwnFile ? "ownFile" : "split",
                ["active"] = ReferenceEquals(t, ViewModel.ActiveTab),
                ["lines"] = t.Document.Lines.Count,
                ["lyricLines"] = t.Document.Lines.Count(l => !l.IsEmpty),
                ["modified"] = ViewModel.IsTabModified(t),
                ["file"] = t.IsMain ? t.FilePath : t.CopyFilePath,
            });
        }

        JsonNode? media = null;
        if (ViewModel.MediaPath is { Length: > 0 } mediaPath)
        {
            var session = PlayerWithSource?.PlaybackSession;
            media = new JsonObject
            {
                ["path"] = mediaPath,
                ["opened"] = session is not null,
                ["playing"] = session?.PlaybackState == Windows.Media.Playback.MediaPlaybackState.Playing,
                ["position"] = session is null ? null : McpTime.Format(ViewModel.MediaSecondsToTagCs(session.Position.TotalSeconds)),
            };
        }

        var (fontNames, defaultFont) = ViewModel.GetExportFontNames();
        var defaultAction = ViewModel.ResolveCurrentDefaultSubtitleAction(out var actionSource);
        return new JsonObject
        {
            ["app"] = new JsonObject
            {
                ["name"] = McpInfo.ServerName,
                ["version"] = McpInfo.Version,
                ["store"] = McpInfo.IsPackaged,
                ["registrationExe"] = McpInfo.RegistrationExePath,
            },
            ["file"] = ViewModel.MainFilePath,
            ["modified"] = ViewModel.HasUnsavedChanges,
            ["view"] = ViewKey(ViewModel.ViewMode),
            ["activeTab"] = ViewModel.ActiveTab.Name,
            ["tabs"] = tabs,
            ["selectedLines"] = new JsonArray(SelectedIndexes.Select(i => (JsonNode?)(i + 1)).ToArray()),
            ["media"] = media,
            ["checks"] = McpChecksJson(),
            ["showTimeSettings"] = McpShowTimeSettingsJson(withSummary: true),
            ["fontSets"] = new JsonArray(fontNames.Where(n => n.Length > 0).Distinct().Select(n => (JsonNode?)n).ToArray()),
            ["defaultFontSet"] = defaultFont,
            ["layouts"] = new JsonArray(ViewModel.GetEffectiveLayouts()
                .Select(l => (JsonNode?)new JsonObject { ["name"] = l.Name, ["lines"] = l.LineCount }).ToArray()),
            // 行ごとの指定が無い歌詞行に書く字幕アクション（書き出しと同じ決め方）と、使える字幕アクション
            ["defaultAction"] = new JsonObject
            {
                ["name"] = ViewModel.DescribeSubtitleAction(defaultAction),
                ["id"] = defaultAction.Id,
                ["from"] = ActionSourceKey(actionSource),
            },
            ["actions"] = new JsonArray(N3SubtitleActionCatalog.Known
                .Select(k => (JsonNode?)new JsonObject { ["id"] = k.Id, ["name"] = k.Name }).ToArray()),
            ["n3proj"] = new JsonObject
            {
                ["basePath"] = ViewModel.SuggestN3ProjBasePath(),
                ["outputPath"] = ViewModel.SuggestN3ProjOutputPath(),
            },
            ["nicoKaraMaker3"] = ViewModel.Nkm3Env?.AppVersion,
            ["dialogOpen"] = IsModalDialogOpen(),
            ["statusText"] = ViewModel.StatusText,
        };
    }

    internal JsonNode McpGetLines(string? tab, int from, int count, bool raw)
    {
        var t = McpUseTab(tab);
        McpRefreshForRead();
        int total = ViewModel.Lines.Count;
        int start = Math.Max(1, from);
        int end = Math.Min(total, start + Math.Clamp(count, 1, 2000) - 1);
        var plans = ViewModel.PlanShowTimes();
        var lines = new JsonArray();
        for (int n = start; n <= end; n++) lines.Add(McpLineJson(n - 1, plans, raw));
        var o = new JsonObject
        {
            ["tab"] = t.Name,
            ["totalLines"] = total,
            ["from"] = start,
            ["to"] = end,
            ["lines"] = lines,
        };
        if (end < total) o["next"] = end + 1;
        return o;
    }

    internal JsonNode McpGetText(string? tab)
    {
        var t = McpUseTab(tab);
        return new JsonObject
        {
            ["tab"] = t.Name,
            ["format"] = "RhythmicaLyrics のテキスト編集モード形式",
            ["text"] = ViewModel.GetTextEditModeText(),
        };
    }

    internal JsonNode McpRunCheck()
    {
        bool fullScreen = FullScreenViewActive;
        string before = ViewModel.StatusText;
        _validateTimer.Stop();
        ViewModel.RunValidation();
        if (fullScreen)
        {
            ViewModel.StatusText = before; // 全画面ビュー（フォント設定・レイアウト設定）の案内は消さない
        }
        else
        {
            RefreshInsertGutter();
            RefreshLineFontPlaceholder();
            IssuePanel.IsExpanded = ViewModel.Issues.Count > 0;
            ViewModel.StatusText = "MCP: " + ViewModel.StatusText;
        }
        var issues = ViewModel.Issues.ToList();
        const int Max = 500;
        var o = McpChecksJson();
        o["tab"] = ViewModel.ActiveTab.Name;
        o["issues"] = new JsonArray(issues.Take(Max).Select(i =>
        {
            var item = new JsonObject
            {
                ["severity"] = SeverityKey(i.Severity),
                ["category"] = i.Category,
                ["line"] = i.LineIndex + 1,
                ["message"] = i.Message,
            };
            if (i.RelatedLineIndex is int related) item["relatedLine"] = related + 1;
            return (JsonNode?)item;
        }).ToArray());
        if (issues.Count > Max) o["truncated"] = true;
        return o;
    }

    internal async Task<(JsonNode Info, byte[] Png)> McpRenderPreviewAsync(string? tab, string? time, int? line, string? at, int width)
    {
        McpUseTab(tab);
        McpRefreshForRead();
        if (ViewModel.PreviewModel is null) TryRun(ViewModel.UpdateLineFonts);
        var model = ViewModel.PreviewModel;
        if (model is null || model.Lines.Count == 0) throw new McpToolException("字幕のプレビューを作れません（歌詞を開いていない、タイムタグが無い、など）");

        double ms;
        if (line is int n)
        {
            int index = McpLineIndex(n);
            if (!ViewModel.PlanShowTimes().TryGetValue(index, out var plan))
            {
                throw new McpToolException($"{n} 行目は空行か、タイムタグが無いため、表示時刻が決まりません");
            }
            var lyric = ViewModel.Document.Lines[index];
            double middle = (plan.BeginMs + plan.EndMs) / 2.0;
            ms = (at ?? "").Trim().ToLowerInvariant() switch
            {
                "" or "middle" => middle,
                "begin" or "start" => Math.Min(plan.BeginMs + 50, middle),
                "end" => Math.Max(plan.EndMs - 50, middle),
                "wipe" or "sing" => N3ShowTimePlanner.SingStartMs(lyric) is int s && N3ShowTimePlanner.SingEndMs(lyric) is int e ? (s + e) / 2.0 : middle,
                _ => throw new McpToolException("at は middle・begin・end・wipe のどれかで指定してください"),
            };
        }
        else if (!string.IsNullOrWhiteSpace(time))
        {
            ms = McpTime.Parse(time, "time") * 10.0;
        }
        else if (PlayerWithSource is not null && !double.IsNegativeInfinity(SubtitlePreview.TimeMs))
        {
            ms = SubtitlePreview.TimeMs; // 今の再生位置
        }
        else
        {
            throw new McpToolException("time（時刻）か line（行番号）を指定してください");
        }

        byte[] png = await SubtitleFrameRenderer.RenderPngAsync(model, ms, width);

        var tabs = ViewModel.GetAllTabs();
        var shown = new JsonArray();
        foreach (var p in SubtitlePreviewView.VisibleLines(model, ms).OrderBy(p => p.Tab).ThenByDescending(p => p.Row))
        {
            var doc = p.Tab >= 0 && p.Tab < tabs.Count ? tabs[p.Tab].Document : null;
            int lineIndex = doc?.Lines.IndexOf(p.Source.Line) ?? -1;
            var item = new JsonObject
            {
                ["tab"] = doc is null ? null : tabs[p.Tab].Name,
                ["text"] = p.Source.Line.GetDisplayText(),
                ["page"] = p.Page + 1,
                ["row"] = p.Row,
                ["showBegin"] = McpTime.FormatMs(p.BeginMs),
                ["showEnd"] = McpTime.FormatMs(p.EndMs),
            };
            if (lineIndex >= 0) item["line"] = lineIndex + 1;
            shown.Add(item);
        }
        var info = new JsonObject
        {
            ["time"] = McpTime.FormatMs((int)Math.Round(ms)),
            ["screen"] = $"{model.ScreenWidth}x{model.ScreenHeight}",
            ["visibleLines"] = shown,
            ["note"] = "動画は含めず、暗い背景に字幕だけを描いた近似です（ニコカラメーカー3 の字幕アクションのフェードなどは描きません）",
        };
        return (info, png);
    }

    // ------------------------------------------------------------ ファイル

    internal JsonNode McpOpenFile(string path, bool force)
    {
        string full = McpExistingFile(path, "開くファイル");
        string ext = Path.GetExtension(full).ToLowerInvariant();
        if (ext == ".n3proj")
        {
            return McpImportN3Proj(full, openLyrics: true, force, checkFont: true, lineTimes: true, timing: false,
                lineShowTimes: null, pageLayouts: true, lineActions: true, exportBase: null, fontSets: null, icons: null, iconsGlobal: false, media: null);
        }
        if (MediaExtensions.Contains(ext))
        {
            OpenMedia(full);
            return new JsonObject { ["media"] = full, ["statusText"] = ViewModel.StatusText };
        }
        if (ViewModel.HasUnsavedChanges && !force) throw new McpToolException(UnsavedMessage());

        ViewModel.OpenFile(full);
        AfterDocumentLoaded();
        if (InsertViewActive) RefreshInsertView(0);
        ValidateNowKeepingStatus();
        return new JsonObject
        {
            ["opened"] = full,
            ["lines"] = ViewModel.Lines.Count,
            ["tabs"] = new JsonArray(ViewModel.Tabs.Select(t => (JsonNode?)t.Name).ToArray()),
            ["checks"] = McpChecksJson(),
            ["statusText"] = ViewModel.StatusText,
        };
    }

    internal JsonNode McpImportN3Proj(string path, bool openLyrics, bool force, bool checkFont, bool lineTimes, bool timing,
        bool? lineShowTimes, bool pageLayouts, bool lineActions, bool? exportBase, string[]? fontSets, bool? icons, bool iconsGlobal, bool? media)
    {
        string full = McpExistingFile(path, "ニコカラメーカー3 プロジェクト");
        if (!full.EndsWith(".n3proj", StringComparison.OrdinalIgnoreCase)) throw new McpToolException($"拡張子が .n3proj のファイルを指定してください: {full}");
        var preview = ViewModel.PrepareN3ProjImport(full);
        string? lyricsNote = openLyrics ? McpOpenProjectLyrics(preview, force) : null;

        // 省いた項目は、読み込み確認画面の最初の選択と同じにする
        var (matched, total) = ViewModel.CountMatchedLyricLines(preview);
        string? currentBase = ViewModel.N3ProjSettings.BasePath;
        bool sameBase = currentBase is { Length: > 0 } && SamePath(currentBase, full);
        string? currentMedia = ViewModel.MediaPath;
        bool sameMedia = currentMedia is { Length: > 0 } && preview.MediaPath is { Length: > 0 } && SamePath(currentMedia, preview.MediaPath);

        var fontNames = new List<string>();
        if (fontSets is { Length: > 0 })
        {
            var available = preview.FontSets.Where(f => f.Name.Length > 0).Select(f => f.Name).ToList();
            if (fontSets.Any(n => n.Trim() == "*"))
            {
                // 全部（ページの文字の大きさのために書き出しで作ったもの（「（麻衣）+4」など）は除く。書き出すたびに作り直すため）
                var projectNames = new HashSet<string>(available);
                fontNames.AddRange(available.Where(n => !N3PageFontSize.IsDerivedName(n, projectNames)));
            }
            else
            {
                foreach (string n in fontSets.Select(n => n.Trim()).Where(n => n.Length > 0))
                {
                    if (!available.Contains(n)) throw new McpToolException($"フォント設定「{n}」はこのプロジェクトにありません（{string.Join("・", available.Take(80))}）");
                    if (!fontNames.Contains(n)) fontNames.Add(n);
                }
            }
        }

        var iconNames = new List<string>();
        if (icons != false)
        {
            foreach (var icon in preview.Icons)
            {
                bool take = icons == true
                    || (icon.ImageExists && ViewModel.DescribeIconImport(icon.Entry, iconsGlobal) is "追加" or "置き換え");
                if (take) iconNames.Add(icon.Entry.ReplaceChar);
            }
        }

        var choices = new N3ProjImportChoices
        {
            CheckFont = checkFont,
            LineTimes = lineTimes && preview.Settings.LineTimes.Count > 0,
            Timing = timing && preview.Timing is not null,
            LineShowTimes = (lineShowTimes ?? true) && matched > 0,
            PageLayouts = pageLayouts && matched > 0 && ViewModel.CountPageLayoutImports(preview) > 0,
            LineActions = lineActions && ViewModel.CountLineActionImports(preview).Default is not null,
            ExportBase = exportBase ?? (currentBase is not { Length: > 0 } || sameBase),
            FontSetNames = fontNames,
            IconNames = iconNames,
            IconsGlobal = iconsGlobal,
            Media = (media ?? (!sameMedia && (currentMedia is not { Length: > 0 } || !File.Exists(currentMedia)))) && preview.MediaExists,
        };
        ViewModel.ApplyN3ProjImport(preview, choices);
        AfterN3ProjImported(full, choices, lyricsNote, addRecent: true);
        if (InsertViewActive) RefreshInsertView(0);

        return new JsonObject
        {
            ["project"] = full,
            ["lyrics"] = lyricsNote,
            ["matchedLines"] = matched,
            ["lyricLines"] = total,
            ["imported"] = new JsonObject
            {
                ["checkFont"] = choices.CheckFont,
                ["lineTimes"] = choices.LineTimes,
                ["timing"] = choices.Timing,
                ["lineShowTimes"] = choices.LineShowTimes,
                ["pageLayouts"] = choices.PageLayouts,
                ["lineActions"] = choices.LineActions,
                ["exportBase"] = choices.ExportBase,
                ["fontSets"] = new JsonArray(choices.FontSetNames.Select(n => (JsonNode?)n).ToArray()),
                ["icons"] = new JsonArray(choices.IconNames.Select(n => (JsonNode?)n).ToArray()),
                ["iconsGlobal"] = choices.IconsGlobal,
                ["media"] = choices.Media,
            },
            ["tabs"] = new JsonArray(ViewModel.Tabs.Select(t => (JsonNode?)t.Name).ToArray()),
            ["checks"] = McpChecksJson(),
            ["statusText"] = ViewModel.StatusText,
        };
    }

    /// <summary>
    /// n3proj のメインの歌詞ファイルを開く（読み込み確認画面の前に開くのと同じ。尋ねる代わりに、保存していない変更があれば force が要る）。
    /// 開いた・開けなかったことの説明を返す。
    /// </summary>
    private string? McpOpenProjectLyrics(N3ProjImportPreview preview, bool force)
    {
        if (preview.Tabs.FirstOrDefault() is not { } main) return null; // 歌詞の無いプロジェクト
        string? lyrics = N3ProjImport.FindLyricsFile(preview.Path, main);
        string? current = ViewModel.MainFilePath;
        bool blank = ViewModel.IsDocumentBlank;
        if (lyrics is null)
        {
            string name = Path.GetFileName(main.LyricsRelativePath ?? main.LyricsPath ?? "");
            return blank ? $"このプロジェクトの歌詞ファイル{(name.Length > 0 ? $"（{name}）" : "")}が見つからないため、歌詞は開きませんでした" : null;
        }
        if (current is not null && N3ProjImport.IsSameLyrics(current, lyrics)) return OpenProjectExtraTabs(preview);
        if (!blank && ViewModel.HasUnsavedChanges && !force)
        {
            throw new McpToolException(
                $"開いている歌詞「{(current is null ? "（無題）" : Path.GetFileName(current))}」に保存していない変更があります。" +
                $"プロジェクトの歌詞「{Path.GetFileName(lyrics)}」を開くと失われます。save で保存するか、捨ててよければ force: true、" +
                "今の歌詞に設定だけ読み込むなら openLyrics: false で呼んでください");
        }
        ViewModel.OpenFile(lyrics, autoImportNearby: false);
        AfterDocumentLoaded();
        string opened = $"プロジェクトの歌詞 {Path.GetFileName(lyrics)} を開きました（{ViewModel.Lines.Count} 行）";
        return OpenProjectExtraTabs(preview) is string extra ? $"{opened}　{extra}" : opened;
    }

    internal JsonNode McpSave(string? path, string? scope)
    {
        string mode = (scope ?? "all").Trim().ToLowerInvariant();
        string target;
        if (mode is "" or "all")
        {
            target = path is { Length: > 0 }
                ? McpOutputPath(path, "保存先", ".lrc", ".rlf", ".txt")
                : ViewModel.MainFilePath ?? throw new McpToolException("まだファイルに保存していない歌詞です。path（.lrc か .rlf のフルパス）を指定してください");
            ViewModel.SaveFullTo(target);
            RefreshRecentFilesMenu();
        }
        else if (mode == "tab")
        {
            target = path is { Length: > 0 }
                ? McpOutputPath(path, "保存先", ".lrc", ".rlf", ".txt")
                : ViewModel.GetActiveTabCopyPath() ?? throw new McpToolException($"表示中のタブ「{ViewModel.ActiveTab.Name}」はまだ別ファイルへ保存していません。path を指定してください");
            ViewModel.SaveActiveTabCopyTo(target);
        }
        else
        {
            throw new McpToolException("scope は all（タブ含む全行）か tab（表示中のタブ）で指定してください");
        }
        return new JsonObject
        {
            ["saved"] = target,
            ["modified"] = ViewModel.HasUnsavedChanges,
            ["statusText"] = ViewModel.StatusText,
        };
    }

    internal JsonNode McpExportN3Proj(string? path, string? basePath, string? projectName, string? defaultFont, bool? mergeFontSets, bool? mergeLayouts,
        Dictionary<string, string>? tabLayouts, Dictionary<string, bool>? tabTopLong, Dictionary<string, string>? tabFonts)
    {
        var exportTabs = ViewModel.GetN3ProjExportTabs();
        if (exportTabs.Count == 0) throw new McpToolException("書き出す歌詞がありません");
        var tabNames = exportTabs.Select(t => t.Name).ToHashSet();
        string CheckTab(string name) => tabNames.Contains(name)
            ? name
            : throw new McpToolException($"タブ「{name}」はありません（書き出すタブ: {string.Join("・", tabNames)}）");

        var current = ViewModel.N3ProjSettings;
        var settings = new N3ProjSongSettings
        {
            BasePath = basePath is null ? ViewModel.SuggestN3ProjBasePath()
                : basePath.Trim().Length == 0 ? null
                : McpExistingFile(basePath, "ベースの n3proj"),
            OutputPath = current.OutputPath,
            ProjectName = projectName?.Trim() ?? current.ProjectName,
            DefaultFontSetName = defaultFont is null ? current.DefaultFontSetName : McpFontName(defaultFont) ?? "",
            MergeFontSets = mergeFontSets ?? current.MergeFontSets,
            MergeLayouts = mergeLayouts ?? current.MergeLayouts,
            TabLayouts = new Dictionary<string, string>(current.TabLayouts),
            TabTopLong = new Dictionary<string, bool>(current.TabTopLong),
            TabFontSetNames = new Dictionary<string, string>(current.TabFontSetNames),
            // 引数に無い曲の設定は今の設定を引き継ぐ（作り直した設定で置き換えるため、写さないと消える）
            SubtitleAction = current.SubtitleAction?.Clone(),
        };
        if (tabLayouts is not null)
        {
            var layoutNames = ViewModel.GetEffectiveLayouts().Select(l => l.Name).ToHashSet();
            foreach (var (tab, layout) in tabLayouts)
            {
                string name = CheckTab(tab);
                if (string.IsNullOrWhiteSpace(layout) || layout.Trim() is "auto" or "自動")
                {
                    settings.TabLayouts.Remove(name);
                }
                else if (layoutNames.Contains(layout.Trim()))
                {
                    settings.TabLayouts[name] = layout.Trim();
                }
                else
                {
                    throw new McpToolException($"レイアウト設定「{layout}」はありません（{string.Join("・", layoutNames)}）");
                }
            }
        }
        if (tabTopLong is not null)
        {
            foreach (var (tab, topLong) in tabTopLong) settings.TabTopLong[CheckTab(tab)] = topLong;
        }
        if (tabFonts is not null)
        {
            foreach (var (tab, font) in tabFonts)
            {
                string name = CheckTab(tab);
                if (McpFontName(font) is string f) settings.TabFontSetNames[name] = f;
                else settings.TabFontSetNames.Remove(name);
            }
        }

        string target = path is { Length: > 0 }
            ? McpOutputPath(path, "書き出し先", ".n3proj")
            : ViewModel.SuggestN3ProjOutputPath() ?? throw new McpToolException("書き出し先を決められません。path（.n3proj のフルパス）を指定してください");
        var result = ViewModel.ExportN3Proj(target, settings);

        _n3FontNamesKey = null;
        RefreshN3LinePanel();
        ValidateNowKeepingStatus();
        NotifyLayoutViewDocumentChanged(); // ベース・タブの固定レイアウトが替わると、レイアウト設定ビューのレイアウトの並びが変わる
        return new JsonObject
        {
            ["project"] = result.ProjectPath,
            ["lyricsFiles"] = new JsonArray(result.LyricsPaths.Select(p => (JsonNode?)p).ToArray()),
            ["lyricsLines"] = result.LyricsLineCount,
            ["fontSets"] = result.FontSetCount,
            ["sizedFontSets"] = new JsonArray((result.SizedFontSets ?? Array.Empty<string>()).Select(n => (JsonNode?)n).ToArray()),
            ["warnings"] = new JsonArray(result.Warnings.Select(w => (JsonNode?)w).ToArray()),
            ["basePath"] = settings.BasePath,
            ["checks"] = McpChecksJson(),
            ["statusText"] = ViewModel.StatusText,
        };
    }

    // ------------------------------------------------------------ 表示時刻

    internal JsonNode McpSetShowTimeSettings(double? lead, double? tail, double? interval, double? protect, double? overlap,
        bool? topLong, bool? emojiYield, bool? layoutAware)
    {
        static double Seconds(double value, string what) =>
            double.IsFinite(value) && value >= 0 && value <= 30 ? value : throw new McpToolException($"{what}は 0〜30 秒で指定してください（{value}）");
        // 先に全部を確かめてから変える（途中で断ったときに一部だけ変わらないように）
        double? a = lead is double l ? Seconds(l, "ワイプ前") : null;
        double? b = tail is double t ? Seconds(t, "ワイプ後") : null;
        double? c = interval is double i ? Seconds(i, "表示間隔") : null;
        double? d = protect is double p ? Seconds(p, "最短表示後") : null;
        double? e = overlap is double o ? Seconds(o, "重ねてよい時間") : null;

        var s = ViewModel.Settings;
        if (a is double va) s.DisplayLeadSeconds = va;
        if (b is double vb) s.DisplayTailSeconds = vb;
        if (c is double vc) s.N3IntervalSeconds = vc;
        if (d is double vd) s.N3ProtectSeconds = vd;
        if (e is double ve) s.N3OverlapSeconds = ve;
        if (topLong is bool tl) s.N3TopLong = tl;
        if (emojiYield is bool ey) s.N3EmojiLeadYield = ey;
        if (layoutAware is bool la) s.N3LayoutAwareRows = la;
        s.Save();

        LineSide.RefreshShowTimePane();
        OnShowTimeSettingsChanged();
        ViewModel.StatusText = "表示時刻のパラメーターを変えました（行に持たせた表示時刻は、自動調整を実行するまで変わりません）";
        ValidateNowKeepingStatus();
        LineSide.RefreshShowTimeSummary();
        return new JsonObject
        {
            ["settings"] = McpShowTimeSettingsJson(withSummary: true),
            ["checks"] = McpChecksJson(),
            ["statusText"] = ViewModel.StatusText,
        };
    }

    internal JsonNode McpRunShowTimeAdjust()
    {
        var result = RunAutoShowTimes() ?? throw new McpToolException(ViewModel.StatusText);
        return new JsonObject
        {
            ["adjustedLines"] = result.AutoLines,
            ["manualLines"] = result.ManualLines,
            ["untimedLines"] = result.UntimedLines,
            ["checks"] = McpChecksJson(),
            ["statusText"] = ViewModel.StatusText,
        };
    }

    internal JsonNode McpSetLineShowTime(string? tab, int line, string? begin, string? end)
    {
        McpUseTab(tab);
        int index = McpLineIndex(line);
        var model = ViewModel.Document.Lines[index];
        if (model.IsEmpty) throw new McpToolException($"{line} 行目は空行（ページ区切り）です");
        if (begin is null && end is null) throw new McpToolException("begin か end を指定してください（\"auto\" で手動の値を外して自動に戻します）");

        static (bool Set, int? Cs) Read(string? text, string what) =>
            text is null ? (false, null)
            : text.Trim() is "auto" or "AUTO" or "Auto" or "自動" ? (true, null)
            : (true, McpTime.Parse(text, what));
        var (setBegin, beginCs) = Read(begin, "表示開始");
        var (setEnd, endCs) = Read(end, "表示終了");
        int? newBegin = setBegin ? beginCs : model.ShowBeginCs;
        int? newEnd = setEnd ? endCs : model.ShowEndCs;
        if (newBegin is int nb && newEnd is int ne && nb >= ne)
        {
            throw new McpToolException($"表示開始（{McpTime.Format(nb)}）は表示終了（{McpTime.Format(ne)}）より前にしてください");
        }

        bool changed = ViewModel.SetLineShowTimeSides(index, setBegin, beginCs, setEnd, endCs);
        ViewModel.Lines[index].RaiseOverrideMark();
        ViewModel.StatusText = !changed
            ? $"{line} 行目の表示時刻は変わりませんでした"
            : (setBegin && beginCs is null) || (setEnd && endCs is null)
                ? $"{line} 行目の表示時刻の手動の指定を外しました"
                : $"{line} 行目の表示時刻を手で指定しました（自動調整を実行し直しても変わりません）";
        RefreshN3LinePanel();
        ValidateNowKeepingStatus();
        return McpChangedLinesJson(new[] { index }, changed ? 1 : 0);
    }

    // ------------------------------------------------------------ 行設定（フォント・レイアウト・文字の大きさ）

    internal JsonNode McpSetLineFont(string? tab, int[] lines, string? font)
    {
        McpUseTab(tab);
        var indexes = McpLineIndexes(lines);
        string? name = McpFontName(font);
        int n = ViewModel.SetLinesFontSet(indexes, name);
        foreach (var l in ViewModel.Lines) l.RaiseOverrideMark();
        ViewModel.StatusText = n == 0 ? "フォント設定の指定は変わりませんでした（空行・同じ指定の行は変えません）"
            : name is null ? $"{n} 行のフォント指定を自動に戻しました"
            : $"{n} 行にフォント設定「{name}」を指定しました";
        RefreshN3LinePanel();
        ValidateNowKeepingStatus();
        return McpChangedLinesJson(indexes, n);
    }

    internal JsonNode McpSetCharFont(string? tab, int line, int start, int end, string? font)
    {
        McpUseTab(tab);
        int index = McpLineIndex(line);
        var model = ViewModel.Document.Lines[index];
        string text = model.GetDisplayText();
        if (model.IsEmpty || text.Length == 0) throw new McpToolException($"{line} 行目には文字がありません");
        if (start < 0 || end > text.Length || start >= end)
        {
            throw new McpToolException($"文字の範囲は 0 ≦ start < end ≦ {text.Length}（「{text}」の文字数）で指定してください");
        }
        string? name = McpFontName(font);
        int startUnit = MainViewModel.DisplayOffsetToUnitIndex(model, start);
        int endUnit = MainViewModel.DisplayOffsetToUnitIndex(model, end) - 1;
        int n = ViewModel.SetCharFontSet(index, startUnit, endUnit, name);
        ViewModel.Lines[index].RaiseOverrideMark();
        string part = text[start..end];
        ViewModel.StatusText = n == 0 ? $"{line} 行目の「{part}」のフォント指定は変わりませんでした"
            : name is null ? $"{line} 行目の「{part}」のフォント指定を自動に戻しました"
            : $"{line} 行目の「{part}」にフォント設定「{name}」を指定しました";
        RefreshN3LinePanel();
        ValidateNowKeepingStatus();
        return McpChangedLinesJson(new[] { index }, n > 0 ? 1 : 0);
    }

    internal JsonNode McpSetPageLayout(string? tab, int[] lines, string? layout)
    {
        McpUseTab(tab);
        var indexes = McpLineIndexes(lines);
        string? name = null;
        if (!string.IsNullOrWhiteSpace(layout) && layout.Trim() is not ("auto" or "自動"))
        {
            name = layout.Trim();
            var names = ViewModel.GetEffectiveLayouts().Select(l => l.Name).ToList();
            if (!names.Contains(name)) throw new McpToolException($"レイアウト設定「{name}」はありません（{string.Join("・", names)}）");
        }
        int n = ViewModel.SetLinesLayout(indexes, name);
        foreach (var l in ViewModel.Lines) l.RaiseOverrideMark();
        ViewModel.StatusText = n == 0 ? "レイアウトの指定は変わりませんでした"
            : name is null ? $"選んだ行のページ（{n} 行）のレイアウト指定を自動に戻しました"
            : $"選んだ行のページ（{n} 行）にレイアウト設定「{name}」を指定しました";
        _n3LayoutNamesKey = null;
        RefreshN3LinePanel();
        ValidateNowKeepingStatus();
        return McpChangedLinesJson(indexes, n);
    }

    internal JsonNode McpSetPageFontSize(string? tab, int[] lines, int delta)
    {
        McpUseTab(tab);
        var indexes = McpLineIndexes(lines);
        if (delta < N3PageFontSize.MinDelta || delta > N3PageFontSize.MaxDelta)
        {
            throw new McpToolException($"文字の大きさの増減は {N3PageFontSize.MinDelta}〜{N3PageFontSize.MaxDelta} px で指定してください");
        }
        int n = ViewModel.SetLinesFontSizeDelta(indexes, delta);
        foreach (var l in ViewModel.Lines) l.RaiseOverrideMark();
        ViewModel.StatusText = n == 0 ? "文字の大きさは変わりませんでした"
            : delta == 0 ? $"選んだ行のページ（{n} 行）の文字の大きさを元に戻しました"
            : $"選んだ行のページ（{n} 行）の文字の大きさを {N3PageFontSize.Signed(delta)} px にしました（n3proj の書き出しで、文字の大きさだけを変えたフォント設定を作って当てます）";
        RefreshN3LinePanel();
        ValidateNowKeepingStatus();
        return McpChangedLinesJson(indexes, n);
    }

    internal JsonNode McpClearLineOverrides(string? tab, int[] lines)
    {
        McpUseTab(tab);
        var indexes = McpLineIndexes(lines);
        int n = ViewModel.ClearLineOverrides(indexes);
        foreach (var l in ViewModel.Lines) l.RaiseOverrideMark();
        ViewModel.StatusText = n > 0
            ? $"{n} 行の表示時刻・フォント・レイアウト・文字の大きさ・字幕アクションの指定を解除しました"
            : "指定のある行はありません";
        RefreshN3LinePanel();
        ValidateNowKeepingStatus();
        return McpChangedLinesJson(indexes, n);
    }

    /// <summary>
    /// 道具の字幕アクションの指定（Id か表示名）。"default"・"既定"・空は null（曲の既定に戻す）。知らないものは断る。
    /// </summary>
    private static N3SubtitleActionKind? McpActionKind(string? action)
    {
        string text = (action ?? "").Trim();
        if (text.Length == 0 || string.Equals(text, "default", StringComparison.OrdinalIgnoreCase) || text is "既定" or "（既定）") return null;
        // Id（"SHINTA." は省いてもよい。大文字・小文字は問わない）か、ニコカラメーカー3 の表示名（「文字単位フェード」など）
        var kind = N3SubtitleActionCatalog.Known.FirstOrDefault(k =>
                string.Equals(k.Id, text, StringComparison.OrdinalIgnoreCase)
                || string.Equals(k.Id, "SHINTA." + text, StringComparison.OrdinalIgnoreCase))
            ?? N3SubtitleActionCatalog.Known.FirstOrDefault(k => k.Name == text);
        return kind ?? throw new McpToolException(
            $"字幕アクション「{text}」はありません。使えるもの: {string.Join("・", N3SubtitleActionCatalog.Known.Select(k => $"{k.Id}（{k.Name}）"))}、" +
            "曲の既定に戻すなら \"default\"");
    }

    internal JsonNode McpSetLineAction(string? tab, int[] lines, string? action, bool wholePage)
    {
        McpUseTab(tab);
        var indexes = McpLineIndexes(lines);
        var kind = McpActionKind(action);
        // 設定値は曲の既定と同じ種類ならその値、違えばその種類の既定値（右のパネル「レイアウト」・レイアウト設定ビューで選ぶのと同じ）
        N3SubtitleAction? value = kind is null ? null : ViewModel.CreatePageSubtitleAction(kind.Id);
        int n = ViewModel.SetLinesSubtitleAction(indexes, value, wholePage);
        foreach (var l in ViewModel.Lines) l.RaiseOverrideMark();
        string target = wholePage ? $"選んだ行のページ（{n} 行）" : $"{n} 行";
        ViewModel.StatusText = n == 0 ? "字幕アクションの指定は変わりませんでした"
            : value is null ? $"{target}の字幕アクションの指定を外しました（曲の既定に戻します）"
            : $"{target}に字幕アクション「{kind!.Name}」を指定しました";
        RefreshN3LinePanel();
        LineSide.RefreshPagePane();
        ValidateNowKeepingStatus();
        return McpChangedLinesJson(indexes, n);
    }

    // ------------------------------------------------------------ 元に戻す・画面

    internal JsonNode McpUndoRedo(bool redo)
    {
        bool done = redo ? ViewModel.Redo() : ViewModel.Undo();
        if (done)
        {
            AfterUndoRedo();
            RefreshN3LinePanel();
        }
        ValidateNowKeepingStatus();
        return new JsonObject
        {
            ["done"] = done,
            ["tab"] = ViewModel.ActiveTab.Name,
            ["undoLeft"] = ViewModel.ActiveTab.UndoStack.Count,
            ["redoLeft"] = ViewModel.ActiveTab.RedoStack.Count,
            ["checks"] = McpChecksJson(),
            ["statusText"] = ViewModel.StatusText,
        };
    }

    internal JsonNode McpSelectLines(string? tab, int[]? lines)
    {
        McpUseTab(tab);
        var indexes = lines is { Length: > 0 } ? McpLineIndexes(lines) : new List<int>();
        LineList.SelectedItems.Clear();
        foreach (int i in indexes) LineList.SelectedItems.Add(ViewModel.Lines[i]);
        if (indexes.Count > 0)
        {
            LineList.ScrollIntoView(ViewModel.Lines[indexes[0]]);
            if (InsertViewActive)
            {
                InsertEditor.SelectionStart = Math.Min(ViewModel.GetInsertViewLineStart(indexes[0]), InsertEditor.Text.Length);
            }
        }
        ViewModel.StatusText = indexes.Count == 0 ? "行の選択を外しました" : $"{indexes.Count} 行を選びました（{string.Join("・", indexes.Take(10).Select(i => i + 1))}{(indexes.Count > 10 ? " など" : "")} 行目）";
        return new JsonObject
        {
            ["selectedLines"] = new JsonArray(SelectedIndexes.Select(i => (JsonNode?)(i + 1)).ToArray()),
            ["statusText"] = ViewModel.StatusText,
        };
    }

    // ------------------------------------------------------------ あとから起動したインスタンスの引数

    /// <summary>
    /// あとから起動したインスタンス（exe へのドロップ・2 回目の起動）の引数を受け取った（UI スレッド。Program.cs → App）。
    /// 画面を前に出し、ファイルがあれば開く（n3proj は読み込み確認画面、動画・音声はメディア再生。歌詞は、保存していない変更があれば尋ねる）。
    /// </summary>
    internal async void OpenFromAnotherInstance(string? path)
    {
        try
        {
            BringToFront();
            if (path is null) return;
            string ext = Path.GetExtension(path);
            if (ext.Equals(".n3proj", StringComparison.OrdinalIgnoreCase))
            {
                await ImportN3ProjAsync(path);
                return;
            }
            if (MediaExtensions.Contains(ext))
            {
                OpenMedia(path);
                return;
            }
            if (ViewModel.HasUnsavedChanges && !await ConfirmDiscardAsync(Path.GetFileName(path))) return;
            TryRun(() => ViewModel.OpenFile(path));
            AfterDocumentLoaded();
            if (InsertViewActive) RefreshInsertView(0);
        }
        catch (Exception ex)
        {
            DebugLog($"ほかのインスタンスから渡されたファイルを開けませんでした: {path}: {ex}");
            ViewModel.StatusText = $"エラー: {ErrorText.Describe(ex)}";
        }
    }

    /// <summary>保存していない変更を捨てて開くか尋ねる（ほかのダイアログを開いているときは尋ねずに開かない）。</summary>
    private async Task<bool> ConfirmDiscardAsync(string fileName)
    {
        if (IsModalDialogOpen())
        {
            ViewModel.StatusText = $"ほかの画面を開いているため、{fileName} は開きませんでした（画面を閉じてから、もう一度開いてください）";
            return false;
        }
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "ファイルを開く",
            Content = $"保存していない変更があります。変更を破棄して {fileName} を開きますか？",
            PrimaryButtonText = "破棄して開く",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Close,
        };
        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>ウィンドウを前に出す（最小化していれば元に戻す）。</summary>
    private void BringToFront()
    {
        if (App.StartedHidden) return; // 検証用の見えない起動
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter &&
            presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized)
        {
            presenter.Restore();
        }
        Activate();
        SetForegroundWindow(Hwnd);
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
