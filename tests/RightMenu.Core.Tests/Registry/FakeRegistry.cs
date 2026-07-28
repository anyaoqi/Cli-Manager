using RightMenu.Core.Registry;

namespace RightMenu.Core.Tests.Registry;

/// <summary>
/// 可写内存 fake registry：同时实现读写接口，写入仅允许 HKCU（与真实 writer 行为一致）。
/// 内部以 (hive, 全路径) 平铺存储，ReadTree 时重建层级。
/// </summary>
public sealed class FakeRegistry : IRegistryAccessor, IRegistryWriter
{
    private sealed record Entry(string OriginalPath, Dictionary<string, RegistryValueData> Values);

    private readonly Dictionary<(RegistryHiveSource Hive, string Path), Entry> _keys = new();

    private static string Norm(string path) => path.ToUpperInvariant();

    /// <summary>测试准备：直接放置键（允许 HKLM）。</summary>
    public void Seed(RegistryHiveSource hive, string path, RawRegistryKey tree)
    {
        _keys[(hive, Norm(path))] = new Entry(path,
            new Dictionary<string, RegistryValueData>(tree.Values, StringComparer.OrdinalIgnoreCase));
        foreach (var sub in tree.SubKeys)
        {
            Seed(hive, $@"{path}\{sub.Name}", sub);
        }
    }

    /// <summary>测试辅助：模拟提权 helper 删除 HKLM 键。</summary>
    public void RemoveSeeded(RegistryHiveSource hive, string path)
    {
        var prefix = Norm(path);
        foreach (var key in _keys.Keys.Where(k => k.Hive == hive &&
            (k.Path == prefix || k.Path.StartsWith(prefix + @"\", StringComparison.Ordinal))).ToList())
        {
            _keys.Remove(key);
        }
    }

    public bool KeyExists(RegistryHiveSource hive, string path) => _keys.ContainsKey((hive, Norm(path)));

    public RawRegistryKey? ReadTree(RegistryHiveSource hive, string relativePath)
    {
        var prefix = Norm(relativePath);
        if (!_keys.ContainsKey((hive, prefix)) &&
            !_keys.Keys.Any(k => k.Hive == hive && k.Path.StartsWith(prefix + @"\", StringComparison.Ordinal)))
        {
            return null;
        }

        return Build(hive, relativePath, GetLeafName(relativePath));
    }

    private RawRegistryKey Build(RegistryHiveSource hive, string path, string name)
    {
        var norm = Norm(path);
        var values = _keys.TryGetValue((hive, norm), out var entry)
            ? new Dictionary<string, RegistryValueData>(entry.Values, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, RegistryValueData>(StringComparer.OrdinalIgnoreCase);

        // 从原始路径取子键名，保留真实大小写（与真实注册表一致）
        var childNames = _keys
            .Where(kv => kv.Key.Hive == hive && kv.Key.Path.StartsWith(norm + @"\", StringComparison.Ordinal))
            .Select(kv => kv.Value.OriginalPath[(path.Length + 1)..].Split('\\')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase);

        return new RawRegistryKey
        {
            Name = name,
            Values = values,
            SubKeys = [.. childNames.Select(c => Build(hive, $@"{path}\{c}", c))],
        };
    }

    public void WriteTree(RegistryHiveSource hive, string relativePath, RawRegistryKey tree)
    {
        EnsureHkcu(hive);
        var norm = Norm(relativePath);
        if (!_keys.TryGetValue((hive, norm), out var entry))
        {
            entry = new Entry(relativePath, new Dictionary<string, RegistryValueData>(StringComparer.OrdinalIgnoreCase));
            _keys[(hive, norm)] = entry;
        }

        foreach (var (name, data) in tree.Values)
        {
            entry.Values[name] = data;
        }

        foreach (var sub in tree.SubKeys)
        {
            WriteTree(hive, $@"{relativePath}\{sub.Name}", sub);
        }
    }

    public void DeleteTree(RegistryHiveSource hive, string relativePath)
    {
        EnsureHkcu(hive);
        RemoveSeeded(hive, relativePath);
    }

    public void SetValue(RegistryHiveSource hive, string relativePath, string name, RegistryValueData value)
    {
        EnsureHkcu(hive);
        var norm = Norm(relativePath);
        if (!_keys.TryGetValue((hive, norm), out var entry))
        {
            entry = new Entry(relativePath, new Dictionary<string, RegistryValueData>(StringComparer.OrdinalIgnoreCase));
            _keys[(hive, norm)] = entry;
        }

        entry.Values[name] = value;
    }

    public void DeleteValue(RegistryHiveSource hive, string relativePath, string name)
    {
        EnsureHkcu(hive);
        if (_keys.TryGetValue((hive, Norm(relativePath)), out var entry))
        {
            entry.Values.Remove(name);
        }
    }

    private static void EnsureHkcu(RegistryHiveSource hive)
    {
        if (hive != RegistryHiveSource.Hkcu)
        {
            throw new InvalidOperationException("写操作仅允许 HKCU");
        }
    }

    private static string GetLeafName(string path)
    {
        var index = path.LastIndexOf('\\');
        return index < 0 ? path : path[(index + 1)..];
    }
}
