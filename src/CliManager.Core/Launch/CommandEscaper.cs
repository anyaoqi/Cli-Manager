using System.Text;
using System.Text.RegularExpressions;

namespace CliManager.Core.Launch;

/// <summary>
/// 命令行与注册表参数转义器，严格遵循方案 §3.3 转义规范与 Phase 0 POC 实测结论。
/// </summary>
public static partial class CommandEscaper
{
    // Windows 资源管理器静态动词参数字符：%V, %W, %L, %D, %H, %I, %S, %M, %0-%9
    // explorer 会在启动前单遍扫描并将匹配项替换为目录或动词参数。
    private static readonly HashSet<char> VerbParamChars =
    [
        'v', 'w', 'l', 'd', 'h', 'i', 's', 'm',
        '0', '1', '2', '3', '4', '5', '6', '7', '8', '9'
    ];

    [GeneratedRegex(@"%([a-zA-Z0-9_]+)%", RegexOptions.Compiled)]
    private static partial Regex EnvVarPattern();

    /// <summary>
    /// 校验输入字符串中 % 字符的安全性（Phase 0 POC 实测固化规则 6/7）。
    /// </summary>
    public static void ValidatePercentSafety(string fieldName, string value, LaunchValidationResult result)
    {
        if (string.IsNullOrEmpty(value) || !value.Contains('%'))
        {
            return;
        }

        // 1. 检查是否紧跟动词参数字符（如 %W, %V, %1 等）导致被 explorer 意外拆解
        for (int i = 0; i < value.Length - 1; i++)
        {
            if (value[i] == '%')
            {
                char next = char.ToLowerInvariant(value[i + 1]);
                if (VerbParamChars.Contains(next))
                {
                    result.AddWarning(
                        $"{fieldName} 中的 \"%{value[i + 1]}\" 包含资源管理器动词参数关键字，将被 Explorer 强制替换为路径导致损坏，请避免此字符组合。");
                }
            }
        }

        // 2. 检查成对的 %VAR% 环境变量形式
        var matches = EnvVarPattern().Matches(value);
        foreach (Match match in matches)
        {
            result.AddWarning(
                $"{fieldName} 包含 \"{match.Value}\" 环境变量引用形式。注册表静态命令中禁止直接引用 %ENV%，环境变量请在工具配置的【环境变量】面板中添加。");
        }
    }

    /// <summary>
    /// 针对 CMD 宿主转义单个参数项。
    /// </summary>
    public static string EscapeCmdArgument(string arg)
    {
        if (string.IsNullOrEmpty(arg))
        {
            return "\"\"";
        }

        // 如果参数内部包含双引号，CMD 解析极易损坏，需在录入期警示
        // 对已有引号做标准处理：将 " 转为 \"
        string processed = arg.Replace("\"", "\\\"");

        // 判断是否需要加外层双引号：包含空格、制表符、特殊符号等
        bool needsQuotes = processed.Any(c => char.IsWhiteSpace(c) || c is '&' or '|' or '<' or '>' or '^' or '(' or ')' or ';' or ',');

        if (needsQuotes)
        {
            // 如果末尾是反斜杠，在追加闭合引号前需要转义末尾反斜杠，避免 \" 导致引号被吞
            int trailingBackslashes = 0;
            for (int i = processed.Length - 1; i >= 0 && processed[i] == '\\'; i--)
            {
                trailingBackslashes++;
            }

            if (trailingBackslashes > 0)
            {
                processed += new string('\\', trailingBackslashes);
            }

            return $"\"{processed}\"";
        }

        return processed;
    }

    /// <summary>
    /// 针对 PowerShell 宿主转义单个参数项（单引号字面量模型）。
    /// </summary>
    public static string EscapePowerShellArgument(string arg)
    {
        if (string.IsNullOrEmpty(arg))
        {
            return "''";
        }

        // PowerShell 单引号字面量中，唯一需要转义的是单引号本身（' 转为 ''）
        string escaped = arg.Replace("'", "''");
        return $"'{escaped}'";
    }

    /// <summary>
    /// 针对 Windows Terminal 宿主转义分号（规则 5：分号是 wt 的命令分隔符，必须转义为 \;）。
    /// </summary>
    public static string EscapeWtSemicolons(string commandLine)
    {
        if (string.IsNullOrEmpty(commandLine) || !commandLine.Contains(';'))
        {
            return commandLine;
        }

        var sb = new StringBuilder(commandLine.Length + 8);
        for (int i = 0; i < commandLine.Length; i++)
        {
            char c = commandLine[i];
            if (c == ';')
            {
                // 如果前面已经有转义反斜杠，不重复加
                if (i > 0 && commandLine[i - 1] == '\\')
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append("\\;");
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
