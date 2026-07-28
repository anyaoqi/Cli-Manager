using Microsoft.Win32;
using RightMenu.Core.Launching;

namespace RightMenu.Core.Registry;

/// <summary>目标键在编辑期间被其他程序修改（规划 03 §5 并发冲突）。</summary>
public sealed class RegistryConflictException(string message) : InvalidOperationException(message);

/// <summary>HKLM 迁移结果（规划 02 §7.4）。</summary>
public enum MigrationOutcome
{
    Success,

    /// <summary>已复制到 HKCU 并生效（遮蔽），但 HKLM 原键删除被拒绝或失败。</summary>
    CopiedButOriginalRemains,

    /// <summary>HKCU 已存在同名键，未执行任何写入。</summary>
    TargetAlreadyExists,
}

/// <summary>
/// HKCU 目标键级 CRUD（规划 02 §7）。所有写操作：
/// 1. 先做乐观并发检查（扫描时的键状态 vs 当前状态）；
/// 2. 快照 before/after 并记录操作日志；
/// 3. 编辑只改用户明确提交的字段，未知值与子键原样保留。
/// </summary>
public sealed class ShellVerbWriter(
    IRegistryAccessor accessor,
    IRegistryWriter writer,
    RegistrySnapshotStore snapshots)
{
    private const string LegacyDisableValue = "LegacyDisable";

    /// <summary>受管项 command：稳定 App 路径 + 位置 + 键名 + 目标占位符，无 cd 模板（规划 02 §4）。</summary>
    public static string BuildManagedCommand(string appExePath, ContextLocation location, string keyName) =>
        $"\"{appExePath}\" --launch {location.Id} {keyName} \"{location.TargetPlaceholder}\"";

    /// <summary>新建受管 CLI 项；键名为稳定 GUID，不由显示名生成（规划 02 §4）。</summary>
    public string CreateManagedEntry(
        ContextLocation location,
        string displayName,
        LaunchSpec spec,
        string iconSpec,
        string appExePath)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("显示名不能为空", nameof(displayName));
        }

        var keyName = $"RightMenu.{Guid.NewGuid():N}";
        var path = $@"{location.RelativeShellPath}\{keyName}";

        if (accessor.ReadTree(RegistryHiveSource.Hkcu, path) is not null)
        {
            throw new RegistryConflictException($"目标键已存在: {keyName}");
        }

        var tree = BuildManagedTree(keyName, location, displayName, spec, iconSpec, appExePath);
        writer.WriteTree(RegistryHiveSource.Hkcu, path, tree);

        snapshots.Record(RegistryOperationType.Create, RegistryHiveSource.Hkcu, path,
            before: null, after: snapshots.Capture(RegistryHiveSource.Hkcu, path),
            description: $"新建受管项 “{displayName}”");
        return keyName;
    }

    /// <summary>编辑受管项：重写受管值与 command，保留未知值与子键。</summary>
    public void UpdateManagedEntry(
        ContextLocation location,
        string keyName,
        RawRegistryKey expected,
        string displayName,
        LaunchSpec spec,
        string iconSpec,
        string appExePath)
    {
        var path = $@"{location.RelativeShellPath}\{keyName}";
        var before = EnsureUnchanged(path, expected);

        var tree = BuildManagedTree(keyName, location, displayName, spec, iconSpec, appExePath);
        writer.WriteTree(RegistryHiveSource.Hkcu, path, tree);

        snapshots.Record(RegistryOperationType.Edit, RegistryHiveSource.Hkcu, path,
            before, snapshots.Capture(RegistryHiveSource.Hkcu, path), $"编辑受管项 “{displayName}”");
    }

    /// <summary>编辑简单项：只改用户明确提交的字段（规划 02 §7.2）。</summary>
    public void UpdateSimpleEntry(
        ContextLocation location,
        string keyName,
        RawRegistryKey expected,
        string? newDisplayName = null,
        string? newIconSpec = null,
        string? newCommand = null)
    {
        var path = $@"{location.RelativeShellPath}\{keyName}";
        var before = EnsureUnchanged(path, expected);

        if (newDisplayName is not null)
        {
            // MUIVerb 存在时改 MUIVerb，否则改默认值（不同时写两处）
            var target = expected.HasValue("MUIVerb") ? "MUIVerb" : "";
            writer.SetValue(RegistryHiveSource.Hkcu, path, target,
                new RegistryValueData(newDisplayName, RegistryValueKind.String));
        }

        if (newIconSpec is not null)
        {
            if (newIconSpec.Length == 0)
            {
                writer.DeleteValue(RegistryHiveSource.Hkcu, path, "Icon");
            }
            else
            {
                writer.SetValue(RegistryHiveSource.Hkcu, path, "Icon",
                    new RegistryValueData(newIconSpec, RegistryValueKind.String));
            }
        }

        if (newCommand is not null)
        {
            writer.SetValue(RegistryHiveSource.Hkcu, $@"{path}\command", "",
                new RegistryValueData(newCommand, RegistryValueKind.String));
        }

        snapshots.Record(RegistryOperationType.Edit, RegistryHiveSource.Hkcu, path,
            before, snapshots.Capture(RegistryHiveSource.Hkcu, path), $"编辑 “{keyName}”");
    }

    /// <summary>启停：LegacyDisable；恢复时只移除本次写入的值（规划 02 §7.2）。</summary>
    public void SetEnabled(ContextLocation location, string keyName, RawRegistryKey expected, bool enabled)
    {
        var path = $@"{location.RelativeShellPath}\{keyName}";
        var before = EnsureUnchanged(path, expected);

        if (enabled)
        {
            writer.DeleteValue(RegistryHiveSource.Hkcu, path, LegacyDisableValue);
        }
        else
        {
            writer.SetValue(RegistryHiveSource.Hkcu, path, LegacyDisableValue,
                new RegistryValueData("", RegistryValueKind.String));
        }

        snapshots.Record(enabled ? RegistryOperationType.Enable : RegistryOperationType.Disable,
            RegistryHiveSource.Hkcu, path, before, snapshots.Capture(RegistryHiveSource.Hkcu, path),
            $"{(enabled ? "启用" : "禁用")} “{keyName}”");
    }

    /// <summary>删除 HKCU 项。</summary>
    public void Delete(ContextLocation location, string keyName, RawRegistryKey expected)
    {
        var path = $@"{location.RelativeShellPath}\{keyName}";
        var before = EnsureUnchanged(path, expected);

        writer.DeleteTree(RegistryHiveSource.Hkcu, path);

        snapshots.Record(RegistryOperationType.Delete, RegistryHiveSource.Hkcu, path,
            before, after: null, $"删除 “{keyName}”");
    }

    /// <summary>
    /// 简单项/旧版遗留项转换为受管项：原键保留（键名不变），
    /// 补写 schema 元数据并重写 command；原命令保留在操作日志 before 中。
    /// </summary>
    public void ConvertToManaged(
        ContextLocation location,
        string keyName,
        RawRegistryKey expected,
        string displayName,
        LaunchSpec spec,
        string iconSpec,
        string appExePath,
        bool isLegacyUpgrade)
    {
        var path = $@"{location.RelativeShellPath}\{keyName}";
        var before = EnsureUnchanged(path, expected);

        var tree = BuildManagedTree(keyName, location, displayName, spec, iconSpec, appExePath);
        writer.WriteTree(RegistryHiveSource.Hkcu, path, tree);
        // 旧式默认值显示名与 MUIVerb 并存会混淆，统一 MUIVerb 后清掉默认值
        if (expected.HasValue(""))
        {
            writer.SetValue(RegistryHiveSource.Hkcu, path, "",
                new RegistryValueData("", RegistryValueKind.String));
        }

        snapshots.Record(
            isLegacyUpgrade ? RegistryOperationType.UpgradeLegacy : RegistryOperationType.ConvertToManaged,
            RegistryHiveSource.Hkcu, path, before, snapshots.Capture(RegistryHiveSource.Hkcu, path),
            $"{(isLegacyUpgrade ? "升级旧版项" : "转换为受管项")} “{displayName}”");
    }

    /// <summary>
    /// HKLM 简单项迁移（规划 02 §7.4）：复制到 HKCU → 验证 → 提权删除原键。
    /// elevatedDelete 由调用方提供（UAC 交互属于 App 层）；返回 false 表示拒绝或失败。
    /// </summary>
    public MigrationOutcome MigrateFromHklm(
        ContextLocation location,
        string keyName,
        RawRegistryKey expectedHklm,
        Func<bool> elevatedDelete)
    {
        var path = $@"{location.RelativeShellPath}\{keyName}";

        if (accessor.ReadTree(RegistryHiveSource.Hkcu, path) is not null)
        {
            return MigrationOutcome.TargetAlreadyExists;
        }

        var hklmCurrent = accessor.ReadTree(RegistryHiveSource.Hklm, path)
            ?? throw new RegistryConflictException($"HKLM 原键已不存在: {keyName}");
        if (!SnapshotKey.AreEquivalent(SnapshotKey.From(hklmCurrent), SnapshotKey.From(expectedHklm)))
        {
            throw new RegistryConflictException($"HKLM 键在扫描后被修改: {keyName}");
        }

        // 1. 复制到 HKCU（无需提权）
        writer.WriteTree(RegistryHiveSource.Hkcu, path, hklmCurrent);

        // 2. 验证复制结果与源一致
        var copied = snapshots.Capture(RegistryHiveSource.Hkcu, path);
        if (!SnapshotKey.AreEquivalent(copied, SnapshotKey.From(hklmCurrent)))
        {
            writer.DeleteTree(RegistryHiveSource.Hkcu, path);
            throw new InvalidOperationException($"复制验证失败，已回滚 HKCU 副本: {keyName}");
        }

        snapshots.Record(RegistryOperationType.MigrateFromHklm, RegistryHiveSource.Hkcu, path,
            before: null, after: copied, $"迁移 “{keyName}” 到当前用户（HKLM 原键快照见日志）");

        // 3. 一次显式 UAC 删除 HKLM 原键；失败时 HKCU 副本保留并生效（遮蔽），如实报告
        return elevatedDelete() ? MigrationOutcome.Success : MigrationOutcome.CopiedButOriginalRemains;
    }

    private static RawRegistryKey BuildManagedTree(
        string keyName,
        ContextLocation location,
        string displayName,
        LaunchSpec spec,
        string iconSpec,
        string appExePath)
    {
        var values = new Dictionary<string, RegistryValueData>(StringComparer.OrdinalIgnoreCase)
        {
            ["MUIVerb"] = new(displayName, RegistryValueKind.String),
            [ShellVerbReader.SchemaVersionValue] = new(ManagedEntryService.CurrentSchemaVersion, RegistryValueKind.DWord),
            [ManagedEntryService.LocationValue] = new(location.Id, RegistryValueKind.String),
            [ManagedEntryService.ExecutableValue] = new(spec.ExecutablePath, RegistryValueKind.String),
            [ManagedEntryService.ArgumentsValue] = new(spec.Arguments.ToArray(), RegistryValueKind.MultiString),
            [ManagedEntryService.TerminalValue] = new(LaunchSpec.TerminalToString(spec.Terminal), RegistryValueKind.String),
            [ManagedEntryService.KeepOpenValue] = new(spec.KeepOpenAfterExit ? 1 : 0, RegistryValueKind.DWord),
        };

        if (!string.IsNullOrWhiteSpace(iconSpec))
        {
            values["Icon"] = new(iconSpec, RegistryValueKind.String);
        }

        var command = new RawRegistryKey
        {
            Name = "command",
            Values = new Dictionary<string, RegistryValueData>(StringComparer.OrdinalIgnoreCase)
            {
                [""] = new(BuildManagedCommand(appExePath, location, keyName), RegistryValueKind.String),
            },
        };

        return new RawRegistryKey { Name = keyName, Values = values, SubKeys = [command] };
    }

    /// <summary>乐观并发检查：目标键当前状态必须与扫描时一致（规划 04 Phase 3）。</summary>
    private SnapshotKey EnsureUnchanged(string path, RawRegistryKey expected)
    {
        var current = snapshots.Capture(RegistryHiveSource.Hkcu, path)
            ?? throw new RegistryConflictException($"目标键已不存在: {path}");
        if (!SnapshotKey.AreEquivalent(current, SnapshotKey.From(expected)))
        {
            throw new RegistryConflictException($"目标键在扫描后被其他程序修改: {path}");
        }

        return current;
    }
}
