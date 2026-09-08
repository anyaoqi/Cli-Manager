using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CliManager.Core.Icons;

/// <summary>
/// 图标提取器：从可执行文件、DLL 或图片文件中提取原始 PNG 字节。
/// 严格满足 §2.1 架构守则：零 UI 框架依赖，只输出 byte[]。
/// </summary>
public static partial class IconExtractor
{
    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern int ExtractIconEx(string szFileName, int nIconIndex, IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, int nIcons);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>
    /// 从指定路径提取图标并转为 PNG 字节数组。
    /// 支持 "path" 或 "path,index" 格式。
    /// </summary>
    public static byte[]? ExtractPngBytes(string? iconPathWithIndex)
    {
        if (string.IsNullOrWhiteSpace(iconPathWithIndex))
        {
            return null;
        }

        string cleanPath = Environment.ExpandEnvironmentVariables(iconPathWithIndex.Trim());
        int iconIndex = 0;

        // 解析 path,index 形式
        int commaIndex = cleanPath.LastIndexOf(',');
        if (commaIndex > 0 && int.TryParse(cleanPath[(commaIndex + 1)..], out int parsedIndex))
        {
            iconIndex = parsedIndex;
            cleanPath = cleanPath[..commaIndex].Trim().Trim('"');
        }
        else
        {
            cleanPath = cleanPath.Trim('"');
        }

        if (!File.Exists(cleanPath))
        {
            string sysCandidate = Path.Combine(Environment.SystemDirectory, cleanPath);
            if (File.Exists(sysCandidate))
            {
                cleanPath = sysCandidate;
            }
            else
            {
                return null;
            }
        }

        string ext = Path.GetExtension(cleanPath).ToLowerInvariant();

        // 1. 如果本身是 png 或图片文件，直接读取文件字节
        if (ext is ".png" or ".jpg" or ".jpeg")
        {
            try
            {
                return File.ReadAllBytes(cleanPath);
            }
            catch
            {
                return null;
            }
        }

        // 2. 如果是 .ico 文件，读取为 Icon 转 PNG 字节
        if (ext is ".ico")
        {
            try
            {
                using var icon = new Icon(cleanPath);
                return IconToPngBytes(icon);
            }
            catch
            {
                return null;
            }
        }

        // 3. 如果是 .exe / .dll，使用 Win32 ExtractIconEx 提取指定索引图标
        try
        {
            IntPtr[] largeIcons = new IntPtr[1];
            int count = ExtractIconEx(cleanPath, iconIndex, largeIcons, null, 1);
            if (count > 0 && largeIcons[0] != IntPtr.Zero)
            {
                try
                {
                    using var icon = Icon.FromHandle(largeIcons[0]);
                    return IconToPngBytes(icon);
                }
                finally
                {
                    DestroyIcon(largeIcons[0]);
                }
            }
        }
        catch
        {
            // 忽略并降级
        }

        // 4. 降级：使用 ExtractAssociatedIcon
        try
        {
            using var associatedIcon = Icon.ExtractAssociatedIcon(cleanPath);
            if (associatedIcon != null)
            {
                return IconToPngBytes(associatedIcon);
            }
        }
        catch
        {
            // 忽略
        }

        return null;
    }

    private static byte[]? IconToPngBytes(Icon icon)
    {
        try
        {
            using var bmp = icon.ToBitmap();
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }
}
