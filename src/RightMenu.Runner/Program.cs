using System.Diagnostics;
using RightMenu.Core.Launching;
using RightMenu.Core.Registry;

namespace RightMenu.Runner;

/// <summary>
/// 终端内执行器：在指定工作目录中启动 CLI 并等待退出（规划 02 §5）。
/// 两种模式：
///   受管模式：--location &lt;id&gt; --entry &lt;键名&gt; --cwd &lt;目录&gt;（从注册表读取 LaunchSpec）
///   显式模式：--cwd &lt;目录&gt; --exec &lt;文件&gt; [--arg &lt;参数&gt;]... [--keep-open]（测试用）
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"RightMenu.Runner 错误: {ex.Message}");
            Pause("发生错误");
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        string? cwd = null;
        string? exec = null;
        string? locationId = null;
        string? entryKey = null;
        var cliArgs = new List<string>();
        var keepOpen = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--cwd" when i + 1 < args.Length:
                    cwd = args[++i];
                    break;
                case "--exec" when i + 1 < args.Length:
                    exec = args[++i];
                    break;
                case "--arg" when i + 1 < args.Length:
                    cliArgs.Add(args[++i]);
                    break;
                case "--location" when i + 1 < args.Length:
                    locationId = args[++i];
                    break;
                case "--entry" when i + 1 < args.Length:
                    entryKey = args[++i];
                    break;
                case "--keep-open":
                    keepOpen = true;
                    break;
                default:
                    Console.Error.WriteLine($"未知参数: {args[i]}");
                    return 2;
            }
        }

        // 受管模式：从注册表读取 LaunchSpec
        if (locationId is not null && entryKey is not null)
        {
            var location = ContextLocation.FindById(locationId);
            if (location is null)
            {
                Console.Error.WriteLine($"未知位置: {locationId}");
                Pause("配置错误");
                return 4;
            }

            var (spec, error) = new ManagedEntryService(new WindowsRegistryAccessor())
                .ReadLaunchSpec(location, entryKey);
            if (spec is null)
            {
                Console.Error.WriteLine($"读取启动配置失败: {error}");
                Pause("配置错误");
                return 4;
            }

            exec = spec.ExecutablePath;
            cliArgs = [.. spec.Arguments];
            keepOpen = spec.KeepOpenAfterExit;
        }

        if (cwd is null || exec is null)
        {
            Console.Error.WriteLine(
                "用法: RightMenu.Runner --location <id> --entry <键名> --cwd <目录>");
            Console.Error.WriteLine(
                "     RightMenu.Runner --cwd <目录> --exec <文件> [--arg <参数>]... [--keep-open]");
            return 2;
        }

        if (!Directory.Exists(cwd))
        {
            Console.Error.WriteLine($"工作目录不存在: {cwd}");
            Pause("目录错误");
            return 3;
        }

        if (!File.Exists(exec))
        {
            Console.Error.WriteLine($"可执行文件不存在: {exec}");
            Console.Error.WriteLine("该 CLI 可能已被移动或卸载，可在“右键菜单管理”中修复。");
            Pause("文件缺失");
            return 3;
        }

        var exitCode = CmdScriptLauncher.Launch(exec, cliArgs, cwd);

        if (keepOpen)
        {
            Pause($"进程已退出（退出码 {exitCode}）");
        }

        return exitCode;
    }

    private static void Pause(string reason)
    {
        // 交互失败时避免 wt 标签页瞬间关闭（规划 02 §5）
        if (!Console.IsInputRedirected)
        {
            Console.WriteLine();
            Console.WriteLine($"{reason}，按任意键关闭……");
            Console.ReadKey(intercept: true);
        }
    }
}

/// <summary>
/// 集中的进程启动实现（规划 02 §5）：
/// .exe 直接启动，参数走 ArgumentList；
/// .cmd/.bat 经 cmd.exe /d /s /c 调用——ArgumentList 只给含空格参数加引号，
/// 会让 &amp;()； 等字符被 cmd 解析，因此必须手工构建全引号命令行。
/// </summary>
internal static class CmdScriptLauncher
{
    internal static int Launch(string exec, IReadOnlyList<string> cliArgs, string cwd)
    {
        var ext = Path.GetExtension(exec).ToLowerInvariant();
        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = cwd,
            UseShellExecute = false,
        };

        if (ext is ".cmd" or ".bat")
        {
            foreach (var arg in cliArgs)
            {
                if (arg.Contains('"'))
                {
                    // cmd 批处理无法可靠传递内嵌引号；拒绝而非静默损坏
                    throw new ArgumentException($"脚本参数不支持内嵌引号: {arg}");
                }
            }

            startInfo.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            var commandLine = new System.Text.StringBuilder("/d /s /c \"");
            commandLine.Append('"').Append(exec).Append('"');
            foreach (var arg in cliArgs)
            {
                commandLine.Append(' ').Append('"').Append(arg).Append('"');
            }

            commandLine.Append('"');
            startInfo.Arguments = commandLine.ToString();
        }
        else
        {
            startInfo.FileName = exec;
            foreach (var arg in cliArgs)
            {
                startInfo.ArgumentList.Add(arg);
            }
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"无法启动进程: {exec}");
        process.WaitForExit();
        return process.ExitCode;
    }
}
