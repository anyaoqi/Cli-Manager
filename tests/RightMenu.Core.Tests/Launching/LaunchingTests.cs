using FluentAssertions;
using RightMenu.Core.Launching;
using RightMenu.Core.Registry;
using Xunit;

namespace RightMenu.Core.Tests.Launching;

public class ManagedEntryServiceTests
{
    [Fact]
    public void 合法受管项_解析成功()
    {
        var key = RightMenu.Core.Tests.Registry.RegKey.Create("RightMenu.x", new()
        {
            ["RightMenu.SchemaVersion"] = 1,
            ["RightMenu.Executable"] = @"C:\bin\claude.exe",
            ["RightMenu.Arguments"] = new[] { "--verbose", "a b" },
            ["RightMenu.Terminal"] = "wt",
            ["RightMenu.KeepOpen"] = 0,
        });

        var (spec, error) = ManagedEntryService.ParseLaunchSpec(key);

        error.Should().BeNull();
        spec!.ExecutablePath.Should().Be(@"C:\bin\claude.exe");
        spec.Arguments.Should().Equal("--verbose", "a b");
        spec.Terminal.Should().Be(TerminalKind.WindowsTerminal);
        spec.KeepOpenAfterExit.Should().BeFalse();
    }

    [Fact]
    public void 缺少Schema_返回错误_不按新协议误解析旧遗留项()
    {
        var key = RightMenu.Core.Tests.Registry.RegKey.Verb("RightMenu.codex", "在 Codex 中打开",
            @"wt.exe -d ""%V"" cmd /k codex.cmd");

        var (spec, error) = ManagedEntryService.ParseLaunchSpec(key);

        spec.Should().BeNull();
        error.Should().Contain("schema");
    }

    [Fact]
    public void Schema版本过高_拒绝()
    {
        var key = RightMenu.Core.Tests.Registry.RegKey.Create("RightMenu.x", new()
        {
            ["RightMenu.SchemaVersion"] = 99,
            ["RightMenu.Executable"] = @"C:\bin\x.exe",
        });

        var (spec, error) = ManagedEntryService.ParseLaunchSpec(key);

        spec.Should().BeNull();
        error.Should().Contain("99");
    }

    [Fact]
    public void 缺少可执行文件_返回错误()
    {
        var key = RightMenu.Core.Tests.Registry.RegKey.Create("RightMenu.x", new()
        {
            ["RightMenu.SchemaVersion"] = 1,
        });

        var (spec, error) = ManagedEntryService.ParseLaunchSpec(key);

        spec.Should().BeNull();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void 默认值_KeepOpen为真_终端为Auto_参数为空()
    {
        var key = RightMenu.Core.Tests.Registry.RegKey.Create("RightMenu.x", new()
        {
            ["RightMenu.SchemaVersion"] = 1,
            ["RightMenu.Executable"] = @"C:\bin\x.exe",
        });

        var (spec, _) = ManagedEntryService.ParseLaunchSpec(key);

        spec!.KeepOpenAfterExit.Should().BeTrue();
        spec.Terminal.Should().Be(TerminalKind.Auto);
        spec.Arguments.Should().BeEmpty();
    }
}

public class LaunchPlannerTests
{
    [Fact]
    public void wt路径中的分号被转义()
    {
        LaunchPlanner.EscapeForWt(@"C:\RightMenuTests\semi;colon").Should().Be(@"C:\RightMenuTests\semi\;colon");
        LaunchPlanner.EscapeForWt(@"C:\normal").Should().Be(@"C:\normal");
    }

    [Fact]
    public void 有wt时Auto走WindowsTerminal_结构化参数含cwd()
    {
        var info = LaunchPlanner.BuildRunnerStart(
            @"C:\app\RightMenu.Runner.exe", "DirectoryBackground", "RightMenu.abc",
            @"C:\work dir;x", TerminalKind.Auto, @"C:\wt\wt.exe");

        info.FileName.Should().Be(@"C:\wt\wt.exe");
        info.ArgumentList.Should().ContainInOrder("-d", @"C:\work dir\;x");
        info.ArgumentList.Should().ContainInOrder("--location", "DirectoryBackground", "--entry", "RightMenu.abc");
        string.Join(" ", info.ArgumentList).Should().NotContain("cd /d", "不允许 cd 模板（规划 02 §5）");
    }

    [Fact]
    public void 无wt时Auto回退经典控制台()
    {
        var info = LaunchPlanner.BuildRunnerStart(
            @"C:\app\RightMenu.Runner.exe", "DirectoryBackground", "RightMenu.abc",
            @"C:\work", TerminalKind.Auto, wtPath: null);

        info.FileName.Should().Be(@"C:\app\RightMenu.Runner.exe");
        info.ArgumentList.Should().ContainInOrder("--cwd", @"C:\work");
    }

    [Fact]
    public void 强制Console时忽略wt()
    {
        var info = LaunchPlanner.BuildRunnerStart(
            @"C:\app\RightMenu.Runner.exe", "DirectoryBackground", "RightMenu.abc",
            @"C:\work", TerminalKind.Console, @"C:\wt\wt.exe");

        info.FileName.Should().Be(@"C:\app\RightMenu.Runner.exe");
    }
}
