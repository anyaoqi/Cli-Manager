namespace CliManager.Core.Models;

/// <summary>
/// 全局应用设置。
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// Windows 11 菜单模式：keep（不干预） | classic（经典菜单） | win11（新版菜单）。
    /// </summary>
    public string ClassicMenuMode { get; set; } = "keep";

    /// <summary>
    /// 注册表备份保留份数。
    /// </summary>
    public int BackupKeepCount { get; set; } = 5;

    /// <summary>
    /// 用户主动删除/屏蔽的 HKLM 存量右键项列表（在 HKCU 下写入 LegacyDisable 持续屏蔽，永不复显）。
    /// </summary>
    public List<string> HiddenHklmKeys { get; set; } = [];
}
