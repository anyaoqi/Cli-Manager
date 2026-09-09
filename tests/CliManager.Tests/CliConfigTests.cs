using CliManager.Core.Models;

namespace CliManager.Tests;

public class CliConfigTests
{
    [Fact]
    public void FromJson_WhenNullOrEmpty_ReturnsDefaultConfig()
    {
        var config = CliConfig.FromJson("");
        Assert.NotNull(config);
        Assert.Equal(1, config.SchemaVersion);
        Assert.Equal("keep", config.Settings.ClassicMenuMode);
        Assert.Empty(config.Folders);
        Assert.Empty(config.Tools);
    }

    [Fact]
    public void RoundTrip_Serialization_PreservesAllFields()
    {
        var original = new CliConfig
        {
            SchemaVersion = 1,
            Settings = new AppSettings
            {
                ClassicMenuMode = "classic",
                BackupKeepCount = 10
            },
            Folders =
            [
                new FolderItem
                {
                    Id = "folder-1",
                    Name = "AI 编程工具",
                    Icon = @"C:\icons\ai.ico",
                    Order = 10,
                    Enabled = true
                }
            ],
            Tools =
            [
                new ToolItem
                {
                    Id = "tool-1",
                    Name = "Claude Code",
                    Icon = @"C:\tools\claude.exe,0",
                    Executable = @"C:\tools\claude.exe",
                    Args = ["--dangerously-skip-permissions", "--verbose"],
                    Host = TerminalHosts.WindowsTerminal,
                    KeepOpen = true,
                    Env = new Dictionary<string, string> { ["HTTP_PROXY"] = "http://127.0.0.1:7899" },
                    ParentId = "folder-1",
                    Order = 20,
                    Enabled = true,
                    Targets = ["DirectoryBackground"]
                }
            ]
        };

        string json = original.ToJson();
        var deserialized = CliConfig.FromJson(json);

        Assert.NotNull(deserialized);
        Assert.Equal(original.SchemaVersion, deserialized.SchemaVersion);
        Assert.Equal("classic", deserialized.Settings.ClassicMenuMode);
        Assert.Equal(10, deserialized.Settings.BackupKeepCount);

        Assert.Single(deserialized.Folders);
        var folder = deserialized.Folders[0];
        Assert.Equal("folder-1", folder.Id);
        Assert.Equal("AI 编程工具", folder.Name);
        Assert.Equal(@"C:\icons\ai.ico", folder.Icon);
        Assert.Equal(10, folder.Order);
        Assert.True(folder.Enabled);

        Assert.Single(deserialized.Tools);
        var tool = deserialized.Tools[0];
        Assert.Equal("tool-1", tool.Id);
        Assert.Equal("Claude Code", tool.Name);
        Assert.Equal(@"C:\tools\claude.exe", tool.Executable);
        Assert.Equal(2, tool.Args.Count);
        Assert.Equal("--dangerously-skip-permissions", tool.Args[0]);
        Assert.Equal(TerminalHosts.WindowsTerminal, tool.Host);
        Assert.True(tool.KeepOpen);
        Assert.Equal("http://127.0.0.1:7899", tool.Env["HTTP_PROXY"]);
        Assert.Equal("folder-1", tool.ParentId);
        Assert.Equal(20, tool.Order);
        Assert.True(tool.Enabled);
    }
}
