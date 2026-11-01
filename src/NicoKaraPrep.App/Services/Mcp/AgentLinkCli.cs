using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NicoKaraPrep.App.Services.Mcp;

/// <summary>
/// 検証用のコマンド（<c>NicoKaraPrep.exe --agent-link 〈status | code | codex | mcpb フォルダ | desktop フォルダ〉</c>。以前の <c>--claude-link</c> も同じ）。
/// 画面を出さずに「AI エージェントと連携」の画面と同じ処理をして、結果を標準出力に JSON で書く（出力をリダイレクトして使う）。
/// </summary>
internal static class AgentLinkCli
{
    public static bool IsCliInvocation(string[] args) =>
        args.Length > 0 && (args[0].Equals("--agent-link", StringComparison.OrdinalIgnoreCase)
                            || args[0].Equals("--claude-link", StringComparison.OrdinalIgnoreCase));

    public static int Run(string[] args)
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
                    var result = ClaudeLink.RegisterClaudeCodeAsync().GetAwaiter().GetResult();
                    stdout.WriteLine(ResultJson(result).ToJsonString(json));
                    return result.Success ? 0 : 1;
                }
                case "codex":
                {
                    var result = CodexLink.RegisterAsync().GetAwaiter().GetResult();
                    stdout.WriteLine(ResultJson(result).ToJsonString(json));
                    return result.Success ? 0 : 1;
                }
                case "mcpb":
                {
                    string path = ClaudeLink.CreateMcpb(args.Length > 2 ? args[2] : ClaudeLink.DownloadsFolder);
                    stdout.WriteLine(new JsonObject { ["success"] = true, ["path"] = path }.ToJsonString(json));
                    return 0;
                }
                case "desktop":
                {
                    string? launcher = ClaudeLink.FindClaudeDesktop();
                    if (launcher is null)
                    {
                        stdout.WriteLine(new JsonObject { ["success"] = false, ["title"] = "Claude Desktop が見つかりません" }.ToJsonString(json));
                        return 1;
                    }
                    string path = ClaudeLink.CreateMcpb(args.Length > 2 ? args[2] : ClaudeLink.DownloadsFolder);
                    ClaudeLink.OpenInClaudeDesktop(launcher, path);
                    stdout.WriteLine(new JsonObject { ["success"] = true, ["path"] = path, ["launcher"] = launcher }.ToJsonString(json));
                    return 0;
                }
                default:
                    stdout.WriteLine("使い方: NicoKaraPrep.exe --agent-link status | code | codex | mcpb [フォルダ] | desktop [フォルダ]");
                    return 2;
            }
        }
        catch (Exception ex)
        {
            stdout.WriteLine(new JsonObject { ["success"] = false, ["error"] = ex.Message }.ToJsonString(json));
            return 1;
        }
    }

    private static JsonArray Strings(IEnumerable<string> values) => new(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());

    private static JsonArray Candidates(IEnumerable<AgentCliExe> candidates) =>
        new(candidates.Select(c => (JsonNode?)new JsonObject { ["path"] = c.Path, ["source"] = c.Source }).ToArray());

    private static JsonObject StatusJson()
    {
        var code = ClaudeLink.ReadClaudeCodeStatus();
        var claudeCandidates = ClaudeLink.FindClaudeCode();
        var desktop = ClaudeLink.ReadClaudeDesktopStatus();
        var codex = CodexLink.ReadStatus();
        var codexCandidates = CodexLink.FindCodex();
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
                ["args"] = Strings(code.Args),
                ["pointsToThisApp"] = code.PointsToThisApp,
                ["readError"] = code.ReadError,
                ["candidates"] = Candidates(claudeCandidates),
                ["text"] = ClaudeLink.DescribeClaudeCode(code, claudeCandidates.Count > 0),
            },
            ["claudeDesktop"] = new JsonObject
            {
                ["launcher"] = ClaudeLink.FindClaudeDesktop(),
                ["installed"] = desktop.Installed,
                ["command"] = desktop.Command,
                ["pointsToThisApp"] = desktop.PointsToThisApp,
                ["text"] = ClaudeLink.DescribeClaudeDesktop(desktop),
            },
            ["codex"] = new JsonObject
            {
                ["configPath"] = codex.ConfigPath,
                ["configExists"] = codex.ConfigExists,
                ["registered"] = codex.Registered,
                ["command"] = codex.Command,
                ["args"] = Strings(codex.Args),
                ["pointsToThisApp"] = codex.PointsToThisApp,
                ["readError"] = codex.ReadError,
                ["candidates"] = Candidates(codexCandidates),
                ["text"] = CodexLink.Describe(codex, codexCandidates.Count > 0),
            },
        };
    }

    private static JsonObject ResultJson(AgentLinkResult result) => new()
    {
        ["success"] = result.Success,
        ["title"] = result.Title,
        ["message"] = result.Message,
        ["detail"] = result.Detail,
    };
}
