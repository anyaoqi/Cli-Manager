using System.Diagnostics;
using FluentAssertions;
using RightMenu.Core.Registry;
using Xunit;
using Xunit.Abstractions;

namespace RightMenu.Core.Tests.Registry;

/// <summary>
/// 真实注册表只读集成测试：不写任何键，仅验证扫描完整性与性能（规划 04 Phase 1 验收）。
/// </summary>
public class RealRegistryScanTests(ITestOutputHelper output)
{
    [Fact]
    public void 扫描真实DirectoryBackground_完整_分类合理_300ms内()
    {
        var provider = new StaticShellProvider(new WindowsRegistryAccessor());

        var sw = Stopwatch.StartNew();
        var verbs = provider.Scan(ContextLocation.DirectoryBackground);
        sw.Stop();

        output.WriteLine($"扫描耗时: {sw.ElapsedMilliseconds}ms, 共 {verbs.Count} 项");
        foreach (var v in verbs)
        {
            output.WriteLine(
                $"  [{v.Source}] {v.KeyName,-24} kind={v.Kind,-8} cap={v.Capability,-10} " +
                $"effective={v.IsEffective} visible={v.IsExplorerVisible}{(v.IsShadowed ? " shadowed" : "")}" +
                $"{(v.IsLegacyManaged ? " legacy" : "")}");
        }

        sw.ElapsedMilliseconds.Should().BeLessThan(300);

        // 本机至少存在系统自带的 HKLM 项（cmd、Powershell 等）
        verbs.Should().NotBeEmpty();

        // 完整性：所有项都被分类，没有黑名单丢项的途径
        verbs.Should().OnlyContain(v => v.DisplayName.Length > 0);

        // HKLM 简单 command 项应为 Migratable 而非死只读（规划 02 §7.4）
        var hklmCommands = verbs.Where(v =>
            v.Source == RegistryHiveSource.Hklm && v.Kind == ShellVerbKind.Command && !v.IsManaged);
        hklmCommands.Should().OnlyContain(v => v.Capability == EditCapability.Migratable);
    }
}
