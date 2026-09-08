namespace CliManager.Core.Migration;

/// <summary>
/// 存量扫描到的右键菜单项模型。
/// </summary>
public sealed class LegacyMenuItem
{
    public string KeyName { get; set; } = string.Empty;

    public string RegistryPath { get; set; } = string.Empty;

    public string Hive { get; set; } = "HKCU"; // "HKCU" | "HKLM"

    public string DisplayName { get; set; } = string.Empty;

    public string? Icon { get; set; }

    public string? RawCommand { get; set; }

    /// <summary>
    /// 是否为旧版 RightMenu 工具建立的结构（具有 RightMenu.SchemaVersion 等特征值）。
    /// </summary>
    public bool IsOldRightMenuSchema { get; set; }

    /// <summary>
    /// 提取到的真实可执行文件路径。
    /// </summary>
    public string? ExtractedExecutable { get; set; }

    /// <summary>
    /// 提取到的参数列表。
    /// </summary>
    public List<string> ExtractedArgs { get; set; } = [];

    /// <summary>
    /// 推荐/推导出的宿主类型。
    /// </summary>
    public string InferredHost { get; set; } = "wt";

    /// <summary>
    /// 是否为死链（指向的文件在本地磁盘上不存在）。
    /// </summary>
    public bool IsDeadLink { get; set; }

    /// <summary>
    /// 是否可以在当前非特权上下文直接删除（HKCU 为 true，HKLM 为 false）。
    /// </summary>
    public bool CanDeleteDirectly => Hive.Equals("HKCU", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 用户是否选中待迁移接管。
    /// </summary>
    public bool IsSelected { get; set; } = true;
}
