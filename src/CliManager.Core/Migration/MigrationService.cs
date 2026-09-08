using CliManager.Core.Models;
using CliManager.Core.Registry;
using Microsoft.Win32;

namespace CliManager.Core.Migration;

/// <summary>
/// 存量迁移服务，负责将选中的存量项转换为标准 ToolItem 并清理旧注册表项。
/// </summary>
public static class MigrationService
{
    /// <summary>
    /// 将存量项转换为标准受管工具项。
    /// </summary>
    public static ToolItem ConvertToToolItem(LegacyMenuItem item, string? parentFolderId = null, int order = 10)
    {
        return new ToolItem
        {
            Id = Guid.NewGuid().ToString("D"),
            Name = item.DisplayName,
            Icon = item.Icon,
            Executable = item.ExtractedExecutable ?? string.Empty,
            Args = [.. item.ExtractedArgs],
            Host = item.InferredHost,
            ParentId = parentFolderId,
            Order = order,
            Enabled = !item.IsDeadLink
        };
    }

    /// <summary>
    /// 执行存量项接管迁移。
    /// </summary>
    public static int MigrateSelected(
        CliConfig config,
        List<LegacyMenuItem> selectedItems,
        RegistryKey? customHkcu = null)
    {
        var hkcu = customHkcu ?? Microsoft.Win32.Registry.CurrentUser;
        using var baseKey = hkcu.OpenSubKey(RegistryConstants.DefaultBackgroundShellPath, writable: true);

        int count = 0;
        int maxOrder = config.Tools.Count > 0 ? config.Tools.Max(t => t.Order) : 0;

        foreach (var item in selectedItems.Where(i => i.IsSelected))
        {
            if (string.IsNullOrWhiteSpace(item.ExtractedExecutable))
            {
                continue;
            }

            maxOrder += 10;
            var tool = ConvertToToolItem(item, parentFolderId: null, order: maxOrder);
            config.Tools.Add(tool);

            // 如果是 HKCU 项，迁移接管后删除旧键
            if (item.CanDeleteDirectly && baseKey != null)
            {
                try
                {
                    baseKey.DeleteSubKeyTree(item.KeyName, throwOnMissingSubKey: false);
                }
                catch
                {
                    // 忽略删除失败
                }
            }

            count++;
        }

        return count;
    }
}
