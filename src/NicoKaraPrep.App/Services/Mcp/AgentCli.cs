using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace NicoKaraPrep.App.Services.Mcp;

/// <summary>
/// AI エージェントへの登録（<see cref="ClaudeLink"/>・<see cref="CodexLink"/>）で共通に使う処理。
/// エージェントのコマンド（claude・codex）を探す場所の PATH、画面を出さずに動かすこと、登録された内容がこのにこぷれっぷかの見分け。
/// </summary>
internal static class AgentCli
{
    /// <summary>エージェントのコマンドの 1 回の実行を待つ長さ。</summary>
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(60);

    public static string UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string RoamingAppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    public static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

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

    /// <summary>
    /// PATH のフォルダ（このプロセスのものと、あとから入れた分も見えるようにレジストリのユーザー・システムのもの）。
    /// </summary>
    public static IEnumerable<string> PathDirectories()
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

    /// <summary>見つかったものを、重ならないように順に集める（無いもの・読めない場所は飛ばす）。</summary>
    public sealed class Finder
    {
        private readonly List<AgentCliExe> _found = [];
        private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<AgentCliExe> Found => _found;

        public void Add(string path, string source)
        {
            try
            {
                path = Path.GetFullPath(path);
                if (File.Exists(path) && _seen.Add(path)) _found.Add(new AgentCliExe(path, source));
            }
            catch (Exception)
            {
                // 読めない場所は飛ばす
            }
        }
    }

    /// <summary>npm で入れたコマンド（.cmd）か。コマンドプロンプト経由で動かすので、% と " を含む引数は渡せない。</summary>
    public static bool IsCmd(AgentCliExe exe) => exe.Path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);

    /// <summary>コマンドプロンプト経由では渡せない引数か。</summary>
    public static bool CannotPassViaCmd(string arg) => arg.Contains('%') || arg.Contains('"');

    public static void AppendRun(StringBuilder log, AgentCliExe exe, string what, (int ExitCode, string Output) run)
    {
        log.AppendLine($"{exe.Path} {what}（終了コード {run.ExitCode}）");
        if (run.Output.Length > 0) log.AppendLine(run.Output);
    }

    private static readonly Regex AnsiEscape = new(@"\x1B\[[0-9;?]*[ -/]*[@-~]|\x1B\][^\x07]*\x07", RegexOptions.Compiled);

    /// <summary>コマンドを画面を出さずに動かし、終了コードと出力（標準出力と標準エラー）を返す。</summary>
    public static async Task<(int ExitCode, string Output)> RunAsync(AgentCliExe exe, IReadOnlyList<string> args, CancellationToken ct)
    {
        string name = Path.GetFileNameWithoutExtension(exe.Path);
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
        if (IsCmd(exe))
        {
            // npm の .cmd はコマンドプロンプト経由で動かす（/s で外側の引用符だけを外す）
            psi.FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            psi.Arguments = "/d /s /c \"" + string.Join(" ", new[] { exe.Path }.Concat(args).Select(a => "\"" + a + "\"")) + "\"";
        }
        else
        {
            psi.FileName = exe.Path;
            foreach (string a in args) psi.ArgumentList.Add(a);
        }
        psi.Environment["NO_COLOR"] = "1"; // 色の制御文字を出さない

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"{name} を起動できません");
        process.StandardInput.Close();
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(RunTimeout);
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
            throw new TimeoutException($"{name} が {RunTimeout.TotalSeconds:0} 秒たっても終わりませんでした");
        }
        string output = ((await stdout).Trim() + "\n" + (await stderr).Trim()).Trim();
        return (process.ExitCode, AnsiEscape.Replace(output, ""));
    }

    public static string? ReadString(JsonNode? node)
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

    public static IReadOnlyList<string> ReadStrings(JsonNode? node) =>
        node is JsonArray array ? array.Select(ReadString).OfType<string>().ToList() : [];
}

/// <summary>見つかったエージェントのコマンド（claude・codex）。</summary>
internal sealed record AgentCliExe(string Path, string Source);

/// <summary>登録のボタンの結果（画面の InfoBar に出す）。</summary>
internal sealed record AgentLinkResult(bool Success, string Title, string Message, string? Detail = null);
