namespace CliManager.Core.Registry;

/// <summary>
/// 注册表常量定义。
/// </summary>
public static class RegistryConstants
{
    /// <summary>
    /// 受管特征标记值名（REG_DWORD = 1）。
    /// </summary>
    public const string ManagedValueName = "CliManager.Managed";

    /// <summary>
    /// 软禁用特征值名（REG_SZ = ""）。
    /// </summary>
    public const string LegacyDisableValueName = "LegacyDisable";

    /// <summary>
    /// 级联子菜单标识（REG_SZ = ""）。
    /// </summary>
    public const string SubCommandsValueName = "SubCommands";

    /// <summary>
    /// 菜单外显文字值名（REG_SZ）。
    /// </summary>
    public const string MuiVerbValueName = "MUIVerb";

    /// <summary>
    /// 菜单图标值名（REG_SZ）。
    /// </summary>
    public const string IconValueName = "Icon";

    /// <summary>
    /// 默认文件夹空白处注册表路径（位于 HKCU）。
    /// </summary>
    public const string DefaultBackgroundShellPath = @"Software\Classes\Directory\Background\shell";

    /// <summary>
    /// Win11 经典菜单 CLSID 注册表路径（位于 HKCU）。
    /// </summary>
    public const string ClassicMenuClsidPath = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";
}
