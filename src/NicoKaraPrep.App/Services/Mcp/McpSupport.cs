using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModelContextProtocol.Protocol;
using NicoKaraPrep.Core.Formats;
using NicoKaraPrep.Core.Model;

namespace NicoKaraPrep.App.Services.Mcp;

/// <summary>道具の処理が利用者に返す失敗（メッセージをそのまま isError の結果にする）。</summary>
internal sealed class McpToolException : Exception
{
    public McpToolException(string message) : base(message)
    {
    }
}

/// <summary>道具の結果の作り方（JSON の文字と画像）。</summary>
internal static class McpResults
{
    /// <summary>結果の JSON（日本語をそのまま出す）。</summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    public static CallToolResult Ok(JsonNode? node) => new()
    {
        Content = new List<ContentBlock> { new TextContentBlock { Text = node?.ToJsonString(JsonOptions) ?? "{}" } },
    };

    public static CallToolResult OkWithImage(JsonNode? node, byte[] png) => new()
    {
        Content = new List<ContentBlock>
        {
            new TextContentBlock { Text = node?.ToJsonString(JsonOptions) ?? "{}" },
            ImageContentBlock.FromBytes(png, "image/png"),
        },
    };

    public static CallToolResult Error(string message) => new()
    {
        IsError = true,
        Content = new List<ContentBlock> { new TextContentBlock { Text = message } },
    };
}

/// <summary>時刻の文字（行リストと同じ mm:ss:cc）の読み書き。</summary>
internal static partial class McpTime
{
    /// <summary>10ms 単位の時刻 → "mm:ss:cc"。</summary>
    public static string Format(int cs) => TimeTag.Format(cs).Trim('[', ']');

    /// <summary>ms の時刻 → "mm:ss:cc"（10ms 単位に四捨五入。行リスト・行設定と同じ）。</summary>
    public static string FormatMs(int ms) => Format(N3ShowTimeAdjuster.ToCs(ms));

    /// <summary>
    /// 時刻の文字を 10ms 単位の時刻にする。"mm:ss:cc"・"mm:ss.cc"・"[mm:ss:cc]"・"mm:ss"・"m:ss.c" と、秒だけの数（"83.5"）を受け付ける。
    /// </summary>
    public static int Parse(string text, string what)
    {
        string t = (text ?? "").Trim().Trim('[', ']').Trim();
        if (t.Length == 0) throw new McpToolException($"{what}の時刻が空です（mm:ss:cc で指定してください）");
        var m = TimePattern().Match(t);
        if (m.Success)
        {
            int mm = int.Parse(m.Groups["m"].Value);
            int ss = int.Parse(m.Groups["s"].Value);
            string ccText = m.Groups["c"].Value;
            int cc = ccText.Length == 0 ? 0 : ccText.Length == 1 ? int.Parse(ccText) * 10 : int.Parse(ccText[..2]);
            if (ss >= 60) throw new McpToolException($"{what}の秒（{ss}）は 59 までです");
            return mm * 6000 + ss * 100 + cc;
        }
        if (double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double seconds) && seconds >= 0)
        {
            return (int)Math.Round(seconds * 100);
        }
        throw new McpToolException($"{what}の時刻「{text}」を読めません。mm:ss:cc（例 01:23:45）か秒（例 83.45）で指定してください");
    }

    [GeneratedRegex(@"^(?<m>\d{1,3}):(?<s>\d{1,2})(?:[:.](?<c>\d{1,3}))?$")]
    private static partial Regex TimePattern();
}
