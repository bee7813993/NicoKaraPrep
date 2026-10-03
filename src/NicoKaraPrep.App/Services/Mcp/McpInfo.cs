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
    /// この exe の場所ごとの識別子（exe のフルパスから作る）。同じフォルダの exe どうしだけが 1 つのインスタンスにまとまり、
    /// 同じフォルダの exe の <c>--mcp</c>（橋渡し）だけがその本体につながる（別のフォルダのビルドは別物として動く）。
    /// 環境変数 NICOKARAPREP_INSTANCE があればそれを使う（検証用）。
    /// </summary>
    public static string InstanceId => s_instanceId ??= ReadInstanceId();

    private static string? s_instanceId;

    private static string ReadInstanceId()
    {
        string? custom = Environment.GetEnvironmentVariable("NICOKARAPREP_INSTANCE");
        if (custom is { Length: > 0 }) return Sanitize(custom);
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
    public static string NotRunningMessage =>
        "にこぷれっぷ（NicoKaraPrep）が起動していません。本体を起動してから、もう一度お願いします" +
        $"（この橋渡しは {Path.GetDirectoryName(Environment.ProcessPath)} の NicoKaraPrep.exe につながります）。";
}
