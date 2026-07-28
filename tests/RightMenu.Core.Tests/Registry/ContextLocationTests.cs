using FluentAssertions;
using RightMenu.Core.Registry;
using Xunit;

namespace RightMenu.Core.Tests.Registry;

public class ContextLocationTests
{
    [Fact]
    public void DirectoryBackground_定义与规划一致()
    {
        var location = ContextLocation.DirectoryBackground;

        location.Id.Should().Be("DirectoryBackground");
        location.RelativeShellPath.Should().Be(@"Directory\Background\shell");
        location.TargetPlaceholder.Should().Be("%V");
    }

    [Fact]
    public void P0_仅启用文件夹空白处()
    {
        ContextLocation.Enabled.Should().ContainSingle()
            .Which.Should().BeSameAs(ContextLocation.DirectoryBackground);
    }

    [Fact]
    public void FindById_区分大小写且未知ID返回null()
    {
        ContextLocation.FindById("DirectoryBackground").Should().BeSameAs(ContextLocation.DirectoryBackground);
        ContextLocation.FindById("directorybackground").Should().BeNull();
        ContextLocation.FindById("Unknown").Should().BeNull();
    }
}
