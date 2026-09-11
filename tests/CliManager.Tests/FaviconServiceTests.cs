using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using CliManager.Core.Icons;
using CliManager.Core.Models;
using CliManager.Core.Registry;
using Microsoft.Win32;

namespace CliManager.Tests;

public class FaviconServiceTests
{
    [Theory]
    [InlineData("https://claude.ai", true)]
    [InlineData("http://192.168.1.1:3000", true)]
    [InlineData("claude.ai", true)]
    [InlineData("www.anthropic.com", true)]
    [InlineData("localhost:3000", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(@"C:\Tools\app.png", false)]
    [InlineData("shell32.dll,3", false)]
    [InlineData("cmd.exe,0", false)]
    [InlineData("%ProgramFiles%\\app.exe,0", false)]
    [InlineData("pack://application:,,,/Assets/default-tool.png", false)]
    [InlineData(@"file:///C:\Tools\icon.ico", false)]
    public void IsWebsiteAddress_DetectsWebsiteAddresses(string? value, bool expected)
    {
        Assert.Equal(expected, FaviconService.IsWebsiteAddress(value));
    }

    [Theory]
    [InlineData("claude.ai", "https://claude.ai")]
    [InlineData("HTTPS://CLAUDE.AI", "https://claude.ai")]
    [InlineData("  https://claude.ai  ", "https://claude.ai")]
    public void NormalizeWebsiteUrl_AddsSchemeAndTrims(string input, string expectedPrefix)
    {
        string? normalized = FaviconService.NormalizeWebsiteUrl(input);
        Assert.NotNull(normalized);
        Assert.StartsWith(expectedPrefix, normalized, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(@"C:\Tools\app.png")]
    [InlineData("")]
    [InlineData(null)]
    public void NormalizeWebsiteUrl_ReturnsNullForNonWebsite(string? input)
    {
        Assert.Null(FaviconService.NormalizeWebsiteUrl(input));
    }

    [Theory]
    [InlineData("https://claude.ai", "claude.ai")]
    [InlineData("http://localhost:3000", "localhost_3000")]
    public void BuildCacheFileBase_ProducesSafeFileName(string url, string expected)
    {
        Assert.Equal(expected, FaviconService.BuildCacheFileBase(url));
    }

    [Fact]
    public void ExtractIconCandidates_PrefersLargerSizesAndFiltersSvg()
    {
        var baseUri = new Uri("https://example.com");
        string html = """
            <html><head>
            <link rel="icon" type="image/svg+xml" href="/icon.svg">
            <link rel="icon" sizes="32x32" href="/favicon-32x32.png">
            <link rel="apple-touch-icon" sizes="180x180" href="/touch-icon.png">
            <link rel="shortcut icon" href="data:image/png;base64,iVBORw0KGgo=">
            </head></html>
            """;

        var candidates = FaviconService.ExtractIconCandidates(html, baseUri);

        // apple-touch-icon (180px) 优先，其次 32px，data URI 保留，SVG 被过滤
        Assert.Equal("/touch-icon.png", new Uri(candidates[0]).AbsolutePath);
        Assert.Equal("/favicon-32x32.png", new Uri(candidates[1]).AbsolutePath);
        Assert.StartsWith("data:image/png;base64,", candidates[2]);
        Assert.DoesNotContain(candidates, c => c.EndsWith(".svg", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(candidates, c => c.Contains("icon.svg"));
    }

    [Fact]
    public void IcoDecoder_WrapAndConvertRoundtrip_PreservesPngBytes()
    {
        byte[] png = CreateTestPng(12, 8);

        byte[]? ico = IcoDecoder.WrapPngAsIco(png);
        Assert.NotNull(ico);
        // ICO 头：保留字 + 类型(1) + 数量(1)
        Assert.Equal(1, ico[2]);
        Assert.Equal(1, ico[4]);
        Assert.Equal(12, ico[6]);
        Assert.Equal(8, ico[7]);

        byte[]? decoded = IcoDecoder.ConvertToPng(ico);
        Assert.NotNull(decoded);
        Assert.Equal(png, decoded);
    }

    [Fact]
    public void IcoDecoder_ConvertToPng_ReturnsPngInputAsIs()
    {
        byte[] png = CreateTestPng(8, 8);
        Assert.Same(png, IcoDecoder.ConvertToPng(png));
    }

    [Fact]
    public void IcoDecoder_ConvertToPng_ReturnsNullForGarbage()
    {
        Assert.Null(IcoDecoder.ConvertToPng([1, 2, 3, 4, 5]));
        Assert.Null(IcoDecoder.ConvertToPng(null));
        Assert.Null(IcoDecoder.WrapPngAsIco([1, 2, 3, 4, 5]));
    }

    private static byte[] CreateTestPng(int width, int height)
    {
        using var bmp = new Bitmap(width, height);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }
}

/// <summary>
/// 默认图标写入注册表的集成测试：未配置图标 / 网址图标 / PNG 图标均应解析为可被 Explorer 显示的本地 ICO。
/// </summary>
public class DefaultIconSyncTests : IDisposable
{
    private readonly string _testBasePath = $@"Software\CliManager_UnitTests_{Guid.NewGuid():N}";
    private readonly string _appDir;
    private readonly RegistrySyncEngine _engine;

    public DefaultIconSyncTests()
    {
        // 构造假程序目录（含 Assets 默认图标文件）
        _appDir = Path.Combine(Path.GetTempPath(), $"climgr-icons-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_appDir, "Assets"));
        File.WriteAllBytes(Path.Combine(_appDir, "Assets", "default-tool.ico"), [0, 0, 1, 0, 1, 0]);
        File.WriteAllBytes(Path.Combine(_appDir, "Assets", "default-folder.ico"), [0, 0, 1, 0, 1, 0]);

        _engine = new RegistrySyncEngine(Microsoft.Win32.Registry.CurrentUser, _testBasePath);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(_testBasePath, throwOnMissingSubKey: false);
        }
        catch
        {
            // 忽略测试清理异常
        }

        try
        {
            Directory.Delete(_appDir, recursive: true);
        }
        catch
        {
            // 忽略
        }
    }

    [Fact]
    public void Sync_EmptyIcons_WriteDefaultIconPathsToRegistry()
    {
        var config = new CliConfig
        {
            Folders = [new FolderItem { Id = "f1", Name = "分组", Order = 10, Enabled = true }],
            Tools =
            [
                new ToolItem
                {
                    Id = "t1",
                    Name = "Claude Code",
                    Executable = @"C:\tools\claude.exe",
                    Icon = null,
                    Order = 10,
                    Enabled = true
                }
            ]
        };

        var result = _engine.Sync(config, resolvedWtPath: null, backupDir: null, appBaseDirectory: _appDir);
        Assert.True(result.Success);

        using var root = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(_testBasePath);
        Assert.NotNull(root);

        var keys = root.GetSubKeyNames();
        var folderKey = root.OpenSubKey(keys.First(k => k.Contains("f1")));
        var toolKey = root.OpenSubKey(keys.First(k => k.Contains("t1")));

        string? folderIcon = folderKey?.GetValue("Icon") as string;
        string? toolIcon = toolKey?.GetValue("Icon") as string;

        Assert.Equal(Path.Combine(_appDir, "Assets", "default-folder.ico"), folderIcon);
        Assert.Equal(Path.Combine(_appDir, "Assets", "default-tool.ico"), toolIcon);
    }

    [Fact]
    public void Sync_WebsiteAddressIcon_FallsBackToDefaultIcon()
    {
        var config = new CliConfig
        {
            Tools =
            [
                new ToolItem
                {
                    Id = "t2",
                    Name = "Web Tool",
                    Executable = @"C:\tools\web.exe",
                    Icon = "https://claude.ai",
                    Order = 10,
                    Enabled = true
                }
            ]
        };

        var result = _engine.Sync(config, null, null, _appDir);
        Assert.True(result.Success);

        using var root = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(_testBasePath);
        var toolKey = root?.OpenSubKey(root.GetSubKeyNames().First(k => k.Contains("t2")));
        string? icon = toolKey?.GetValue("Icon") as string;

        // 网址无法被 Explorer 解析，必须回退默认图标
        Assert.Equal(Path.Combine(_appDir, "Assets", "default-tool.ico"), icon);
    }

    [Fact]
    public void Sync_PngIconWithCompanionIco_UsesIcoInstead()
    {
        string pngPath = Path.Combine(_appDir, "custom.png");
        string icoPath = Path.ChangeExtension(pngPath, ".ico");
        File.WriteAllBytes(pngPath, [0x89, 0x50]);
        File.WriteAllBytes(icoPath, [0, 0, 1, 0]);

        var config = new CliConfig
        {
            Tools =
            [
                new ToolItem
                {
                    Id = "t3",
                    Name = "Png Tool",
                    Executable = @"C:\tools\png.exe",
                    Icon = pngPath,
                    Order = 10,
                    Enabled = true
                }
            ]
        };

        var result = _engine.Sync(config, null, null, _appDir);
        Assert.True(result.Success);

        using var root = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(_testBasePath);
        var toolKey = root?.OpenSubKey(root.GetSubKeyNames().First(k => k.Contains("t3")));
        string? icon = toolKey?.GetValue("Icon") as string;

        Assert.Equal(icoPath, icon);
    }

    [Fact]
    public void Sync_PngIconWithoutCompanionIco_GeneratesIcoAutomatically()
    {
        // 构造一个真实的单帧 PNG 文件（无 companion .ico）
        string pngPath = Path.Combine(_appDir, "standalone.png");
        using (var bmp = new Bitmap(16, 16))
        {
            bmp.Save(pngPath, ImageFormat.Png);
        }

        string expectedIco = Path.ChangeExtension(pngPath, ".ico");
        Assert.False(File.Exists(expectedIco));

        var config = new CliConfig
        {
            Tools =
            [
                new ToolItem
                {
                    Id = "t-png-auto",
                    Name = "Auto Png Tool",
                    Executable = @"C:\tools\app.exe",
                    Icon = pngPath,
                    Order = 10,
                    Enabled = true
                }
            ]
        };

        var result = _engine.Sync(config, null, null, _appDir);
        Assert.True(result.Success);

        using var root = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(_testBasePath);
        var toolKey = root?.OpenSubKey(root.GetSubKeyNames().First(k => k.Contains("Auto_Png_Tool")));
        string? icon = toolKey?.GetValue("Icon") as string;

        // 验证自动生成了 .ico 伴侣文件且注册表指向该 .ico，而不是 Explorer 无法解析的 .png
        Assert.NotNull(icon);
        Assert.EndsWith(".ico", icon, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(icon));
    }

    [Fact]
    public void ResolveConfigIcons_PreservesOriginalUrlWhenDownloadFails()
    {
        // 模拟不可达地址：抓取失败时应保持原 URL，绝不覆盖为 null 导致用户输入丢失
        var config = new CliConfig
        {
            Tools =
            [
                new ToolItem
                {
                    Id = "t-fail",
                    Name = "Offline Site",
                    Executable = @"C:\tools\app.exe",
                    Icon = "https://unreachable-domain-123456789.xyz"
                }
            ]
        };

        string cacheDir = Path.Combine(_appDir, "test_cache");
        int resolved = FaviconService.ResolveConfigIcons(config, cacheDir);

        Assert.Equal(0, resolved);
        Assert.Equal("https://unreachable-domain-123456789.xyz", config.Tools[0].Icon);
    }

    [Fact]
    public void Sync_WithoutBaseDirectory_KeepsLegacyBehavior()
    {
        var config = new CliConfig
        {
            Tools =
            [
                new ToolItem
                {
                    Id = "t4",
                    Name = "Legacy Tool",
                    Executable = @"C:\tools\legacy.exe",
                    Icon = null,
                    Order = 10,
                    Enabled = true
                }
            ]
        };

        var result = _engine.Sync(config);
        Assert.True(result.Success);

        using var root = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(_testBasePath);
        var toolKey = root?.OpenSubKey(root.GetSubKeyNames().First(k => k.Contains("t4")));

        // 未提供程序目录时保持旧行为：不写 Icon 值
        Assert.Null(toolKey?.GetValue("Icon"));
    }
}
