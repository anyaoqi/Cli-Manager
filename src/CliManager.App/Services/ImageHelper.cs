using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CliManager.Core.Icons;

namespace CliManager.App.Services;

/// <summary>
/// 图片辅助服务，负责将 Core 层的 PNG 字节流转为 WPF ImageSource 并进行缓存。
/// 图标解析优先级：自定义图标（本地路径 / 已缓存的网站 favicon）→ 可执行文件自身 → 内置默认图标。
/// </summary>
public static class ImageHelper
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    // 注意：必须使用带 ;component 的完整 pack URI，
    // 否则在测试宿主等非应用入口环境下会解析到错误程序集导致资源加载失败
    private const string DefaultToolIconUri = "pack://application:,,,/CliManager.App;component/Assets/default-tool.png";
    private const string DefaultFolderIconUri = "pack://application:,,,/CliManager.App;component/Assets/default-folder.png";
    /// <summary>
    /// 获取指定路径或内置资源的图标源。
    /// 支持本地文件路径与网站地址（网站地址仅在 favicon 已下载至本地缓存后可显示）。
    /// </summary>
    public static ImageSource? GetIconSource(string? iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath))
        {
            return null;
        }

        string? resolved = ResolveToFilePath(iconPath.Trim());
        if (resolved == null)
        {
            return null;
        }

        // 仅缓存成功加载的结果：启动早期等瞬时失败不落缓存，后续调用可自愈
        if (Cache.TryGetValue(resolved, out var cached) && cached != null)
        {
            return cached;
        }

        var loaded = LoadImageSource(resolved);
        if (loaded != null)
        {
            Cache[resolved] = loaded;
        }

        return loaded;
    }

    /// <summary>
    /// 获取文件夹的展示图标（优先使用自定义配置，未指定或提取失败时回退至内置默认文件夹图标）。
    /// </summary>
    public static ImageSource GetFolderIcon(string? iconPath)
    {
        if (!string.IsNullOrWhiteSpace(iconPath))
        {
            var src = GetIconSource(iconPath);
            if (src != null)
            {
                return src;
            }
        }

        return GetDefaultFolderIcon();
    }

    /// <summary>
    /// 获取 CLI 工具的展示图标（依次尝试：自定义图标 -> 可执行程序自身 -> 同级.exe -> 内置默认工具图标）。
    /// </summary>
    public static ImageSource GetToolIcon(string? iconPath, string? executablePath = null)
    {
        // 1. 尝试自定义图标路径（含已缓存的网站 favicon）
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

        // 3. 回退至内置默认工具图标
        return GetDefaultToolIcon();
    }

    /// <summary>
    /// 内置默认工具图标（扫描未识别、手动添加未配置图标时的统一占位）。
    /// </summary>
    public static ImageSource GetDefaultToolIcon() => LoadDefaultAsset(DefaultToolIconUri, "assets/default-tool.png")!;

    /// <summary>
    /// 内置默认文件夹图标（未配置图标的文件夹统一占位）。
    /// </summary>
    public static ImageSource GetDefaultFolderIcon() => LoadDefaultAsset(DefaultFolderIconUri, "assets/default-folder.png")!;

    /// <summary>
    /// 清理指定缓存（网站地址会同时失效其本地缓存文件键）。
    /// </summary>
    public static void InvalidateCache(string? iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath))
        {
            return;
        }

        Cache.TryRemove(iconPath, out _);
        string? resolved = ResolveToFilePath(iconPath.Trim());
        if (resolved != null)
        {
            Cache.TryRemove(resolved, out _);
        }
    }

    /// <summary>
    /// 将图标配置值解析为可加载的文件路径；网站地址映射为本地 favicon 缓存路径（未下载时返回 null）。
    /// </summary>
    private static string? ResolveToFilePath(string iconPath)
    {
        if (FaviconService.IsWebsiteAddress(iconPath))
        {
            string cachedPath = FaviconService.GetCachedPngPath(iconPath, ConfigStorageService.GetIconCacheDirectory());
            return File.Exists(cachedPath) ? cachedPath : null;
        }

        return iconPath;
    }

    private static ImageSource? LoadImageSource(string path)
    {
        // 内置资源走 pack URI 直接解码
        if (path.StartsWith("pack://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

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
    }

    /// <summary>
    /// 加载内嵌默认图标资源：pack URI 失败时直接从程序集 .g.resources 读取字节兜底
    /// （测试宿主等 WPF 资源子系统未完全就绪的环境下仍可加载）。
    /// </summary>
    private static ImageSource? LoadDefaultAsset(string packUri, string resourceKey)
    {
        var viaPack = LoadImageSource(packUri);
        if (viaPack != null)
        {
            return viaPack;
        }

        try
        {
            var assembly = typeof(ImageHelper).Assembly;
            // WPF 将资源打包为 {Assembly}.g.resources；ResourceManager 会自动追加 ".resources" 后缀，
            // 因此 baseName 必须写 ".g"，键为小写相对路径（如 assets/default-tool.png）
            var manager = new System.Resources.ResourceManager(assembly.GetName().Name + ".g", assembly);
            using var stream = manager.GetStream(resourceKey);
            if (stream == null)
            {
                return null;
            }

            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            frame.Freeze();
            return frame;
        }
        catch
        {
            return null;
        }
    }
}
