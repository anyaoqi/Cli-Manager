using FluentAssertions;
using RightMenu.Core.Launching;
using RightMenu.Core.Registry;
using Xunit;

namespace RightMenu.Core.Tests.Registry;

/// <summary>
/// 真实注册表集成测试（规划 04 Phase 1/3）：
/// 使用随机测试 ProgID HKCU\Software\Classes\RightMenu.Test.&lt;GUID&gt;\shell，
/// 绝不写用户真实 Directory\Background\shell；finally 双重清理。
/// </summary>
public sealed class RealRegistryCrudTests : IDisposable
{
    private readonly string _testProgId = $"RightMenu.Test.{Guid.NewGuid():N}";
    private readonly ContextLocation _testLocation;
    private readonly string _storeDir = Path.Combine(Path.GetTempPath(), $"rightmenu-crud-{Guid.NewGuid():N}");
    private readonly WindowsRegistryAccessor _accessor = new();
    private readonly WindowsRegistryWriter _writer = new();
    private readonly RegistrySnapshotStore _snapshots;
    private readonly ShellVerbWriter _verbWriter;

    public RealRegistryCrudTests()
    {
        _testLocation = new ContextLocation
        {
            Id = "DirectoryBackground",
            RelativeShellPath = $@"{_testProgId}\shell",
            TargetPlaceholder = "%V",
            DisplayName = "测试位置",
        };
        _snapshots = new RegistrySnapshotStore(_accessor, _writer, _storeDir);
        _verbWriter = new ShellVerbWriter(_accessor, _writer, _snapshots);
    }

    public void Dispose()
    {
        // 双重清理：测试 ProgID 子树 + 操作日志目录
        Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(
            $@"Software\Classes\{_testProgId}", throwOnMissingSubKey: false);
        if (Directory.Exists(_storeDir))
        {
            Directory.Delete(_storeDir, recursive: true);
        }
    }

    [Fact]
    public void 真实注册表_完整生命周期与逐级撤销()
    {
        var spec = new LaunchSpec
        {
            ExecutablePath = @"C:\Windows\System32\cmd.exe",
            Arguments = ["/k", "echo a&b 50% (test)"],
            Terminal = TerminalKind.Auto,
        };

        // 新建
        var keyName = _verbWriter.CreateManagedEntry(_testLocation, "集成测试项", spec, "", @"C:\app\App.exe");
        var created = _accessor.ReadTree(RegistryHiveSource.Hkcu, $@"{_testLocation.RelativeShellPath}\{keyName}");
        created.Should().NotBeNull();
        created!.FindValue(ManagedEntryService.ArgumentsValue)!.Value
            .Should().BeEquivalentTo(new[] { "/k", "echo a&b 50% (test)" }, "REG_MULTI_SZ 参数往返不失真");

        // 编辑 → 禁用 → 启用 → 删除
        RawRegistryKey Current() => _accessor.ReadTree(RegistryHiveSource.Hkcu, $@"{_testLocation.RelativeShellPath}\{keyName}")!;
        _verbWriter.UpdateManagedEntry(_testLocation, keyName, Current(), "改名后", spec, "", @"C:\app\App.exe");
        _verbWriter.SetEnabled(_testLocation, keyName, Current(), enabled: false);
        Current().HasValue("LegacyDisable").Should().BeTrue();
        _verbWriter.SetEnabled(_testLocation, keyName, Current(), enabled: true);
        Current().HasValue("LegacyDisable").Should().BeFalse();
        _verbWriter.Delete(_testLocation, keyName, Current());
        _accessor.ReadTree(RegistryHiveSource.Hkcu, $@"{_testLocation.RelativeShellPath}\{keyName}").Should().BeNull();

        // 逐级撤销全部 5 次操作
        foreach (var record in _snapshots.List())
        {
            _snapshots.Undo(record).Should().Be(UndoResult.Success, $"撤销 {record.Type}");
        }

        _accessor.ReadTree(RegistryHiveSource.Hkcu, $@"{_testLocation.RelativeShellPath}\{keyName}").Should().BeNull();
    }

    [Fact]
    public void 真实注册表_写入HKLM被Core拒绝而不只是UI禁用()
    {
        var act = () => _writer.SetValue(RegistryHiveSource.Hklm, $@"{_testProgId}\shell\x", "",
            new RegistryValueData("x", Microsoft.Win32.RegistryValueKind.String));
        act.Should().Throw<InvalidOperationException>().WithMessage("*HKCU*");
    }
}
