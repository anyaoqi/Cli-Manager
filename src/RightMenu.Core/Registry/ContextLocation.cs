namespace RightMenu.Core.Registry;

/// <summary>
/// 静态右键菜单位置。定义位置 ID、HKCU/HKLM 下的相对注册表路径与目标参数语义。
/// P0 仅启用 <see cref="DirectoryBackground"/>；其余位置按规划 02 §1 在 P1 逐个验收后启用。
/// </summary>
public sealed record ContextLocation
{
    /// <summary>稳定位置 ID，写入受管项的 RightMenu.Location 值。</summary>
    public required string Id { get; init; }

    /// <summary>相对 Software\Classes 的 shell 容器路径，如 Directory\Background\shell。</summary>
    public required string RelativeShellPath { get; init; }

    /// <summary>Explorer 传入的目标占位符（%V 或 %1）。</summary>
    public required string TargetPlaceholder { get; init; }

    /// <summary>UI 显示名。</summary>
    public required string DisplayName { get; init; }

    /// <summary>文件夹空白处（P0 唯一启用位置）。</summary>
    public static ContextLocation DirectoryBackground { get; } = new()
    {
        Id = "DirectoryBackground",
        RelativeShellPath = @"Directory\Background\shell",
        TargetPlaceholder = "%V",
        DisplayName = "文件夹空白处",
    };

    /// <summary>当前已启用的位置。P1 每验收一个 provider 再追加。</summary>
    public static IReadOnlyList<ContextLocation> Enabled { get; } = [DirectoryBackground];

    public static ContextLocation? FindById(string id) =>
        Enabled.FirstOrDefault(l => string.Equals(l.Id, id, StringComparison.Ordinal));
}
