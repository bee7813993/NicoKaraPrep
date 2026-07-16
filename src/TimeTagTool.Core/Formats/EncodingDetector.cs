using System.Text;

namespace TimeTagTool.Core.Formats;

/// <summary>
/// 歌詞ファイルのエンコーディング自動判別。
/// BOM（UTF-8 / UTF-16 LE / UTF-16 BE）→ UTF-8 厳格デコード → Shift-JIS の順で判定する。
/// </summary>
public static class EncodingDetector
{
    static EncodingDetector()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>Shift-JIS (CP932)。</summary>
    public static Encoding ShiftJis => Encoding.GetEncoding(932);

    /// <summary>BOM 付き UTF-8。</summary>
    public static Encoding Utf8Bom => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    /// <summary>BOM なし UTF-8。</summary>
    public static Encoding Utf8NoBom => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>バイト列からエンコーディングを判別してデコードする。</summary>
    public static string Decode(byte[] bytes, out Encoding detected)
    {
        // BOM 判定
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            detected = Utf8Bom;
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            detected = Encoding.Unicode;
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            detected = Encoding.BigEndianUnicode;
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        // UTF-8 厳格デコードを試す
        var strictUtf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);
        try
        {
            string s = strictUtf8.GetString(bytes);
            detected = Utf8NoBom;
            return s;
        }
        catch (DecoderFallbackException)
        {
            detected = ShiftJis;
            return ShiftJis.GetString(bytes);
        }
    }

    /// <summary>ファイルを読み込み、エンコーディングを判別してテキストを返す。</summary>
    public static string ReadAllText(string path, out Encoding detected) =>
        Decode(File.ReadAllBytes(path), out detected);
}
