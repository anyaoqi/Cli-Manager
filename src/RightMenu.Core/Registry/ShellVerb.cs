namespace RightMenu.Core.Registry;

/// <summary>
/// 扫描出的单个静态 shell 动词（规划 02 §3.1）。
/// 主键为 Location + Source + KeyName；只用 KeyName 无法表达 HKCU/HKLM 同名遮蔽。
/// </summary>
public sealed record ShellVerb
{
    public required ContextLocation Location { get; init; }

    public required RegistryHiveSource Source { get; init; }

    public required string KeyName { get; init; }

    /// <summary>解析后的显示名（间接字符串已展开；空时回退 KeyName）。</summary>
    public required string DisplayName { get; init; }

    /// <summary>原始显示名（默认值或 MUIVerb，未展开）。</summary>
    public string RawDisplayName { get; init; } = "";

    /// <summary>command 子键默认值（原样保留）。</summary>
    public string Command { get; init; } = "";

    public string IconSpec { get; init; } = "";

    public ShellVerbKind Kind { get; init; }

    /// <summary>无 LegacyDisable。</summary>
    public bool IsEnabled { get; init; }

    /// <summary>Extended：仅 Shift+右键显示。</summary>
    public bool IsExtended { get; init; }

    /// <summary>存在 AppliesTo 等运行时条件，无法静态判定是否显示。</summary>
    public bool IsConditional { get; init; }

    /// <summary>ProgrammaticAccessOnly / HideBasedOnVelocityId 等隐藏标记。</summary>
    public bool IsHidden { get; init; }

    /// <summary>综合判定：会出现在资源管理器右键菜单中（Extended 视为可见）。</summary>
    public bool IsExplorerVisible { get; init; }

    /// <summary>在 HKCR 合并视图中生效（未被同名 HKCU 键遮蔽）。</summary>
    public bool IsEffective { get; init; }

    /// <summary>被同名 HKCU 键遮蔽的 HKLM 项。</summary>
    public bool IsShadowed { get; init; }

    /// <summary>受管项：依据 RightMenu.SchemaVersion 元数据值判定，非键名前缀（规划 02 §1）。</summary>
    public bool IsManaged { get; init; }

    /// <summary>旧版工具遗留项（RightMenu.* 键名但无 schema 元数据），需提供升级入口。</summary>
    public bool IsLegacyManaged { get; init; }

    public EditCapability Capability { get; init; }

    /// <summary>Capability 为 ReadOnly 时的原因说明。</summary>
    public string? ReadOnlyReason { get; init; }

    /// <summary>原始键子树，供 Inspector 展示与编辑时保留未知值/子键。</summary>
    public required RawRegistryKey Raw { get; init; }

    /// <summary>完整注册表路径（展示用）。</summary>
    public string FullKeyPath => Source switch
    {
        RegistryHiveSource.Hkcu => $@"HKEY_CURRENT_USER\Software\Classes\{Location.RelativeShellPath}\{KeyName}",
        _ => $@"HKEY_LOCAL_MACHINE\Software\Classes\{Location.RelativeShellPath}\{KeyName}",
    };
}
