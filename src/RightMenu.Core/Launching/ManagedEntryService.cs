using RightMenu.Core.Registry;

namespace RightMenu.Core.Launching;

/// <summary>
/// 受管项注册表协议（规划 02 §4）的读取与校验。
/// Phase 2 只读；写入（新建/编辑）在 Phase 3 经 RegistrySnapshotStore 后实现。
/// </summary>
public sealed class ManagedEntryService(IRegistryAccessor registry)
{
    public const int CurrentSchemaVersion = 1;

    public const string ExecutableValue = "RightMenu.Executable";
    public const string ArgumentsValue = "RightMenu.Arguments";
    public const string TerminalValue = "RightMenu.Terminal";
    public const string KeepOpenValue = "RightMenu.KeepOpen";
    public const string LocationValue = "RightMenu.Location";

    /// <summary>按位置与键名读取受管项 LaunchSpec；结构不合法时返回错误说明。</summary>
    public (LaunchSpec? Spec, string? Error) ReadLaunchSpec(ContextLocation location, string entryKeyName)
    {
        var key = registry.ReadTree(RegistryHiveSource.Hkcu, $@"{location.RelativeShellPath}\{entryKeyName}");
        if (key is null)
        {
            return (null, $"受管项不存在: {entryKeyName}");
        }

        return ParseLaunchSpec(key);
    }

    /// <summary>从已读取的键子树解析 LaunchSpec。</summary>
    public static (LaunchSpec? Spec, string? Error) ParseLaunchSpec(RawRegistryKey key)
    {
        if (key.FindValue(ShellVerbReader.SchemaVersionValue)?.Value is not int schemaVersion)
        {
            return (null, "缺少 schema 版本，可能是旧版遗留项或非受管项");
        }

        if (schemaVersion > CurrentSchemaVersion)
        {
            return (null, $"schema 版本 {schemaVersion} 高于当前支持的 {CurrentSchemaVersion}");
        }

        var executable = key.FindValue(ExecutableValue)?.AsString();
        if (string.IsNullOrWhiteSpace(executable))
        {
            return (null, "缺少可执行文件路径");
        }

        var arguments = key.FindValue(ArgumentsValue)?.Value switch
        {
            string[] lines => lines,
            string single when single.Length > 0 => [single],
            _ => Array.Empty<string>(),
        };

        var keepOpen = key.FindValue(KeepOpenValue)?.Value is not 0;

        return (new LaunchSpec
        {
            ExecutablePath = executable,
            Arguments = arguments,
            Terminal = LaunchSpec.TerminalFromString(key.FindValue(TerminalValue)?.AsString()),
            KeepOpenAfterExit = keepOpen,
        }, null);
    }
}
