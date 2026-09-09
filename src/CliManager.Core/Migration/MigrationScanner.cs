using System.Text.RegularExpressions;
using CliManager.Core.Detection;
using CliManager.Core.Models;
using CliManager.Core.Registry;
using Microsoft.Win32;

namespace CliManager.Core.Migration;

/// <summary>
/// 存量右键项扫描与解析器。
/// </summary>
public static partial class MigrationScanner
{
    private const string ShellPath = RegistryConstants.DefaultBackgroundShellPath;

    [GeneratedRegex(@"[""']?([a-zA-Z]:\\[^""'|><&]+?\.(?:exe|cmd|bat|ps1))[""']?", RegexOptions.IgnoreCase)]
    private static partial Regex FilePathPattern();

    /// <summary>
    /// 扫描 HKCU 和 HKLM 下所有非受管存量项。
    /// </summary>
    public static List<LegacyMenuItem> ScanAll(RegistryKey? customHkcu = null, RegistryKey? customHklm = null)
    {
        var result = new List<LegacyMenuItem>();

        var hkcu = customHkcu ?? Microsoft.Win32.Registry.CurrentUser;
        ScanHive(hkcu, "HKCU", result);

        var hklm = customHklm ?? Microsoft.Win32.Registry.LocalMachine;
        ScanHive(hklm, "HKLM", result);

        return result;
    }

    private static void ScanHive(RegistryKey rootHive, string hiveName, List<LegacyMenuItem> result)
    {
        using var baseKey = rootHive.OpenSubKey(ShellPath);
        if (baseKey == null)
        {
            return;
        }

        foreach (string subName in baseKey.GetSubKeyNames())
        {
            using var subKey = baseKey.OpenSubKey(subName);
            if (subKey == null)
            {
                continue;
            }

            // 过滤掉本工具创建的受管项
            object? managed = subKey.GetValue(RegistryConstants.ManagedValueName);
            if (managed is int intVal && intVal == 1)
            {
                continue;
            }

            var item = ParseKey(subName, subKey, hiveName);
            if (item != null)
            {
                result.Add(item);
            }
        }
    }

    private static LegacyMenuItem ParseKey(string keyName, RegistryKey key, string hiveName)
    {
        string? muiVerb = key.GetValue(RegistryConstants.MuiVerbValueName) as string;
        string? defaultValue = key.GetValue("") as string;
        string? icon = key.GetValue(RegistryConstants.IconValueName) as string;

        string? rawCommand = null;
        using (var cmdKey = key.OpenSubKey("command"))
        {
            rawCommand = cmdKey?.GetValue("") as string;
        }

        string rawDisplayName = !string.IsNullOrWhiteSpace(muiVerb)
            ? muiVerb
            : !string.IsNullOrWhiteSpace(defaultValue)
                ? defaultValue
                : keyName;

        string displayName = IndirectStringResolver.Resolve(rawDisplayName, fallback: keyName);

        var item = new LegacyMenuItem
        {
            KeyName = keyName,
            RegistryPath = $"{hiveName}\\{ShellPath}\\{keyName}",
            Hive = hiveName,
            DisplayName = displayName,
            Icon = icon,
            RawCommand = rawCommand
        };

        // 1. 检查旧版 RightMenu 特征值
        object? oldSchemaVersion = key.GetValue("RightMenu.SchemaVersion");
        string? oldExecutable = key.GetValue("RightMenu.Executable") as string;
        if (oldSchemaVersion != null || !string.IsNullOrWhiteSpace(oldExecutable))
        {
            item.IsOldRightMenuSchema = true;
            item.ExtractedExecutable = oldExecutable;

            string? oldTerminal = key.GetValue("RightMenu.Terminal") as string;
            item.InferredHost = string.Equals(oldTerminal, "auto", StringComparison.OrdinalIgnoreCase)
                ? TerminalHosts.WindowsTerminal
                : oldTerminal ?? TerminalHosts.WindowsTerminal;

            if (key.GetValue("RightMenu.Arguments") is string[] argsArray)
            {
                item.ExtractedArgs.AddRange(argsArray);
            }
        }
        else if (!string.IsNullOrWhiteSpace(rawCommand))
        {
            // 2. 从 rawCommand 提取程序与推导宿主
            ParseRawCommand(item);
        }

        // 3. 校验死链状态
        if (!string.IsNullOrWhiteSpace(item.ExtractedExecutable))
        {
            // 如果不是绝对路径，尝试在 PATH 中定位
            if (!Path.IsPathRooted(item.ExtractedExecutable))
            {
                string? resolved = ResolveFromPath(item.ExtractedExecutable);
                if (resolved != null)
                {
                    item.ExtractedExecutable = resolved;
                    item.IsDeadLink = false;
                }
                else
                {
                    item.IsDeadLink = true;
                }
            }
            else
            {
                item.IsDeadLink = !File.Exists(item.ExtractedExecutable);
            }
        }
        else
        {
            item.IsDeadLink = true;
        }

        return item;
    }

    private static void ParseRawCommand(LegacyMenuItem item)
    {
        string cmd = item.RawCommand!;

        // 优先提取绝对路径
        var match = FilePathPattern().Match(cmd);
        string? targetExe = match.Success ? match.Groups[1].Value : null;

        // 尝试匹配已知的 AI / CLI 预设
        string matchedPresetToken = string.Empty;
        string[] tokens = cmd.Split([' ', '&', '|', '"', '\''], StringSplitOptions.RemoveEmptyEntries);
        foreach (string token in tokens.Reverse())
        {
            var preset = CliPresetRegistry.FindMatch(token);
            if (preset != null)
            {
                matchedPresetToken = token;
                break;
            }
        }

        if (!string.IsNullOrEmpty(matchedPresetToken))
        {
            item.ExtractedExecutable = matchedPresetToken;
            if (cmd.StartsWith("wt.exe", StringComparison.OrdinalIgnoreCase) || cmd.Contains("wt.exe ", StringComparison.OrdinalIgnoreCase))
            {
                item.InferredHost = TerminalHosts.WindowsTerminal;
            }
            else if (cmd.StartsWith("powershell", StringComparison.OrdinalIgnoreCase))
            {
                item.InferredHost = TerminalHosts.WindowsPowerShell;
            }
            else if (cmd.StartsWith("pwsh", StringComparison.OrdinalIgnoreCase))
            {
                item.InferredHost = TerminalHosts.PowerShell7;
            }
            else
            {
                item.InferredHost = TerminalHosts.Cmd;
            }
        }
        else if (!string.IsNullOrWhiteSpace(targetExe))
        {
            // 通用存量项（如 Cursor, Trae, Cmder, Git GUI, Git Bash, Warp 等）
            // 采用 CustomTemplate 完整保留原始命令，避免丢失复杂参数或强制包入黑窗口
            item.ExtractedExecutable = targetExe;
            item.InferredHost = TerminalHosts.Custom;
            item.CustomTemplate = cmd;
        }
        else
        {
            string firstToken = tokens.Length > 0 ? tokens[0] : string.Empty;
            if (!string.IsNullOrEmpty(firstToken))
            {
                item.ExtractedExecutable = firstToken;
                item.InferredHost = TerminalHosts.Custom;
                item.CustomTemplate = cmd;
            }
        }
    }

    private static string? ResolveFromPath(string fileName)
    {
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            return null;
        }

        string[] exts = [".exe", ".cmd", ".bat", ".ps1", ""];
        string[] dirs = pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (string dir in dirs)
        {
            foreach (string ext in exts)
            {
                string target = Path.Combine(dir, fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? fileName : fileName + ext);
                if (File.Exists(target))
                {
                    return Path.GetFullPath(target);
                }
            }
        }

        return null;
    }
}
