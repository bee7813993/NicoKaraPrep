using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using NicoKaraPrep.App.Services.Mcp;
using NicoKaraPrep.Core;

namespace NicoKaraPrep.App.Views;

/// <summary>
/// 「AI エージェントと連携」（ファイル > AI エージェントと連携...）。Claude Desktop・Claude Code・Codex への登録をボタン 1 つで行う
/// （処理は <see cref="ClaudeLink"/>・<see cref="CodexLink"/>）。
/// 開いたときに今の登録の状態を出す。Claude Desktop へ拡張機能を渡したあとは、インストールされるのを見張って知らせる（5 分まで）。
/// ボタンの無い AI エージェント（Cursor・Gemini CLI・VS Code など）に書く内容は「ほかの AI エージェントで使う」の中、
/// 手作業の登録（コマンド・設定ファイルに書く内容）は「うまくいかないとき」の中（ボタンが失敗したときは開いて見せる）。
/// </summary>
public sealed partial class AgentLinkDialog : ContentDialog
{
    private static readonly TimeSpan WatchLength = TimeSpan.FromMinutes(5);

    private readonly Func<bool> _isMcpEnabled;
    private readonly Action _enableMcp;
    private DispatcherQueueTimer? _watchTimer;
    private DateTime _watchUntil;
    private DateTime? _watchBaseline;
    private bool _watchBusy;
    private bool _busy;
    private bool _closed;

    /// <param name="isMcpEnabled">MCP サーバーを有効にしているか（ファイル > 設定 の MCP）。</param>
    /// <param name="enableMcp">MCP サーバーを有効にして待ち受けを始める（設定も保存する）。</param>
    public AgentLinkDialog(Func<bool> isMcpEnabled, Action enableMcp)
    {
        InitializeComponent();
        _isMcpEnabled = isMcpEnabled;
        _enableMcp = enableMcp;
        ExePathBox.Text = McpInfo.RegistrationExePath;
        McpJsonBox.Text = McpInfo.McpServersJson;
        McpCommandBox.Text = McpInfo.ClaudeCodeCommand;
        CodexCommandBox.Text = McpInfo.CodexCommand;
        CodexTomlText.Text = "「codex」が見つからないと言われたとき（Codex アプリだけを使っているときなど）は、" +
                             $"Codex の設定ファイル（{CodexLink.ConfigPath}）の終わりに次を書き足して、Codex を起動し直します。";
        CodexTomlBox.Text = McpInfo.CodexConfigEntry;
        UpdateMcpOffBar();
        Opened += async (_, _) => await RefreshStatusAsync();
        Closed += (_, _) =>
        {
            _closed = true;
            _watchTimer?.Stop();
        };
    }

    // ------------------------------------------------------------ 状態

    private void UpdateMcpOffBar() => McpOffBar.IsOpen = !_isMcpEnabled();

    private void OnEnableMcpClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _enableMcp();
        }
        catch (Exception ex)
        {
            ShowResult(new AgentLinkResult(false, "MCP サーバーを有効にできませんでした", ErrorText.Describe(ex)));
        }
        UpdateMcpOffBar();
    }

    /// <summary>ボタンの横の、今の登録の状態を読み直す（設定ファイル・拡張機能のフォルダを読むだけ）。</summary>
    private async Task RefreshStatusAsync()
    {
        try
        {
            var (code, claudeFound, desktop, codex, codexFound) = await Task.Run(() =>
                (ClaudeLink.ReadClaudeCodeStatus(), ClaudeLink.FindClaudeCode().Count > 0, ClaudeLink.ReadClaudeDesktopStatus(),
                 CodexLink.ReadStatus(), CodexLink.FindCodex().Count > 0));
            if (_closed) return;
            CodeStatusText.Text = ClaudeLink.DescribeClaudeCode(code, claudeFound);
            DesktopStatusText.Text = ClaudeLink.DescribeClaudeDesktop(desktop);
            CodexStatusText.Text = CodexLink.Describe(codex, codexFound);
        }
        catch (Exception)
        {
            if (_closed) return;
            CodeStatusText.Text = "";
            DesktopStatusText.Text = "";
            CodexStatusText.Text = "";
        }
    }

    // ------------------------------------------------------------ Claude Code・Codex（コマンドで登録）

    private async void OnCodeClick(object sender, RoutedEventArgs e) =>
        await RegisterAsync("Claude Code に登録しています…", ClaudeLink.RegisterClaudeCodeAsync, "Claude Code に登録できませんでした");

    private async void OnCodexClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (await Task.Run(() => CodexLink.FindCodex().Count) == 0)
        {
            if (_closed) return;
            ShowResult(
                new AgentLinkResult(false, "Codex が見つかりません",
                    "Codex（ChatGPT の Codex アプリか Codex CLI）を入れてから、もう一度押してください。"),
                ("ダウンロードのページを開く", () => ClaudeLink.OpenUrl(CodexLink.CodexDownloadUrl)));
            return;
        }
        await RegisterAsync("Codex に登録しています…", CodexLink.RegisterAsync, "Codex に登録できませんでした");
    }

    /// <summary>コマンド（claude・codex）で登録するボタンの共通の流れ。終わったら状態を読み直す。</summary>
    private async Task RegisterAsync(string progress, Func<CancellationToken, Task<AgentLinkResult>> register, string failTitle)
    {
        if (_busy) return;
        SetBusy(true);
        ShowProgress(progress);
        AgentLinkResult result;
        try
        {
            result = await Task.Run(() => register(CancellationToken.None));
        }
        catch (Exception ex)
        {
            result = new AgentLinkResult(false, failTitle, ErrorText.Describe(ex));
        }
        SetBusy(false);
        if (_closed) return;
        ShowResult(result);
        await RefreshStatusAsync();
    }

    // ------------------------------------------------------------ Claude Desktop

    private async void OnDesktopClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        string? launcher = ClaudeLink.FindClaudeDesktop();
        if (launcher is null && !ClaudeLink.IsClaudeDesktopInstalled())
        {
            ShowResult(
                new AgentLinkResult(false, "Claude Desktop が見つかりません", "Claude Desktop を入れてから、もう一度押してください。"),
                ("ダウンロードのページを開く", () => ClaudeLink.OpenUrl(ClaudeLink.ClaudeDesktopDownloadUrl)));
            return;
        }
        SetBusy(true);
        try
        {
            var before = await Task.Run(ClaudeLink.ReadClaudeDesktopStatus);
            string path = await Task.Run(() => ClaudeLink.CreateMcpb(ClaudeLink.DownloadsFolder));
            if (launcher is null)
            {
                // 入っているが起動のしかたが無い（アプリ実行エイリアスを切っている）: ファイルを見せて、ドラッグで入れてもらう
                ClaudeLink.ShowInExplorer(path);
                if (_closed) return;
                ShowResult(
                    new AgentLinkResult(true, "拡張機能のファイルを Claude Desktop へドラッグしてください",
                        $"Claude Desktop を開き、設定 → 拡張機能 の画面へ、いま開いたフォルダの {Path.GetFileName(path)} をドラッグします。" +
                        "「インストールしますか？」が出たら「インストール」を押します。"),
                    ("ファイルの場所を開く", () => ClaudeLink.ShowInExplorer(path)),
                    informational: true);
                StartWatching(before);
                return;
            }
            ClaudeLink.OpenInClaudeDesktop(launcher, path);
            if (_closed) return;
            ShowResult(
                new AgentLinkResult(true, "Claude Desktop で「インストール」を押してください",
                    "Claude Desktop に「拡張機能をインストールしますか？」が出ます（見えないときはタスクバーの Claude を開きます）。" +
                    "「Anthropic によって確認されていません」とも出ますが、このにこぷれっぷが作った拡張機能なので、そのまま「インストール」を押してください。" +
                    $"画面が出てこないときは、Claude Desktop の 設定 → 拡張機能 の画面へ、ダウンロード フォルダの {Path.GetFileName(path)} をドラッグして入れます。"),
                ("ファイルの場所を開く", () => ClaudeLink.ShowInExplorer(path)),
                informational: true);
            StartWatching(before);
        }
        catch (Exception ex)
        {
            if (!_closed) ShowResult(new AgentLinkResult(false, "Claude Desktop に渡せませんでした", ErrorText.Describe(ex)));
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>「うまくいかないとき」: 拡張機能のファイルを作って、エクスプローラーでその場所を開く（ドラッグして入れる用）。</summary>
    private async void OnMcpbFolderClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            var before = await Task.Run(ClaudeLink.ReadClaudeDesktopStatus);
            string path = await Task.Run(() => ClaudeLink.CreateMcpb(ClaudeLink.DownloadsFolder));
            ClaudeLink.ShowInExplorer(path);
            if (_closed) return;
            ShowResult(
                new AgentLinkResult(true, "拡張機能のファイルを作りました",
                    $"{path} を、Claude Desktop の 設定 → 拡張機能 の画面へドラッグして入れます。「インストールしますか？」が出たら「インストール」を押します。"),
                informational: true);
            StartWatching(before);
        }
        catch (Exception ex)
        {
            if (!_closed) ShowResult(new AgentLinkResult(false, "拡張機能のファイルを作れませんでした", ErrorText.Describe(ex)));
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>
    /// Claude Desktop に入るのを見張る（2 秒ごと、5 分まで）。入ったら知らせる。
    /// 渡す前から同じ拡張機能が入っていたときは、入れ直されたこと（manifest.json の更新日時が変わったこと）で見分ける。
    /// </summary>
    private void StartWatching(ClaudeDesktopStatus before)
    {
        _watchBaseline = before.PointsToThisApp ? before.ManifestTime : null;
        _watchUntil = DateTime.UtcNow + WatchLength;
        if (_watchTimer is null)
        {
            _watchTimer = DispatcherQueue.CreateTimer();
            _watchTimer.Interval = TimeSpan.FromSeconds(2);
            _watchTimer.Tick += async (_, _) => await WatchTickAsync();
        }
        _watchTimer.Start();
    }

    private async Task WatchTickAsync()
    {
        if (_watchBusy || _closed) return;
        if (DateTime.UtcNow > _watchUntil)
        {
            _watchTimer?.Stop();
            return;
        }
        _watchBusy = true;
        try
        {
            var status = await Task.Run(ClaudeLink.ReadClaudeDesktopStatus);
            if (_closed || !status.PointsToThisApp) return;
            if (_watchBaseline is not null && status.ManifestTime == _watchBaseline) return; // まだ入れ直されていない
            _watchTimer?.Stop();
            DesktopStatusText.Text = ClaudeLink.DescribeClaudeDesktop(status);
            ShowResult(new AgentLinkResult(true, "Claude Desktop に追加しました",
                "Claude Desktop のチャットから、にこぷれっぷを操作できます（使うときは、にこぷれっぷを起動しておきます）。"));
        }
        catch (Exception)
        {
            // 次の回に読み直す
        }
        finally
        {
            _watchBusy = false;
        }
    }

    // ------------------------------------------------------------ 結果の表示

    private void SetBusy(bool busy)
    {
        _busy = busy;
        DesktopButton.IsEnabled = !busy;
        CodeButton.IsEnabled = !busy;
        CodexButton.IsEnabled = !busy;
        McpbFolderButton.IsEnabled = !busy;
    }

    private void ShowProgress(string title)
    {
        ResultBar.Severity = InfoBarSeverity.Informational;
        ResultBar.Title = title;
        ResultBar.Message = "";
        ResultBar.ActionButton = null;
        ResultBar.Content = new ProgressBar { IsIndeterminate = true, Margin = new Thickness(0, 0, 0, 12) };
        ResultBar.IsOpen = true;
    }

    /// <summary>
    /// ボタンの結果を出す。失敗したときはコマンドの出力（あれば）を中に出し、手作業の登録のしかたを開いて見せる。
    /// informational は「まだ終わっていない（相手の画面で続きをする）」案内。
    /// </summary>
    private void ShowResult(AgentLinkResult result, (string Label, Action Run)? action = null, bool informational = false)
    {
        ResultBar.Severity = !result.Success ? InfoBarSeverity.Error
            : informational ? InfoBarSeverity.Informational
            : InfoBarSeverity.Success;
        ResultBar.Title = result.Title;
        ResultBar.Message = result.Message;
        ResultBar.ActionButton = action is { } a ? MakeActionButton(a.Label, a.Run) : null;
        ResultBar.Content = !result.Success && result.Detail is { Length: > 0 } detail
            ? new TextBox
            {
                Text = detail,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                MaxHeight = 160,
                Margin = new Thickness(0, 0, 0, 12),
            }
            : null;
        ResultBar.IsOpen = true;
        if (!result.Success) ManualExpander.IsExpanded = true;
    }

    private ButtonBase MakeActionButton(string label, Action run)
    {
        var button = new Button { Content = label };
        button.Click += (_, _) =>
        {
            try
            {
                run();
            }
            catch (Exception ex)
            {
                ShowResult(new AgentLinkResult(false, "開けませんでした", ErrorText.Describe(ex)));
            }
        };
        return button;
    }

    // ------------------------------------------------------------ 設定に書く内容（コピー）

    private void OnCopyExePathClick(object sender, RoutedEventArgs e) => CopyText(ExePathBox.Text, ExePathCopyButton);

    private void OnCopyMcpJsonClick(object sender, RoutedEventArgs e) => CopyText(McpJsonBox.Text, McpJsonCopyButton);

    private void OnCopyMcpCommandClick(object sender, RoutedEventArgs e) => CopyText(McpCommandBox.Text, McpCommandCopyButton);

    private void OnCopyCodexCommandClick(object sender, RoutedEventArgs e) => CopyText(CodexCommandBox.Text, CodexCommandCopyButton);

    private void OnCopyCodexTomlClick(object sender, RoutedEventArgs e) => CopyText(CodexTomlBox.Text, CodexTomlCopyButton);

    /// <summary>クリップボードへ写し、ボタンの文字で知らせる。</summary>
    private static void CopyText(string text, Button button)
    {
        try
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            button.Content = "コピーしました";
        }
        catch (Exception)
        {
            button.Content = "コピーできません";
        }
    }
}
