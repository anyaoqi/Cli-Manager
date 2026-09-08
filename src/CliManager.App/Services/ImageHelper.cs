using System.Collections.Concurrent;
using System.IO;
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

    public const string DefaultFolderIcon = "shell32.dll,3";
    public const string DefaultCliIcon = "cmd.exe,0";

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
    /// 获取文件夹的展示图标（优先使用自定义配置，若未指定或提取失败则回退至系统文件夹默认图标）。
    /// </summary>
    public static ImageSource? GetFolderIcon(string? iconPath)
    {
        if (!string.IsNullOrWhiteSpace(iconPath))
        {
            var src = GetIconSource(iconPath);
            if (src != null)
            {
                return src;
            }
        }

        return GetIconSource(DefaultFolderIcon);
    }

    /// <summary>
    /// 获取 CLI 工具的展示图标（依次尝试：自定义图标 -> 可执行程序自身 -> 同级.exe -> 默认终端/CLI图标）。
    /// </summary>
    public static ImageSource? GetToolIcon(string? iconPath, string? executablePath = null)
    {
        // 1. 尝试自定义图标路径
        if (!string.IsNullOrWhiteSpace(iconPath))
        {
            var src = GetIconSource(iconPath);
            if (src != null)
            {
                return src;
            }
        }

        // 2. 尝试从 Executable 提取（若是 .cmd/.bat 则尝试探测同级同名 .exe）
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            string cleanExe = executablePath.Trim().Trim('"');
            if (cleanExe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
                cleanExe.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            {
                string siblingExe = Path.ChangeExtension(cleanExe, ".exe");
                if (File.Exists(siblingExe))
                {
                    var siblingSrc = GetIconSource(siblingExe);
                    if (siblingSrc != null)
                    {
                        return siblingSrc;
                    }
                }
            }

            var exeSrc = GetIconSource(cleanExe);
            if (exeSrc != null)
            {
                return exeSrc;
            }
        }

        // 3. 回退至默认 CLI 工具图标
        return GetIconSource(DefaultCliIcon);
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

