using Microsoft.Win32;

namespace RightMenu.Core.Registry;

/// <summary>注册表值来源 hive。HKCR 是两者的合并视图，不是独立存储区（规划 02 §1）。</summary>
public enum RegistryHiveSource
{
    /// <summary>HKCU\Software\Classes：当前用户，可写区。</summary>
    Hkcu,

    /// <summary>HKLM\Software\Classes：系统范围；简单项可迁移，其余只读（规划 02 §7）。</summary>
    Hklm,
}

/// <summary>动词结构类型（规划 02 §3.1）。</summary>
public enum ShellVerbKind
{
    /// <summary>普通 command 动词。</summary>
    Command,

    /// <summary>子菜单（SubCommands / ExtendedSubCommandsKey / shell 子键）。</summary>
    Submenu,

    /// <summary>DelegateExecute（COM 处理）。</summary>
    Delegate,

    /// <summary>无法识别的结构。</summary>
    Unknown,
}

/// <summary>编辑能力（规划 02 §1/§7）。</summary>
public enum EditCapability
{
    /// <summary>受管项：完整结构化编辑。</summary>
    Full,

    /// <summary>HKCU 简单命令项：谨慎编辑/启停/删除。</summary>
    Simple,

    /// <summary>HKLM 简单项：迁移到当前用户后可管理（规划 02 §7.4）。</summary>
    Migratable,

    /// <summary>只读；原因见 ReadOnlyReason。</summary>
    ReadOnly,
}

/// <summary>单个注册表值（保留类型与原始数据，编辑时未知值不丢失）。</summary>
public sealed record RegistryValueData(object? Value, RegistryValueKind Kind)
{
    public string AsString() => Value switch
    {
        string s => s,
        string[] lines => string.Join('\n', lines),
        null => "",
        _ => Value.ToString() ?? "",
    };
}

/// <summary>递归读取的原始注册表键。扫描不丢数据（规划 02 §1）。</summary>
public sealed record RawRegistryKey
{
    public required string Name { get; init; }

    public IReadOnlyDictionary<string, RegistryValueData> Values { get; init; }
        = new Dictionary<string, RegistryValueData>();

    public IReadOnlyList<RawRegistryKey> SubKeys { get; init; } = [];

    public RawRegistryKey? FindSubKey(string name) =>
        SubKeys.FirstOrDefault(k => string.Equals(k.Name, name, StringComparison.OrdinalIgnoreCase));

    public RegistryValueData? FindValue(string name)
    {
        foreach (var (key, data) in Values)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                return data;
            }
        }

        return null;
    }

    public bool HasValue(string name) => FindValue(name) is not null;
}
