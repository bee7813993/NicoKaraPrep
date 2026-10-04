using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace NicoKaraPrep.App.Services.Mcp;

/// <summary>
/// Claude への登録をボタン 1 つで行う処理（ファイル > Claude と連携 の画面と、検証用の <c>--claude-link</c>）。
/// どちらも、つなぐ先は MCP の橋渡し（<see cref="McpInfo.RegistrationExePath"/> に <c>--mcp</c> を付けたもの）。
/// <list type="bullet">
/// <item>Claude Code: claude.exe を探して（PATH・%USERPROFILE%\.local\bin・npm・Claude Desktop に入っているもの）、
/// 「claude mcp add --scope user nicokaraprep -- "exe" --mcp」を実行する（別の exe が登録されていれば消してから）。
/// 登録先の設定ファイル（~/.claude.json）を読み直して、この exe になったことを確かめる。</item>
/// <item>Claude Desktop: 拡張機能のファイル（nicokaraprep.mcpb。中身は manifest.json とアイコンだけで、この exe を --mcp で起動する）を
/// ダウンロード フォルダに作り、Claude Desktop に渡す（「拡張機能をインストールしますか？」が出て、インストール を押すと使える）。
/// ストア版の Claude Desktop は .mcpb を開くアプリとして登録されていないため、実行エイリアス claude-desktop.exe にファイルのパスを渡す
/// （Claude Desktop は起動の引数の .mcpb をインストールの確認として開く。別名が ccd のときだけ Code のフォルダとして扱う）。</item>
/// </list>
/// </summary>
internal static class ClaudeLink
{
    /// <summary>Claude に登録する MCP サーバーの名前（Claude Code の名前・拡張機能の name）。</summary>
    public const string ServerKey = "nicokaraprep";

    /// <summary>拡張機能のファイルの名前。</summary>
    public const string McpbFileName = "nicokaraprep.mcpb";

    /// <summary>Claude Desktop のダウンロードのページ。</summary>
    public const string ClaudeDesktopDownloadUrl = "https://claude.ai/download";

    /// <summary>claude の 1 回の実行を待つ長さ。</summary>
    private static readonly TimeSpan ClaudeTimeout = TimeSpan.FromSeconds(60);

    private const string IconResourceName = "NicoKaraPrep.McpbIcon.png";

    private static string UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string RoamingAppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>登録されている起動のしかたが、このにこぷれっぷ（の橋渡し）か。</summary>
    public static bool IsThisApp(string? command, IReadOnlyList<string> args) =>
        command is { Length: > 0 }
        && SamePath(command, McpInfo.RegistrationExePath)
        && args.Any(a => a.Equals("--mcp", StringComparison.OrdinalIgnoreCase));

    private static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a.Trim().Trim('"')), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    // ============================================================ Claude Code

    /// <summary>
    /// Claude Code の設定ファイル（ユーザーの MCP サーバーの登録先）。Claude Code と同じ決まりで、
    /// CLAUDE_CONFIG_DIR があればその中の .claude.json、無ければ %USERPROFILE%\.claude.json（古い版の .config.json が残っていればそちら）。
    /// </summary>
    public static string ClaudeCodeConfigPath
    {
        get
        {
            string? custom = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            bool hasCustom = !string.IsNullOrWhiteSpace(custom);
            string configHome = hasCustom ? custom! : Path.Combine(UserProfile, ".claude");
            string legacy = Path.Combine(configHome, ".config.json");
            if (File.Exists(legacy)) return legacy;
            return Path.Combine(hasCustom ? custom! : UserProfile, ".claude.json");
        }
    }

    /// <summary>Claude Code にこのアプリが登録されているか（設定ファイルを読むだけ。claude は動かさない）。</summary>
    public static ClaudeCodeStatus ReadClaudeCodeStatus()
    {
        string path = ClaudeCodeConfigPath;
        try
        {
            if (!File.Exists(path)) return new ClaudeCodeStatus(path, false, false, null, [], null);
            JsonNode? root;
            // Claude Code が書いている途中でも読めるように、共有で開く
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                root = JsonNode.Parse(stream, documentOptions: new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                });
            }
            if (root?["mcpServers"]?[ServerKey] is not JsonObject entry) return new ClaudeCodeStatus(path, true, false, null, [], null);
            return new ClaudeCodeStatus(path, true, true, ReadString(entry["command"]), ReadStrings(entry["args"]), null);
        }
        catch (Exception ex)
        {
            return new ClaudeCodeStatus(path, File.Exists(path), false, null, [], ex.Message);
        }
    }

    /// <summary>画面に出す、Claude Code への登録の状態。</summary>
    public static string DescribeClaudeCode(ClaudeCodeStatus status, bool claudeFound)
    {
        if (status.PointsToThisApp) return "登録済みです";
        if (status.ReadError is not null) return "登録の状態を読めませんでした（押すと登録し直します）";
        if (status.Registered) return "別の場所のにこぷれっぷが登録されています（押すと、このにこぷれっぷに登録し直します）";
        return claudeFound ? "まだ登録されていません" : "Claude Code が見つかりません";
    }

    /// <summary>
    /// このパソコンの claude（Claude Code）を、使う順に探す。
    /// PATH（このプロセスのものと、あとから入れた分も見えるようにレジストリのユーザー・システムのもの）、
    /// ネイティブのインストーラーの既定の場所、npm で入れたもの、Claude Desktop に入っているもの（新しい版から）。
    /// どれも設定ファイル（~/.claude.json）は同じなので、どれで登録しても同じ。
    /// </summary>
    public static IReadOnlyList<ClaudeCodeExe> FindClaudeCode()
    {
        var found = new List<ClaudeCodeExe>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string path, string source)
        {
            try
            {
                path = Path.GetFullPath(path);
                if (File.Exists(path) && seen.Add(path)) found.Add(new ClaudeCodeExe(path, source));
            }
            catch (Exception)
            {
                // 読めない場所は飛ばす
            }
        }

        var pathDirectories = PathDirectories().ToList();
        foreach (string dir in pathDirectories) Add(Path.Combine(dir, "claude.exe"), "PATH");
        Add(Path.Combine(UserProfile, ".local", "bin", "claude.exe"), "Claude Code（%USERPROFILE%\\.local\\bin）");
        foreach (string dir in pathDirectories) Add(Path.Combine(dir, "claude.cmd"), "PATH（npm）");
        Add(Path.Combine(RoamingAppData, "npm", "claude.cmd"), "npm");
        foreach (string exe in ClaudeDesktopBundledCli()) Add(exe, "Claude Desktop");
        return found;
    }

    private static IEnumerable<string> PathDirectories()
    {
        string?[] values =
        [
            Environment.GetEnvironmentVariable("PATH"),
            ReadRegistryPath(Registry.CurrentUser, "Environment"),
            ReadRegistryPath(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment"),
        ];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string? value in values)
        {
            if (string.IsNullOrEmpty(value)) continue;
            foreach (string raw in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string dir = Environment.ExpandEnvironmentVariables(raw.Trim('"'));
                if (dir.Length == 0 || !Path.IsPathFullyQualified(dir)) continue;
                if (seen.Add(dir)) yield return dir;
            }
        }
    }

    private static string? ReadRegistryPath(RegistryKey root, string subKey)
    {
        try
        {
            using var key = root.OpenSubKey(subKey);
            return key?.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Claude Desktop に入っている Claude Code（Code タブが使うもの）。新しい版から。</summary>
    private static IEnumerable<string> ClaudeDesktopBundledCli()
    {
        foreach (string data in ClaudeDesktopDataDirectories())
        {
            string[] versions;
            try
            {
                versions = Directory.GetDirectories(Path.Combine(data, "claude-code"));
            }
            catch (Exception)
            {
                continue;
            }
            foreach (string versionDir in versions.OrderByDescending(d => ParseVersion(Path.GetFileName(d))))
            {
                string direct = Path.Combine(versionDir, "claude.exe");
                if (File.Exists(direct)) yield return direct;
                string[] builds;
                try
                {
                    builds = Directory.GetDirectories(versionDir);
                }
                catch (Exception)
                {
                    continue;
                }
                foreach (string build in builds)
                {
                    string exe = Path.Combine(build, "claude.exe");
                    if (File.Exists(exe)) yield return exe;
                }
            }
        }
    }

    private static Version ParseVersion(string text) => Version.TryParse(text, out var v) ? v : new Version(0, 0);

    /// <summary>
    /// Claude Code に登録する（ボタンの処理）。別の exe が登録されていれば消してから足し、設定ファイルを読み直して確かめる。
    /// 見つかった claude で順に試し、どれかで登録できれば成功。
    /// </summary>
    public static async Task<ClaudeLinkResult> RegisterClaudeCodeAsync(CancellationToken ct = default)
    {
        const string usable = "Claude Code で新しく始めた会話から、にこぷれっぷを操作できます（登録の前から開いていた会話には出てきません）。";
        var before = ReadClaudeCodeStatus();
        if (before.PointsToThisApp) return new ClaudeLinkResult(true, "Claude Code には登録済みです", usable);

        var candidates = FindClaudeCode();
        if (candidates.Count == 0)
        {
            return new ClaudeLinkResult(false, "Claude Code が見つかりません",
                "Claude Code を入れてから、もう一度押してください。Claude Desktop のチャットで使うときは「Claude Desktop に追加」を押します。");
        }

        string exe = McpInfo.RegistrationExePath;
        var log = new StringBuilder();
        foreach (var claude in candidates)
        {
            bool viaCmd = claude.Path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);
            if (viaCmd && (exe.Contains('%') || exe.Contains('"')))
            {
                log.AppendLine($"{claude.Path}: この場所の exe はコマンドプロンプト経由では渡せないため飛ばしました");
                continue;
            }
            try
            {
                if (ReadClaudeCodeStatus().Registered)
                {
                    var removed = await RunClaudeAsync(claude, ["mcp", "remove", ServerKey, "--scope", "user"], ct);
                    AppendRun(log, claude, "mcp remove", removed);
                }
                var added = await RunClaudeAsync(claude, ["mcp", "add", "--scope", "user", ServerKey, "--", exe, "--mcp"], ct);
                AppendRun(log, claude, "mcp add", added);
                var after = ReadClaudeCodeStatus();
                if (after.PointsToThisApp)
                {
                    string message = usable + (claude.Source == "Claude Desktop" ? "（Claude Desktop に入っている Claude Code で登録しました）" : "");
                    return new ClaudeLinkResult(true, "Claude Code に登録しました", message, log.ToString().Trim());
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                log.AppendLine($"{claude.Path}: {ex.Message}");
            }
        }
        return new ClaudeLinkResult(false, "Claude Code に登録できませんでした",
            "「うまくいかないとき」のコマンドを PowerShell に貼り付けて実行しても登録できます。", log.ToString().Trim());
    }

    private static void AppendRun(StringBuilder log, ClaudeCodeExe claude, string what, (int ExitCode, string Output) run)
    {
        log.AppendLine($"{claude.Path} {what}（終了コード {run.ExitCode}）");
        if (run.Output.Length > 0) log.AppendLine(run.Output);
    }

    private static readonly Regex AnsiEscape = new(@"\x1B\[[0-9;?]*[ -/]*[@-~]|\x1B\][^\x07]*\x07", RegexOptions.Compiled);

    /// <summary>claude を画面を出さずに動かし、終了コードと出力（標準出力と標準エラー）を返す。</summary>
    private static async Task<(int ExitCode, string Output)> RunClaudeAsync(ClaudeCodeExe claude, IReadOnlyList<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            WorkingDirectory = UserProfile,
        };
        if (claude.Path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            // npm の claude.cmd はコマンドプロンプト経由で動かす（/s で外側の引用符だけを外す）
            psi.FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            psi.Arguments = "/d /s /c \"" + string.Join(" ", new[] { claude.Path }.Concat(args).Select(a => "\"" + a + "\"")) + "\"";
        }
        else
        {
            psi.FileName = claude.Path;
            foreach (string a in args) psi.ArgumentList.Add(a);
        }
        psi.Environment["NO_COLOR"] = "1"; // 色の制御文字を出さない

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("claude を起動できません");
        process.StandardInput.Close();
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ClaudeTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception)
            {
                // もう終わっている
            }
            ct.ThrowIfCancellationRequested();
            throw new TimeoutException($"claude が {ClaudeTimeout.TotalSeconds:0} 秒たっても終わりませんでした");
        }
        string output = ((await stdout).Trim() + "\n" + (await stderr).Trim()).Trim();
        return (process.ExitCode, AnsiEscape.Replace(output, ""));
    }

    // ============================================================ Claude Desktop

    /// <summary>Claude Desktop のデータの置き場所の候補（ストアの MSIX 版はパッケージの中の LocalCache、以前のインストーラーの版は %APPDATA%\Claude）。</summary>
    private static IEnumerable<string> ClaudeDesktopDataDirectories()
    {
        string[] packaged = [];
        try
        {
            packaged = Directory.GetDirectories(Path.Combine(LocalAppData, "Packages"), "Claude_*");
        }
        catch (Exception)
        {
            // 無ければ飛ばす
        }
        foreach (string dir in packaged) yield return Path.Combine(dir, "LocalCache", "Roaming", "Claude");
        yield return Path.Combine(RoamingAppData, "Claude");
    }

    /// <summary>
    /// Claude Desktop を起動するもの。ストア版は実行エイリアス claude-desktop.exe、以前のインストーラーの版は %LOCALAPPDATA%\AnthropicClaude\claude.exe。
    /// どちらも無ければ null（入っていない）。
    /// </summary>
    public static string? FindClaudeDesktop()
    {
        string alias = Path.Combine(LocalAppData, "Microsoft", "WindowsApps", "claude-desktop.exe");
        if (File.Exists(alias)) return alias;
        string squirrel = Path.Combine(LocalAppData, "AnthropicClaude", "claude.exe");
        if (File.Exists(squirrel)) return squirrel;
        return null;
    }

    /// <summary>
    /// Claude Desktop が入っているか。起動するもの（<see cref="FindClaudeDesktop"/>）が無くても、
    /// ストア版のデータのフォルダがあれば入っている（設定の「アプリ実行エイリアス」で claude-desktop.exe を切っているとき）。
    /// </summary>
    public static bool IsClaudeDesktopInstalled()
    {
        if (FindClaudeDesktop() is not null) return true;
        try
        {
            return Directory.GetDirectories(Path.Combine(LocalAppData, "Packages"), "Claude_*").Length > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Claude Desktop にこのアプリの拡張機能が入っているか（拡張機能のフォルダの manifest.json を読むだけ）。</summary>
    public static ClaudeDesktopStatus ReadClaudeDesktopStatus()
    {
        bool found = IsClaudeDesktopInstalled();
        foreach (string data in ClaudeDesktopDataDirectories())
        {
            string[] extensions;
            try
            {
                extensions = Directory.GetDirectories(Path.Combine(data, "Claude Extensions"));
            }
            catch (Exception)
            {
                continue;
            }
            foreach (string dir in extensions)
            {
                // 手元のファイルから入れた拡張機能のフォルダは local.mcpb.<作者>.<name>
                if (!Path.GetFileName(dir).Contains(ServerKey, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    string manifestPath = Path.Combine(dir, "manifest.json");
                    var manifest = JsonNode.Parse(File.ReadAllText(manifestPath));
                    if (ReadString(manifest?["name"]) != ServerKey) continue;
                    var config = manifest?["server"]?["mcp_config"];
                    return new ClaudeDesktopStatus(true, true, ReadString(config?["command"]), ReadStrings(config?["args"]),
                        File.GetLastWriteTimeUtc(manifestPath));
                }
                catch (Exception)
                {
                    // 読めないものは飛ばす
                }
            }
        }
        return new ClaudeDesktopStatus(found, false, null, []);
    }

    /// <summary>画面に出す、Claude Desktop への追加の状態。</summary>
    public static string DescribeClaudeDesktop(ClaudeDesktopStatus status)
    {
        if (status.PointsToThisApp) return "追加済みです";
        if (status.Installed) return "別の場所のにこぷれっぷが追加されています（押すと、このにこぷれっぷで入れ直します）";
        return status.Found ? "まだ追加されていません" : "Claude Desktop が見つかりません";
    }

    /// <summary>ダウンロード フォルダ（場所を移していればその場所）。</summary>
    public static string DownloadsFolder
    {
        get
        {
            try
            {
                var id = FolderIdDownloads;
                if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out IntPtr pointer) == 0)
                {
                    try
                    {
                        string? path = Marshal.PtrToStringUni(pointer);
                        if (!string.IsNullOrEmpty(path)) return path;
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(pointer);
                    }
                }
            }
            catch (Exception)
            {
                // 既定の場所にする
            }
            return Path.Combine(UserProfile, "Downloads");
        }
    }

    private static readonly Guid FolderIdDownloads = new("374DE290-123F-4565-9164-39C4925E467B");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath(ref Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

    /// <summary>
    /// 拡張機能のファイル（nicokaraprep.mcpb）を folder に作って、そのパスを返す。中身は manifest.json（この exe を --mcp で起動する）とアイコン。
    /// 同じ名前のファイルは置き換える（開かれていて置き換えられなければ、日時を付けた名前にする）。
    /// </summary>
    public static string CreateMcpb(string folder)
    {
        Directory.CreateDirectory(folder);
        byte[]? icon = ReadIcon();
        string temp = Path.Combine(folder, $".{McpbFileName}.{Environment.ProcessId}.tmp");
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            var manifest = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
            using (var writer = new StreamWriter(manifest.Open(), new UTF8Encoding(false)))
            {
                writer.Write(BuildManifestJson(icon is not null));
            }
            if (icon is not null)
            {
                var entry = zip.CreateEntry("icon.png", CompressionLevel.NoCompression);
                using var iconStream = entry.Open();
                iconStream.Write(icon);
            }
        }
        string path = Path.Combine(folder, McpbFileName);
        try
        {
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            path = Path.Combine(folder, $"nicokaraprep-{DateTime.Now:yyyyMMdd-HHmmss}.mcpb");
            File.Move(temp, path, overwrite: true);
        }
        return path;
    }

    /// <summary>拡張機能の manifest.json（MCPB の manifest_version 0.3）。</summary>
    public static string BuildManifestJson(bool withIcon)
    {
        var tools = new JsonArray();
        foreach (var tool in McpToolCatalog.Create(null))
        {
            var item = new JsonObject { ["name"] = tool.ProtocolTool.Name };
            if (tool.ProtocolTool.Description is { Length: > 0 } description) item["description"] = description;
            tools.Add(item);
        }
        var manifest = new JsonObject
        {
            ["manifest_version"] = "0.3",
            ["name"] = ServerKey,
            ["display_name"] = "にこぷれっぷ",
            ["version"] = McpInfo.Version,
            ["description"] = "起動中のにこぷれっぷ（NicoKaraPrep）を Claude から操作します。",
            ["long_description"] =
                "にこぷれっぷ（RhythmicaLyrics のタイムタグ付き歌詞をニコカラメーカー3 向けに加工する Windows アプリ）を、Claude から操作できるようにします。" +
                "起動中のにこぷれっぷにつながり、行の確認・チェック・表示時刻やフォントの設定・n3proj の書き出しなどを行います（書き込みはにこぷれっぷの Ctrl+Z で戻せます）。" +
                "この拡張機能はにこぷれっぷ（ファイル > Claude と連携）が作ったもので、このパソコンのにこぷれっぷを起動するだけです。",
            ["author"] = new JsonObject { ["name"] = "YFRTeam" },
            ["server"] = new JsonObject
            {
                ["type"] = "binary",
                ["entry_point"] = McpInfo.IsPackaged ? McpInfo.AliasName : "NicoKaraPrep.exe",
                ["mcp_config"] = new JsonObject
                {
                    ["command"] = McpInfo.RegistrationExePath,
                    ["args"] = new JsonArray("--mcp"),
                },
            },
            ["tools"] = tools,
            ["keywords"] = new JsonArray("nicokara", "karaoke", "lyrics", "timetag"),
            ["license"] = "MIT",
            ["compatibility"] = new JsonObject { ["platforms"] = new JsonArray("win32") },
        };
        if (withIcon) manifest["icon"] = "icon.png";
        return manifest.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }

    private static byte[]? ReadIcon()
    {
        try
        {
            using var stream = typeof(ClaudeLink).Assembly.GetManifestResourceStream(IconResourceName);
            if (stream is null) return null;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Claude Desktop に拡張機能のファイルを渡す（Claude Desktop が起動して、インストールの確認を出す）。
    /// launcher は <see cref="FindClaudeDesktop"/> の結果。
    /// </summary>
    public static void OpenInClaudeDesktop(string launcher, string mcpbPath)
    {
        var psi = new ProcessStartInfo(launcher)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(mcpbPath) ?? UserProfile,
        };
        psi.ArgumentList.Add(mcpbPath);
        // 起動中の Claude Desktop が確認の画面を前に出せるように（前に出す権利はボタンを押したこのプロセスにある）
        AllowSetForegroundWindow(AsfwAny);
        using var process = Process.Start(psi);
    }

    private const uint AsfwAny = unchecked((uint)-1);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint dwProcessId);

    /// <summary>エクスプローラーでファイルの場所を開く（そのファイルを選んだ状態）。</summary>
    public static void ShowInExplorer(string path)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
            Arguments = $"/select,\"{path}\"",
            UseShellExecute = false,
        });
    }

    /// <summary>ブラウザーで開く。</summary>
    public static void OpenUrl(string url)
    {
        using var process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    // ============================================================ 共通

    private static string? ReadString(JsonNode? node)
    {
        try
        {
            return node is JsonValue value && value.TryGetValue(out string? text) ? text : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> ReadStrings(JsonNode? node) =>
        node is JsonArray array ? array.Select(ReadString).OfType<string>().ToList() : [];

    // ============================================================ 検証用のコマンド（--claude-link）

    /// <summary>
    /// 検証用のコマンドか（<c>NicoKaraPrep.exe --claude-link 〈status | code | mcpb フォルダ | desktop〉</c>）。
    /// 画面を出さずに「Claude と連携」の画面と同じ処理をして、結果を標準出力に JSON で書く（出力をリダイレクトして使う）。
    /// </summary>
    public static bool IsCliInvocation(string[] args) =>
        args.Length > 0 && args[0].Equals("--claude-link", StringComparison.OrdinalIgnoreCase);

    public static int RunCli(string[] args)
    {
        using var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        var json = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        string command = args.Length > 1 ? args[1].ToLowerInvariant() : "status";
        try
        {
            switch (command)
            {
                case "status":
                    stdout.WriteLine(StatusJson().ToJsonString(json));
                    return 0;
                case "code":
                {
                    var result = RegisterClaudeCodeAsync().GetAwaiter().GetResult();
                    stdout.WriteLine(ResultJson(result, null).ToJsonString(json));
                    return result.Success ? 0 : 1;
                }
                case "mcpb":
                {
                    string path = CreateMcpb(args.Length > 2 ? args[2] : DownloadsFolder);
                    stdout.WriteLine(new JsonObject { ["success"] = true, ["path"] = path }.ToJsonString(json));
                    return 0;
                }
                case "desktop":
                {
                    string? launcher = FindClaudeDesktop();
                    if (launcher is null)
                    {
                        stdout.WriteLine(new JsonObject { ["success"] = false, ["title"] = "Claude Desktop が見つかりません" }.ToJsonString(json));
                        return 1;
                    }
                    string path = CreateMcpb(args.Length > 2 ? args[2] : DownloadsFolder);
                    OpenInClaudeDesktop(launcher, path);
                    stdout.WriteLine(new JsonObject { ["success"] = true, ["path"] = path, ["launcher"] = launcher }.ToJsonString(json));
                    return 0;
                }
                default:
                    stdout.WriteLine("使い方: NicoKaraPrep.exe --claude-link status | code | mcpb [フォルダ] | desktop [フォルダ]");
                    return 2;
            }
        }
        catch (Exception ex)
        {
            stdout.WriteLine(new JsonObject { ["success"] = false, ["error"] = ex.Message }.ToJsonString(json));
            return 1;
        }
    }

    private static JsonObject StatusJson()
    {
        var code = ReadClaudeCodeStatus();
        var candidates = FindClaudeCode();
        var desktop = ReadClaudeDesktopStatus();
        return new JsonObject
        {
            ["registrationExe"] = McpInfo.RegistrationExePath,
            ["store"] = McpInfo.IsPackaged,
            ["claudeCode"] = new JsonObject
            {
                ["configPath"] = code.ConfigPath,
                ["configExists"] = code.ConfigExists,
                ["registered"] = code.Registered,
                ["command"] = code.Command,
                ["args"] = new JsonArray(code.Args.Select(a => (JsonNode?)JsonValue.Create(a)).ToArray()),
                ["pointsToThisApp"] = code.PointsToThisApp,
                ["readError"] = code.ReadError,
                ["candidates"] = new JsonArray(candidates.Select(c => (JsonNode?)new JsonObject { ["path"] = c.Path, ["source"] = c.Source }).ToArray()),
                ["text"] = DescribeClaudeCode(code, candidates.Count > 0),
            },
            ["claudeDesktop"] = new JsonObject
            {
                ["launcher"] = FindClaudeDesktop(),
                ["installed"] = desktop.Installed,
                ["command"] = desktop.Command,
                ["pointsToThisApp"] = desktop.PointsToThisApp,
                ["text"] = DescribeClaudeDesktop(desktop),
            },
        };
    }

    private static JsonObject ResultJson(ClaudeLinkResult result, string? path) => new()
    {
        ["success"] = result.Success,
        ["title"] = result.Title,
        ["message"] = result.Message,
        ["detail"] = result.Detail,
        ["path"] = path,
    };
}

/// <summary>Claude Code への登録の状態（設定ファイルを読んだ結果）。</summary>
internal sealed record ClaudeCodeStatus(string ConfigPath, bool ConfigExists, bool Registered, string? Command, IReadOnlyList<string> Args, string? ReadError)
{
    /// <summary>このにこぷれっぷ（の橋渡し）が登録されているか。</summary>
    public bool PointsToThisApp => Registered && ClaudeLink.IsThisApp(Command, Args);
}

/// <summary>見つかった claude（Claude Code）。</summary>
internal sealed record ClaudeCodeExe(string Path, string Source);

/// <summary>Claude Desktop への追加の状態（ManifestTime は入っている拡張機能の manifest.json の更新日時。入れ直されたかの見分けに使う）。</summary>
internal sealed record ClaudeDesktopStatus(bool Found, bool Installed, string? Command, IReadOnlyList<string> Args, DateTime? ManifestTime = null)
{
    /// <summary>このにこぷれっぷ（の橋渡し）を起動する拡張機能が入っているか。</summary>
    public bool PointsToThisApp => Installed && ClaudeLink.IsThisApp(Command, Args);
}

/// <summary>登録のボタンの結果（画面の InfoBar に出す）。</summary>
internal sealed record ClaudeLinkResult(bool Success, string Title, string Message, string? Detail = null);
