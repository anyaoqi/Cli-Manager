using FluentAssertions;
using RightMenu.Core.Launching;
using RightMenu.Core.Registry;
using Xunit;

namespace RightMenu.Core.Tests.Registry;

/// <summary>
/// Phase 3 验收（规划 04）：新建→编辑→禁用→启用→删除→逐步撤销；
/// 未知值不丢失；并发冲突；HKLM 迁移三种结果；旧版遗留项升级。
/// </summary>
public sealed class ShellVerbWriterTests : IDisposable
{
    private static readonly ContextLocation Location = ContextLocation.DirectoryBackground;
    private static readonly string ShellPath = Location.RelativeShellPath;

    private readonly FakeRegistry _registry = new();
    private readonly RegistrySnapshotStore _snapshots;
    private readonly ShellVerbWriter _writer;
    private readonly string _storeDir = Path.Combine(Path.GetTempPath(), $"rightmenu-test-{Guid.NewGuid():N}");

    public ShellVerbWriterTests()
    {
        _snapshots = new RegistrySnapshotStore(_registry, _registry, _storeDir);
        _writer = new ShellVerbWriter(_registry, _registry, _snapshots);
    }

    public void Dispose()
    {
        if (Directory.Exists(_storeDir))
        {
            Directory.Delete(_storeDir, recursive: true);
        }
    }

    private static LaunchSpec Spec(string exe = @"C:\bin\claude.exe") => new()
    {
        ExecutablePath = exe,
        Arguments = ["--flag", "a b"],
        Terminal = TerminalKind.Auto,
        KeepOpenAfterExit = true,
    };

    private RawRegistryKey Current(string keyName) =>
        _registry.ReadTree(RegistryHiveSource.Hkcu, $@"{ShellPath}\{keyName}")!;

    [Fact]
    public void 新建受管项_结构符合协议()
    {
        var keyName = _writer.CreateManagedEntry(Location, "在 Claude Code 中打开", Spec(),
            @"C:\bin\claude.exe,0", @"C:\app\RightMenu.App.exe");

        keyName.Should().StartWith("RightMenu.").And.HaveLength("RightMenu.".Length + 32);

        var key = Current(keyName);
        key.FindValue("MUIVerb")!.AsString().Should().Be("在 Claude Code 中打开");
        key.FindValue(ShellVerbReader.SchemaVersionValue)!.Value.Should().Be(1);
        key.FindValue(ManagedEntryService.LocationValue)!.AsString().Should().Be("DirectoryBackground");
        key.FindValue(ManagedEntryService.ArgumentsValue)!.Value.Should().BeEquivalentTo(new[] { "--flag", "a b" });

        var command = key.FindSubKey("command")!.FindValue("")!.AsString();
        command.Should().Be($"\"C:\\app\\RightMenu.App.exe\" --launch DirectoryBackground {keyName} \"%V\"");
        command.Should().NotContain("cd /d");
    }

    [Fact]
    public void 完整生命周期_每一步可逐级撤销()
    {
        // 新建 → 编辑 → 禁用 → 启用 → 删除
        var keyName = _writer.CreateManagedEntry(Location, "名称A", Spec(), "", @"C:\app\App.exe");
        _writer.UpdateManagedEntry(Location, keyName, Current(keyName), "名称B", Spec(@"C:\bin\other.exe"), "", @"C:\app\App.exe");
        _writer.SetEnabled(Location, keyName, Current(keyName), enabled: false);
        _writer.SetEnabled(Location, keyName, Current(keyName), enabled: true);
        _writer.Delete(Location, keyName, Current(keyName));

        _registry.ReadTree(RegistryHiveSource.Hkcu, $@"{ShellPath}\{keyName}").Should().BeNull();

        // 按时间倒序逐步撤销全部 5 次操作
        var records = _snapshots.List();
        records.Should().HaveCount(5);
        foreach (var record in records)
        {
            _snapshots.Undo(record).Should().Be(UndoResult.Success, $"撤销 {record.Type} 应成功");
        }

        // 全部撤销后键不存在（回到初始状态）
        _registry.ReadTree(RegistryHiveSource.Hkcu, $@"{ShellPath}\{keyName}").Should().BeNull();
    }

    [Fact]
    public void 撤销删除_恢复完整键内容()
    {
        var keyName = _writer.CreateManagedEntry(Location, "将被删除", Spec(), "", @"C:\app\App.exe");
        var beforeDelete = SnapshotKey.From(Current(keyName));

        _writer.Delete(Location, keyName, Current(keyName));
        var deleteRecord = _snapshots.List().First(r => r.Type == RegistryOperationType.Delete);

        _snapshots.Undo(deleteRecord).Should().Be(UndoResult.Success);
        SnapshotKey.AreEquivalent(SnapshotKey.From(Current(keyName)), beforeDelete).Should().BeTrue();
    }

    [Fact]
    public void 编辑简单项_未知值与子键不丢失()
    {
        var original = RegKey.Verb("Claude", "旧名", @"cmd /c claude",
            new() { ["CustomValue"] = "keep-me", ["NoWorkingDirectory"] = "" });
        _registry.Seed(RegistryHiveSource.Hkcu, $@"{ShellPath}\Claude",
            RegKey.Create("Claude", new() { [""] = "旧名", ["CustomValue"] = "keep-me", ["NoWorkingDirectory"] = "" },
                RegKey.Create("command", new() { [""] = @"cmd /c claude" }),
                RegKey.Create("unknown-sub", new() { ["x"] = "y" })));

        _writer.UpdateSimpleEntry(Location, "Claude", Current("Claude"), newDisplayName: "新名");

        var after = Current("Claude");
        after.FindValue("")!.AsString().Should().Be("新名");
        after.FindValue("CustomValue")!.AsString().Should().Be("keep-me");
        after.HasValue("NoWorkingDirectory").Should().BeTrue();
        after.FindSubKey("unknown-sub").Should().NotBeNull();
        after.FindSubKey("command")!.FindValue("")!.AsString().Should().Be(@"cmd /c claude");
    }

    [Fact]
    public void 并发修改_写操作被拒绝()
    {
        var keyName = _writer.CreateManagedEntry(Location, "原始", Spec(), "", @"C:\app\App.exe");
        var scanned = Current(keyName);

        // 模拟其他程序在扫描后修改
        _registry.SetValue(RegistryHiveSource.Hkcu, $@"{ShellPath}\{keyName}", "Hijacked",
            new RegistryValueData("x", Microsoft.Win32.RegistryValueKind.String));

        var act = () => _writer.Delete(Location, keyName, scanned);
        act.Should().Throw<RegistryConflictException>();
    }

    [Fact]
    public void 撤销时目标被第三方修改_返回冲突不覆盖()
    {
        var keyName = _writer.CreateManagedEntry(Location, "原始", Spec(), "", @"C:\app\App.exe");
        var record = _snapshots.List().Single();

        _registry.SetValue(RegistryHiveSource.Hkcu, $@"{ShellPath}\{keyName}", "ThirdParty",
            new RegistryValueData("x", Microsoft.Win32.RegistryValueKind.String));

        _snapshots.Undo(record).Should().Be(UndoResult.Conflict);
        Current(keyName).HasValue("ThirdParty").Should().BeTrue("冲突时不得覆盖第三方修改");
    }

    [Fact]
    public void HKLM迁移_成功路径()
    {
        var hklmKey = RegKey.Verb("OpenCode", "在OpenCode终端中打开", @"cmd.exe /c opencode");
        _registry.Seed(RegistryHiveSource.Hklm, $@"{ShellPath}\OpenCode", hklmKey);

        var outcome = _writer.MigrateFromHklm(Location, "OpenCode", hklmKey, elevatedDelete: () =>
        {
            _registry.RemoveSeeded(RegistryHiveSource.Hklm, $@"{ShellPath}\OpenCode");
            return true;
        });

        outcome.Should().Be(MigrationOutcome.Success);
        Current("OpenCode").FindSubKey("command")!.FindValue("")!.AsString().Should().Be(@"cmd.exe /c opencode");
        _registry.ReadTree(RegistryHiveSource.Hklm, $@"{ShellPath}\OpenCode").Should().BeNull();
    }

    [Fact]
    public void HKLM迁移_UAC拒绝_副本保留且如实报告()
    {
        var hklmKey = RegKey.Verb("Cursor", "通过 Cursor 打开", @"""C:\cursor.exe"" ""%V""");
        _registry.Seed(RegistryHiveSource.Hklm, $@"{ShellPath}\Cursor", hklmKey);

        var outcome = _writer.MigrateFromHklm(Location, "Cursor", hklmKey, elevatedDelete: () => false);

        outcome.Should().Be(MigrationOutcome.CopiedButOriginalRemains);
        Current("Cursor").Should().NotBeNull("HKCU 副本保留并生效（遮蔽）");
        _registry.ReadTree(RegistryHiveSource.Hklm, $@"{ShellPath}\Cursor").Should().NotBeNull("原键仍在");
    }

    [Fact]
    public void HKLM迁移_HKCU已有同名键_不执行写入()
    {
        var hklmKey = RegKey.Verb("Claude", "系统版", @"x.exe");
        _registry.Seed(RegistryHiveSource.Hklm, $@"{ShellPath}\Claude", hklmKey);
        _registry.Seed(RegistryHiveSource.Hkcu, $@"{ShellPath}\Claude", RegKey.Verb("Claude", "用户版", @"y.exe"));

        var outcome = _writer.MigrateFromHklm(Location, "Claude", hklmKey, elevatedDelete: () => true);

        outcome.Should().Be(MigrationOutcome.TargetAlreadyExists);
        Current("Claude").FindValue("")!.AsString().Should().Be("用户版");
    }

    [Fact]
    public void 旧版遗留项升级_原命令保留在操作日志中()
    {
        var legacyCommand = @"wt.exe -d ""%V"" cmd /k ""D:\nodejs\codex.cmd""";
        _registry.Seed(RegistryHiveSource.Hkcu, $@"{ShellPath}\RightMenu.codex",
            RegKey.Verb("RightMenu.codex", "在 Codex 中打开", legacyCommand));

        _writer.ConvertToManaged(Location, "RightMenu.codex", Current("RightMenu.codex"),
            "在 Codex 中打开", Spec(@"D:\nodejs\codex.cmd"), "", @"C:\app\App.exe", isLegacyUpgrade: true);

        var after = Current("RightMenu.codex");
        after.FindValue(ShellVerbReader.SchemaVersionValue)!.Value.Should().Be(1);
        after.FindSubKey("command")!.FindValue("")!.AsString().Should().NotContain("cmd /k");

        var record = _snapshots.List().Single(r => r.Type == RegistryOperationType.UpgradeLegacy);
        var backedUpCommand = record.Before!.SubKeys.Single(k => k.Name == "command")
            .Values.Single(v => v.Name == "").Text;
        backedUpCommand.Should().Be(legacyCommand);
    }

    [Fact]
    public void 操作日志只保留50条()
    {
        for (var i = 0; i < 55; i++)
        {
            _snapshots.Record(RegistryOperationType.Edit, RegistryHiveSource.Hkcu, $@"{ShellPath}\x{i}",
                null, null, $"op {i}");
        }

        _snapshots.List().Should().HaveCount(RegistrySnapshotStore.MaxRecords);
    }
}
