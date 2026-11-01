using System.Text.Json.Nodes;
using NicoKaraPrep.Core.Formats;

namespace NicoKaraPrep.Core.Tests;

public class TomlLiteTests
{
    private static string? Str(JsonNode? node) => node?.GetValue<string>();

    private static string[] Strs(JsonNode? node) => ((JsonArray)node!).Select(n => n!.GetValue<string>()).ToArray();

    [Fact]
    public void Codexが書くMCPサーバーの登録を読める()
    {
        // codex mcp add が書く形（\ を含むパスはリテラル文字列、ほかは基本文字列）と、ほかの設定が混ざった設定ファイル
        const string text = """
            model = "gpt-5"
            approval_policy = "on-request" # 注釈

            [features]
            web_search = true

            [mcp_servers.playwright]
            enabled = true
            command = "C:\\Program Files\\nodejs\\npx.cmd"
            args = [ "@playwright/mcp@latest" ]

            [mcp_servers.node_repl]
            args = []
            command = 'C:\Users\someone\AppData\Local\OpenAI\Codex\runtimes\node_repl.exe'
            startup_timeout_sec = 120

            [mcp_servers.node_repl.env]
            NODE_OPTIONS = "--max-old-space-size=4096"

            [mcp_servers.nicokaraprep]
            command = 'C:\Users\someone\AppData\Local\Microsoft\WindowsApps\nicokaraprep.exe'
            args = ["--mcp"]

            [projects.'c:\work\歌詞']
            trust_level = "trusted"
            """;
        var root = TomlLite.Parse(text);

        var entry = TomlLite.GetTable(root, "mcp_servers", "nicokaraprep");
        Assert.NotNull(entry);
        Assert.Equal(@"C:\Users\someone\AppData\Local\Microsoft\WindowsApps\nicokaraprep.exe", Str(entry!["command"]));
        Assert.Equal(["--mcp"], Strs(entry["args"]));

        Assert.Equal(@"C:\Program Files\nodejs\npx.cmd", Str(TomlLite.GetTable(root, "mcp_servers", "playwright")!["command"]));
        Assert.Equal("--max-old-space-size=4096", Str(TomlLite.GetTable(root, "mcp_servers", "node_repl", "env")!["NODE_OPTIONS"]));
        Assert.Equal("trusted", Str(TomlLite.GetTable(root, "projects", @"c:\work\歌詞")!["trust_level"]));
        Assert.Equal("on-request", Str(root["approval_policy"]));
        Assert.Equal("true", Str(TomlLite.GetTable(root, "features")!["web_search"])); // 文字列以外の値は書かれたまま
        Assert.Null(TomlLite.GetTable(root, "mcp_servers", "missing"));
    }

    [Fact]
    public void ドット付きのキーとインライン表と引用符付きのキー()
    {
        const string text = """
            mcp_servers.a.command = "a.exe"
            [mcp_servers]
            b = { command = "b.exe", args = ["--x", 'y'], env = { K = "V" } }
            "c d".command = "c.exe"
            """;
        var root = TomlLite.Parse(text);
        Assert.Equal("a.exe", Str(TomlLite.GetTable(root, "mcp_servers", "a")!["command"]));
        var b = TomlLite.GetTable(root, "mcp_servers", "b")!;
        Assert.Equal("b.exe", Str(b["command"]));
        Assert.Equal(["--x", "y"], Strs(b["args"]));
        Assert.Equal("V", Str(b["env"]!["K"]));
        Assert.Equal("c.exe", Str(TomlLite.GetTable(root, "mcp_servers", "c d")!["command"]));
    }

    [Fact]
    public void 複数行の配列と文字列と表の配列()
    {
        const string text = "args = [\n  \"--a\", # 注釈\n  \"--b\",\n]\n" +
                            "basic = \"\"\"\n1 行目\\n[見出しではない]\n\"\"\"\n" +
                            "lines = '''\n[[これも見出しではない]]\n'''\n" +
                            "cont = \"\"\"a \\\n    b\"\"\"\n" +
                            "quotes = \"\"\"x\"\"\"\"\"\n" +
                            "esc = \"tab\\there \\u3042 \\U0001F600 \\\"q\\\"\"\n" +
                            "[[profiles]]\nname = \"one\"\n[[profiles]]\nname = \"two\"\n[profiles.sub]\nk = 1\n" +
                            "[after]\nok = \"yes\"\n";
        var root = TomlLite.Parse(text);
        Assert.Equal(["--a", "--b"], Strs(root["args"]));
        Assert.Equal("1 行目\n[見出しではない]\n", Str(root["basic"]));
        Assert.Equal("[[これも見出しではない]]\n", Str(root["lines"]));
        Assert.Equal("a b", Str(root["cont"]));
        Assert.Equal("x\"\"", Str(root["quotes"]));
        Assert.Equal("tab\there あ 😀 \"q\"", Str(root["esc"]));
        var profiles = (JsonArray)root["profiles"]!;
        Assert.Equal(2, profiles.Count);
        Assert.Equal("two", Str(profiles[1]!["name"]));
        Assert.Equal("1", Str(profiles[1]!["sub"]!["k"]));
        Assert.Equal("yes", Str(TomlLite.GetTable(root, "after")!["ok"]));
    }

    [Fact]
    public void BOMとCRLFを読める()
    {
        var root = TomlLite.Parse("\uFEFF[mcp_servers.x]\r\ncommand = \"x.exe\"\r\nargs = [\r\n\"--mcp\"\r\n]\r\n");
        Assert.Equal("x.exe", Str(TomlLite.GetTable(root, "mcp_servers", "x")!["command"]));
        Assert.Equal(["--mcp"], Strs(TomlLite.GetTable(root, "mcp_servers", "x")!["args"]));
    }

    [Theory]
    [InlineData("a = \"閉じていない\n", "1 行目")]
    [InlineData("[t]\nk2 = [1, 2\n", "3 行目")]
    [InlineData("a = 1\nb = \n", "2 行目")]
    [InlineData("a = 'x' y\n", "1 行目")]
    public void 読めない書き方は行番号付きの例外(string text, string line)
    {
        var ex = Assert.Throws<FormatException>(() => TomlLite.Parse(text));
        Assert.StartsWith(line, ex.Message);
    }

    [Theory]
    [InlineData(@"C:\Users\someone\nicokaraprep.exe", @"'C:\Users\someone\nicokaraprep.exe'")]
    [InlineData(@"C:\it's\a.exe", "\"C:\\\\it's\\\\a.exe\"")]
    public void 文字列の書き方(string value, string expected)
    {
        string quoted = TomlLite.Quote(value);
        Assert.Equal(expected, quoted);
        Assert.Equal(value, Str(TomlLite.Parse($"k = {quoted}\n")["k"]));
    }
}
