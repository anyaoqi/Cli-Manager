using System.Diagnostics;

namespace RightMenu.Core.Launching;

/// <summary>Windows Terminal 探测与启动计划构建（规划 02 §5）。</summary>
public static class TerminalDetector
{
    /// <summary>查找 wt.exe；未安装返回 null。</summary>
    public static string? FindWindowsTerminal()
    {
        // 标准安装位置：执行别名
        var aliasPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Microsoft\WindowsApps\wt.exe");
        if (File.Exists(aliasPath))
        {
            return aliasPath;
        }

        // PATH 兜底
        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var dir in pathDirs)
        {
            var candidate = Path.Combine(dir.Trim(), "wt.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}

/// <summary>
/// 构建启动 Runner 的 ProcessStartInfo（纯构建不执行，便于测试）。
/// 工作目录通过结构化参数传递，绝不使用 cd /d 字符串模板（规划 02 §5）。
/// </summary>
public static class LaunchPlanner
{
    /// <summary>
    /// wt.exe 把未转义的 ';' 当作窗格分隔符，路径中的 ';' 必须转义（规划 02 §10）。
    /// </summary>
    public static string EscapeForWt(string value) => value.Replace(";", "\\;");

    public static ProcessStartInfo BuildRunnerStart(
        string runnerPath,
        string locationId,
        string entryKeyName,
        string workingDirectory,
        TerminalKind terminal,
        string? wtPath)
    {
        var useWt = terminal switch
        {
            TerminalKind.WindowsTerminal => true,
            TerminalKind.Console => false,
            _ => wtPath is not null,
        };

        if (useWt && wtPath is not null)
        {
            var startInfo = new ProcessStartInfo { FileName = wtPath, UseShellExecute = false };
            startInfo.ArgumentList.Add("-d");
            startInfo.ArgumentList.Add(EscapeForWt(workingDirectory));
            startInfo.ArgumentList.Add(EscapeForWt(runnerPath));
            startInfo.ArgumentList.Add("--location");
            startInfo.ArgumentList.Add(locationId);
            startInfo.ArgumentList.Add("--entry");
            startInfo.ArgumentList.Add(entryKeyName);
            startInfo.ArgumentList.Add("--cwd");
            startInfo.ArgumentList.Add(EscapeForWt(workingDirectory));
            return startInfo;
        }

        // 经典控制台兜底：broker 是 WinExe，Runner 作为控制台程序自动获得新控制台
        var fallback = new ProcessStartInfo { FileName = runnerPath, UseShellExecute = false };
        fallback.ArgumentList.Add("--location");
        fallback.ArgumentList.Add(locationId);
        fallback.ArgumentList.Add("--entry");
        fallback.ArgumentList.Add(entryKeyName);
        fallback.ArgumentList.Add("--cwd");
        fallback.ArgumentList.Add(workingDirectory);
        return fallback;
    }
}
