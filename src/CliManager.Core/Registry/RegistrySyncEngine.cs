using System.Runtime.InteropServices;
using CliManager.Core.Launch;
using CliManager.Core.Models;
using Microsoft.Win32;

namespace CliManager.Core.Registry;

/// <summary>
/// 注册表同步引擎：负责将 CliConfig 原子计算 Diff 并同步至 Windows 注册表。
/// 严格满足 ADR D1（直写架构）、D3（免提权 HKCU）与加权排序机制。
/// </summary>
public sealed class RegistrySyncEngine
{
    private const int ShcneAssocChanged = 0x08000000;
    private const uint ShcnfIdList = 0x0000;

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, nint dwItem1, nint dwItem2);

    private readonly RegistryKey _rootKey;
    private readonly string _basePath;

    public RegistrySyncEngine(RegistryKey? rootKey = null, string? basePath = null)
    {
        _rootKey = rootKey ?? Microsoft.Win32.Registry.CurrentUser;
        _basePath = basePath ?? RegistryConstants.DefaultBackgroundShellPath;
    }

    /// <summary>
    /// 执行配置同步，将配置持久化至注册表。
    /// </summary>
    public RegistrySyncResult Sync(CliConfig config, string? resolvedWtPath = null, string? backupDir = null)
    {
        var result = new RegistrySyncResult();

        try
        {
            // 1. 同步前备份
            if (!string.IsNullOrWhiteSpace(backupDir))
            {
                result.BackupFilePath = CreateBackup(backupDir, config.Settings.BackupKeepCount);
            }

            // 2. 打开或创建根键
            using var baseKey = _rootKey.CreateSubKey(_basePath, writable: true);
            if (baseKey == null)
            {
                result.Success = false;
                result.Errors.Add($"无法打开或创建注册表根路径: {_basePath}");
                return result;
            }

            // 3. 收集现存的受管顶级项
            var existingManagedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string subName in baseKey.GetSubKeyNames())
            {
                using var sub = baseKey.OpenSubKey(subName);
                if (sub != null && IsManagedKey(sub))
                {
                    existingManagedKeys.Add(subName);
                }
            }

            // 4. 计算顶级目标项（合并顶层工具与文件夹）
            var topLevelItems = new List<(int Order, bool IsFolder, FolderItem? Folder, ToolItem? Tool)>();

            foreach (var folder in config.Folders)
            {
                topLevelItems.Add((folder.Order, true, folder, null));
            }

            foreach (var tool in config.Tools.Where(t => string.IsNullOrEmpty(t.ParentId)))
            {
                topLevelItems.Add((tool.Order, false, null, tool));
            }

            // 按 Order 排序并规范化序列 10, 20, 30...
            topLevelItems.Sort((a, b) => a.Order.CompareTo(b.Order));
            int width = Math.Max(3, topLevelItems.Count.ToString().Length + 1);

            var desiredManagedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < topLevelItems.Count; i++)
            {
                int normalizedOrder = (i + 1) * 10;
                var item = topLevelItems[i];

                if (item.IsFolder && item.Folder != null)
                {
                    string folderKeyName = KeyNameHelper.GenerateKeyName(normalizedOrder, item.Folder.Id, item.Folder.Name, width);
                    desiredManagedKeys.Add(folderKeyName);

                    SyncFolder(baseKey, folderKeyName, item.Folder, config.Tools, resolvedWtPath, result);
                }
                else if (!item.IsFolder && item.Tool != null)
                {
                    string toolKeyName = KeyNameHelper.GenerateKeyName(normalizedOrder, item.Tool.Id, item.Tool.Name, width);
                    desiredManagedKeys.Add(toolKeyName);

                    SyncToolKey(baseKey, toolKeyName, item.Tool, resolvedWtPath, result);
                }
            }

            // 5. 清理不再需要的旧受管项
            foreach (string oldKey in existingManagedKeys)
            {
                if (!desiredManagedKeys.Contains(oldKey))
                {
                    baseKey.DeleteSubKeyTree(oldKey, throwOnMissingSubKey: false);
                    result.DeletedCount++;
                }
            }

            // 6. 同步免提权 HKLM 影子屏蔽（若当前受管工具接管了 HKLM 对应项，写入 HKCU 影子软禁用屏蔽原 HKLM 项）
            SyncHklmShadowOverrides(baseKey, config);

            // 7. 通知 Explorer 刷新
            NotifyShell();
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"同步注册表失败: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// 一键清除所有受管右键项（绝对不影响系统原有项和其他软件项）。
    /// </summary>
    public int RemoveAllManaged()
    {
        int deleted = 0;
        using var baseKey = _rootKey.OpenSubKey(_basePath, writable: true);
        if (baseKey == null)
        {
            return 0;
        }

        foreach (string subName in baseKey.GetSubKeyNames())
        {
            using var sub = baseKey.OpenSubKey(subName);
            if (sub != null && (IsManagedKey(sub) || IsShadowOverrideKey(sub)))
            {
                baseKey.DeleteSubKeyTree(subName, throwOnMissingSubKey: false);
                deleted++;
            }
        }

        NotifyShell();
        return deleted;
    }

    private void SyncFolder(
        RegistryKey parentKey,
        string folderKeyName,
        FolderItem folder,
        List<ToolItem> allTools,
        string? resolvedWtPath,
        RegistrySyncResult result)
    {
        using var folderKey = parentKey.CreateSubKey(folderKeyName, writable: true);

        // 设置父级级联属性与显示文本（同时写入 MUIVerb 与 Default 键以保证全版本 Explorer 兼容）
        folderKey.SetValue(RegistryConstants.MuiVerbValueName, folder.Name, RegistryValueKind.String);
        folderKey.SetValue("", folder.Name, RegistryValueKind.String);
        string hkcrPath = GetHkcrRelativePath(_basePath, folderKeyName);
        folderKey.SetValue(RegistryConstants.ExtendedSubCommandsKeyValueName, hkcrPath, RegistryValueKind.String);
        folderKey.DeleteValue(RegistryConstants.SubCommandsValueName, throwOnMissingValue: false);
        folderKey.SetValue(RegistryConstants.ManagedValueName, 1, RegistryValueKind.DWord);

        if (!string.IsNullOrWhiteSpace(folder.Icon))
        {
            folderKey.SetValue(RegistryConstants.IconValueName, folder.Icon, RegistryValueKind.String);
        }
        else
        {
            folderKey.DeleteValue(RegistryConstants.IconValueName, throwOnMissingValue: false);
        }

        // 启用/禁用
        if (!folder.Enabled)
        {
            folderKey.SetValue(RegistryConstants.LegacyDisableValueName, "", RegistryValueKind.String);
        }
        else
        {
            folderKey.DeleteValue(RegistryConstants.LegacyDisableValueName, throwOnMissingValue: false);
        }

        // 同步子级 shell 节点
        using var shellKey = folderKey.CreateSubKey("shell", writable: true);
        var children = allTools.Where(t => t.ParentId == folder.Id).ToList();
        children.Sort((a, b) => a.Order.CompareTo(b.Order));

        int childWidth = Math.Max(3, children.Count.ToString().Length + 1);
        var desiredChildKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 现存子项
        var existingChildKeys = new HashSet<string>(shellKey.GetSubKeyNames(), StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < children.Count; i++)
        {
            int childOrder = (i + 1) * 10;
            var child = children[i];
            string childKeyName = KeyNameHelper.GenerateKeyName(childOrder, child.Id, child.Name, childWidth);
            desiredChildKeys.Add(childKeyName);

            SyncToolKey(shellKey, childKeyName, child, resolvedWtPath, result);
        }

        // 清理不再存在的旧子项
        foreach (string oldChild in existingChildKeys)
        {
            if (!desiredChildKeys.Contains(oldChild))
            {
                shellKey.DeleteSubKeyTree(oldChild, throwOnMissingSubKey: false);
                result.DeletedCount++;
            }
        }

        result.AddedOrUpdatedCount++;
    }

    private void SyncToolKey(
        RegistryKey parentKey,
        string toolKeyName,
        ToolItem tool,
        string? resolvedWtPath,
        RegistrySyncResult result)
    {
        var compileResult = LaunchCompiler.Compile(tool, resolvedWtPath);
        if (!compileResult.Success || string.IsNullOrEmpty(compileResult.Command))
        {
            result.Warnings.Add($"工具 [{tool.Name}] 命令编译不完全，跳过写入注册表。");
            return;
        }

        using var toolKey = parentKey.CreateSubKey(toolKeyName, writable: true);
        toolKey.SetValue(RegistryConstants.MuiVerbValueName, tool.Name, RegistryValueKind.String);
        toolKey.SetValue("", tool.Name, RegistryValueKind.String);
        toolKey.SetValue(RegistryConstants.ManagedValueName, 1, RegistryValueKind.DWord);

        if (!string.IsNullOrWhiteSpace(tool.Icon))
        {
            toolKey.SetValue(RegistryConstants.IconValueName, tool.Icon, RegistryValueKind.String);
        }
        else
        {
            toolKey.DeleteValue(RegistryConstants.IconValueName, throwOnMissingValue: false);
        }

        // 软禁用
        if (!tool.Enabled)
        {
            toolKey.SetValue(RegistryConstants.LegacyDisableValueName, "", RegistryValueKind.String);
        }
        else
        {
            toolKey.DeleteValue(RegistryConstants.LegacyDisableValueName, throwOnMissingValue: false);
        }

        // 写入 command 子键
        using var cmdKey = toolKey.CreateSubKey("command", writable: true);
        cmdKey.SetValue("", compileResult.Command, RegistryValueKind.String);

        result.AddedOrUpdatedCount++;
    }

    private static bool IsManagedKey(RegistryKey key)
    {
        object? val = key.GetValue(RegistryConstants.ManagedValueName);
        if (val is int intVal && intVal == 1)
        {
            return true;
        }
        return false;
    }

    private static bool IsShadowOverrideKey(RegistryKey key)
    {
        object? val = key.GetValue(RegistryConstants.ShadowOverrideValueName);
        return val is int intVal && intVal == 1;
    }

    private void SyncHklmShadowOverrides(RegistryKey baseKey, CliConfig config)
    {
        try
        {
            var hiddenKeys = new HashSet<string>(config.Settings.HiddenHklmKeys, StringComparer.OrdinalIgnoreCase);

            using var hklmShell = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(_basePath);
            if (hklmShell != null)
            {
                var managedExecutables = new HashSet<string>(
                    config.Tools
                        .Where(t => t.Enabled && !string.IsNullOrWhiteSpace(t.Executable))
                        .Select(t => Path.GetFileName(t.Executable)),
                    StringComparer.OrdinalIgnoreCase);

                foreach (string hklmSubName in hklmShell.GetSubKeyNames())
                {
                    using var hklmSub = hklmShell.OpenSubKey(hklmSubName);
                    if (hklmSub == null)
                    {
                        continue;
                    }

                    // 1. 显式隐藏列表中的项强制屏蔽
                    bool shouldShadow = hiddenKeys.Contains(hklmSubName);

                    // 2. 检查是否有受管工具记录了 OriginalHklmKey
                    if (!shouldShadow)
                    {
                        if (config.Tools.Any(t => t.Enabled && string.Equals(t.OriginalHklmKey, hklmSubName, StringComparison.OrdinalIgnoreCase)))
                        {
                            shouldShadow = true;
                        }
                    }

                    // 3. 检查 HKLM 项的 command 中是否包含某个受管工具的 exe
                    if (!shouldShadow)
                    {
                        string? hklmCmd = null;
                        using (var cmdKey = hklmSub.OpenSubKey("command"))
                        {
                            hklmCmd = cmdKey?.GetValue("") as string;
                        }

                        if (!string.IsNullOrWhiteSpace(hklmCmd))
                        {
                            foreach (var exe in managedExecutables)
                            {
                                if (hklmCmd.Contains(exe, StringComparison.OrdinalIgnoreCase))
                                {
                                    shouldShadow = true;
                                    break;
                                }
                            }
                        }
                    }

                    // 4. 也支持按名称匹配
                    if (!shouldShadow)
                    {
                        string? mui = hklmSub.GetValue(RegistryConstants.MuiVerbValueName) as string;
                        string? def = hklmSub.GetValue("") as string;
                        string hklmName = IndirectStringResolver.Resolve(!string.IsNullOrWhiteSpace(mui) ? mui : def, hklmSubName);

                        if (config.Tools.Any(t => t.Enabled && (t.Name.Equals(hklmName, StringComparison.OrdinalIgnoreCase) || t.Name.Equals(hklmSubName, StringComparison.OrdinalIgnoreCase))))
                        {
                            shouldShadow = true;
                        }
                    }

                    // 检查 HKCU 中是否已有现存的影子屏蔽
                    using var existingShadow = baseKey.OpenSubKey(hklmSubName);
                    if (existingShadow != null && IsShadowOverrideKey(existingShadow))
                    {
                        // 若现有影子键已被软禁用，但用户当前配置中未显式加入 HiddenHklmKeys，则自动纳入以防意外复显
                        if (existingShadow.GetValue(RegistryConstants.LegacyDisableValueName) != null &&
                            !config.Settings.HiddenHklmKeys.Contains(hklmSubName, StringComparer.OrdinalIgnoreCase))
                        {
                            config.Settings.HiddenHklmKeys.Add(hklmSubName);
                            hiddenKeys.Add(hklmSubName);
                            shouldShadow = true;
                        }
                    }

                    if (shouldShadow)
                    {
                        using var shadowKey = baseKey.CreateSubKey(hklmSubName, writable: true);
                        shadowKey.SetValue(RegistryConstants.LegacyDisableValueName, "", RegistryValueKind.String);
                        shadowKey.SetValue(RegistryConstants.ShadowOverrideValueName, 1, RegistryValueKind.DWord);
                    }
                    else
                    {
                        // 仅当用户未隐藏该项且不再受管时才清除影子键恢复原有项
                        if (existingShadow != null && IsShadowOverrideKey(existingShadow))
                        {
                            baseKey.DeleteSubKeyTree(hklmSubName, throwOnMissingSubKey: false);
                        }
                    }
                }
            }

            // 兜底确保所有 HiddenHklmKeys 均已被写入 LegacyDisable 影子屏蔽
            foreach (string hiddenKey in hiddenKeys)
            {
                using var shadowKey = baseKey.CreateSubKey(hiddenKey, writable: true);
                shadowKey.SetValue(RegistryConstants.LegacyDisableValueName, "", RegistryValueKind.String);
                shadowKey.SetValue(RegistryConstants.ShadowOverrideValueName, 1, RegistryValueKind.DWord);
            }
        }
        catch
        {
            // 忽略非特权环境下的探测异常
        }
    }

    private string? CreateBackup(string backupDir, int keepCount)
    {
        try
        {
            Directory.CreateDirectory(backupDir);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string backupPath = Path.Combine(backupDir, $"CliManager_Backup_{timestamp}.reg");

            string regContent = RegExporter.ExportSubKeyTree(_rootKey, _basePath);
            File.WriteAllText(backupPath, regContent, System.Text.Encoding.Unicode);

            // 清理旧备份
            PruneBackups(backupDir, keepCount);

            return backupPath;
        }
        catch
        {
            return null;
        }
    }

    private static void PruneBackups(string backupDir, int keepCount)
    {
        try
        {
            var files = Directory.GetFiles(backupDir, "CliManager_Backup_*.reg")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.CreationTime)
                .ToList();

            if (files.Count > keepCount)
            {
                foreach (var oldFile in files.Skip(keepCount))
                {
                    oldFile.Delete();
                }
            }
        }
        catch
        {
            // 忽略清理失败
        }
    }

    private static string GetHkcrRelativePath(string basePath, string keyName)
    {
        const string prefix = @"Software\Classes\";
        string relBase = basePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? basePath[prefix.Length..]
            : basePath;
        return string.IsNullOrEmpty(relBase) ? keyName : $"{relBase}\\{keyName}";
    }

    public static void NotifyShell()
    {
        try
        {
            SHChangeNotify(ShcneAssocChanged, ShcnfIdList, IntPtr.Zero, IntPtr.Zero);
        }
        catch
        {
            // 忽略非致命异常
        }
    }
}
