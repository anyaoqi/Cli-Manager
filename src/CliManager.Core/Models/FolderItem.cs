namespace CliManager.Core.Models;

/// <summary>
/// 菜单文件夹（一级级联子菜单）模型。
/// </summary>
public sealed class FolderItem
{
    /// <summary>
    /// 唯一稳定标识（GUID 格式）。
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("D");

    /// <summary>
    /// 显示名称（MUIVerb）。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 文件夹专属图标路径（支持 path 或 path,index）。
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// 排序权重（加权前缀计算基准）。
    /// </summary>
    public int Order { get; set; } = 10;

    /// <summary>
    /// 是否在右键菜单中启用。
    /// </summary>
    public bool Enabled { get; set; } = true;
}
