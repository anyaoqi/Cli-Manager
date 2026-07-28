using System.Diagnostics;
using System.IO;
using RightMenu.Core.Launching;
using RightMenu.Core.Registry;

namespace RightMenu.App;

/// <summary>
/// 显式入口：在创建任何 WPF 类型之前识别 --launch，broker 模式不加载 WPF Application（规划 02 §5）。
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--launch")
        {
            return LaunchBroker.Run(args);
        }

        if (args.Length > 0 && args[0] == "--elevated-delete")
        {
            return ElevatedDeleteHelper.Run(args);
        }

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}

/// <summary>
/// 提权 helper（规划 02 §7.4）：仅删除白名单 ContextLocation 下的单个 HKLM 动词键。
/// 不常驻、不接受任意路径；由主进程以 runas 调起。
/// 用法: RightMenu.App.exe --elevated-delete &lt;locationId&gt; &lt;keyName&gt;
/// </summary>
internal static class ElevatedDeleteHelper
{
    internal static int Run(string[] args)
    {
        if (args.Length != 3)
        {
            return 2;
        }

        var location = ContextLocation.FindById(args[1]);
        var keyName = args[2];

        // 严格验证：位置必须在白名单，键名必须是单段名称（无路径分隔符）
        if (location is null || string.IsNullOrWhiteSpace(keyName) ||
            keyName.Contains('\\') || keyName.Contains('/'))
        {
            return 2;
        }

        try
        {
            Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree(
                $@"Software\Classes\{location.RelativeShellPath}\{keyName}", throwOnMissingSubKey: false);
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 5;
        }
        catch (System.Security.SecurityException)
        {
            return 5;
        }
    }

    /// <summary>主进程调用：以 UAC 调起自身删除单个 HKLM 键；用户拒绝或失败返回 false。</summary>
    internal static bool InvokeElevated(ContextLocation location, string keyName)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath ?? "RightMenu.App.exe",
                UseShellExecute = true,
                Verb = "runas",
                Arguments = $"--elevated-delete {location.Id} \"{keyName}\"",
            };
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // 用户在 UAC 弹窗点了“否”
            return false;
        }
    }
}

/// <summary>
/// 无窗口 broker：读取受管项 LaunchSpec → 选择终端 → 在目标目录启动 Runner（规划 02 §5）。
/// 用法: RightMenu.App.exe --launch &lt;location&gt; &lt;entry-key&gt; &lt;target-path&gt;
/// </summary>
internal static class LaunchBroker
{
    internal static int Run(string[] args)
    {
        try
        {
            if (args.Length < 4)
            {
                Log($"参数不足: [{string.Join(" | ", args)}]");
                return 2;
            }

            var locationId = args[1];
            var entryKey = args[2];
            var targetPath = args[3];

            var location = ContextLocation.FindById(locationId);
            if (location is null)
            {
                Log($"未知位置: {locationId}");
                return 4;
            }

            // 目标目录必须是存在的绝对路径（规划 02 §5）
            if (!Path.IsPathRooted(targetPath) || !Directory.Exists(targetPath))
            {
                Log($"目标目录无效: {targetPath}");
                return 3;
            }

            var (spec, error) = new ManagedEntryService(new WindowsRegistryAccessor())
                .ReadLaunchSpec(location, entryKey);
            if (spec is null)
            {
                Log($"读取 LaunchSpec 失败 [{entryKey}]: {error}");
                return 4;
            }

            var runnerPath = Path.Combine(AppContext.BaseDirectory, "RightMenu.Runner.exe");
            if (!File.Exists(runnerPath))
            {
                Log($"Runner 不存在: {runnerPath}");
                return 5;
            }

            var wtPath = spec.Terminal == TerminalKind.Console ? null : TerminalDetector.FindWindowsTerminal();
            var startInfo = LaunchPlanner.BuildRunnerStart(
                runnerPath, locationId, entryKey, targetPath, spec.Terminal, wtPath);

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                Log($"启动失败: {startInfo.FileName}");
                return 5;
            }

            LogTiming(args);
            return 0;
        }
        catch (Exception ex)
        {
            Log($"broker 异常: {ex}");
            return 1;
        }
    }

    private static void LogTiming(string[] args)
    {
        var startupMs = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
        Log($"broker 完成 {startupMs:F0}ms args=[{string.Join(" | ", args)}]");
    }

    private static void Log(string message)
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RightMenu", "logs");
            Directory.CreateDirectory(logDir);
            File.AppendAllText(
                Path.Combine(logDir, "broker.log"),
                $"{DateTime.Now:O} {message}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // 日志失败不阻塞启动链
        }
    }
}
