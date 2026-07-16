namespace TimeTagTool.Core.Formats;

/// <summary>
/// 画像ファイル（PNG / JPEG / GIF / BMP）のピクセルサイズをヘッダだけ読んで取得する。
/// （デコード不要・依存ライブラリ不要）
/// </summary>
public static class ImageSizeReader
{
    public static bool TryGetSize(string path, out int width, out int height)
    {
        width = 0;
        height = 0;
        try
        {
            if (!File.Exists(path)) return false;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var head = new byte[32];
            int read = fs.Read(head, 0, head.Length);
            if (read < 10) return false;

            // PNG: 8B シグネチャ + IHDR チャンク（幅・高さはビッグエンディアン）
            if (head[0] == 0x89 && head[1] == 'P' && head[2] == 'N' && head[3] == 'G')
            {
                if (read < 24) return false;
                width = (head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19];
                height = (head[20] << 24) | (head[21] << 16) | (head[22] << 8) | head[23];
                return width > 0 && height > 0;
            }

            // GIF: "GIF87a"/"GIF89a" + リトルエンディアン u16
            if (head[0] == 'G' && head[1] == 'I' && head[2] == 'F')
            {
                width = head[6] | (head[7] << 8);
                height = head[8] | (head[9] << 8);
                return width > 0 && height > 0;
            }

            // BMP: "BM" + BITMAPINFOHEADER（幅 int32 @18、高さ int32 @22）
            if (head[0] == 'B' && head[1] == 'M')
            {
                if (read < 26) return false;
                width = BitConverter.ToInt32(head, 18);
                height = Math.Abs(BitConverter.ToInt32(head, 22));
                return width > 0 && height > 0;
            }

            // JPEG: SOF0/1/2 マーカーを探す
            if (head[0] == 0xFF && head[1] == 0xD8)
            {
                fs.Position = 2;
                var buf = new byte[4];
                while (true)
                {
                    int b = fs.ReadByte();
                    if (b < 0) return false;
                    if (b != 0xFF) continue;
                    int marker = fs.ReadByte();
                    if (marker < 0) return false;
                    if (marker == 0xFF) { fs.Position--; continue; }
                    if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7)) continue; // 長さ無しマーカー

                    if (fs.Read(buf, 0, 2) != 2) return false;
                    int length = (buf[0] << 8) | buf[1];

                    if (marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF)
                    {
                        fs.ReadByte(); // 精度
                        if (fs.Read(buf, 0, 4) != 4) return false;
                        height = (buf[0] << 8) | buf[1];
                        width = (buf[2] << 8) | buf[3];
                        return width > 0 && height > 0;
                    }
                    fs.Position += length - 2;
                }
            }

            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
