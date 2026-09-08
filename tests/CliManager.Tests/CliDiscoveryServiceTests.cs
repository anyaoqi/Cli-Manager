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
}
