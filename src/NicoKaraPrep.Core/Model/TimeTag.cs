namespace NicoKaraPrep.Core.Model;

/// <summary>
/// タイムタグ時刻の解析・書式化。
/// 時刻は曲頭からの経過時間を 10ms 単位 (centisecond) の int で扱う。
/// 拡張タイムタグ [mm:ss:cc]（cc は 10ms 単位）と秒単位タイムタグ [mm:ss] に対応。
/// 曲頭より前（アイコンの先行タグが 0 秒より前になったときなど）はマイナスの値のまま持ち、
/// 画面・作業状態では [-mm:ss:cc] と書く（<see cref="FormatSigned"/>）。歌詞ファイル・書き出しには 0 として書く（<see cref="Format"/>）。
/// </summary>
public static class TimeTag
{
    /// <summary>10ms 単位の時刻を [mm:ss:cc] 形式に書式化する（ファイルへ書く形。0 より前は 0 にする）。</summary>
    public static string Format(int cs) => FormatCore(Math.Max(0, cs));

    /// <summary>10ms 単位の時刻を、0 より前なら [-mm:ss:cc] の形で書式化する（画面・行の編集欄・作業状態用）。</summary>
    public static string FormatSigned(int cs) => cs < 0 ? "[-" + FormatCore(-cs)[1..] : FormatCore(cs);

    private static string FormatCore(int cs)
    {
        int mm = cs / 6000;
        int ss = cs / 100 % 60;
        int cc = cs % 100;
        if (mm > 99) mm = 99; // 規格上 2 桁
        return $"[{mm:D2}:{ss:D2}:{cc:D2}]";
    }

    /// <summary>
    /// s の pos 位置から始まるタイムタグを解析する。
    /// [mm:ss:cc]（10文字）と [mm:ss]（7文字）、0 秒より前の [-mm:ss:cc]（11文字）・[-mm:ss]（8文字）に対応。
    /// </summary>
    /// <returns>タイムタグとして解析できた場合 true。length は消費した文字数。</returns>
    public static bool TryParseAt(string s, int pos, out int cs, out int length)
    {
        cs = 0;
        length = 0;
        if (pos < 0 || pos >= s.Length || s[pos] != '[') return false;
        bool negative = pos + 1 < s.Length && s[pos + 1] == '-';
        int p = negative ? pos + 1 : pos; // p は '[' の位置とみなす（'-' の分だけずらす）

        // [mm:ss:cc]
        if (p + 10 <= s.Length &&
            IsDigit(s, p + 1) && IsDigit(s, p + 2) && s[p + 3] == ':' &&
            IsDigit(s, p + 4) && IsDigit(s, p + 5) && s[p + 6] == ':' &&
            IsDigit(s, p + 7) && IsDigit(s, p + 8) && s[p + 9] == ']')
        {
            int mm = Digits(s, p + 1);
            int ss = Digits(s, p + 4);
            int cc = Digits(s, p + 7);
            if (ss >= 60) return false;
            cs = mm * 6000 + ss * 100 + cc;
            if (negative) cs = -cs;
            length = 10 + (negative ? 1 : 0);
            return true;
        }

        // [mm:ss]
        if (p + 7 <= s.Length &&
            IsDigit(s, p + 1) && IsDigit(s, p + 2) && s[p + 3] == ':' &&
            IsDigit(s, p + 4) && IsDigit(s, p + 5) && s[p + 6] == ']')
        {
            int mm = Digits(s, p + 1);
            int ss = Digits(s, p + 4);
            if (ss >= 60) return false;
            cs = (mm * 60 + ss) * 100;
            if (negative) cs = -cs;
            length = 7 + (negative ? 1 : 0);
            return true;
        }

        return false;
    }

    /// <summary>"mm:ss:cc" / "mm:ss" 形式（角括弧なし可）の文字列を解析する。</summary>
    public static bool TryParse(string s, out int cs)
    {
        cs = 0;
        if (string.IsNullOrEmpty(s)) return false;
        string t = s.Trim();
        if (!t.StartsWith('[')) t = "[" + t;
        if (!t.EndsWith(']')) t += "]";
        return TryParseAt(t, 0, out cs, out int len) && len == t.Length;
    }

    private static bool IsDigit(string s, int i) => s[i] >= '0' && s[i] <= '9';
    private static int Digits(string s, int i) => (s[i] - '0') * 10 + (s[i + 1] - '0');
}
