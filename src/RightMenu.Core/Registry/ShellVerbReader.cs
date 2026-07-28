namespace RightMenu.Core.Registry;

/// <summary>
/// 单个动词键的分类逻辑：类型、可见性、可编辑能力（规划 02 §1/§7）。
/// 纯函数，便于用 fake registry fixture 全覆盖测试。
/// </summary>
public static class ShellVerbReader
{
    // 受管项 schema 元数据（规划 02 §4）
    public const string SchemaVersionValue = "RightMenu.SchemaVersion";
    public const string ManagedKeyPrefix = "RightMenu.";

    private const string CommandSubKey = "command";
    private const string DelegateExecuteValue = "DelegateExecute";

    public static ShellVerb Read(
        ContextLocation location,
        RegistryHiveSource source,
        RawRegistryKey key,
        Func<string, string>? indirectStringResolver = null)
    {
        var commandKey = key.FindSubKey(CommandSubKey);
        var command = commandKey?.FindValue("")?.AsString() ?? "";
        var hasDelegate = commandKey?.HasValue(DelegateExecuteValue) == true;

        var kind = Classify(key, commandKey, hasDelegate);

        var rawDisplayName = key.FindValue("MUIVerb")?.AsString();
        if (string.IsNullOrEmpty(rawDisplayName))
        {
            rawDisplayName = key.FindValue("")?.AsString() ?? "";
        }

        var displayName = rawDisplayName;
        if (displayName.StartsWith('@') && indirectStringResolver is not null)
        {
            displayName = indirectStringResolver(displayName);
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = key.Name;
        }
        else
        {
            displayName = StripMnemonics(displayName);
        }

        var isEnabled = !key.HasValue("LegacyDisable");
        var isExtended = key.HasValue("Extended");
        var isConditional = key.HasValue("AppliesTo");
        var isHidden = key.HasValue("ProgrammaticAccessOnly") || key.HasValue("HideBasedOnVelocityId");

        var isManaged = key.HasValue(SchemaVersionValue);
        var isLegacyManaged = !isManaged
            && key.Name.StartsWith(ManagedKeyPrefix, StringComparison.OrdinalIgnoreCase);

        var (capability, readOnlyReason) = DetermineCapability(source, kind, isManaged, hasDelegate);

        return new ShellVerb
        {
            Location = location,
            Source = source,
            KeyName = key.Name,
            DisplayName = displayName,
            RawDisplayName = rawDisplayName,
            Command = command,
            IconSpec = key.FindValue("Icon")?.AsString() ?? "",
            Kind = kind,
            IsEnabled = isEnabled,
            IsExtended = isExtended,
            IsConditional = isConditional,
            IsHidden = isHidden,
            IsExplorerVisible = isEnabled && !isHidden && kind != ShellVerbKind.Unknown,
            IsManaged = isManaged,
            IsLegacyManaged = isLegacyManaged,
            Capability = capability,
            ReadOnlyReason = readOnlyReason,
            Raw = key,
        };
    }

    /// <summary>去掉菜单助记符："C&ursor" → "Cursor"，"A && B" → "A & B"。</summary>
    private static string StripMnemonics(string text)
    {
        if (!text.Contains('&'))
        {
            return text;
        }

        var result = new System.Text.StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '&')
            {
                result.Append(text[i]);
            }
            else if (i + 1 < text.Length && text[i + 1] == '&')
            {
                result.Append('&');
                i++;
            }
        }

        return result.ToString();
    }

    private static ShellVerbKind Classify(RawRegistryKey key, RawRegistryKey? commandKey, bool hasDelegate)
    {
        if (hasDelegate)
        {
            return ShellVerbKind.Delegate;
        }

        if (key.HasValue("SubCommands") || key.HasValue("ExtendedSubCommandsKey") || key.FindSubKey("shell") is not null)
        {
            return ShellVerbKind.Submenu;
        }

        if (commandKey is not null && !string.IsNullOrWhiteSpace(commandKey.FindValue("")?.AsString()))
        {
            return ShellVerbKind.Command;
        }

        return ShellVerbKind.Unknown;
    }

    private static (EditCapability Capability, string? Reason) DetermineCapability(
        RegistryHiveSource source,
        ShellVerbKind kind,
        bool isManaged,
        bool hasDelegate)
    {
        if (hasDelegate)
        {
            return (EditCapability.ReadOnly, "由 DelegateExecute（COM）处理，修改可能破坏其行为");
        }

        switch (kind)
        {
            case ShellVerbKind.Submenu:
                return (EditCapability.ReadOnly, "子菜单结构，首版不支持编辑");
            case ShellVerbKind.Unknown:
                return (EditCapability.ReadOnly, "无法识别的键结构，为安全起见不做写入");
        }

        // 此处 kind 一定是 Command
        if (source == RegistryHiveSource.Hkcu)
        {
            return isManaged ? (EditCapability.Full, null) : (EditCapability.Simple, null);
        }

        if (isManaged)
        {
            // 受管项不应出现在 HKLM；如实标记，不做写入
            return (EditCapability.ReadOnly, "受管项位于系统范围（HKLM），非本工具写入的正常位置");
        }

        return (EditCapability.Migratable, null);
    }
}
