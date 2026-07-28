using FluentAssertions;
using RightMenu.Core.Registry;
using Xunit;

namespace RightMenu.Core.Tests.Registry;

/// <summary>
/// 覆盖规划 04 Phase 1 的 fixture 清单：简单 command、LegacyDisable、Extended、
/// ProgrammaticAccessOnly、AppliesTo、HideBasedOnVelocityId、DelegateExecute、
/// 子菜单、未知结构、HKCU/HKLM 同名遮蔽、受管项 schema、旧版遗留项。
/// </summary>
public class StaticShellProviderTests
{
    private static readonly ContextLocation Location = ContextLocation.DirectoryBackground;
    private static readonly string ShellPath = Location.RelativeShellPath;

    private static IReadOnlyList<ShellVerb> Scan(RawRegistryKey? hkcu, RawRegistryKey? hklm)
    {
        var registry = new FakeRegistryAccessor();
        if (hkcu is not null)
        {
            registry.SetTree(RegistryHiveSource.Hkcu, ShellPath, hkcu);
        }

        if (hklm is not null)
        {
            registry.SetTree(RegistryHiveSource.Hklm, ShellPath, hklm);
        }

        return new StaticShellProvider(registry).Scan(Location);
    }

    private static RawRegistryKey Shell(params RawRegistryKey[] verbs) => RegKey.Create("shell", null, verbs);

    [Fact]
    public void 简单command项_HKCU可编辑()
    {
        var verbs = Scan(Shell(RegKey.Verb("Claude", "在 Claude Code 中打开", @"cmd /c claude")), null);

        var verb = verbs.Should().ContainSingle().Subject;
        verb.Kind.Should().Be(ShellVerbKind.Command);
        verb.Capability.Should().Be(EditCapability.Simple);
        verb.DisplayName.Should().Be("在 Claude Code 中打开");
        verb.Command.Should().Be(@"cmd /c claude");
        verb.IsExplorerVisible.Should().BeTrue();
        verb.IsEffective.Should().BeTrue();
    }

    [Fact]
    public void 简单command项_HKLM为可迁移而非只读()
    {
        var verbs = Scan(null, Shell(RegKey.Verb("OpenCode", "在OpenCode终端中打开", @"cmd.exe /c opencode")));

        var verb = verbs.Should().ContainSingle().Subject;
        verb.Capability.Should().Be(EditCapability.Migratable);
        verb.ReadOnlyReason.Should().BeNull();
    }

    [Fact]
    public void LegacyDisable_禁用但仍列出()
    {
        var verbs = Scan(Shell(RegKey.Verb("Trae", "Trae", "trae.exe", new() { ["LegacyDisable"] = "" })), null);

        var verb = verbs.Should().ContainSingle().Subject;
        verb.IsEnabled.Should().BeFalse();
        verb.IsExplorerVisible.Should().BeFalse();
    }

    [Fact]
    public void Extended_仅Shift显示但视为可见()
    {
        var verbs = Scan(null, Shell(RegKey.Verb("cmd", "@shell32.dll,-8506", "cmd.exe", new() { ["Extended"] = "" })));

        var verb = verbs.Should().ContainSingle().Subject;
        verb.IsExtended.Should().BeTrue();
        verb.IsExplorerVisible.Should().BeTrue();
    }

    [Fact]
    public void ProgrammaticAccessOnly_隐藏()
    {
        var verbs = Scan(Shell(RegKey.Verb("Hidden", "Hidden", "x.exe", new() { ["ProgrammaticAccessOnly"] = "" })), null);

        verbs.Single().IsHidden.Should().BeTrue();
        verbs.Single().IsExplorerVisible.Should().BeFalse();
    }

    [Fact]
    public void HideBasedOnVelocityId_隐藏_本机cmd与WSL实际在用()
    {
        var verbs = Scan(null, Shell(RegKey.Verb("cmd", "@shell32.dll,-8506", "cmd.exe /s /k pushd \"%V\"",
            new() { ["HideBasedOnVelocityId"] = 6527944 })));

        verbs.Single().IsHidden.Should().BeTrue();
        verbs.Single().IsExplorerVisible.Should().BeFalse();
    }

    [Fact]
    public void AppliesTo_标记为条件项()
    {
        var verbs = Scan(Shell(RegKey.Verb("Cond", "Cond", "x.exe", new() { ["AppliesTo"] = "System.ItemName:war" })), null);

        verbs.Single().IsConditional.Should().BeTrue();
    }

    [Fact]
    public void DelegateExecute_只读()
    {
        var command = RegKey.Create("command", new()
        {
            [""] = "",
            ["DelegateExecute"] = "{11dbb47c-a525-400b-9e80-a54615a090c0}",
        });
        var verbs = Scan(null, Shell(RegKey.Create("Delegated", new() { [""] = "X" }, command)));

        var verb = verbs.Single();
        verb.Kind.Should().Be(ShellVerbKind.Delegate);
        verb.Capability.Should().Be(EditCapability.ReadOnly);
        verb.ReadOnlyReason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void 子菜单_只读()
    {
        var verbs = Scan(null, Shell(RegKey.Create("Sub", new() { ["SubCommands"] = "" })));

        var verb = verbs.Single();
        verb.Kind.Should().Be(ShellVerbKind.Submenu);
        verb.Capability.Should().Be(EditCapability.ReadOnly);
    }

    [Fact]
    public void 未知结构_只读且默认不可见()
    {
        var verbs = Scan(Shell(RegKey.Create("Odd", new() { ["Whatever"] = "1" })), null);

        var verb = verbs.Single();
        verb.Kind.Should().Be(ShellVerbKind.Unknown);
        verb.Capability.Should().Be(EditCapability.ReadOnly);
        verb.IsExplorerVisible.Should().BeFalse();
    }

    [Fact]
    public void 同名遮蔽_HKCU生效_HKLM被遮蔽()
    {
        var verbs = Scan(
            Shell(RegKey.Verb("Claude", "用户版", "user.exe")),
            Shell(RegKey.Verb("claude", "系统版", "machine.exe")));

        verbs.Should().HaveCount(2);
        var hkcu = verbs.Single(v => v.Source == RegistryHiveSource.Hkcu);
        var hklm = verbs.Single(v => v.Source == RegistryHiveSource.Hklm);
        hkcu.IsEffective.Should().BeTrue();
        hkcu.IsShadowed.Should().BeFalse();
        hklm.IsEffective.Should().BeFalse();
        hklm.IsShadowed.Should().BeTrue();
    }

    [Fact]
    public void 受管项_依据Schema元数据而非键名前缀()
    {
        var managed = RegKey.Verb("RightMenu.a1b2c3d4e5f6a7b8a1b2c3d4e5f6a7b8", "在 Codex 中打开",
            @"""C:\app\RightMenu.App.exe"" --launch DirectoryBackground RightMenu.a1b2 ""%V""",
            new() { ["RightMenu.SchemaVersion"] = 1 });
        var verbs = Scan(Shell(managed), null);

        var verb = verbs.Single();
        verb.IsManaged.Should().BeTrue();
        verb.IsLegacyManaged.Should().BeFalse();
        verb.Capability.Should().Be(EditCapability.Full);
    }

    [Fact]
    public void 旧版遗留项_RightMenu前缀但无Schema_识别为Legacy非受管()
    {
        // 本机真实存在：旧 Tauri 版创建的 RightMenu.codex（无元数据）
        var legacy = RegKey.Verb("RightMenu.codex", "在 Codex 中打开", @"wt.exe -d ""%V"" cmd /k ""D:\nodejs\codex.cmd""");
        var verbs = Scan(Shell(legacy), null);

        var verb = verbs.Single();
        verb.IsManaged.Should().BeFalse();
        verb.IsLegacyManaged.Should().BeTrue();
        verb.Capability.Should().Be(EditCapability.Simple);
    }

    [Fact]
    public void MUIVerb优先于默认值_间接字符串经解析器展开()
    {
        var verb = RegKey.Verb("X", "@shell32.dll,-8506", "x.exe");
        var registry = new FakeRegistryAccessor();
        registry.SetTree(RegistryHiveSource.Hkcu, ShellPath, Shell(verb));
        var provider = new StaticShellProvider(registry, s => s == "@shell32.dll,-8506" ? "打开命令窗口" : s);

        var scanned = provider.Scan(Location).Single();
        scanned.DisplayName.Should().Be("打开命令窗口");
        scanned.RawDisplayName.Should().Be("@shell32.dll,-8506");
    }

    [Fact]
    public void 两个hive都为空_返回空列表不抛异常()
    {
        Scan(null, null).Should().BeEmpty();
    }

    [Fact]
    public void 显示名为空_回退键名()
    {
        var verbs = Scan(Shell(RegKey.Verb("NoName", "", "x.exe")), null);

        verbs.Single().DisplayName.Should().Be("NoName");
    }

    [Fact]
    public void 助记符在显示名中被清理_原始值保留()
    {
        var verbs = Scan(Shell(RegKey.Verb("Cursor", "通过 C&ursor 打开", "cursor.exe")), null);

        var verb = verbs.Single();
        verb.DisplayName.Should().Be("通过 Cursor 打开");
        verb.RawDisplayName.Should().Be("通过 C&ursor 打开");
    }

    [Fact]
    public void 双与号转义为单个与号()
    {
        var verbs = Scan(Shell(RegKey.Verb("X", "A && B", "x.exe")), null);

        verbs.Single().DisplayName.Should().Be("A & B");
    }
}
