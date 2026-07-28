namespace RightMenu.Core.Registry;

/// <summary>
/// 静态 shell 动词通用 provider：对一个 ContextLocation 分别读取 HKCU/HKLM，
/// 计算有效/遮蔽状态并合并（规划 02 §1）。所有静态位置复用此实现，不按位置复制代码。
/// </summary>
public sealed class StaticShellProvider(
    IRegistryAccessor registry,
    Func<string, string>? indirectStringResolver = null)
{
    /// <summary>
    /// 扫描指定位置的全部动词。完整枚举，不用黑名单丢项；
    /// HKCU 与 HKLM 同名时 HKCU 生效、HKLM 被遮蔽（HKCR 合并语义）。
    /// </summary>
    public IReadOnlyList<ShellVerb> Scan(ContextLocation location)
    {
        var hkcuTree = registry.ReadTree(RegistryHiveSource.Hkcu, location.RelativeShellPath);
        var hklmTree = registry.ReadTree(RegistryHiveSource.Hklm, location.RelativeShellPath);

        var hkcuNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ShellVerb>();

        if (hkcuTree is not null)
        {
            foreach (var key in hkcuTree.SubKeys)
            {
                hkcuNames.Add(key.Name);
                result.Add(ShellVerbReader.Read(location, RegistryHiveSource.Hkcu, key, indirectStringResolver)
                    with
                { IsEffective = true, IsShadowed = false });
            }
        }

        if (hklmTree is not null)
        {
            foreach (var key in hklmTree.SubKeys)
            {
                var shadowed = hkcuNames.Contains(key.Name);
                result.Add(ShellVerbReader.Read(location, RegistryHiveSource.Hklm, key, indirectStringResolver)
                    with
                { IsEffective = !shadowed, IsShadowed = shadowed });
            }
        }

        return result;
    }
}
