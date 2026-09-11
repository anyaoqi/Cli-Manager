using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using CliManager.Core.Models;

namespace CliManager.Core.Icons;

/// <summary>
/// 网站 favicon 抓取服务（零 UI 依赖，仅输入网址、输出本地缓存文件路径）。
/// 抓取优先级：HTML <link rel="icon"> 候选（大尺寸优先）→ /favicon.ico；
/// 结果规范化为 PNG 缓存，并同时生成同名 .ico 伴侣文件（注册表 Icon 值不接受 PNG 引用）。
/// </summary>
public static partial class FaviconService
{
    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            ConnectTimeout = TimeSpan.FromSeconds(6)
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.ParseAdd("image/*,text/html;q=0.8,*/*;q=0.5");
        return client;
    }

    // ---------------- 纯函数（单元测试覆盖） ----------------

    /// <summary>
    /// 判断图标配置值是否为网站地址（绝对 http/https URL 或可解析的裸域名）。
    /// 本地文件路径（含盘符、反斜杠、逗号索引等）一律返回 false。
    /// </summary>
    public static bool IsWebsiteAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string v = value.Trim().Trim('"');
        if (v.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            v.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return TryCreateHttpUri(v, out _);
        }

        // 其余带协议的 URI（pack:、file:、ftp: 等）一律不是可抓取的网站地址；
        // 仅放行 host:port 形式（端口为纯数字），如 localhost:3000
        int colonIndex = v.IndexOf(':');
        if (colonIndex >= 0)
        {
            bool isHostPort = v.IndexOf(':', colonIndex + 1) < 0 &&
                              int.TryParse(v[(colonIndex + 1)..], out _);
            if (!isHostPort)
            {
                return false;
            }
        }

        // 裸域名粗筛：排除本地路径特征
        if (v.Contains('\\') || v.Contains(' ') || v.Contains(',') ||
            v.Contains('<') || v.Contains('>') || v.Contains('|') || v.Contains('*') || v.Contains('?'))
        {
            return false;
        }

        if (File.Exists(Environment.ExpandEnvironmentVariables(v)))
        {
            return false;
        }

        return TryCreateHttpUri("https://" + v, out _);
    }

    /// <summary>
    /// 将网站地址规范化为绝对 http/https URL（裸域名自动补全 https://）；无法解析时返回 null。
    /// </summary>
    public static string? NormalizeWebsiteUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string v = value.Trim().Trim('"');
        if (v.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            v.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return TryCreateHttpUri(v, out Uri? uri) ? uri.ToString() : null;
        }

        if (IsWebsiteAddress(v) && TryCreateHttpUri("https://" + v, out Uri? built))
        {
            return built.ToString();
        }

        return null;
    }

    /// <summary>
    /// 生成 favicon 缓存文件基名（基于主机名，已净化非法字符）。
    /// </summary>
    public static string BuildCacheFileBase(string websiteUrl)
    {
        string? normalized = NormalizeWebsiteUrl(websiteUrl);
        string source = normalized ?? websiteUrl;
        string host = source;
        try
        {
            if (Uri.TryCreate(source, UriKind.Absolute, out Uri? uri))
            {
                host = uri.IsDefaultPort ? uri.Host : $"{uri.Host}_{uri.Port}";
            }
        }
        catch
        {
            // 保留原始输入做净化
        }

        host = host.ToLowerInvariant();
        var sb = new System.Text.StringBuilder(host.Length);
        foreach (char c in host)
        {
            bool safe = char.IsLetterOrDigit(c) || c is '.' or '-' or '_';
            sb.Append(safe ? c : '_');
        }

        return sb.ToString();
    }

    /// <summary>
    /// 从 HTML 中提取 favicon 候选地址（按尺寸降序排列）。
    /// 返回值可能是绝对 URL 或 data: 内联 URI；SVG 引用被过滤（无法光栅化）。
    /// </summary>
    public static IReadOnlyList<string> ExtractIconCandidates(string html, Uri baseUrl)
    {
        var candidates = new List<(int Size, string Href)>();

        foreach (Match link in LinkTagRegex().Matches(html))
        {
            string tag = link.Value;
            string rel = GetAttribute(tag, "rel").ToLowerInvariant();
            if (!rel.Contains("icon"))
            {
                continue;
            }

            string href = GetAttribute(tag, "href");
            if (string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            string type = GetAttribute(tag, "type").ToLowerInvariant();
            if (type.Contains("svg") || href.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int size = ParseMaxSize(GetAttribute(tag, "sizes"));
            if (rel.Contains("apple-touch"))
            {
                // apple-touch-icon 通常为 180px 高清图，未声明尺寸时也给高权重
                size = Math.Max(size, 180);
            }

            candidates.Add((size, href));
        }

        return candidates
            .OrderByDescending(c => c.Size)
            .Select(c => ResolveHref(c.Href, baseUrl))
            .Where(h => h != null)
            .Cast<string>()
            .ToList();
    }

    private static string? ResolveHref(string href, Uri baseUrl)
    {
        if (href.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return href;
        }

        if (Uri.TryCreate(baseUrl, href, out Uri? resolved) &&
            (resolved.Scheme == Uri.UriSchemeHttps || resolved.Scheme == Uri.UriSchemeHttp))
        {
            return resolved.ToString();
        }

        return null;
    }

    private static int ParseMaxSize(string sizes)
    {
        int max = 0;
        foreach (Match m in SizeRegex().Matches(sizes))
        {
            if (int.TryParse(m.Groups[1].Value, out int w) && w > max)
            {
                max = w;
            }
        }

        return max;
    }

    private static string GetAttribute(string tag, string name)
    {
        Match m = AttributeRegex(name).Match(tag);
        if (!m.Success)
        {
            return string.Empty;
        }

        if (m.Groups[1].Success)
        {
            return m.Groups[1].Value;
        }

        if (m.Groups[2].Success)
        {
            return m.Groups[2].Value;
        }

        return m.Groups[3].Value;
    }

    [GeneratedRegex("<link\\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex LinkTagRegex();

    private static Regex SizeRegex() => new("(\\d+)x\\d+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static Regex AttributeRegex(string name)
    {
        string pattern = $"\\b{name}\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+))";
        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    // ---------------- 网络抓取 ----------------

    /// <summary>
    /// 抓取网站最佳 favicon 并转为 PNG 字节；失败返回 null。
    /// </summary>
    public static byte[]? FetchIconBytes(string websiteUrl)
    {
        string? normalized = NormalizeWebsiteUrl(websiteUrl);
        if (normalized == null || !Uri.TryCreate(normalized, UriKind.Absolute, out Uri? baseUri))
        {
            return null;
        }

        var candidates = new List<string>();
        try
        {
            string html = Http.GetStringAsync(baseUri).GetAwaiter().GetResult();
            candidates.AddRange(ExtractIconCandidates(html, baseUri));
        }
        catch
        {
            // 首页抓取失败不阻塞：继续尝试 /favicon.ico
        }

        candidates.Add(new Uri(baseUri, "/favicon.ico").ToString());
        if (baseUri.Scheme == Uri.UriSchemeHttps)
        {
            // https 不可达时兜底尝试 http（部分内网/旧站点）
            candidates.Add(new Uri("http://" + baseUri.Authority + "/favicon.ico").ToString());
        }

        foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                byte[]? raw = candidate.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                    ? DecodeDataUri(candidate)
                    : Http.GetByteArrayAsync(candidate).GetAwaiter().GetResult();

                byte[]? png = IcoDecoder.ConvertToPng(raw);
                if (png is { Length: > 16 })
                {
                    return png;
                }
            }
            catch
            {
                // 单个候选失败继续下一个
            }
        }

        return null;
    }

    /// <summary>
    /// 抓取 favicon 并写入本地缓存（{host}.png + {host}.ico）。
    /// 返回 PNG 缓存路径；抓取失败返回 null。
    /// </summary>
    public static string? FetchToCache(string websiteUrl, string cacheDirectory)
    {
        byte[]? png = FetchIconBytes(websiteUrl);
        if (png == null)
        {
            return null;
        }

        Directory.CreateDirectory(cacheDirectory);
        string pngPath = GetCachedPngPath(websiteUrl, cacheDirectory);
        File.WriteAllBytes(pngPath, png);

        byte[]? ico = IcoDecoder.WrapPngAsIco(png);
        if (ico != null)
        {
            File.WriteAllBytes(GetCachedIcoPath(websiteUrl, cacheDirectory), ico);
        }

        return pngPath;
    }

    /// <summary>
    /// 遍历配置，把所有网站地址图标下载为本地缓存路径（抓取失败保持原网址，不覆盖为 null，由同步引擎回退默认图标）。
    /// 返回成功解析的数量。
    /// </summary>
    public static int ResolveConfigIcons(CliConfig config, string cacheDirectory)
    {
        int resolved = 0;

        foreach (var tool in config.Tools)
        {
            if (IsWebsiteAddress(tool.Icon))
            {
                string? cached = FetchToCache(tool.Icon!.Trim(), cacheDirectory);
                if (cached != null)
                {
                    tool.Icon = cached;
                    resolved++;
                }
            }
        }

        foreach (var folder in config.Folders)
        {
            if (IsWebsiteAddress(folder.Icon))
            {
                string? cached = FetchToCache(folder.Icon!.Trim(), cacheDirectory);
                if (cached != null)
                {
                    folder.Icon = cached;
                    resolved++;
                }
            }
        }

        return resolved;
    }

    // ---------------- 缓存路径 ----------------

    public static string GetCachedPngPath(string websiteUrl, string cacheDirectory)
    {
        return Path.Combine(cacheDirectory, BuildCacheFileBase(websiteUrl) + ".png");
    }

    public static string GetCachedIcoPath(string websiteUrl, string cacheDirectory)
    {
        return Path.Combine(cacheDirectory, BuildCacheFileBase(websiteUrl) + ".ico");
    }

    private static byte[]? DecodeDataUri(string dataUri)
    {
        int commaIndex = dataUri.IndexOf(',');
        if (commaIndex < 0)
        {
            return null;
        }

        string header = dataUri[..commaIndex];
        string payload = dataUri[(commaIndex + 1)..];
        if (!header.Contains("base64", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(payload);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryCreateHttpUri(string value, [NotNullWhen(true)] out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? parsed))
        {
            return false;
        }

        if (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp)
        {
            return false;
        }

        if (string.IsNullOrEmpty(parsed.Host) ||
            parsed.HostNameType != UriHostNameType.Dns && parsed.HostNameType != UriHostNameType.IPv4 && parsed.HostNameType != UriHostNameType.IPv6)
        {
            return false;
        }

        uri = parsed;
        return true;
    }
}
