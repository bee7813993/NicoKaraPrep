using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace NicoKaraPrep.Core.Formats;

/// <summary>
/// TOML を読むだけの小さな読み取り器（Codex の設定ファイル ~/.codex/config.toml の MCP サーバーの登録を確かめるため）。
/// 表（[a.b]）・表の配列（[[a]]）・ドット付きのキー・インライン表・配列・4 種類の文字列（"…" '…' """…""" '''…'''）を読み、
/// JSON の木（<see cref="JsonObject"/>）にする。文字列以外の値（数・真偽・日時）は、書かれた文字のまま文字列にする（値の検査はしない）。
/// 読めない書き方のときは <see cref="FormatException"/>（何行目か付き）。
/// </summary>
public static class TomlLite
{
    public static JsonObject Parse(string text) => new Parser(text).ParseDocument();

    /// <summary>path の表（なければ null）。例: GetTable(root, "mcp_servers", "nicokaraprep")。</summary>
    public static JsonObject? GetTable(JsonObject root, params string[] path)
    {
        JsonNode? node = root;
        foreach (string key in path)
        {
            node = (node as JsonObject)?[key];
            if (node is null) return null;
        }
        return node as JsonObject;
    }

    /// <summary>
    /// TOML の文字列として書く（設定ファイルに貼る見本用）。\ を含むパスが読みやすいよう、書ければ '…'（リテラル文字列）、
    /// ' や制御文字を含むときは "…"（エスケープ付き）。
    /// </summary>
    public static string Quote(string value)
    {
        if (!value.Contains('\'') && !value.Any(c => char.IsControl(c) && c != '\t')) return "'" + value + "'";
        var sb = new StringBuilder("\"");
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (char.IsControl(c)) sb.Append($"\\u{(int)c:X4}");
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    private sealed class Parser(string text)
    {
        private readonly string _s = text.Length > 0 && text[0] == '﻿' ? text[1..] : text;
        private int _i;

        private char Cur => _i < _s.Length ? _s[_i] : '\0';

        private bool AtEnd => _i >= _s.Length;

        public JsonObject ParseDocument()
        {
            var root = new JsonObject();
            JsonObject current = root;
            while (true)
            {
                SkipWhitespaceAndNewlines();
                if (AtEnd) return root;
                if (Cur == '[')
                {
                    bool arrayOfTables = _i + 1 < _s.Length && _s[_i + 1] == '[';
                    _i += arrayOfTables ? 2 : 1;
                    SkipSpaces();
                    var path = ParseKeyPath();
                    SkipSpaces();
                    Expect(']');
                    if (arrayOfTables) Expect(']');
                    current = arrayOfTables ? AppendArrayTable(root, path) : OpenTable(root, path);
                    EndOfLine();
                    continue;
                }
                ParseKeyValue(current);
                EndOfLine();
            }
        }

        private JsonObject OpenTable(JsonObject root, List<string> path)
        {
            JsonObject table = root;
            foreach (string key in path)
            {
                switch (table[key])
                {
                    case null:
                        var created = new JsonObject();
                        table[key] = created;
                        table = created;
                        break;
                    case JsonObject existing:
                        table = existing;
                        break;
                    case JsonArray { Count: > 0 } array when array[^1] is JsonObject last:
                        table = last; // [[a]] のあとの [a.b] は最後の要素の下
                        break;
                    default:
                        throw Error($"{key} は表ではありません");
                }
            }
            return table;
        }

        private JsonObject AppendArrayTable(JsonObject root, List<string> path)
        {
            var parent = OpenTable(root, path.GetRange(0, path.Count - 1));
            string last = path[^1];
            if (parent[last] is not JsonArray array)
            {
                if (parent[last] is not null) throw Error($"{last} は表の配列ではありません");
                array = new JsonArray();
                parent[last] = array;
            }
            var item = new JsonObject();
            array.Add(item);
            return item;
        }

        private void ParseKeyValue(JsonObject table)
        {
            var path = ParseKeyPath();
            SkipSpaces();
            Expect('=');
            SkipSpaces();
            JsonNode? value = ParseValue();
            var parent = OpenTable(table, path.GetRange(0, path.Count - 1));
            parent[path[^1]] = value;
        }

        private List<string> ParseKeyPath()
        {
            var keys = new List<string> { ParseKey() };
            while (true)
            {
                SkipSpaces();
                if (Cur != '.') return keys;
                _i++;
                SkipSpaces();
                keys.Add(ParseKey());
            }
        }

        private string ParseKey()
        {
            if (Cur == '"') return ParseBasicString();
            if (Cur == '\'') return ParseLiteralString();
            int start = _i;
            while (!AtEnd && (char.IsAsciiLetterOrDigit(Cur) || Cur is '-' or '_')) _i++;
            if (_i == start) throw Error("キーがありません");
            return _s[start.._i];
        }

        private JsonNode? ParseValue()
        {
            if (StartsWith("\"\"\"")) return JsonValue.Create(ParseMultilineBasicString());
            if (StartsWith("'''")) return JsonValue.Create(ParseMultilineLiteralString());
            switch (Cur)
            {
                case '"': return JsonValue.Create(ParseBasicString());
                case '\'': return JsonValue.Create(ParseLiteralString());
                case '[': return ParseArray();
                case '{': return ParseInlineTable();
            }
            // 数・真偽・日時: 区切り（, ] } # 改行）までをそのまま
            int start = _i;
            while (!AtEnd && Cur is not (',' or ']' or '}' or '#' or '\n' or '\r')) _i++;
            string raw = _s[start.._i].Trim();
            if (raw.Length == 0) throw Error("値がありません");
            return JsonValue.Create(raw);
        }

        private JsonArray ParseArray()
        {
            Expect('[');
            var array = new JsonArray();
            while (true)
            {
                SkipWhitespaceAndNewlines();
                if (Cur == ']')
                {
                    _i++;
                    return array;
                }
                array.Add(ParseValue());
                SkipWhitespaceAndNewlines();
                if (Cur == ',')
                {
                    _i++;
                    continue;
                }
                Expect(']');
                return array;
            }
        }

        private JsonObject ParseInlineTable()
        {
            Expect('{');
            var table = new JsonObject();
            SkipSpaces();
            if (Cur == '}')
            {
                _i++;
                return table;
            }
            while (true)
            {
                SkipWhitespaceAndNewlines(); // TOML 1.1 では改行も書ける
                if (Cur == '}')
                {
                    _i++;
                    return table;
                }
                ParseKeyValue(table);
                SkipWhitespaceAndNewlines();
                if (Cur == ',')
                {
                    _i++;
                    continue;
                }
                Expect('}');
                return table;
            }
        }

        private string ParseBasicString()
        {
            Expect('"');
            var sb = new StringBuilder();
            while (true)
            {
                if (AtEnd || Cur is '\n' or '\r') throw Error("文字列が閉じていません");
                char c = _s[_i++];
                if (c == '"') return sb.ToString();
                if (c == '\\') AppendEscape(sb);
                else sb.Append(c);
            }
        }

        private string ParseLiteralString()
        {
            Expect('\'');
            int start = _i;
            while (!AtEnd && Cur != '\'')
            {
                if (Cur is '\n' or '\r') throw Error("文字列が閉じていません");
                _i++;
            }
            string value = _s[start.._i];
            Expect('\'');
            return value;
        }

        private string ParseMultilineBasicString()
        {
            _i += 3;
            SkipOneNewline();
            var sb = new StringBuilder();
            while (true)
            {
                if (AtEnd) throw Error("文字列が閉じていません");
                if (StartsWith("\"\"\""))
                {
                    // 閉じの """ の直前に " を 2 つまで書ける（""""" なら中身の終わりに "" が付く）
                    int quotes = Math.Min(CountQuotesAhead(), 5);
                    sb.Append('"', quotes - 3);
                    _i += quotes;
                    return sb.ToString();
                }
                char c = _s[_i++];
                if (c == '\\')
                {
                    // 行末の \ は、改行と次の空白をまとめて飛ばす
                    int save = _i;
                    while (Cur is ' ' or '\t') _i++;
                    if (Cur is '\n' or '\r')
                    {
                        while (Cur is ' ' or '\t' or '\n' or '\r') _i++;
                        continue;
                    }
                    _i = save;
                    AppendEscape(sb);
                }
                else
                {
                    sb.Append(c);
                }
            }
        }

        private int CountQuotesAhead()
        {
            int n = 0;
            while (_i + n < _s.Length && _s[_i + n] == '"') n++;
            return n;
        }

        private string ParseMultilineLiteralString()
        {
            _i += 3;
            SkipOneNewline();
            int start = _i;
            int end = _s.IndexOf("'''", _i, StringComparison.Ordinal);
            if (end < 0) throw Error("文字列が閉じていません");
            // 閉じの ''' の直前に ' を 2 つまで書ける
            while (end + 3 < _s.Length && _s[end + 3] == '\'') end++;
            _i = end + 3;
            return _s[start..end];
        }

        private void AppendEscape(StringBuilder sb)
        {
            if (AtEnd) throw Error("エスケープが途中で終わっています");
            char e = _s[_i++];
            switch (e)
            {
                case 'b': sb.Append('\b'); break;
                case 't': sb.Append('\t'); break;
                case 'n': sb.Append('\n'); break;
                case 'f': sb.Append('\f'); break;
                case 'r': sb.Append('\r'); break;
                case 'e': sb.Append('\u001B'); break;
                case '"': sb.Append('"'); break;
                case '\\': sb.Append('\\'); break;
                case 'x': sb.Append((char)ParseHex(2)); break;
                case 'u': sb.Append((char)ParseHex(4)); break;
                case 'U': sb.Append(char.ConvertFromUtf32(ParseHex(8))); break;
                default: throw Error($"知らないエスケープ \\{e}");
            }
        }

        private int ParseHex(int digits)
        {
            if (_i + digits > _s.Length) throw Error("エスケープが途中で終わっています");
            string hex = _s.Substring(_i, digits);
            if (!int.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int value)) throw Error($"エスケープの数が読めません: {hex}");
            _i += digits;
            return value;
        }

        // ------------------------------------------------------------ 空白・改行・注釈

        private bool StartsWith(string s) => string.CompareOrdinal(_s, _i, s, 0, s.Length) == 0;

        private void SkipSpaces()
        {
            while (Cur is ' ' or '\t') _i++;
        }

        private void SkipComment()
        {
            if (Cur != '#') return;
            while (!AtEnd && Cur is not ('\n' or '\r')) _i++;
        }

        private void SkipOneNewline()
        {
            if (Cur == '\r') _i++;
            if (Cur == '\n') _i++;
        }

        private void SkipWhitespaceAndNewlines()
        {
            while (true)
            {
                SkipSpaces();
                SkipComment();
                if (Cur is '\n' or '\r') _i++;
                else return;
            }
        }

        /// <summary>値や表の見出しのあとは、空白と注釈だけで行が終わること。</summary>
        private void EndOfLine()
        {
            SkipSpaces();
            SkipComment();
            if (AtEnd) return;
            if (Cur is not ('\n' or '\r')) throw Error($"行の終わりに余計な文字があります: {Cur}");
        }

        private void Expect(char c)
        {
            if (Cur != c) throw Error(AtEnd ? $"{c} がありません（ファイルの終わり）" : $"{c} がありません（{Cur}）");
            _i++;
        }

        private FormatException Error(string message)
        {
            int line = 1;
            for (int k = 0; k < Math.Min(_i, _s.Length); k++) if (_s[k] == '\n') line++;
            return new FormatException($"{line} 行目: {message}");
        }
    }
}
