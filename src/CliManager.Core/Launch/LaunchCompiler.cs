using System.Text;
using CliManager.Core.Models;

namespace CliManager.Core.Launch;

/// <summary>
/// 启动链编译器：将 ToolItem 转换为注册表 command 字符串。
/// 严格满足 ADR D1（直写架构）与 §3.3 规范。
/// </summary>
public static class LaunchCompiler
{
    /// <summary>
    /// 编译单个工具项为注册表 command 字符串。
    /// </summary>
    /// <param name="tool">工具配置</param>
    /// <param name="resolvedWtPath">已探测到的 wt.exe 绝对路径（若为空则使用默认 wt.exe）</param>
    public static LaunchResult Compile(ToolItem tool, string? resolvedWtPath = null)
    {
        var result = new LaunchResult();

        // 1. 基础字段非空检查
        if (string.IsNullOrWhiteSpace(tool.Executable))
        {
            result.Validation.AddError("可执行文件路径不能为空。");
            return result;
        }

        // 2. 检查可执行文件路径是否包含双引号（非法路径）
        if (tool.Executable.Contains('"'))
        {
            result.Validation.AddError("可执行文件路径中不能包含双引号。");
            return result;
        }

        // 3. 校验 % 安全性
        CommandEscaper.ValidatePercentSafety("执行程序路径", tool.Executable, result.Validation);
        foreach (var arg in tool.Args)
        {
            CommandEscaper.ValidatePercentSafety("命令行参数", arg, result.Validation);
        }
        foreach (var (k, v) in tool.Env)
        {
            CommandEscaper.ValidatePercentSafety($"环境变量 {k}", v, result.Validation);
        }

        // 4. 根据 Host 分发编译
        string host = string.IsNullOrWhiteSpace(tool.Host) ? TerminalHosts.WindowsTerminal : tool.Host.Trim().ToLowerInvariant();

        try
        {
            result.Command = host switch
            {
                TerminalHosts.WindowsTerminal => CompileWindowsTerminal(tool, resolvedWtPath),
                TerminalHosts.Cmd => CompileCmd(tool),
                TerminalHosts.WindowsPowerShell => CompilePowerShell(tool, isPwsh: false),
                TerminalHosts.PowerShell7 => CompilePowerShell(tool, isPwsh: true),
                TerminalHosts.Custom => CompileCustom(tool, result.Validation),
                _ => throw new NotSupportedException($"不支持的终端宿主类型: {tool.Host}")
            };
        }
        catch (Exception ex)
        {
            result.Validation.AddError($"编译失败: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// 编译 Windows Terminal 启动命令。
    /// 模板：wt.exe -d "%V" cmd.exe /d /c "<EXE>" <ARGS>
    /// </summary>
    private static string CompileWindowsTerminal(ToolItem tool, string? resolvedWtPath)
    {
        string wtExe = string.IsNullOrWhiteSpace(resolvedWtPath) ? "wt.exe" : resolvedWtPath.Trim();
        string switchFlag = tool.KeepOpen ? "/k" : "/c";

        // 编译环境变量前缀
        var envBuilder = new StringBuilder();
        foreach (var (k, v) in tool.Env)
        {
            if (!string.IsNullOrWhiteSpace(k))
            {
                string safeKey = k.Replace("\"", "").Trim();
                string safeVal = v.Replace("\"", "\\\"");
                envBuilder.Append($"set \"{safeKey}={safeVal}\" && ");
            }
        }

        // 编译参数列表
        var argsBuilder = new StringBuilder();
        foreach (var arg in tool.Args)
        {
            if (argsBuilder.Length > 0)
            {
                argsBuilder.Append(' ');
            }
            argsBuilder.Append(CommandEscaper.EscapeCmdArgument(arg));
        }

        string exePart = $"\"{tool.Executable.Trim()}\"";
        string innerCmd = envBuilder.Length > 0 || argsBuilder.Length > 0
            ? $"{envBuilder}{exePart}{(argsBuilder.Length > 0 ? " " + argsBuilder : "")}"
            : exePart;

        // WT 的分号转义规则
        string safeInner = CommandEscaper.EscapeWtSemicolons(innerCmd);

        return $"\"{wtExe}\" -d \"%V\" cmd.exe /d {switchFlag} {safeInner}";
    }

    /// <summary>
    /// 编译经典 CMD 启动命令。
    /// 模板：cmd.exe /d /c pushd "%V" && "<EXE>" <ARGS>
    /// </summary>
    private static string CompileCmd(ToolItem tool)
    {
        string switchFlag = tool.KeepOpen ? "/k" : "/c";

        var envBuilder = new StringBuilder();
        foreach (var (k, v) in tool.Env)
        {
            if (!string.IsNullOrWhiteSpace(k))
            {
                string safeKey = k.Replace("\"", "").Trim();
                string safeVal = v.Replace("\"", "\\\"");
                envBuilder.Append($" && set \"{safeKey}={safeVal}\"");
            }
        }

        var argsBuilder = new StringBuilder();
        foreach (var arg in tool.Args)
        {
            if (argsBuilder.Length > 0)
            {
                argsBuilder.Append(' ');
            }
            argsBuilder.Append(CommandEscaper.EscapeCmdArgument(arg));
        }

        string exePart = $"\"{tool.Executable.Trim()}\"";
        string commandLine = $"{exePart}{(argsBuilder.Length > 0 ? " " + argsBuilder : "")}";

        return $"cmd.exe /d {switchFlag} pushd \"%V\"{envBuilder} && {commandLine}";
    }

    /// <summary>
    /// 编译 PowerShell 启动命令。
    /// 模板：powershell.exe -NoExit -Command "Set-Location -LiteralPath '%V'; & '<EXE>' <ARGS>"
    /// </summary>
    private static string CompilePowerShell(ToolItem tool, bool isPwsh)
    {
        string exeName = isPwsh ? "pwsh.exe" : "powershell.exe";
        string noExitFlag = tool.KeepOpen ? "-NoExit " : "";

        var envBuilder = new StringBuilder();
        foreach (var (k, v) in tool.Env)
        {
            if (!string.IsNullOrWhiteSpace(k))
            {
                string safeKey = k.Replace("'", "").Trim();
                string safeVal = v.Replace("'", "''");
                envBuilder.Append($"$env:{safeKey}='{safeVal}'; ");
            }
        }

        var argsBuilder = new StringBuilder();
        foreach (var arg in tool.Args)
        {
            if (argsBuilder.Length > 0)
            {
                argsBuilder.Append(' ');
            }
            argsBuilder.Append(CommandEscaper.EscapePowerShellArgument(arg));
        }

        string escapedExe = tool.Executable.Trim().Replace("'", "''");
        string callPart = $"& '{escapedExe}'{(argsBuilder.Length > 0 ? " " + argsBuilder : "")}";

        return $"{exeName} {noExitFlag}-Command \"{envBuilder}Set-Location -LiteralPath '%V'; {callPart}\"";
    }

    /// <summary>
    /// 编译自定义模板命令。
    /// 支持占位符：%V（目录）、%EXE%（程序路径）、%ARGS%（参数）。
    /// </summary>
    private static string CompileCustom(ToolItem tool, LaunchValidationResult validation)
    {
        if (string.IsNullOrWhiteSpace(tool.CustomTemplate))
        {
            validation.AddError("自定义终端模式必须提供 CustomTemplate 模板。");
            return string.Empty;
        }

        var argsBuilder = new StringBuilder();
        foreach (var arg in tool.Args)
        {
            if (argsBuilder.Length > 0)
            {
                argsBuilder.Append(' ');
            }
            argsBuilder.Append(CommandEscaper.EscapeCmdArgument(arg));
        }

        string result = tool.CustomTemplate
            .Replace("%EXE%", $"\"{tool.Executable.Trim()}\"")
            .Replace("%ARGS%", argsBuilder.ToString());

        // 如果用户模板未写引号包裹的 %V，做智能包装
        if (result.Contains("%V") && !result.Contains("\"%V\""))
        {
            result = result.Replace("%V", "\"%V\"");
        }

        return result;
    }
}
