using Microsoft.Win32;
using RightMenu.Core.Registry;

namespace RightMenu.Core.Tests.Registry;

/// <summary>内存 fake registry，单元测试不碰真实注册表（规划 04 Phase 1）。</summary>
public sealed class FakeRegistryAccessor : IRegistryAccessor
{
    private readonly Dictionary<(RegistryHiveSource Hive, string Path), RawRegistryKey> _trees = new();

    public void SetTree(RegistryHiveSource hive, string relativePath, RawRegistryKey tree) =>
        _trees[(hive, relativePath.ToUpperInvariant())] = tree;

    public RawRegistryKey? ReadTree(RegistryHiveSource hive, string relativePath) =>
        _trees.GetValueOrDefault((hive, relativePath.ToUpperInvariant()));
}

/// <summary>fixture 构造辅助。</summary>
public static class RegKey
{
    public static RawRegistryKey Create(
        string name,
        Dictionary<string, object?>? values = null,
        params RawRegistryKey[] subKeys)
    {
        var typed = new Dictionary<string, RegistryValueData>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in values ?? [])
        {
            typed[key] = value switch
            {
                int i => new RegistryValueData(i, RegistryValueKind.DWord),
                string[] lines => new RegistryValueData(lines, RegistryValueKind.MultiString),
                _ => new RegistryValueData(value, RegistryValueKind.String),
            };
        }

        return new RawRegistryKey { Name = name, Values = typed, SubKeys = subKeys };
    }

    /// <summary>普通 command 动词。</summary>
    public static RawRegistryKey Verb(string name, string display, string command, Dictionary<string, object?>? extraValues = null)
    {
        var values = extraValues ?? [];
        values[""] = display;
        return Create(name, values, Create("command", new() { [""] = command }));
    }
}
