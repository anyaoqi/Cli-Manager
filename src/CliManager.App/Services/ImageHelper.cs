using System.Collections.Concurrent;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CliManager.Core.Icons;

namespace CliManager.App.Services;

/// <summary>
/// 图片辅助服务，负责将 Core 层的 PNG 字节流转为 WPF ImageSource 并进行缓存。
/// </summary>
public static class ImageHelper
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 获取指定路径或内置资源的图标源。
    /// </summary>
    public static ImageSource? GetIconSource(string? iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath))
        {
            return null;
        }

        return Cache.GetOrAdd(iconPath, path =>
        {
            byte[]? bytes = IconExtractor.ExtractPngBytes(path);
            if (bytes == null || bytes.Length == 0)
            {
                return null;
            }

            try
            {
                var bitmap = new BitmapImage();
                using var ms = new MemoryStream(bytes);
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        });
    }

    /// <summary>
    /// 清理指定缓存。
    /// </summary>
    public static void InvalidateCache(string? iconPath)
    {
        if (!string.IsNullOrWhiteSpace(iconPath))
        {
            Cache.TryRemove(iconPath, out _);
        }
    }
}
