using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Imaging;

namespace CliManager.Core.Icons;

/// <summary>
/// ICO 解码与封装工具：
/// - 解析 ICO 目录并提取最佳帧转为 PNG 字节（兼容现代 PNG 压缩条目，.NET 内置 Icon 类无法解析）；
/// - 将 PNG 字节封装为单条目 ICO，供注册表 Icon 值使用（Explorer 右键菜单不接受 PNG 文件引用）。
/// 严格满足 §2.1 架构守则：零 UI 框架依赖，只处理 byte[]。
/// </summary>
public static class IcoDecoder
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static bool IsPng(byte[]? bytes)
    {
        return bytes is { Length: >= 8 } && bytes.AsSpan(0, 8).SequenceEqual(PngSignature);
    }

    /// <summary>
    /// 将任意图像字节（PNG/JPEG/GIF/BMP/ICO）规范化为 PNG 字节；无法解码时返回 null。
    /// </summary>
    public static byte[]? ConvertToPng(byte[]? raw)
    {
        if (raw == null || raw.Length == 0)
        {
            return null;
        }

        if (IsPng(raw))
        {
            return raw;
        }

        // ICO：文件头 00 00 01 00
        if (raw.Length > 6 && raw[0] == 0 && raw[1] == 0 && raw[2] == 1 && raw[3] == 0)
        {
            return DecodeIcoToPng(raw);
        }

        // JPEG / GIF / BMP：GDI+ 解码后重编码为 PNG
        try
        {
            using var ms = new MemoryStream(raw);
            using var image = Image.FromStream(ms);
            return ImageToPngBytes(image);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 将 PNG 字节封装为单条目 ICO 字节（Vista+ 支持 PNG 压缩条目，Win10/11 Explorer 渲染正常）。
    /// </summary>
    public static byte[]? WrapPngAsIco(byte[]? png)
    {
        if (!IsPng(png) || png!.Length < 24)
        {
            return null;
        }

        // PNG IHDR：宽高固定位于第 16-23 字节（大端）
        int width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16));
        int height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20));
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        byte widthByte = width >= 256 ? (byte)0 : (byte)width;
        byte heightByte = height >= 256 ? (byte)0 : (byte)height;

        using var ms = new MemoryStream(6 + 16 + png.Length);
        using var bw = new BinaryWriter(ms);
        bw.Write((ushort)0);              // reserved
        bw.Write((ushort)1);              // type: icon
        bw.Write((ushort)1);              // image count
        bw.Write(widthByte);
        bw.Write(heightByte);
        bw.Write((byte)0);                // palette colors
        bw.Write((byte)0);                // reserved
        bw.Write((ushort)1);              // color planes
        bw.Write((ushort)32);             // bits per pixel
        bw.Write((uint)png.Length);       // bytes in resource
        bw.Write((uint)22);               // image offset (6 + 16)
        bw.Write(png);
        bw.Flush();
        return ms.ToArray();
    }

    private static byte[]? DecodeIcoToPng(byte[] ico)
    {
        try
        {
            int count = BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4));
            int bestScore = -1;
            int bestOffset = -1;
            int bestLength = 0;
            bool bestIsPng = false;
            int bestWidth = 0;
            int bestHeight = 0;

            for (int i = 0; i < count; i++)
            {
                int entry = 6 + (i * 16);
                if (entry + 16 > ico.Length)
                {
                    break;
                }

                int width = ico[entry] == 0 ? 256 : ico[entry];
                int height = ico[entry + 1] == 0 ? 256 : ico[entry + 1];
                int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(ico.AsSpan(entry + 8));
                int offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(ico.AsSpan(entry + 12));
                if (length <= 0 || offset < 0 || offset + length > ico.Length)
                {
                    continue;
                }

                bool isPng = length > 8 && IsPng(ico[offset..(offset + 8)]);
                // 同尺寸优先 PNG 帧（平滑 alpha 通道），其次取面积最大帧
                int score = (width * height) + (isPng ? 1 : 0);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestOffset = offset;
                    bestLength = length;
                    bestIsPng = isPng;
                    bestWidth = width;
                    bestHeight = height;
                }
            }

            if (bestOffset < 0)
            {
                return null;
            }

            if (bestIsPng)
            {
                byte[] png = new byte[bestLength];
                Array.Copy(ico, bestOffset, png, 0, bestLength);
                return png;
            }

            // BMP 条目：重建仅含该帧的最小 ICO 流交给 GDI+ Icon 解码
            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write((ushort)0);
                bw.Write((ushort)1);
                bw.Write((ushort)1);
                bw.Write(bestWidth >= 256 ? (byte)0 : (byte)bestWidth);
                bw.Write(bestHeight >= 256 ? (byte)0 : (byte)bestHeight);
                bw.Write((byte)0);
                bw.Write((byte)0);
                bw.Write((ushort)1);
                bw.Write((ushort)32);
                bw.Write((uint)bestLength);
                bw.Write((uint)22);
                bw.Write(ico, bestOffset, bestLength);
                bw.Flush();
            }

            ms.Position = 0;
            using var icon = new Icon(ms);
            using var bitmap = icon.ToBitmap();
            return ImageToPngBytes(bitmap);
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? ImageToPngBytes(Image image)
    {
        try
        {
            using var ms = new MemoryStream();
            image.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }
}
