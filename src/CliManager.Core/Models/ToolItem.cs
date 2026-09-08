namespace CliManager.Core.Models;

/// <summary>
/// CLI 终端工具配置模型。
/// </summary>
public sealed class ToolItem
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
    /// 图标路径（支持 path 或 path,index）。
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// 可执行文件绝对路径或命令。
    /// </summary>
    public string Executable { get; set; } = string.Empty;

    /// <summary>
    /// 附加参数列表（列表建模，杜绝整串自由文本注入）。
    /// </summary>
    public List<string> Args { get; set; } = [];

    /// <summary>
    /// 终端宿主类型：wt | cmd | powershell | pwsh | custom。
    /// </summary>
    public string Host { get; set; } = TerminalHosts.WindowsTerminal;

    /// <summary>
    /// 自定义模板字符串（仅在 Host 为 custom 时生效）。
    /// 支持占位符：%V（目录）、%EXE%（程序路径）、%ARGS%（参数）。
    /// </summary>
    public string? CustomTemplate { get; set; }

    /// <summary>
    /// 命令退出后是否保留终端窗口（cmd/wt 对应 /k 与 /c，ps 对应 -NoExit）。
    /// </summary>
    public bool KeepOpen { get; set; } = false;

    /// <summary>
    /// 注入环境变量键值对（编译为 set "K=V" && 或 $env:K='V';）。
    /// </summary>
    public Dictionary<string, string> Env { get; set; } = [];

    /// <summary>
    /// 所属菜单文件夹 ID（null 表示一级根菜单直出）。
    /// </summary>
    public string? ParentId { get; set; }

    /// <summary>
    /// 排序权重（加权前缀计算基准）。
    /// </summary>
    public int Order { get; set; } = 10;

    /// <summary>
    /// 是否在右键菜单中启用。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 目标挂载点，v0.0.1 固定为 ["DirectoryBackground"]。
    /// </summary>
    public List<string> Targets { get; set; } = ["DirectoryBackground"];
}
