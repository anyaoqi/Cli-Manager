namespace RightMenu.Core.Registry;

/// <summary>
/// 实验性经典右键菜单开关（规划 02 §8）。
/// 非官方 hack，可能随 Windows 更新失效；开/关都基于精确快照，可撤销。
/// </summary>
public sealed class ClassicMenuService(
    IRegistryAccessor accessor,
    IRegistryWriter writer,
    RegistrySnapshotStore snapshots)
{
    /// <summary>相对 Software\Classes 的目标键。</summary>
    public const string InprocServerPath =
        @"CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";

    /// <summary>当前是否已启用经典菜单（键存在且默认值为空）。</summary>
    public bool IsClassicMenuEnabled()
    {
        var key = accessor.ReadTree(RegistryHiveSource.Hkcu, InprocServerPath);
        return key is not null && string.IsNullOrEmpty(key.FindValue("")?.AsString());
    }

    /// <summary>
    /// 开启：写入空默认值键；先对目标键做精确快照。
    /// 关闭：只恢复变更前的精确状态，不无条件删除整个 CLSID 键。
    /// 生效需要重启 Explorer；由 UI 层确认后执行。
    /// </summary>
    public void SetClassicMenu(bool enable)
    {
        if (enable == IsClassicMenuEnabled())
        {
            return;
        }

        var before = snapshots.Capture(RegistryHiveSource.Hkcu, InprocServerPath);

        if (enable)
        {
            writer.WriteTree(RegistryHiveSource.Hkcu, InprocServerPath, new RawRegistryKey
            {
                Name = "InprocServer32",
                Values = new Dictionary<string, RegistryValueData>
                {
                    [""] = new("", Microsoft.Win32.RegistryValueKind.String),
                },
            });
        }
        else
        {
            // 找本工具最近一次开启记录，恢复其 before；没有记录（外部开启）则删除该键
            var lastToggle = snapshots.List().FirstOrDefault(r =>
                r.Type == RegistryOperationType.ClassicMenuToggle &&
                r.RelativePath.Equals(InprocServerPath, StringComparison.OrdinalIgnoreCase) &&
                r.Before is not null);

            writer.DeleteTree(RegistryHiveSource.Hkcu, InprocServerPath);
            if (lastToggle?.Before is not null)
            {
                writer.WriteTree(RegistryHiveSource.Hkcu, InprocServerPath, lastToggle.Before.ToRaw());
            }
        }

        snapshots.Record(RegistryOperationType.ClassicMenuToggle, RegistryHiveSource.Hkcu, InprocServerPath,
            before, snapshots.Capture(RegistryHiveSource.Hkcu, InprocServerPath),
            enable ? "开启经典右键菜单（实验性）" : "关闭经典右键菜单");
    }
}
