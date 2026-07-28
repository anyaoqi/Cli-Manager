using Microsoft.Win32;

namespace RightMenu.Core.Registry;

/// <summary>真实注册表写入器：仅 HKCU\Software\Classes（规划 02 §7.1）。</summary>
public sealed class WindowsRegistryWriter : IRegistryWriter
{
    public void WriteTree(RegistryHiveSource hive, string relativePath, RawRegistryKey tree)
    {
        using var key = OpenHkcuRoot(hive).CreateSubKey($@"Software\Classes\{relativePath}", writable: true)
            ?? throw new InvalidOperationException($"无法创建键: {relativePath}");
        WriteKeyContent(key, tree);
    }

    public void DeleteTree(RegistryHiveSource hive, string relativePath)
    {
        OpenHkcuRoot(hive).DeleteSubKeyTree($@"Software\Classes\{relativePath}", throwOnMissingSubKey: false);
    }

    public void SetValue(RegistryHiveSource hive, string relativePath, string name, RegistryValueData value)
    {
        using var key = OpenHkcuRoot(hive).CreateSubKey($@"Software\Classes\{relativePath}", writable: true)
            ?? throw new InvalidOperationException($"无法打开键: {relativePath}");
        key.SetValue(name, value.Value ?? "", value.Kind);
    }

    public void DeleteValue(RegistryHiveSource hive, string relativePath, string name)
    {
        using var key = OpenHkcuRoot(hive).OpenSubKey($@"Software\Classes\{relativePath}", writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    private static RegistryKey OpenHkcuRoot(RegistryHiveSource hive) =>
        hive == RegistryHiveSource.Hkcu
            ? Microsoft.Win32.Registry.CurrentUser
            : throw new InvalidOperationException("写操作仅允许 HKCU；HKLM 变更必须经迁移流程（规划 02 §7.4）");

    private static void WriteKeyContent(RegistryKey key, RawRegistryKey tree)
    {
        foreach (var (name, data) in tree.Values)
        {
            key.SetValue(name, data.Value ?? "", data.Kind);
        }

        foreach (var subTree in tree.SubKeys)
        {
            using var subKey = key.CreateSubKey(subTree.Name, writable: true)
                ?? throw new InvalidOperationException($"无法创建子键: {subTree.Name}");
            WriteKeyContent(subKey, subTree);
        }
    }
}
