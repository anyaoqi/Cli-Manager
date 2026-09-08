using System.Text;
using Microsoft.Win32;

namespace CliManager.Core.Registry;

/// <summary>
/// 注册表 .reg 文件导出器，用于在执行更改前自动生成备份。
/// </summary>
public static class RegExporter
{
    /// <summary>
    /// 将指定的注册表项及其全部子树导出为 Windows 注册表文件格式 (Version 5.00)。
    /// </summary>
    public static string ExportSubKeyTree(RegistryKey rootKey, string subKeyPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Windows Registry Editor Version 5.00");
        sb.AppendLine();

        using var key = rootKey.OpenSubKey(subKeyPath);
        if (key == null)
        {
            return sb.ToString();
        }

        string fullPath = $"{rootKey.Name}\\{subKeyPath}";
        ExportRecursive(key, fullPath, sb);
        return sb.ToString();
    }

    private static void ExportRecursive(RegistryKey key, string currentFullPath, StringBuilder sb)
    {
        sb.AppendLine($"[{currentFullPath}]");

        // 导出默认值
        object? defaultValue = key.GetValue("");
        if (defaultValue != null)
        {
            sb.AppendLine($"@={FormatValue(defaultValue, key.GetValueKind(""))}");
        }

        // 导出所有非默认值
        foreach (string valName in key.GetValueNames())
        {
            if (string.IsNullOrEmpty(valName))
            {
                continue;
            }

            object? val = key.GetValue(valName);
            if (val != null)
            {
                var kind = key.GetValueKind(valName);
                sb.AppendLine($"\"{EscapeString(valName)}\"={FormatValue(val, kind)}");
            }
        }

        sb.AppendLine();

        // 递归子键
        foreach (string childName in key.GetSubKeyNames())
        {
            using var childKey = key.OpenSubKey(childName);
            if (childKey != null)
            {
                ExportRecursive(childKey, $"{currentFullPath}\\{childName}", sb);
            }
        }
    }

    private static string FormatValue(object val, RegistryValueKind kind)
    {
        return kind switch
        {
            RegistryValueKind.DWord => $"dword:{(int)val:x8}",
            RegistryValueKind.String or RegistryValueKind.ExpandString => $"\"{EscapeString(val.ToString() ?? "")}\"",
            _ => $"\"{EscapeString(val.ToString() ?? "")}\""
        };
    }

    private static string EscapeString(string str)
    {
        return str
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "")
            .Replace("\n", "\\n");
    }
}
