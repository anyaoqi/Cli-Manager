using CliManager.Core.Launch;
using CliManager.Core.Models;

namespace CliManager.Tests;

public class LaunchCompilerTests
{
    [Fact]
    public void Compile_ReturnsError_WhenExecutableIsEmpty()
    {
        var tool = new ToolItem { Executable = "" };
        var result = LaunchCompiler.Compile(tool);

        Assert.False(result.Success);
        Assert.Contains(result.Validation.Errors, e => e.Contains("可执行文件路径不能为空"));
    }

    [Fact]
    public void Compile_ReturnsError_WhenExecutableContainsDoubleQuotes()
    {
        var tool = new ToolItem { Executable = @"C:\tools\app""bad.exe" };
        var result = LaunchCompiler.Compile(tool);

        Assert.False(result.Success);
        Assert.Contains(result.Validation.Errors, e => e.Contains("不能包含双引号"));
    }

    [Fact]
    public void Compile_WindowsTerminal_WithKeepOpenAndEnv()
    {
        var tool = new ToolItem
        {
            Name = "Claude Code",
            Executable = @"C:\Users\admin\.local\bin\claude.exe",
            Args = ["--dangerously-skip-permissions", "--mcp", "a;b"],
            Host = TerminalHosts.WindowsTerminal,
            KeepOpen = true,
            Env = new Dictionary<string, string> { ["HTTP_PROXY"] = "http://127.0.0.1:7899" }
        };

        var result = LaunchCompiler.Compile(tool, @"C:\Users\admin\AppData\Local\Microsoft\WindowsApps\wt.exe");

        Assert.True(result.Success);
        Assert.NotNull(result.Command);

        // 必须使用指定 wt 路径
        Assert.StartsWith("\"C:\\Users\\admin\\AppData\\Local\\Microsoft\\WindowsApps\\wt.exe\" -d \"%V\" cmd.exe /d /k ", result.Command);
        // 包含环境变量
        Assert.Contains("set \"HTTP_PROXY=http://127.0.0.1:7899\" && ", result.Command);
        // 分号被转义为 \;
        Assert.Contains("a\\;b", result.Command);
    }

    [Fact]
    public void Compile_WindowsTerminal_WithoutKeepOpen_UsesSlashC()
    {
        var tool = new ToolItem
        {
            Executable = @"C:\tools\opencode.exe",
            Host = TerminalHosts.WindowsTerminal,
            KeepOpen = false
        };

        var result = LaunchCompiler.Compile(tool);

        Assert.True(result.Success);
        Assert.Contains("cmd.exe /d /c ", result.Command);
    }

    [Fact]
    public void Compile_Cmd_WithPushdAndEnv()
    {
        var tool = new ToolItem
        {
            Executable = @"D:\Software\nodejs\codex.cmd",
            Args = ["start", "my project"],
            Host = TerminalHosts.Cmd,
            KeepOpen = false,
            Env = new Dictionary<string, string> { ["NODE_ENV"] = "production" }
        };

        var result = LaunchCompiler.Compile(tool);

        Assert.True(result.Success);
        Assert.NotNull(result.Command);

        // 验证 pushd 定位与 /c
        Assert.StartsWith("cmd.exe /d /c pushd \"%V\"", result.Command);
        Assert.Contains(" && set \"NODE_ENV=production\"", result.Command);
        Assert.Contains("\"D:\\Software\\nodejs\\codex.cmd\" start \"my project\"", result.Command);
    }

    [Fact]
    public void Compile_PowerShell_WithSingleQuotesAndLiteralPath()
    {
        var tool = new ToolItem
        {
            Executable = @"C:\tools\tool's\run.exe",
            Args = ["arg with spaces", "O'Brien"],
            Host = TerminalHosts.WindowsPowerShell,
            KeepOpen = true,
            Env = new Dictionary<string, string> { ["PROXY"] = "http://localhost" }
        };

        var result = LaunchCompiler.Compile(tool);

        Assert.True(result.Success);
        Assert.NotNull(result.Command);

        Assert.StartsWith("powershell.exe -NoExit -Command \"", result.Command);
        Assert.Contains("$env:PROXY='http://localhost'; ", result.Command);
        Assert.Contains("Set-Location -LiteralPath '%V';", result.Command);
        Assert.Contains("& 'C:\\tools\\tool''s\\run.exe'", result.Command);
        Assert.Contains("'arg with spaces'", result.Command);
        Assert.Contains("'O''Brien'", result.Command);
    }

    [Fact]
    public void Compile_PowerShell7_UsesPwsh()
    {
        var tool = new ToolItem
        {
            Executable = @"C:\tools\run.exe",
            Host = TerminalHosts.PowerShell7,
            KeepOpen = false
        };

        var result = LaunchCompiler.Compile(tool);

        Assert.True(result.Success);
        Assert.StartsWith("pwsh.exe -Command \"Set-Location -LiteralPath '%V';", result.Command);
    }

    [Fact]
    public void Compile_CustomTemplate_ReplacesPlaceholders()
    {
        var tool = new ToolItem
        {
            Executable = @"C:\tools\warp.exe",
            Args = ["--new-tab"],
            Host = TerminalHosts.Custom,
            CustomTemplate = "warp.exe --cwd %V --exec %EXE% %ARGS%"
        };

        var result = LaunchCompiler.Compile(tool);

        Assert.True(result.Success);
        Assert.Equal("warp.exe --cwd \"%V\" --exec \"C:\\tools\\warp.exe\" --new-tab", result.Command);
    }
}
