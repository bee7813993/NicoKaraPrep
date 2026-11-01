using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using NicoKaraPrep.Core.Formats;

namespace NicoKaraPrep.App.Services.Mcp;

/// <summary>
/// Codex（OpenAI）への登録をボタン 1 つで行う処理（ファイル > AI エージェントと連携 の画面と、検証用の <c>--agent-link codex</c>）。
/// Codex は、ChatGPT の Codex アプリ・Codex CLI・IDE の拡張機能が 1 つの設定ファイル（~/.codex/config.toml）を共有するので、1 回の登録でどれからも使える。
/// codex を探して（PATH・npm・Codex アプリが置いたもの・Codex アプリのパッケージの中）、
/// 「codex mcp add nicokaraprep -- "exe" --mcp」を実行する（別の exe が登録されていれば消してから）。
/// 設定ファイルを読み直して、この exe になったことを確かめる（読めない書き方なら「codex mcp get --json」で確かめる）。
/// </summary>
internal static class CodexLink
{
    public const string ServerKey = McpInfo.ServerKey;

    /// <summary>Codex のダウンロードのページ。</summary>
    public const string CodexDownloadUrl = "https://openai.com/codex/";

    /// <summary>Codex アプリ（Microsoft Store）のパッケージのファミリー名。</summary>
    private const string CodexAppFamily = "OpenAI.Codex_2p2nqsd0c76g0";

    /// <summary>
    /// Codex の設定のフォルダ。Codex と同じ決まりで、CODEX_HOME があればそこ、無ければ %USERPROFILE%\.codex。
    /// </summary>
    public static string CodexHome
    {
        get
        {
            string? custom = Environment.GetEnvironmentVariable("CODEX_HOME");
            return string.IsNullOrWhiteSpace(custom) ? Path.Combine(AgentCli.UserProfile, ".codex") : custom;
        }
    }

    /// <summary>Codex の設定ファイル（MCP サーバーの登録先）。</summary>
    public static string ConfigPath => Path.Combine(CodexHome, "config.toml");

    /// <summary>Codex にこのアプリが登録されているか（設定ファイルを読むだけ。codex は動かさない）。</summary>
    public static CodexStatus ReadStatus()
    {
        string path = ConfigPath;
        try
        {
            if (!File.Exists(path)) return new CodexStatus(path, false, false, null, [], null);
            string text;
            // Codex が書いている途中でも読めるように、共有で開く
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
            {
                text = reader.ReadToEnd();
            }
            var root = TomlLite.Parse(text);
            if (TomlLite.GetTable(root, "mcp_servers", ServerKey) is not JsonObject entry) return new CodexStatus(path, true, false, null, [], null);
            return new CodexStatus(path, true, true, AgentCli.ReadString(entry["command"]), AgentCli.ReadStrings(entry["args"]), null);
        }
        catch (Exception ex)
        {
            return new CodexStatus(path, File.Exists(path), false, null, [], ex.Message);
        }
    }

    /// <summary>画面に出す、Codex への登録の状態。</summary>
    public static string Describe(CodexStatus status, bool codexFound)
    {
        if (status.PointsToThisApp) return "登録済みです";
        if (status.ReadError is not null) return "登録の状態を読めませんでした（押すと登録し直します）";
        if (status.Registered) return "別の場所のにこぷれっぷが登録されています（押すと、このにこぷれっぷに登録し直します）";
        return codexFound ? "まだ登録されていません" : "Codex が見つかりません";
    }

    /// <summary>
    /// このパソコンの codex を、使う順に探す。
    /// PATH（Codex CLI）、npm で入れたもの、Codex アプリが置いたもの（%LOCALAPPDATA%\OpenAI\Codex\bin の新しい順）、Codex アプリのパッケージの中のもの。
    /// どれも設定ファイル（~/.codex/config.toml）は同じなので、どれで登録しても同じ。
    /// </summary>
    public static IReadOnlyList<AgentCliExe> FindCodex()
    {
        var finder = new AgentCli.Finder();
        var pathDirectories = AgentCli.PathDirectories().ToList();
        foreach (string dir in pathDirectories) finder.Add(Path.Combine(dir, "codex.exe"), "PATH");
        foreach (string dir in pathDirectories) finder.Add(Path.Combine(dir, "codex.cmd"), "PATH（npm）");
        finder.Add(Path.Combine(AgentCli.RoamingAppData, "npm", "codex.cmd"), "npm");
        foreach (string exe in CodexAppCopies()) finder.Add(exe, "Codex アプリ");
        foreach (string dir in PackagePaths(CodexAppFamily)) finder.Add(Path.Combine(dir, "app", "resources", "codex.exe"), "Codex アプリ");
        return finder.Found;
    }

    /// <summary>Codex アプリが %LOCALAPPDATA%\OpenAI\Codex\bin\〈ハッシュ〉 に置いた codex.exe（新しい順）。</summary>
    private static IEnumerable<string> CodexAppCopies()
    {
        try
        {
            return new DirectoryInfo(Path.Combine(AgentCli.LocalAppData, "OpenAI", "Codex", "bin"))
                .GetDirectories()
                .Select(d => Path.Combine(d.FullName, "codex.exe"))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>パッケージのファミリー名から、入っているパッケージのフォルダ（今のユーザーの分）。</summary>
    private static IEnumerable<string> PackagePaths(string family)
    {
        var paths = new List<string>();
        try
        {
            uint count = 0, length = 0;
            if (GetPackagesByPackageFamily(family, ref count, null, ref length, null) != ErrorInsufficientBuffer || count == 0) return paths;
            var pointers = new IntPtr[count];
            var buffer = new char[length];
            if (GetPackagesByPackageFamily(family, ref count, pointers, ref length, buffer) != 0) return paths;
            // buffer にはフルネームが NUL 区切りで並ぶ
            foreach (string fullName in new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                uint pathLength = 0;
                if (GetPackagePathByFullName(fullName, ref pathLength, null) != ErrorInsufficientBuffer || pathLength == 0) continue;
                var path = new StringBuilder((int)pathLength);
                if (GetPackagePathByFullName(fullName, ref pathLength, path) == 0) paths.Add(path.ToString());
            }
        }
        catch (Exception)
        {
            // 古い Windows など
        }
        return paths;
    }

    private const int ErrorInsufficientBuffer = 122;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackagesByPackageFamily(string packageFamilyName, ref uint count, IntPtr[]? packageFullNames, ref uint bufferLength, char[]? buffer);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackagePathByFullName(string packageFullName, ref uint pathLength, StringBuilder? path);

    /// <summary>
    /// Codex に登録する（ボタンの処理）。別の exe が登録されていれば消してから足し、設定ファイルを読み直して確かめる。
    /// 見つかった codex で順に試し、どれかで登録できれば成功。
    /// </summary>
    public static async Task<AgentLinkResult> RegisterAsync(CancellationToken ct = default)
    {
        const string usable = "Codex（ChatGPT の Codex アプリ・Codex CLI・IDE の拡張機能）で新しく始めた会話から、にこぷれっぷを操作できます" +
                              "（登録の前から開いていた会話には出てきません。出てこないときは Codex を起動し直します）。";
        var before = ReadStatus();
        if (before.PointsToThisApp) return new AgentLinkResult(true, "Codex には登録済みです", usable);

        var candidates = FindCodex();
        if (candidates.Count == 0)
        {
            return new AgentLinkResult(false, "Codex が見つかりません",
                "Codex（ChatGPT の Codex アプリか Codex CLI）を入れてから、もう一度押してください。");
        }

        string exe = McpInfo.RegistrationExePath;
        var log = new StringBuilder();
        foreach (var codex in candidates)
        {
            if (AgentCli.IsCmd(codex) && AgentCli.CannotPassViaCmd(exe))
            {
                log.AppendLine($"{codex.Path}: この場所の exe はコマンドプロンプト経由では渡せないため飛ばしました");
                continue;
            }
            try
            {
                if (ReadStatus() is { Registered: true } or { ReadError: not null })
                {
                    var removed = await AgentCli.RunAsync(codex, ["mcp", "remove", ServerKey], ct);
                    AgentCli.AppendRun(log, codex, "mcp remove", removed);
                }
                var added = await AgentCli.RunAsync(codex, ["mcp", "add", ServerKey, "--", exe, "--mcp"], ct);
                AgentCli.AppendRun(log, codex, "mcp add", added);
                if (await PointsToThisAppAsync(codex, log, ct))
                {
                    return new AgentLinkResult(true, "Codex に登録しました", usable, log.ToString().Trim());
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                log.AppendLine($"{codex.Path}: {ex.Message}");
            }
        }
        return new AgentLinkResult(false, "Codex に登録できませんでした",
            "「うまくいかないとき」のコマンドを PowerShell に貼り付けて実行するか、Codex の設定ファイルに書いても登録できます。", log.ToString().Trim());
    }

    /// <summary>登録したあと、このにこぷれっぷになったか。設定ファイルを読めないときは codex に尋ねる（codex mcp get --json）。</summary>
    private static async Task<bool> PointsToThisAppAsync(AgentCliExe codex, StringBuilder log, CancellationToken ct)
    {
        var status = ReadStatus();
        if (status.ReadError is null) return status.PointsToThisApp;
        log.AppendLine($"{status.ConfigPath} を読めませんでした: {status.ReadError}");
        var got = await AgentCli.RunAsync(codex, ["mcp", "get", ServerKey, "--json"], ct);
        if (got.ExitCode != 0) return false;
        try
        {
            int start = got.Output.IndexOf('{');
            if (start < 0) return false;
            var transport = JsonNode.Parse(got.Output[start..])?["transport"];
            return AgentCli.IsThisApp(AgentCli.ReadString(transport?["command"]), AgentCli.ReadStrings(transport?["args"]));
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>Codex への登録の状態（設定ファイルを読んだ結果）。</summary>
internal sealed record CodexStatus(string ConfigPath, bool ConfigExists, bool Registered, string? Command, IReadOnlyList<string> Args, string? ReadError)
{
    /// <summary>このにこぷれっぷ（の橋渡し）が登録されているか。</summary>
    public bool PointsToThisApp => Registered && AgentCli.IsThisApp(Command, Args);
}
