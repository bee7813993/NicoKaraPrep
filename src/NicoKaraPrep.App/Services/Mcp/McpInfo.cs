using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace NicoKaraPrep.App.Services.Mcp;

/// <summary>
/// MCP（Model Context Protocol）で本体を操作するための共通の決まり。
/// 本体（<see cref="McpHost"/>）と橋渡し（<see cref="McpBridge"/>）は同じ exe なので、パイプの名前・サーバーの名前をここで共有する。
/// </summary>
internal static class McpInfo
{
    /// <summary>MCP のサーバーの名前（クライアントに見せる）。</summary>
    public const string ServerName = "NicoKaraPrep";

    /// <summary>クライアントに渡す使い方の説明（道具の一覧と一緒に読まれる）。</summary>
    public const string Instructions =
        "NicoKaraPrep（にこぷれっぷ）は、RhythmicaLyrics のタイムタグ付き歌詞をニコカラメーカー3 向けに加工する Windows アプリです。" +
        "この MCP サーバーは起動中の本体をそのまま操作します（結果は画面にすぐ反映され、書き込みは Ctrl+Z で戻せます）。" +
        "行は「タブ名＋行リストの行番号（1 始まり。空行も数える）」で指定します。タブを指定すると画面もそのタブに切り替わります。" +
        "時刻は行リストと同じ mm:ss:cc（cc は 10ms 単位）の文字列でやり取りします。" +
        "文字の位置は、行の表示文字列（タグを除いた歌詞）の先頭からの位置（0 始まり、UTF-16 の文字数）です。" +
        "本体が起動していないときは、道具を呼ぶと「起動してください」と返ります。";

    /// <summary>本体（アプリ）のバージョン。</summary>
    public static string Version => s_version ??= ReadVersion();

    private static string? s_version;

    private static string ReadVersion()
    {
        var assembly = typeof(McpInfo).Assembly;
        string? info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (info is { Length: > 0 })
        {
            int plus = info.IndexOf('+');
            return plus > 0 ? info[..plus] : info;
        }
        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    /// <summary>
    /// このアプリの識別子。ストア版（MSIX）はパッケージのファミリー名（更新して場所が変わっても同じ）、
    /// それ以外（zip 版・開発中のビルド）は exe のフルパスから作る（同じフォルダの exe どうしだけがまとまり、別のフォルダのビルドは別物として動く）。
    /// 同じ識別子のものだけが 1 つのインスタンスにまとまり、その <c>--mcp</c>（橋渡し）だけがその本体につながる。
    /// 環境変数 NICOKARAPREP_INSTANCE があればそれを使う（検証用）。
    /// </summary>
    public static string InstanceId => s_instanceId ??= ReadInstanceId();

    private static string? s_instanceId;

    private static string ReadInstanceId()
    {
        string? custom = Environment.GetEnvironmentVariable("NICOKARAPREP_INSTANCE");
        if (custom is { Length: > 0 }) return Sanitize(custom);
        if (PackageFamilyName is string family) return "pkg-" + Sanitize(family);
        string path = Environment.ProcessPath ?? typeof(McpInfo).Assembly.Location;
        try
        {
            path = Path.GetFullPath(path);
        }
        catch (Exception)
        {
            // そのまま
        }
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(path.ToLowerInvariant()));
        return Convert.ToHexString(hash, 0, 6).ToLowerInvariant();
    }

    private static string Sanitize(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            sb.Append(char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_');
        }
        return sb.Length == 0 ? "custom" : sb.ToString();
    }

    /// <summary>単一インスタンスの鍵（Windows App SDK の AppInstance の key）。</summary>
    public static string InstanceKey => $"NicoKaraPrep.{InstanceId}";

    /// <summary>本体が待ち受ける名前付きパイプの名前（\\.\pipe\ の下）。</summary>
    public static string PipeName => $"NicoKaraPrep.mcp.{InstanceId}";

    /// <summary>橋渡しが本体につなげないときの案内。</summary>
    public static string NotRunningMessage => IsPackaged
        ? "にこぷれっぷ（NicoKaraPrep）が起動していません。スタートメニューからにこぷれっぷ（ストア版）を起動してから、もう一度お願いします。"
        : "にこぷれっぷ（NicoKaraPrep）が起動していません。本体を起動してから、もう一度お願いします" +
          $"（この橋渡しは {Path.GetDirectoryName(Environment.ProcessPath)} の NicoKaraPrep.exe につながります）。";

    // ------------------------------------------------------------ ストア版（MSIX）

    /// <summary>ストア版（MSIX のパッケージ）として動いているか。</summary>
    public static bool IsPackaged => PackageFamilyName is not null;

    /// <summary>パッケージのファミリー名（ストア版でなければ null）。</summary>
    public static string? PackageFamilyName => s_family.Value;

    private static readonly Lazy<string?> s_family = new(ReadPackageFamilyName);

    private static string? ReadPackageFamilyName()
    {
        try
        {
            uint length = 0;
            // パッケージが無ければ APPMODEL_ERROR_NO_PACKAGE（15700）、あれば ERROR_INSUFFICIENT_BUFFER（122）で長さが返る
            if (GetCurrentPackageFamilyName(ref length, null) != ErrorInsufficientBuffer || length == 0) return null;
            var buffer = new StringBuilder((int)length);
            return GetCurrentPackageFamilyName(ref length, buffer) == 0 ? buffer.ToString() : null;
        }
        catch (Exception)
        {
            return null; // 古い Windows など
        }
    }

    private const int ErrorInsufficientBuffer = 122;

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetCurrentPackageFamilyName(ref uint packageFamilyNameLength, StringBuilder? packageFamilyName);

    // ------------------------------------------------------------ クライアントへの登録のしかた

    /// <summary>ストア版の実行エイリアスの名前（Package.appxmanifest の desktop:ExecutionAlias と同じ）。</summary>
    public const string AliasName = "nicokaraprep.exe";

    /// <summary>
    /// MCP クライアントに登録する exe のパス。ストア版は実行エイリアス（%LOCALAPPDATA%\Microsoft\WindowsApps\nicokaraprep.exe。更新しても同じ）、
    /// それ以外は今動いている exe。
    /// </summary>
    public static string RegistrationExePath => IsPackaged
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", AliasName)
        : Environment.ProcessPath ?? "NicoKaraPrep.exe";

    /// <summary>Claude Code に登録するコマンド（PowerShell・コマンドプロンプトで 1 回だけ実行する）。</summary>
    public static string ClaudeCodeCommand =>
        $"claude mcp add --scope user nicokaraprep -- \"{RegistrationExePath}\" --mcp";

    /// <summary>Claude Desktop の構成ファイル（claude_desktop_config.json）の mcpServers に書く内容。</summary>
    public static string ClaudeDesktopEntry
    {
        get
        {
            var entry = new System.Text.Json.Nodes.JsonObject
            {
                ["nicokaraprep"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["command"] = RegistrationExePath,
                    ["args"] = new System.Text.Json.Nodes.JsonArray("--mcp"),
                },
            };
            string json = entry.ToJsonString(new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
            // 外側の { } を外して、mcpServers の中に貼る形にする
            string inner = json.Trim();
            inner = inner[1..^1].Trim('\r', '\n');
            return string.Join("\n", inner.Split('\n').Select(l => l.StartsWith("  ", StringComparison.Ordinal) ? l[2..] : l));
        }
    }
}
