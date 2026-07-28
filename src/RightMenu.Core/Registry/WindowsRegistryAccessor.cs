using Microsoft.Win32;

namespace RightMenu.Core.Registry;

/// <summary>真实注册表访问器：递归读取 Software\Classes 下的键子树。</summary>
public sealed class WindowsRegistryAccessor : IRegistryAccessor
{
    public RawRegistryKey? ReadTree(RegistryHiveSource hive, string relativePath)
    {
        var root = hive == RegistryHiveSource.Hkcu ? Microsoft.Win32.Registry.CurrentUser : Microsoft.Win32.Registry.LocalMachine;
        using var key = root.OpenSubKey($@"Software\Classes\{relativePath}", writable: false);
        return key is null ? null : ReadKey(key, GetLeafName(relativePath));
    }

    private static string GetLeafName(string path)
    {
        var index = path.LastIndexOf('\\');
        return index < 0 ? path : path[(index + 1)..];
    }

    private static RawRegistryKey ReadKey(RegistryKey key, string name)
    {
        var values = new Dictionary<string, RegistryValueData>(StringComparer.OrdinalIgnoreCase);
        foreach (var valueName in key.GetValueNames())
        {
            // 不展开 REG_EXPAND_SZ：保留原始内容，展示与写回都不失真
            var value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            values[valueName] = new RegistryValueData(value, key.GetValueKind(valueName));
        }

        var subKeys = new List<RawRegistryKey>();
        foreach (var subKeyName in key.GetSubKeyNames())
        {
            using var subKey = key.OpenSubKey(subKeyName, writable: false);
            if (subKey is not null)
            {
                subKeys.Add(ReadKey(subKey, subKeyName));
            }
        }

        return new RawRegistryKey { Name = name, Values = values, SubKeys = subKeys };
    }
}
