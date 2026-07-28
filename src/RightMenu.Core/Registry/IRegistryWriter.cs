namespace RightMenu.Core.Registry;

/// <summary>
/// 注册表写抽象。真实实现只允许 HKCU（规划 02 §7.1）；
/// HKLM 原键删除由独立提权 helper 执行（规划 02 §7.4）。
/// </summary>
public interface IRegistryWriter
{
    /// <summary>确保键存在并写入 tree 中的所有值与子键（不删除未列出的既有值）。</summary>
    void WriteTree(RegistryHiveSource hive, string relativePath, RawRegistryKey tree);

    /// <summary>递归删除键；不存在时静默成功。</summary>
    void DeleteTree(RegistryHiveSource hive, string relativePath);

    void SetValue(RegistryHiveSource hive, string relativePath, string name, RegistryValueData value);

    void DeleteValue(RegistryHiveSource hive, string relativePath, string name);
}
