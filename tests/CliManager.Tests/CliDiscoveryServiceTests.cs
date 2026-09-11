using System.IO;
using CliManager.Core.Detection;

namespace CliManager.Tests;

public class CliDiscoveryServiceTests
{
    [Fact]
    public void PresetRegistry_MatchesKnownTools()
    {
        var claude = CliPresetRegistry.FindMatch(@"C:\Users\admin\.local\bin\claude.exe");
        Assert.NotNull(claude);
        Assert.Equal("claude", claude.Id);
        Assert.Equal("Claude Code", claude.DisplayName);

        var opencode = CliPresetRegistry.FindMatch(@"D:\Software\nodejs\opencode.cmd");
        Assert.NotNull(opencode);
        Assert.Equal("opencode", opencode.Id);
        Assert.Equal("OpenCode", opencode.DisplayName);

        var codex = CliPresetRegistry.FindMatch(@"codex");
        Assert.NotNull(codex);
        Assert.Equal("codex", codex.Id);

        var unknown = CliPresetRegistry.FindMatch(@"notepad.exe");
        Assert.Null(unknown);
    }

    [Fact]
    public void DetectWindowsTerminal_ReturnsValidPathOrNull()
    {
        string? wtPath = CliDiscoveryService.DetectWindowsTerminalPath();
        // 在安装了 Windows Terminal 的系统上应当能探测到
        if (wtPath != null)
        {
            Assert.Contains("wt.exe", wtPath, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void DetectNodeInstallDirectory_ReturnsNullOrExistingDirectory()
    {
        string? nodeDir = CliDiscoveryService.DetectNodeInstallDirectory();
        // 未安装 Node 时返回 null；已安装（PATH 或注册表）时必须返回真实存在的目录
        if (nodeDir != null)
        {
            Assert.True(Directory.Exists(nodeDir));
        }
    }

    [Fact]
    public void DiscoverInstalledTools_IncludesAllToolsFromNodeDirectory()
    {
        // 在临时目录模拟 Node.js 安装目录：.cmd 工具（无论是否 AI CLI）都应被扫描
        string tempDir = Path.Combine(Path.GetTempPath(), $"climgr-test-node-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "zz-fake-cli.cmd"), "@echo off\r\n");
            File.WriteAllText(Path.Combine(tempDir, "zz-another-tool.bat"), "@echo off\r\n");
            File.WriteAllText(Path.Combine(tempDir, "zz-not-a-tool.txt"), "ignored");

            var tools = CliDiscoveryService.ScanDirectoryForExecutables(tempDir, "node");

            Assert.Contains(tools, t => t.ExecutablePath.EndsWith("zz-fake-cli.cmd", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(tools, t => t.ExecutablePath.EndsWith("zz-another-tool.bat", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(tools, t => t.ExecutablePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void DeduplicateDetectedTools_FiltersOutExtensionlessAndPrefersExecutable()
    {
        var raw = new List<DetectedTool>
        {
            new()
            {
                Name = "OpenCode",
                ExecutablePath = @"D:\Software\nodejs\opencode", // 无扩展名
                MatchedPresetId = "opencode",
                Source = "path"
            },
            new()
            {
                Name = "OpenCode",
                ExecutablePath = @"D:\Software\nodejs\opencode.ps1",
                MatchedPresetId = "opencode",
                Source = "path"
            },
            new()
            {
                Name = "OpenCode",
                ExecutablePath = @"D:\Software\nodejs\opencode.cmd",
                MatchedPresetId = "opencode",
                Source = "path"
            },
            new()
            {
                Name = "Codex CLI",
                ExecutablePath = @"D:\Software\nodejs\codex", // 无扩展名
                MatchedPresetId = "codex",
                Source = "path"
            },
            new()
            {
                Name = "Codex CLI",
                ExecutablePath = @"D:\Software\nodejs\codex.cmd",
                MatchedPresetId = "codex",
                Source = "path"
            }
        };

        var result = CliDiscoveryService.DeduplicateDetectedTools(raw);

        // 仅保留 2 项：opencode.cmd 与 codex.cmd
        Assert.Equal(2, result.Count);
        Assert.Contains(result, t => t.ExecutablePath.EndsWith("opencode.cmd", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result, t => t.ExecutablePath.EndsWith("codex.cmd", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result, t => t.ExecutablePath.EndsWith(@"\opencode", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result, t => t.ExecutablePath.EndsWith(@"\codex", StringComparison.OrdinalIgnoreCase));
    }
}
