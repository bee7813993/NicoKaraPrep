using System.Text;

namespace NicoKaraPrep.Core.Formats;

/// <summary>HSP の変数型（保存対象のみ）。</summary>
public enum HspVarType : short
{
    Str = 2,
    Double = 3,
    Int = 4,
}

/// <summary>hspda の vsave 形式に保存される変数 1 つ。</summary>
public sealed class HspVariable
{
    public string Name { get; set; } = "";
    public HspVarType Type { get; set; }

    /// <summary>配列の次元（d1..d4、最大4つ。スカラーは [1]）。</summary>
    public int[] Dims { get; set; } = [1];

    /// <summary>Str: 要素ごとの生バッファ（NUL 終端を含む確保サイズ全体）。</summary>
    public byte[][]? StrElements { get; set; }

    /// <summary>Int: 全要素の値（メモリ順 = 第1次元が最も速く変化）。</summary>
    public int[]? IntValues { get; set; }

    /// <summary>Double など: 生バイト列。</summary>
    public byte[]? RawData { get; set; }

    public int ElementCount => Dims.Aggregate(1, (a, d) => a * Math.Max(1, d));
}

/// <summary>
/// HSP 標準プラグイン hspda.dll の vsave / vload バイナリ形式の読み書き。
/// （実ファイル解析によるリバースエンジニアリング結果に基づく）
///
/// ファイル構造:
///   ヘッダ 16B:  "hspv" | int32 0x1000 | int32 変数数 | int32 データ部開始オフセット
///   索引: 変数ごとに 64B（HSP の PVal 構造体ダンプ）
///     +0x00: 16B ランタイムポインタ等（読込時は無視、書込時はゼロ）
///     +0x10: int16 flag（型: 2=str, 3=double, 4=int）
///     +0x12: int16 mode（=1）
///     +0x14: int32 len[5]（len[0]=1 固定、len[1..4]=次元）
///     +0x28: int32 size（int: 4*要素数 / str: 4*要素数（ポインタ表サイズ））
///     +0x2C: int32 pt / +0x30: int32 master（ランタイム値、無視）
///     +0x34: int16 support（str=0x0A, int/double=0x09）
///     +0x36: int16 arraycnt（0）
///     +0x38: int32 offset（0）
///     +0x3C: int32 arraymul（2次元以上: len[1] / それ以外: 1）
///   データ部: 変数ごとに
///     変数名 ASCII + NUL
///     int:    生 int32 × 要素数
///     double: 生 8B × 要素数
///     str:    要素ごとに [int32 0x55AA0000][int32 バッファ長][生バイト×バッファ長]
/// </summary>
public static class HspVsaveFile
{
    private const int HeaderSize = 16;
    private const int RecordSize = 64;
    private const int Version = 0x1000;
    private const int StrBlockMagic = 0x55AA0000;
    private static readonly byte[] Magic = "hspv"u8.ToArray();

    // ---------------------------------------------------------------- Read

    public static List<HspVariable> Read(byte[] data)
    {
        if (data.Length < HeaderSize || data[0] != 'h' || data[1] != 's' || data[2] != 'p' || data[3] != 'v')
            throw new InvalidDataException("hspv ヘッダがありません（rlf ファイルではない可能性があります）");

        int count = BitConverter.ToInt32(data, 8);
        int dataOffset = BitConverter.ToInt32(data, 12);
        if (count < 0 || count > 10000 || dataOffset < HeaderSize || dataOffset > data.Length)
            throw new InvalidDataException("hspv ヘッダの値が不正です");

        var vars = new List<HspVariable>(count);

        // 索引
        for (int i = 0; i < count; i++)
        {
            int rec = HeaderSize + i * RecordSize;
            if (rec + RecordSize > data.Length) throw new InvalidDataException("索引が途中で終わっています");

            short flag = BitConverter.ToInt16(data, rec + 0x10);
            var dims = new List<int>();
            for (int d = 1; d <= 4; d++)
            {
                int len = BitConverter.ToInt32(data, rec + 0x14 + d * 4);
                if (len <= 0) break;
                dims.Add(len);
            }
            if (dims.Count == 0) dims.Add(1);

            vars.Add(new HspVariable
            {
                Type = (HspVarType)flag,
                Dims = dims.ToArray(),
            });
        }

        // データ部
        int pos = dataOffset;
        foreach (var v in vars)
        {
            // 変数名（ASCII, NUL 終端）
            int nameEnd = Array.IndexOf(data, (byte)0, pos);
            if (nameEnd < 0) throw new InvalidDataException("変数名が読めません");
            v.Name = Encoding.ASCII.GetString(data, pos, nameEnd - pos);
            pos = nameEnd + 1;

            int elems = v.ElementCount;
            switch (v.Type)
            {
                case HspVarType.Int:
                {
                    var values = new int[elems];
                    for (int e = 0; e < elems; e++)
                    {
                        values[e] = BitConverter.ToInt32(data, pos);
                        pos += 4;
                    }
                    v.IntValues = values;
                    break;
                }
                case HspVarType.Double:
                {
                    v.RawData = data[pos..(pos + elems * 8)];
                    pos += elems * 8;
                    break;
                }
                case HspVarType.Str:
                {
                    var elements = new byte[elems][];
                    for (int e = 0; e < elems; e++)
                    {
                        int magic = BitConverter.ToInt32(data, pos);
                        if (magic != StrBlockMagic)
                            throw new InvalidDataException($"文字列ブロックのマジックが不正です (変数 {v.Name}, 要素 {e})");
                        int bufSize = BitConverter.ToInt32(data, pos + 4);
                        if (bufSize < 0 || pos + 8 + bufSize > data.Length)
                            throw new InvalidDataException($"文字列ブロック長が不正です (変数 {v.Name}, 要素 {e})");
                        elements[e] = data[(pos + 8)..(pos + 8 + bufSize)];
                        pos += 8 + bufSize;
                    }
                    v.StrElements = elements;
                    break;
                }
                default:
                    throw new InvalidDataException($"未対応の変数型 {v.Type} (変数 {v.Name})");
            }
        }

        return vars;
    }

    // ---------------------------------------------------------------- Write

    public static byte[] Write(IReadOnlyList<HspVariable> vars)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        int dataOffset = HeaderSize + vars.Count * RecordSize;
        w.Write(Magic);
        w.Write(Version);
        w.Write(vars.Count);
        w.Write(dataOffset);

        // 索引
        foreach (var v in vars)
        {
            int[] dims = v.Dims.Length == 0 ? new[] { 1 } : v.Dims;
            int elems = v.ElementCount;
            int size = v.Type switch
            {
                HspVarType.Double => elems * 8,
                _ => elems * 4,
            };

            w.Write(0L); w.Write(0L);                       // +0x00 ランタイム値（ゼロ）
            w.Write((short)v.Type);                          // +0x10 flag
            w.Write((short)1);                               // +0x12 mode
            w.Write(1);                                      // +0x14 len[0]
            for (int d = 0; d < 4; d++)                      // len[1..4]
            {
                w.Write(d < dims.Length ? dims[d] : 0);
            }
            w.Write(size);                                   // +0x28 size
            w.Write(0); w.Write(0);                          // +0x2C pt / +0x30 master
            w.Write((short)(v.Type == HspVarType.Str ? 0x0A : 0x09)); // +0x34 support
            w.Write((short)0);                               // +0x36 arraycnt
            w.Write(0);                                      // +0x38 offset
            w.Write(dims.Length >= 2 ? dims[0] : 1);         // +0x3C arraymul
        }

        // データ部
        foreach (var v in vars)
        {
            w.Write(Encoding.ASCII.GetBytes(v.Name));
            w.Write((byte)0);

            int elems = v.ElementCount;
            switch (v.Type)
            {
                case HspVarType.Int:
                {
                    var values = v.IntValues ?? throw new InvalidOperationException($"IntValues がありません ({v.Name})");
                    if (values.Length != elems) throw new InvalidOperationException($"要素数が次元と一致しません ({v.Name})");
                    foreach (int x in values) w.Write(x);
                    break;
                }
                case HspVarType.Double:
                {
                    var raw = v.RawData ?? throw new InvalidOperationException($"RawData がありません ({v.Name})");
                    w.Write(raw);
                    break;
                }
                case HspVarType.Str:
                {
                    var elements = v.StrElements ?? throw new InvalidOperationException($"StrElements がありません ({v.Name})");
                    if (elements.Length != elems) throw new InvalidOperationException($"要素数が次元と一致しません ({v.Name})");
                    foreach (var buf in elements)
                    {
                        w.Write(StrBlockMagic);
                        w.Write(buf.Length);
                        w.Write(buf);
                    }
                    break;
                }
            }
        }

        return ms.ToArray();
    }
}
