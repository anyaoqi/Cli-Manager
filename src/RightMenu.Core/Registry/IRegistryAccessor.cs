namespace RightMenu.Core.Registry;

/// <summary>
/// Software\Classes 访问抽象。单元测试注入内存 fake，不碰真实注册表（规划 04 Phase 1）。
/// </summary>
public interface IRegistryAccessor
{
    /// <summary>
    /// 递归读取 &lt;hive&gt;\Software\Classes\&lt;relativePath&gt; 的完整子树。
    /// 键不存在时返回 null。
    /// </summary>
    RawRegistryKey? ReadTree(RegistryHiveSource hive, string relativePath);
}
