namespace TimeTagTool.Core.Model;

/// <summary>
/// タイムタグ時刻の解析・書式化。
/// 時刻は曲頭からの経過時間を 10ms 単位 (centisecond) の int で扱う。
/// 拡張タイムタグ [mm:ss:cc]（cc は 10ms 単位）と秒単位タイムタグ [mm:ss] に対応。
/// </summary>
public static class TimeTag
{
    /// <summary>10ms 単位の時刻を [mm:ss:cc] 形式に書式化する。</summary>
    public static string Format(int cs)
    {
        if (cs < 0) cs = 0;
        int mm = cs / 6000;
        int ss = cs / 100 % 60;
        int cc = cs % 100;
        if (mm > 99) mm = 99; // 規格上 2 桁
        return $"[{mm:D2}:{ss:D2}:{cc:D2}]";
    }

    /// <summary>
    /// s の pos 位置から始まるタイムタグを解析する。
    /// [mm:ss:cc]（10文字）と [mm:ss]（7文字）に対応。
    /// </summary>
    /// <returns>タイムタグとして解析できた場合 true。length は消費した文字数。</returns>
    public static bool TryParseAt(string s, int pos, out int cs, out int length)
    {
        cs = 0;
        length = 0;
        if (pos < 0 || pos >= s.Length || s[pos] != '[') return false;

        // [mm:ss:cc]
        if (pos + 10 <= s.Length &&
            IsDigit(s, pos + 1) && IsDigit(s, pos + 2) && s[pos + 3] == ':' &&
            IsDigit(s, pos + 4) && IsDigit(s, pos + 5) && s[pos + 6] == ':' &&
            IsDigit(s, pos + 7) && IsDigit(s, pos + 8) && s[pos + 9] == ']')
        {
            int mm = Digits(s, pos + 1);
            int ss = Digits(s, pos + 4);
            int cc = Digits(s, pos + 7);
            if (ss >= 60) return false;
            cs = mm * 6000 + ss * 100 + cc;
            length = 10;
            return true;
        }

        // [mm:ss]
        if (pos + 7 <= s.Length &&
            IsDigit(s, pos + 1) && IsDigit(s, pos + 2) && s[pos + 3] == ':' &&
            IsDigit(s, pos + 4) && IsDigit(s, pos + 5) && s[pos + 6] == ']')
        {
            int mm = Digits(s, pos + 1);
            int ss = Digits(s, pos + 4);
            if (ss >= 60) return false;
            cs = (mm * 60 + ss) * 100;
            length = 7;
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
