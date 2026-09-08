using CliManager.Core.Migration;
using CliManager.Core.Models;
using CliManager.Core.Registry;
using Microsoft.Win32;

namespace CliManager.Tests;

public class MigrationTests : IDisposable
{
    private readonly string _testBasePath;
    private readonly RegistryKey _rootKey;

    public MigrationTests()
    {
        _rootKey = Microsoft.Win32.Registry.CurrentUser;
        _testBasePath = $@"Software\CliManager_MigrationTests_{Guid.NewGuid():N}";
    }

    public void Dispose()
    {
        try
        {
            _rootKey.DeleteSubKeyTree(_testBasePath, throwOnMissingSubKey: false);
        }
        catch
        {
            // 忽略
        }
    }

    [Fact]
    public void ConvertToToolItem_PreservesProperties()
    {
        var legacy = new LegacyMenuItem
        {
            DisplayName = "My Claude",
            Icon = @"C:\tools\claude.ico",
            ExtractedExecutable = @"C:\tools\claude.exe",
            ExtractedArgs = ["--fast"],
            InferredHost = TerminalHosts.WindowsTerminal,
            IsDeadLink = false
        };

        var tool = MigrationService.ConvertToToolItem(legacy, "folder-1", 30);

        Assert.Equal("My Claude", tool.Name);
        Assert.Equal(@"C:\tools\claude.ico", tool.Icon);
        Assert.Equal(@"C:\tools\claude.exe", tool.Executable);
        Assert.Single(tool.Args);
        Assert.Equal("--fast", tool.Args[0]);
        Assert.Equal(TerminalHosts.WindowsTerminal, tool.Host);
        Assert.Equal("folder-1", tool.ParentId);
        Assert.Equal(30, tool.Order);
        Assert.True(tool.Enabled);
    }

    [Fact]
    public void ConvertToToolItem_WhenDeadLink_SetsEnabledFalse()
    {
        var legacy = new LegacyMenuItem
        {
            DisplayName = "Dead Tool",
            ExtractedExecutable = @"C:\non_existent_folder\dead.exe",
            IsDeadLink = true
        };

        var tool = MigrationService.ConvertToToolItem(legacy);
        Assert.False(tool.Enabled);
    }

    [Fact]
    public void IndirectStringResolver_ResolvesSystemStringsOrFallsBack()
    {
        // 普通字符串原样返回
        Assert.Equal("Plain Text", IndirectStringResolver.Resolve("Plain Text"));

        // 系统标准 MUI 字符串在 Windows 上解析为非空文字
        string cmdResolved = IndirectStringResolver.Resolve("@shell32.dll,-8506", fallback: "cmd");
        Assert.False(cmdResolved.StartsWith("@"));
        Assert.NotEmpty(cmdResolved);

        // 不存在的资源回退到 fallback
        string fallbackResolved = IndirectStringResolver.Resolve("@nonexistent_lib.dll,-9999", fallback: "MyFallback");
        Assert.Equal("MyFallback", fallbackResolved);
    }

    [Fact]
    public void ConvertToToolItem_PreservesCustomTemplate()
    {
        var legacy = new LegacyMenuItem
        {
            DisplayName = "Cursor",
            ExtractedExecutable = @"D:\Software\cursor\Cursor.exe",
            InferredHost = TerminalHosts.Custom,
            CustomTemplate = @"""D:\Software\cursor\Cursor.exe"" ""%V""",
            IsDeadLink = false
        };

        var tool = MigrationService.ConvertToToolItem(legacy);
        Assert.Equal(TerminalHosts.Custom, tool.Host);
        Assert.Equal(@"""D:\Software\cursor\Cursor.exe"" ""%V""", tool.CustomTemplate);
    }
}
